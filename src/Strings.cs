namespace QuickCut
{
    // Every piece of text the user sees lives here.
    // To add a language later: add a method like LoadDutch() that reassigns these fields,
    // and call it at startup based on CultureInfo.CurrentUICulture or a setting.
    static class Str
    {
        // Tray
        public static string MenuOpenRing = "Open ring";
        public static string MenuSettings = "Settings...";
        public static string MenuExit = "Exit";
        public static string TrayRunning = "QuickCut is running in the background. Double right-click (or press {0}) to open the ring.";
        public static string ToastStarted = "QuickCut started";
        public static string ToastReady = "Ready in the system tray";
        public static string HotkeyInUse = "The hotkey {0} is already used by another program. Pick a different one in Settings.";
        public static string HotkeyUnavailable = "{0} is already taken by Windows or another program. Try a different combination.";
        public static string SaveFailed = "Saving failed: {0}";
        public static string RunFailed = "Couldn't run \"{0}\":\n{1}";
        public static string CrashNotice = "Something went wrong in QuickCut. It keeps running; the details are in {0}";

        // Actions
        public static string NoProgram = "No app set.";
        public static string NoAddress = "No address set.";
        public static string UnknownMedia = "Unknown media action: {0}";
        public static string InvalidCombo = "Invalid key combination: {0}";
        public static string[] MediaNames = { "Play / Pause", "Previous track", "Next track", "Stop", "Volume up", "Volume down", "Mute" };
        public static string[] MediaShort = { "Play/Pause", "Previous", "Next", "Stop", "Volume +", "Volume -", "Mute" };

        // Volume panel under the ring
        public static string VolumeLevel = "Volume {0}%";
        public static string VolumeMuted = "Muted";
        public static string VolumeUnavailable = "No audio device";
        public static string NothingPlaying = "Nothing playing";

        // Default buttons
        public static string DefaultExplorer = "Explorer";

        // Settings window
        public static string SettingsTitle = "QuickCut settings";
        public static string Saving = "Saving…";
        public static string AllSaved = "✓ All changes saved";
        public static string NotSaved = "Couldn't save your changes";
        public static string[] Tabs = { "Buttons", "Appearance", "General" };
        public static string PreviewHint = "Click a button in the ring to edit it.";
        public static string AddButton = "Add button";
        public static string MoveLeft = "↺  Move";  // ↺
        public static string MoveRight = "Move  ↻"; // ↻
        public static string MoveCounterClockwise = "Move counter-clockwise";
        public static string MoveClockwise = "Move clockwise";

        // Buttons tab
        public static string CenterButton = "Center button";
        public static string ButtonN = "Button {0}";
        public static string Remove = "Remove";
        public static string WhatShouldItDo = "What should it do?";
        public static string[] TypeTiles = { "Open app", "Website", "Media", "Shortcut" };
        public static string AppLabel = "App";
        public static string ChooseApp = "Choose an app";
        public static string PickApp = "Pick app";
        public static string BrowseFile = "Browse for a file";
        public static string Arguments = "Arguments (optional)";
        public static string WebAddress = "Web address";
        public static string WebCue = "youtube.com";
        public static string MediaAction = "Action";
        public static string KeysToSend = "Shortcut to send";
        public static string Label = "Label";
        public static string ButtonColor = "Button color";
        public static string Test = "▶  Test this button";

        // Appearance tab
        public static string Theme = "Theme";
        public static string[] ThemeOptions = { "Windows setting", "Light", "Dark" };
        public static string RingSize = "Ring size";
        public static string[] SizeOptions = { "Small", "Medium", "Large" };
        public static string ButtonColorAll = "Button color (all buttons)";
        public static string HoverColor = "Hover color";
        public static string HoverStyle = "Hover style";
        public static string[] HoverOptions = { "Fill", "Inverted", "Outline", "Grow" };
        public static string HoverHint = "Hover over the ring to try your colors.";
        public static string ResetAppearance = "Reset to defaults";

        // Color choosers and the color picker
        public static string[] ButtonColorModes = { "Automatic", "Choose" };
        public static string[] ItemColorModes = { "Same as all buttons", "Choose" };
        public static string[] HoverColorModes = { "Automatic", "Windows accent", "Choose" };
        public static string[] ButtonPresetNames = { "Black", "Graphite", "Steel", "Slate", "Navy", "Indigo", "Teal", "Forest",
            "Plum", "Wine", "Espresso", "Rust", "Mist", "Sand", "White" };
        public static string[] HoverPresetNames = { "Blue", "Sky", "Cyan", "Teal", "Mint", "Green", "Lime", "Yellow",
            "Amber", "Coral", "Red", "Pink", "Magenta", "Violet", "Purple" };
        public static string MoreColors = "More colors…";
        public static string CustomColor = "Custom {0}";
        public static string PickColorTitle = "Pick a color";
        public static string CurrentColor = "Current";
        public static string NewColor = "New";
        public static string Done = "Done";

        // General tab
        public static string OpeningSection = "Opening the ring";
        public static string OpenDoubleRight = "Double right-click";
        public static string NotFullscreen = "Don't open over fullscreen apps (games)";
        public static string KeyboardShortcut = "Keyboard shortcut";
        public static string Default = "Default";
        public static string Hint = "Hold, move, release to pick. Tap to keep it open: 1-9, 0, -, = for buttons, Space = center, scroll = volume, Esc = close.";
        public static string VolumeSection = "Volume and media";
        public static string ShowMedia = "Show what's playing";
        public static string ShowVolume = "Show volume under the ring";
        public static string VolumeStep = "Volume step";
        public static string[] VolumeStepOptions = { "5%", "10%" };
        public static string OtherSection = "Other";
        public static string StartWithWindows = "Start with Windows";
        public static string StartupBlockedByUser = "Starting with Windows was turned off in Task Manager.\n\n" +
            "To turn it back on, open Task Manager > Startup apps, select QuickCut and choose Enable.";
        public static string LaunchNotice = "Show a notice when QuickCut starts";
        public static string RestoreButtons = "Restore default buttons";
        public static string ReportBug = "Report a bug";
        public static string SuggestFeature = "Suggest a feature";
        public static string RestoreConfirm = "Replace all buttons with the suggested defaults?";

        // Import / export (General tab)
        public static string ImportExport = "Import / export…";
        public static string ExportMenu = "Export settings…";
        public static string ImportMenu = "Import settings…";
        public static string TransferFilter = "QuickCut ring (*.quickcut)|*.quickcut|All files|*.*";
        public static string ExportFileName = "QuickCut buttons";
        public static string Exported = "✓ Exported to {0}";
        public static string ImportConfirm = "Replace your buttons and look with the ones in \"{0}\"?\n\n" +
            "It has {1} buttons and a center button. Your shortcut and other settings stay as they are.";
        public static string ImportMissing = "\n\nNot installed on this PC: {0}. These buttons stay; pick the app again to fix them.";
        public static string ImportNotQuickCut = "This isn't a QuickCut file: it has no buttons in it.";
        public static string ImportTooBig = "This file is too big to be a QuickCut file.";
        public static string ImportFailed = "Couldn't import the file: {0}";
        public static string ExportFailed = "Couldn't export: {0}";

        // App picker and dialogs
        public static string PickAppTitle = "Pick an app";
        public static string SearchApps = "Search apps";
        public static string Suggested = "Suggested";
        public static string AllApps = "All apps";
        public static string Select = "Select";
        public static string Cancel = "Cancel";
        public static string PressKeys = "Click here and press the keys";
        public static string BrowseTitle = "Choose a file";
        public static string BrowseFilter = "Programs and shortcuts|*.exe;*.lnk;*.bat;*.cmd;*.url|All files|*.*";
    }
}
