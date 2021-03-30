﻿namespace Files
{
    public static class Constants
    {
        public static class Browser
        {
            public static class GridViewBrowser
            {
                public const int GridViewIncrement = 20;

                public const int GridViewSizeMax = 300; // Max achievable ctrl + scroll, not a default layout size.

                public const int GridViewSizeLarge = 220;

                public const int GridViewSizeMedium = 160;

                public const int GridViewSizeSmall = 100;
            }

            public static class GenericFileBrowser
            {
                public const int DetailsViewSize = 24;
            }
        }

        public static class Widgets
        {
            public static class Bundles
            {
                public const int MaxAmountOfItemsPerBundle = 8;
            }
        }

        public static class LocalSettings
        {
            public const string DateTimeFormat = "datetimeformat";

            public const string Theme = "theme";

            public const string SettingsFolderName = "settings";

            public const string BundlesSettingsFileName = "bundles.json";
        }

        public static class PreviewPane
        {
            /// <summary>
            /// The maximum number of characters that should be loaded into the preview.
            /// Enforcing this limit ensures that attempting to open an absurdly large file will not cause Files to freeze.
            /// </summary>
            public const int TextCharacterLimit = 50000;

            /// <summary>
            /// The maximum number of pages loaded into the PDF preview.
            /// </summary>
            public const int PDFPageLimit = 10;

            /// <summary>
            /// The maximum file size, in bytes, that will attempted to be loaded as text if the extension is unknown.
            /// </summary>
            public const long TryLoadAsTextSizeLimit = 1000000;

            /// <summary>
            /// The number of thumbnails that will be shown for FolderPreviews
            /// </summary>
            public const int FolderPreviewThumbnailCount = 10;
        }

        public static class ResourceFilePaths
        {
            /// <summary>
            /// The path to the json file containing a list of file properties to be loaded in the properties window details page.
            /// </summary>
            public const string DetailsPagePropertiesJsonPath = @"ms-appx:///Resources/PropertiesInformation.json";

            /// <summary>
            /// The path to the json file containing a list of file properties to be loaded in the preview pane.
            /// </summary>
            public const string PreviewPaneDetailsPropertiesJsonPath = @"ms-appx:///Resources/PreviewPanePropertiesInformation.json";
        }
    }
}