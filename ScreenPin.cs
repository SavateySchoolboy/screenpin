using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
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
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
        const int HotkeyMessage = 0x0312;
        readonly NotifyIcon tray;
        readonly Label hotkeyNote;
        readonly TextBox hotkeyInput;
        HotkeyBinding activeHotkey;
        HotkeyBinding candidateHotkey;
        int activeHotkeyId;
        bool capturing;
        bool exiting;

        public MainWindow() {
            Text = "ScreenPin · Настройки"; ClientSize = new Size(480, 390); FormBorderStyle = FormBorderStyle.FixedSingle; MaximizeBox = false;
            HandleCreated += delegate {
                try { int dark = 1; if (DwmSetWindowAttribute(Handle, 20, ref dark, sizeof(int)) != 0) DwmSetWindowAttribute(Handle, 19, ref dark, sizeof(int)); }
                catch (DllNotFoundException) { } catch (EntryPointNotFoundException) { }
            };
            StartPosition = FormStartPosition.CenterScreen; BackColor = Color.FromArgb(19, 23, 34);
            ForeColor = Color.FromArgb(237, 240, 249); Font = new Font("Segoe UI", 10);
            var title = new Label { Text = "ScreenPin", Font = new Font("Segoe UI Semibold", 22), AutoSize = true, Location = new Point(26, 18) };
            var subtitle = new Label { Text = "Настройки захвата", ForeColor = Color.FromArgb(165, 177, 199), AutoSize = true, Location = new Point(30, 62) };
            var capture = new Button { Text = "✂  Выбрать область", Location = new Point(30, 98), Size = new Size(408, 44), BackColor = Color.FromArgb(104, 102, 241), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand };
            capture.FlatAppearance.BorderSize = 0; capture.Click += delegate { StartCapture(); };
            var label = new Label { Text = "ГОРЯЧАЯ КЛАВИША", ForeColor = Color.FromArgb(165, 177, 199), AutoSize = true, Location = new Point(30, 167), Font = new Font("Segoe UI Semibold", 9) };
            hotkeyInput = new TextBox { Location = new Point(30, 196), Width = 268, ReadOnly = true, BorderStyle = BorderStyle.FixedSingle, BackColor = Color.FromArgb(39, 45, 62), ForeColor = Color.White, Font = new Font("Segoe UI", 13), ShortcutsEnabled = false };
            hotkeyInput.KeyDown += HotkeyKeyDown;
            var apply = new Button { Text = "Сохранить", Location = new Point(307, 192), Size = new Size(131, 36), BackColor = Color.FromArgb(75, 80, 112), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand };
            apply.FlatAppearance.BorderSize = 0;
            apply.Click += delegate { ApplyCandidate(); };
            hotkeyNote = new Label { Text = "Нажми на поле и введи новое сочетание", AutoSize = true, Location = new Point(30, 239), ForeColor = Color.FromArgb(192, 201, 220) };
            var help = new Label { Text = "Колесо — размер   ·   Alt + колесо — прозрачность\nКнопка булавки закрепляет снимок на экране\nЗапуск вместе с Windows настраивается в трее", AutoSize = true, Location = new Point(30, 282), ForeColor = Color.FromArgb(148, 160, 182) };
            Controls.AddRange(new Control[] { title, subtitle, capture, label, hotkeyInput, apply, hotkeyNote, help });
            var menu = new ContextMenuStrip();
            menu.Items.Add("Новый снимок", null, delegate { StartCapture(); });
            menu.Items.Add("Настройки", null, delegate { ShowMain(); });
            var startup = new ToolStripMenuItem("Запускать вместе с Windows") { CheckOnClick = true };
            try { using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run")) {
                string savedPath = key == null ? null : key.GetValue("ScreenPin") as string;
                startup.Checked = string.Equals(savedPath, "\"" + Application.ExecutablePath + "\"", StringComparison.OrdinalIgnoreCase);
                if (!startup.Checked && !string.IsNullOrEmpty(savedPath)) startup.Text = "Обновить автозапуск для этой версии";
            } } catch { startup.Enabled = false; }
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
            Shown += delegate {
                candidateHotkey = HotkeyBinding.Load();
                if (!InstallHotkey(candidateHotkey)) {
                    tray.ShowBalloonTip(4000, "ScreenPin", "Сохранённая клавиша занята. Откройте настройки в трее.", ToolTipIcon.Warning);
                    hotkeyNote.Text = "Сочетание занято — выбери другое";
                }
                hotkeyInput.Text = candidateHotkey.Display;
                BeginInvoke((Action)delegate { Hide(); });
            };
            FormClosing += delegate(object sender, FormClosingEventArgs e) { if (!exiting && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); } };
            FormClosed += delegate { if (activeHotkeyId != 0) UnregisterHotKey(Handle, activeHotkeyId); tray.Visible = false; tray.Dispose(); };
        }
        protected override void WndProc(ref Message m) { if (m.Msg == HotkeyMessage && m.WParam.ToInt32() == activeHotkeyId && !hotkeyInput.Focused) StartCapture(); base.WndProc(ref m); }
        void HotkeyKeyDown(object sender, KeyEventArgs e) {
            e.SuppressKeyPress = true;
            if (e.KeyCode == Keys.ControlKey || e.KeyCode == Keys.ShiftKey || e.KeyCode == Keys.Menu) return;
            var binding = HotkeyBinding.FromKeys(e.KeyCode, e.Modifiers);
            if (binding == null) { hotkeyNote.Text = "Используй Ctrl, Alt или Shift вместе с клавишей"; return; }
            candidateHotkey = binding;
            hotkeyInput.Text = binding.Display;
            hotkeyNote.Text = "Нажми «Сохранить», чтобы применить";
        }
        bool InstallHotkey(HotkeyBinding binding) {
            if (activeHotkey != null && activeHotkey.SameAs(binding)) return true;
            int nextId = activeHotkeyId == 1 ? 2 : 1;
            if (!RegisterHotKey(Handle, nextId, binding.Modifiers | 0x4000, binding.Key)) return false;
            if (activeHotkeyId != 0) UnregisterHotKey(Handle, activeHotkeyId);
            activeHotkey = binding;
            activeHotkeyId = nextId;
            return true;
        }
        void ApplyCandidate() {
            if (candidateHotkey == null) return;
            if (!InstallHotkey(candidateHotkey)) { hotkeyNote.Text = "Сочетание занято. Прежнее остаётся активным."; return; }
            try { activeHotkey.Save(); hotkeyNote.Text = "Сохранено: " + activeHotkey.Display; }
            catch (Exception ex) { hotkeyNote.Text = "Активно до выхода, сохранить не удалось"; MessageBox.Show(ex.Message, "ScreenPin"); }
            captureFocus();
        }
        void captureFocus() { ActiveControl = null; }
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

    class HotkeyBinding {
        public readonly uint Modifiers;
        public readonly uint Key;
        public HotkeyBinding(uint modifiers, uint key) { Modifiers = modifiers; Key = key; }
        static string SettingsPath { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ScreenPin", "hotkey.txt"); } }
        public bool SameAs(HotkeyBinding other) { return other != null && Modifiers == other.Modifiers && Key == other.Key; }
        public string Display {
            get {
                string prefix = "";
                if ((Modifiers & 2) != 0) prefix += "Ctrl + ";
                if ((Modifiers & 4) != 0) prefix += "Shift + ";
                if ((Modifiers & 1) != 0) prefix += "Alt + ";
                string keyName = ((Keys)Key).ToString();
                if (keyName.Length == 2 && keyName[0] == 'D' && char.IsDigit(keyName[1])) keyName = keyName.Substring(1);
                return prefix + keyName;
            }
        }
        public static HotkeyBinding FromKeys(Keys key, Keys modifiers) {
            uint flags = 0;
            if ((modifiers & Keys.Control) != 0) flags |= 2;
            if ((modifiers & Keys.Shift) != 0) flags |= 4;
            if ((modifiers & Keys.Alt) != 0) flags |= 1;
            bool validKey = (key >= Keys.A && key <= Keys.Z) || (key >= Keys.D0 && key <= Keys.D9) || (key >= Keys.F1 && key <= Keys.F11);
            return flags != 0 && validKey ? new HotkeyBinding(flags, (uint)key) : null;
        }
        public static HotkeyBinding Load() {
            try {
                string[] parts = File.ReadAllText(SettingsPath).Split(',');
                uint flags, key;
                if (parts.Length == 2 && uint.TryParse(parts[0], out flags) && uint.TryParse(parts[1], out key) && flags > 0 && flags <= 7 &&
                    ((key >= 0x41 && key <= 0x5A) || (key >= 0x30 && key <= 0x39) || (key >= 0x70 && key <= 0x7A))) return new HotkeyBinding(flags, key);
            } catch (IOException) { } catch (UnauthorizedAccessException) { }
            return new HotkeyBinding(2 | 4, (uint)Keys.D2);
        }
        public void Save() {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));
            File.WriteAllText(SettingsPath, Modifiers + "," + Key);
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
        bool locked;

        public PinWindow(Bitmap image) {
            this.image = image;
            Text = "ScreenPin — снимок";
            FormBorderStyle = FormBorderStyle.None;
            TopMost = true;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.FromArgb(20, 23, 31);
            MinimumSize = new Size(200, 90);
            DoubleBuffered = true;
            int maxW = Math.Min(900, Screen.PrimaryScreen.WorkingArea.Width - 50);
            int maxH = Screen.PrimaryScreen.WorkingArea.Height - 50;
            double scale = Math.Min(1.0, Math.Min((double)maxW / image.Width, (double)maxH / image.Height));
            ClientSize = new Size(Math.Max(200, (int)(image.Width * scale)), Math.Max(90, (int)(image.Height * scale)));
            UpdateRoundedRegion();

            picture = new SmoothPictureBox { Dock = DockStyle.Fill, Image = image, SizeMode = PictureBoxSizeMode.Zoom, Cursor = Cursors.SizeAll, BackColor = Color.FromArgb(20, 23, 31) };
            picture.MouseDown += delegate(object sender, MouseEventArgs e) {
                if (e.Button != MouseButtons.Left) return;
                picture.Focus();
                if (locked) return;
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
                picture.Cursor = locked ? Cursors.Default : (e.X >= picture.Width - 18 && e.Y >= picture.Height - 18 ? Cursors.SizeNWSE : Cursors.SizeAll);
            };
            picture.MouseWheel += delegate(object sender, MouseEventArgs e) {
                if ((ModifierKeys & Keys.Alt) != 0) AdjustOpacity(e.Delta);
                else Zoom(e.Delta > 0 ? 1.12 : 1 / 1.12);
            };
            Controls.Add(picture);

            toolbar = new PinToolbar();
            toolbar.ZoomIn += delegate { Zoom(1.2); };
            toolbar.ZoomOut += delegate { Zoom(1 / 1.2); };
            toolbar.Save += delegate { SaveImage(); };
            toolbar.ClosePin += delegate { Close(); };
            toolbar.ToggleLock += delegate { locked = !locked; toolbar.SetLocked(locked); picture.Cursor = locked ? Cursors.Default : Cursors.SizeAll; };
            LocationChanged += delegate { PositionToolbar(); };
            SizeChanged += delegate { UpdateRoundedRegion(); PositionToolbar(); };
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
        void UpdateRoundedRegion() {
            if (ClientSize.Width < 2 || ClientSize.Height < 2) return;
            int diameter = Math.Min(20, Math.Min(ClientSize.Width, ClientSize.Height));
            using (var path = new GraphicsPath()) {
                path.AddArc(0, 0, diameter, diameter, 180, 90);
                path.AddArc(ClientSize.Width - diameter, 0, diameter, diameter, 270, 90);
                path.AddArc(ClientSize.Width - diameter, ClientSize.Height - diameter, diameter, diameter, 0, 90);
                path.AddArc(0, ClientSize.Height - diameter, diameter, diameter, 90, 90);
                path.CloseFigure();
                var previous = Region;
                Region = new Region(path);
                if (previous != null) previous.Dispose();
            }
        }
        void AdjustOpacity(int delta) {
            Opacity = Math.Max(0.25, Math.Min(1.0, Opacity + (delta > 0 ? 0.08 : -0.08)));
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
            zoomTarget = new Size(Math.Max(200, Math.Min(4000, (int)(ClientSize.Width * factor))),
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
        public event EventHandler ZoomIn, ZoomOut, Save, ClosePin, ToggleLock;
        readonly ToolbarButton lockButton;
        protected override bool ShowWithoutActivation { get { return true; } }
        public PinToolbar() {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = Color.FromArgb(27, 31, 43);
            Opacity = 0.90;
            Size = new Size(180, 40);
            using (var shape = new GraphicsPath()) {
                shape.AddArc(0, 0, 26, 26, 180, 90);
                shape.AddArc(Width - 26, 0, 26, 26, 270, 90);
                shape.AddArc(Width - 26, Height - 26, 26, 26, 0, 90);
                shape.AddArc(0, Height - 26, 26, 26, 90, 90);
                shape.CloseFigure();
                Region = new Region(shape);
            }
            var tip = new ToolTip();
            AddButton("minus", "Уменьшить", 5, delegate { if (ZoomOut != null) ZoomOut(this, EventArgs.Empty); }, tip);
            AddButton("plus", "Увеличить", 39, delegate { if (ZoomIn != null) ZoomIn(this, EventArgs.Empty); }, tip);
            lockButton = AddButton("pin", "Закрепить или открепить", 73, delegate { if (ToggleLock != null) ToggleLock(this, EventArgs.Empty); }, tip);
            AddButton("save", "Сохранить PNG", 107, delegate { if (Save != null) Save(this, EventArgs.Empty); }, tip);
            AddButton("close", "Закрыть снимок", 141, delegate { if (ClosePin != null) ClosePin(this, EventArgs.Empty); }, tip);
            FormClosed += delegate { tip.Dispose(); };
        }
        public void SetLocked(bool value) { lockButton.Active = value; lockButton.Invalidate(); }
        ToolbarButton AddButton(string icon, string hint, int x, EventHandler action, ToolTip tip) {
            var button = new ToolbarButton(icon) { Location = new Point(x, 5), Size = new Size(30, 30), Cursor = Cursors.Hand };
            button.Click += action;
            tip.SetToolTip(button, hint);
            Controls.Add(button);
            return button;
        }
    }

    class ToolbarButton : Control {
        readonly string icon;
        bool hovered;
        public bool Active { get; set; }
        public ToolbarButton(string icon) {
            this.icon = icon;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            MouseEnter += delegate { hovered = true; Invalidate(); };
            MouseLeave += delegate { hovered = false; Invalidate(); };
        }
        protected override void OnPaint(PaintEventArgs e) {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Color fill = Active ? Color.FromArgb(105, 103, 224) : hovered ? Color.FromArgb(75, 82, 103) : Color.FromArgb(50, 56, 72);
            using (var brush = new SolidBrush(fill)) g.FillEllipse(brush, 1, 1, Width - 3, Height - 3);
            using (var pen = new Pen(Color.FromArgb(239, 242, 250), 1.7f)) {
                pen.StartCap = LineCap.Round; pen.EndCap = LineCap.Round; pen.LineJoin = LineJoin.Round;
                if (icon == "minus" || icon == "plus") {
                    g.DrawLine(pen, 10, 15, 20, 15);
                    if (icon == "plus") g.DrawLine(pen, 15, 10, 15, 20);
                } else if (icon == "close") {
                    g.DrawLine(pen, 11, 11, 19, 19); g.DrawLine(pen, 19, 11, 11, 19);
                } else if (icon == "save") {
                    g.DrawLine(pen, 15, 8, 15, 18); g.DrawLines(pen, new Point[]{new Point(11,15),new Point(15,19),new Point(19,15)});
                    g.DrawLines(pen, new Point[]{new Point(10,21),new Point(10,23),new Point(20,23),new Point(20,21)});
                } else if (icon == "pin") {
                    g.DrawLines(pen, new Point[]{new Point(11,9),new Point(19,9),new Point(18,15),new Point(21,18),new Point(9,18),new Point(12,15),new Point(11,9)});
                    g.DrawLine(pen,15,18,15,23);
                }
            }
        }
    }
}
