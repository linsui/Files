﻿using Files.Enums;
using Files.Filesystem;
using Files.UserControls.Selection;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Windows.System;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Navigation;
using Interaction = Files.Interacts.Interaction;

namespace Files.Views.LayoutModes
{
    public sealed partial class GridViewBrowser : BaseLayout
    {
        public string oldItemName;

        public GridViewBrowser()
        {
            InitializeComponent();
            this.DataContext = this;
            base.BaseLayoutContextFlyout = BaseLayoutContextFlyout;
            base.BaseLayoutItemContextFlyout = BaseLayoutItemContextFlyout;

            var selectionRectangle = RectangleSelection.Create(FileList, SelectionRectangle, FileList_SelectionChanged);
            selectionRectangle.SelectionEnded += SelectionRectangle_SelectionEnded;
        }

        protected override void OnNavigatedTo(NavigationEventArgs eventArgs)
        {
            base.OnNavigatedTo(eventArgs);
            FileList.ItemsSource = ParentShellPageInstance.FilesystemViewModel.FilesAndFolders;
            currentIconSize = GetIconSize();
            FolderSettings.LayoutModeChangeRequested -= FolderSettings_LayoutModeChangeRequested;
            FolderSettings.LayoutModeChangeRequested += FolderSettings_LayoutModeChangeRequested;
            SetItemTemplate(); // Set ItemTemplate
            var parameters = (NavigationArguments)eventArgs.Parameter;
            if (parameters.IsLayoutSwitch)
            {
                ReloadItemIcons();
            }
        }

        protected override void OnNavigatingFrom(NavigatingCancelEventArgs e)
        {
            var selectorItems = new List<SelectorItem>();
            Interaction.FindChildren<SelectorItem>(selectorItems, FileList);
            foreach (SelectorItem gvi in selectorItems)
            {
                base.UninitializeDrag(gvi);
                gvi.PointerPressed -= FileListGridItem_PointerPressed;
            }
            selectorItems.Clear();
            base.OnNavigatingFrom(e);
            FolderSettings.LayoutModeChangeRequested -= FolderSettings_LayoutModeChangeRequested;
            FolderSettings.GridViewSizeChangeRequested -= FolderSettings_GridViewSizeChangeRequested;
            
            FileList.ItemsSource = null;
        }

        private async void SelectionRectangle_SelectionEnded(object sender, EventArgs e)
        {
            await Task.Delay(200);
            FileList.Focus(FocusState.Programmatic);
        }

        public override void FocusFileList()
        {
            FileList.Focus(FocusState.Programmatic);
        }

        private void FolderSettings_LayoutModeChangeRequested(object sender, EventArgs e)
        {
            if (FolderSettings.LayoutMode == FolderLayoutModes.GridView || FolderSettings.LayoutMode == FolderLayoutModes.TilesView)
            {
                SetItemTemplate(); // Set ItemTemplate
                var requestedIconSize = GetIconSize();
                if (requestedIconSize != currentIconSize)
                {
                    currentIconSize = requestedIconSize;
                    ReloadItemIcons();
                }
            }
        }

        private void SetItemTemplate()
        {
            FileList.ItemTemplate = (FolderSettings.LayoutMode == FolderLayoutModes.TilesView) ? TilesBrowserTemplate : GridViewBrowserTemplate; // Choose Template

            // Set GridViewSize event handlers
            if (FolderSettings.LayoutMode == FolderLayoutModes.TilesView)
            {
                FolderSettings.GridViewSizeChangeRequested -= FolderSettings_GridViewSizeChangeRequested;
            }
            else if (FolderSettings.LayoutMode == FolderLayoutModes.GridView)
            {
                FolderSettings.GridViewSizeChangeRequested -= FolderSettings_GridViewSizeChangeRequested;
                FolderSettings.GridViewSizeChangeRequested += FolderSettings_GridViewSizeChangeRequested;
            }
        }

        protected override void AddSelectedItem(ListedItem item)
        {
            FileList.SelectedItems.Add(item);
        }

        protected override IEnumerable GetAllItems()
        {
            return FileList.Items;
        }

        public override void SelectAllItems()
        {
            FileList.SelectAll();
        }

        public override void ClearSelection()
        {
            FileList.SelectedItems.Clear();
        }

