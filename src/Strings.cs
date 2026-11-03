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
        public static string HotkeyInUse = "The hotkey {0} is already used by another program. Pick a different one in Settings.";
        public static string HotkeyUnavailable = "The hotkey {0} can't be used (already taken by Windows or another program). Pick a different combination.";
        public static string SaveFailed = "Saving failed: {0}";
        public static string RunFailed = "Couldn't run \"{0}\":\n{1}";

        // Actions
        public static string NoProgram = "No program set.";
        public static string NoAddress = "No address set.";
        public static string UnknownMedia = "Unknown media action: {0}";
        public static string InvalidCombo = "Invalid key combination: {0}";
        public static string[] MediaNames = { "Play / Pause", "Next track", "Previous track", "Stop", "Volume up", "Volume down", "Mute" };
        public static string[] MediaShort = { "Play/Pause", "Next", "Previous", "Stop", "Volume +", "Volume -", "Mute" };

        // Default buttons
        public static string DefaultExplorer = "Explorer";

        // Settings window
        public static string SettingsTitle = "QuickCut - Settings";
        public static string HotkeyLabel = "Hotkey to open the ring";
        public static string Default = "Default";
        public static string Hint = "Hold, move to a button and release to pick it. A quick tap keeps the ring open: click or 1-9 = button, Space = center, scroll = volume, Esc = close.";
        public static string MouseTrigger = "Also open with a double right-click";
        public static string SkipFullscreen = "Not in fullscreen (games)";
        public static string ButtonsLabel = "Buttons in the ring";
        public static string Add = "Add";
        public static string Remove = "Remove";
        public static string MoveUp = "Move up";
        public static string MoveDown = "Move down";
        public static string EditButton = "Edit button";
        public static string CenterButton = "Center button";
        public static string ButtonN = "Button {0}";
        public static string Type = "Type";
        public static string[] TypeNames = { "Program / file / folder", "Website / link", "Media action (Spotify etc.)", "Key combination" };
        public static string Name = "Name (text in the ring)";
        public static string TargetProgram = "Program, file or folder";
        public static string TargetUrl = "Web address (e.g. youtube.com)";
        public static string TargetMedia = "Action (works with Spotify, YouTube, etc.)";
        public static string TargetKeys = "Keys (click and press)";
        public static string Browse = "Browse...";
        public static string Arguments = "Arguments (optional)";
        public static string Test = "Test";
        public static string StartWithWindows = "Start with Windows";
        public static string Save = "Save";
        public static string Cancel = "Cancel";
        public static string Center = "Center: {0}";
        public static string Unnamed = "(unnamed)";
        public static string PressKeys = "(click and press keys)";
        public static string BrowseTitle = "Choose a program or shortcut";
        public static string BrowseFilter = "Programs and shortcuts|*.exe;*.lnk;*.bat;*.cmd;*.url|All files|*.*";
        public static string NeedTarget = "Please enter a program or address.";
        public static string NeedCombo = "Please set a key combination.";
    }
}
