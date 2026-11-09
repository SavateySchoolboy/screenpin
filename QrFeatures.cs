using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using ZXing;
using ZXing.Common;

namespace ScreenPin {
    // Оба сочетания меняются независимо от основного бинда захвата.
    class QrHotkeys : IDisposable {
        [DllImport("user32.dll", SetLastError = true)] static extern bool RegisterHotKey(IntPtr h, int id, uint modifiers, uint key);
        [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr h, int id);
        readonly IntPtr handle;
        readonly Action create, scan;
        readonly HotkeyBinding[] bindings = new HotkeyBinding[2];
        readonly int[] ids = new int[2];
        public bool Recording { get; set; }
        static string FilePath { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ScreenPin", "qr-hotkeys.txt"); } }
        public QrHotkeys(IntPtr handle, Action create, Action scan) {
            this.handle = handle; this.create = create; this.scan = scan;
            bindings[0] = new HotkeyBinding(3, (uint)Keys.D1);
            bindings[1] = new HotkeyBinding(3, (uint)Keys.D2);
            try {
                var lines = File.ReadAllLines(FilePath);
                for (int i = 0; i < Math.Min(2, lines.Length); i++) {
                    var parts = lines[i].Split(','); uint mod, key;
                    if (parts.Length == 2 && uint.TryParse(parts[0], out mod) && uint.TryParse(parts[1], out key) && mod > 0 && mod <= 7 &&
                        ((key >= 0x41 && key <= 0x5A) || (key >= 0x30 && key <= 0x39) || (key >= 0x70 && key <= 0x7A))) bindings[i] = new HotkeyBinding(mod, key);
                }
            } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        public HotkeyBinding Get(int index) { return bindings[index]; }
        public bool RegisterInitial() { bool first = Install(0, bindings[0]); bool second = Install(1, bindings[1]); return first && second; }
        public bool Install(int index, HotkeyBinding binding) {
            if (ids[index] != 0 && bindings[index].SameAs(binding)) return true;
            int firstId = 20 + index * 2;
            int next = ids[index] == firstId ? firstId + 1 : firstId;
            if (!RegisterHotKey(handle, next, binding.Modifiers | 0x4000, binding.Key)) return false;
            if (ids[index] != 0) UnregisterHotKey(handle, ids[index]);
            ids[index] = next; bindings[index] = binding;
            return true;
        }
        public void Save() {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            File.WriteAllLines(FilePath, new string[] { bindings[0].Modifiers + "," + bindings[0].Key, bindings[1].Modifiers + "," + bindings[1].Key });
        }
        public void HandleHotkey(int id) {
            if (Recording || id == 0) return;
            if (id == ids[0]) create(); else if (id == ids[1]) scan();
        }
        public void Dispose() { foreach (int id in ids) if (id != 0) UnregisterHotKey(handle, id); }
    }

    class QrSettingsWindow : Form {
        readonly QrHotkeys hotkeys;
        readonly HotkeyBinding[] candidates = new HotkeyBinding[2];
        readonly Label[] notes = new Label[2];
        public QrSettingsWindow(QrHotkeys hotkeys) {
            this.hotkeys = hotkeys;
            Text = "ScreenPin · QR"; ClientSize = new Size(480, 402); StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedSingle; MaximizeBox = false; TopMost = true; BackColor = Color.FromArgb(17,20,29);
            ForeColor = Color.FromArgb(237,240,249); Font = new Font("Segoe UI", 10);
            Controls.Add(new Label { Text = "QR-коды", Font = new Font("Segoe UI Semibold",22), AutoSize = true, Location = new Point(26,22) });
            Controls.Add(new Label { Text = "Сочетания клавиш", ForeColor = Color.FromArgb(151,162,184), AutoSize = true, Location = new Point(30,67) });
            AddBinding(0, "Создать QR из выделенного текста", 108);
            AddBinding(1, "Считать QR с экрана", 235);
            Controls.Add(new Label { Text = "Ctrl, Shift или Alt + буква, цифра или F1–F11", ForeColor = Color.FromArgb(151,162,184), Font = new Font("Segoe UI",9), AutoSize = true, Location = new Point(32,373) });
        }
        void AddBinding(int index, string title, int y) {
            var card = new SettingsCard { Location = new Point(28,y), Size = new Size(424,115) };
            card.Controls.Add(new Label { Text = title, AutoSize = true, Location = new Point(18,13), BackColor = Color.FromArgb(27,31,43), ForeColor = Color.FromArgb(228,232,244), Font = new Font("Segoe UI Semibold",10) });
            candidates[index] = hotkeys.Get(index);
            var field = new TextBox { Text = candidates[index].Display, Location = new Point(18,43), Width = 236, ReadOnly = true, BorderStyle = BorderStyle.FixedSingle, BackColor = Color.FromArgb(39,45,62), ForeColor = Color.White, Font = new Font("Segoe UI",13), ShortcutsEnabled = false };
            notes[index] = new Label { Text = "Нажми на поле и введи сочетание", AutoSize = true, Location = new Point(18,84), BackColor = Color.FromArgb(27,31,43), ForeColor = Color.FromArgb(151,162,184), Font = new Font("Segoe UI",9) };
            field.KeyDown += delegate(object sender, KeyEventArgs e) {
                e.SuppressKeyPress = true;
                if (e.KeyCode == Keys.ControlKey || e.KeyCode == Keys.ShiftKey || e.KeyCode == Keys.Menu) return;
                var binding = HotkeyBinding.FromKeys(e.KeyCode, e.Modifiers);
                if (binding == null) { notes[index].Text = "Выбери поддерживаемое сочетание"; return; }
                candidates[index] = binding; field.Text = binding.Display; notes[index].Text = "Нажми «Сохранить»";
            };
            var apply = new RoundedButton { Text = "Сохранить", Location = new Point(263,40), Size = new Size(141,36), Fill = Color.FromArgb(65,70,96), ForeColor = Color.White, Cursor = Cursors.Hand };
            apply.Click += delegate {
                if (!hotkeys.Install(index, candidates[index])) { notes[index].Text = "Сочетание занято — выбери другое"; return; }
                try { hotkeys.Save(); notes[index].Text = "Сохранено: " + candidates[index].Display; }
                catch (Exception ex) { notes[index].Text = "Активно до выхода, сохранить не удалось"; MessageBox.Show(ex.Message, "ScreenPin"); }
                ActiveControl = null;
            };
            card.Controls.Add(field); card.Controls.Add(apply); card.Controls.Add(notes[index]); Controls.Add(card);
        }
    }

    static class QrWorkflows {
        [DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);
        [DllImport("user32.dll")] static extern uint GetClipboardSequenceNumber();
        [DllImport("user32.dll", SetLastError = true)] static extern uint SendInput(uint count, Input[] inputs, int size);
        [StructLayout(LayoutKind.Sequential)] struct Input { public uint Type; public InputUnion Data; }
        [StructLayout(LayoutKind.Explicit)] struct InputUnion {
            [FieldOffset(0)] public KeyboardInput Keyboard;
            [FieldOffset(0)] public MouseInput Mouse;
        }
        [StructLayout(LayoutKind.Sequential)] struct KeyboardInput { public ushort VirtualKey, Scan; public uint Flags, Time; public UIntPtr ExtraInfo; }
        [StructLayout(LayoutKind.Sequential)] struct MouseInput { public int X, Y; public uint MouseData, Flags, Time; public UIntPtr ExtraInfo; }
        static Input Key(ushort code, bool release) {
            return new Input { Type = 1, Data = new InputUnion { Keyboard = new KeyboardInput { VirtualKey = code, Flags = release ? 2u : 0u } } };
        }
        static void SendCopy() {
            var keys = new Input[] { Key(0x11, false), Key(0x43, false), Key(0x43, true), Key(0x11, true) };
            if (SendInput((uint)keys.Length, keys, Marshal.SizeOf(typeof(Input))) != keys.Length)
                throw new InvalidOperationException("Windows не передала команду копирования активному приложению.");
        }
        static bool copying;
        public static void CreateFromSelection() {
            if (copying) return;
            copying = true; int ticks = 0;
            var wait = new Timer { Interval = 80 };
            wait.Tick += delegate {
                ticks++;
                bool held = (GetAsyncKeyState(0x11) & 0x8000) != 0 || (GetAsyncKeyState(0x12) & 0x8000) != 0 || (GetAsyncKeyState(0x10) & 0x8000) != 0;
                if (held && ticks < 30) return;
                wait.Stop(); wait.Dispose();
                if (ticks >= 30) { copying = false; MessageBox.Show("Отпусти клавиши и попробуй снова.", "ScreenPin"); return; }
                try {
                    uint previous = GetClipboardSequenceNumber();
                    SendCopy();
                    int count = 0; var poll = new Timer { Interval = 100 };
                    poll.Tick += delegate {
                        count++;
                        if (GetClipboardSequenceNumber() == previous && count < 20) return;
                        poll.Stop(); poll.Dispose(); copying = false;
                        if (GetClipboardSequenceNumber() == previous) { MessageBox.Show("Не удалось скопировать выделение. Попробуй скопировать текст через Ctrl+C, затем выбери «QR из буфера обмена» в меню ScreenPin в трее.", "ScreenPin"); return; }
                        CreateFromClipboard();
                    };
                    poll.Start();
                } catch (Exception ex) { copying = false; MessageBox.Show("Не удалось скопировать выделение: " + ex.Message, "ScreenPin"); }
            };
            wait.Start();
        }
        public static void CreateFromClipboard() {
            try {
                if (!Clipboard.ContainsText()) { MessageBox.Show("В буфере нет текста. Выдели ссылку целиком и повтори.", "ScreenPin"); return; }
                string text = Clipboard.GetText();
                if (String.IsNullOrWhiteSpace(text)) { MessageBox.Show("Выделенный текст пуст.", "ScreenPin"); return; }
                using (var qr = QrCodec.Generate(text)) { var window = new QrCreateWindow(text, (Bitmap)qr.Clone()); window.Show(); }
            } catch (Exception ex) { MessageBox.Show("Не удалось создать QR-код: " + ex.Message, "ScreenPin"); }
        }
        public static void DecodeImage(Bitmap bitmap) {
            try {
                string value = QrCodec.Decode(bitmap);
                if (value == null) { MessageBox.Show("QR-код не найден. Выдели его целиком, вместе с белыми полями.", "ScreenPin"); return; }
                var window = new QrResultWindow(value); window.Show();
            } catch (Exception ex) { MessageBox.Show("Не удалось прочитать QR-код: " + ex.Message, "ScreenPin"); }
        }
    }

    static class QrCodec {
        public static Bitmap Generate(string text) {
            var options = new EncodingOptions { Width = 600, Height = 600, Margin = 4 };
            options.Hints[EncodeHintType.CHARACTER_SET] = "UTF-8";
            var writer = new BarcodeWriter { Format = BarcodeFormat.QR_CODE, Options = options };
            return writer.Write(text);
        }
        public static string Decode(Bitmap image) {
            var reader = new BarcodeReader { AutoRotate = true };
            reader.Options = new DecodingOptions { TryHarder = true, TryInverted = true, PossibleFormats = new List<BarcodeFormat> { BarcodeFormat.QR_CODE } };
            var result = reader.Decode(image); return result == null ? null : result.Text;
        }
    }

    class QrBaseWindow : Form {
        [DllImport("user32.dll")] static extern bool ReleaseCapture();
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int message, int wParam, int lParam);
        protected readonly Color Muted = Color.FromArgb(151, 162, 184);
        protected QrBaseWindow(string heading, int height) {
            Text = "ScreenPin · QR"; ClientSize = new Size(420, height); StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.None; TopMost = true; ShowInTaskbar = false;
            BackColor = Color.FromArgb(17, 20, 29); ForeColor = Color.FromArgb(237, 240, 249); Font = new Font("Segoe UI", 10);
            using (var path = Shapes.Rounded(new Rectangle(0, 0, Width, Height), 16)) Region = new Region(path);
            var title = new Label { Text = heading, Font = new Font("Segoe UI Semibold", 19), AutoSize = true, Location = new Point(24, 20), Cursor = Cursors.SizeAll };
            title.MouseDown += delegate(object sender, MouseEventArgs e) { if (e.Button == MouseButtons.Left) { ReleaseCapture(); SendMessage(Handle, 0x00A1, 2, 0); } };
            Controls.Add(title);
            var close = new RoundedButton { Text = "×", Font = new Font("Segoe UI", 15), Fill = Color.FromArgb(48, 54, 73), ForeColor = Color.White, Location = new Point(376, 17), Size = new Size(30, 30), Cursor = Cursors.Hand };
            close.Click += delegate { Close(); }; Controls.Add(close);
        }
        protected RoundedButton Button(string text, int x, int y, int width, bool primary, EventHandler handler) {
            var button = new RoundedButton { Text = text, Location = new Point(x, y), Size = new Size(width, 40), Fill = primary ? Color.FromArgb(104,102,241) : Color.FromArgb(48,54,73), ForeColor = Color.White, Cursor = Cursors.Hand };
            button.Click += handler; Controls.Add(button); return button;
        }
    }

