﻿using Files.Enums;
using Files.Filesystem;
using Files.Helpers;
using Microsoft.Toolkit.Uwp;
using Windows.Foundation.Collections;
using Windows.UI.Xaml;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using System.Collections.Generic;
using System.Linq;
using Windows.ApplicationModel.AppService;
using Files.Views;
using System;
using Windows.UI.Core;
using Windows.System;
using Files.Dialogs;
using System.Diagnostics;
using Windows.Foundation;
using Windows.UI.Xaml.Input;
using Files.ViewModels;

namespace Files.Interacts
{
    /// <summary>
    /// This class provides default implementation for BaseLayout commands.
    /// This class can be also inherited from and functions overridden to provide custom functionality
    /// </summary>
    public class BaseLayoutCommandImplementationModel : IBaseLayoutCommandImplementationModel
    {
        #region Singleton

        private NamedPipeAsAppServiceConnection ServiceConnection => associatedInstance?.ServiceConnection;

        private IBaseLayout SlimContentPage => associatedInstance?.SlimContentPage;

        private IFilesystemHelpers FilesystemHelpers => associatedInstance?.FilesystemHelpers;

        #endregion Singleton

        #region Private Members

        private readonly IShellPage associatedInstance;

        #endregion Private Members

        #region Constructor

        public BaseLayoutCommandImplementationModel(IShellPage associatedInstance)
        {
            this.associatedInstance = associatedInstance;
        }

        #endregion Constructor

        #region IDisposable

        public void Dispose()
        {
            //associatedInstance = null;
        }

        #endregion IDisposable

        #region Command Implementation

        public virtual void RenameItem(RoutedEventArgs e)
        {
            SlimContentPage.StartRenameItem();
        }

        public virtual async void CreateShortcut(RoutedEventArgs e)
        {
            foreach (ListedItem selectedItem in SlimContentPage.SelectedItems)
            {
                if (ServiceConnection != null)
                {
                    var value = new ValueSet()
                    {
                        { "Arguments", "FileOperation" },
                        { "fileop", "CreateLink" },
                        { "targetpath", selectedItem.ItemPath },
                        { "arguments", "" },
                        { "workingdir", "" },
                        { "runasadmin", false },
                        {
                            "filepath",
                            System.IO.Path.Combine(associatedInstance.FilesystemViewModel.WorkingDirectory,
                                string.Format("ShortcutCreateNewSuffix".GetLocalized(), selectedItem.ItemName) + ".lnk")
                        }
                    };
                    await ServiceConnection.SendMessageAsync(value);
                }
            }
        }

        public virtual void SetAsLockscreenBackgroundItem(RoutedEventArgs e)
        {
            WallpaperHelpers.SetAsBackground(WallpaperType.LockScreen, SlimContentPage.SelectedItem.ItemPath, associatedInstance);
        }

        public virtual void SetAsDesktopBackgroundItem(RoutedEventArgs e)
        {
            WallpaperHelpers.SetAsBackground(WallpaperType.Desktop, SlimContentPage.SelectedItem.ItemPath, associatedInstance);
        }

        public virtual async void RunAsAdmin(RoutedEventArgs e)
        {
            if (ServiceConnection != null)
            {
                await ServiceConnection.SendMessageAsync(new ValueSet()
                {
                    { "Arguments", "InvokeVerb" },
                    { "FilePath", SlimContentPage.SelectedItem.ItemPath },
                    { "Verb", "runas" }
                });
            }
        }

        public virtual async void RunAsAnotherUser(RoutedEventArgs e)
        {
            if (ServiceConnection != null)
            {
                await ServiceConnection.SendMessageAsync(new ValueSet()
                {
                    { "Arguments", "InvokeVerb" },
                    { "FilePath", SlimContentPage.SelectedItem.ItemPath },
                    { "Verb", "runasuser" }
                });
            }
        }

        public virtual void SidebarPinItem(RoutedEventArgs e)
        {
            SidebarHelpers.PinItems(SlimContentPage.SelectedItems);
        }

        public virtual void SidebarUnpinItem(RoutedEventArgs e)
        {
            SidebarHelpers.UnpinItems(SlimContentPage.SelectedItems);
        }

        public virtual void OpenItem(RoutedEventArgs e)
        {
            NavigationHelpers.OpenSelectedItems(associatedInstance, false);
        }

        public virtual void UnpinDirectoryFromSidebar(RoutedEventArgs e)
        {
            App.SidebarPinnedController.Model.RemoveItem(associatedInstance.FilesystemViewModel.WorkingDirectory);
        }

        public virtual void EmptyRecycleBin(RoutedEventArgs e)
        {
            RecycleBinHelpers.EmptyRecycleBin(associatedInstance);
        }

        public virtual void QuickLook(RoutedEventArgs e)
        {
            QuickLookHelpers.ToggleQuickLook(associatedInstance);
        }

