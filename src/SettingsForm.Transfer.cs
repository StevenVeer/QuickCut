using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace QuickCut
{
    // Import / export of the buttons and look (see ConfigTransfer), from the link in General > Other.
    partial class SettingsForm
    {
        ContextMenuStrip transferMenu;

        void ShowTransferMenu(Control anchor)
        {
            if (transferMenu == null)
            {
                transferMenu = new ContextMenuStrip();
                transferMenu.ShowImageMargin = false;
                transferMenu.Padding = new Padding(S(4));
                transferMenu.Font = Font;
                foreach (ToolStripMenuItem it in new[] {
                    new ToolStripMenuItem(Str.ExportMenu, null, delegate { ExportSettings(); }),
                    new ToolStripMenuItem(Str.ImportMenu, null, delegate { ImportSettings(); }) })
                {
                    it.Padding = new Padding(S(6), S(7), S(18), S(7));
                    transferMenu.Items.Add(it);
                }
                transferMenu.HandleCreated += delegate { MenuRenderer.RoundCorners(transferMenu); };
                Disposed += delegate { transferMenu.Dispose(); };
            }
            // Follows the current theme, which may have changed since the last time.
            transferMenu.Renderer = new MenuRenderer(pal, sc);
            transferMenu.BackColor = pal.Card;
            transferMenu.ForeColor = pal.Text;
            // Under the link, lined up with its right edge (the link's text is right-aligned).
            transferMenu.Show(anchor, new Point(anchor.Width, anchor.Height + S(2)), ToolStripDropDownDirection.BelowLeft);
        }

        // Draws a menu like the rest of the settings window: the card color, a thin border and a rounded
        // hover highlight, in light and dark mode. On Windows 11 the menu window itself gets rounded corners.
        class MenuRenderer : ToolStripRenderer
        {
            readonly Palette pal;
            readonly float sc;

            public MenuRenderer(Palette pal, float sc) { this.pal = pal; this.sc = sc; }

            [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

            public static void RoundCorners(Control menu)
            {
                try
                {
                    int round = 3; // DWMWCP_ROUNDSMALL: the size Windows uses for its own menus
                    DwmSetWindowAttribute(menu.Handle, 33 /*DWMWA_WINDOW_CORNER_PREFERENCE*/, ref round, 4);
                }
                catch { } // before Windows 11: square corners
            }

            protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
            {
                e.Graphics.Clear(pal.Card);
            }

            protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
            {
                using (Pen p = new Pen(pal.Border))
                    e.Graphics.DrawRectangle(p, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
            }

            protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
            {
                if (!e.Item.Selected || !e.Item.Enabled) return;
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                RectangleF r = new RectangleF(1, 1, e.Item.Width - 2, e.Item.Height - 2);
                using (GraphicsPath p = ThemedControl.Round(r, 5 * sc))
                using (SolidBrush b = new SolidBrush(pal.Hover))
                    g.FillPath(b, p);
            }

            protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
            {
                e.TextColor = e.Item.Enabled ? pal.Text : pal.Muted;
                base.OnRenderItemText(e);
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
