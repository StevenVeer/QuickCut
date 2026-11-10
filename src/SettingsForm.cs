using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Microsoft.Win32;

namespace QuickCut
{
    // Settings: a live ring preview on the left, three tabs on the right. Changes apply right away.
    // Import / export lives in SettingsForm.Transfer.cs.
    partial class SettingsForm : Form
    {
        static readonly string[] ThemeIds = { "system", "light", "dark" };
        static readonly string[] HoverIds = { "fill", "invert", "outline", "grow" };
        static readonly string[] SizeIds = { "small", "medium", "large" };

        readonly App app;
        readonly Config work;
        readonly float sc;
        readonly Timer commitTimer;
        readonly UserPreferenceChangedEventHandler prefChanged;
        readonly Icon windowIcon; // a Form doesn't dispose its Icon, so the window does it itself
        Palette pal;
        bool loading;

        readonly Segmented tabs;
        readonly RingPreview preview;
        readonly FlatButton btnLeft, btnAdd, btnRight;
        readonly Panel pnlButtons, pnlAppearance, pnlGeneral;

        // Buttons tab
        Label lblTitle;
        FlatButton btnRemove, btnTest;
        Tile[] typeTiles, mediaTiles;
        Panel subApp, subWeb, subMedia, subKeys;
        AppDisplay appDisplay;
        FieldBox argsField, urlField, keysField, labelField;
        HotkeyBox keysBox;
        ColorChooser itemColor;

        // Appearance tab
        Segmented themeSeg, hoverSeg, sizeSeg;
        ColorChooser allColor, accentColor;
        Label lblSize, lblHoverStyle, lblPreviewHint;

        // General tab
        ToggleSwitch tgMouse, tgFullscreen, tgAutostart, tgNotice, tgVolume, tgMedia;
        Segmented stepSeg;
        HotkeyBox hotkeyBox;
        Label lblHotkeyError;

        Label lblStatus; // "Saving..." / "All changes saved"

        public SettingsForm(App app, Config cfg)
        {
            this.app = app;
            work = cfg.Clone();
            // Open on the monitor with the mouse, at that monitor's scale (smaller if it wouldn't fit).
            Point m = Cursor.Position;
            sc = FitScale(Native.ScaleAt(m), Screen.FromPoint(m).WorkingArea);
            ThemedControl.S = sc;
            pal = Palette.Get(work);

            Text = Str.SettingsTitle;
            Font = F("Segoe UI", 9.5f);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = new Size(S(780), S(666));
            Rectangle wa = Screen.FromPoint(m).WorkingArea;
            StartPosition = FormStartPosition.Manual;
            Location = new Point(wa.X + Math.Max(0, (wa.Width - Width) / 2), wa.Y + Math.Max(0, (wa.Height - Height) / 2));
            Icon = windowIcon = Art.CreateIcon(32);

            Label title = AddLabel(this, "QuickCut", 22, 18, 110, 30, null);
            title.Font = F("Segoe UI Semibold", 13f);
            lblStatus = AddLabel(this, "", 136, 24, 280, 22, "muted"); // empty until something changes
            tabs = Add(this, new Segmented(Str.Tabs), 430, 16, 330, 36);
            tabs.SelectedIndexChanged += delegate { ShowTab(); };

            Card left = Add(this, new Card(), 20, 64, 340, 582);
            preview = Add(left, new RingPreview(), 8, 6, 324, 412);
            preview.Cfg = work;
            preview.Selected = work.Items.Count > 0 ? 0 : RingRenderer.HitCenter;
            preview.SelectedChanged += delegate { tabs.SelectedIndex = 0; LoadEditor(); };
            preview.Reordered += delegate { LoadEditor(); Changed(); };
            // Circular arrows: "left/right" is ambiguous on a circle (it flips at the bottom).
            Font arrows = F("Segoe UI Symbol", 10.5f); // has the ↺ ↻ glyphs
            btnLeft = Add(left, new FlatButton(Str.MoveLeft, FlatButton.Kind.Normal), 20, 424, 88, 34);
            btnAdd = Add(left, new FlatButton(Str.AddButton, FlatButton.Kind.Normal), 116, 424, 108, 34);
            btnRight = Add(left, new FlatButton(Str.MoveRight, FlatButton.Kind.Normal), 232, 424, 88, 34);
            btnLeft.Font = btnRight.Font = arrows;
            ToolTip tips = new ToolTip();
            tips.SetToolTip(btnLeft, Str.MoveCounterClockwise);
            tips.SetToolTip(btnRight, Str.MoveClockwise);
            btnLeft.Click += delegate { MoveItem(-1); };
            btnRight.Click += delegate { MoveItem(1); };
            btnAdd.Click += delegate { AddItem(); };
            btnTest = Add(left, new FlatButton(Str.Test, FlatButton.Kind.Normal), 20, 466, 300, 34);
            btnTest.Click += delegate
            {
                try { Actions.Execute(Current); }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "QuickCut", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            };
            lblPreviewHint = AddLabel(left, Str.PreviewHint, 20, 508, 300, 38, "muted"); // text follows the tab, see ShowTab

            Card right = Add(this, new Card(), 376, 64, 384, 582);
            pnlButtons = Add(right, new Panel(), 16, 14, 352, 488);
            pnlAppearance = Add(right, new Panel(), 16, 14, 352, 488);
            pnlGeneral = Add(right, new Panel(), 16, 14, 352, 562); // a bit taller: room for the shortcut error line

            commitTimer = new Timer();
            commitTimer.Interval = 300;
            commitTimer.Tick += delegate { Commit(); };

            // Setting the saved values fires change events; loading keeps them from counting as edits.
            loading = true;
            BuildButtonsTab();
            BuildAppearanceTab();
            BuildGeneralTab();
            loading = false;

            prefChanged = delegate(object s, UserPreferenceChangedEventArgs e)
            {
                if (IsHandleCreated) BeginInvoke((Action)ApplyTheme); // Windows light/dark or accent changed
            };
            SystemEvents.UserPreferenceChanged += prefChanged;

            ApplyTheme();
            ShowTab();
            LoadEditor();
            RefreshHotkeyWarning();
            if (app != null) AppCatalog.RefreshInBackground(); // so "Pick app" opens instantly
            ActiveControl = tabs;
        }

