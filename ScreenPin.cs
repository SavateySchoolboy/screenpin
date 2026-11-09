using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace ScreenPin {
    static class Program {
        [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
        [STAThread] static void Main() {
            SetProcessDPIAware();
            bool created;
            using (var mutex = new Mutex(true, @"Local\ScreenPinMVP", out created)) {
                if (!created) { MessageBox.Show("ScreenPin уже запущен. Найдите его значок в области уведомлений.", "ScreenPin"); return; }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainWindow());
            }
        }
    }

    class MainWindow : Form {
        [DllImport("user32.dll", SetLastError = true)] static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
        [DllImport("user32.dll", SetLastError = true)] static extern bool UnregisterHotKey(IntPtr hwnd, int id);
        const int HotkeyMessage = 0x0312;
        readonly NotifyIcon tray;
        readonly Label hotkeyNote;
        bool capturing;
        bool exiting;

        public MainWindow() {
            Text = "ScreenPin"; Size = new Size(440, 338); MinimumSize = new Size(440, 338);
            StartPosition = FormStartPosition.CenterScreen; BackColor = Color.FromArgb(19, 23, 34);
            ForeColor = Color.FromArgb(237, 240, 249); Font = new Font("Segoe UI", 10);
            var title = new Label { Text = "ScreenPin", Font = new Font("Segoe UI Semibold", 23), AutoSize = true, Location = new Point(26, 22) };
            var subtitle = new Label { Text = "Нужное всегда перед глазами", ForeColor = Color.FromArgb(165, 177, 199), AutoSize = true, Location = new Point(30, 72) };
            var capture = new Button { Text = "✂  Выбрать область", Location = new Point(30, 116), Size = new Size(355, 48), BackColor = Color.FromArgb(104, 102, 241), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand };
            capture.FlatAppearance.BorderSize = 0; capture.Click += delegate { StartCapture(); };
            hotkeyNote = new Label { Text = "Горячая клавиша: Ctrl + Shift + 2", AutoSize = true, Location = new Point(30, 180), ForeColor = Color.FromArgb(192, 201, 220) };
            var help = new Label { Text = "Перетащи снимок мышью · колесо — масштаб\nEsc отменяет выделение · значок в трее — выход", AutoSize = true, Location = new Point(30, 214), ForeColor = Color.FromArgb(148, 160, 182) };
            Controls.AddRange(new Control[] { title, subtitle, capture, hotkeyNote, help });
            var menu = new ContextMenuStrip();
            menu.Items.Add("Новый снимок", null, delegate { StartCapture(); });
            menu.Items.Add("Открыть ScreenPin", null, delegate { ShowMain(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Выход", null, delegate { exiting = true; Close(); });
            tray = new NotifyIcon { Text = "ScreenPin", Icon = SystemIcons.Application, ContextMenuStrip = menu, Visible = true };
            tray.DoubleClick += delegate { ShowMain(); };
            Shown += delegate { if (!RegisterHotKey(Handle, 1, 0x0002 | 0x0004 | 0x4000, 0x32)) hotkeyNote.Text = "Горячая клавиша занята — используй кнопку"; };
            FormClosing += delegate(object sender, FormClosingEventArgs e) { if (!exiting && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); } };
            FormClosed += delegate { UnregisterHotKey(Handle, 1); tray.Visible = false; tray.Dispose(); };
        }
        protected override void WndProc(ref Message m) { if (m.Msg == HotkeyMessage && m.WParam.ToInt32() == 1) StartCapture(); base.WndProc(ref m); }
        void ShowMain() { Show(); WindowState = FormWindowState.Normal; Activate(); }
        void StartCapture() {
            if (capturing) return;
            capturing = true;
            bool wasVisible = Visible && WindowState != FormWindowState.Minimized;
            Hide();
            var timer = new System.Windows.Forms.Timer { Interval = 180 };
            timer.Tick += delegate {
                timer.Stop(); timer.Dispose();
                try {
                    Rectangle screen = SystemInformation.VirtualScreen;
                    using (var desktop = new Bitmap(screen.Width, screen.Height, PixelFormat.Format32bppArgb)) {
                        using (var g = Graphics.FromImage(desktop)) g.CopyFromScreen(screen.Location, Point.Empty, screen.Size);
                        using (var selector = new SelectionWindow(desktop, screen)) {
                            if (selector.ShowDialog() == DialogResult.OK) {
                                Rectangle r = selector.Selected;
                                var image = desktop.Clone(r, PixelFormat.Format32bppArgb);
                                var pin = new PinWindow(image);
                                pin.Location = new Point(screen.Left + r.Left, screen.Top + r.Top);
                                pin.Show();
                            }
                        }
                    }
                } catch (Exception ex) { MessageBox.Show("Не удалось сделать снимок: " + ex.Message, "ScreenPin", MessageBoxButtons.OK, MessageBoxIcon.Error); }
                finally { capturing = false; if (wasVisible) ShowMain(); }
            };
            timer.Start();
        }
    }

    class SelectionWindow : Form {
        readonly Bitmap desktop;
        Point start, current;
        bool selecting;
        public Rectangle Selected { get; private set; }
        public SelectionWindow(Bitmap desktop, Rectangle bounds) {
            this.desktop = desktop; Bounds = bounds; FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual; TopMost = true; ShowInTaskbar = false;
            Cursor = Cursors.Cross; DoubleBuffered = true; KeyPreview = true;
            KeyDown += delegate(object s, KeyEventArgs e) { if (e.KeyCode == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); } };
            MouseDown += delegate(object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left) { start = current = e.Location; selecting = true; Invalidate(); } };
            MouseMove += delegate(object s, MouseEventArgs e) { if (selecting) { current = e.Location; Invalidate(); } };
            MouseUp += delegate(object s, MouseEventArgs e) { if (!selecting) return; selecting = false; current = e.Location; Selected = Rect(start, current); if (Selected.Width >= 4 && Selected.Height >= 4) { DialogResult = DialogResult.OK; Close(); } else Invalidate(); };
        }
        static Rectangle Rect(Point a, Point b) { return Rectangle.FromLTRB(Math.Min(a.X,b.X), Math.Min(a.Y,b.Y), Math.Max(a.X,b.X), Math.Max(a.Y,b.Y)); }
        protected override void OnPaint(PaintEventArgs e) {
            e.Graphics.DrawImageUnscaled(desktop, 0, 0);
            using (var shade = new SolidBrush(Color.FromArgb(125, 8, 12, 22))) e.Graphics.FillRectangle(shade, ClientRectangle);
            if (selecting) { Rectangle r = Rect(start, current); if (r.Width > 0 && r.Height > 0) { e.Graphics.DrawImage(desktop, r, r, GraphicsUnit.Pixel); using (var pen = new Pen(Color.FromArgb(155, 150, 255), 2)) e.Graphics.DrawRectangle(pen, r); } }
            using (var b = new SolidBrush(Color.FromArgb(225, 22, 26, 39))) e.Graphics.FillRectangle(b, 18, 18, 410, 39);
            using (var f = new Font("Segoe UI", 11)) e.Graphics.DrawString("Выдели область мышью  ·  Esc — отмена", f, Brushes.White, 28, 26);
        }
    }

    class PinWindow : Form {
        readonly Bitmap image;
        readonly PictureBox picture;
        Point dragStart, windowStart;
        public PinWindow(Bitmap image) {
            this.image = image; Text = "ScreenPin — снимок"; FormBorderStyle = FormBorderStyle.SizableToolWindow;
            TopMost = true; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual;
            BackColor = Color.FromArgb(24, 28, 39); MinimumSize = new Size(160, 100);
            int maxW = Math.Min(900, Screen.PrimaryScreen.WorkingArea.Width - 50);
            double scale = Math.Min(1.0, (double)maxW / image.Width);
            ClientSize = new Size(Math.Max(160, (int)(image.Width * scale)), Math.Max(75, (int)(image.Height * scale) + 34));
            var bar = new Panel { Dock = DockStyle.Top, Height = 34, BackColor = Color.FromArgb(30, 35, 49) };
            var label = new Label { Text = "  SCREENPIN  ·  перетащи снимок", ForeColor = Color.FromArgb(186, 197, 216), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Segoe UI", 9) };
            var close = MakeButton("✕", "Закрыть снимок"); close.Click += delegate { Close(); };
            var save = MakeButton("↓", "Сохранить как PNG"); save.Click += delegate { SaveImage(); };
            var plus = MakeButton("+", "Увеличить"); plus.Click += delegate { Zoom(1.2); };
            var minus = MakeButton("−", "Уменьшить"); minus.Click += delegate { Zoom(1 / 1.2); };
            bar.Controls.Add(label); bar.Controls.Add(close); bar.Controls.Add(save); bar.Controls.Add(plus); bar.Controls.Add(minus);
            picture = new PictureBox { Dock = DockStyle.Fill, Image = image, SizeMode = PictureBoxSizeMode.Zoom, Cursor = Cursors.SizeAll, BackColor = Color.FromArgb(20, 23, 31) };
            picture.MouseDown += delegate(object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left) { dragStart = Cursor.Position; windowStart = Location; } };
            picture.MouseMove += delegate(object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left) { Point pos = Cursor.Position; Location = new Point(windowStart.X + pos.X - dragStart.X, windowStart.Y + pos.Y - dragStart.Y); } };
            picture.MouseWheel += delegate(object s, MouseEventArgs e) { Zoom(e.Delta > 0 ? 1.12 : 1 / 1.12); };
            picture.MouseEnter += delegate { picture.Focus(); };
            Controls.Add(picture); Controls.Add(bar);
            FormClosed += delegate { picture.Image = null; image.Dispose(); };
        }
        Button MakeButton(string text, string description) {
            var button = new Button { Text = text, Dock = DockStyle.Right, Width = 37, FlatStyle = FlatStyle.Flat, ForeColor = Color.White, BackColor = Color.FromArgb(30, 35, 49), TabStop = false, Cursor = Cursors.Hand };
            button.FlatAppearance.BorderSize = 0; new ToolTip().SetToolTip(button, description); return button;
        }
        void Zoom(double factor) { int w = Math.Max(160, Math.Min(4000, (int)(ClientSize.Width * factor))); int h = Math.Max(75, Math.Min(4000, (int)((ClientSize.Height - 34) * factor))) + 34; ClientSize = new Size(w, h); }
        void SaveImage() { using (var dialog = new SaveFileDialog { Filter = "PNG (*.png)|*.png", FileName = "screenpin.png", DefaultExt = "png" }) { if (dialog.ShowDialog(this) == DialogResult.OK) try { image.Save(dialog.FileName, ImageFormat.Png); } catch (Exception ex) { MessageBox.Show("Не удалось сохранить файл: " + ex.Message, "ScreenPin"); } } }
    }
}
