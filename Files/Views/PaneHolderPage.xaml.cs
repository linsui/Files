﻿using Files.Filesystem;
using Files.Helpers;
using Files.Interacts;
using Files.UserControls;
using Files.UserControls.MultitaskingControl;
using Files.ViewModels;
using Microsoft.Toolkit.Uwp;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using Windows.ApplicationModel.AppService;
using Windows.ApplicationModel.Core;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation.Collections;
using Windows.Storage;
using Windows.System;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Navigation;

namespace Files.Views
{
    public sealed partial class PaneHolderPage : Page, IPaneHolder, ITabItemContent, INotifyPropertyChanged
    {
        public SettingsViewModel AppSettings => App.AppSettings;
        public IFilesystemHelpers FilesystemHelpers => ActivePane?.FilesystemHelpers;
        public ICommand EmptyRecycleBinCommand { get; private set; }
        private AdaptiveSidebarViewModel adaptiveSidebarViewModel => ((Window.Current.Content as Frame).Content as MainPage).SidebarAdaptiveViewModel;

        public PaneHolderPage()
        {
            this.InitializeComponent();

            this.ActivePane = PaneLeft;
            this.IsRightPaneVisible = IsMultiPaneEnabled && AppSettings.AlwaysOpenDualPaneInNewTab;
            App.AppSettings.PropertyChanged += AppSettings_PropertyChanged;
            adaptiveSidebarViewModel.WindowCompactStateChanged += AdaptiveSidebarViewModel_WindowCompactStateChanged;
            // TODO: fallback / error when failed to get NavigationViewCompactPaneLength value?
        }

        private void AdaptiveSidebarViewModel_WindowCompactStateChanged(object sender, WindowCompactStateChangedEventArgs e)
        {
            if (e.IsWindowCompact)
            {
                wasRightPaneVisible = isRightPaneVisible;
                IsRightPaneVisible = false;
            }
            else if (wasRightPaneVisible)
            {
                IsRightPaneVisible = true;
                wasRightPaneVisible = false;
            }
            NotifyPropertyChanged(nameof(IsMultiPaneEnabled));
        }

        private bool wasRightPaneVisible;

        public bool IsMultiPaneEnabled
        {
            get => AppSettings.IsDualPaneEnabled && !adaptiveSidebarViewModel.IsWindowCompactSize;
        }

        private string navParamsLeft;

        public string NavParamsLeft
        {
            get => navParamsLeft;
            set
            {
                if (navParamsLeft != value)
                {
                    navParamsLeft = value;
                    NotifyPropertyChanged(nameof(NavParamsLeft));
                }
            }
        }

        private string navParamsRight;

        public string NavParamsRight
        {
            get => navParamsRight;
            set
            {
                if (navParamsRight != value)
                {
                    navParamsRight = value;
                    NotifyPropertyChanged(nameof(NavParamsRight));
                }
            }
        }

        private IShellPage activePane;

        public IShellPage ActivePane
        {
            get => activePane;
            set
            {
                if (activePane != value)
                {
                    activePane = value;
                    PaneLeft.IsCurrentInstance = false;
                    if (PaneRight != null)
                    {
                        PaneRight.IsCurrentInstance = false;
                    }
                    if (ActivePane != null)
                    {
                        ActivePane.IsCurrentInstance = isCurrentInstance;
                    }
                    NotifyPropertyChanged(nameof(ActivePane));
                    NotifyPropertyChanged(nameof(IsLeftPaneActive));
                    NotifyPropertyChanged(nameof(IsRightPaneActive));
                    NotifyPropertyChanged(nameof(FilesystemHelpers));
                    UpdateSidebarSelectedItem();
                }
            }
        }

        public bool IsLeftPaneActive => ActivePane == PaneLeft;

        public bool IsRightPaneActive => ActivePane == PaneRight;

        private bool isRightPaneVisible;

        public bool IsRightPaneVisible
        {
            get => isRightPaneVisible;
            set
            {
                if (value != isRightPaneVisible)
                {
                    isRightPaneVisible = value;
                    if (!isRightPaneVisible)
                    {
                        ActivePane = PaneLeft;
                    }
                    Pane_ContentChanged(null, null);
                    NotifyPropertyChanged(nameof(IsRightPaneVisible));
                }
            }
        }

