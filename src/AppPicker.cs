using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
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
        readonly ListBox list;
        readonly List<AppEntry> all, suggested;

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
                if (e.KeyCode == Keys.Down) { list.Focus(); e.Handled = true; }
                else if (e.KeyCode == Keys.Enter) { SelectFirst(); Accept(); e.Handled = e.SuppressKeyPress = true; }
            };

            list = new ListBox();
            list.Bounds = new Rectangle(S(16), S(60), S(388), S(398));
            list.BorderStyle = BorderStyle.None;
            list.DrawMode = DrawMode.OwnerDrawFixed;
            list.ItemHeight = S(36);
            list.IntegralHeight = false;
            list.BackColor = pal.Card;
            list.ForeColor = pal.Text;
            list.DrawItem += DrawRow;
            list.DoubleClick += delegate { Accept(); };
            list.KeyDown += delegate(object s, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) { Accept(); e.Handled = true; } };
            Controls.Add(list);

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

        protected override void OnShown(EventArgs e) { base.OnShown(e); search.Box.Focus(); }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Escape) DialogResult = DialogResult.Cancel;
        }

        void Fill()
        {
            string q = search.Box.Text.Trim();
            list.BeginUpdate();
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
            list.EndUpdate();
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

        void DrawRow(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            Graphics g = e.Graphics;
            object item = list.Items[e.Index];
            AppEntry a = item as AppEntry;
            bool sel = a != null && (e.State & DrawItemState.Selected) != 0;
            using (SolidBrush b = new SolidBrush(sel ? Theme.Mix(pal.Card, pal.Accent, pal.Dark ? 0.3f : 0.15f) : pal.Card))
                g.FillRectangle(b, e.Bounds);
            if (a == null)
            {
                Rectangle hr = new Rectangle(e.Bounds.X + S(6), e.Bounds.Y + S(10), e.Bounds.Width, e.Bounds.Height - S(10));
                TextRenderer.DrawText(g, (string)item, Font, hr, pal.Muted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
                return;
            }
            float isz = S(24);
            Bitmap icon = Art.PathIcon(a.Path, false);
            RectangleF ir = new RectangleF(e.Bounds.X + S(8), e.Bounds.Y + (e.Bounds.Height - isz) / 2, isz, isz);
            if (icon != null)
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(icon, ir);
            }
            Rectangle tr = new Rectangle((int)ir.Right + S(10), e.Bounds.Y, e.Bounds.Width - S(48), e.Bounds.Height);
            TextRenderer.DrawText(g, a.Name, Font, tr, pal.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }
}