        // ---------- layout helpers ----------

        int S(float v) { return (int)Math.Round(v * sc); }

        // The layout is a fixed 780 x 666 plus the title bar and border. On a small screen with a large
        // scale (e.g. 1080p at 175%) that runs off the bottom, so everything is drawn a bit smaller
        // instead. When it fits, the monitor's own scale is used unchanged.
        internal static float FitScale(float scale, Rectangle workArea)
        {
            // Title bar and borders, generously; they follow the monitor's real scale.
            float frameW = 8 * scale, frameH = 32 * scale;
            float fit = Math.Min((workArea.Width - frameW) / 780f, (workArea.Height - frameH) / 666f);
            return Math.Max(0.5f, Math.Min(scale, fit));
        }

        // Font sized in pixels for this monitor. Point sizes would follow the main screen instead.
        Font F(string family, float points) { return new Font(family, points * 96f / 72f * sc, GraphicsUnit.Pixel); }

        T Add<T>(Control parent, T c, float x, float y, float w, float h) where T : Control
        {
            c.Bounds = new Rectangle(S(x), S(y), S(w), S(h));
            parent.Controls.Add(c);
            return c;
        }

        // tag: null = normal text, "muted", "danger" or "heading"
        Label AddLabel(Control parent, string text, float x, float y, float w, float h, string tag)
        {
            Label l = Add(parent, new Label(), x, y, w, h);
            l.Text = text;
            l.Tag = tag;
            if (tag == "heading") l.Font = F("Segoe UI Semibold", 10.5f);
            return l;
        }

        FieldBox AddField(Control parent, TextBox box, float x, float y, float w)
        {
            return Add(parent, new FieldBox(box), x, y, w, 34);
        }

        // ---------- tabs ----------

