﻿using Files.Enums;
using Files.Helpers;
using Microsoft.Toolkit.Mvvm.ComponentModel;
using Microsoft.Toolkit.Uwp;
using System;
using System.Collections.Generic;
using Windows.UI.Xaml;

namespace Files.ViewModels.SettingsViewModels
{
    public class AppearanceViewModel : ObservableObject
    {
        private int selectedThemeIndex = (int)Enum.Parse(typeof(ElementTheme), ThemeHelper.RootTheme.ToString());
        private int selectedDateFormatIndex = (int)Enum.Parse(typeof(TimeStyle), App.AppSettings.DisplayedTimeStyle.ToString());
        private bool isAcrylicDisabled = App.AppSettings.IsAcrylicDisabled;
        private bool moveOverflowMenuItemsToSubMenu = App.AppSettings.MoveOverflowMenuItemsToSubMenu;
        private bool areRightClickContentMenuAnimationsEnabled = App.AppSettings.AreRightClickContentMenuAnimationsEnabled;
        private string selectedThemeName = App.AppSettings.PathToThemeFile;
        private bool showRestartControl = false;

        public AppearanceViewModel()
        {
            Themes = new List<string>()
            {
                "SystemTheme".GetLocalized(),
                "LightTheme".GetLocalized(),
                "DarkTheme".GetLocalized()
            };

            DateFormats = new List<string>
            {
                "ApplicationTimeStye".GetLocalized(),
                "SystemTimeStye".GetLocalized()
            };
        }

        public List<string> Themes { get; set; }
        public List<string> ColorSchemes => App.ExternalResourcesHelper.Themes;

        public int SelectedThemeIndex
        {
            get => selectedThemeIndex;
            set
            {
                if (SetProperty(ref selectedThemeIndex, value))
                {
                    ThemeHelper.RootTheme = (ElementTheme)value;
                }
            }
        }

        public List<string> DateFormats { get; set; }

        public int SelectedDateFormatIndex
        {
            get
            {
                return selectedDateFormatIndex;
            }
            set
            {
                if (SetProperty(ref selectedDateFormatIndex, value))
                {
                    App.AppSettings.DisplayedTimeStyle = (TimeStyle)value;
                }
            }
        }

        public bool IsAcrylicDisabled
        {
            get
            {
                return isAcrylicDisabled;
            }
            set
            {
                if (SetProperty(ref isAcrylicDisabled, value))
                {
                    App.AppSettings.IsAcrylicDisabled = value;
                }
            }
        }

        public bool MoveOverflowMenuItemsToSubMenu
        {
            get
            {
                return moveOverflowMenuItemsToSubMenu;
            }
            set
            {
                if (SetProperty(ref moveOverflowMenuItemsToSubMenu, value))
                {
                    App.AppSettings.MoveOverflowMenuItemsToSubMenu = value;
                }
            }
        }

        public bool AreRightClickContentMenuAnimationsEnabled
        {
            get
            {
                return areRightClickContentMenuAnimationsEnabled;
            }
            set
            {
                if (SetProperty(ref areRightClickContentMenuAnimationsEnabled, value))
                {
                    App.AppSettings.AreRightClickContentMenuAnimationsEnabled = value;
                }
            }
        }
        public string SelectedThemeName
        {
            get
            {
                return selectedThemeName;
            }
            set
            {
                if (SetProperty(ref selectedThemeName, value))
                {
                    App.AppSettings.PathToThemeFile = selectedThemeName;
                    ShowRestartControl = true;
                }
            }
        }

        public bool ShowRestartControl
        {
            get => showRestartControl;
            set => SetProperty(ref showRestartControl, value);
        }
    }
}