        public virtual void CopyItem(RoutedEventArgs e)
        {
            UIFilesystemHelpers.CopyItem(associatedInstance);
        }

        public virtual void CutItem(RoutedEventArgs e)
        {
            UIFilesystemHelpers.CutItem(associatedInstance);
        }

        public virtual async void RestoreItem(RoutedEventArgs e)
        {
            if (SlimContentPage.IsItemSelected)
            {
                foreach (ListedItem listedItem in SlimContentPage.SelectedItems)
                {
                    if (listedItem is RecycleBinItem binItem)
                    {
                        FilesystemItemType itemType = binItem.PrimaryItemAttribute == StorageItemTypes.Folder ? FilesystemItemType.Directory : FilesystemItemType.File;
                        await FilesystemHelpers.RestoreFromTrashAsync(StorageItemHelpers.FromPathAndType(
                            (listedItem as RecycleBinItem).ItemPath,
                            itemType), (listedItem as RecycleBinItem).ItemOriginalPath, true);
                    }
                }
            }
        }

        public virtual async void DeleteItem(RoutedEventArgs e)
        {
            await FilesystemHelpers.DeleteItemsAsync(
                SlimContentPage.SelectedItems.Select((item) => StorageItemHelpers.FromPathAndType(
                    item.ItemPath,
                    item.PrimaryItemAttribute == StorageItemTypes.File ? FilesystemItemType.File : FilesystemItemType.Directory)).ToList(),
                true, false, true);
        }

        public virtual void ShowFolderProperties(RoutedEventArgs e)
        {
            FilePropertiesHelpers.ShowProperties(associatedInstance);
        }

        public virtual void ShowProperties(RoutedEventArgs e)
        {
            FilePropertiesHelpers.ShowProperties(associatedInstance);
        }

        public virtual async void OpenFileLocation(RoutedEventArgs e)
        {
            ShortcutItem item = SlimContentPage.SelectedItem as ShortcutItem;

            if (string.IsNullOrWhiteSpace(item?.TargetPath))
            {
                return;
            }

            // Check if destination path exists
            string folderPath = System.IO.Path.GetDirectoryName(item.TargetPath);
            FilesystemResult<StorageFolderWithPath> destFolder = await associatedInstance.FilesystemViewModel.GetFolderWithPathFromPathAsync(folderPath);

            if (destFolder)
            {
                associatedInstance.NavigateWithArguments(associatedInstance.InstanceViewModel.FolderSettings.GetLayoutType(folderPath), new NavigationArguments()
                {
                    NavPathParam = folderPath,
                    AssociatedTabInstance = associatedInstance
                });
            }
            else if (destFolder == FileSystemStatusCode.NotFound)
            {
                await DialogDisplayHelper.ShowDialogAsync("FileNotFoundDialog/Title".GetLocalized(), "FileNotFoundDialog/Text".GetLocalized());
            }
            else
            {
                await DialogDisplayHelper.ShowDialogAsync("InvalidItemDialogTitle".GetLocalized(),
                    string.Format("InvalidItemDialogContent".GetLocalized(), Environment.NewLine, destFolder.ErrorCode.ToString()));
            }
        }

        public virtual void OpenItemWithApplicationPicker(RoutedEventArgs e)
        {
            NavigationHelpers.OpenSelectedItems(associatedInstance, true);
        }

        public virtual async void OpenDirectoryInNewTab(RoutedEventArgs e)
        {
            foreach (ListedItem listedItem in SlimContentPage.SelectedItems)
            {
                await CoreWindow.GetForCurrentThread().Dispatcher.RunAsync(CoreDispatcherPriority.Low, async () =>
                {
                    await MainPageViewModel.AddNewTabByPathAsync(typeof(PaneHolderPage), (listedItem as ShortcutItem)?.TargetPath ?? listedItem.ItemPath);
                });
            }
        }

        public virtual void OpenDirectoryInNewPane(RoutedEventArgs e)
        {
            ListedItem listedItem = SlimContentPage.SelectedItems.FirstOrDefault();
            if (listedItem != null)
            {
                associatedInstance.PaneHolder?.OpenPathInNewPane((listedItem as ShortcutItem)?.TargetPath ?? listedItem.ItemPath);
            }
        }

        public virtual async void OpenInNewWindowItem(RoutedEventArgs e)
        {
            List<ListedItem> items = SlimContentPage.SelectedItems;
            foreach (ListedItem listedItem in items)
            {
                var selectedItemPath = (listedItem as ShortcutItem)?.TargetPath ?? listedItem.ItemPath;
                var folderUri = new Uri($"files-uwp:?folder={@selectedItemPath}");
                await Launcher.LaunchUriAsync(folderUri);
            }
        }

        public virtual void CreateNewFolder(RoutedEventArgs e)
        {
            UIFilesystemHelpers.CreateFileFromDialogResultType(AddItemType.Folder, null, associatedInstance);
        }

