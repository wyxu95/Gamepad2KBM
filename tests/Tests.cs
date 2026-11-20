using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace Gamepad2KBM
{
    public static class Tests
    {
        private static int checks;
        private static void Check(bool condition, string message)
        { checks++; if (!condition) throw new Exception(message); }
        private static bool Has(List<Command> commands, CommandKind kind, int code)
        { return commands.Exists(delegate(Command c) { return c.Kind == kind && c.Code == code; }); }
        private static void Near(double actual, double expected, string label)
        { Check(Math.Abs(actual - expected) < 0.00001, label + ": " + actual + " != " + expected); }
        private static Pad P(Buttons buttons) { return new Pad { Buttons = buttons }; }
        private static Engine MouseMode()
        {
            var e = new Engine(new Settings());
            e.Step(new Pad(), 0);
            e.Step(P(Buttons.LB | Buttons.RB), 0.01);
            e.Step(new Pad(), 0.05);
            e.Step(P(Buttons.LB | Buttons.RB), 0.10);
            e.Step(new Pad(), 0.11);
            e.Step(new Pad(), 0.51);
            Check(e.Mode == Mode.Mouse, "Activation chord enters mouse mode");
            return e;
        }
        private static Engine KeyboardMode()
        {
            var e = MouseMode();
            var commands = e.Step(P(Buttons.Y), 0.6);
            Check(e.Mode == Mode.Keyboard, "Y enters keyboard");
            Check(Has(commands, CommandKind.MouseDown, 0) && Has(commands, CommandKind.MouseUp, 0), "Y focuses target with a click");
            e.Step(new Pad(), 0.61);
            return e;
        }
        private static Pad Direction(char left, char right, Buttons buttons)
        {
            double l = Qwerty.LeftLetters.IndexOf(left) * 2 * Math.PI / Qwerty.LeftLetters.Length;
            double r = Qwerty.RightLetters.IndexOf(right) * 2 * Math.PI / Qwerty.RightLetters.Length;
            return new Pad { Buttons = buttons, LX = Math.Sin(l), LY = Math.Cos(l), RX = Math.Sin(r), RY = Math.Cos(r) };
        }

        private static void Acceleration()
        {
            var s = new Settings(); var mouse = new TimeMouse(s);
            mouse.Step(1, 0, 0, 0.01); Near(mouse.Speed, 100, "Initial speed");
            mouse.Step(1, 0, 0.08, 0.01); Near(mouse.Speed, 100, "Shortened delay at fixed deflection");
            mouse.Step(1, 0, 0.30, 0.01); Check(mouse.Speed > 300, "Early acceleration is faster than previous initial speed");
            mouse.Step(1, 0, 0.49, 0.01); Near(mouse.Speed, 850, "Smoothstep midpoint at fixed deflection");
            mouse.Step(1, 0, 0.90, 0.01); Near(mouse.Speed, 1600, "Full speed reached earlier at fixed deflection");
            mouse.Step(1, 0, 9, 0.01); Near(mouse.Speed, 1600, "Speed is capped");
            mouse.Step(0, 1, 9.01, 0.01); Near(mouse.Speed, 1600, "Turning preserves elapsed time");
            var stopped = mouse.Step(0.12, 0, 9.02, 0.01);
            Near(mouse.Speed, 0, "Deadzone stops immediately");
            Check(stopped.X == 0 && stopped.Y == 0, "No movement at deadzone boundary");
            mouse.Step(1, 0, 9.03, 0.01); Near(mouse.Speed, 100, "Re-pushing resets timer");
            mouse.Step(0.56, 0, 9.04, 0.01); Near(mouse.Speed, 6.25, "Reducing tilt immediately reduces current speed");
            mouse.Reset(); mouse.Step(0.56, 0, 0, 0.01); Near(mouse.Speed, 6.25, "Partial push receives stronger initial slowdown");
            mouse.Step(0.56, 0, 5, 0.01); Near(mouse.Speed, 100, "Holding half normalized deflection stays at one sixteenth of maximum speed");
            mouse.Step(0.34, 0, 5.01, 0.01); Near(mouse.Speed, 6.25, "Backing off an accelerated stick slows immediately");
            mouse.Step(1, 0, 5.02, 0.01); Near(mouse.Speed, 1600, "Full deflection uses full time-accelerated speed");
            mouse.Reset(); var capped = mouse.Step(1, 0, 0, 30);
            Check(capped.X == 5, "A stalled frame cannot cause a large jump");
            mouse.Reset(); int pixels = 0;
            for (int i = 0; i < 100; i++) pixels += mouse.Step(0.40, 0, 0, 0.01).X;
            Check(pixels == 1, "Fractional pixels accumulate at low speed");
            mouse.Reset(); mouse.Step(1, 1, 0, 0.01); Near(mouse.Speed, 100, "Diagonal magnitude is capped");
        }

        private static void Modes()
        {
            var e = new Engine(new Settings());
            Check(e.Step(P(Buttons.A), 0).Count == 0, "Standby never injects clicks");
            e.Step(new Pad(), 0.01);
            e.Step(P(Buttons.Back | Buttons.Start), 0.02);
            e.Step(P(Buttons.Back | Buttons.Start), 0.2);
            Check(e.Mode == Mode.Standby, "Hardware mode chord does not activate Gamepad2KBM");
            e.Step(new Pad(), 0.21);
            e.Step(P(Buttons.LB | Buttons.RB), 0.3);
            Check(e.Mode == Mode.Standby && e.Toggle.Count == 1, "First chord arms double tap without activating");
            e.Step(P(Buttons.LB | Buttons.RB), 10);
            Check(e.Mode == Mode.Standby, "Holding a chord does not count as a double tap");
            e.Step(new Pad(), 10.01);
            e.Step(P(Buttons.LB | Buttons.RB), 10.02);
            e.Step(new Pad(), 10.03);
            e.Step(P(Buttons.LB | Buttons.RB), 10.04);
            Check(e.Mode == Mode.Mouse && e.Toggle.Count == 2, "Two separate chord taps activate");
            e.Step(P(Buttons.LB | Buttons.RB), 10.05);
            Check(e.Mode == Mode.Mouse, "Held second chord never retriggers");
            Check(e.Step(P(Buttons.A), 10.1).Count == 0, "Transition waits for buttons to be released");
            e.Step(new Pad(), 10.2);
            Check(Has(e.Step(P(Buttons.A), 10.3), CommandKind.MouseDown, 0), "A supports held drag");
            Check(e.Step(P(Buttons.A), 10.4).Count == 0, "Held mouse button is not re-injected");
            Check(Has(e.Stop(), CommandKind.MouseUp, 0), "Disconnect releases a held drag");
            Check(e.Mode == Mode.Standby, "Disconnect returns to standby");
            Check(e.Step(P(Buttons.A), 11).Count == 0, "Reconnect held A is harmless");

            e = MouseMode();
            e.Step(P(Buttons.B), 0.55);
            var stopped = e.Step(P(Buttons.LB | Buttons.RB), 0.56);
            Check(Has(stopped, CommandKind.MouseUp, 1), "Starting chord releases right mouse button");
            e.Step(new Pad(), 0.57);
            e.Step(P(Buttons.LB | Buttons.RB), 0.7);
            Check(e.Mode == Mode.Standby, "Same chord exits active control");

            e = MouseMode();
            var commands = e.Step(new Pad { LX = 1 }, 0.52);
            Check(commands.Exists(delegate(Command c) { return c.Kind == CommandKind.Move && c.X > 0; }), "Left stick moves mouse");
            commands = e.Step(new Pad { RY = 1, RX = 1 }, 0.54);
            Check(commands.Exists(delegate(Command c) { return c.Kind == CommandKind.Wheel && c.Code > 0; }), "Right stick scrolls vertically");
            Check(commands.Exists(delegate(Command c) { return c.Kind == CommandKind.HorizontalWheel && c.Code > 0; }), "Right stick scrolls horizontally");
            Check(!commands.Exists(delegate(Command c) { return c.Kind == CommandKind.Move; }), "Right stick does not move mouse");
            commands = e.Step(new Pad { RY = -1, RX = -1 }, 0.56);
            Check(commands.Exists(delegate(Command c) { return c.Kind == CommandKind.Wheel && c.Code < 0; }), "Downward scroll");
            Check(commands.Exists(delegate(Command c) { return c.Kind == CommandKind.HorizontalWheel && c.Code < 0; }), "Leftward scroll");
        }

        private static void Wheels()
        {
            var s = new Settings(); var wheel = new LetterWheel(s, Qwerty.LeftLetters);
            var allKeys = new HashSet<char>();
            foreach (string half in new[] { Qwerty.LeftLetters, Qwerty.RightLetters })
            {
                var halfWheel = new LetterWheel(s, half);
                for (int i = 0; i < half.Length; i++)
                {
                    Check(allKeys.Add(half[i]), "Key belongs to one QWERTY half: " + half[i]);
                    halfWheel.Reset(); double angle = i * 2 * Math.PI / half.Length;
                    halfWheel.Update(Math.Sin(angle), Math.Cos(angle));
                    Check(halfWheel.Selected == i && halfWheel.VirtualKey == (half[i] == ';' ? 0xBA : half[i]), "Every split keyboard key is reachable: " + half[i]);
                }
            }
            Check(allKeys.Count == 27 && Qwerty.LeftLetters == "QWERASDFZXCVB" && Qwerty.RightLetters == "TYUIOPGHJKL;NM", "Balanced QWERTY halves cover all letters plus semicolon");
            Check(Qwerty.LeftLetters.Length == 13 && Qwerty.RightLetters.Length == 14, "Keyboard halves differ by one key");
            wheel.Update(0, 0); Check(wheel.VirtualKey == 0, "Neutral cannot type a stale letter");
            wheel.Update(0, 1);
            double boundary = Math.PI / wheel.Letters.Length + Math.PI / 180;
            wheel.Update(Math.Sin(boundary), Math.Cos(boundary));
            Check(wheel.Selected == 0, "Sector boundary jitter is suppressed");
            double beyond = boundary + 3 * Math.PI / 180;
            wheel.Update(Math.Sin(beyond), Math.Cos(beyond));
            Check(wheel.Selected == 1, "Deliberate sector change succeeds");

            var e = KeyboardMode();
            var commands = e.Step(Direction('R', 'I', Buttons.None), 0.7);
            Check(commands.Count == 0, "Pushing selects without typing");
            commands = e.Step(Direction('R', 'I', Buttons.LT | Buttons.RT), 0.8);
            Check(commands.Count == 2 && commands[0].Code == 'R' && commands[1].Code == 'I', "Simultaneous trigger presses confirm left then right");
            Check(e.Step(Direction('R', 'I', Buttons.LT | Buttons.RT), 0.81).Count == 0, "Held triggers confirm once");
            e.Step(Direction('R', 'I', Buttons.None), 0.82);
            Check(e.Step(Direction('R', 'I', Buttons.LT | Buttons.RT), 0.83).Count == 2, "Repressing triggers can repeat selected letters");
            commands = e.Step(new Pad(), 0.9);
            Check(commands.Count == 0, "Releasing sticks never commits letters");
            Check(!commands.Exists(delegate(Command c) { return c.Kind == CommandKind.Move; }), "Keyboard mode locks controller cursor movement");
            Check(e.Step(P(Buttons.LT | Buttons.RT), 1).Count == 0, "Centered sticks cannot be confirmed by triggers");
            e.Step(new Pad(), 1.1);
            Check(Has(e.Step(P(Buttons.A), 1.2), CommandKind.Key, 0x20), "A commits IME candidate with Space");
            Check(Has(e.Step(P(Buttons.X), 1.3), CommandKind.Key, 0x08), "X sends Backspace");
            Check(e.Step(P(Buttons.X), 1.5).Count == 0, "Backspace repeat has a delay");
            Check(Has(e.Step(P(Buttons.X), 1.8), CommandKind.Key, 0x08), "Backspace repeats after delay");
            e.Step(P(Buttons.Y), 2);
            Check(e.Mode == Mode.Mouse, "Y returns to mouse");
            Check(e.Mouse.Speed == 0, "Mode change clears acceleration");

            e = KeyboardMode();
            Check(e.Step(P(Buttons.LB), 0.7).Count == 0, "First bumper waits to distinguish paging from a chord");
            Check(Has(e.Step(new Pad(), 0.8), CommandKind.Key, 0x21), "Single LB release pages up");
            e.Step(P(Buttons.RB), 0.9);
            Check(Has(e.Step(new Pad(), 1.0), CommandKind.Key, 0x22), "Single RB release pages down");
            e.Step(P(Buttons.LB), 1.1);
            Check(e.Step(P(Buttons.LB | Buttons.RB), 1.2).Count == 0, "Staggered bumper chord does not page candidates");
            Check(e.Mode == Mode.Keyboard, "First bumper chord keeps keyboard active");
            e.Step(P(Buttons.RB), 1.3);
            e.Step(new Pad(), 1.4);
            e.Step(P(Buttons.LB | Buttons.RB), 1.5);
            Check(e.Mode == Mode.Standby, "Second bumper chord exits keyboard");
            e.Step(new Pad(), 1.6);
            Check(e.Mode == Mode.Standby, "Staggered chord release does not retrigger");

            e = KeyboardMode();
            e.Step(new Pad { LY = 0.46 }, 0.7);
            Check(e.Step(new Pad { LY = 0.43 }, 0.71).Count == 0, "Amplitude jitter does not release a letter");
            Check(Has(e.Step(new Pad { LY = 0.43, Buttons = Buttons.LT }, 0.75), CommandKind.Key, 'Q'), "LT confirms left letter through amplitude jitter");
            Check(e.Step(new Pad(), 0.8).Count == 0, "Returning to center cancels selection without typing");
            Check(e.Step(new Pad { LY = 0.43 }, 0.81).Count == 0 && e.LeftWheel.Selected < 0, "Amplitude jitter cannot rearm below selection threshold");
            Check(e.Step(new Pad(), 0.9).Count == 0, "Neutral jitter cannot type twice");
            e.Step(Direction('R', 'I', Buttons.None), 1);
            e.Step(P(Buttons.LB | Buttons.RB), 1.1);
            e.Step(new Pad(), 1.2);
            Check(e.Step(new Pad(), 1.21).Count == 0, "Mode gesture cancels pending wheel letters");
            e = KeyboardMode();
            e.Step(P(Buttons.LT), 0.7);
            Check(e.Step(Direction('R', 'I', Buttons.LT), 0.8).Count == 0, "Holding a trigger before selection cannot type");
            e.Step(Direction('R', 'I', Buttons.None), 0.9);
            Check(Has(e.Step(Direction('R', 'I', Buttons.LT), 1), CommandKind.Key, 'R'), "Fresh LT press confirms left selection only");
            Check(Xiaohe.Initials['V' - 'A'] == "zh" && Xiaohe.Initials['I' - 'A'] == "ch" && Xiaohe.Initials['U' - 'A'] == "sh", "Xiaohe compound initials use correct physical keys");
            Check(Xiaohe.Finals['K' - 'A'].Contains("ing") && Xiaohe.Finals['L' - 'A'].Contains("iang"), "Xiaohe final annotations use expected keys");

            bool lt = false, rt = false;
            var raw = new XState { LX = -32768, RY = 32767, LT = 150 };
            Pad p = raw.ToPad(ref lt, ref rt);
            Near(p.LX, -1, "Negative XInput endpoint"); Near(p.RY, 1, "Positive XInput endpoint");
            Check((p.Buttons & Buttons.LT) != 0, "Trigger press threshold");
            raw.LT = 100; Check((raw.ToPad(ref lt, ref rt).Buttons & Buttons.LT) != 0, "Trigger hysteresis holds");
            raw.LT = 89; Check((raw.ToPad(ref lt, ref rt).Buttons & Buttons.LT) == 0, "Trigger release threshold");
        }

        private static void Rumble()
        {
            var sent = new List<string>();
            var feedback = new RumbleFeedback(delegate(int slot, ushort left, ushort right) {
                sent.Add(slot + ":" + left + ":" + right);
            });
            feedback.Pulse(2, 1, 0.24, 18000, 24000);
            Check(sent.Count == 1 && sent[0] == "2:18000:24000", "Feedback starts only on the selected controller");
            feedback.Update(1.239);
            Check(sent.Count == 1, "Feedback remains on until its duration expires");
            feedback.Update(1.241);
            Check(sent.Count == 2 && sent[1] == "2:0:0", "Feedback stops both motors at its deadline");
            feedback.Update(2);
            Check(sent.Count == 2, "Expired feedback does not keep sending stop commands");
            feedback.Pulse(1, 3, 0.36, 18000, 24000);
            feedback.Pulse(3, 3.01, 0.165, 12000, 16000);
            Check(sent[3] == "1:0:0" && sent[4] == "3:12000:16000", "A replacement pulse stops the previous controller first");
            feedback.Stop();
            Check(sent.Count == 6 && sent[5] == "3:0:0", "Disconnect or shutdown cancels feedback immediately");
            feedback.Update(4); feedback.Stop();
            Check(sent.Count == 6, "Cancelling a pulse clears its pending timer");
            feedback.Pulse(-1, 5, 0.36, 18000, 24000);
            Check(sent.Count == 6, "Missing controller does not receive feedback");
        }

        private static void Pump()
        { for (int i = 0; i < 8; i++) { Application.DoEvents(); Thread.Sleep(15); } }

        private static Pad SelectKey(LetterWheel wheel, char key, bool left, Buttons buttons)
        {
            double angle = wheel.Letters.IndexOf(key) * 2 * Math.PI / wheel.Letters.Length;
            return left ? new Pad { LX = Math.Sin(angle), LY = Math.Cos(angle), Buttons = buttons } :
                new Pad { RX = Math.Sin(angle), RY = Math.Cos(angle), Buttons = buttons };
        }

        private static void Layers()
        {
            var e = KeyboardMode();
            e.Step(Direction('R', 'I', Buttons.None), 0.7);
            Check(e.Step(P(Buttons.Back | Buttons.LT), 0.8).Count == 0, "Switching layers cancels a pending confirmation");
            Check(e.LeftNumeric && !e.RightSymbols && e.LeftWheel.Selected < 0, "Back changes only the left half to digits");
            e.Step(P(Buttons.Back), 0.81);
            Check(e.LeftNumeric, "Holding a layer key toggles once");
            e.Step(new Pad(), 0.9);
            e.Step(P(Buttons.Start), 1);
            Check(e.LeftNumeric && e.RightSymbols, "Start changes the right half independently");
            double now = 1.1;
            foreach (char digit in Qwerty.Digits)
            {
                e.Step(SelectKey(e.LeftWheel, digit, true, Buttons.None), now);
                Check(Has(e.Step(SelectKey(e.LeftWheel, digit, true, Buttons.LT), now + 0.01), CommandKind.Key, digit), "LT types digit " + digit);
                now += 0.1;
            }
            foreach (char symbol in Qwerty.Symbols)
            {
                e.Step(SelectKey(e.RightWheel, symbol, false, Buttons.None), now);
                var pressed = SelectKey(e.RightWheel, symbol, false, Buttons.RT);
                Check(Has(e.Step(pressed, now + 0.01), CommandKind.Text, symbol), "RT types literal symbol " + symbol);
                Check(e.Step(pressed, now + 0.02).Count == 0, "Held symbol trigger types once");
                now += 0.1;
            }
            Check(Has(e.Step(P(Buttons.L3), now), CommandKind.Key, 0x0D), "L3 remains Enter in alternate layers");
            Check(Has(e.Step(P(Buttons.R3), now + 0.1), CommandKind.Key, 0x09), "R3 remains Tab in alternate layers");
            e.Step(P(Buttons.Back), now + 0.2);
            Check(!e.LeftNumeric && e.RightSymbols && e.LeftWheel.Letters == Qwerty.LeftLetters, "Left half switches back to its QWERTY letters");
            e.Stop();
            Check(!e.LeftNumeric && !e.RightSymbols && !e.RightWheel.Literal && e.RightWheel.Letters == Qwerty.RightLetters, "Stopping resets both layers");
        }

        private static void CursorRecovery()
        {
            foreach (string operation in new[] { "GetCursorPos", "SetCursorPos" })
            {
                var engine = MouseMode();
                engine.Step(new Pad { Buttons = Buttons.A }, 0.7);
                var commands = engine.Step(new Pad { LX = 1 }, 0.8);
                Check(commands.Count >= 2 && commands[0].Kind == CommandKind.Move && Has(commands, CommandKind.MouseUp, 0),
                    "Moving while releasing a drag produces movement and mouse-up");
                var sent = new List<Command>();
                int failures = 0;
                BridgeContext.Execute(commands, delegate(Command command) {
                    if (command.Kind == CommandKind.Move) throw new CursorMoveException(operation, 5);
                    sent.Add(command);
                }, delegate(CursorMoveException ex) {
                    failures++;
                    Check(ex.NativeErrorCode == 5 && ex.Message.Contains(operation), "Cursor failure identifies API and Windows error");
                });
                Check(failures == 1 && engine.Mode == Mode.Mouse, "Cursor failure preserves mouse mode");
                Check(Has(sent, CommandKind.MouseUp, 0), "Cursor failure still releases a held mouse button");
                BridgeContext.Execute(engine.Step(new Pad { LX = 1 }, 0.81), sent.Add,
                    delegate(CursorMoveException ex) { throw new Exception("Unexpected cursor failure"); });
                Check(sent.Exists(delegate(Command c) { return c.Kind == CommandKind.Move; }), "Next tick resumes pointer movement");
            }
            bool propagated = false;
            try
            {
                BridgeContext.Execute(new List<Command> { Command.Key('A') },
                    delegate(Command command) { throw new System.ComponentModel.Win32Exception(5); },
                    delegate(CursorMoveException ex) { throw new Exception("Unexpected cursor failure"); });
            }
            catch (System.ComponentModel.Win32Exception) { propagated = true; }
            Check(propagated, "Other input failures retain the stop-and-release path");
        }

        private sealed class KeyRecorder : TextBox
        {
            public int Escapes, Enters;
            protected override bool ProcessCmdKey(ref Message message, Keys keys)
            {
                if (keys == Keys.Escape) { Escapes++; return true; }
                if (keys == Keys.Enter) { Enters++; return true; }
                return base.ProcessCmdKey(ref message, keys);
            }
        }

        private static void DesktopSmoke()
        {
            Check(Native.InputSize == 40, "x64 INPUT ABI size");
            Check(Marshal.SizeOf(typeof(XState)) == 16, "XINPUT_STATE ABI size");
            Check(Marshal.SizeOf(typeof(XVibration)) == 4, "XINPUT_VIBRATION ABI size");
            Native.Point oldCursor; Native.GetCursorPos(out oldCursor);
            IntPtr oldForeground = Native.GetForegroundWindow();
            try
            {
                using (var target = new Form())
                using (var box = new KeyRecorder())
                using (var overlay = new Overlay(KeyboardMode()))
                {
                    target.Text = "Gamepad2KBM temporary input test";
                    target.StartPosition = FormStartPosition.CenterScreen;
                    target.Size = new Size(500, 200);
                    box.Multiline = true; box.Dock = DockStyle.Fill; box.ImeMode = ImeMode.Disable;
                    target.Controls.Add(box); target.Show(); target.Activate(); box.Focus(); Pump();
                    Check(Native.GetForegroundWindow() == target.Handle, "Temporary test target receives focus");
                    overlay.RefreshMode(0); Pump();
                    IntPtr actual = Native.GetForegroundWindow();
                    var caption = new System.Text.StringBuilder(256); Native.GetWindowText(actual, caption, 256);
                    Console.WriteLine("Focus target=" + target.Handle + " overlay=" + overlay.Handle + " actual=" + actual + " title=" + caption);
                    Check(Native.GetForegroundWindow() == target.Handle, "Overlay preserves foreground input focus");
                    Native.Tap('N'); Native.Tap('I'); Pump();
                    Check(box.Text.ToLowerInvariant() == "ni", "Real SendInput reaches target as letter keys");
                    Native.Tap(0x08); Pump();
                    Check(box.Text.ToLowerInvariant() == "n", "Real Backspace reaches target");
                    bool caps = Control.IsKeyLocked(Keys.CapsLock);
                    Native.Tap(0x1B); Pump();
                    Check(box.Escapes == 1, "Real Escape reaches target through active AHK hooks");
                    Check(Control.IsKeyLocked(Keys.CapsLock) == caps, "Controller Escape does not toggle CapsLock");
                    Native.Tap(0x0D); Pump();
                    Check(box.Enters == 1, "Real Enter reaches target");
                    string beforeSymbols = box.Text;
                    foreach (char symbol in Qwerty.Symbols) Native.Text(symbol);
                    Pump();
                    Check(box.Text == beforeSymbols + Qwerty.Symbols, "All layer symbols reach the real target as literal text");
                    Native.Point before; Native.GetCursorPos(out before);
                    Native.Execute(new Command(CommandKind.Move, 0, 4, -3));
                    Native.Point after; Native.GetCursorPos(out after);
                    Check(after.X == before.X + 4 && after.Y == before.Y - 3, "Real pixel movement matches command");
                    // Test the actual click path against our own disposable input box.
                    Point click = box.PointToScreen(new Point(40, 40));
                    Native.SetCursorPos(click.X, click.Y);
                    int clicked = 0; box.MouseDown += delegate { clicked++; };
                    Native.Execute(new Command(CommandKind.MouseDown, 0, 0, 0));
                    Native.Execute(new Command(CommandKind.MouseUp, 0, 0, 0)); Pump();
                    Check(clicked == 1, "Real mouse click reaches target");
                    string screenshot = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "keyboard-preview.png");
                    using (var bitmap = new Bitmap(overlay.Width, overlay.Height))
                    { overlay.DrawToBitmap(bitmap, overlay.ClientRectangle); bitmap.Save(screenshot); }
                    Console.WriteLine("Preview: " + screenshot);
                }
            }
            finally
            { Native.SetCursorPos(oldCursor.X, oldCursor.Y); if (oldForeground != IntPtr.Zero) Native.SetForegroundWindow(oldForeground); }
        }

        private static void PreviewContrast()
        {
            var keyboard = KeyboardMode();
            keyboard.Step(Direction('R', 'I', Buttons.None), 0.7);
            using (var comparison = new Bitmap(1920, 623))
            using (var canvas = Graphics.FromImage(comparison))
            using (var dark = new SolidBrush(Color.FromArgb(30, 30, 30)))
            {
                canvas.Clear(Color.White);
                canvas.FillRectangle(dark, 960, 0, 960, comparison.Height);
                foreach (var engine in new[] { keyboard, MouseMode() })
                {
                    Size size = engine.Mode == Mode.Keyboard ? new Size(960, 535) : new Size(700, 88);
                    int y = engine.Mode == Mode.Keyboard ? 0 : 535;
                    using (var overlay = new Overlay(engine))
                    using (var bitmap = new Bitmap(size.Width, size.Height))
                    {
                        overlay.Size = size;
                        overlay.DrawToBitmap(bitmap, new Rectangle(Point.Empty, size));
                        if (!overlay.TransparencyKey.IsEmpty) bitmap.MakeTransparent(overlay.TransparencyKey);
                        using (var attributes = new System.Drawing.Imaging.ImageAttributes())
                        {
                            var matrix = new System.Drawing.Imaging.ColorMatrix();
                            matrix.Matrix33 = (float)overlay.Opacity;
                            attributes.SetColorMatrix(matrix);
                            canvas.DrawImage(bitmap, new Rectangle(0, y, size.Width, size.Height),
                                0, 0, size.Width, size.Height, GraphicsUnit.Pixel, attributes);
                            canvas.DrawImage(bitmap, new Rectangle(960, y, size.Width, size.Height),
                                0, 0, size.Width, size.Height, GraphicsUnit.Pixel, attributes);
                        }
                    }
                }
                comparison.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "contrast-preview.png"));
            }
        }

        [STAThread]
        public static int Main(string[] args)
        {
            try
            {
                Native.SetProcessDPIAware();
                Native.SetThreadDpiAwarenessContext(new IntPtr(-4));
                Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
                if (args.Length > 0 && args[0] == "--preview-contrast") { PreviewContrast(); return 0; }
                if (args.Length > 0 && (args[0] == "--preview" || args[0] == "--preview-layers"))
                {
                    var engine = KeyboardMode();
                    bool layers = args[0] == "--preview-layers";
                    if (layers) engine.Step(P(Buttons.Back | Buttons.Start), 0.7);
                    using (var overlay = new Overlay(engine))
                    using (var bitmap = new Bitmap(960, 535))
                    {
                        overlay.Size = bitmap.Size;
                        overlay.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                        bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, layers ? "layers-preview.png" : "keyboard-preview.png"));
                    }
                    return 0;
                }
                Acceleration(); Modes(); Wheels(); Layers(); Rumble(); CursorRecovery();
                bool coreOnly = args.Length > 0 && args[0] == "--core-only";
                if (!coreOnly) DesktopSmoke();
                Console.WriteLine("PASS: " + checks + (coreOnly ? " core checks." : " checks, including real Windows input/focus smoke tests."));
                return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine("FAIL: " + ex); return 1; }
        }
    }
}
