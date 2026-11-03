# QuickCut

A tiny Windows app (~60 KB) that opens a ring of buttons around your mouse with one shortcut:
launch programs, open websites, control media (Spotify, YouTube, ...) or send key combinations.

- Open: **double right-click** or the hotkey **Ctrl+Alt+Space** (changeable in Settings).
  Not in fullscreen apps (games), unless you turn that off.
- **Hold**, move towards a button and release to pick it (with double right-click: hold the second click)
- A quick tap keeps the ring open: click or 1-9 = button, Space = center, scroll = volume, Esc = close
- Center = default button (Play/Pause), surrounded by up to 12 buttons of your own
- Settings: double-click the tray icon, or start the exe again
- Settings are stored in `%APPDATA%\QuickCut\config.ini`

## Building

No extra software needed: it uses the C# compiler that ships with Windows (.NET Framework 4.x).

```
build.cmd
```

This creates `QuickCut.exe` in the project folder.

## Adding a language

All user-facing text lives in `src/Strings.cs`. To add a language, add a method that reassigns
those fields and call it at startup (for example based on `CultureInfo.CurrentUICulture`).