        void BuildButtonsTab()
        {
            Panel p = pnlButtons;
            lblTitle = AddLabel(p, "", 0, 0, 260, 26, "heading");
            btnRemove = Add(p, new FlatButton(Str.Remove, FlatButton.Kind.Danger), 290, 0, 62, 26);
            btnRemove.Click += delegate { RemoveItem(); };

            AddLabel(p, Str.WhatShouldItDo, 0, 34, 352, 20, "muted");
            Action<Graphics, RectangleF, Color>[] glyphs = {
                Art.DrawApps, Art.DrawGlobe, (g, r, c) => Art.DrawMedia(g, "playpause", r, c), Art.DrawKeyboard };
            typeTiles = new Tile[4];
            for (int i = 0; i < 4; i++)
            {
                ActionType t = (ActionType)i;
                typeTiles[i] = Add(p, new Tile(Str.TypeTiles[i], glyphs[i]), i * 90, 56, 82, 66);
                typeTiles[i].Click += delegate { ChangeType(t); };
            }

            // One sub panel per type, all in the same spot.
            subApp = Add(p, new Panel(), 0, 132, 352, 142);
            AddLabel(subApp, Str.AppLabel, 0, 0, 200, 20, "muted");
            appDisplay = Add(subApp, new AppDisplay(), 0, 20, 308, 36);
            appDisplay.Click += delegate { PickApp(); };
            FlatButton pick = Add(subApp, new FlatButton("⋯", FlatButton.Kind.Normal), 316, 20, 36, 36); // ⋯
            pick.Font = F("Segoe UI Symbol", 12f);
            pick.Click += delegate { PickApp(); };
            new ToolTip().SetToolTip(pick, Str.PickApp);
            FlatButton browse = Add(subApp, new FlatButton(Str.BrowseFile, FlatButton.Kind.Link), 0, 60, 140, 22);
            browse.Click += delegate { BrowseFile(); };
            AddLabel(subApp, Str.Arguments, 0, 88, 200, 20, "muted");
            argsField = AddField(subApp, new TextBox(), 0, 108, 352);
            argsField.Box.TextChanged += delegate { if (!loading) { Current.Args = argsField.Box.Text; Changed(); } };

            subWeb = Add(p, new Panel(), 0, 132, 352, 142);
            AddLabel(subWeb, Str.WebAddress, 0, 0, 200, 20, "muted");
            urlField = AddField(subWeb, new TextBox(), 0, 20, 352);
            urlField.Box.TextChanged += delegate { if (!loading) { Current.Target = urlField.Box.Text; Changed(); } };

            subMedia = Add(p, new Panel(), 0, 132, 352, 142);
            AddLabel(subMedia, Str.MediaAction, 0, 0, 200, 20, "muted");
            mediaTiles = new Tile[Actions.MediaIds.Length];
            for (int i = 0; i < mediaTiles.Length; i++)
            {
                string id = Actions.MediaIds[i];
                mediaTiles[i] = Add(subMedia, new Tile(Str.MediaShort[i], (g, r, c) => Art.DrawMedia(g, id, r, c)), (i % 4) * 90, 20 + (i / 4) * 60, 82, 54);
                mediaTiles[i].Click += delegate { SetMedia(id); };
            }

            subKeys = Add(p, new Panel(), 0, 132, 352, 142);
            AddLabel(subKeys, Str.KeysToSend, 0, 0, 200, 20, "muted");
            keysBox = new HotkeyBox(false);
            keysField = AddField(subKeys, keysBox, 0, 20, 352);
            keysBox.ComboChanged += delegate
            {
                RingItem it = Current;
                string old = it.Target;
                it.Target = keysBox.Combo;
                if (it.Label.Length == 0 || it.Label == old) it.Label = it.Target;
                LoadEditor();
                Changed();
            };

            AddLabel(p, Str.Label, 0, 282, 200, 20, "muted");
            labelField = AddField(p, new TextBox(), 0, 302, 352);
            labelField.Box.TextChanged += delegate { if (!loading) { Current.Label = labelField.Box.Text; Changed(); } };

            itemColor = Add(p, ButtonChooser(Str.ButtonColor, Str.ItemColorModes), 0, 344, 352, ColorChooser.ClosedHeight);
            itemColor.AutoColor = delegate
            {
                Color? c = Theme.ParseHex(work.ButtonColor);
                return c.HasValue ? c.Value : pal.Button;
            };
            itemColor.Changed += delegate { Current.ColorHex = itemColor.Value; Changed(); };
        }

        static ColorChooser ButtonChooser(string title, string[] modes)
        {
            return new ColorChooser(title, modes, false, Palette.ButtonPresets, Str.ButtonPresetNames);
        }

