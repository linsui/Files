﻿using Files.Filesystem;
using Files.Helpers;
using Files.Interacts;
using Files.UserControls;
using Files.UserControls.MultitaskingControl;
using Files.ViewModels;
using Files.Views;
using System;

namespace Files
{
    public interface IShellPage : ITabItemContent, IMultiPaneInfo, IDisposable
    {
        IStatusCenterActions StatusCenterActions { get; }

        //Interaction InteractionOperations { get; }

        ItemViewModel FilesystemViewModel { get; }

        CurrentInstanceViewModel InstanceViewModel { get; }

        NamedPipeAsAppServiceConnection ServiceConnection { get; }

        IBaseLayout SlimContentPage { get; }

        Type CurrentPageType { get; }

        IFilesystemHelpers FilesystemHelpers { get; }

        INavigationToolbar NavigationToolbar { get; }

        bool CanNavigateBackward { get; }

        bool CanNavigateForward { get; }

        void Refresh_Click();

        void UpdatePathUIToWorkingDirectory(string newWorkingDir, string singleItemOverride = null);

        void NavigateToPath(string navigationPath, Type sourcePageType, NavigationArguments navArgs = null);

        void NavigateWithArguments(Type sourcePageType, NavigationArguments navArgs);

        void RemoveLastPageFromBackStack();
    }

    public interface IPaneHolder : IDisposable
    {
        public void OpenPathInNewPane(string path);
    }

    public interface IMultiPaneInfo
    {
        public bool IsPageMainPane { get; } // The instance is the left (or only) pane
        public bool IsMultiPaneActive { get; } // Another pane is shown
        public bool IsMultiPaneEnabled { get; } // Multi pane is enabled
        public IPaneHolder PaneHolder { get; }
    }
}