using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;
using Microsoft.Win32;

namespace QuickCut
{
    // Settings, as a dashboard: a rail with the four pages on the left, the live preview of the ring at
    // its real size in the middle (a "stage"), and the page's settings as cards on the right. Changes
    // apply right away. The Volume page is in SettingsForm.Volume.cs, import / export in .Transfer.cs.
    partial class SettingsForm : Form
    {
        static readonly string[] ThemeIds = { "system", "light", "dark" };
        static readonly string[] HoverIds = { "fill", "invert", "outline", "grow" };
        static readonly string[] SizeIds = { "small", "medium" };
        static readonly string[] NavIcons = { "nav-buttons", "nav-look", "nav-volume", "nav-general" };

        // Layout, in unscaled pixels. The window grows with the preview (left / right panels, app rows,
        // a large ring) and with the tallest page; this is its smallest size.
        const float RailW = 76, Pad = 24, Gap = 20, ColW = 400, ContentTop = 82, MinStageW = 368, MinStageH = 670, CardGap = 12;
        const float BaseW = RailW + Pad + MinStageW + Gap + ColW + Pad, BaseH = ContentTop + MinStageH + Pad;

        readonly App app;
        readonly Config work;
        readonly float sc;
        readonly Timer commitTimer, liveTimer;
        readonly UserPreferenceChangedEventHandler prefChanged;
        readonly Icon windowIcon; // a Form doesn't dispose its Icon, so the window does it itself
        Palette pal;
        bool loading;
        Rectangle workArea; // of the screen the window opened on
        int page;

        readonly NavRail rail;
        readonly NavButton[] nav = new NavButton[4];
        readonly Label lblPageTitle, lblPageSub;
        readonly StatusPill status;
        readonly RingPreview preview;
        readonly PillToolbar toolbar;
        readonly Panel pnlButtons, pnlLook, pnlVolume, pnlGeneral;
        Panel[] pages;

        // Buttons page
        Label lblCaption, lblItemColorName;
        FieldBox labelField, argsField, urlField;
        FlatButton btnRemove, btnChangeApp, lnkBrowse, lnkArgs;
        ChoiceTile[] typeTiles;
        Card appCard, webCard, mediaCard, keysCard, colorCard;
        Label lblArgs;
        AppDisplay appDisplay;
        ChipGroup mediaChips;
        KeyRecorder keysRec;
        IconPicker iconPicker;
        Swatches itemColor;
        bool showArgs;

        // Look page
        ChoiceTile[] themeTiles, hoverTiles;
        Segmented sizeSeg;
        Swatches allColor, accentColor;
        Label lblAllColorName, lblAccentName;

        // General page
        Card openCard, startCard;
        SettingRow rowMouse, rowFullscreen, rowHotkey, rowAutostart, rowNotice;
        KeyRecorder hotkeyRec;
        Label lblHotkeyError;
        ChoiceTile[] toolTiles;
        IconLink lnkBug, lnkIdea;

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
            ClientSize = new Size(S(BaseW), S(BaseH));
            Rectangle wa = workArea = Screen.FromPoint(m).WorkingArea;
            StartPosition = FormStartPosition.Manual;
            Icon = windowIcon = Art.CreateIcon(32);

            rail = Add(this, new NavRail(), 0, 0, RailW, BaseH);
            rail.Text = string.Format(Str.Version, AppVersion());
            for (int i = 0; i < 4; i++)
            {
                int pi = i;
                nav[i] = Add(rail, new NavButton(NavIcons[i], Str.PageNames[i]), 7, 14 + i * 64, 62, 58);
                nav[i].Click += delegate { ShowPage(pi); };
            }

            lblPageTitle = AddLabel(this, "", RailW + Pad, 12, 420, 34, "pagetitle");
            lblPageSub = AddLabel(this, "", RailW + Pad, 46, 520, 20, "pagesub");
            status = Add(this, new StatusPill(), 0, 22, 260, 34);

            preview = Add(this, new RingPreview(), RailW + Pad, ContentTop, MinStageW, MinStageH);
            preview.Cfg = work;
            preview.Selected = work.Items.Count > 0 ? 0 : RingRenderer.HitCenter;
            preview.SelectedChanged += delegate { ShowPage(0); LoadEditor(); };
            preview.Reordered += delegate { LoadEditor(); Changed(); };