        void BuildAppearanceTab()
        {
            Panel p = pnlAppearance;
            AddLabel(p, Str.Theme, 0, 0, 200, 20, "muted");
            // The reset link shares the first row, so the color grids have room to open below.
            FlatButton reset = Add(p, new FlatButton(Str.ResetAppearance, FlatButton.Kind.Link), 172, -2, 180, 22);
            reset.AlignRight = true;
            themeSeg = Add(p, new Segmented(Str.ThemeOptions), 0, 20, 352, 36);
            themeSeg.SelectedIndex = Math.Max(0, Array.IndexOf(ThemeIds, work.ThemeMode));
            themeSeg.SelectedIndexChanged += delegate { work.ThemeMode = ThemeIds[themeSeg.SelectedIndex]; ApplyTheme(); Changed(); };

            lblSize = AddLabel(p, Str.RingSize, 0, 0, 352, 20, "muted");
            sizeSeg = Add(p, new Segmented(Str.SizeOptions), 0, 0, 352, 36);
            sizeSeg.SelectedIndex = Math.Max(0, Array.IndexOf(SizeIds, work.RingSize));
            sizeSeg.SelectedIndexChanged += delegate { work.RingSize = SizeIds[sizeSeg.SelectedIndex]; Changed(); };

            allColor = Add(p, ButtonChooser(Str.ButtonColorAll, Str.ButtonColorModes), 0, 0, 352, ColorChooser.ClosedHeight);
            allColor.AutoColor = delegate { return pal.Button; };
            allColor.Value = work.ButtonColor;
            allColor.Changed += delegate { work.ButtonColor = allColor.Value; Changed(); };
            allColor.HeightChanged += delegate { LayoutAppearance(); };

            accentColor = Add(p, new ColorChooser(Str.HoverColor, Str.HoverColorModes, true, Palette.HoverPresets, Str.HoverPresetNames),
                0, 0, 352, ColorChooser.ClosedHeight);
            accentColor.AutoColor = delegate { return Palette.DefaultAccent; };
            accentColor.Value = work.Accent;
            accentColor.Changed += delegate { work.Accent = accentColor.Value; ApplyTheme(); Changed(); };
            accentColor.HeightChanged += delegate { LayoutAppearance(); };

            lblHoverStyle = AddLabel(p, Str.HoverStyle, 0, 0, 352, 20, "muted");
            hoverSeg = Add(p, new Segmented(Str.HoverOptions), 0, 0, 352, 36);
            hoverSeg.SelectedIndex = Math.Max(0, Array.IndexOf(HoverIds, work.HoverStyle));
            hoverSeg.SelectedIndexChanged += delegate { work.HoverStyle = HoverIds[hoverSeg.SelectedIndex]; Changed(); };
            LayoutAppearance();

            reset.Click += delegate
            {
                work.ThemeMode = "system"; work.HoverStyle = "fill"; work.Accent = ""; work.ButtonColor = ""; work.RingSize = "medium";
                work.Center.ColorHex = "";
                foreach (RingItem it in work.Items) it.ColorHex = "";
                themeSeg.SelectedIndex = 0; hoverSeg.SelectedIndex = 0; sizeSeg.SelectedIndex = 1;
                allColor.Value = ""; accentColor.Value = "";
                ApplyTheme();
                LoadEditor();
                Changed();
            };
        }

        // Stacks the Appearance settings; the color choosers grow when their grid is open.
        void LayoutAppearance()
        {
            const float gap = 12;
            float y = 68; // below the theme row
            lblSize.Top = S(y); sizeSeg.Top = S(y + 20); y += 56 + gap;
            allColor.Top = S(y); y += allColor.Height / sc + gap;
            accentColor.Top = S(y); y += accentColor.Height / sc + gap;
            lblHoverStyle.Top = S(y); hoverSeg.Top = S(y + 20);
        }