        public virtual void CreateNewFile(RoutedEventArgs e)
        {
            UIFilesystemHelpers.CreateFileFromDialogResultType(AddItemType.File, null, associatedInstance);
        }

        public virtual async void PasteItemsFromClipboard(RoutedEventArgs e)
        {
            await UIFilesystemHelpers.PasteItemAsync(associatedInstance.FilesystemViewModel.WorkingDirectory, associatedInstance);
        }

        public virtual void CopyPathOfSelectedItem(RoutedEventArgs e)
        {
            try
            {
                if (SlimContentPage != null)
                {
                    DataPackage data = new DataPackage();
                    data.SetText(SlimContentPage.SelectedItem.ItemPath);
                    Clipboard.SetContent(data);
                    Clipboard.Flush();
                }
            }
            catch (Exception ex)
            {
                Debugger.Break();
            }
        }

        public virtual void OpenDirectoryInDefaultTerminal(RoutedEventArgs e)
        {
            NavigationHelpers.OpenDirectoryInTerminal(associatedInstance.FilesystemViewModel.WorkingDirectory, associatedInstance);
        }

        public virtual void ShareItem(RoutedEventArgs e)
        {
            DataTransferManager manager = DataTransferManager.GetForCurrentView();
            manager.DataRequested += new TypedEventHandler<DataTransferManager, DataRequestedEventArgs>(Manager_DataRequested);
            DataTransferManager.ShowShareUI();

            async void Manager_DataRequested(DataTransferManager sender, DataRequestedEventArgs args)
            {
                DataRequestDeferral dataRequestDeferral = args.Request.GetDeferral();
                List<IStorageItem> items = new List<IStorageItem>();
                DataRequest dataRequest = args.Request;

                /*dataRequest.Data.Properties.Title = "Data Shared From Files";
                dataRequest.Data.Properties.Description = "The items you selected will be shared";*/

                foreach (ListedItem item in SlimContentPage.SelectedItems)
                {
                    if (item.IsShortcutItem)
                    {
                        if (item.IsLinkItem)
                        {
                            dataRequest.Data.Properties.Title = string.Format("ShareDialogTitle".GetLocalized(), items.First().Name);
                            dataRequest.Data.Properties.Description = "ShareDialogSingleItemDescription".GetLocalized();
                            dataRequest.Data.SetWebLink(new Uri(((ShortcutItem)item).TargetPath));
                            dataRequestDeferral.Complete();
                            return;
                        }
                    }
                    else if (item.PrimaryItemAttribute == StorageItemTypes.Folder)
                    {
                        if (await StorageItemHelpers.ToStorageItem<StorageFolder>(item.ItemPath, associatedInstance) is StorageFolder folder)
                        {
                            items.Add(folder);
                        }
                    }
                    else
                    {
                        if (await StorageItemHelpers.ToStorageItem<StorageFile>(item.ItemPath, associatedInstance) is StorageFile file)
                        {
                            items.Add(file);
                        }
                    }
                }

                if (items.Count == 1)
                {
                    dataRequest.Data.Properties.Title = string.Format("ShareDialogTitle".GetLocalized(), items.First().Name);
                    dataRequest.Data.Properties.Description = "ShareDialogSingleItemDescription".GetLocalized();
                }
                else if (items.Count == 0)
                {
                    dataRequest.FailWithDisplayText("ShareDialogFailMessage".GetLocalized());
                    dataRequestDeferral.Complete();
                    return;
                }
                else
                {
                    dataRequest.Data.Properties.Title = string.Format("ShareDialogTitleMultipleItems".GetLocalized(), items.Count,
                        "ItemsCount.Text".GetLocalized());
                    dataRequest.Data.Properties.Description = "ShareDialogMultipleItemsDescription".GetLocalized();
                }

                dataRequest.Data.SetStorageItems(items);
                dataRequestDeferral.Complete();

                // TODO: Unhook the event somewhere
            }
        }

        public virtual void PinDirectoryToSidebar(RoutedEventArgs e)
        {
            App.SidebarPinnedController.Model.AddItem(associatedInstance.FilesystemViewModel.WorkingDirectory);
        }

        public virtual void ItemPointerPressed(PointerRoutedEventArgs e)
        {
            if (e.GetCurrentPoint(null).Properties.IsMiddleButtonPressed)
            {
                if ((e.OriginalSource as FrameworkElement)?.DataContext is ListedItem Item && Item.PrimaryItemAttribute == StorageItemTypes.Folder)
                {
                    if (Item.IsShortcutItem)
                    {
                        NavigationHelpers.OpenPathInNewTab(((e.OriginalSource as FrameworkElement)?.DataContext as ShortcutItem)?.TargetPath ?? Item.ItemPath);
                    }
                    else
                    {
                        NavigationHelpers.OpenPathInNewTab(Item.ItemPath);
                    }
                }
            }
        }

        #endregion Command Implementation
    }
}