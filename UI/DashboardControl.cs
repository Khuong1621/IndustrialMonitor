using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using IndustrialMonitor.Models;

namespace IndustrialMonitor.UI
{
    /// <summary>
    /// Custom UserControl hiển thị dashboard thiết bị
    /// Thể hiện: Custom UserControl, Owner-draw, double buffering
    /// </summary>
    public class DashboardControl : UserControl
    {
        // ---- Fields ----
        private readonly List<DeviceStatusTile> _tiles = new List<DeviceStatusTile>();
        private readonly System.Windows.Forms.Timer _blinkTimer;
        private bool _blinkState = false;

        // ---- Events ----
        public event EventHandler<DeviceModel> DeviceSelected;

        public DashboardControl()
        {
            // Double buffering: tránh flicker khi redraw
            this.DoubleBuffered = true;
            this.SetStyle(ControlStyles.OptimizedDoubleBuffer |
                          ControlStyles.AllPaintingInWmPaint |
                          ControlStyles.UserPaint, true);

            this.BackColor = Color.FromArgb(30, 30, 30);

            // Blink timer cho alarm tiles
            _blinkTimer = new System.Windows.Forms.Timer { Interval = 600 };
            _blinkTimer.Tick += (s, e) =>
            {
                _blinkState = !_blinkState;
                this.Invalidate();   // Redraw toàn bộ control
            };
            _blinkTimer.Start();

            this.Resize += (s, e) => ArrangeTiles();
        }

        // ---- Public API ----

        public void AddDevice(DeviceModel model)
        {
            var tile = new DeviceStatusTile(model);
            _tiles.Add(tile);
            ArrangeTiles();
            this.Invalidate();
        }

        public void UpdateDevice(int deviceId, DeviceStatus newStatus, string lastValue = "")
        {
            var tile = _tiles.Find(t => t.Model.Id == deviceId);
            if (tile != null)
            {
                tile.Model.Status = newStatus;
                tile.Model.LastSeen = DateTime.Now;
                tile.LastValue = lastValue;
                this.Invalidate();
            }
        }

        public void ClearAll()
        {
            _tiles.Clear();
            this.Invalidate();
        }

        // ---- Layout ----

        private const int TileW = 160, TileH = 100, TileGap = 10;

        private void ArrangeTiles()
        {
            int x = TileGap, y = TileGap;
            int cols = Math.Max(1, (this.Width - TileGap) / (TileW + TileGap));
            int col = 0;

            foreach (var tile in _tiles)
            {
                tile.Bounds = new Rectangle(x, y, TileW, TileH);
                col++;
                if (col >= cols) { col = 0; x = TileGap; y += TileH + TileGap; }
                else x += TileW + TileGap;
            }
        }

        // ---- Paint ----

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            foreach (var tile in _tiles)
                DrawTile(g, tile);
        }

        private void DrawTile(Graphics g, DeviceStatusTile tile)
        {
            var r = tile.Bounds;

            // Background color by status
            Color bg;
            if (tile.Model.Status == DeviceStatus.Online)
                bg = Color.FromArgb(20, 80, 20);
            else if (tile.Model.Status == DeviceStatus.Warning)
                bg = _blinkState ? Color.FromArgb(80, 60, 0) : Color.FromArgb(60, 40, 0);
            else if (tile.Model.Status == DeviceStatus.Error)
                bg = _blinkState ? Color.FromArgb(100, 10, 10) : Color.FromArgb(60, 10, 10);
            else
                bg = Color.FromArgb(50, 50, 50);

            // Fill rounded rectangle
            using (var brush = new SolidBrush(bg))
                g.FillRectangle(brush, r);

            // Border
            Color border = tile.Model.Status == DeviceStatus.Online ? Color.LimeGreen :
                           tile.Model.Status == DeviceStatus.Error ? Color.Red :
                           tile.Model.Status == DeviceStatus.Warning ? Color.Orange : Color.Gray;

            using (var pen = new Pen(border, 1.5f))
                g.DrawRectangle(pen, r.X, r.Y, r.Width - 1, r.Height - 1);

            // Device name
            using (var f = new Font("Segoe UI", 9f, FontStyle.Bold))
            using (var b = new SolidBrush(Color.White))
                g.DrawString(tile.Model.Name, f, b, r.X + 8, r.Y + 8);

            // Status badge
            using (var f = new Font("Segoe UI", 7.5f))
            using (var b = new SolidBrush(border))
                g.DrawString(tile.Model.Status.ToString(), f, b, r.X + 8, r.Y + 28);

            // Type
            using (var f = new Font("Segoe UI", 7.5f))
            using (var b = new SolidBrush(Color.Silver))
                g.DrawString(tile.Model.Type.ToString(), f, b, r.X + 8, r.Y + 44);

            // Last value
            if (!string.IsNullOrEmpty(tile.LastValue))
                using (var f = new Font("Consolas", 10f, FontStyle.Bold))
                using (var b = new SolidBrush(Color.Cyan))
                    g.DrawString(tile.LastValue, f, b, r.X + 8, r.Y + 62);

            // Last seen
            using (var f = new Font("Segoe UI", 6.5f))
            using (var b = new SolidBrush(Color.DimGray))
                g.DrawString(tile.Model.LastSeen.ToString("HH:mm:ss"), f, b,
                             r.Right - 52, r.Bottom - 14);
        }

        // ---- Mouse interaction ----

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            var tile = _tiles.Find(t => t.Bounds.Contains(e.Location));
            if (tile != null)
                DeviceSelected?.Invoke(this, tile.Model);
        }

        // ---- Inner class ----

        private class DeviceStatusTile
        {
            public DeviceModel Model { get; }
            public Rectangle Bounds { get; set; }
            public string LastValue { get; set; } = "";

            public DeviceStatusTile(DeviceModel model) { Model = model; }
        }
    }
}