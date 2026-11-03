using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace QuickCut
{
    class SettingsForm : Form
    {
        static readonly string[] TypeNames = { "Programma / bestand / map", "Website / link", "Media-actie (Spotify e.d.)", "Toetscombinatie" };

        readonly App app;
        readonly Config work;
        readonly float sc;
        bool loading;

        readonly HotkeyBox hotkeyBox, keysBox;
        readonly ListBox list;
        readonly Button btnAdd, btnRemove, btnUp, btnDown, btnBrowse, btnTest;
        readonly GroupBox editBox;
        readonly ComboBox cmbType, cmbMedia;
        readonly TextBox txtLabel, txtTarget, txtArgs;
        readonly Label lblTarget, lblArgs;
        readonly CheckBox chkAutostart, chkMouse, chkFullscreen;

        public SettingsForm(App app, Config cfg)
        {
            this.app = app;
            work = cfg.Clone();
            using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) sc = g.DpiX / 96f;

            Text = "QuickCut - Instellingen";
            Font = new Font("Segoe UI", 9f);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = true;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = new Size(S(520), S(520));
            Icon = Art.CreateIcon(32);

            // Hotkey
            Add(this, new Label { Text = "Sneltoets om de ring te openen", AutoSize = true }, 16, 14, 300, 18);
            hotkeyBox = Add(this, new HotkeyBox(true), 16, 36, 200, 24);
            hotkeyBox.Combo = work.Hotkey;
            hotkeyBox.Enter += delegate { app.SuspendHotkey(); };
            hotkeyBox.Leave += delegate { app.ResumeHotkey(); };
            hotkeyBox.ComboChanged += delegate { work.Hotkey = hotkeyBox.Combo; };
            Button btnDefault = Add(this, new Button { Text = "Standaard" }, 224, 35, 90, 26);
            btnDefault.Click += delegate { hotkeyBox.Combo = Config.DefaultHotkey; work.Hotkey = Config.DefaultHotkey; };
            Add(this, new Label
            {
                Text = "Ingedrukt houden, naar een knop bewegen en loslaten kiest die knop. Kort tikken laat de ring open: klik of 1-9 = knop, Spatie = midden, scrollen = volume, Esc = sluiten.",
                ForeColor = SystemColors.GrayText
            }, 16, 64, 490, 34);
            chkMouse = Add(this, new CheckBox { Text = "Ook openen met 2x rechtermuisklik", AutoSize = true }, 16, 104, 240, 22);
            chkMouse.Checked = work.MouseTrigger;
            chkMouse.CheckedChanged += delegate { work.MouseTrigger = chkMouse.Checked; chkFullscreen.Enabled = chkMouse.Checked; };
            chkFullscreen = Add(this, new CheckBox { Text = "Niet in volledig scherm (games)", AutoSize = true }, 270, 104, 240, 22);
            chkFullscreen.Checked = work.SkipFullscreen;
            chkFullscreen.Enabled = work.MouseTrigger;
            chkFullscreen.CheckedChanged += delegate { work.SkipFullscreen = chkFullscreen.Checked; };

            // List of buttons
            Add(this, new Label { Text = "Knoppen in de ring", AutoSize = true }, 16, 134, 220, 18);
            list = Add(this, new ListBox { IntegralHeight = false }, 16, 156, 220, 252);
            list.SelectedIndexChanged += delegate { if (!loading) { LoadEditor(); UpdateButtons(); } };
            btnAdd = Add(this, new Button { Text = "Toevoegen" }, 16, 414, 106, 28);
            btnRemove = Add(this, new Button { Text = "Verwijderen" }, 130, 414, 106, 28);
            btnUp = Add(this, new Button { Text = "Omhoog" }, 16, 446, 106, 28);
            btnDown = Add(this, new Button { Text = "Omlaag" }, 130, 446, 106, 28);
            btnAdd.Click += delegate
            {
                work.Items.Add(new RingItem(ActionType.Program, "", ""));
                RefreshList(work.Items.Count);
                txtLabel.Focus();
            };
            btnRemove.Click += delegate
            {
                int i = list.SelectedIndex;
                if (i <= 0) return;
                work.Items.RemoveAt(i - 1);
                RefreshList(Math.Min(i, work.Items.Count));
            };
            btnUp.Click += delegate { Swap(list.SelectedIndex - 1, list.SelectedIndex - 2); };
            btnDown.Click += delegate { Swap(list.SelectedIndex - 1, list.SelectedIndex); };

            // Editor
            editBox = Add(this, new GroupBox { Text = "Knop bewerken" }, 252, 148, 252, 294);
            Add(editBox, new Label { Text = "Soort", AutoSize = true }, 12, 24, 200, 18);
            cmbType = Add(editBox, new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList }, 12, 44, 228, 24);
            cmbType.Items.AddRange(TypeNames);
            Add(editBox, new Label { Text = "Naam (tekst in de ring)", AutoSize = true }, 12, 78, 200, 18);
            txtLabel = Add(editBox, new TextBox(), 12, 98, 228, 24);
            lblTarget = Add(editBox, new Label { AutoSize = true }, 12, 132, 228, 18);
            txtTarget = Add(editBox, new TextBox(), 12, 152, 150, 24);
            btnBrowse = Add(editBox, new Button { Text = "Bladeren..." }, 168, 151, 72, 26);
            cmbMedia = Add(editBox, new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList }, 12, 152, 228, 24);
            cmbMedia.Items.AddRange(Actions.MediaNames);
            keysBox = Add(editBox, new HotkeyBox(false), 12, 152, 228, 24);
            lblArgs = Add(editBox, new Label { Text = "Argumenten (optioneel)", AutoSize = true }, 12, 186, 200, 18);
            txtArgs = Add(editBox, new TextBox(), 12, 206, 228, 24);
            btnTest = Add(editBox, new Button { Text = "Testen" }, 12, 252, 100, 28);

            cmbType.SelectedIndexChanged += delegate { if (!loading) ChangeType((ActionType)cmbType.SelectedIndex); };
            txtLabel.TextChanged += delegate { if (!loading && Current != null) { Current.Label = txtLabel.Text; UpdateListText(); } };
            txtTarget.TextChanged += delegate { if (!loading && Current != null) Current.Target = txtTarget.Text; };
            txtArgs.TextChanged += delegate { if (!loading && Current != null) Current.Args = txtArgs.Text; };
            cmbMedia.SelectedIndexChanged += delegate
            {
                RingItem it = Current;
                if (loading || it == null || cmbMedia.SelectedIndex < 0) return;
                string old = it.Target;
                it.Target = Actions.MediaIds[cmbMedia.SelectedIndex];
                if (it.Label.Length == 0 || it.Label == Actions.MediaShortName(old)) SetLabel(Actions.MediaShortName(it.Target));
            };
            keysBox.ComboChanged += delegate
            {
                RingItem it = Current;
                if (it == null) return;
                string old = it.Target;
                it.Target = keysBox.Combo;
                if (it.Label.Length == 0 || it.Label == old) SetLabel(it.Target);
            };
            btnBrowse.Click += delegate { Browse(); };
            btnTest.Click += delegate
            {
                if (Current == null) return;
                try { Actions.Execute(Current); }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "QuickCut", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            };

            // Bottom
            chkAutostart = Add(this, new CheckBox { Text = "Starten met Windows", AutoSize = true }, 16, 486, 200, 22);
            chkAutostart.Checked = Autostart.IsEnabled();
            Button btnSave = Add(this, new Button { Text = "Opslaan" }, 318, 480, 90, 30);
            Button btnCancel = Add(this, new Button { Text = "Annuleren" }, 414, 480, 90, 30);
            btnSave.Click += delegate { Save(); };
            btnCancel.Click += delegate { Close(); };
            CancelButton = btnCancel;

            RefreshList(1);
            ActiveControl = list; // not the hotkey box: focusing it pauses the hotkey
        }

        int S(float v) { return (int)Math.Round(v * sc); }

        T Add<T>(Control parent, T c, float x, float y, float w, float h) where T : Control
        {
            c.Bounds = new Rectangle(S(x), S(y), S(w), S(h));
            parent.Controls.Add(c);
            return c;
        }

        RingItem Current
        {
            get
            {
                int i = list.SelectedIndex;
                if (i < 0) return null;
                return i == 0 ? work.Center : work.Items[i - 1];
            }
        }

        static string Display(RingItem it) { return string.IsNullOrEmpty(it.Label) ? "(naamloos)" : it.Label; }

        string ListText(int i)
        {
            return i == 0 ? "Midden: " + Display(work.Center) : i + ".  " + Display(work.Items[i - 1]);
        }

        void RefreshList(int select)
        {
            loading = true;
            list.BeginUpdate();
            list.Items.Clear();
            for (int i = 0; i <= work.Items.Count; i++) list.Items.Add(ListText(i));
            list.EndUpdate();
            list.SelectedIndex = Math.Max(0, Math.Min(select, list.Items.Count - 1));
            loading = false;
            LoadEditor();
            UpdateButtons();
        }

        void UpdateListText()
        {
            int i = list.SelectedIndex;
            if (i < 0) return;
            loading = true;
            list.Items[i] = ListText(i);
            list.SelectedIndex = i;
            loading = false;
        }

        void UpdateButtons()
        {
            int i = list.SelectedIndex;
            btnRemove.Enabled = i > 0;
            btnUp.Enabled = i > 1;
            btnDown.Enabled = i > 0 && i < work.Items.Count;
            btnAdd.Enabled = work.Items.Count < Config.MaxItems;
        }

        void Swap(int a, int b)
        {
            if (a < 0 || b < 0 || a >= work.Items.Count || b >= work.Items.Count) return;
            RingItem t = work.Items[a];
            work.Items[a] = work.Items[b];
            work.Items[b] = t;
            RefreshList(b + 1);
        }

        void SetLabel(string text)
        {
            Current.Label = text;
            loading = true;
            txtLabel.Text = text;
            loading = false;
            UpdateListText();
        }

        void LoadEditor()
        {
            RingItem it = Current;
            editBox.Enabled = it != null;
            if (it == null) return;
            loading = true;
            editBox.Text = list.SelectedIndex == 0 ? "Middelste knop" : "Knop " + list.SelectedIndex;
            cmbType.SelectedIndex = (int)it.Type;
            txtLabel.Text = it.Label;
            txtTarget.Text = it.Type == ActionType.Program || it.Type == ActionType.Url ? it.Target : "";
            cmbMedia.SelectedIndex = Math.Max(0, Array.IndexOf(Actions.MediaIds, it.Target));
            keysBox.Combo = it.Type == ActionType.Keys ? it.Target : "";
            txtArgs.Text = it.Args;
            loading = false;

            ActionType t = it.Type;
            bool prog = t == ActionType.Program;
            txtTarget.Visible = prog || t == ActionType.Url;
            txtTarget.Width = S(prog ? 150 : 228);
            btnBrowse.Visible = prog;
            cmbMedia.Visible = t == ActionType.Media;
            keysBox.Visible = t == ActionType.Keys;
            lblArgs.Visible = txtArgs.Visible = prog;
            lblTarget.Text = prog ? "Programma, bestand of map"
                : t == ActionType.Url ? "Webadres (bijv. youtube.com)"
                : t == ActionType.Media ? "Actie (werkt met Spotify, YouTube, enz.)"
                : "Toetsen (klik en druk in)";
        }

        void ChangeType(ActionType t)
        {
            RingItem it = Current;
            if (it == null || it.Type == t) return;
            bool autoLabel = it.Label.Length == 0 || it.Label == Actions.MediaShortName(it.Target) || it.Label == it.Target;
            it.Type = t;
            it.Target = t == ActionType.Media ? Actions.MediaIds[0] : "";
            it.Args = "";
            if (autoLabel) it.Label = t == ActionType.Media ? Actions.MediaShortName(it.Target) : "";
            LoadEditor();
            UpdateListText();
        }

        void Browse()
        {
            using (OpenFileDialog d = new OpenFileDialog())
            {
                d.Title = "Kies een programma of snelkoppeling";
                d.Filter = "Programma's en snelkoppelingen|*.exe;*.lnk;*.bat;*.cmd;*.url|Alle bestanden|*.*";
                d.DereferenceLinks = false;
                d.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms);
                if (d.ShowDialog(this) != DialogResult.OK) return;
                RingItem it = Current;
                string oldName = SafeName(it.Target);
                txtTarget.Text = d.FileName;
                if (it.Label.Length == 0 || it.Label == oldName) SetLabel(SafeName(d.FileName));
            }
        }

        static string SafeName(string path)
        {
            try { return Path.GetFileNameWithoutExtension(Actions.ExpandPath(path)); } catch { return ""; }
        }

        void Save()
        {
            for (int i = 0; i <= work.Items.Count; i++)
            {
                RingItem it = i == 0 ? work.Center : work.Items[i - 1];
                string problem = null;
                uint m; Keys k;
                if ((it.Type == ActionType.Program || it.Type == ActionType.Url) && it.Target.Trim().Length == 0)
                    problem = "Vul nog een programma of adres in.";
                else if (it.Type == ActionType.Keys && !KeyCombo.TryParse(it.Target, out m, out k))
                    problem = "Stel nog een toetscombinatie in.";
                if (problem != null)
                {
                    list.SelectedIndex = i;
                    MessageBox.Show(this, ListText(i) + ": " + problem, "QuickCut", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
            }
            string err;
            if (app.ApplyConfig(work.Clone(), chkAutostart.Checked, out err)) Close();
            else MessageBox.Show(this, err, "QuickCut", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    // Read-only text box that records a key combination instead of text.
    class HotkeyBox : TextBox
    {
        readonly bool requireModifier;
        string combo = "";
        public event EventHandler ComboChanged;

        public HotkeyBox(bool requireModifier)
        {
            this.requireModifier = requireModifier;
            ReadOnly = true;
            BackColor = SystemColors.Window;
            ShortcutsEnabled = false;
            Combo = "";
        }

        public string Combo
        {
            get { return combo; }
            set { combo = value ?? ""; Text = combo.Length == 0 ? "(klik en druk toetsen in)" : combo; }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            Keys key = keyData & Keys.KeyCode;
            bool noMods = (keyData & Keys.Modifiers) == 0;
            if (noMods && (key == Keys.Tab || key == Keys.Escape)) return false;
            if (key == Keys.ControlKey || key == Keys.ShiftKey || key == Keys.Menu || key == Keys.LWin || key == Keys.RWin) return true;

            uint mods = 0;
            if ((keyData & Keys.Control) != 0) mods |= KeyCombo.MOD_CONTROL;
            if ((keyData & Keys.Alt) != 0) mods |= KeyCombo.MOD_ALT;
            if ((keyData & Keys.Shift) != 0) mods |= KeyCombo.MOD_SHIFT;
            if (Native.GetKeyState(0x5B) < 0 || Native.GetKeyState(0x5C) < 0) mods |= KeyCombo.MOD_WIN;

            bool fkey = key >= Keys.F1 && key <= Keys.F24;
            if (requireModifier && mods == 0 && !fkey)
            {
                System.Media.SystemSounds.Beep.Play();
                return true;
            }
            Combo = KeyCombo.Format(mods, key);
            if (ComboChanged != null) ComboChanged(this, EventArgs.Empty);
            return true;
        }
    }
}
