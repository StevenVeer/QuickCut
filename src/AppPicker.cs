using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading;
using System.Windows.Forms;

namespace QuickCut
{
    // Searchable list of installed apps, with suggested ones on top.
    class AppPicker : Form
    {
        public AppEntry Result;

        readonly Palette pal;
        readonly float sc;
        readonly FieldBox search;
        readonly SmoothList list;
        readonly List<AppEntry> all, suggested;
        // Row icons, already shrunk to row size. Filled by a background thread so scrolling never waits on the shell.
        readonly Dictionary<string, Bitmap> icons = new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);
        volatile bool closed;

        public AppPicker(Palette pal)
        {
            this.pal = pal;
            sc = ThemedControl.S;
            Text = Str.PickAppTitle;
            Font = new Font("Segoe UI", 9.5f * 96f / 72f * sc, GraphicsUnit.Pixel); // pixels: follows the monitor scale
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = new Size(S(420), S(520));
            BackColor = pal.Window;
            KeyPreview = true;

            Cursor = Cursors.WaitCursor;
            all = AppCatalog.Cached();
            suggested = AppCatalog.Suggested(all);
            Cursor = Cursors.Default;

            search = new FieldBox(new TextBox());
            search.Bounds = new Rectangle(S(16), S(16), S(388), S(34));
            Controls.Add(search);
            search.SetPalette(pal);
            search.Box.TextChanged += delegate { Fill(); };
            search.Box.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Down) { list.Focus(); if (list.SelectedIndex < 0) SelectFirst(); e.Handled = e.SuppressKeyPress = true; }
                else if (e.KeyCode == Keys.Enter) { if (list.SelectedIndex < 0) SelectFirst(); Accept(); e.Handled = e.SuppressKeyPress = true; }
            };

            list = new SmoothList();
            list.Bounds = new Rectangle(S(16), S(60), S(388), S(398));
            list.ItemHeight = S(36);
            list.Selectable = delegate(object o) { return o is AppEntry; };
            list.PaintRow = DrawRow;
            list.Activated += delegate { Accept(); };
            Controls.Add(list);
            list.SetPalette(pal);

            FlatButton ok = new FlatButton(Str.Select, FlatButton.Kind.Normal);
            ok.Bounds = new Rectangle(S(204), S(472), S(96), S(34));
            ok.Click += delegate { Accept(); };
            FlatButton cancel = new FlatButton(Str.Cancel, FlatButton.Kind.Normal);
            cancel.Bounds = new Rectangle(S(308), S(472), S(96), S(34));
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; };
            Controls.Add(ok);
            Controls.Add(cancel);
            ok.SetPalette(pal);
            cancel.SetPalette(pal);

            Fill();
        }

        int S(float v) { return (int)Math.Round(v * sc); }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.TitleBar(Handle, pal.Dark);
            search.SetCue(Str.SearchApps);
        }

        protected override void OnShown(EventArgs e) { base.OnShown(e); search.Box.Focus(); StartIconLoader(); }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            closed = true;
            foreach (Bitmap b in icons.Values) if (b != null) b.Dispose();
            icons.Clear();
            base.OnFormClosed(e);
        }

        // Loads every icon on an STA thread (the shell needs one), suggested apps first, and hands
        // them to the UI thread in small batches.
        void StartIconLoader()
        {
            List<string> paths = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (AppEntry a in suggested) if (seen.Add(a.Path)) paths.Add(a.Path);
            foreach (AppEntry a in all) if (seen.Add(a.Path)) paths.Add(a.Path);
            int isz = S(24);
            Thread t = new Thread(delegate()
            {
                List<KeyValuePair<string, Bitmap>> batch = new List<KeyValuePair<string, Bitmap>>();
                for (int i = 0; i < paths.Count && !closed; i++)
                {
                    batch.Add(new KeyValuePair<string, Bitmap>(paths[i], Shrink(Art.LoadPathIcon(paths[i], false), isz)));
                    if (batch.Count >= 12 || i == paths.Count - 1) { Deliver(batch); batch = new List<KeyValuePair<string, Bitmap>>(); }
                }
            });
            t.IsBackground = true;
            t.SetApartmentState(ApartmentState.STA);
            t.Start();
        }

        void Deliver(List<KeyValuePair<string, Bitmap>> batch)
        {
            try
            {
                BeginInvoke((Action)delegate
                {
                    foreach (KeyValuePair<string, Bitmap> kv in batch)
                    {
                        if (closed || icons.ContainsKey(kv.Key)) { if (kv.Value != null) kv.Value.Dispose(); }
                        else icons[kv.Key] = kv.Value;
                    }
                    if (!closed) list.Invalidate();
                });
            }
            catch (InvalidOperationException) { foreach (KeyValuePair<string, Bitmap> kv in batch) if (kv.Value != null) kv.Value.Dispose(); }
        }

        static Bitmap Shrink(Bitmap src, int size)
        {
            if (src == null) return null;
            Bitmap b = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            using (Graphics g = Graphics.FromImage(b))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(src, 0, 0, size, size);
            }
            src.Dispose();
            return b;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Escape) DialogResult = DialogResult.Cancel;
        }

        void Fill()
        {
            string q = search.Box.Text.Trim();
            list.Items.Clear();
            if (q.Length == 0)
            {
                if (suggested.Count > 0)
                {
                    list.Items.Add(Str.Suggested);
                    foreach (AppEntry a in suggested) list.Items.Add(a);
                }
                list.Items.Add(Str.AllApps);
                foreach (AppEntry a in all) list.Items.Add(a);
            }
            else
                foreach (AppEntry a in all)
                    if (a.Name.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0) list.Items.Add(a);
            list.Reset();
        }

        void SelectFirst()
        {
            for (int i = 0; i < list.Items.Count; i++)
                if (list.Items[i] is AppEntry) { list.SelectedIndex = i; return; }
        }

        void Accept()
        {
            AppEntry a = list.SelectedItem as AppEntry;
            if (a == null) return;
            Result = a;
            DialogResult = DialogResult.OK;
        }

        void DrawRow(Graphics g, int index, Rectangle r, bool sel, bool hot)
        {
            object item = list.Items[index];
            AppEntry a = item as AppEntry;
            if (a == null)
            {
                Rectangle hr = new Rectangle(r.X + S(6), r.Y + S(10), r.Width, r.Height - S(10));
                TextRenderer.DrawText(g, (string)item, Font, hr, pal.Muted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
                return;
            }
            if (sel || hot)
            {
                Color c = sel ? Theme.Mix(pal.Card, pal.Accent, pal.Dark ? 0.3f : 0.15f) : Theme.Mix(pal.Card, pal.Text, 0.06f);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (SolidBrush b = new SolidBrush(c))
                using (GraphicsPath p = ThemedControl.Round(new RectangleF(r.X + 2, r.Y + 1, r.Width - 4, r.Height - 2), S(6)))
                    g.FillPath(b, p);
            }
            int isz = S(24);
            Bitmap icon; // not loaded yet: the row shows without it and the loader repaints later
            Rectangle ir = new Rectangle(r.X + S(8), r.Y + (r.Height - isz) / 2, isz, isz);
            if (icons.TryGetValue(a.Path, out icon) && icon != null) g.DrawImageUnscaled(icon, ir.X, ir.Y);
            Rectangle tr = new Rectangle(ir.Right + S(10), r.Y, r.Right - ir.Right - S(14), r.Height);
            TextRenderer.DrawText(g, a.Name, Font, tr, pal.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }
}