        public override void SetDragModeForItems()
        {
            if (!InstanceViewModel.IsPageTypeSearchResults)
            {
                foreach (ListedItem listedItem in FileList.Items)
                {
                    if (FileList.ContainerFromItem(listedItem) is GridViewItem gridViewItem)
                    {
                        gridViewItem.CanDrag = gridViewItem.IsSelected;
                    }
                }
            }
        }

        public override void ScrollIntoView(ListedItem item)
        {
            FileList.ScrollIntoView(item);
        }

        public override void FocusSelectedItems()
        {
            FileList.ScrollIntoView(FileList.Items.Last());
        }

        private void StackPanel_RightTapped(object sender, RightTappedRoutedEventArgs e)
        {
            var parentContainer = Interaction.FindParent<GridViewItem>(e.OriginalSource as DependencyObject);
            if (parentContainer.IsSelected)
            {
                return;
            }
            // The following code is only reachable when a user RightTapped an unselected row
            SetSelectedItemOnUi(FileList.ItemFromContainer(parentContainer) as ListedItem);
        }

        private void GridViewBrowserViewer_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (e.GetCurrentPoint(null).Properties.IsMiddleButtonPressed)
            {
                ParentShellPageInstance.InteractionOperations.ItemPointerPressed(sender, e);
            }
        }

