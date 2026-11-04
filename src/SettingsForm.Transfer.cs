using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace QuickCut
{
    // Import / export of the buttons and look (see ConfigTransfer), from the link in General > Other.
    partial class SettingsForm
    {
        ToolStripDropDown transferMenu;

        // A small popup under the link with two full-width rows, drawn like the rest of the window. Not a
        // ContextMenuStrip: its items keep hidden margins for icons, so they never reach the edges.
        void ShowTransferMenu(Control anchor)
        {
            if (transferMenu == null) Disposed += delegate { if (transferMenu != null) transferMenu.Dispose(); };
            else transferMenu.Dispose(); // built fresh each time, so it follows the current theme

            string[] texts = { Str.ExportMenu, Str.ImportMenu };
            Action[] actions = { ExportSettings, ImportSettings };
            int rowH = S(34), pad = S(4), w = S(160);
            foreach (string t in texts) w = Math.Max(w, TextRenderer.MeasureText(t, Font).Width + S(48));

            ToolStripDropDown dd = new ToolStripDropDown();
            dd.Padding = Padding.Empty;
            dd.Renderer = new PlainRenderer();
            BorderPanel box = new BorderPanel(pal);
            box.Size = new Size(w, 2 * pad + rowH * texts.Length);
            for (int i = 0; i < texts.Length; i++)
            {
                MenuRow row = new MenuRow(texts[i]);
                row.Font = Font;
                row.Bounds = new Rectangle(1, pad + i * rowH, w - 2, rowH); // inside the 1 px border
                row.SetPalette(pal);
                Action act = actions[i];
                row.Click += delegate { dd.Close(); act(); };
                box.Controls.Add(row);
            }
            ToolStripControlHost host = new ToolStripControlHost(box);
            host.Padding = host.Margin = Padding.Empty;
            host.AutoSize = false;
            host.Size = box.Size;
            dd.Items.Add(host);
            dd.HandleCreated += delegate { RoundCorners(dd.Handle); };
            transferMenu = dd;
            // Under the link, lined up with its right edge (the link's text is right-aligned).
            dd.Show(anchor, new Point(anchor.Width, anchor.Height + S(2)), ToolStripDropDownDirection.BelowLeft);
        }

        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        static void RoundCorners(IntPtr hwnd)
        {
            try
            {
                int round = 3; // DWMWCP_ROUNDSMALL: the size Windows uses for its own menus
                DwmSetWindowAttribute(hwnd, 33 /*DWMWA_WINDOW_CORNER_PREFERENCE*/, ref round, 4);
            }
            catch { } // before Windows 11: square corners
        }

        // Draws nothing: the hosted panel paints the whole popup.
        class PlainRenderer : ToolStripRenderer { }

        // The popup's background: the card color with a thin border.
        class BorderPanel : Panel
        {
            readonly Palette pal;
            public BorderPanel(Palette pal) { this.pal = pal; BackColor = pal.Card; DoubleBuffered = true; }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                using (Pen p = new Pen(pal.Border)) e.Graphics.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
            }
        }

        // One full-width row: centered text, the hover color under the mouse.
        class MenuRow : ThemedControl
        {
            public MenuRow(string text) { Text = text; Cursor = Cursors.Hand; }

            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.Clear(Hot ? Pal.Hover : Pal.Card);
                DrawCentered(e.Graphics, Text, Font, ClientRectangle, Pal.Text);
            }
        }

        void ExportSettings()
        {
            if (commitTimer.Enabled) Commit(); // the file on disk matches what you see
            using (SaveFileDialog d = new SaveFileDialog())
            {
                d.Title = Str.ExportMenu.TrimEnd('…');
                d.Filter = Str.TransferFilter;
                d.FileName = Str.ExportFileName + ConfigTransfer.Extension;
                d.DefaultExt = ConfigTransfer.Extension.Substring(1);
                d.AddExtension = true;
                if (d.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    ConfigTransfer.Export(work, d.FileName);
                    SetStatus(string.Format(Str.Exported, Path.GetFileName(d.FileName)), "muted");
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, string.Format(Str.ExportFailed, ex.Message), "QuickCut", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        void ImportSettings()
        {
            string path;
            using (OpenFileDialog d = new OpenFileDialog())
            {
                d.Title = Str.ImportMenu.TrimEnd('…');
                d.Filter = Str.TransferFilter;
                if (d.ShowDialog(this) != DialogResult.OK) return;
                path = d.FileName;
            }

            ConfigTransfer.ImportResult r;
            Cursor = Cursors.WaitCursor; // the app list may still be loading (about half a second)
            try { r = ConfigTransfer.Import(path, AppCatalog.Cached()); }
            finally { Cursor = Cursors.Default; }
            if (r.Error != null)
            {
                MessageBox.Show(this, r.Error, "QuickCut", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string msg = string.Format(Str.ImportConfirm, Path.GetFileName(path), r.Config.Items.Count);
            if (r.Missing.Count > 0) msg += string.Format(Str.ImportMissing, string.Join(", ", r.Missing.ToArray()));
            if (MessageBox.Show(this, msg, "QuickCut", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

            ConfigTransfer.ApplyTo(r.Config, work);
            // Show the new look in the Appearance tab, the same way "Reset to defaults" does.
            loading = true;
            themeSeg.SelectedIndex = Math.Max(0, Array.IndexOf(ThemeIds, work.ThemeMode));
            hoverSeg.SelectedIndex = Math.Max(0, Array.IndexOf(HoverIds, work.HoverStyle));
            sizeSeg.SelectedIndex = Math.Max(0, Array.IndexOf(SizeIds, work.RingSize));
            allColor.Value = work.ButtonColor;
            accentColor.Value = work.Accent;
            loading = false;
            preview.Selected = work.Items.Count > 0 ? 0 : RingRenderer.HitCenter;
            ApplyTheme();
            LoadEditor();
            Changed(); // applies to the ring and saves, like any other edit
        }
    }
}