        private bool isCurrentInstance;

        public bool IsCurrentInstance
        {
            get => isCurrentInstance;
            set
            {
                isCurrentInstance = value;
                PaneLeft.IsCurrentInstance = false;
                if (PaneRight != null)
                {
                    PaneRight.IsCurrentInstance = false;
                }
                if (ActivePane != null)
                {
                    ActivePane.IsCurrentInstance = value;
                }
            }
        }

        public event EventHandler<TabItemArguments> ContentChanged;

        public event PropertyChangedEventHandler PropertyChanged;

        private void NotifyPropertyChanged([CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected override void OnNavigatedTo(NavigationEventArgs eventArgs)
        {
            base.OnNavigatedTo(eventArgs);

            if (eventArgs.Parameter is string navPath)
            {
                NavParamsLeft = navPath;
                NavParamsRight = "NewTab".GetLocalized();
            }
            if (eventArgs.Parameter is PaneNavigationArguments paneArgs)
            {
                NavParamsLeft = paneArgs.LeftPaneNavPathParam;
                NavParamsRight = paneArgs.RightPaneNavPathParam;
                IsRightPaneVisible = IsMultiPaneEnabled && paneArgs.RightPaneNavPathParam != null;
            }
        }

        private void AppSettings_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(App.AppSettings.IsDualPaneEnabled):
                    NotifyPropertyChanged(nameof(IsMultiPaneEnabled));
                    break;
            }
        }

        public void Dispose()
        {
            App.AppSettings.PropertyChanged -= AppSettings_PropertyChanged;
            adaptiveSidebarViewModel.WindowCompactStateChanged -= AdaptiveSidebarViewModel_WindowCompactStateChanged;
            PaneLeft?.Dispose();
            PaneRight?.Dispose();
            if (SidebarControl != null)
            {
                SidebarControl.SidebarItemInvoked -= SidebarControl_SidebarItemInvoked;
                SidebarControl.SidebarItemPropertiesInvoked -= SidebarControl_SidebarItemPropertiesInvoked;
                SidebarControl.SidebarItemDropped -= SidebarControl_SidebarItemDropped;
                SidebarControl.RecycleBinItemRightTapped -= SidebarControl_RecycleBinItemRightTapped;
                SidebarControl.SidebarItemNewPaneInvoked -= SidebarControl_SidebarItemNewPaneInvoked;
            }
        }

        private void SidebarControl_Loaded(object sender, RoutedEventArgs e)
        {
            SidebarControl.SidebarItemInvoked += SidebarControl_SidebarItemInvoked;
            SidebarControl.SidebarItemPropertiesInvoked += SidebarControl_SidebarItemPropertiesInvoked;
            SidebarControl.SidebarItemDropped += SidebarControl_SidebarItemDropped;
            SidebarControl.RecycleBinItemRightTapped += SidebarControl_RecycleBinItemRightTapped;
            SidebarControl.SidebarItemNewPaneInvoked += SidebarControl_SidebarItemNewPaneInvoked;
            SidebarControl.Loaded -= SidebarControl_Loaded;
        }

        private void PaneLeft_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            ActivePane = PaneLeft;
            e.Handled = false;
        }

