using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace QuickCut
{
    // A double-buffered list with pixel-smooth, animated scrolling and a slim themed scrollbar.
    // Replaces ListBox in the app picker: ListBox scrolls in whole-row jumps and repaints rows one by one.
    class SmoothList : ThemedControl
    {
        public delegate void RowPainter(Graphics g, int index, Rectangle bounds, bool selected, bool hot);

        public readonly List<object> Items = new List<object>();
        public int ItemHeight = 36;
        public RowPainter PaintRow;
        public Func<object, bool> Selectable = delegate { return true; };
        public event EventHandler Activated; // double click or Enter

        float offset, target;     // current and animated-to scroll position, in pixels
        int selected = -1, hot = -1;
        bool dragThumb;
        float dragStartY, dragStartOffset;
        readonly Timer anim = new Timer();

        public SmoothList()
        {
            SetStyle(ControlStyles.Selectable, true);
            TabStop = true;
            anim.Interval = 10;
            anim.Tick += delegate
            {
                float d = target - offset;
                if (Math.Abs(d) < 0.5f) { offset = target; anim.Stop(); }
                else offset += d * 0.3f;
                UpdateHot(PointToClient(Cursor.Position));
                Invalidate();
            };
        }

        protected override void Dispose(bool disposing) { if (disposing) anim.Dispose(); base.Dispose(disposing); }

        public int SelectedIndex
        {
            get { return selected; }
            set
            {
                selected = value >= 0 && value < Items.Count && Selectable(Items[value]) ? value : -1;
                if (selected >= 0) EnsureVisible(selected);
                Invalidate();
            }
        }

        public object SelectedItem { get { return selected >= 0 ? Items[selected] : null; } }

        // Call after changing Items.
        public void Reset()
        {
            offset = target = 0;
            selected = hot = -1;
            anim.Stop();
            Invalidate();
        }

        float MaxOffset { get { return Math.Max(0, Items.Count * ItemHeight - Height); } }
        float Clamp(float v) { return Math.Max(0, Math.Min(MaxOffset, v)); }

        void ScrollTo(float v, bool animate)
        {
            target = Clamp(v);
            if (animate) anim.Start();
            else { offset = target; anim.Stop(); Invalidate(); }
        }

        void EnsureVisible(int i)
        {
            float top = i * ItemHeight, bottom = top + ItemHeight;
            if (top < target) ScrollTo(top, true);
            else if (bottom > target + Height) ScrollTo(bottom - Height, true);
        }

        int RowAt(int y)
        {
            int i = (int)Math.Floor((y + offset) / ItemHeight);
            return i >= 0 && i < Items.Count ? i : -1;
        }

        // ---------- scrollbar ----------

        int BarW { get { return (int)Math.Round(6 * S); } }
        bool HasBar { get { return MaxOffset > 0; } }

        RectangleF Thumb
        {
            get
            {
                float content = Items.Count * ItemHeight, track = Height - 4 * S;
                float h = Math.Max(24 * S, track * Height / content);
                float y = 2 * S + (track - h) * (MaxOffset > 0 ? offset / MaxOffset : 0);
                return new RectangleF(Width - BarW - 3 * S, y, BarW, h);
            }
        }

        bool OnBar(Point p) { return HasBar && p.X >= Width - BarW - 6 * S; }

        // ---------- painting ----------

        protected override void OnPaint(PaintEventArgs e)
        {
            if (Pal == null) return;
            Graphics g = e.Graphics;
            g.Clear(Pal.Card);
            int first = Math.Max(0, (int)(offset / ItemHeight));
            int rowW = Width - (HasBar ? BarW + 8 * (int)Math.Ceiling(S) : 0);
            for (int i = first; i < Items.Count; i++)
            {
                int y = (int)Math.Round(i * ItemHeight - offset);
                if (y >= Height) break;
                if (PaintRow != null) PaintRow(g, i, new Rectangle(0, y, rowW, ItemHeight), i == selected, i == hot && !dragThumb);
            }
            if (HasBar)
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                bool active = dragThumb || OnBar(PointToClient(Cursor.Position));
                using (SolidBrush b = new SolidBrush(Theme.Mix(Pal.Card, Pal.Text, active ? 0.45f : 0.25f)))
                using (System.Drawing.Drawing2D.GraphicsPath p = Round(Thumb, BarW / 2f))
                    g.FillPath(b, p);
            }
        }

        // ---------- mouse ----------

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            ScrollTo(target - e.Delta / 120f * 3 * ItemHeight, true);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            if (e.Button != MouseButtons.Left) return;
            if (OnBar(e.Location))
            {
                RectangleF t = Thumb;
                if (e.Y < t.Top) ScrollTo(target - Height, true);
                else if (e.Y > t.Bottom) ScrollTo(target + Height, true);
                else { dragThumb = true; dragStartY = e.Y; dragStartOffset = offset; Capture = true; }
                Invalidate();
                return;
            }
            int i = RowAt(e.Y);
            if (i >= 0 && Selectable(Items[i])) { selected = i; Invalidate(); }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (dragThumb)
            {
                float track = Height - 4 * S - Thumb.Height;
                if (track > 0) ScrollTo(dragStartOffset + (e.Y - dragStartY) / track * MaxOffset, false);
                return;
            }
            UpdateHot(e.Location);
            Invalidate();
        }

        void UpdateHot(Point p)
        {
            int i = ClientRectangle.Contains(p) && !OnBar(p) ? RowAt(p.Y) : -1;
            hot = i >= 0 && Selectable(Items[i]) ? i : -1;
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (dragThumb) { dragThumb = false; Capture = false; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e) { hot = -1; base.OnMouseLeave(e); }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            if (e.Button == MouseButtons.Left && !OnBar(e.Location) && RowAt(e.Y) == selected && selected >= 0 && Activated != null)
                Activated(this, EventArgs.Empty);
        }

        // ---------- keyboard ----------

        protected override bool IsInputKey(Keys k)
        {
            switch (k & Keys.KeyCode)
            {
                case Keys.Up: case Keys.Down: case Keys.PageUp: case Keys.PageDown: case Keys.Home: case Keys.End: case Keys.Enter:
                    return true;
            }
            return base.IsInputKey(k);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            int page = Math.Max(1, Height / ItemHeight - 1);
            switch (e.KeyCode)
            {
                case Keys.Down: Step(selected < 0 ? 0 : selected + 1, 1); break;
                case Keys.Up: Step(selected - 1, -1); break;
                case Keys.PageDown: Step(Math.Min(Items.Count - 1, Math.Max(0, selected) + page), 1); break;
                case Keys.PageUp: Step(Math.Max(0, selected - page), -1); break;
                case Keys.Home: Step(0, 1); break;
                case Keys.End: Step(Items.Count - 1, -1); break;
                case Keys.Enter: if (selected >= 0 && Activated != null) Activated(this, EventArgs.Empty); break;
                default: return;
            }
            e.Handled = true;
        }

        // Moves the selection to i, skipping rows that can't be selected (headers) in direction dir.
        void Step(int i, int dir)
        {
            for (; i >= 0 && i < Items.Count; i += dir)
                if (Selectable(Items[i])) { SelectedIndex = i; return; }
            if (dir < 0 && selected >= 0) ScrollTo(0, true); // at the top: show the header above
        }

        protected override void OnResize(EventArgs e) { base.OnResize(e); offset = target = Clamp(target); }
    }
}