        private void FileList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            SelectedItems = FileList.SelectedItems.Cast<ListedItem>().ToList();
        }

        private ListedItem renamingItem;

        public override void StartRenameItem()
        {
            renamingItem = SelectedItem;
            int extensionLength = renamingItem.FileExtension?.Length ?? 0;
            GridViewItem gridViewItem = FileList.ContainerFromItem(renamingItem) as GridViewItem;
            TextBox textBox = null;

            // Handle layout differences between tiles browser and photo album
            if (FolderSettings.LayoutMode == FolderLayoutModes.GridView)
            {
                Popup popup = (gridViewItem.ContentTemplateRoot as Grid).FindName("EditPopup") as Popup;
                TextBlock textBlock = (gridViewItem.ContentTemplateRoot as Grid).FindName("ItemName") as TextBlock;
                textBox = popup.Child as TextBox;
                textBox.Text = textBlock.Text;
                popup.IsOpen = true;
                oldItemName = textBlock.Text;
            }
            else
            {
                TextBlock textBlock = (gridViewItem.ContentTemplateRoot as Grid).FindName("ItemName") as TextBlock;
                textBox = (gridViewItem.ContentTemplateRoot as Grid).FindName("TileViewTextBoxItemName") as TextBox;
                textBox.Text = textBlock.Text;
                oldItemName = textBlock.Text;
                textBlock.Visibility = Visibility.Collapsed;
                textBox.Visibility = Visibility.Visible;
            }

            textBox.Focus(FocusState.Pointer);
            textBox.LostFocus += RenameTextBox_LostFocus;
            textBox.KeyDown += RenameTextBox_KeyDown;

            int selectedTextLength = SelectedItem.ItemName.Length;
            if (!SelectedItem.IsShortcutItem && App.AppSettings.ShowFileExtensions)
            {
                selectedTextLength -= extensionLength;
            }
            textBox.Select(0, selectedTextLength);
            IsRenamingItem = true;
        }

        private void GridViewTextBoxItemName_TextChanged(object sender, TextChangedEventArgs e)
        {
            var textBox = sender as TextBox;

            if (FilesystemHelpers.ContainsRestrictedCharacters(textBox.Text))
            {
                FileNameTeachingTip.IsOpen = true;
            }
            else
            {
                FileNameTeachingTip.IsOpen = false;
            }
        }

        private void RenameTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == VirtualKey.Escape)
            {
                TextBox textBox = sender as TextBox;
                textBox.LostFocus -= RenameTextBox_LostFocus;
                textBox.Text = oldItemName;
                EndRename(textBox);
                e.Handled = true;
            }
            else if (e.Key == VirtualKey.Enter)
            {
                TextBox textBox = sender as TextBox;
                textBox.LostFocus -= RenameTextBox_LostFocus;
                CommitRename(textBox);
                e.Handled = true;
            }
        }

        private void RenameTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            TextBox textBox = e.OriginalSource as TextBox;
            CommitRename(textBox);
        }

        private async void CommitRename(TextBox textBox)
        {
            EndRename(textBox);
            string newItemName = textBox.Text.Trim().TrimEnd('.');

            bool successful = await ParentShellPageInstance.InteractionOperations.RenameFileItemAsync(renamingItem, oldItemName, newItemName);
            if (!successful)
            {
                renamingItem.ItemName = oldItemName;
            }
        }

        private void EndRename(TextBox textBox)
        {
            if (FolderSettings.LayoutMode == FolderLayoutModes.GridView)
            {
                Popup popup = textBox.Parent as Popup;
                TextBlock textBlock = (popup.Parent as Grid).Children[1] as TextBlock;
                popup.IsOpen = false;
            }
            else
            {
                StackPanel parentPanel = textBox.Parent as StackPanel;
                TextBlock textBlock = parentPanel.Children[0] as TextBlock;
                textBox.Visibility = Visibility.Collapsed;
                textBlock.Visibility = Visibility.Visible;
            }

            textBox.LostFocus -= RenameTextBox_LostFocus;
            textBox.KeyDown -= RenameTextBox_KeyDown;
            FileNameTeachingTip.IsOpen = false;
            IsRenamingItem = false;
        }

        private void FileList_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
        {
            var ctrlPressed = Window.Current.CoreWindow.GetKeyState(VirtualKey.Control).HasFlag(CoreVirtualKeyStates.Down);
            var shiftPressed = Window.Current.CoreWindow.GetKeyState(VirtualKey.Shift).HasFlag(CoreVirtualKeyStates.Down);

            if (e.Key == VirtualKey.Enter && !e.KeyStatus.IsMenuKeyDown)
            {
                if (!IsRenamingItem)
                {
                    ParentShellPageInstance.InteractionOperations.OpenItem_Click(null, null);
                    e.Handled = true;
                }
            }
            else if (e.Key == VirtualKey.Enter && e.KeyStatus.IsMenuKeyDown)
            {
                ParentShellPageInstance.InteractionOperations.ShowPropertiesButton_Click(null, null);
            }
            else if (e.Key == VirtualKey.Space)
            {
                if (!IsRenamingItem && !ParentShellPageInstance.NavigationToolbar.IsEditModeEnabled)
                {
                    if (IsQuickLookEnabled)
                    {
                        ParentShellPageInstance.InteractionOperations.ToggleQuickLook();
                    }
                    e.Handled = true;
                }
            }
            else if (e.KeyStatus.IsMenuKeyDown && (e.Key == VirtualKey.Left || e.Key == VirtualKey.Right || e.Key == VirtualKey.Up))
            {
                // Unfocus the GridView so keyboard shortcut can be handled
                Focus(FocusState.Programmatic);
            }
            else if (ctrlPressed && shiftPressed && (e.Key == VirtualKey.Left || e.Key == VirtualKey.Right || e.Key == VirtualKey.W))
            {
                // Unfocus the ListView so keyboard shortcut can be handled (ctrl + shift + W/"->"/"<-")
                Focus(FocusState.Programmatic);
            }
            else if (e.KeyStatus.IsMenuKeyDown && shiftPressed && e.Key == VirtualKey.Add)
            {
                // Unfocus the ListView so keyboard shortcut can be handled (alt + shift + "+")
                Focus(FocusState.Programmatic);
            }
        }

        protected override void Page_CharacterReceived(CoreWindow sender, CharacterReceivedEventArgs args)
        {
            if (ParentShellPageInstance != null)
            {
                if (ParentShellPageInstance.CurrentPageType == typeof(GridViewBrowser) && !IsRenamingItem)
                {
                    // Don't block the various uses of enter key (key 13)
                    var focusedElement = FocusManager.GetFocusedElement() as FrameworkElement;
                    if (args.KeyCode == 13
                        || focusedElement is Button
                        || focusedElement is TextBox
                        || focusedElement is PasswordBox
                        || Interaction.FindParent<ContentDialog>(focusedElement) != null)
                    {
                        return;
                    }

                    base.Page_CharacterReceived(sender, args);
                    FileList.Focus(FocusState.Keyboard);
                }
            }
        }

        protected override ListedItem GetItemFromElement(object element)
        {
            return (element as GridViewItem).DataContext as ListedItem;
        }

        private void FileListGridItem_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (e.KeyModifiers == VirtualKeyModifiers.Control)
            {
                if ((sender as SelectorItem).IsSelected)
                {
                    (sender as SelectorItem).IsSelected = false;
                    // Prevent issues arising caused by the default handlers attempting to select the item that has just been deselected by ctrl + click
                    e.Handled = true;
                }
            }
            else if (e.GetCurrentPoint(sender as UIElement).Properties.IsLeftButtonPressed)
            {
                if (!(sender as SelectorItem).IsSelected)
                {
                    (sender as SelectorItem).IsSelected = true;
                }
            }
        }

        private uint currentIconSize;

        private uint GetIconSize()
        {
            if (FolderSettings.LayoutMode == FolderLayoutModes.TilesView)
            {
                return Constants.Browser.GridViewBrowser.GridViewSizeSmall; // Small thumbnail
            }
            else if (FolderSettings.GridViewSize <= Constants.Browser.GridViewBrowser.GridViewSizeSmall)
            {
                return Constants.Browser.GridViewBrowser.GridViewSizeSmall; // Small thumbnail
            }
            else if (FolderSettings.GridViewSize <= Constants.Browser.GridViewBrowser.GridViewSizeMedium)
            {
                return Constants.Browser.GridViewBrowser.GridViewSizeMedium; // Medium thumbnail
            }
            else if (FolderSettings.GridViewSize <= Constants.Browser.GridViewBrowser.GridViewSizeLarge)
            {
                return Constants.Browser.GridViewBrowser.GridViewSizeLarge; // Large thumbnail
            }
            else
            {
                return Constants.Browser.GridViewBrowser.GridViewSizeMax; // Extra large thumbnail
            }
        }

        private void FolderSettings_GridViewSizeChangeRequested(object sender, EventArgs e)
        {
            var requestedIconSize = GetIconSize(); // Get new icon size

            // Prevents reloading icons when the icon size hasn't changed
            if (requestedIconSize != currentIconSize)
            {
                currentIconSize = requestedIconSize; // Update icon size before refreshing
                ReloadItemIcons();
            }
        }

        private async void ReloadItemIcons()
        {
            ParentShellPageInstance.FilesystemViewModel.CancelExtendedPropertiesLoading();
            foreach (ListedItem listedItem in ParentShellPageInstance.FilesystemViewModel.FilesAndFolders)
            {
                listedItem.ItemPropertiesInitialized = false;
                if (FileList.ContainerFromItem(listedItem) != null)
                {
                    listedItem.ItemPropertiesInitialized = true;
                    await ParentShellPageInstance.FilesystemViewModel.LoadExtendedItemProperties(listedItem, currentIconSize);
                }
            }
        }

        private async void FileList_ItemClick(object sender, ItemClickEventArgs e)
        {
            var ctrlPressed = Window.Current.CoreWindow.GetKeyState(VirtualKey.Control).HasFlag(CoreVirtualKeyStates.Down);
            var shiftPressed = Window.Current.CoreWindow.GetKeyState(VirtualKey.Shift).HasFlag(CoreVirtualKeyStates.Down);

            // Skip code if the control or shift key is pressed
            if (ctrlPressed || shiftPressed)
            {
                return;
            }

            // Check if the setting to open items with a single click is turned on
            if (AppSettings.OpenItemsWithOneclick)
            {
                await Task.Delay(200); // The delay gives time for the item to be selected
                ParentShellPageInstance.InteractionOperations.OpenItem_Click(null, null);
            }
        }

        private async void FileList_ChoosingItemContainer(ListViewBase sender, ChoosingItemContainerEventArgs args)
        {
            if (args.ItemContainer == null)
            {
                GridViewItem gvi = new GridViewItem();
                args.ItemContainer = gvi;
            }
            args.ItemContainer.DataContext = args.Item;

            if (args.Item is ListedItem item && !item.ItemPropertiesInitialized)
            {
                args.ItemContainer.PointerPressed += FileListGridItem_PointerPressed;
                InitializeDrag(args.ItemContainer);
                args.ItemContainer.CanDrag = args.ItemContainer.IsSelected; // Update CanDrag

                item.ItemPropertiesInitialized = true;
                await ParentShellPageInstance.FilesystemViewModel.LoadExtendedItemProperties(item, currentIconSize);
            }
        }
    }
}