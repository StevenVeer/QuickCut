using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace QuickCut
{
    // Import / export of the buttons and look (see ConfigTransfer), from the tiles on the General page;
    // and the popup parts the mixer's "Add app" menu is built from.
    partial class SettingsForm
    {
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
            // Show the new look on the Look page, the same way "Reset to defaults" does.
            SyncLook();
            preview.Selected = work.Items.Count > 0 ? 0 : RingRenderer.HitCenter;
            ApplyTheme();
            LoadEditor();
            Changed(); // applies to the ring and saves, like any other edit
        }
    }
}
