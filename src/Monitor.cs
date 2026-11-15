using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace Gamepad2KBM
{
    public sealed class ControllerMonitor : Form
    {
        private readonly Timer timer = new Timer { Interval = 25 };
        private readonly TextBox history = new TextBox();
        private readonly Font titleFont = new Font("Microsoft YaHei UI", 16, FontStyle.Bold);
        private readonly Font bodyFont = new Font("Microsoft YaHei UI", 10);
        private readonly Font smallFont = new Font("Microsoft YaHei UI", 9);
        private readonly Color accent = Color.FromArgb(93, 225, 173);
        private readonly bool[] connected = new bool[4];
        private readonly XState[] states = new XState[4];
        private readonly ushort[] previousButtons = new ushort[4];
        private readonly byte[] previousTriggers = new byte[4];
        private readonly ToggleGesture[] gestures = { new ToggleGesture(), new ToggleGesture(), new ToggleGesture(), new ToggleGesture() };
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "monitor.log");
        private int selected = -1;
        private string latest = "请按任意手柄按钮，看这里显示的名称";

        public ControllerMonitor()
        {
            Text = "Gamepad2KBM 手柄按键监听器 — LB + RB 连按两次";
            ClientSize = new Size(840, 670); StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedSingle; MaximizeBox = false;
            BackColor = Color.FromArgb(20, 26, 38); ForeColor = Color.FromArgb(231, 237, 248);
            DoubleBuffered = true; AutoScaleMode = AutoScaleMode.None;
            history.SetBounds(20, 505, 800, 145);
            history.Multiline = true; history.ReadOnly = true; history.ScrollBars = ScrollBars.Vertical;
            history.BackColor = Color.FromArgb(30, 39, 54); history.ForeColor = ForeColor;
            history.Font = smallFont; history.BorderStyle = BorderStyle.FixedSingle;
            Controls.Add(history);
            Log("监听器已打开；LB + RB 同时按下、松开，0.6 秒内再按一次，切换鼠标模式。");
            timer.Tick += Poll; timer.Start();
            FormClosed += delegate { timer.Stop(); timer.Dispose(); titleFont.Dispose(); bodyFont.Dispose(); smallFont.Dispose(); };
        }

        private void Poll(object sender, EventArgs args)
        {
            int first = -1;
            for (int i = 0; i < 4; i++)
            {
                XState state;
                bool online = Native.XInputGetState((uint)i, out state) == 0;
                if (online != connected[i]) Log("手柄 " + (i + 1) + (online ? " 已连接" : " 已断开"));
                connected[i] = online; states[i] = state;
                if (!online) { previousButtons[i] = 0; previousTriggers[i] = 0; gestures[i].Reset(); continue; }
                if (first < 0) first = i;
                int oldCount = gestures[i].Count;
                bool complete = gestures[i].Update((Buttons)state.Buttons, clock.Elapsed.TotalSeconds);
                if (complete) Log("手柄 " + (i + 1) + " LB+RB 第二次（2/2）：已识别双击组合");
                else if (gestures[i].Count == 1 && oldCount != 1)
                    Log("手柄 " + (i + 1) + " LB+RB 第一次（1/2）：松开后，0.6 秒内再按一次");
                ushort pressed = (ushort)(state.Buttons & ~previousButtons[i]);
                byte triggers = (byte)((state.LT > 100 ? 1 : 0) | (state.RT > 100 ? 2 : 0));
                byte newTriggers = (byte)(triggers & ~previousTriggers[i]);
                if (pressed != 0 || newTriggers != 0)
                {
                    selected = i;
                    latest = Names(pressed);
                    if ((newTriggers & 1) != 0) latest += " LT（左扳机）";
                    if ((newTriggers & 2) != 0) latest += " RT（右扳机）";
                    latest = latest.Trim();
                    Log("手柄 " + (i + 1) + " 按下：" + latest);
                }
                previousButtons[i] = state.Buttons; previousTriggers[i] = triggers;
            }
            if (selected < 0 || !connected[selected]) selected = first;
            Invalidate();
        }

        private static string Names(ushort buttons)
        {
            string result = "";
            int[] masks = { 1, 2, 4, 8, 16, 32, 64, 128, 256, 512, 4096, 8192, 16384, 32768 };
            string[] names = { "十字键↑", "十字键↓", "十字键←", "十字键→", "Menu / Start（三横线）",
                "View / Back（重叠方框）", "L3（按下左摇杆）", "R3（按下右摇杆）", "LB", "RB", "A", "B", "X", "Y" };
            for (int i = 0; i < masks.Length; i++) if ((buttons & masks[i]) != 0) result += names[i] + "  ";
            return result.Trim();
        }

        private void Log(string text)
        {
            string line = DateTime.Now.ToString("HH:mm:ss") + "  " + text + Environment.NewLine;
            if (history.TextLength > 16000) history.Clear();
            history.AppendText(line); File.AppendAllText(logPath, line, Encoding.UTF8);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); Graphics g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
            TextAt(g, "手柄按键监听器", titleFont, ForeColor, 20, 14);
            TextAt(g, "背键可绑定 View / Menu：键盘模式分别切换左盘数字、右盘符号", bodyFont, ForeColor, 20, 49);
            for (int i = 0; i < 4; i++)
                TextAt(g, "● 手柄 " + (i + 1) + (connected[i] ? " 已连接" : " 等待连接"),
                    bodyFont, connected[i] ? accent : Color.FromArgb(135, 146, 165), 20 + i * 203, 83);
            XState state = selected < 0 ? new XState() : states[selected];
            using (var fill = new SolidBrush(Color.FromArgb(30, 39, 54))) g.FillRectangle(fill, 20, 123, 800, 313);
            Button(g, state, Buttons.LB, "LB", new Rectangle(92, 136, 135, 31));
            Button(g, state, Buttons.RB, "RB", new Rectangle(612, 136, 135, 31));
            Trigger(g, "LT", state.LT, 92, 177); Trigger(g, "RT", state.RT, 612, 177);
            Stick(g, state.LX, state.LY, "左摇杆 / L3", (state.Buttons & 64) != 0, 150, 267);
            Stick(g, state.RX, state.RY, "右摇杆 / R3", (state.Buttons & 128) != 0, 498, 347);
            Button(g, state, Buttons.Back, "View / Back", new Rectangle(278, 231, 122, 40));
            Button(g, state, Buttons.Start, "Menu / Start", new Rectangle(428, 231, 122, 40));
            using (var pen = new Pen(accent, 2))
            {
                g.DrawRectangle(pen, 316, 202, 15, 12); g.DrawRectangle(pen, 323, 208, 15, 12);
                for (int i = 0; i < 3; i++) g.DrawLine(pen, 480, 204 + i * 6, 500, 204 + i * 6);
            }
            TextAt(g, "左侧小键", smallFont, accent, 301, 276);
            TextAt(g, "右侧小键", smallFont, accent, 451, 276);
            Button(g, state, Buttons.Y, "Y", new Rectangle(658, 220, 44, 35));
            Button(g, state, Buttons.X, "X", new Rectangle(611, 258, 44, 35));
            Button(g, state, Buttons.B, "B", new Rectangle(705, 258, 44, 35));
            Button(g, state, Buttons.A, "A", new Rectangle(658, 296, 44, 35));
            Button(g, state, Buttons.Up, "↑", new Rectangle(258, 320, 38, 30));
            Button(g, state, Buttons.Left, "←", new Rectangle(217, 353, 38, 30));
            Button(g, state, Buttons.Right, "→", new Rectangle(299, 353, 38, 30));
            Button(g, state, Buttons.Down, "↓", new Rectangle(258, 386, 38, 30));
            TextAt(g, selected < 0 ? "当前等待手柄：按 Xbox 键唤醒，确认蓝牙或 USB 连接。" : "最近按下：" + latest,
                bodyFont, selected < 0 ? Color.FromArgb(255, 205, 125) : accent, 20, 448);
            int taps = selected < 0 ? 0 : gestures[selected].Count;
            TextAt(g, "LB + RB 按下、松开，连续两次（0.6 秒内）  ·  当前计数 " + taps + "/2", bodyFont, accent, 20, 476);
        }

        private void Button(Graphics g, XState state, Buttons button, string label, Rectangle rectangle)
        {
            bool down = ((Buttons)state.Buttons & button) != 0;
            using (var brush = new SolidBrush(down ? accent : Color.FromArgb(52, 66, 88))) g.FillRectangle(brush, rectangle);
            SizeF size = g.MeasureString(label, bodyFont);
            TextAt(g, label, bodyFont, down ? BackColor : ForeColor,
                rectangle.X + (rectangle.Width - size.Width) / 2, rectangle.Y + (rectangle.Height - size.Height) / 2);
        }
        private void Trigger(Graphics g, string label, byte value, int x, int y)
        {
            TextAt(g, label + " " + value, smallFont, ForeColor, x, y);
            using (var brush = new SolidBrush(accent)) g.FillRectangle(brush, x + 56, y + 5, 79 * value / 255f, 12);
        }
        private void Stick(Graphics g, short x, short y, string label, bool pressed, int cx, int cy)
        {
            using (var pen = new Pen(pressed ? accent : Color.FromArgb(105, 122, 148), 2))
            {
                g.DrawEllipse(pen, cx - 34, cy - 34, 68, 68);
                g.DrawLine(pen, cx - 34, cy, cx + 34, cy); g.DrawLine(pen, cx, cy - 34, cx, cy + 34);
            }
            using (var brush = new SolidBrush(accent))
                g.FillEllipse(brush, cx + x / 32768f * 28 - 5, cy - y / 32768f * 28 - 5, 10, 10);
            TextAt(g, label, smallFont, ForeColor, cx - 46, cy + 37);
            TextAt(g, x + " / " + y, smallFont, ForeColor, cx - 57, cy + 58);
        }
        private static void TextAt(Graphics g, string text, Font font, Color color, float x, float y)
        { using (var brush = new SolidBrush(color)) g.DrawString(text, font, brush, x, y); }
    }

    public static class MonitorProgram
    {
        [STAThread]
        public static void Main()
        {
            Native.SetProcessDPIAware();
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new ControllerMonitor());
        }
    }
}
