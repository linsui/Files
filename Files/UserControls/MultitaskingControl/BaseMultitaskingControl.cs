﻿using Files.ViewModels;
using Files.Views;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace Files.UserControls.MultitaskingControl
{
    public class BaseMultitaskingControl : UserControl, IMultitaskingControl, INotifyPropertyChanged
    {
        protected ITabItemContent CurrentSelectedAppInstance;

        public const string TabDropHandledIdentifier = "FilesTabViewItemDropHandled";

        public const string TabPathIdentifier = "FilesTabViewItemPath";

        public event EventHandler<CurrentInstanceChangedEventArgs> CurrentInstanceChanged;

        public event PropertyChangedEventHandler PropertyChanged;

        public void SelectionChanged() => TabStrip_SelectionChanged(null, null);

        public BaseMultitaskingControl()
        {
            Loaded += MultitaskingControl_Loaded;
        }

        public ObservableCollection<TabItem> Items => MainPageViewModel.AppInstances;

        public List<ITabItem> RecentlyClosedTabs { get; private set; } = new List<ITabItem>();

        private void MultitaskingControl_CurrentInstanceChanged(object sender, CurrentInstanceChangedEventArgs e)
        {
            foreach (ITabItemContent instance in e.PageInstances)
            {
                if (instance != null)
                {
                    instance.IsCurrentInstance = instance == e.CurrentInstance;
                }
            }
        }

        protected void TabStrip_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (App.InteractionViewModel.TabStripSelectedIndex >= 0 && App.InteractionViewModel.TabStripSelectedIndex < Items.Count)
            {
                CurrentSelectedAppInstance = GetCurrentSelectedTabInstance();

                if (CurrentSelectedAppInstance != null)
                {
                    CurrentInstanceChanged?.Invoke(this, new CurrentInstanceChangedEventArgs()
                    {
                        CurrentInstance = CurrentSelectedAppInstance,
                        PageInstances = GetAllTabInstances()
                    });
                }
            }
        }

        protected void TabStrip_TabCloseRequested(TabView sender, TabViewTabCloseRequestedEventArgs args)
        {
            CloseTab(args.Item as TabItem);
        }

        protected async void TabView_AddTabButtonClick(TabView sender, object args)
        {
            await MainPageViewModel.AddNewTabAsync();
        }

        public void MultitaskingControl_Loaded(object sender, RoutedEventArgs e)
        {
            CurrentInstanceChanged += MultitaskingControl_CurrentInstanceChanged;
        }

        public ITabItemContent GetCurrentSelectedTabInstance()
        {
            return MainPageViewModel.AppInstances[App.InteractionViewModel.TabStripSelectedIndex].Control?.TabItemContent;
        }

        public List<ITabItemContent> GetAllTabInstances()
        {
            return MainPageViewModel.AppInstances.Select(x => x.Control?.TabItemContent).ToList();
        }

        public void CloseTab(TabItem tabItem)
        {
            if (Items.Count == 1)
            {
                App.CloseApp();
            }
            else if (Items.Count > 1)
            {
                Items.Remove(tabItem);
                tabItem?.Unload(); // Dispose and save tab arguments
                RecentlyClosedTabs.Add((ITabItem)tabItem);
            }
        }

        protected void OnPropertyChanged([CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}