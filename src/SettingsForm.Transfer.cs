using System;
using System.Drawing;
using System.IO;
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
                transferMenu.Font = Font;
                transferMenu.Items.Add(Str.ExportMenu, null, delegate { ExportSettings(); });
                transferMenu.Items.Add(Str.ImportMenu, null, delegate { ImportSettings(); });
                Disposed += delegate { transferMenu.Dispose(); };
            }
            // Under the link, lined up with its right edge (the link's text is right-aligned).
            transferMenu.Show(anchor, new Point(anchor.Width, anchor.Height), ToolStripDropDownDirection.BelowLeft);
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
