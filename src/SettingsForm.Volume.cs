using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace QuickCut
{
    // The Volume page: the volume and song panels, where they sit around the ring, and the apps that
    // get their own volume row (the mixer).
    partial class SettingsForm
    {
        ToggleTile tileVolume, tileNow;
        ChoiceTile[] placeTiles;
        Segmented stepSeg;
        Card placeCard, stepCard, mixerCard;
        FlatButton btnAddApp;
        Label lblMixerFooter;
        MixerList mixerList;
        ToolStripDropDown mixerMenu;

        void BuildVolumePage()
        {
            Panel p = pnlVolume;
            tileVolume = Add(p, new ToggleTile("nav-volume", Str.VolumeTile, Str.VolumeTileDesc), 0, 0, 194, 110);
            tileVolume.Checked = work.VolumeBar;
            tileVolume.CheckedChanged += delegate
            {
                if (loading) return;
                work.VolumeBar = tileVolume.Checked;
                LayoutVolumePage();
                Changed();
            };
            tileNow = Add(p, new ToggleTile("music", Str.NowPlaying, Str.NowPlayingDesc), 206, 0, 194, 110);
            tileNow.Checked = work.MediaBar;
            tileNow.CheckedChanged += delegate { if (!loading) { work.MediaBar = tileNow.Checked; Changed(); } };

            placeCard = AddCard(p);
            AddLabel(placeCard, Str.Placement, 16, 14, 180, 22, "title");
            AddLabel(placeCard, Str.PlacementDesc, 184, 16, 200, 18, "desc right");
            placeTiles = new ChoiceTile[4];
            for (int i = 0; i < 4; i++)
            {
                string id = Config.PanelPositions[i];
                placeTiles[i] = Add(placeCard, new ChoiceTile(Str.PanelPositionOptions[i], PlacePicture(id)), 16 + i * 94, 44, 86, 74);
                placeTiles[i].PictureHeight = 30;
                placeTiles[i].Selected = work.PanelPos == id;
                placeTiles[i].Click += delegate
                {
                    work.PanelPos = id;
                    for (int k = 0; k < 4; k++) placeTiles[k].Selected = Config.PanelPositions[k] == id;
                    Changed();
                };
            }

            stepCard = AddCard(p);
            AddLabel(stepCard, Str.VolumeStep, 16, 10, 230, 22, "title");
            AddLabel(stepCard, Str.StepDesc, 16, 32, 250, 18, "desc");
            stepSeg = Add(stepCard, new Segmented(Str.VolumeStepOptions), 272, 14, 112, 34);
            stepSeg.SelectedIndex = work.VolumeStep == 10 ? 1 : 0;
            stepSeg.SelectedIndexChanged += delegate { if (!loading) { work.VolumeStep = stepSeg.SelectedIndex == 1 ? 10 : 5; Changed(); } };

            mixerCard = AddCard(p);
            AddLabel(mixerCard, Str.MixerSection, 16, 14, 250, 22, "title");
            AddLabel(mixerCard, Str.MixerDesc, 16, 36, 260, 18, "desc");
            btnAddApp = Add(mixerCard, new FlatButton(Str.AddApp, FlatButton.Kind.Accent), 280, 18, 104, 32);
            btnAddApp.IconName = "plus";
            btnAddApp.Click += delegate { ShowMixerMenu(btnAddApp); };
            mixerList = Add(mixerCard, new MixerList(), 1, 64, ColW - 2, 46);
            mixerList.Apps = work.MixerApps;
            mixerList.Live = delegate { return preview.Live(); };
            mixerList.Removed += delegate(int i) { work.MixerApps.RemoveAt(i); MixerChanged(); };
            mixerList.Reordered += delegate { MixerChanged(); };
            lblMixerFooter = AddLabel(mixerCard, "", 16, 0, 368, 36, "desc");
            lblMixerFooter.AutoEllipsis = false;
            LayoutVolumePage();
        }

        void LayoutVolumePage()
        {
            float y = 110 + CardGap;
            Place(placeCard, 0, y, ColW, 132); y += 132 + CardGap;
            Place(stepCard, 0, y, ColW, 62); y += 62 + CardGap;
            float listH = 46 * Math.Max(1, work.MixerApps.Count);
            Place(mixerList, 1, 64, ColW - 2, listH);
            Place(lblMixerFooter, 16, 64 + listH + 10, 368, 36);
            lblMixerFooter.Text = work.VolumeBar ? Str.MixerFooter : Str.MixerNeedsVolume;
            float cardH = 64 + listH + 56;
            Place(mixerCard, 0, y, ColW, cardH);
            volumeH = y + cardH;
        }

        void MixerChanged()
        {
            LayoutVolumePage();
            mixerList.Invalidate();
            preview.RefreshMixer();
            Changed();
        }

        // The ring and where its panels go, as a small picture.
        static Action<Graphics, RectangleF, Color> PlacePicture(string id)
        {
            return delegate(Graphics g, RectangleF r, Color ink)
            {
                // Drawn on a 44 x 36 grid, centered.
                float k = r.Height / 36f, ox = r.X + (r.Width - 44 * k) / 2, oy = r.Y;
                float cx = id == "left" ? 30 : id == "right" ? 14 : 22, cy = id == "below" ? 13 : id == "above" ? 23 : 18;
                RectangleF bar = id == "below" ? new RectangleF(13, 25, 18, 4) : id == "above" ? new RectangleF(13, 7, 18, 4)
                    : id == "left" ? new RectangleF(4, 13, 4, 10) : new RectangleF(36, 13, 4, 10);
                using (Pen pen = new Pen(ink, 2.4f * k)) g.DrawEllipse(pen, ox + (cx - 9) * k, oy + (cy - 9) * k, 18 * k, 18 * k);
                using (GraphicsPath p = ThemedControl.Round(new RectangleF(ox + bar.X * k, oy + bar.Y * k, bar.Width * k, bar.Height * k), 2 * k))
                using (SolidBrush b = new SolidBrush(ink)) g.FillPath(b, p);
            };
        }

        void AddMixerApp(MixerApp m)
        {
            foreach (MixerApp x in work.MixerApps)
                if (string.Equals(x.Match, m.Match, StringComparison.OrdinalIgnoreCase)) return; // already there
            if (work.MixerApps.Count >= Config.MaxMixerApps)
            {
                MessageBox.Show(this, string.Format(Str.MixerFull, Config.MaxMixerApps), "QuickCut", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            work.MixerApps.Add(m);
            MixerChanged();
        }

        void PickInstalledMixerApp()
        {
            using (AppPicker picker = new AppPicker(pal))
            {
                if (picker.ShowDialog(this) != DialogResult.OK || picker.Result == null) return;
                string match = MixerApp.MatchFor(picker.Result.Path);
                if (match == null)
                {
                    MessageBox.Show(this, string.Format(Str.MixerCantMatch, picker.Result.Name), "QuickCut", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                AddMixerApp(new MixerApp(picker.Result.Name, match, picker.Result.Path));
            }
        }

        // A popup under the button: the apps playing sound now (not in the list yet), then the installed
        // apps. Built like the import / export menu.
        void ShowMixerMenu(Control anchor)
        {
            if (mixerMenu == null) Disposed += delegate { if (mixerMenu != null) mixerMenu.Dispose(); };
            else mixerMenu.Dispose();

            List<MixerApp> running = new List<MixerApp>();
            Cursor = Cursors.WaitCursor;
            try
            {
                foreach (MixerApp r in AppVolume.Running())
                {
                    bool have = false;
                    foreach (MixerApp x in work.MixerApps) if (x.MatchesProcess(r.Icon)) { have = true; break; }
                    if (!have && running.Count < 8) running.Add(r);
                }
            }
            finally { Cursor = Cursors.Default; }

            int rowH = S(36), headH = S(28), pad = S(6), w = S(260);
            ToolStripDropDown dd = new ToolStripDropDown();
            dd.Padding = Padding.Empty;
            dd.Renderer = new PlainRenderer();
            BorderPanel box = new BorderPanel(pal);
            int y = pad;
            AppMenuRow head = new AppMenuRow(Str.MixerPlayingNow, null, null, true);
            head.Bounds = new Rectangle(pad, y, w - 2 * pad, headH);
            box.Controls.Add(head);
            y += headH;
            if (running.Count == 0)
            {
                AppMenuRow none = new AppMenuRow(Str.MixerNothingPlaying, null, null, true);
                none.Bounds = new Rectangle(pad, y, w - 2 * pad, rowH);
                box.Controls.Add(none);
                y += rowH;
            }
            foreach (MixerApp r in running)
            {
                AppMenuRow row = new AppMenuRow(r.Name, r.Icon, null, false);
                row.Bounds = new Rectangle(pad, y, w - 2 * pad, rowH);
                MixerApp pick = r;
                row.Click += delegate { dd.Close(); AddMixerApp(pick); };
                box.Controls.Add(row);
                y += rowH;
            }
            Separator sep = new Separator();
            sep.Bounds = new Rectangle(pad + S(4), y + S(5), w - 2 * pad - S(8), Math.Max(1, S(1)));
            box.Controls.Add(sep);
            y += S(11);
            AppMenuRow installed = new AppMenuRow(Str.MixerInstalled, null, "apps", false);
            installed.Bounds = new Rectangle(pad, y, w - 2 * pad, rowH);
            installed.Click += delegate { dd.Close(); BeginInvoke((Action)PickInstalledMixerApp); };
            box.Controls.Add(installed);
            y += rowH + pad;
            box.Size = new Size(w, y);
            foreach (Control c in box.Controls) { c.Font = Font; ((ThemedControl)c).SetPalette(pal); }

            ToolStripControlHost host = new ToolStripControlHost(box);
            host.Padding = host.Margin = Padding.Empty;
            host.AutoSize = false;
            host.Size = box.Size;
            dd.Items.Add(host);
            dd.HandleCreated += delegate { RoundCorners(dd.Handle); };
            mixerMenu = dd;
            dd.Show(anchor, new Point(anchor.Width, anchor.Height + S(4)), ToolStripDropDownDirection.BelowLeft);
        }

        // A menu row with an app icon (or a line icon) and left-aligned text; a header is muted and can't
        // be clicked.
        class AppMenuRow : ThemedControl
        {
            readonly string iconPath, lineIcon;
            readonly bool header;

            public AppMenuRow(string text, string iconPath, string lineIcon, bool header)
            {
                Text = text;
                this.iconPath = iconPath;
                this.lineIcon = lineIcon;
                this.header = header;
                if (!header) Cursor = Cursors.Hand;
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.Clear(Pal.Card);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                if (Hot && !header)
                    using (GraphicsPath p = Round(new RectangleF(0, 0, Width - 1, Height - 1), 8 * S))
                    using (SolidBrush b = new SolidBrush(Pal.Hover)) g.FillPath(b, p);
                int x = (int)(10 * S);
                int isz = (int)(20 * S);
                if (iconPath != null)
                {
                    Bitmap icon = Art.PathIcon(iconPath, false);
                    if (icon != null)
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.DrawImage(icon, new Rectangle(x, (Height - isz) / 2, isz, isz));
                    }
                    x += isz + (int)(10 * S);
                }
                else if (lineIcon != null)
                {
                    Icons.Draw(g, lineIcon, new RectangleF(x + 1 * S, (Height - 18 * S) / 2, 18 * S, 18 * S), Pal.Text);
                    x += isz + (int)(10 * S);
                }
                Rectangle tr = new Rectangle(x, 0, Width - x - (int)(10 * S), Height);
                TextRenderer.DrawText(g, Text, header ? Ui.Font(11, false) : Ui.Font(13, false), tr, header ? Pal.Muted : Pal.Text, Ui.Left);
            }
        }

        // A thin line between menu sections.
        class Separator : ThemedControl
        {
            protected override void OnPaint(PaintEventArgs e) { e.Graphics.Clear(Pal.Border); }
        }
    }

    // The apps in the mixer: a handle to drag them into another order, the icon and name, how loud the
    // app is right now, and a button to remove it.
    class MixerList : ThemedControl
    {
        public List<MixerApp> Apps;
        public Func<List<AppVolume.AppSessions>> Live; // the apps' sound right now
        public event Action<int> Removed;
        public event EventHandler Reordered;
        int hotRow = -1;
        bool hotRemove;
        int dragRow = -1, dropAt = -1; // the row being dragged, and where it would land (0..Count)
        bool dragging;
        int pressY;

        int RowH { get { return (int)Math.Round(46 * S); } }

        Rectangle RemoveRect(int row)
        {
            int s = (int)Math.Round(30 * S);
            return new Rectangle(Width - s - (int)(10 * S), row * RowH + (RowH - s) / 2, s, s);
        }

        int RowAt(int y) { return Apps == null || y < 0 ? -1 : Math.Min(y / Math.Max(1, RowH), Apps.Count - 1); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left || Apps == null) return;
            int row = RowAt(e.Y);
            if (row < 0 || RemoveRect(row).Contains(e.Location)) return;
            dragRow = row;
            pressY = e.Y;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (Apps == null) return;
            if (dragRow >= 0 && e.Button == MouseButtons.Left)
            {
                if (!dragging && Math.Abs(e.Y - pressY) > 4 * S) dragging = true;
                if (dragging)
                {
                    int at = Math.Max(0, Math.Min(Apps.Count, (e.Y + RowH / 2) / Math.Max(1, RowH)));
                    if (at != dropAt) { dropAt = at; Invalidate(); }
                    Cursor = Cursors.SizeNS;
                    return;
                }
            }
            int r = e.Y < Apps.Count * RowH ? RowAt(e.Y) : -1;
            bool rem = r >= 0 && RemoveRect(r).Contains(e.Location);
            if (r != hotRow || rem != hotRemove) { hotRow = r; hotRemove = rem; Invalidate(); }
            Cursor = rem ? Cursors.Hand : r >= 0 && e.X < 30 * S ? Cursors.SizeNS : Cursors.Default;
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (dragging && dragRow >= 0 && dropAt >= 0 && dropAt != dragRow && dropAt != dragRow + 1)
            {
                MixerApp m = Apps[dragRow];
                Apps.RemoveAt(dragRow);
                Apps.Insert(dropAt > dragRow ? dropAt - 1 : dropAt, m);
                if (Reordered != null) Reordered(this, EventArgs.Empty);
            }
            dragging = false;
            dragRow = dropAt = -1;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e) { if (!dragging) { hotRow = -1; hotRemove = false; } base.OnMouseLeave(e); }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (e.Button != MouseButtons.Left || Apps == null) return;
            int row = RowAt(e.Y);
            if (row >= 0 && RemoveRect(row).Contains(e.Location))
            {
                hotRow = -1; hotRemove = false;
                if (Removed != null) Removed(row);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = Prepare(e);
            int n = Apps == null ? 0 : Apps.Count;
            if (n == 0)
            {
                using (Pen line = new Pen(Pal.Border)) g.DrawLine(line, 0, 0, Width, 0);
                TextRenderer.DrawText(g, Str.MixerEmpty, Ui.Font(13, false), new Rectangle((int)(16 * S), 0, Width - (int)(32 * S), RowH), Pal.Muted, Ui.Left);
                return;
            }
            List<AppVolume.AppSessions> live = Live != null ? Live() : null;
            int isz = (int)Math.Round(22 * S);
            for (int i = 0; i < n; i++)
            {
                MixerApp a = Apps[i];
                int y = i * RowH;
                if (i == dragRow && dragging)
                    using (SolidBrush b = new SolidBrush(Pal.Field)) g.FillRectangle(b, 0, y, Width, RowH);
                else if (i == hotRow && !dragging)
                    using (SolidBrush b = new SolidBrush(Theme.Mix(Pal.Card, Pal.Text, 0.03f))) g.FillRectangle(b, 0, y, Width, RowH);
                using (Pen line = new Pen(Pal.Border)) g.DrawLine(line, 0, y, Width, y);

                // Handle: six dots
                using (SolidBrush dot = new SolidBrush(i == hotRow ? Pal.Text : Pal.Muted))
                    for (int k = 0; k < 6; k++)
                    {
                        float dx = 14 * S + (k % 2) * 5 * S, dy = y + RowH / 2f - 5 * S + (k / 2) * 5 * S, d = 3f * S;
                        g.FillEllipse(dot, dx - d / 2, dy - d / 2, d, d);
                    }

                Bitmap icon = a.Icon.Length > 0 ? Art.PathIcon(a.Icon, false) : null;
                AppVolume.AppSessions s = null;
                if (live != null) foreach (AppVolume.AppSessions x in live) if (x.App == a) { s = x; break; }
                if (icon == null && s != null) icon = Art.PathIcon(s.ProcessPath, false);
                int x0 = (int)(34 * S);
                if (icon != null)
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.DrawImage(icon, new Rectangle(x0, y + (RowH - isz) / 2, isz, isz));
                }
                int tx = x0 + isz + (int)(12 * S);
                Rectangle rr = RemoveRect(i);

                // How loud: a bar and the percentage, or why there's nothing to show.
                float level; bool muted;
                string state = s == null ? Str.AppNotRunning : !s.TryGet(out level, out muted) ? Str.AppWaiting : null;
                int right = rr.Left - (int)(8 * S);
                int nameRight;
                if (state != null)
                {
                    int sw = TextRenderer.MeasureText(g, state, Ui.Font(12, false), new Size(1000, 100), TextFormatFlags.NoPadding).Width + 4;
                    TextRenderer.DrawText(g, state, Ui.Font(12, false), new Rectangle(right - sw, y, sw, RowH), Pal.Muted, Ui.Left);
                    nameRight = right - sw - (int)(10 * S);
                }
                else
                {
                    s.TryGet(out level, out muted);
                    int pct = (int)Math.Round(level * 100);
                    string t = muted ? Str.VolumeMuted : string.Format(Str.AppLevel, pct);
                    int pw = (int)(44 * S);
                    TextRenderer.DrawText(g, t, Ui.Font(12, false), new Rectangle(right - pw, y, pw, RowH), Pal.Muted,
                        TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                    RectangleF bar = new RectangleF(right - pw - 90 * S, y + RowH / 2f - 3 * S, 84 * S, 6 * S);
                    using (GraphicsPath p = Round(bar, 3 * S)) using (SolidBrush b = new SolidBrush(Pal.Track)) g.FillPath(b, p);
                    float fw = muted ? 0 : bar.Width * Math.Max(0, Math.Min(1, level));
                    if (fw > 1)
                        using (GraphicsPath p = Round(new RectangleF(bar.X, bar.Y, fw, bar.Height), 3 * S))
                        using (SolidBrush b = new SolidBrush(Pal.Accent)) g.FillPath(b, p);
                    nameRight = (int)bar.X - (int)(10 * S);
                }
                TextRenderer.DrawText(g, a.Name, Ui.Font(13, false), new Rectangle(tx, y, Math.Max(10, nameRight - tx), RowH), Pal.Text, Ui.Left);

                // × to remove
                if (i == hotRow && hotRemove)
                    using (GraphicsPath p = Round(rr, 8 * S))
                    using (SolidBrush b = new SolidBrush(Pal.Hover)) g.FillPath(b, p);
                Icons.Get("x").Draw(g, RectangleF.Inflate(rr, -8 * S, -8 * S), i == hotRow ? Pal.Text : Pal.Muted, 2f, false);
            }
            // Where the dragged row would land
            if (dragging && dropAt >= 0)
            {
                float ly = Math.Max(1.5f * S, Math.Min(n * RowH - 1.5f * S, dropAt * RowH));
                using (Pen pen = new Pen(Pal.Accent, 3 * S)) g.DrawLine(pen, 10 * S, ly, Width - 10 * S, ly);
            }
        }
    }
}