        void BuildGeneralTab()
        {
            Panel p = pnlGeneral;
            loading = true;
            AddLabel(p, Str.OpeningSection, 0, 0, 352, 24, "heading");
            tgMouse = Row(p, Str.OpenDoubleRight, 34, work.MouseTrigger);
            tgMouse.CheckedChanged += delegate { if (!loading) { work.MouseTrigger = tgMouse.Checked; Changed(); } };
            tgFullscreen = Row(p, Str.NotFullscreen, 70, work.SkipFullscreen);
            tgFullscreen.CheckedChanged += delegate { if (!loading) { work.SkipFullscreen = tgFullscreen.Checked; Changed(); } };

            AddLabel(p, Str.KeyboardShortcut, 0, 110, 352, 20, null);
            hotkeyBox = new HotkeyBox(true);
            AddField(p, hotkeyBox, 0, 132, 240);
            hotkeyBox.Combo = work.Hotkey;
            hotkeyBox.Enter += delegate { app.SuspendHotkey(); };
            hotkeyBox.Leave += delegate { app.ResumeHotkey(); };
            hotkeyBox.ComboChanged += delegate { TryHotkey(hotkeyBox.Combo); };
            FlatButton def = Add(p, new FlatButton(Str.Default, FlatButton.Kind.Normal), 248, 132, 104, 34);
            def.Click += delegate { TryHotkey(Config.DefaultHotkey); };
            lblHotkeyError = AddLabel(p, "", 0, 172, 352, 28, "danger");
            int firstBelow = p.Controls.Count; // everything from here down moves when the error shows
            AddLabel(p, Str.Hint, 0, 174, 352, 38, "muted");

            AddLabel(p, Str.VolumeSection, 0, 222, 352, 24, "heading");
            tgVolume = Row(p, Str.ShowVolume, 254, work.VolumeBar);
            tgVolume.CheckedChanged += delegate { if (!loading) { work.VolumeBar = tgVolume.Checked; Changed(); } };
            // The step also applies with the blocks hidden: scrolling over the ring uses it.
            AddLabel(p, Str.VolumeStep, 0, 295, 220, 22, null);
            stepSeg = Add(p, new Segmented(Str.VolumeStepOptions), 232, 290, 120, 32);
            stepSeg.SelectedIndex = work.VolumeStep == 10 ? 1 : 0;
            stepSeg.SelectedIndexChanged += delegate { if (!loading) { work.VolumeStep = stepSeg.SelectedIndex == 1 ? 10 : 5; Changed(); } };

            tgMedia = Row(p, Str.ShowMedia, 330, work.MediaBar);
            tgMedia.CheckedChanged += delegate { if (!loading) { work.MediaBar = tgMedia.Checked; Changed(); } };

            AddLabel(p, Str.OtherSection, 0, 368, 352, 24, "heading");
            tgAutostart = Row(p, Str.StartWithWindows, 402, Autostart.IsEnabled());
            tgAutostart.CheckedChanged += delegate
            {
                if (loading) return;
                Autostart.Set(tgAutostart.Checked);
                bool actual = Autostart.IsEnabled();
                if (actual == tgAutostart.Checked) return;
                // Windows didn't take it (Store version, turned off in Task Manager): show the real state.
                loading = true; tgAutostart.Checked = actual; loading = false;
                if (Autostart.BlockedByUser())
                    MessageBox.Show(this, Str.StartupBlockedByUser, "QuickCut", MessageBoxButtons.OK, MessageBoxIcon.Information);
            };
            tgNotice = Row(p, Str.LaunchNotice, 438, work.LaunchNotice);
            tgNotice.CheckedChanged += delegate { if (!loading) { work.LaunchNotice = tgNotice.Checked; Changed(); } };
            FlatButton restore = Add(p, new FlatButton(Str.RestoreButtons, FlatButton.Kind.Link), 0, 476, 200, 24);
            restore.Click += delegate { RestoreDefaults(); };
            FlatButton transfer = Add(p, new FlatButton(Str.ImportExport, FlatButton.Kind.Link), 212, 476, 140, 24);
            transfer.AlignRight = true;
            transfer.Click += delegate { ShowTransferMenu(transfer); };
            FlatButton bug = Add(p, new FlatButton(Str.ReportBug, FlatButton.Kind.Link), 0, 506, 160, 24);
            bug.Click += delegate { OpenIssue("bug.yml"); };
            FlatButton idea = Add(p, new FlatButton(Str.SuggestFeature, FlatButton.Kind.Link), 172, 506, 180, 24);
            idea.Click += delegate { OpenIssue("feature.yml"); };

            belowError = new System.Collections.Generic.List<Control>();
            belowErrorTop = new System.Collections.Generic.List<int>();
            for (int i = firstBelow; i < p.Controls.Count; i++) { belowError.Add(p.Controls[i]); belowErrorTop.Add(p.Controls[i].Top); }
            lblHotkeyError.TextChanged += delegate { LayoutHotkeyError(); };
            LayoutHotkeyError();
            loading = false;
        }

        ToggleSwitch Row(Panel p, string text, float y, bool on)
        {
            AddLabel(p, text, 0, y + 3, 290, 22, null);
            ToggleSwitch t = Add(p, new ToggleSwitch(), 300, y, 52, 28);
            t.Checked = on;
            return t;
        }