        private void PaneRight_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            ActivePane = PaneRight;
            e.Handled = false;
        }

        private void PaneResizer_ManipulationCompleted(object sender, ManipulationCompletedRoutedEventArgs e)
        {
            if (PaneRight != null && PaneRight.ActualWidth <= 300)
            {
                IsRightPaneVisible = false;
            }
        }

        private TabItemArguments tabItemArguments;

        public TabItemArguments TabItemArguments
        {
            get => tabItemArguments;
            set
            {
                if (tabItemArguments != value)
                {
                    tabItemArguments = value;
                    ContentChanged?.Invoke(this, value);
                }
            }
        }

        private void Pane_ContentChanged(object sender, TabItemArguments e)
        {
            TabItemArguments = new TabItemArguments()
            {
                InitialPageType = typeof(PaneHolderPage),
                NavigationArg = new PaneNavigationArguments()
                {
                    LeftPaneNavPathParam = PaneLeft.TabItemArguments?.NavigationArg as string,
                    RightPaneNavPathParam = IsRightPaneVisible ? PaneRight?.TabItemArguments?.NavigationArg as string : null
                }
            };
            UpdateSidebarSelectedItem();
        }

        public void UpdateSidebarSelectedItem()
        {
            var value = IsLeftPaneActive ?
                PaneLeft.TabItemArguments?.NavigationArg as string :
                PaneRight.TabItemArguments?.NavigationArg as string;
            if (string.IsNullOrEmpty(value))
            {
                return;
            }

            INavigationControlItem item = null;
            List<INavigationControlItem> sidebarItems = UserControls.SidebarControl.SideBarItems
                .Where(x => !string.IsNullOrWhiteSpace(x.Path))
                .Concat(UserControls.SidebarControl.SideBarItems.Where(x => (x as LocationItem)?.ChildItems != null).SelectMany(x => (x as LocationItem).ChildItems).Where(x => !string.IsNullOrWhiteSpace(x.Path)))
                .ToList();

            item = sidebarItems.FirstOrDefault(x => x.Path.Equals(value, StringComparison.OrdinalIgnoreCase));
            if (item == null)
            {
                item = sidebarItems.FirstOrDefault(x => x.Path.Equals(value + "\\", StringComparison.OrdinalIgnoreCase));
            }
            if (item == null)
            {
                item = sidebarItems.FirstOrDefault(x => value.StartsWith(x.Path, StringComparison.OrdinalIgnoreCase));
            }
            if (item == null)
            {
                item = sidebarItems.FirstOrDefault(x => x.Path.Equals(Path.GetPathRoot(value), StringComparison.OrdinalIgnoreCase));
            }
            if (item == null)
            {
                if (value == "NewTab".GetLocalized())
                {
                    item = sidebarItems.FirstOrDefault(x => x.Path.Equals("Home"));
                }
            }

            if (SidebarSelectedItem != item)
            {
                SidebarSelectedItem = item;
            }
        }

        public DataPackageOperation TabItemDragOver(object sender, DragEventArgs e)
        {
            return ActivePane?.TabItemDragOver(sender, e) ?? DataPackageOperation.None;
        }

        public async Task<DataPackageOperation> TabItemDrop(object sender, DragEventArgs e)
        {
            if (ActivePane != null)
            {
                return await ActivePane.TabItemDrop(sender, e);
            }
            return DataPackageOperation.None;
        }

        public void OpenPathInNewPane(string path)
        {
            IsRightPaneVisible = true;
            NavParamsRight = path;
        }

        private void KeyboardAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
        {
            args.Handled = true;
            var ctrl = args.KeyboardAccelerator.Modifiers.HasFlag(VirtualKeyModifiers.Control);
            var shift = args.KeyboardAccelerator.Modifiers.HasFlag(VirtualKeyModifiers.Shift);
            var menu = args.KeyboardAccelerator.Modifiers.HasFlag(VirtualKeyModifiers.Menu);

            switch (c: ctrl, s: shift, m: menu, k: args.KeyboardAccelerator.Key)
            {
                case (true, true, false, VirtualKey.Left): // ctrl + shift + "<-" select left pane
                    if (AppSettings.IsDualPaneEnabled)
                    {
                        ActivePane = PaneLeft;
                    }
                    break;

                case (true, true, false, VirtualKey.Right): // ctrl + shift + "->" select right pane
                    if (AppSettings.IsDualPaneEnabled)
                    {
                        if (string.IsNullOrEmpty(NavParamsRight))
                        {
                            NavParamsRight = "NewTab".GetLocalized();
                        }
                        IsRightPaneVisible = true;
                        ActivePane = PaneRight;
                    }
                    break;

                case (true, true, false, VirtualKey.W): // ctrl + shift + "W" close right pane
                    IsRightPaneVisible = false;
                    break;

                case (false, true, true, VirtualKey.Add): // alt + shift + "+" open pane
                    if (AppSettings.IsDualPaneEnabled)
                    {
                        if (string.IsNullOrEmpty(NavParamsRight))
                        {
                            NavParamsRight = "NewTab".GetLocalized();
                        }
                        IsRightPaneVisible = true;
                    }
                    break;
            }
        }

        public INavigationControlItem SidebarSelectedItem
        {
            get => SidebarControl?.SelectedSidebarItem;
            set
            {
                if (SidebarControl != null)
                {
                    SidebarControl.SelectedSidebarItem = value;
                }
            }
        }

        private async void SidebarControl_RecycleBinItemRightTapped(object sender, EventArgs e)
        {
            var recycleBinHasItems = false;
            var connection = await AppServiceConnectionHelper.Instance;
            if (connection != null)
            {
                var value = new ValueSet
                {
                    { "Arguments", "RecycleBin" },
                    { "action", "Query" }
                };
                var (status, response) = await connection.SendMessageForResponseAsync(value);
                if (status == AppServiceResponseStatus.Success && response.TryGetValue("NumItems", out var numItems))
                {
                    recycleBinHasItems = (long)numItems > 0;
                }
            }
            SidebarControl.RecycleBinHasItems = recycleBinHasItems;
        }

        private async void SidebarControl_SidebarItemDropped(object sender, SidebarItemDroppedEventArgs e)
        {
            await FilesystemHelpers.PerformOperationTypeAsync(e.AcceptedOperation, e.Package, e.ItemPath, true);
        }

        private async void SidebarControl_SidebarItemPropertiesInvoked(object sender, SidebarItemPropertiesInvokedEventArgs e)
        {
            if (e.InvokedItemDataContext is DriveItem)
            {
                await FilePropertiesHelpers.OpenPropertiesWindowAsync(e.InvokedItemDataContext, ActivePane);
            }
            else if (e.InvokedItemDataContext is LocationItem)
            {
                ListedItem listedItem = new ListedItem(null)
                {
                    ItemPath = (e.InvokedItemDataContext as LocationItem).Path,
                    ItemName = (e.InvokedItemDataContext as LocationItem).Text,
                    PrimaryItemAttribute = StorageItemTypes.Folder,
                    ItemType = "FileFolderListItem".GetLocalized(),
                    LoadFolderGlyph = true
                };
                await FilePropertiesHelpers.OpenPropertiesWindowAsync(listedItem, ActivePane);
            }
        }

        private void SidebarControl_SidebarItemNewPaneInvoked(object sender, SidebarItemNewPaneInvokedEventArgs e)
        {
            if (e.InvokedItemDataContext is INavigationControlItem navItem)
            {
                OpenPathInNewPane(navItem.Path);
            }
        }

        private void SidebarControl_SidebarItemInvoked(object sender, SidebarItemInvokedEventArgs e)
        {
            var invokedItemContainer = e.InvokedItemContainer;

            // All items must have DataContext except Settings item
            if (invokedItemContainer.DataContext is MainPageViewModel)
            {
                Frame rootFrame = Window.Current.Content as Frame;
                rootFrame.Navigate(typeof(Settings));

                return;
            }

            string navigationPath; // path to navigate
            Type sourcePageType = null; // type of page to navigate

            switch ((invokedItemContainer.DataContext as INavigationControlItem).ItemType)
            {
                case NavigationControlItemType.Location:
                    {
                        var ItemPath = (invokedItemContainer.DataContext as INavigationControlItem).Path; // Get the path of the invoked item

                        if (string.IsNullOrEmpty(ItemPath)) // Section item
                        {
                            navigationPath = invokedItemContainer.Tag?.ToString();
                        }
                        else if (ItemPath.Equals("Home", StringComparison.OrdinalIgnoreCase)) // Home item
                        {
                            if (ItemPath.Equals(SidebarSelectedItem?.Path, StringComparison.OrdinalIgnoreCase))
                            {
                                return; // return if already selected
                            }

                            navigationPath = "NewTab".GetLocalized();
                            sourcePageType = typeof(YourHome);
                        }
                        else // Any other item
                        {
                            navigationPath = invokedItemContainer.Tag?.ToString();
                        }

                        break;
                    }
                default:
                    {
                        navigationPath = invokedItemContainer.Tag?.ToString();
                        break;
                    }
            }

            ActivePane?.NavigateToPath(navigationPath, sourcePageType);
        }
    }

    public class PaneNavigationArguments
    {
        public string LeftPaneNavPathParam { get; set; } = null;
        public string RightPaneNavPathParam { get; set; } = null;
    }
}