    class QrCreateWindow : QrBaseWindow {
        readonly Bitmap qr;
        readonly PictureBox preview;
        Point dragStart;
        public QrCreateWindow(string text, Bitmap qr) : base("QR-код", 525) {
            this.qr = qr;
            Controls.Add(new Label { Text = "Перетащи код в чат или сохрани его", Location = new Point(27, 63), AutoSize = true, ForeColor = Muted, Font = new Font("Segoe UI", 9) });
            preview = new PictureBox { Image = qr, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.White, Location = new Point(69, 101), Size = new Size(282, 282), Cursor = Cursors.Hand };
            preview.MouseDown += delegate(object sender, MouseEventArgs e) { if (e.Button == MouseButtons.Left) dragStart = e.Location; };
            preview.MouseMove += delegate(object sender, MouseEventArgs e) {
                if (e.Button != MouseButtons.Left) return;
                if (Math.Abs(e.X - dragStart.X) < SystemInformation.DragSize.Width / 2 && Math.Abs(e.Y - dragStart.Y) < SystemInformation.DragSize.Height / 2) return;
                try {
                    string directory = Path.Combine(Path.GetTempPath(), "ScreenPin", "qr-drag"); Directory.CreateDirectory(directory);
                    string file = Path.Combine(directory, "ScreenPin-QR-" + Guid.NewGuid().ToString("N") + ".png"); qr.Save(file, ImageFormat.Png);
                    var data = new DataObject(); data.SetData(DataFormats.FileDrop, new string[] { file }); data.SetData(DataFormats.Bitmap, true, qr);
                    preview.DoDragDrop(data, DragDropEffects.Copy);
                } catch (Exception ex) { MessageBox.Show("Не удалось перетащить QR-код: " + ex.Message, "ScreenPin"); }
            };
            Controls.Add(preview);
            var textBox = new TextBox { Text = text, ReadOnly = true, BorderStyle = BorderStyle.FixedSingle, BackColor = Color.FromArgb(39,45,62), ForeColor = Color.White, Location = new Point(27, 402), Width = 366 };
            Controls.Add(textBox);
            Button("Копировать", 27, 455, 175, false, delegate { try { Clipboard.SetImage(qr); } catch (Exception ex) { MessageBox.Show(ex.Message,"ScreenPin"); } });
            Button("Сохранить PNG", 218, 455, 175, true, delegate {
                using (var dialog = new SaveFileDialog { Filter = "PNG (*.png)|*.png", FileName = "screenpin-qr.png", DefaultExt = "png" })
                    if (dialog.ShowDialog(this) == DialogResult.OK) try { qr.Save(dialog.FileName, ImageFormat.Png); } catch (Exception ex) { MessageBox.Show(ex.Message,"ScreenPin"); }
            });
            FormClosed += delegate { preview.Image = null; qr.Dispose(); };
        }
    }