        void ShowTab()
        {
            pnlButtons.Visible = tabs.SelectedIndex == 0;
            pnlAppearance.Visible = tabs.SelectedIndex == 1;
            pnlGeneral.Visible = tabs.SelectedIndex == 2;
            // The hover hint sits under the ring it talks about, so it always fits, even with both color grids open.
            lblPreviewHint.Text = tabs.SelectedIndex == 1 ? Str.HoverHint : Str.PreviewHint;
        }

        // ---------- theme ----------

        void ApplyTheme()
        {
            pal = Palette.Get(work);
            BackColor = pal.Window;
            if (IsHandleCreated) Theme.TitleBar(Handle, pal.Dark);
            Walk(this);
            Invalidate(true);
        }

        void Walk(Control parent)
        {
            foreach (Control c in parent.Controls)
            {
                ThemedControl t = c as ThemedControl;
                if (t != null) t.SetPalette(pal);
                else if (c is Label)
                {
                    string tag = c.Tag as string;
                    c.BackColor = parent.BackColor;
                    c.ForeColor = tag == "muted" ? pal.Muted : tag == "danger" ? pal.Danger : pal.Text;
                }
                else if (c is Panel) c.BackColor = parent.BackColor;
                Walk(c);
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.TitleBar(Handle, pal.Dark);
            urlField.SetCue(Str.WebCue);
        }

        // ---------- editing ----------

        RingItem Current
        {
            get
            {
                int s = preview.Selected;
                return s >= 0 && s < work.Items.Count ? work.Items[s] : work.Center;
            }
        }

        static string NameOf(string path)
        {
            try
            {
                if (AppCatalog.IsAppsFolder(path)) return AppCatalog.NameFor(path) ?? "";
                string p = Actions.ExpandPath(path);
                if (Directory.Exists(p)) return new DirectoryInfo(p).Name;
                return Path.GetFileNameWithoutExtension(p);
            }
            catch { return ""; }
        }

        void LoadEditor()
        {
            loading = true;
            RingItem it = Current;
            int s = preview.Selected;
            bool center = s < 0;
            int n = work.Items.Count;

            lblTitle.Text = center ? Str.CenterButton : string.Format(Str.ButtonN, s + 1);
            btnRemove.Visible = !center;
            for (int i = 0; i < typeTiles.Length; i++) typeTiles[i].Selected = (int)it.Type == i;
            subApp.Visible = it.Type == ActionType.Program;
            subWeb.Visible = it.Type == ActionType.Url;
            subMedia.Visible = it.Type == ActionType.Media;
            subKeys.Visible = it.Type == ActionType.Keys;

            bool prog = it.Type == ActionType.Program;
            appDisplay.Path = prog ? it.Target : "";
            string appName = NameOf(it.Target);
            appDisplay.Title = !prog || it.Target.Length == 0 ? Str.ChooseApp : appName.Length > 0 ? appName : it.Label;
            appDisplay.Invalidate();
            argsField.Box.Text = it.Args;
            urlField.Box.Text = it.Type == ActionType.Url ? it.Target : "";
            for (int i = 0; i < mediaTiles.Length; i++)
                mediaTiles[i].Selected = it.Type == ActionType.Media && Actions.MediaIds[i] == it.Target;
            keysBox.Combo = it.Type == ActionType.Keys ? it.Target : "";
            labelField.Box.Text = it.Label;
            itemColor.Value = it.ColorHex;

            btnLeft.Enabled = btnRight.Enabled = !center && n > 1;
            btnAdd.Enabled = n < Config.MaxItems;
            btnLeft.Invalidate(); btnRight.Invalidate(); btnAdd.Invalidate();
            preview.Invalidate();
            loading = false;
        }

        void ChangeType(ActionType t)
        {
            RingItem it = Current;
            if (it.Type == t) return;
            bool auto = it.Label.Length == 0 || it.Label == Actions.MediaShortName(it.Target) || it.Label == it.Target || it.Label == NameOf(it.Target);
            it.Type = t;
            it.Target = t == ActionType.Media ? Actions.MediaIds[0] : "";
            it.Args = "";
            if (auto) it.Label = t == ActionType.Media ? Actions.MediaShortName(it.Target) : "";
            LoadEditor();
            Changed();
        }

        void SetMedia(string id)
        {
            RingItem it = Current;
            string old = Actions.MediaShortName(it.Target);
            it.Target = id;
            if (it.Label.Length == 0 || it.Label == old) it.Label = Actions.MediaShortName(id);
            LoadEditor();
            Changed();
        }

        void SetTarget(string path, string name)
        {
            RingItem it = Current;
            if (it.Label.Length == 0 || it.Label == NameOf(it.Target)) it.Label = name;
            it.Target = path;
            it.Args = "";
            LoadEditor();
            Changed();
        }

        void PickApp()
        {
            using (AppPicker d = new AppPicker(pal))
                if (d.ShowDialog(this) == DialogResult.OK) SetTarget(d.Result.Path, d.Result.Name);
        }

        void BrowseFile()
        {
            using (OpenFileDialog d = new OpenFileDialog())
            {
                d.Title = Str.BrowseTitle;
                d.Filter = Str.BrowseFilter;
                d.DereferenceLinks = false;
                if (d.ShowDialog(this) == DialogResult.OK) SetTarget(d.FileName, NameOf(d.FileName));
            }
        }


        void AddItem()
        {
            if (work.Items.Count >= Config.MaxItems) return;
            work.Items.Add(new RingItem(ActionType.Program, "", ""));
            preview.Selected = work.Items.Count - 1;
            tabs.SelectedIndex = 0;
            LoadEditor();
            Changed();
        }

        void RemoveItem()
        {
            int s = preview.Selected;
            if (s < 0 || s >= work.Items.Count) return;
            work.Items.RemoveAt(s);
            preview.Selected = work.Items.Count == 0 ? RingRenderer.HitCenter : Math.Min(s, work.Items.Count - 1);
            LoadEditor();
            Changed();
        }

        void MoveItem(int dir)
        {
            // Wraps around: the last button and the first one are neighbours on the ring.
            int n = work.Items.Count, s = preview.Selected;
            if (s < 0 || n < 2) return;
            int t = (s + dir + n) % n;
            RingItem tmp = work.Items[s];
            work.Items[s] = work.Items[t];
            work.Items[t] = tmp;
            preview.Selected = t;
            LoadEditor();
            Changed();
        }

        void RestoreDefaults()
        {
            if (MessageBox.Show(this, Str.RestoreConfirm, "QuickCut", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            Config d = Config.CreateDefault();
            work.Center = d.Center;
            work.Items.Clear();
            work.Items.AddRange(d.Items);
            preview.Selected = work.Items.Count > 0 ? 0 : RingRenderer.HitCenter;
            LoadEditor();
            Changed();
        }

        void TryHotkey(string combo)
        {
            commitTimer.Stop();
            string old = work.Hotkey;
            work.Hotkey = combo;
            string saveErr, keyErr;
            bool saved = app.ApplyConfig(work.Clone(), out saveErr, out keyErr);
            if (keyErr != null && old != combo)
            {
                // Windows refused it: keep the previous shortcut and say why.
                work.Hotkey = old;
                saved = app.ApplyConfig(work.Clone(), out saveErr, out keyErr);
                lblHotkeyError.Text = string.Format(Str.HotkeyUnavailable, combo);
            }
            else RefreshHotkeyWarning();
            hotkeyBox.Combo = work.Hotkey;
            if (saved) SetStatus(Str.AllSaved, "muted");
            else SetStatus(saveErr ?? Str.NotSaved, "danger");
        }

        // The error line only takes room while there is something to say; the rest slides down under it.
        System.Collections.Generic.List<Control> belowError;
        System.Collections.Generic.List<int> belowErrorTop;
        // Opens a new GitHub issue with the versions filled in (issue forms take field ids as query parameters).
        void OpenIssue(string template)
        {
            string url = "https://github.com/StevenVeer/QuickCut/issues/new?template=" + template +
                "&version=" + Uri.EscapeDataString(AppVersion()) +
                "&windows=" + Uri.EscapeDataString(WindowsVersion());
            try { Actions.Start(url, "", ""); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "QuickCut", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }

        static string AppVersion()
        {
            Version v = typeof(SettingsForm).Assembly.GetName().Version;
            return v.Revision > 0 ? v.ToString() : v.ToString(3); // 1.0.0, or 1.0.0.5 when the revision is set
        }

        // "Windows 11 Pro 24H2 (build 26100.2314)". The registry's ProductName still says "Windows 10" on 11, so the build decides.
        static string WindowsVersion()
        {
            try
            {
                using (RegistryKey k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                {
                    string build = Convert.ToString(k.GetValue("CurrentBuild", ""));
                    int b;
                    int.TryParse(build, out b);
                    string name = Convert.ToString(k.GetValue("ProductName", "Windows"));
                    if (b >= 22000) name = name.Replace("Windows 10", "Windows 11");
                    string display = Convert.ToString(k.GetValue("DisplayVersion", ""));
                    object ubr = k.GetValue("UBR");
                    string full = build + (ubr != null ? "." + ubr : "");
                    return (name + " " + display).Trim() + " (build " + full + ")";
                }
            }
            catch { return Environment.OSVersion.VersionString; }
        }

        void LayoutHotkeyError()
        {
            bool show = lblHotkeyError.Text.Length > 0;
            lblHotkeyError.Visible = show;
            int shift = show ? S(30) : 0;
            for (int i = 0; i < belowError.Count; i++) belowError[i].Top = belowErrorTop[i] + shift;
        }

        void RefreshHotkeyWarning()
        {
            if (app == null) return;
            lblHotkeyError.Text = app.HotkeyActive ? "" : string.Format(Str.HotkeyUnavailable, work.Hotkey);
        }

        // ---------- saving ----------

        // Changes apply right away; saving is batched so typing doesn't write the file on every key.
        void Changed()
        {
            if (loading) return;
            preview.Invalidate();
            SetStatus(Str.Saving, "muted");
            commitTimer.Stop();
            commitTimer.Start();
        }

        void Commit()
        {
            commitTimer.Stop();
            string saveErr, keyErr;
            if (app.ApplyConfig(work.Clone(), out saveErr, out keyErr)) SetStatus(Str.AllSaved, "muted");
            else SetStatus(saveErr ?? Str.NotSaved, "danger");
            RefreshHotkeyWarning();
        }

        void SetStatus(string text, string tag)
        {
            lblStatus.Text = text;
            lblStatus.Tag = tag;
            lblStatus.ForeColor = tag == "danger" ? pal.Danger : pal.Muted;
        }

        // The shortcut box turns the global hotkey off while it records (Enter/Leave above), but
        // Leave doesn't fire when you switch to another app with the box still focused. So the
        // hotkey comes back whenever this window isn't active, and goes off again when you return
        // to the box. ActiveControl still points at the box while the window is in the background.
        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);
            if (app != null && ActiveControl == hotkeyBox) app.SuspendHotkey();
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            if (app != null) app.ResumeHotkey();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (commitTimer.Enabled) Commit();
            base.OnFormClosing(e);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            SystemEvents.UserPreferenceChanged -= prefChanged;
            commitTimer.Dispose();
            base.OnFormClosed(e);
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            // After the window is gone, so it never shows a freed icon.
            if (disposing && windowIcon != null) windowIcon.Dispose();
        }

        // Draws the window with default buttons into a PNG (used to check the design), off screen.
        public static void SavePreview(string path, string theme, int tab, string size, string buttonColor, string accent)
        {
            Config c = Config.CreateDefault();
            c.ThemeMode = theme;
            c.RingSize = size;
            c.ButtonColor = buttonColor;
            c.Accent = accent;
            using (Bitmap b = Render(c, tab)) b.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        }

        // Draws the settings window off screen (for previews and the Store screenshots).
        public static Bitmap Render(Config c, int tab) { return Render(c, tab, false); }

        // clientOnly: without the title bar and border (DrawToBitmap draws those in an old style).
        public static Bitmap Render(Config c, int tab, bool clientOnly)
        {
            using (SettingsForm f = new SettingsForm(null, c))
            {
                f.StartPosition = FormStartPosition.Manual;
                f.Location = new Point(-20000, -20000);
                f.tabs.SelectedIndex = tab;
                f.Show();
                Application.DoEvents();
                Bitmap b = new Bitmap(f.Width, f.Height);
                f.DrawToBitmap(b, new Rectangle(Point.Empty, f.Size));
                f.commitTimer.Stop();
                if (!clientOnly) return b;
                Point origin = f.PointToScreen(Point.Empty);
                Rectangle client = new Rectangle(origin.X - f.Left, origin.Y - f.Top, f.ClientSize.Width, f.ClientSize.Height);
                using (b) return b.Clone(client, b.PixelFormat);
            }
        }
    }
}
