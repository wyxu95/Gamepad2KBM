using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Gamepad2KBM
{
    public sealed class Overlay : Form
    {
        private readonly Engine engine;
        private readonly Font body = new Font("Microsoft YaHei UI", 10);
        private readonly Font heading = new Font("Microsoft YaHei UI", 13, FontStyle.Bold);
        private readonly Font letter = new Font("Segoe UI", 16, FontStyle.Bold);
        private readonly Font big = new Font("Segoe UI", 38, FontStyle.Bold);
        private readonly Font phonetic = new Font("Segoe UI", 10, FontStyle.Bold);
        private readonly Color ink = Color.FromArgb(229, 235, 246);
        private readonly Color leftColor = Color.FromArgb(88, 209, 188);
        private readonly Color rightColor = Color.FromArgb(151, 176, 255);
        private int slot;
        private IntPtr inputTarget;

        public Overlay(Engine engine)
        {
            this.engine = engine;
            Text = "Gamepad2KBM"; FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false; TopMost = true; DoubleBuffered = true;
            BackColor = Color.FromArgb(20, 26, 38); ForeColor = ink;
            Opacity = 0.80; AutoScaleMode = AutoScaleMode.None;
            StartPosition = FormStartPosition.Manual;
        }
        protected override bool ShowWithoutActivation { get { return true; } }
        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);
            // Some desktop window managers activate newly shown tool windows.
            // Return focus to the input target if they activate this overlay.
            if (inputTarget != IntPtr.Zero) Native.SetForegroundWindow(inputTarget);
        }
        protected override CreateParams CreateParams
        {
            get
            {
                var p = base.CreateParams;
                p.ExStyle |= 0x08000000 | 0x00000080 | 0x00000020; // no activate, tool window, click through
                return p;
            }
        }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x21) { m.Result = new IntPtr(3); return; } // MA_NOACTIVATE
            if (m.Msg == 0x84) { m.Result = new IntPtr(-1); return; } // HTTRANSPARENT
            base.WndProc(ref m);
        }

        public void RefreshMode(int controllerSlot)
        {
            slot = controllerSlot;
            if (engine.Mode == Mode.Standby) { Hide(); return; }
            IntPtr foreground = Native.GetForegroundWindow();
            if (!IsHandleCreated || foreground != Handle) inputTarget = foreground;
            Rectangle area = Screen.FromPoint(Cursor.Position).WorkingArea;
            Size = engine.Mode == Mode.Keyboard ? new Size(960, 535) : new Size(700, 88);
            Location = new Point(area.Left + (area.Width - Width) / 2, area.Bottom - Height - 20);
            Show();
            if (Native.GetForegroundWindow() == Handle && inputTarget != IntPtr.Zero)
                Native.SetForegroundWindow(inputTarget);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var line = new Pen(Color.FromArgb(64, 79, 103)))
                g.DrawRectangle(line, 0, 0, Width - 1, Height - 1);
            if (engine.Mode == Mode.Mouse)
            {
                DrawText(g, "鼠标  /  Xbox " + (slot + 1), heading, leftColor, 18, 10);
                DrawText(g, Math.Round(engine.Mouse.Speed) + " px/s", heading, ink, Width - 130, 10);
                DrawText(g, "左摇杆 移动 · 右摇杆 上下/横向滚动 · A 左键/拖动 · B 右键 · Y 键盘", body, ink, 18, 43);
                return;
            }
            DrawText(g, "Gamepad2KBM", heading, ink, 20, 12);
            DrawText(g, "QWERTY 分半 · LT / RT 确认 · LB + RB 连按两次退出", body, ink, 335, 17);
            DrawWheel(g, 240, 247, engine.LeftWheel, (engine.LeftNumeric ? "数字" : "左半字母") + " / LT 确认", leftColor);
            DrawWheel(g, 720, 247, engine.RightWheel, (engine.RightSymbols ? "符号" : "右半字母") + " / RT 确认", rightColor);
            DrawText(g, "左右摇杆选字，LT / RT 确认 · A 空格选词 · X 退格 · B Esc · Y 返回鼠标", body, ink, 24, 462);
            DrawText(g, "View 左盘字母/数字 · Menu 右盘字母/符号 · LB/RB 翻页 · L3 回车 · R3 Tab · F8 停用", body, ink, 24, 495);
        }

        private void DrawWheel(Graphics g, int cx, int cy, LetterWheel wheel, string label, Color accent)
        {
            const float outer = 143, inner = 109, middle = 126;
            float step = 360f / wheel.Letters.Length;
            for (int i = 0; i < wheel.Letters.Length; i++)
            {
                float start = -90 + i * step - step / 2;
                using (var path = new GraphicsPath())
                {
                    path.AddArc(cx - outer, cy - outer, outer * 2, outer * 2, start + 0.6f, step - 1.2f);
                    path.AddArc(cx - inner, cy - inner, inner * 2, inner * 2, start + step - 0.6f, -step + 1.2f);
                    path.CloseFigure();
                    using (var fill = new SolidBrush(wheel.Selected == i ? accent : Color.FromArgb(35, 45, 63)))
                        g.FillPath(fill, path);
                }
                double angle = (-90 + i * step) * Math.PI / 180;
                string text = wheel.Letters[i].ToString();
                SizeF size = g.MeasureString(text, letter);
                DrawText(g, text, letter, wheel.Selected == i ? BackColor : ink,
                    cx + (float)Math.Cos(angle) * middle - size.Width / 2,
                    cy + (float)Math.Sin(angle) * middle - size.Height / 2);
                if (wheel.Literal || wheel.Letters == Qwerty.Digits) continue;
                using (var path = new GraphicsPath())
                {
                    path.AddArc(cx - 190, cy - 190, 380, 380, start + 0.6f, step - 1.2f);
                    path.AddArc(cx - 150, cy - 150, 300, 300, start + step - 0.6f, -step + 1.2f);
                    path.CloseFigure();
                    using (var fill = new SolidBrush(wheel.Selected == i ? Color.FromArgb(60, accent) : Color.FromArgb(28, 37, 51)))
                        g.FillPath(fill, path);
                }
                string annotation = Xiaohe.Label(wheel.Letters[i]);
                SizeF annotationSize = g.MeasureString(annotation, phonetic);
                DrawText(g, annotation, phonetic, accent,
                    cx + (float)Math.Cos(angle) * 170 - annotationSize.Width / 2,
                    cy + (float)Math.Sin(angle) * 170 - annotationSize.Height / 2);
            }
            CenterText(g, label, heading, accent, cx, cy - 48);
            CenterText(g, wheel.Selected < 0 ? "·" : wheel.Letters[wheel.Selected].ToString(), big, ink, cx, cy - 14);
            CenterText(g, "推杆选字 · 扳机确认", body, ink, cx, cy + 53);
        }
        private static void DrawText(Graphics g, string text, Font font, Color color, float x, float y)
        { using (var brush = new SolidBrush(color)) g.DrawString(text, font, brush, x, y); }
        private static void CenterText(Graphics g, string text, Font font, Color color, float x, float y)
        { DrawText(g, text, font, color, x - g.MeasureString(text, font).Width / 2, y); }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { body.Dispose(); heading.Dispose(); letter.Dispose(); big.Dispose(); phonetic.Dispose(); }
            base.Dispose(disposing);
        }
    }

    internal static class StartupRegistration
    {
        private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string Name = "Gamepad2KBM";
        private static string Command { get { return "\"" + Application.ExecutablePath + "\""; } }
        public static bool IsEnabled
        {
            get
            {
                using (var key = Registry.CurrentUser.OpenSubKey(KeyPath))
                    return key != null && String.Equals(key.GetValue(Name) as string, Command, StringComparison.OrdinalIgnoreCase);
            }
        }
        public static void SetEnabled(bool enabled)
        {
            using (var key = Registry.CurrentUser.CreateSubKey(KeyPath))
            {
                if (enabled) key.SetValue(Name, Command, RegistryValueKind.String);
                else key.DeleteValue(Name, false);
            }
        }
    }

    internal sealed class BridgeContext : ApplicationContext
    {
        private readonly Engine engine;
        private readonly Overlay overlay;
        private readonly NotifyIcon tray;
        private readonly System.Windows.Forms.Timer timer;
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly RumbleFeedback feedback;
        private readonly string logPath;
        private bool leftTrigger, rightTrigger, f8Down, stopping;
        private int selectedSlot = -1;
        private int shownTapCount;
        private double nextDiscovery, nextPaint, nextCursorWarning;
        private Mode shownMode = Mode.Standby;

        public BridgeContext(Settings settings, string directory)
        {
            logPath = Path.Combine(directory, "gamepad2kbm.log");
            engine = new Engine(settings); overlay = new Overlay(engine);
            feedback = new RumbleFeedback(SendRumble);
            var menu = new ContextMenuStrip();
            menu.Items.Add("停用控制 (F8)", null, delegate { StopControl("Tray stop"); });
            menu.Items.Add("测试震动", null, delegate { feedback.Pulse(selectedSlot, clock.Elapsed.TotalSeconds, 0.36, 18000, 24000); });
            menu.Items.Add("打开配置（修改后重启）", null, delegate { Process.Start("notepad.exe", Path.Combine(directory, "settings.json")); });
            menu.Items.Add("打开日志", null, delegate { Process.Start("notepad.exe", logPath); });
            var startupItem = new ToolStripMenuItem("开机自启") { Checked = StartupRegistration.IsEnabled };
            startupItem.Click += delegate {
                StartupRegistration.SetEnabled(!StartupRegistration.IsEnabled);
                startupItem.Checked = StartupRegistration.IsEnabled;
            };
            menu.Items.Add(startupItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("退出 Gamepad2KBM", null, delegate { ExitThread(); });
            tray = new NotifyIcon { Icon = SystemIcons.Application, Text = "Gamepad2KBM · 待机", ContextMenuStrip = menu, Visible = true };
            tray.ShowBalloonTip(5000, "Gamepad2KBM 已启动", "LB + RB 同时按下再松开，连续两次进入鼠标；Y 键盘；F8 停用。", ToolTipIcon.Info);
            timer = new System.Windows.Forms.Timer { Interval = 10 };
            timer.Tick += Tick;
            Native.timeBeginPeriod(1); timer.Start();
            Log("Started; LB+RB double tap; left stick mouse, right stick scroll; LT/RT confirm, split QWERTY wheels");
        }

        private void Tick(object sender, EventArgs args)
        {
            double now = clock.Elapsed.TotalSeconds;
            try
            {
                feedback.Update(now);
                bool f8 = (Native.GetAsyncKeyState(0x77) & 0x8000) != 0;
                if (f8 && !f8Down) StopControl("F8");
                f8Down = f8;
                XState state = new XState();
                bool connected = false;
                if (engine.Mode == Mode.Standby)
                {
                    // Poll every connected slot: virtual and physical controllers can coexist.
                    // The slot receiving the activation chord wins.
                    int fallback = -1;
                    XState fallbackState = new XState();
                    for (int i = 0; i < 4; i++)
                    {
                        XState candidate;
                        if (Native.XInputGetState((uint)i, out candidate) != 0) continue;
                        if (fallback < 0 || i == selectedSlot) { fallback = i; fallbackState = candidate; }
                        if (((Buttons)candidate.Buttons & Engine.ToggleButtons) == Engine.ToggleButtons)
                        { fallback = i; fallbackState = candidate; break; }
                    }
                    if (fallback >= 0)
                    {
                        if (selectedSlot != fallback)
                        {
                            feedback.Stop();
                            selectedSlot = fallback; leftTrigger = rightTrigger = false;
                            Execute(engine.Stop());
                            engine.Step(new Pad(), now);
                            Log("Controller slot " + selectedSlot);
                        }
                        state = fallbackState; connected = true;
                    }
                }
                else connected = selectedSlot >= 0 && Native.XInputGetState((uint)selectedSlot, out state) == 0;

                if (!connected)
                {
                    if (selectedSlot >= 0)
                    { StopControl("Controller disconnected"); selectedSlot = -1; }
                    if (now >= nextDiscovery)
                    { tray.Text = "Gamepad2KBM · 等待 Xbox 手柄"; nextDiscovery = now + 2; }
                    return;
                }
                bool leftNumeric = engine.LeftNumeric, rightSymbols = engine.RightSymbols;
                Execute(engine.Step(state.ToPad(ref leftTrigger, ref rightTrigger), now));
                if (engine.Toggle.Count != shownTapCount)
                {
                    shownTapCount = engine.Toggle.Count;
                    if (shownTapCount > 0) Log("LB+RB " + shownTapCount + "/2; slot " + selectedSlot);
                }
                if (engine.Mode != shownMode)
                {
                    shownMode = engine.Mode;
                    overlay.RefreshMode(selectedSlot);
                    tray.Text = "Gamepad2KBM · " + (shownMode == Mode.Mouse ? "鼠标" : shownMode == Mode.Keyboard ? "键盘" : "待机");
                    Log("Mode " + shownMode + "; slot " + selectedSlot);
                    feedback.Pulse(selectedSlot, clock.Elapsed.TotalSeconds, shownMode == Mode.Standby ? 0.12 : shownMode == Mode.Mouse ? 0.24 : 0.36, 18000, 24000);
                }
                else if (engine.Mode == Mode.Keyboard && (leftNumeric != engine.LeftNumeric || rightSymbols != engine.RightSymbols))
                    feedback.Pulse(selectedSlot, clock.Elapsed.TotalSeconds, 0.165, 12000, 16000);
                if (now >= nextPaint)
                { if (overlay.Visible) overlay.Invalidate(); nextPaint = now + 0.033; }
            }
            catch (Exception ex)
            {
                StopControl("Input error: " + ex.Message);
                tray.ShowBalloonTip(5000, "Gamepad2KBM 已停用", ex.Message, ToolTipIcon.Error);
            }
        }

        private void Execute(List<Command> commands)
        {
            Execute(commands, Native.Execute, delegate(CursorMoveException ex) {
                double now = clock.Elapsed.TotalSeconds;
                if (now < nextCursorWarning) return;
                Log("Cursor movement unavailable; keeping current mode and retrying on next movement: " + ex.Message);
                nextCursorWarning = now + 5;
            });
        }

        internal static void Execute(List<Command> commands, Action<Command> execute, Action<CursorMoveException> cursorFailed)
        {
            foreach (Command command in commands)
            {
                try { execute(command); }
                catch (CursorMoveException ex)
                {
                    if (command.Kind != CommandKind.Move) throw;
                    // A transient cursor API failure must not discard later button releases.
                    cursorFailed(ex);
                }
            }
        }

        private void SendRumble(int slot, ushort left, ushort right)
        {
            var vibration = new XVibration { LeftMotor = left, RightMotor = right };
            uint result = Native.XInputSetState((uint)slot, ref vibration);
            if (left != 0 || right != 0) Log("Rumble slot " + slot + "; result " + result);
        }

        private void StopControl(string reason)
        {
            feedback.Stop();
            foreach (Command command in engine.Stop())
            { try { Native.Execute(command); } catch (Exception ex) { Log("Release failed: " + ex.Message); } }
            leftTrigger = rightTrigger = false;
            shownMode = Mode.Standby; overlay.RefreshMode(selectedSlot);
            tray.Text = "Gamepad2KBM · 待机"; Log(reason);
        }
        private void Log(string text)
        { File.AppendAllText(logPath, DateTime.Now.ToString("s") + " " + text + Environment.NewLine, Encoding.UTF8); }
        protected override void ExitThreadCore()
        {
            if (!stopping)
            {
                stopping = true; timer.Stop(); StopControl("Exited");
                timer.Dispose(); tray.Visible = false; tray.ContextMenuStrip.Dispose(); tray.Dispose();
                overlay.Dispose(); Native.timeEndPeriod(1);
            }
            base.ExitThreadCore();
        }
    }

    public static class Program
    {
        [STAThread]
        public static int Main(string[] args)
        {
            Native.SetProcessDPIAware();
            try { Native.SetThreadDpiAwarenessContext(new IntPtr(-4)); } catch (EntryPointNotFoundException) { }
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            if (args.Length > 0 && (args[0] == "--enable-startup" || args[0] == "--disable-startup"))
            {
                StartupRegistration.SetEnabled(args[0] == "--enable-startup");
                return 0;
            }
            if (args.Length > 0 && args[0] == "--probe")
            {
                var slots = new List<object>();
                for (int i = 0; i < 4; i++)
                {
                    XState state; uint error = Native.XInputGetState((uint)i, out state);
                    slots.Add(new { Slot = i, Connected = error == 0, Error = error, Packet = state.Packet,
                        Buttons = state.Buttons, LT = state.LT, RT = state.RT,
                        LX = state.LX, LY = state.LY, RX = state.RX, RY = state.RY });
                }
                File.WriteAllText(args.Length > 1 ? args[1] : "probe.json",
                    new JavaScriptSerializer().Serialize(slots), Encoding.UTF8);
                return 0;
            }
            bool created;
            using (var mutex = new Mutex(true, "Local\\Gamepad2KBM.Controller", out created))
            {
                if (!created) { MessageBox.Show("Gamepad2KBM 已在运行，可从系统托盘退出。", "Gamepad2KBM"); return 1; }
                try
                {
                    string directory = AppDomain.CurrentDomain.BaseDirectory;
                    string config = Path.Combine(directory, "settings.json");
                    var serializer = new JavaScriptSerializer();
                    Settings settings = File.Exists(config) ? serializer.Deserialize<Settings>(File.ReadAllText(config)) : new Settings();
                    if (settings == null) throw new ArgumentException("settings.json must contain a settings object");
                    settings.Validate();
                    if (!File.Exists(config)) File.WriteAllText(config, serializer.Serialize(settings));
                    using (var context = new BridgeContext(settings, directory)) Application.Run(context);
                    return 0;
                }
                catch (Exception ex) { MessageBox.Show(ex.Message, "Gamepad2KBM 启动失败"); return 1; }
                finally { mutex.ReleaseMutex(); }
            }
        }
    }
}