            toolbar = new PillToolbar();
            toolbar.Items.Add(new PillToolbar.Item { Icon = "ccw", Tip = Str.MoveCounterClockwise });
            toolbar.Items.Add(new PillToolbar.Item { Icon = "plus", Text = Str.AddButton });
            toolbar.Items.Add(new PillToolbar.Item { Icon = "cw", Tip = Str.MoveClockwise });
            toolbar.Items.Add(new PillToolbar.Item { Icon = "play", Text = Str.TestShort, Primary = true });
            toolbar.ItemClicked += delegate(int i)
            {
                if (i == 0) MoveItem(-1);
                else if (i == 1) AddItem();
                else if (i == 2) MoveItem(1);
                else
                {
                    try { Actions.Execute(Current); }
                    catch (Exception ex) { MessageBox.Show(this, ex.Message, "QuickCut", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
                }
            };
            preview.Controls.Add(toolbar);

            float colX = RailW + Pad + MinStageW + Gap;
            pnlButtons = Add(this, new Panel(), colX, ContentTop, ColW, MinStageH);
            pnlLook = Add(this, new Panel(), colX, ContentTop, ColW, MinStageH);
            pnlVolume = Add(this, new Panel(), colX, ContentTop, ColW, MinStageH);
            pnlGeneral = Add(this, new Panel(), colX, ContentTop, ColW, MinStageH);
            pages = new[] { pnlButtons, pnlLook, pnlVolume, pnlGeneral };

            commitTimer = new Timer();
            commitTimer.Interval = 300;
            commitTimer.Tick += delegate { Commit(); };
            // The mixer rows and the preview's app rows follow the apps' sound while the Volume page shows.
            liveTimer = new Timer();
            liveTimer.Interval = 1000;
            liveTimer.Tick += delegate { if (page == 2 && Visible) { mixerList.Invalidate(); preview.Invalidate(); } };
            liveTimer.Start();

            // Setting the saved values fires change events; loading keeps them from counting as edits.
            loading = true;
            BuildButtonsPage();
            BuildLookPage();
            BuildVolumePage();
            BuildGeneralPage();
            loading = false;

            prefChanged = delegate(object s, UserPreferenceChangedEventArgs e)
            {
                if (IsHandleCreated) BeginInvoke((Action)ApplyTheme); // Windows light/dark or accent changed
            };
            SystemEvents.UserPreferenceChanged += prefChanged;

            SetStatus(Str.AllSaved, "muted");
            ApplyTheme();
            ShowPage(0);
            LoadEditor();
            RefreshHotkeyWarning();
            Location = new Point(wa.X + Math.Max(0, (wa.Width - Width) / 2), wa.Y + Math.Max(0, (wa.Height - Height) / 2));
            if (app != null) AppCatalog.RefreshInBackground(); // so "Pick app" opens instantly
            ActiveControl = nav[0];
        }

        // ---------- layout helpers ----------

        int S(float v) { return (int)Math.Round(v * sc); }

        // At the monitor's own scale when the window fits; on a small screen with a large scale (1080p at
        // 175%, say) everything is drawn a bit smaller instead of running off the bottom.
        internal static float FitScale(float scale, Rectangle workArea)
        {
            // Title bar and borders, generously; they follow the monitor's real scale.
            float frameW = 8 * scale, frameH = 32 * scale;
            float fit = Math.Min((workArea.Width - frameW) / BaseW, (workArea.Height - frameH) / BaseH);
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

        void Place(Control c, float x, float y, float w, float h) { c.Bounds = new Rectangle(S(x), S(y), S(w), S(h)); }

        // tag: null = normal text, "muted", "danger", "title" (card title), "desc" (small muted line),
        // "pagetitle", "pagesub"; "right" can follow, for right-aligned text ("desc right").
        Label AddLabel(Control parent, string text, float x, float y, float w, float h, string tag)
        {
            Label l = Add(parent, new Label(), x, y, w, h);
            l.Text = text;
            l.Tag = tag;
            l.UseMnemonic = false;
            l.AutoEllipsis = true;
            string t = tag ?? "";
            if (t.StartsWith("title")) l.Font = Ui.Font(14, true);
            else if (t.StartsWith("desc")) l.Font = Ui.Font(12, false);
            else if (t == "pagetitle") l.Font = Ui.Font(22, true);
            else if (t == "pagesub") l.Font = Ui.Font(13, false);
            if (t.EndsWith("right")) l.TextAlign = ContentAlignment.TopRight;
            return l;
        }

        FieldBox AddField(Control parent, TextBox box, float x, float y, float w)
        {
            return Add(parent, new FieldBox(box), x, y, w, 36);
        }

        Card AddCard(Panel page) { return Add(page, new Card(), 0, 0, ColW, 60); }

        // ---------- pages ----------

        void ShowPage(int i)
        {
            page = i;
            for (int k = 0; k < 4; k++) { nav[k].Selected = k == i; pages[k].Visible = k == i; }
            lblPageTitle.Text = Str.PageNames[i];
            lblPageSub.Text = Str.PageSubs[i];
            preview.Hint = i == 1 ? Str.HoverHint.TrimEnd('.') : i == 2 ? Str.StageHintVolume : i == 3 ? string.Format(Str.StageHintGeneral, work.Hotkey) : "";
            toolbar.Visible = i == 0;
            if (i == 2) preview.RefreshMixer();
            preview.Invalidate();
        }

        // The window around the preview and the pages: the preview shows the ring at its real size, so the
        // stage (and the window) grows to fit it, and to fit the tallest page. Only when the screen is too
        // small for that is the preview drawn smaller.
        string layoutKey;

        void LayoutWindow()
        {
            bool vol = work.VolumeBar;
            int rows = vol ? work.MixerApps.Count : 0;
            SizeF ext = RingRenderer.Extent(vol, work.MediaBar, work.PanelPos, rows);
            float frameW = Width - ClientSize.Width, frameH = Height - ClientSize.Height;
            float roomW = (workArea.Width - frameW) / sc - (RailW + Pad + Gap + ColW + Pad) - 48;
            float roomH = (workArea.Height - frameH) / sc - (ContentTop + Pad) - 48 - RingPreview.BottomStrip;
            float rs = Math.Max(0.4f, Math.Min(work.SizeFactor, Math.Min(roomW / ext.Width, roomH / ext.Height)));
            float stageW = Math.Max(MinStageW, (float)Math.Ceiling(ext.Width * rs) + 48);
            float stageH = Math.Max(MinStageH, (float)Math.Ceiling(ext.Height * rs) + 48 + RingPreview.BottomStrip);
            stageH = Math.Max(stageH, Math.Max(Math.Max(buttonsH, volumeH), Math.Max(lookH, generalH)));
            string key = stageW + "x" + stageH + ":" + rs;
            preview.RealScale = sc * rs;
            if (key == layoutKey) return;
            layoutKey = key;

            SuspendLayout();
            float colX = RailW + Pad + stageW + Gap;
            Place(rail, 0, 0, RailW, ContentTop + stageH + Pad);
            Place(preview, RailW + Pad, ContentTop, stageW, stageH);
            foreach (Panel p in pages) Place(p, colX, ContentTop, ColW, stageH);
            Place(status, colX + ColW - 260, 22, 260, 34);
            ClientSize = new Size(S(colX + ColW + Pad), S(ContentTop + stageH + Pad));
            PlaceToolbar();
            ResumeLayout();
            // Keep the window on the screen as it grows.
            if (IsHandleCreated)
            {
                int x = Math.Max(workArea.Left, Math.Min(Left, workArea.Right - Width));
                int y = Math.Max(workArea.Top, Math.Min(Top, workArea.Bottom - Height));
                if (x != Left || y != Top) Location = new Point(x, y);
            }
            Invalidate(true);
        }

        void PlaceToolbar()
        {
            int w = toolbar.PreferredWidth(), h = S(46);
            toolbar.Bounds = new Rectangle((preview.Width - w) / 2, preview.Height - S(RingPreview.BottomStrip) + (S(RingPreview.BottomStrip) - h) / 2, w, h);
        }

        // ---------- Buttons page ----------

        float buttonsH, lookH, volumeH, generalH; // each page's height, for the window's

        void BuildButtonsPage()
        {
            Panel p = pnlButtons;
            lblCaption = AddLabel(p, "", 0, 0, 280, 18, "desc");
            labelField = Add(p, new FieldBox(new TextBox()), 0, 22, 280, 40);
            labelField.Box.Font = Ui.Font(15, true);
            labelField.Box.TextChanged += delegate
            {
                if (loading) return;
                Current.Label = labelField.Box.Text;
                iconPicker.Invalidate(); // the letter icon and an image's name follow the label
                Changed();
            };
            btnRemove = Add(p, new FlatButton(Str.Remove, FlatButton.Kind.DangerBox), 292, 22, 108, 40);
            btnRemove.IconName = "trash";
            btnRemove.Click += delegate { RemoveItem(); };

            string[] typeIcons = { "apps", "globe", "media", "keyboard" };
            typeTiles = new ChoiceTile[4];
            for (int i = 0; i < 4; i++)
            {
                ActionType t = (ActionType)i;
                typeTiles[i] = Add(p, new ChoiceTile(Str.TypeTiles[i], typeIcons[i]), i * 102, 74, 94, 76);
                typeTiles[i].Click += delegate { ChangeType(t); };
            }

            appCard = AddCard(p);
            AddLabel(appCard, Str.AppLabel, 16, 14, 200, 18, "desc");
            appDisplay = Add(appCard, new AppDisplay(), 16, 40, 260, 44);
            appDisplay.Click += delegate { PickApp(); };
            btnChangeApp = Add(appCard, new FlatButton(Str.Change, FlatButton.Kind.Normal), 300, 46, 84, 32);
            btnChangeApp.Click += delegate { PickApp(); };
            Add(appCard, new Divider(), 16, 96, 368, 1);
            lnkBrowse = Add(appCard, new FlatButton(Str.BrowseFile, FlatButton.Kind.Link), 16, 108, 150, 22);
            lnkBrowse.Click += delegate { BrowseFile(); };
            lnkArgs = Add(appCard, new FlatButton(Str.AddArguments, FlatButton.Kind.Link), 180, 108, 150, 22);
            lnkArgs.Click += delegate { showArgs = true; LayoutButtonsPage(); argsField.Box.Focus(); };
            lblArgs = AddLabel(appCard, Str.Arguments, 16, 142, 300, 18, "desc");
            argsField = AddField(appCard, new TextBox(), 16, 164, 368);
            argsField.Box.TextChanged += delegate { if (!loading) { Current.Args = argsField.Box.Text; Changed(); } };

            webCard = AddCard(p);
            AddLabel(webCard, Str.WebAddress, 16, 14, 200, 18, "desc");
            urlField = AddField(webCard, new TextBox(), 16, 38, 368);
            urlField.Box.TextChanged += delegate { if (!loading) { Current.Target = urlField.Box.Text; Changed(); } };

            mediaCard = AddCard(p);
            AddLabel(mediaCard, Str.MediaAction, 16, 14, 200, 18, "desc");
            mediaChips = Add(mediaCard, new ChipGroup(), 16, 40, 368, 66);
            mediaChips.Options = Str.MediaNames;
            mediaChips.SelectedIndexChanged += delegate { SetMedia(Actions.MediaIds[mediaChips.SelectedIndex]); };

            keysCard = AddCard(p);
            AddLabel(keysCard, Str.KeysToSend, 16, 14, 200, 18, "desc");
            keysRec = Add(keysCard, new KeyRecorder(false), 16, 38, 368, 36);
            keysRec.ButtonText = Str.Change;
            keysRec.Empty = Str.NotSet;
            keysRec.Box.ComboChanged += delegate
            {
                RingItem it = Current;
                string old = it.Target;
                it.Target = keysRec.Combo;
                if (it.Label.Length == 0 || it.Label == old) it.Label = it.Target;
                LoadEditor();
                Changed();
            };

            iconPicker = Add(p, new IconPicker(), 0, 0, ColW, 214);
            iconPicker.LabelText = delegate { return Current.Label; };
            iconPicker.Changed += delegate { Current.Icon = iconPicker.Value; Changed(); };

            colorCard = AddCard(p);
            AddLabel(colorCard, Str.ButtonColor, 16, 14, 200, 18, "desc");
            lblItemColorName = AddLabel(colorCard, "", 200, 14, 184, 18, "desc right");
            itemColor = Add(colorCard, new Swatches(), 11, 35, 378, 72);
            itemColor.Entries.Add(new Swatches.Entry { Value = "", Name = Str.AutoColor, Color = delegate { return AllButtonsColor(); } });
            AddPresets(itemColor, Palette.ButtonPresets, Str.ButtonPresetNames);
            itemColor.PickerStart = delegate { return AllButtonsColor(); };
            itemColor.Changed += delegate
            {
                Current.ColorHex = itemColor.Value;
                lblItemColorName.Text = itemColor.NameOf(itemColor.Value);
                Changed();
            };
        }

        Color AllButtonsColor()
        {
            Color? c = Theme.ParseHex(work.ButtonColor);
            return c.HasValue ? c.Value : pal.Button;
        }

        static void AddPresets(Swatches s, string[] presets, string[] names)
        {
            for (int i = 0; i < presets.Length; i++)
            {
                Color c = Theme.ParseHex(presets[i]).Value;
                s.Entries.Add(new Swatches.Entry { Value = presets[i], Name = names[i], Color = delegate { return c; } });
            }
        }

        // Stacks the Buttons page: the parts for this button's type, then its icon and color.
        void LayoutButtonsPage()
        {
            RingItem it = Current;
            float y = 74 + 76 + CardGap;
            bool app_ = it.Type == ActionType.Program, web = it.Type == ActionType.Url;
            bool media = it.Type == ActionType.Media, keys = it.Type == ActionType.Keys;
            appCard.Visible = app_; webCard.Visible = web; mediaCard.Visible = media; keysCard.Visible = keys;
            iconPicker.Visible = web || keys;

            bool args = showArgs || it.Args.Length > 0;
            lblArgs.Visible = argsField.Visible = args;
            lnkArgs.Visible = !args;
            if (app_) { Place(appCard, 0, y, ColW, args ? 214 : 144); y += (args ? 214 : 144) + CardGap; }
            if (web) { Place(webCard, 0, y, ColW, 88); y += 88 + CardGap; }
            if (media)
            {
                int ch = mediaChips.PreferredHeight(S(368));
                mediaChips.Height = ch;
                float h = 40 + ch / sc + 16;
                Place(mediaCard, 0, y, ColW, h);
                y += h + CardGap;
            }
            if (keys) { Place(keysCard, 0, y, ColW, 88); y += 88 + CardGap; }
            if (web || keys) { Place(iconPicker, 0, y, ColW, 214); y += 214 + CardGap; }
            int sh = itemColor.PreferredHeight(S(378));
            itemColor.Height = sh;
            float colorH = 35 + sh / sc + 11;
            Place(colorCard, 0, y, ColW, colorH);
            y += colorH;
            buttonsH = y;
            LayoutWindow();
        }

        // ---------- Look page ----------

        void BuildLookPage()
        {
            Panel p = pnlLook;
            Card theme = AddCard(p);
            Place(theme, 0, 0, ColW, 136);
            AddLabel(theme, Str.ThemeTitle, 16, 14, 200, 22, "title");
            FlatButton reset = Add(theme, new FlatButton(Str.ResetAppearance, FlatButton.Kind.Link), 200, 14, 184, 22);
            reset.AlignRight = true;
            reset.Click += delegate { ResetLook(); };
            themeTiles = new ChoiceTile[3];
            for (int i = 0; i < 3; i++)
            {
                string id = ThemeIds[i];
                themeTiles[i] = Add(theme, new ChoiceTile(Str.ThemeOptions[i], ThemePicture(id)), 16 + i * 125, 44, 117, 78);
                themeTiles[i].PictureHeight = 38;
                themeTiles[i].Click += delegate { work.ThemeMode = id; SyncLook(); ApplyTheme(); Changed(); };
            }

            Card size = AddCard(p);
            Place(size, 0, 148, ColW, 62);
            AddLabel(size, Str.RingSize, 16, 10, 190, 22, "title");
            AddLabel(size, Str.SizeDesc, 16, 32, 190, 18, "desc");
            sizeSeg = Add(size, new Segmented(Str.SizeOptions), 232, 14, 152, 34);
            sizeSeg.SelectedIndexChanged += delegate { if (!loading) { work.RingSize = SizeIds[sizeSeg.SelectedIndex]; Changed(); } };

            Card hover = AddCard(p);
            Place(hover, 0, 222, ColW, 130);
            AddLabel(hover, Str.HoverStyle, 16, 14, 300, 22, "title");
            hoverTiles = new ChoiceTile[4];
            for (int i = 0; i < 4; i++)
            {
                string id = HoverIds[i];
                hoverTiles[i] = Add(hover, new ChoiceTile(Str.HoverOptions[i], HoverPicture(id)), 16 + i * 94, 44, 86, 72);
                hoverTiles[i].PictureHeight = 26;
                hoverTiles[i].Click += delegate { work.HoverStyle = id; SyncLook(); Changed(); };
            }

            Card all = AddCard(p);
            AddLabel(all, Str.ButtonColor, 16, 14, 200, 22, "title");
            lblAllColorName = AddLabel(all, "", 200, 16, 184, 18, "desc right");
            allColor = Add(all, new Swatches(), 11, 39, 378, 72);
            allColor.Entries.Add(new Swatches.Entry { Value = "", Name = Str.AutoColor, Color = delegate { return pal.Button; } });
            AddPresets(allColor, Palette.ButtonPresets, Str.ButtonPresetNames);
            allColor.PickerStart = delegate { return pal.Button; };
            allColor.Changed += delegate { work.ButtonColor = allColor.Value; SyncLook(); Changed(); };

            Card accent = AddCard(p);
            AddLabel(accent, Str.HoverColor, 16, 14, 200, 22, "title");
            lblAccentName = AddLabel(accent, "", 200, 16, 184, 18, "desc right");
            accentColor = Add(accent, new Swatches(), 11, 39, 378, 72);
            accentColor.Entries.Add(new Swatches.Entry { Value = "", Name = Str.AutoColor, Color = delegate { return Palette.DefaultAccent; } });
            accentColor.Entries.Add(new Swatches.Entry { Value = "windows", Name = Str.WindowsAccent, Color = delegate { return Art.Accent(); } });
            AddPresets(accentColor, Palette.HoverPresets, Str.HoverPresetNames);
            accentColor.PickerStart = delegate { return pal.Accent; };
            accentColor.Changed += delegate { work.Accent = accentColor.Value; SyncLook(); ApplyTheme(); Changed(); };

            int ah = allColor.PreferredHeight(S(378)), ch = accentColor.PreferredHeight(S(378));
            allColor.Height = ah; accentColor.Height = ch;
            float y = 364;
            Place(all, 0, y, ColW, 39 + ah / sc + 11); y += 39 + ah / sc + 11 + CardGap;
            Place(accent, 0, y, ColW, 39 + ch / sc + 11); y += 39 + ch / sc + 11;
            lookH = y;
            SyncLook();
        }

        // Shows the Look settings as they are in work (after a change, a reset or an import).
        void SyncLook()
        {
            bool was = loading;
            loading = true;
            for (int i = 0; i < 3; i++) themeTiles[i].Selected = work.ThemeMode == ThemeIds[i];
            for (int i = 0; i < 4; i++) hoverTiles[i].Selected = work.HoverStyle == HoverIds[i];
            sizeSeg.SelectedIndex = Math.Max(0, Array.IndexOf(SizeIds, work.RingSize));
            allColor.Value = work.ButtonColor;
            accentColor.Value = work.Accent;
            lblAllColorName.Text = allColor.NameOf(work.ButtonColor);
            lblAccentName.Text = accentColor.NameOf(work.Accent);
            loading = was;
        }

        void ResetLook()
        {
            work.ThemeMode = "system"; work.HoverStyle = "fill"; work.Accent = ""; work.ButtonColor = ""; work.RingSize = "medium";
            work.Center.ColorHex = "";
            foreach (RingItem it in work.Items) it.ColorHex = "";
            SyncLook();
            ApplyTheme();
            LoadEditor();
            Changed();
        }

        // A tiny window: the rail, the ring and two lines, in the theme's colors ("system" is half and half).
        static Action<Graphics, RectangleF, Color> ThemePicture(string id)
        {
            return delegate(Graphics g, RectangleF r, Color ink)
            {
                float s = ThemedControl.S;
                Color light = Color.FromArgb(0xF4, 0xF4, 0xF6), dark = Color.FromArgb(0x1B, 0x1B, 0x1E);
                Color a = id == "dark" ? dark : light, b = id == "light" ? light : dark;
                RectangleF box = new RectangleF(r.X - r.Width * 0.15f, r.Y, r.Width * 1.3f, r.Height);
                GraphicsState st = g.Save();
                using (GraphicsPath p = ThemedControl.Round(box, 6 * s))
                {
                    g.SetClip(p);
                    using (SolidBrush br = new SolidBrush(a)) g.FillRectangle(br, box);
                    using (SolidBrush br = new SolidBrush(b)) g.FillRectangle(br, box.X + box.Width / 2, box.Y, box.Width / 2, box.Height);
                    using (SolidBrush br = new SolidBrush(id == "dark" ? Color.FromArgb(0x14, 0x14, 0x16) : Color.FromArgb(0xE2, 0xE2, 0xE7)))
                        g.FillRectangle(br, box.X, box.Y, box.Width * 0.16f, box.Height);
                    g.ResetClip();
                    using (Pen pen = new Pen(Color.FromArgb(60, 128, 128, 128))) g.DrawPath(pen, p);
                }
                float d = box.Height * 0.52f, cx = box.X + box.Width * 0.46f, cy = box.Y + box.Height / 2;
                using (Pen ring = new Pen(Color.FromArgb(0xFF, 0xB3, 0x00), d * 0.2f)) g.DrawEllipse(ring, cx - d / 2, cy - d / 2, d, d);
                Color line = id == "light" ? Color.FromArgb(0xC9, 0xC9, 0xD0) : Color.FromArgb(0x55, 0x55, 0x5E);
                using (SolidBrush br = new SolidBrush(line))
                {
                    g.FillRectangle(br, box.X + box.Width * 0.68f, cy - 7 * s, box.Width * 0.22f, 4 * s);
                    g.FillRectangle(br, box.X + box.Width * 0.68f, cy + 2 * s, box.Width * 0.16f, 4 * s);
                }
                g.Restore(st);
            };
        }

        // One ring button in each hover style.
        Action<Graphics, RectangleF, Color> HoverPicture(string id)
        {
            return delegate(Graphics g, RectangleF r, Color ink)
            {
                float cx = r.X + r.Width / 2, grow = id == "grow" ? 1.12f : 1f;
                float ro = r.Height * 0.95f * grow, ri = r.Height * 0.38f;
                float cy = r.Bottom + ri * 0.1f;
                using (GraphicsPath p = Art.Sector(cx, cy + ro * 0.05f, ro, ri, -140, 100))
                {
                    Color btn = pal.Button;
                    Color fill = id == "fill" || id == "grow" ? pal.Accent : id == "invert" ? (pal.Dark ? Color.FromArgb(245, 245, 247) : Color.FromArgb(32, 32, 36)) : btn;
                    using (SolidBrush b = new SolidBrush(fill)) g.FillPath(b, p);
                    if (id == "outline") using (Pen pen = new Pen(pal.Accent, 2 * ThemedControl.S)) g.DrawPath(pen, p);
                    else using (Pen pen = new Pen(Color.FromArgb(50, 128, 128, 128))) g.DrawPath(pen, p);
                }
            };
        }

        // ---------- General page ----------

        void BuildGeneralPage()
        {
            Panel p = pnlGeneral;
            openCard = AddCard(p);
            AddLabel(openCard, Str.OpeningSection, 16, 14, 300, 22, "title");
            rowMouse = Add(openCard, new SettingRow("mouse", Str.OpenDoubleRight, Str.DoubleRightDesc, true), 1, 42, ColW - 2, 56);
            rowMouse.Checked = work.MouseTrigger;
            rowMouse.CheckedChanged += delegate { if (!loading) { work.MouseTrigger = rowMouse.Checked; Changed(); } };
            rowFullscreen = Add(openCard, new SettingRow("desktop", Str.FullscreenTitle, Str.FullscreenDesc, true), 1, 98, ColW - 2, 56);
            rowFullscreen.Checked = work.SkipFullscreen;
            rowFullscreen.CheckedChanged += delegate { if (!loading) { work.SkipFullscreen = rowFullscreen.Checked; Changed(); } };
            rowHotkey = Add(openCard, new SettingRow("keyboard", Str.KeyboardShortcut, Str.ShortcutDesc, false), 1, 154, ColW - 2, 56);
            rowHotkey.RightReserve = 222; // the key caps and the Change button
            hotkeyRec = Add(rowHotkey, new KeyRecorder(true), 183, 10, 200, 36);
            hotkeyRec.ButtonText = Str.Change;
            hotkeyRec.Empty = Str.NotSet;
            hotkeyRec.Combo = work.Hotkey;
            hotkeyRec.Box.Enter += delegate { if (app != null) app.SuspendHotkey(); };
            hotkeyRec.Box.Leave += delegate { if (app != null) app.ResumeHotkey(); };
            hotkeyRec.Box.LostFocus += delegate { if (app != null) app.ResumeHotkey(); }; // also when recording just stops
            hotkeyRec.Box.ComboChanged += delegate { TryHotkey(hotkeyRec.Combo); };
            lblHotkeyError = AddLabel(openCard, "", 16, 214, 368, 20, "danger");
            lblHotkeyError.TextChanged += delegate { LayoutGeneralPage(); };

            startCard = AddCard(p);
            AddLabel(startCard, Str.StartupSection, 16, 14, 300, 22, "title");
            rowAutostart = Add(startCard, new SettingRow("power", Str.StartWithWindows, Str.StartWithWindowsDesc, true), 1, 42, ColW - 2, 56);
            rowAutostart.Checked = Autostart.IsEnabled();
            rowAutostart.CheckedChanged += delegate
            {
                if (loading) return;
                Autostart.Set(rowAutostart.Checked);
                bool actual = Autostart.IsEnabled();
                if (actual == rowAutostart.Checked) return;
                // Windows didn't take it (Store version, turned off in Task Manager): show the real state.
                loading = true; rowAutostart.Checked = actual; loading = false;
                if (Autostart.BlockedByUser())
                    MessageBox.Show(this, Str.StartupBlockedByUser, "QuickCut", MessageBoxButtons.OK, MessageBoxIcon.Information);
            };
            rowNotice = Add(startCard, new SettingRow("bell", Str.NoticeTitle, Str.NoticeDesc, true), 1, 98, ColW - 2, 56);
            rowNotice.Checked = work.LaunchNotice;
            rowNotice.CheckedChanged += delegate { if (!loading) { work.LaunchNotice = rowNotice.Checked; Changed(); } };

            string[] names = { Str.ExportTile, Str.ImportTile, Str.RestoreTile };
            string[] icons = { "download", "upload", "ccw" };
            Action[] acts = { ExportSettings, ImportSettings, RestoreDefaults };
            toolTiles = new ChoiceTile[3];
            for (int i = 0; i < 3; i++)
            {
                Action a = acts[i];
                toolTiles[i] = Add(p, new ChoiceTile(names[i], icons[i]), i * 136, 0, 128, 76);
                toolTiles[i].Plain = true;
                toolTiles[i].Click += delegate { a(); };
            }
            lnkBug = Add(p, new IconLink("bug", Str.ReportBug), 4, 0, 150, 24);
            lnkBug.Click += delegate { OpenIssue("bug.yml"); };
            lnkIdea = Add(p, new IconLink("bulb", Str.SuggestFeature), 170, 0, 180, 24);
            lnkIdea.Click += delegate { OpenIssue("feature.yml"); };
            LayoutGeneralPage();
        }

        void LayoutGeneralPage()
        {
            bool err = lblHotkeyError.Text.Length > 0;
            lblHotkeyError.Visible = err;
            float openH = err ? 242 : 216, y = 0;
            Place(openCard, 0, y, ColW, openH); y += openH + CardGap;
            Place(startCard, 0, y, ColW, 160); y += 160 + CardGap;
            for (int i = 0; i < 3; i++) Place(toolTiles[i], i * 136, y, 128, 76);
            y += 76 + 20;
            Place(lnkBug, 4, y, 150, 24);
            Place(lnkIdea, 170, y, 180, 24);
            generalH = y + 24;
            toolTiles[2].Ink = pal.Danger;
        }

        // ---------- theme ----------

        void ApplyTheme()
        {
            pal = Palette.Get(work);
            BackColor = pal.Window;
            if (IsHandleCreated) Theme.TitleBar(Handle, pal.Dark);
            if (toolTiles != null) toolTiles[2].Ink = pal.Danger;
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
                    string tag = (c.Tag as string) ?? "";
                    c.BackColor = parent.BackColor;
                    c.ForeColor = tag.StartsWith("muted") || tag.StartsWith("desc") || tag == "pagesub" ? pal.Muted : tag == "danger" ? pal.Danger : pal.Text;
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
            labelField.SetCue(Str.LabelCue);
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

        // "Microsoft Store app", "Program", "Shortcut", "Folder" or "File", under the app's name.
        static string KindOf(string path)
        {
            try
            {
                if (path.Length == 0) return "";
                if (AppCatalog.IsAppsFolder(path)) return path.IndexOf('!') > 0 ? Str.AppKindStore : Str.AppKindProgram;
                string p = Actions.ExpandPath(path);
                if (Directory.Exists(p)) return Str.AppKindFolder;
                string ext = Path.GetExtension(p).ToLowerInvariant();
                if (ext == ".lnk" || ext == ".url") return Str.AppKindShortcut;
                if (ext == ".exe" || ext == ".bat" || ext == ".cmd") return Str.AppKindProgram;
                return Str.AppKindFile;
            }
            catch { return ""; }
        }

        int lastSelected = int.MinValue;

        void LoadEditor()
        {
            loading = true;
            RingItem it = Current;
            int s = preview.Selected;
            bool center = s < 0;
            int n = work.Items.Count;
            if (s != lastSelected) { showArgs = false; lastSelected = s; }

            lblCaption.Text = center ? Str.CenterButton : string.Format(Str.ButtonOf, s + 1, n);
            if (labelField.Box.Text != it.Label) labelField.Box.Text = it.Label;
            btnRemove.Visible = !center;
            for (int i = 0; i < typeTiles.Length; i++) typeTiles[i].Selected = (int)it.Type == i;

            bool prog = it.Type == ActionType.Program;
            appDisplay.Path = prog ? it.Target : "";
            string appName = NameOf(it.Target);
            appDisplay.Title = !prog || it.Target.Length == 0 ? Str.ChooseApp : appName.Length > 0 ? appName : it.Label;
            appDisplay.Kind = prog ? KindOf(it.Target) : "";
            appDisplay.Invalidate();
            argsField.Box.Text = it.Args;
            urlField.Box.Text = it.Type == ActionType.Url ? it.Target : "";
            mediaChips.SelectedIndex = it.Type == ActionType.Media ? Array.IndexOf(Actions.MediaIds, it.Target) : -1;
            keysRec.Combo = it.Type == ActionType.Keys ? it.Target : "";
            iconPicker.Website = it.Type == ActionType.Url;
            iconPicker.Value = it.Icon;
            itemColor.Value = it.ColorHex;
            lblItemColorName.Text = itemColor.NameOf(it.ColorHex);

            toolbar.Items[0].Enabled = toolbar.Items[2].Enabled = !center && n > 1;
            toolbar.Items[1].Enabled = n < Config.MaxItems;
            toolbar.Invalidate();
            LayoutButtonsPage();
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
            it.Icon = ""; // each kind of button starts with its own automatic icon
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
            ShowPage(0);
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
            hotkeyRec.Combo = work.Hotkey;
            preview.Hint = page == 3 ? string.Format(Str.StageHintGeneral, work.Hotkey) : preview.Hint;
            preview.Invalidate();
            if (saved) SetStatus(Str.AllSaved, "muted");
            else SetStatus(saveErr ?? Str.NotSaved, "danger");
        }

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
            LayoutWindow();
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
            // The pill shows the check mark itself.
            string t = text.StartsWith("✓") ? text.Substring(1).Trim() : text;
            status.Set(t, tag == "danger", text == Str.Saving);
        }

        // The shortcut box turns the global hotkey off while it records (Enter/Leave above), but
        // Leave doesn't fire when you switch to another app with the box still focused. So the
        // hotkey comes back whenever this window isn't active, and goes off again when you return
        // to the box. ActiveControl still points at the box while the window is in the background.
        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);
            if (app != null && ActiveControl == hotkeyRec.Box) app.SuspendHotkey();
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
            liveTimer.Dispose();
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
                f.ShowPage(tab);
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
