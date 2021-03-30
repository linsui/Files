using Files.Common;
using Microsoft.Win32;
using Newtonsoft.Json;
using NLog;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Vanara.PInvoke;
using Vanara.Windows.Shell;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation.Collections;
using Windows.Storage;

namespace FilesFullTrust
{
    internal class Program
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

        [STAThread]
        private static void Main(string[] args)
        {
            StorageFolder storageFolder = ApplicationData.Current.LocalFolder;
            LogManager.Configuration = new NLog.Config.XmlLoggingConfiguration(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "NLog.config"));
            LogManager.Configuration.Variables["LogPath"] = storageFolder.Path;

            AppDomain.CurrentDomain.UnhandledException += UnhandledExceptionTrapper;

            if (HandleCommandLineArgs())
            {
                // Handles OpenShellCommandInExplorer
                return;
            }

            try
            {
                // Create handle table to store e.g. context menu references
                handleTable = new Win32API.DisposableDictionary();

                // Create shell COM object and get recycle bin folder
                using var recycler = new ShellFolder(Shell32.KNOWNFOLDERID.FOLDERID_RecycleBinFolder);
                ApplicationData.Current.LocalSettings.Values["RecycleBin_Title"] = recycler.Name;

                // Create filesystem watcher to monitor recycle bin folder(s)
                // SHChangeNotifyRegister only works if recycle bin is open in explorer :(
                binWatchers = new List<FileSystemWatcher>();
                var sid = WindowsIdentity.GetCurrent().User.ToString();
                foreach (var drive in DriveInfo.GetDrives())
                {
                    var recyclePath = Path.Combine(drive.Name, "$Recycle.Bin", sid);
                    if (drive.DriveType == DriveType.Network || !Directory.Exists(recyclePath))
                    {
                        continue;
                    }
                    var watcher = new FileSystemWatcher
                    {
                        Path = recyclePath,
                        Filter = "*.*",
                        NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.DirectoryName
                    };
                    watcher.Created += Watcher_Changed;
                    watcher.Deleted += Watcher_Changed;
                    watcher.EnableRaisingEvents = true;
                    binWatchers.Add(watcher);
                }

                // Preload context menu for better performance
                // We query the context menu for the app's local folder
                var preloadPath = ApplicationData.Current.LocalFolder.Path;
                using var _ = Win32API.ContextMenu.GetContextMenuForFiles(new string[] { preloadPath }, Shell32.CMF.CMF_NORMAL | Shell32.CMF.CMF_SYNCCASCADEMENU, FilterMenuItems(false));

                // Create cancellation token for drop window
                cancellation = new CancellationTokenSource();

                // Connect to app service and wait until the connection gets closed
                appServiceExit = new AutoResetEvent(false);
                InitializeAppServiceConnection();

                // Initialize device watcher
                deviceWatcher = new DeviceWatcher(connection);
                deviceWatcher.Start();

                // Wait until the connection gets closed
                appServiceExit.WaitOne();
            }
            finally
            {
                connection?.Dispose();
                foreach (var watcher in binWatchers)
                {
                    watcher.Dispose();
                }
                handleTable?.Dispose();
                deviceWatcher?.Dispose();
                cancellation?.Cancel();
                cancellation?.Dispose();
                appServiceExit?.Dispose();
            }
        }

        private static void UnhandledExceptionTrapper(object sender, UnhandledExceptionEventArgs e)
        {
            var exception = e.ExceptionObject as Exception;
            Logger.Error(exception, exception.Message);
        }

        private static async void Watcher_Changed(object sender, FileSystemEventArgs e)
        {
            Debug.WriteLine($"Recycle bin event: {e.ChangeType}, {e.FullPath}");
            if (e.Name.StartsWith("$I"))
            {
                // Recycle bin also stores a file starting with $I for each item
                return;
            }
            if (connection?.IsConnected ?? false)
            {
                var response = new ValueSet()
                {
                    { "FileSystem", @"Shell:RecycleBinFolder" },
                    { "Path", e.FullPath },
                    { "Type", e.ChangeType.ToString() }
                };
                if (e.ChangeType == WatcherChangeTypes.Created)
                {
                    using var folderItem = new ShellItem(e.FullPath);
                    var shellFileItem = GetShellFileItem(folderItem);
                    response["Item"] = JsonConvert.SerializeObject(shellFileItem);
                }
                // Send message to UWP app to refresh items
                await Win32API.SendMessageAsync(connection, response);
            }
        }

        private static NamedPipeServerStream connection;
        private static AutoResetEvent appServiceExit;
        private static CancellationTokenSource cancellation;
        private static Win32API.DisposableDictionary handleTable;
        private static IList<FileSystemWatcher> binWatchers;
        private static DeviceWatcher deviceWatcher;

        private static async void InitializeAppServiceConnection()
        {
            connection = new NamedPipeServerStream($@"FilesInteropService_ServerPipe", PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Message, PipeOptions.Asynchronous, 2048, 2048, null, HandleInheritability.None, PipeAccessRights.ChangePermissions);

            PipeSecurity Security = connection.GetAccessControl();
            PipeAccessRule ClientRule = new PipeAccessRule(new SecurityIdentifier("S-1-15-2-1"), PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance, AccessControlType.Allow);
            PipeAccessRule OwnerRule = new PipeAccessRule(WindowsIdentity.GetCurrent().Owner, PipeAccessRights.FullControl, AccessControlType.Allow);
            Security.AddAccessRule(ClientRule);
            Security.AddAccessRule(OwnerRule);
            if (IsAdministrator())
            {
                PipeAccessRule EveryoneRule = new PipeAccessRule(new SecurityIdentifier("S-1-1-0"), PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance, AccessControlType.Allow);
                Security.AddAccessRule(EveryoneRule); // TODO: find the minimum permission to allow connection when admin
            }
            connection.SetAccessControl(Security);

            await connection.WaitForConnectionAsync();

            if (connection.IsConnected)
            {
                var info = (Buffer: new byte[connection.InBufferSize], Message: new StringBuilder());
                BeginRead(info);
            }
        }

        private static void BeginRead((byte[] Buffer, StringBuilder Message) info)
        {
            var isConnected = connection.IsConnected;
            if (isConnected)
            {
                try
                {
                    connection.BeginRead(info.Buffer, 0, info.Buffer.Length, EndReadCallBack, info);
                }
                catch
                {
                    isConnected = false;
                }
            }
            if (!isConnected)
            {
                appServiceExit.Set();
            }
        }

        private static void EndReadCallBack(IAsyncResult result)
        {
            var info = ((byte[] Buffer, StringBuilder Message))result.AsyncState;
            var readBytes = connection.EndRead(result);
            if (readBytes > 0)
            {
                // Get the read bytes and append them
                info.Message.Append(Encoding.UTF8.GetString(info.Buffer, 0, readBytes));

                if (connection.IsMessageComplete) // Message is completed
                {
                    var message = info.Message.ToString().TrimEnd('\0');

                    Connection_RequestReceived(connection, JsonConvert.DeserializeObject<Dictionary<string, object>>(message));

                    // Begin a new reading operation
                    var nextInfo = (Buffer: new byte[connection.InBufferSize], Message: new StringBuilder());
                    BeginRead(nextInfo);

                    return;
                }
            }
            BeginRead(info);
        }

        private static async void Connection_RequestReceived(NamedPipeServerStream conn, Dictionary<string, object> message)
        {
            // Get a deferral because we use an awaitable API below to respond to the message
            // and we don't want this call to get cancelled while we are waiting.
            if (message == null)
            {
                return;
            }

            if (message.ContainsKey("Arguments"))
            {
                // This replaces launching the fulltrust process with arguments
                // Instead a single instance of the process is running
                // Requests from UWP app are sent via AppService connection
                var arguments = (string)message["Arguments"];
                var localSettings = ApplicationData.Current.LocalSettings;
                Logger.Info($"Argument: {arguments}");

                await ParseArgumentsAsync(message, arguments, localSettings);
            }
            else if (message.ContainsKey("Application"))
            {
                var application = (string)message["Application"];
                HandleApplicationLaunch(application, message);
            }
            else if (message.ContainsKey("ApplicationList"))
            {
                var applicationList = JsonConvert.DeserializeObject<IEnumerable<string>>((string)message["ApplicationList"]);
                HandleApplicationsLaunch(applicationList, message);
            }
        }

        private static bool IsAdministrator()
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }

        private static async Task ParseArgumentsAsync(Dictionary<string, object> message, string arguments, ApplicationDataContainer localSettings)
        {
            switch (arguments)
            {
                case "Terminate":
                    // Exit fulltrust process (UWP is closed or suspended)
                    appServiceExit.Set();
                    break;

                case "Elevate":
                    // Relaunch fulltrust process as admin
                    if (!IsAdministrator())
                    {
                        try
                        {
                            using (Process elevatedProcess = new Process())
                            {
                                elevatedProcess.StartInfo.Verb = "runas";
                                elevatedProcess.StartInfo.UseShellExecute = true;
                                elevatedProcess.StartInfo.FileName = Process.GetCurrentProcess().MainModule.FileName;
                                elevatedProcess.StartInfo.Arguments = "elevate";
                                elevatedProcess.Start();
                            }
                            await Win32API.SendMessageAsync(connection, new ValueSet() { { "Success", 0 } }, message.Get("RequestID", (string)null));
                            appServiceExit.Set();
                        }
                        catch (Win32Exception)
                        {
                            // If user cancels UAC
                            await Win32API.SendMessageAsync(connection, new ValueSet() { { "Success", 1 } }, message.Get("RequestID", (string)null));
                        }
                    }
                    else
                    {
                        await Win32API.SendMessageAsync(connection, new ValueSet() { { "Success", -1 } }, message.Get("RequestID", (string)null));
                    }
                    break;

                case "RecycleBin":
                    var binAction = (string)message["action"];
                    await ParseRecycleBinActionAsync(message, binAction);
                    break;

                case "DetectQuickLook":
                    // Check QuickLook Availability
                    var available = QuickLook.CheckQuickLookAvailability();
                    await Win32API.SendMessageAsync(connection, new ValueSet() { { "IsAvailable", available } }, message.Get("RequestID", (string)null));
                    break;

                case "ToggleQuickLook":
                    var path = (string)message["path"];
                    QuickLook.ToggleQuickLook(path);
                    break;

                case "LoadContextMenu":
                    var contextMenuResponse = new ValueSet();
                    var loadThreadWithMessageQueue = new Win32API.ThreadWithMessageQueue<Dictionary<string, object>>(HandleMenuMessage);
                    var cMenuLoad = await loadThreadWithMessageQueue.PostMessageAsync<Win32API.ContextMenu>(message);
                    contextMenuResponse.Add("Handle", handleTable.AddValue(loadThreadWithMessageQueue));
                    contextMenuResponse.Add("ContextMenu", JsonConvert.SerializeObject(cMenuLoad));
                    var serializedCm = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(contextMenuResponse));
                    await Win32API.SendMessageAsync(connection, contextMenuResponse, message.Get("RequestID", (string)null));
                    break;

                case "ExecAndCloseContextMenu":
                    var menuKey = (string)message["Handle"];
                    var execThreadWithMessageQueue = handleTable.GetValue<Win32API.ThreadWithMessageQueue<Dictionary<string, object>>>(menuKey);
                    if (execThreadWithMessageQueue != null)
                    {
                        await execThreadWithMessageQueue.PostMessage(message);
                    }
                    // The following line is needed to cleanup resources when menu is closed.
                    // Unfortunately if you uncomment it some menu items will randomly stop working.
                    // Resource cleanup is currently done on app closing,
                    // if we find a solution for the issue above, we should cleanup as soon as a menu is closed.
                    //handleTable.RemoveValue(menuKey);
                    break;

                case "InvokeVerb":
                    var filePath = (string)message["FilePath"];
                    var split = filePath.Split('|').Where(x => !string.IsNullOrWhiteSpace(x));
                    using (var cMenu = Win32API.ContextMenu.GetContextMenuForFiles(split.ToArray(), Shell32.CMF.CMF_DEFAULTONLY))
                    {
                        cMenu?.InvokeVerb((string)message["Verb"]);
                    }
                    break;

                case "Bitlocker":
                    var bitlockerAction = (string)message["action"];
                    if (bitlockerAction == "Unlock")
                    {
                        var drive = (string)message["drive"];
                        var password = (string)message["password"];
                        Win32API.UnlockBitlockerDrive(drive, password);
                        await Win32API.SendMessageAsync(connection, new ValueSet() { { "Bitlocker", "Unlock" } }, message.Get("RequestID", (string)null));
                    }
                    break;

                case "SetVolumeLabel":
                    var driveName = (string)message["drivename"];
                    var newLabel = (string)message["newlabel"];
                    Win32API.SetVolumeLabel(driveName, newLabel);
                    break;

                case "FileOperation":
                    await ParseFileOperationAsync(message);
                    break;

                case "GetIconOverlay":
                    var fileIconPath = (string)message["filePath"];
                    var thumbnailSize = (int)(long)message["thumbnailSize"];
                    var iconOverlay = Win32API.StartSTATask(() => Win32API.GetFileIconAndOverlay(fileIconPath, thumbnailSize)).Result;
                    await Win32API.SendMessageAsync(connection, new ValueSet()
                    {
                        { "Icon", iconOverlay.icon },
                        { "Overlay", iconOverlay.overlay },
                        { "HasCustomIcon", iconOverlay.isCustom }
                    }, message.Get("RequestID", (string)null));
                    break;

                case "NetworkDriveOperation":
                    await ParseNetworkDriveOperationAsync(message);
                    break;

                case "GetOneDriveAccounts":
                    try
                    {
                        var oneDriveAccountsKey = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\OneDrive\Accounts", false);

                        if (oneDriveAccountsKey == null)
                        {
                            await Win32API.SendMessageAsync(connection, new ValueSet() { { "Count", 0 } }, message.Get("RequestID", (string)null));
                            return;
                        }

                        var oneDriveAccounts = new ValueSet();
                        foreach (var account in oneDriveAccountsKey.GetSubKeyNames())
                        {
                            var accountKeyName = @$"{oneDriveAccountsKey.Name}\{account}";
                            var displayName = (string)Registry.GetValue(accountKeyName, "DisplayName", null);
                            var userFolder = (string)Registry.GetValue(accountKeyName, "UserFolder", null);
                            var accountName = string.IsNullOrWhiteSpace(displayName) ? "OneDrive" : $"OneDrive - {displayName}";
                            if (!string.IsNullOrWhiteSpace(userFolder) && !oneDriveAccounts.ContainsKey(accountName))
                            {
                                oneDriveAccounts.Add(accountName, userFolder);
                            }
                        }
                        oneDriveAccounts.Add("Count", oneDriveAccounts.Count);
                        await Win32API.SendMessageAsync(connection, oneDriveAccounts, message.Get("RequestID", (string)null));
                    }
                    catch
                    {
                        await Win32API.SendMessageAsync(connection, new ValueSet() { { "Count", 0 } }, message.Get("RequestID", (string)null));
                    }
                    break;

                case "GetSharePointSyncLocationsFromOneDrive":
                    try
                    {
                        using var oneDriveAccountsKey = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\OneDrive\Accounts", false);

                        if (oneDriveAccountsKey == null)
                        {
                            await Win32API.SendMessageAsync(connection, new ValueSet() { { "Count", 0 } }, message.Get("RequestID", (string)null));
                            return;
                        }

                        var sharepointAccounts = new ValueSet();

                        foreach (var account in oneDriveAccountsKey.GetSubKeyNames())
                        {
                            var accountKeyName = @$"{oneDriveAccountsKey.Name}\{account}";
                            var displayName = (string)Registry.GetValue(accountKeyName, "DisplayName", null);
                            var userFolderToExcludeFromResults = (string)Registry.GetValue(accountKeyName, "UserFolder", null);
                            var accountName = string.IsNullOrWhiteSpace(displayName) ? "SharePoint" : $"SharePoint - {displayName}";

                            var sharePointSyncFolders = new List<string>();
                            var mountPointKeyName = @$"SOFTWARE\Microsoft\OneDrive\Accounts\{account}\ScopeIdToMountPointPathCache";
                            using (var mountPointsKey = Registry.CurrentUser.OpenSubKey(mountPointKeyName))
                            {
                                if (mountPointsKey == null)
                                {
                                    continue;
                                }

                                var valueNames = mountPointsKey.GetValueNames();
                                foreach (var valueName in valueNames)
                                {
                                    var value = (string)Registry.GetValue(@$"HKEY_CURRENT_USER\{mountPointKeyName}", valueName, null);
                                    if (!string.Equals(value, userFolderToExcludeFromResults, StringComparison.OrdinalIgnoreCase))
                                    {
                                        sharePointSyncFolders.Add(value);
                                    }
                                }
                            }

                            foreach (var sharePointSyncFolder in sharePointSyncFolders.OrderBy(o => o))
                            {
                                var parentFolder = Directory.GetParent(sharePointSyncFolder)?.FullName ?? string.Empty;
                                if (!sharepointAccounts.Any(acc => string.Equals(acc.Key, accountName, StringComparison.OrdinalIgnoreCase)) && !string.IsNullOrWhiteSpace(parentFolder))
                                {
                                    sharepointAccounts.Add(accountName, parentFolder);
                                }
                            }
                        }

                        sharepointAccounts.Add("Count", sharepointAccounts.Count);
                        await Win32API.SendMessageAsync(connection, sharepointAccounts, message.Get("RequestID", (string)null));
                    }
                    catch
                    {
                        await Win32API.SendMessageAsync(connection, new ValueSet() { { "Count", 0 } }, message.Get("RequestID", (string)null));
                    }
                    break;

                case "ShellFolder":
                    // Enumerate shell folder contents and send response to UWP
                    var folderPath = (string)message["folder"];
                    var responseEnum = new ValueSet();
                    var folderContentsList = await Win32API.StartSTATask(() =>
                    {
                        var flc = new List<ShellFileItem>();
                        try
                        {
                            using (var shellFolder = new ShellFolder(folderPath))
                            {
                                foreach (var folderItem in shellFolder)
                                {
                                    try
                                    {
                                        var shellFileItem = GetShellFileItem(folderItem);
                                        flc.Add(shellFileItem);
                                    }
                                    catch (FileNotFoundException)
                                    {
                                        // Happens if files are being deleted
                                    }
                                    finally
                                    {
                                        folderItem.Dispose();
                                    }
                                }
                            }
                        }
                        catch
                        {
                        }
                        return flc;
                    });
                    responseEnum.Add("Enumerate", JsonConvert.SerializeObject(folderContentsList));
                    await Win32API.SendMessageAsync(connection, responseEnum, message.Get("RequestID", (string)null));
                    break;

                default:
                    if (message.ContainsKey("Application"))
                    {
                        var application = (string)message["Application"];
                        HandleApplicationLaunch(application, message);
                    }
                    else if (message.ContainsKey("ApplicationList"))
                    {
                        var applicationList = JsonConvert.DeserializeObject<IEnumerable<string>>((string)message["ApplicationList"]);
                        HandleApplicationsLaunch(applicationList, message);
                    }
                    break;
            }
        }

        private static async Task ParseNetworkDriveOperationAsync(Dictionary<string, object> message)
        {
            switch (message.Get("netdriveop", ""))
            {
                case "GetNetworkLocations":
                    var networkLocations = await Win32API.StartSTATask(() =>
                    {
                        var netl = new ValueSet();
                        using (var nethood = new ShellFolder(Shell32.KNOWNFOLDERID.FOLDERID_NetHood))
                        {
                            foreach (var link in nethood)
                            {
                                var linkPath = (string)link.Properties["System.Link.TargetParsingPath"];
                                if (linkPath != null)
                                {
                                    netl.Add(link.Name, linkPath);
                                }
                            }
                        }
                        return netl;
                    });
                    networkLocations.Add("Count", networkLocations.Count);
                    await Win32API.SendMessageAsync(connection, networkLocations, message.Get("RequestID", (string)null));
                    break;

                case "OpenMapNetworkDriveDialog":
                    NetworkDrivesAPI.OpenMapNetworkDriveDialog();
                    break;

                case "DisconnectNetworkDrive":
                    var drivePath = (string)message["drive"];
                    NetworkDrivesAPI.DisconnectNetworkDrive(drivePath);
                    break;
            }
        }

        private static object HandleMenuMessage(Dictionary<string, object> message, Win32API.DisposableDictionary table)
        {
            switch (message.Get("Arguments", ""))
            {
                case "LoadContextMenu":
                    var contextMenuResponse = new ValueSet();
                    var filePath = (string)message["FilePath"];
                    var extendedMenu = (bool)message["ExtendedMenu"];
                    var showOpenMenu = (bool)message["ShowOpenMenu"];
                    var split = filePath.Split('|').Where(x => !string.IsNullOrWhiteSpace(x));
                    var cMenuLoad = Win32API.ContextMenu.GetContextMenuForFiles(split.ToArray(),
                        (extendedMenu ? Shell32.CMF.CMF_EXTENDEDVERBS : Shell32.CMF.CMF_NORMAL) | Shell32.CMF.CMF_SYNCCASCADEMENU, FilterMenuItems(showOpenMenu));
                    table.SetValue("MENU", cMenuLoad);
                    return cMenuLoad;

                case "ExecAndCloseContextMenu":
                    var cMenuExec = table.GetValue<Win32API.ContextMenu>("MENU");
                    if (message.TryGetValue("ItemID", out var menuId))
                    {
                        switch (message.Get("CommandString", (string)null))
                        {
                            case "format":
                                var drivePath = cMenuExec.ItemsPath.First();
                                Win32API.OpenFormatDriveDialog(drivePath);
                                break;

                            default:
                                cMenuExec?.InvokeItem((int)(long)menuId);
                                break;
                        }
                    }
                    // The following line is needed to cleanup resources when menu is closed.
                    // Unfortunately if you uncomment it some menu items will randomly stop working.
                    // Resource cleanup is currently done on app closing,
                    // if we find a solution for the issue above, we should cleanup as soon as a menu is closed.
                    //table.RemoveValue("MENU");
                    return null;

                default:
                    return null;
            }
        }

        private static Func<string, bool> FilterMenuItems(bool showOpenMenu)
        {
            var knownItems = new List<string>()
            {
                "opennew", "openas", "opencontaining", "opennewprocess",
                "runas", "runasuser", "pintohome", "PinToStartScreen",
                "cut", "copy", "paste", "delete", "properties", "link",
                "Windows.ModernShare", "Windows.Share", "setdesktopwallpaper",
                "eject", "rename", "explore",
                Win32API.ExtractStringFromDLL("shell32.dll", 30312), // SendTo menu
                Win32API.ExtractStringFromDLL("shell32.dll", 34593), // Add to collection
            };

            bool filterMenuItemsImpl(string menuItem)
            {
                return string.IsNullOrEmpty(menuItem) ? false : knownItems.Contains(menuItem)
                    || (!showOpenMenu && menuItem.Equals("open", StringComparison.OrdinalIgnoreCase));
            }

            return filterMenuItemsImpl;
        }

        private static async Task ParseFileOperationAsync(Dictionary<string, object> message)
        {
            switch (message.Get("fileop", ""))
            {
                case "Clipboard":
                    await Win32API.StartSTATask(() =>
                    {
                        System.Windows.Forms.Clipboard.Clear();
                        var fileToCopy = (string)message["filepath"];
                        var operation = (DataPackageOperation)(long)message["operation"];
                        var fileList = new System.Collections.Specialized.StringCollection();
                        fileList.AddRange(fileToCopy.Split('|'));
                        if (operation == DataPackageOperation.Copy)
                        {
                            System.Windows.Forms.Clipboard.SetFileDropList(fileList);
                        }
                        else if (operation == DataPackageOperation.Move)
                        {
                            byte[] moveEffect = new byte[] { 2, 0, 0, 0 };
                            MemoryStream dropEffect = new MemoryStream();
                            dropEffect.Write(moveEffect, 0, moveEffect.Length);
                            var data = new System.Windows.Forms.DataObject();
                            data.SetFileDropList(fileList);
                            data.SetData("Preferred DropEffect", dropEffect);
                            System.Windows.Forms.Clipboard.SetDataObject(data, true);
                        }
                        return true;
                    });
                    break;

                case "DragDrop":
                    cancellation.Cancel();
                    cancellation.Dispose();
                    cancellation = new CancellationTokenSource();
                    var dropPath = (string)message["droppath"];
                    var dropText = (string)message["droptext"];
                    var drops = Win32API.StartSTATask<List<string>>(() =>
                    {
                        var form = new DragDropForm(dropPath, dropText, cancellation.Token);
                        System.Windows.Forms.Application.Run(form);
                        return form.DropTargets;
                    });
                    break;

                case "DeleteItem":
                    var fileToDeletePath = (string)message["filepath"];
                    var permanently = (bool)message["permanently"];
                    using (var op = new ShellFileOperations())
                    {
                        op.Options = ShellFileOperations.OperationFlags.NoUI;
                        if (!permanently)
                        {
                            op.Options |= ShellFileOperations.OperationFlags.AllowUndo;
                        }
                        using var shi = new ShellItem(fileToDeletePath);
                        op.QueueDeleteOperation(shi);
                        var deleteTcs = new TaskCompletionSource<bool>();
                        op.PostDeleteItem += (s, e) => deleteTcs.TrySetResult(e.Result.Succeeded);
                        op.PerformOperations();
                        var result = await deleteTcs.Task;
                        await Win32API.SendMessageAsync(connection, new ValueSet() { { "Success", result } }, message.Get("RequestID", (string)null));
                    }
                    break;

                case "ParseLink":
                    var linkPath = (string)message["filepath"];
                    try
                    {
                        if (linkPath.EndsWith(".lnk"))
                        {
                            using var link = new ShellLink(linkPath, LinkResolution.NoUIWithMsgPump, null, TimeSpan.FromMilliseconds(100));
                            await Win32API.SendMessageAsync(connection, new ValueSet()
                            {
                                { "TargetPath", link.TargetPath },
                                { "Arguments", link.Arguments },
                                { "WorkingDirectory", link.WorkingDirectory },
                                { "RunAsAdmin", link.RunAsAdministrator },
                                { "IsFolder", !string.IsNullOrEmpty(link.TargetPath) && link.Target.IsFolder }
                            }, message.Get("RequestID", (string)null));
                        }
                        else if (linkPath.EndsWith(".url"))
                        {
                            var linkUrl = await Win32API.StartSTATask(() =>
                            {
                                var ipf = new Url.IUniformResourceLocator();
                                (ipf as System.Runtime.InteropServices.ComTypes.IPersistFile).Load(linkPath, 0);
                                ipf.GetUrl(out var retVal);
                                return retVal;
                            });
                            await Win32API.SendMessageAsync(connection, new ValueSet()
                            {
                                { "TargetPath", linkUrl },
                                { "Arguments", null },
                                { "WorkingDirectory", null },
                                { "RunAsAdmin", false },
                                { "IsFolder", false }
                            }, message.Get("RequestID", (string)null));
                        }
                    }
                    catch (Exception ex)
                    {
                        // Could not parse shortcut
                        Logger.Warn(ex, ex.Message);
                        await Win32API.SendMessageAsync(connection, new ValueSet()
                            {
                                { "TargetPath", null },
                                { "Arguments", null },
                                { "WorkingDirectory", null },
                                { "RunAsAdmin", false },
                                { "IsFolder", false }
                            }, message.Get("RequestID", (string)null));
                    }
                    break;

                case "CreateLink":
                case "UpdateLink":
                    var linkSavePath = (string)message["filepath"];
                    var targetPath = (string)message["targetpath"];
                    if (linkSavePath.EndsWith(".lnk"))
                    {
                        var arguments = (string)message["arguments"];
                        var workingDirectory = (string)message["workingdir"];
                        var runAsAdmin = (bool)message["runasadmin"];
                        using var newLink = new ShellLink(targetPath, arguments, workingDirectory);
                        newLink.RunAsAdministrator = runAsAdmin;
                        newLink.SaveAs(linkSavePath); // Overwrite if exists
                    }
                    else if (linkSavePath.EndsWith(".url"))
                    {
                        await Win32API.StartSTATask(() =>
                        {
                            var ipf = new Url.IUniformResourceLocator();
                            ipf.SetUrl(targetPath, Url.IURL_SETURL_FLAGS.IURL_SETURL_FL_GUESS_PROTOCOL);
                            (ipf as System.Runtime.InteropServices.ComTypes.IPersistFile).Save(linkSavePath, false); // Overwrite if exists
                            return true;
                        });
                    }
                    break;
            }
        }

        private static async Task ParseRecycleBinActionAsync(Dictionary<string, object> message, string action)
        {
            switch (action)
            {
                case "Empty":
                    // Shell function to empty recyclebin
                    Shell32.SHEmptyRecycleBin(IntPtr.Zero, null, Shell32.SHERB.SHERB_NOCONFIRMATION | Shell32.SHERB.SHERB_NOPROGRESSUI);
                    break;

                case "Query":
                    var responseQuery = new ValueSet();
                    Win32API.SHQUERYRBINFO queryBinInfo = new Win32API.SHQUERYRBINFO();
                    queryBinInfo.cbSize = Marshal.SizeOf(queryBinInfo);
                    var res = Win32API.SHQueryRecycleBin("", ref queryBinInfo);
                    if (res == HRESULT.S_OK)
                    {
                        var numItems = queryBinInfo.i64NumItems;
                        var binSize = queryBinInfo.i64Size;
                        responseQuery.Add("NumItems", numItems);
                        responseQuery.Add("BinSize", binSize);
                        await Win32API.SendMessageAsync(connection, responseQuery, message.Get("RequestID", (string)null));
                    }
                    break;

                default:
                    break;
            }
        }

        private static ShellFileItem GetShellFileItem(ShellItem folderItem)
        {
            bool isFolder = folderItem.IsFolder && Path.GetExtension(folderItem.Name) != ".zip";
            if (folderItem.Properties == null)
            {
                return new ShellFileItem(isFolder, folderItem.FileSystemPath, Path.GetFileName(folderItem.Name), folderItem.Name, DateTime.Now, DateTime.Now, DateTime.Now, null, 0, null);
            }
            folderItem.Properties.TryGetValue<string>(
                Ole32.PROPERTYKEY.System.ParsingPath, out var parsingPath);
            parsingPath ??= folderItem.FileSystemPath; // True path on disk
            folderItem.Properties.TryGetValue<string>(
                Ole32.PROPERTYKEY.System.ItemNameDisplay, out var fileName);
            fileName ??= Path.GetFileName(folderItem.Name); // Original file name
            string filePath = folderItem.Name; // Original file path + name (recycle bin only)
            folderItem.Properties.TryGetValue<System.Runtime.InteropServices.ComTypes.FILETIME?>(
                Ole32.PROPERTYKEY.System.Recycle.DateDeleted, out var fileTime);
            var recycleDate = fileTime?.ToDateTime().ToLocalTime() ?? DateTime.Now; // This is LocalTime
            folderItem.Properties.TryGetValue<System.Runtime.InteropServices.ComTypes.FILETIME?>(
                Ole32.PROPERTYKEY.System.DateModified, out fileTime);
            var modifiedDate = fileTime?.ToDateTime().ToLocalTime() ?? DateTime.Now; // This is LocalTime
            folderItem.Properties.TryGetValue<System.Runtime.InteropServices.ComTypes.FILETIME?>(
                Ole32.PROPERTYKEY.System.DateCreated, out fileTime);
            var createdDate = fileTime?.ToDateTime().ToLocalTime() ?? DateTime.Now; // This is LocalTime
            string fileSize = folderItem.Properties.TryGetValue<ulong?>(
                Ole32.PROPERTYKEY.System.Size, out var fileSizeBytes) ?
                folderItem.Properties.GetPropertyString(Ole32.PROPERTYKEY.System.Size) : null;
            folderItem.Properties.TryGetValue<string>(
                Ole32.PROPERTYKEY.System.ItemTypeText, out var fileType);
            return new ShellFileItem(isFolder, parsingPath, fileName, filePath, recycleDate, modifiedDate, createdDate, fileSize, fileSizeBytes ?? 0, fileType);
        }

        private static void HandleApplicationsLaunch(IEnumerable<string> applications, Dictionary<string, object> message)
        {
            foreach (var application in applications)
            {
                HandleApplicationLaunch(application, message);
            }
        }

        private static async void HandleApplicationLaunch(string application, Dictionary<string, object> message)
        {
            var arguments = message.Get("Arguments", "");
            var workingDirectory = message.Get("WorkingDirectory", "");

            try
            {
                using Process process = new Process();
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.FileName = application;
                // Show window if workingDirectory (opening terminal)
                process.StartInfo.CreateNoWindow = string.IsNullOrEmpty(workingDirectory);
                if (arguments == "runas")
                {
                    process.StartInfo.UseShellExecute = true;
                    process.StartInfo.Verb = "runas";
                    if (Path.GetExtension(application).ToLower() == ".msi")
                    {
                        process.StartInfo.FileName = "msiexec.exe";
                        process.StartInfo.Arguments = $"/a \"{application}\"";
                    }
                }
                else if (arguments == "runasuser")
                {
                    process.StartInfo.UseShellExecute = true;
                    process.StartInfo.Verb = "runasuser";
                    if (Path.GetExtension(application).ToLower() == ".msi")
                    {
                        process.StartInfo.FileName = "msiexec.exe";
                        process.StartInfo.Arguments = $"/i \"{application}\"";
                    }
                }
                else
                {
                    process.StartInfo.Arguments = arguments;
                }
                process.StartInfo.WorkingDirectory = workingDirectory;
                process.Start();
            }
            catch (Win32Exception)
            {
                using Process process = new Process();
                process.StartInfo.UseShellExecute = true;
                process.StartInfo.FileName = application;
                process.StartInfo.CreateNoWindow = true;
                process.StartInfo.Arguments = arguments;
                process.StartInfo.WorkingDirectory = workingDirectory;
                try
                {
                    process.Start();
                }
                catch (Win32Exception)
                {
                    try
                    {
                        await Win32API.StartSTATask(() =>
                        {
                            var split = application.Split('|').Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => GetMtpPath(x));
                            if (split.Count() == 1)
                            {
                                Process.Start(split.First());
                            }
                            else
                            {
                                var groups = split.GroupBy(x => new
                                {
                                    Dir = Path.GetDirectoryName(x),
                                    Prog = Win32API.GetFileAssociationAsync(x).Result ?? Path.GetExtension(x)
                                });
                                foreach (var group in groups)
                                {
                                    if (!group.Any())
                                    {
                                        continue;
                                    }
                                    using var cMenu = Win32API.ContextMenu.GetContextMenuForFiles(group.ToArray(), Shell32.CMF.CMF_DEFAULTONLY);
                                    cMenu?.InvokeVerb(Shell32.CMDSTR_OPEN);
                                }
                            }
                            return true;
                        });
                    }
                    catch (Win32Exception)
                    {
                        // Cannot open file (e.g DLL)
                    }
                    catch (ArgumentException)
                    {
                        // Cannot open file (e.g DLL)
                    }
                }
            }
            catch (InvalidOperationException)
            {
                // Invalid file path
            }
        }

        private static bool HandleCommandLineArgs()
        {
            var localSettings = ApplicationData.Current.LocalSettings;
            var arguments = (string)localSettings.Values["Arguments"];
            if (!string.IsNullOrWhiteSpace(arguments))
            {
                localSettings.Values.Remove("Arguments");

                if (arguments == "ShellCommand")
                {
                    // Kill the process. This is a BRUTAL WAY to kill a process.
#if DEBUG
                    // In debug mode this kills this process too??
#else
                    var pid = (int)localSettings.Values["pid"];
                    Process.GetProcessById(pid).Kill();
#endif

                    using Process process = new Process();
                    process.StartInfo.UseShellExecute = true;
                    process.StartInfo.FileName = "explorer.exe";
                    process.StartInfo.CreateNoWindow = false;
                    process.StartInfo.Arguments = (string)localSettings.Values["ShellCommand"];
                    process.Start();

                    return true;
                }
            }
            return false;
        }

        private static string GetMtpPath(string executable)
        {
            if (executable.StartsWith("\\\\?\\"))
            {
                using var computer = new ShellFolder(Shell32.KNOWNFOLDERID.FOLDERID_ComputerFolder);
                using var device = computer.FirstOrDefault(i => executable.Replace("\\\\?\\", "").StartsWith(i.Name));
                var deviceId = device?.ParsingName;
                var itemPath = Regex.Replace(executable, @"^\\\\\?\\[^\\]*\\?", "");
                return deviceId != null ? Path.Combine(deviceId, itemPath) : executable;
            }
            return executable;
        }
    }
}