    class BrowserChoice {
        public string Name, Path;
        public override string ToString() { return Name; }
    }
    class QrResultWindow : QrBaseWindow {
        readonly string value;
        readonly ComboBox browsers;
        public QrResultWindow(string value) : base("QR распознан", 325) {
            this.value = value;
            Controls.Add(new Label { Text = "Содержимое QR-кода", Location = new Point(28, 70), AutoSize = true, ForeColor = Muted, Font = new Font("Segoe UI", 9) });
            Controls.Add(new TextBox { Text = value, ReadOnly = true, Multiline = true, ScrollBars = ScrollBars.Vertical, Location = new Point(28, 97), Size = new Size(364, 70), BackColor = Color.FromArgb(39,45,62), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle });
            Controls.Add(new Label { Text = "Открыть в браузере", Location = new Point(28, 187), AutoSize = true, ForeColor = Muted, Font = new Font("Segoe UI", 9) });
            browsers = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(28, 212), Width = 364, BackColor = Color.FromArgb(39,45,62), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
            browsers.Items.Add(new BrowserChoice { Name = "Браузер по умолчанию", Path = null });
            var roots = new string[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) };
            AddBrowser("Google Chrome", roots, @"Google\Chrome\Application\chrome.exe");
            AddBrowser("Microsoft Edge", roots, @"Microsoft\Edge\Application\msedge.exe");
            AddBrowser("Mozilla Firefox", roots, @"Mozilla Firefox\firefox.exe");
            AddBrowser("Brave", roots, @"BraveSoftware\Brave-Browser\Application\brave.exe");
            browsers.SelectedIndex = 0; Controls.Add(browsers);
            Button("Копировать", 28, 265, 174, false, delegate { try { Clipboard.SetText(value); } catch (Exception ex) { MessageBox.Show(ex.Message, "ScreenPin"); } });
            Button("Открыть", 218, 265, 174, true, delegate { OpenLink(); });
        }
        void AddBrowser(string name, string[] roots, string relative) {
            foreach (string root in roots) { if (String.IsNullOrEmpty(root)) continue; string file = System.IO.Path.Combine(root, relative); if (File.Exists(file)) { browsers.Items.Add(new BrowserChoice { Name = name, Path = file }); return; } }
        }
        void OpenLink() {
            Uri uri;
            if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)) { MessageBox.Show("В QR-коде нет полной ссылки http:// или https://. Можно скопировать содержимое.", "ScreenPin"); return; }
            try {
                var selected = (BrowserChoice)browsers.SelectedItem;
                if (selected.Path == null) Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
                else Process.Start(new ProcessStartInfo(selected.Path, "\"" + uri.AbsoluteUri.Replace("\"", "%22") + "\"") { UseShellExecute = false });
            } catch (Exception ex) { MessageBox.Show("Не удалось открыть ссылку: " + ex.Message, "ScreenPin"); }
        }
    }
}
