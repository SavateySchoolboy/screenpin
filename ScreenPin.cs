using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

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
            var startup = new ToolStripMenuItem("Запускать вместе с Windows") { CheckOnClick = true };
            try { using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run")) startup.Checked = key != null && key.GetValue("ScreenPin") != null; } catch { startup.Enabled = false; }
            startup.Click += delegate {
                try { using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true)) {
                    if (key == null) throw new InvalidOperationException("Раздел автозапуска недоступен");
                    if (startup.Checked) key.SetValue("ScreenPin", "\"" + Application.ExecutablePath + "\"");
                    else key.DeleteValue("ScreenPin", false);
                } } catch (Exception ex) { startup.Checked = !startup.Checked; MessageBox.Show("Не удалось изменить автозапуск: " + ex.Message, "ScreenPin"); }
            };
            menu.Items.Add(startup);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Выход", null, delegate { exiting = true; Close(); });
            tray = new NotifyIcon { Text = "ScreenPin", Icon = SystemIcons.Application, ContextMenuStrip = menu, Visible = true };
            tray.DoubleClick += delegate { ShowMain(); };
            Shown += delegate { if (!RegisterHotKey(Handle, 1, 0x0002 | 0x0004 | 0x4000, 0x32)) { hotkeyNote.Text = "Горячая клавиша занята — используй кнопку"; tray.ShowBalloonTip(4000, "ScreenPin", "Ctrl + Shift + 2 уже занята. Используйте меню в трее.", ToolTipIcon.Warning); } BeginInvoke((Action)delegate { Hide(); }); };
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

    // Изображение занимает всю площадь: отдельная полупрозрачная панель
    // появляется только при наведении и не меняет яркость самого снимка.
    class PinWindow : Form {
        [DllImport("user32.dll")] static extern bool ReleaseCapture();
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);
        const int WM_NCLBUTTONDOWN = 0x00A1;
        const int HTCAPTION = 2;
        const int HTBOTTOMRIGHT = 17;
        readonly Bitmap image;
        readonly PictureBox picture;
        readonly PinToolbar toolbar;
        readonly System.Windows.Forms.Timer hoverTimer;
        readonly System.Windows.Forms.Timer zoomTimer;
        Size zoomStart, zoomTarget;
        int zoomFrame;
        bool moving;

        public PinWindow(Bitmap image) {
            this.image = image;
            Text = "ScreenPin — снимок";
            FormBorderStyle = FormBorderStyle.None;
            TopMost = true;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.FromArgb(20, 23, 31);
            MinimumSize = new Size(160, 90);
            DoubleBuffered = true;
            int maxW = Math.Min(900, Screen.PrimaryScreen.WorkingArea.Width - 50);
            int maxH = Screen.PrimaryScreen.WorkingArea.Height - 50;
            double scale = Math.Min(1.0, Math.Min((double)maxW / image.Width, (double)maxH / image.Height));
            ClientSize = new Size(Math.Max(160, (int)(image.Width * scale)), Math.Max(90, (int)(image.Height * scale)));

            picture = new SmoothPictureBox { Dock = DockStyle.Fill, Image = image, SizeMode = PictureBoxSizeMode.Zoom, Cursor = Cursors.SizeAll, BackColor = Color.FromArgb(20, 23, 31) };
            picture.MouseDown += delegate(object sender, MouseEventArgs e) {
                if (e.Button != MouseButtons.Left) return;
                picture.Focus();
                // Нижний правый угол — изменение размера, остальная площадь — перемещение.
                bool resize = e.X >= picture.Width - 18 && e.Y >= picture.Height - 18;
                moving = true;
                toolbar.Hide();
                ReleaseCapture();
                SendMessage(Handle, WM_NCLBUTTONDOWN, resize ? HTBOTTOMRIGHT : HTCAPTION, 0);
                moving = false;
                UpdateToolbar();
            };
            picture.MouseMove += delegate(object sender, MouseEventArgs e) {
                picture.Cursor = e.X >= picture.Width - 18 && e.Y >= picture.Height - 18 ? Cursors.SizeNWSE : Cursors.SizeAll;
            };
            picture.MouseWheel += delegate(object sender, MouseEventArgs e) { Zoom(e.Delta > 0 ? 1.12 : 1 / 1.12); };
            Controls.Add(picture);

            toolbar = new PinToolbar();
            toolbar.ZoomIn += delegate { Zoom(1.2); };
            toolbar.ZoomOut += delegate { Zoom(1 / 1.2); };
            toolbar.Save += delegate { SaveImage(); };
            toolbar.ClosePin += delegate { Close(); };
            LocationChanged += delegate { PositionToolbar(); };
            SizeChanged += delegate { PositionToolbar(); };
            Shown += delegate { toolbar.Show(this); PositionToolbar(); UpdateToolbar(); };
            hoverTimer = new System.Windows.Forms.Timer { Interval = 100 };
            hoverTimer.Tick += delegate { UpdateToolbar(); };
            hoverTimer.Start();
            zoomTimer = new System.Windows.Forms.Timer { Interval = 16 };
            zoomTimer.Tick += delegate {
                zoomFrame++;
                double t = Math.Min(1.0, zoomFrame / 7.0);
                double eased = 1.0 - Math.Pow(1.0 - t, 3);
                ClientSize = new Size((int)(zoomStart.Width + (zoomTarget.Width - zoomStart.Width) * eased),
                                      (int)(zoomStart.Height + (zoomTarget.Height - zoomStart.Height) * eased));
                if (t >= 1.0) zoomTimer.Stop();
            };
            FormClosed += delegate { hoverTimer.Stop(); hoverTimer.Dispose(); zoomTimer.Stop(); zoomTimer.Dispose(); toolbar.Close(); toolbar.Dispose(); picture.Image = null; image.Dispose(); };
        }
        void PositionToolbar() {
            if (toolbar.IsDisposed) return;
            toolbar.Location = new Point(Right - toolbar.Width - 8, Top + 8);
        }
        void UpdateToolbar() {
            if (toolbar.IsDisposed || moving || !Visible) return;
            bool inside = Bounds.Contains(Cursor.Position) || (toolbar.Visible && toolbar.Bounds.Contains(Cursor.Position));
            if (inside && !toolbar.Visible) { PositionToolbar(); toolbar.Show(this); }
            else if (!inside && toolbar.Visible) toolbar.Hide();
        }
        void Zoom(double factor) {
            zoomTimer.Stop();
            zoomStart = ClientSize;
            zoomTarget = new Size(Math.Max(160, Math.Min(4000, (int)(ClientSize.Width * factor))),
                                  Math.Max(90, Math.Min(4000, (int)(ClientSize.Height * factor))));
            zoomFrame = 0;
            zoomTimer.Start();
        }
        void SaveImage() {
            using (var dialog = new SaveFileDialog { Filter = "PNG (*.png)|*.png", FileName = "screenpin.png", DefaultExt = "png" }) {
                if (dialog.ShowDialog(this) == DialogResult.OK)
                    try { image.Save(dialog.FileName, ImageFormat.Png); }
                    catch (Exception ex) { MessageBox.Show("Не удалось сохранить файл: " + ex.Message, "ScreenPin"); }
            }
        }
    }

    class SmoothPictureBox : PictureBox {
        public SmoothPictureBox() {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.Selectable, true);
            TabStop = true;
        }
    }

    // Отдельное окно позволяет менять прозрачность панели, не затрагивая снимок.
    class PinToolbar : Form {
        public event EventHandler ZoomIn, ZoomOut, Save, ClosePin;
        protected override bool ShowWithoutActivation { get { return true; } }
        public PinToolbar() {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = Color.FromArgb(32, 36, 48);
            Opacity = 0.86;
            Size = new Size(144, 38);
            using (var shape = new GraphicsPath()) {
                shape.AddArc(0, 0, 24, 24, 180, 90);
                shape.AddArc(Width - 24, 0, 24, 24, 270, 90);
                shape.AddArc(Width - 24, Height - 24, 24, 24, 0, 90);
                shape.AddArc(0, Height - 24, 24, 24, 90, 90);
                shape.CloseFigure();
                Region = new Region(shape);
            }
            var tip = new ToolTip();
            AddButton("−", "Уменьшить", 4, delegate { if (ZoomOut != null) ZoomOut(this, EventArgs.Empty); }, tip);
            AddButton("+", "Увеличить", 38, delegate { if (ZoomIn != null) ZoomIn(this, EventArgs.Empty); }, tip);
            AddButton("↓", "Сохранить PNG", 72, delegate { if (Save != null) Save(this, EventArgs.Empty); }, tip);
            AddButton("×", "Закрыть снимок", 106, delegate { if (ClosePin != null) ClosePin(this, EventArgs.Empty); }, tip);
            FormClosed += delegate { tip.Dispose(); };
        }
        void AddButton(string symbol, string hint, int x, EventHandler action, ToolTip tip) {
            var button = new Button { Text = symbol, Location = new Point(x, 4), Size = new Size(30, 30), FlatStyle = FlatStyle.Flat,
                ForeColor = Color.White, BackColor = Color.FromArgb(53, 58, 72), Font = new Font("Segoe UI", 13), TabStop = false, Cursor = Cursors.Hand };
            button.FlatAppearance.BorderSize = 0;
            using (var circle = new GraphicsPath()) { circle.AddEllipse(0, 0, 29, 29); button.Region = new Region(circle); }
            button.Click += action;
            tip.SetToolTip(button, hint);
            Controls.Add(button);
        }
    }
}
