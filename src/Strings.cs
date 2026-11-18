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
        public static string AppLevel = "{0}%";
        public static string AppWaiting = "Waiting for sound";
        public static string AppNotRunning = "Not running";
        public static string NothingPlaying = "Nothing playing";

        // Default buttons
        public static string DefaultExplorer = "Explorer";

        // Settings window
        public static string SettingsTitle = "QuickCut settings";
        public static string Saving = "Saving…";
        public static string AllSaved = "✓ All changes saved";
        public static string NotSaved = "Couldn't save your changes";
        public static string AddButton = "Add button";
        public static string MoveLeft = "↺  Move";  // ↺
        public static string MoveRight = "Move  ↻"; // ↻
        public static string MoveCounterClockwise = "Move counter-clockwise";
        public static string MoveClockwise = "Move clockwise";

        // Buttons tab
        public static string CenterButton = "Center button";
        public static string Remove = "Remove";
        public static string WhatShouldItDo = "What should it do?";
        public static string[] TypeTiles = { "Open app", "Website", "Media", "Shortcut" };
        public static string AppLabel = "App";
        public static string ChooseApp = "Choose an app";
        public static string BrowseFile = "Browse for a file";
        public static string Arguments = "Arguments (optional)";
        public static string WebAddress = "Web address";
        public static string WebCue = "youtube.com";
        public static string MediaAction = "Action";
        public static string KeysToSend = "Shortcut to send";
        public static string Label = "Label";
        public static string ButtonColor = "Button color";

        // Appearance tab
        public static string Theme = "Theme";
        public static string[] ThemeOptions = { "Windows setting", "Light", "Dark" };
        public static string RingSize = "Ring size";
        public static string[] SizeOptions = { "Small", "Medium" };
        public static string ButtonColorAll = "Button color (all buttons)";
        public static string HoverColor = "Hover color";
        public static string HoverStyle = "Hover style";
        public static string[] HoverOptions = { "Fill", "Inverted", "Outline", "Grow" };
        public static string HoverHint = "Hover over the ring to try your colors.";
        public static string ResetAppearance = "Reset to defaults";

        // Color choosers and the color picker
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
        public static string KeyboardShortcut = "Keyboard shortcut";

        // Volume tab
        public static string VolumeSection = "Volume and media";
        public static string VolumeStep = "Volume step";
        public static string[] VolumeStepOptions = { "5%", "10%" };
        public static string[] PanelPositionOptions = { "Below", "Above", "Left", "Right" };
        public static string MixerSection = "Apps in the mixer";
        public static string MixerEmpty = "No apps yet. Add Spotify, Discord, a game, ...";
        public static string MixerNeedsVolume = "Turn on Show volume to see the app rows.";
        public static string MixerPlayingNow = "Playing sound now";
        public static string MixerNothingPlaying = "Nothing else is playing sound";
        public static string MixerInstalled = "Choose from installed apps…";
        public static string ImageUnreadable = "That file isn't an image QuickCut can read. Try a PNG, ICO or JPG.";
        public static string MixerFull = "You can add up to {0} apps.";
        public static string MixerCantMatch = "QuickCut can't tell which program \"{0}\" starts, so it can't find its sound.\n\n" +
            "Start the app and play something, then pick it under \"Playing sound now\".";
        public static string StartWithWindows = "Start with Windows";
        public static string StartupBlockedByUser = "Starting with Windows was turned off in Task Manager.\n\n" +
            "To turn it back on, open Task Manager > Startup apps, select QuickCut and choose Enable.";
        public static string RestoreButtons = "Restore default buttons";
        public static string ReportBug = "Report a bug";
        public static string SuggestFeature = "Suggest a feature";
        public static string RestoreConfirm = "Replace all buttons with the suggested defaults?";

        // Import / export (General tab)
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

        // Settings window: pages, header and preview
        public static string[] PageNames = { "Buttons", "Look", "Volume", "General" };
        public static string[] PageSubs = {
            "Click a button in the ring to edit it",
            "Theme, size and colors of the ring",
            "The panels by the ring and the per-app mixer",
            "How the ring opens, and when QuickCut starts" };
        public static string PreviewChip = "Preview at real size";
        public static string PreviewOneToOne = "1:1";
        public static string StageHintVolume = "Scroll over the ring to try the volume";
        public static string StageHintGeneral = "Press {0} anywhere to open the ring";
        public static string TestShort = "Test";
        public static string Version = "v{0}";

        // Buttons page
        public static string ButtonOf = "Button {0} of {1}";
        public static string LabelCue = "Label";
        public static string Change = "Change";
        public static string AddArguments = "Add arguments";
        public static string AppKindStore = "Microsoft Store app";
        public static string AppKindProgram = "Program";
        public static string AppKindShortcut = "Shortcut";
        public static string AppKindFile = "File";
        public static string AppKindFolder = "Folder";
        public static string IconTitle = "Icon";
        public static string IconSite = "Website's own icon";
        public static string IconKeyboard = "Keyboard";
        public static string IconLetter = "First letter of the label";
        public static string[] IconNames = { "Task Manager", "Keyboard", "Screenshot", "Snip", "Lock", "Copy", "Paste", "Undo", "Search",
            "Mute mic", "Show desktop", "Windows", "Save", "Refresh", "Emoji", "Terminal", "Star" };
        public static string YourImages = "Your images";
        public static string UploadImage = "Upload image";
        public static string UploadImageTip = "Use your own picture as the icon";
        public static string UploadImageTitle = "Choose an image";
        public static string ImageFilter = "Images|*.png;*.ico;*.jpg;*.jpeg;*.bmp;*.gif|All files|*.*";
        public static string ImageHint = "PNG, ICO or JPG. Square images look best; QuickCut keeps a copy.";
        public static string Recording = "Recording…";
        public static string PressKeysNow = "Press the keys…";
        public static string NotSet = "Not set";

        // Look page
        public static string ThemeTitle = "Theme";
        public static string SizeDesc = "The preview shows the real size";
        public static string AutoColor = "Automatic";
        public static string WindowsAccent = "Windows accent";

        // Volume page
        public static string VolumeTile = "Volume";
        public static string VolumeTileDesc = "Blocks and − / + by the ring";
        public static string NowPlaying = "Now playing";
        public static string NowPlayingDesc = "Song with play controls";
        public static string Placement = "Placement";
        public static string PlacementDesc = "Where the panels sit";
        public static string StepDesc = "For scrolling, − / + and the blocks";
        public static string MixerDesc = "Each gets its own row while it's running";
        public static string MixerFooter = "Drag to change the order in the ring. Scroll over a row to change its volume, click its icon to mute.";
        public static string AddApp = "Add app";

        // General page
        public static string DoubleRightDesc = "Opens the ring where your mouse is";
        public static string FullscreenTitle = "Not over fullscreen games";
        public static string FullscreenDesc = "Keeps games and videos undisturbed";
        public static string ShortcutDesc = "Opens the ring";
        public static string StartupSection = "Startup";
        public static string StartWithWindowsDesc = "QuickCut is ready when you sign in";
        public static string NoticeTitle = "Startup notice";
        public static string NoticeDesc = "A small note that QuickCut is running";
        public static string ExportTile = "Export";
        public static string ImportTile = "Import";
        public static string RestoreTile = "Restore buttons";
    }
}
