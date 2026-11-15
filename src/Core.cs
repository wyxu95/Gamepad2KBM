using System;
using System.Collections.Generic;

namespace Gamepad2KBM
{
    [Flags]
    public enum Buttons
    {
        None = 0, Up = 1, Down = 2, Left = 4, Right = 8,
        Start = 16, Back = 32, L3 = 64, R3 = 128,
        LB = 256, RB = 512, A = 4096, B = 8192, X = 16384, Y = 32768,
        LT = 65536, RT = 131072
    }

    public struct Pad
    {
        public Buttons Buttons;
        public double LX, LY, RX, RY;
    }

    public sealed class Settings
    {
        public double MouseDeadzone = 0.12;
        public double InitialSpeed = 100;
        public double MaximumSpeed = 1600;
        public double AccelerationDelay = 0.08;
        public double FullSpeedAfter = 0.90;
        public double LetterDeadzone = 0.45;
        public double LetterHysteresisDegrees = 2;

        public void Validate()
        {
            Range(MouseDeadzone, 0, 0.8, "MouseDeadzone");
            Range(InitialSpeed, 1, 10000, "InitialSpeed");
            Range(MaximumSpeed, InitialSpeed, 20000, "MaximumSpeed");
            Range(AccelerationDelay, 0, 10, "AccelerationDelay");
            Range(FullSpeedAfter, AccelerationDelay + 0.01, 20, "FullSpeedAfter");
            Range(LetterDeadzone, 0.1, 0.9, "LetterDeadzone");
            Range(LetterHysteresisDegrees, 0, 5, "LetterHysteresisDegrees");
        }

        private static void Range(double value, double min, double max, string name)
        {
            if (Double.IsNaN(value) || Double.IsInfinity(value) || value < min || value > max)
                throw new ArgumentException(name + " must be between " + min + " and " + max);
        }
    }

    public enum Mode { Standby, Mouse, Keyboard }
    public enum CommandKind { Move, MouseDown, MouseUp, Wheel, HorizontalWheel, Key, Text }

    public struct Command
    {
        public CommandKind Kind;
        public int X, Y, Code;
        public Command(CommandKind kind, int code, int x, int y)
        { Kind = kind; Code = code; X = x; Y = y; }
        public static Command Key(int vk) { return new Command(CommandKind.Key, vk, 0, 0); }
    }

    public sealed class TimeMouse
    {
        private readonly Settings settings;
        private double started = -1, fractionX, fractionY;
        public double Speed { get; private set; }
        public TimeMouse(Settings settings) { this.settings = settings; }

        public void Reset()
        { started = -1; fractionX = fractionY = Speed = 0; }

        public Command Step(double x, double y, double now, double dt)
        {
            double magnitude = Math.Sqrt(x * x + y * y);
            if (magnitude <= settings.MouseDeadzone)
            {
                Reset();
                return new Command(CommandKind.Move, 0, 0, 0);
            }
            if (started < 0) started = now;
            double u = Math.Max(0, Math.Min(1, (now - started - settings.AccelerationDelay) /
                (settings.FullSpeedAfter - settings.AccelerationDelay)));
            double ramp = u * u * (3 - 2 * u);
            double strength = (Math.Min(1, magnitude) - settings.MouseDeadzone) /
                (1 - settings.MouseDeadzone);
            Speed = (settings.InitialSpeed + (settings.MaximumSpeed - settings.InitialSpeed) * ramp) * Math.Pow(strength, 4);
            dt = Math.Max(0, Math.Min(dt, 0.05));
            fractionX += x / magnitude * Speed * dt;
            fractionY -= y / magnitude * Speed * dt;
            int dx = (int)Math.Round(fractionX), dy = (int)Math.Round(fractionY);
            fractionX -= dx; fractionY -= dy;
            return new Command(CommandKind.Move, 0, dx, dy);
        }
    }

    public sealed class LetterWheel
    {
        public string Letters { get; private set; }
        public bool Literal { get; private set; }
        public int Selected { get; private set; }
        private readonly Settings settings;
        public LetterWheel(Settings settings, string letters)
        { this.settings = settings; Letters = letters; Selected = -1; }
        public void Reset() { Selected = -1; }
        public void SetKeys(string keys, bool literal)
        { Letters = keys; Literal = literal; Reset(); }

        public void Update(double x, double y)
        {
            double threshold = Selected < 0 ? settings.LetterDeadzone : settings.LetterDeadzone * 0.65;
            if (Math.Sqrt(x * x + y * y) < threshold)
            { Reset(); return; }
            // Each half follows QWERTY row order, clockwise from twelve o'clock.
            double angle = Math.Atan2(x, y);
            double sector = 2 * Math.PI / Letters.Length;
            if (Selected >= 0)
            {
                double delta = Math.Atan2(Math.Sin(angle - Selected * sector),
                    Math.Cos(angle - Selected * sector));
                if (Math.Abs(delta) <= sector / 2 + settings.LetterHysteresisDegrees * Math.PI / 180)
                    return;
            }
            Selected = ((int)Math.Floor(angle / sector + 0.5) + Letters.Length) % Letters.Length;
        }

        public int VirtualKey
        { get { return Selected < 0 ? 0 : (Letters[Selected] == ';' ? 0xBA : Letters[Selected]); } }
        public Command Confirm()
        {
            return Literal ? new Command(CommandKind.Text, Letters[Selected], 0, 0) : Command.Key(VirtualKey);
        }
    }

    public sealed class ToggleGesture
    {
        public const double DoubleTapSeconds = 0.6;
        public int Count { get; private set; }
        public bool Consuming { get; private set; }
        private bool armed;
        private double tappedAt;

        public void Reset() { Count = 0; Consuming = armed = false; }
        public bool Update(Buttons buttons, double now)
        {
            if (Count != 0 && now - tappedAt > (Count == 1 ? DoubleTapSeconds : 1.0)) Count = 0;
            Buttons bumpers = buttons & Engine.ToggleButtons;
            if (bumpers == 0) { armed = true; Consuming = false; return false; }
            if (!armed || bumpers != Engine.ToggleButtons) return false;
            armed = false; Consuming = true;
            bool complete = Count == 1;
            Count = complete ? 2 : 1; tappedAt = now;
            return complete;
        }
    }

    public sealed class RumbleFeedback
    {
        private readonly Action<int, ushort, ushort> send;
        private int slot = -1;
        private double until;
        public RumbleFeedback(Action<int, ushort, ushort> send) { this.send = send; }
        public void Pulse(int controller, double now, double duration, ushort left, ushort right)
        {
            Stop();
            if (controller < 0) return;
            slot = controller; until = now + duration;
            send(controller, left, right);
        }
        public void Update(double now) { if (slot >= 0 && now >= until) Stop(); }
        public void Stop()
        {
            if (slot < 0) return;
            int previous = slot; slot = -1;
            send(previous, 0, 0);
        }
    }

    public static class Xiaohe
    {
        // Indexed by A-Z. Labels describe physical keys; composition remains in the system IME.
        public static readonly string[] Initials = {
            "a", "b", "c", "d", "e", "f", "g", "h", "ch", "j", "k", "l", "m",
            "n", "o", "p", "q", "r", "s", "t", "sh", "zh", "w", "x", "y", "z", "；"
        };
        public static readonly string[] Finals = {
            "a", "in", "ao", "ai", "e", "en", "eng", "ang", "i", "an", "ing\nuai", "iang\nuang", "ian",
            "iao", "o\nuo", "ie", "iu", "uan", "ong\niong", "üe", "u", "ü\nui", "ei", "ia\nua", "un", "ou", "；"
        };

        public static string Label(char key)
        {
            int index = key == ';' ? 26 : key - 'A';
            string initial = Initials[index], final = Finals[index].Replace('\n', '/');
            return initial == final ? initial : initial + "\n" + final;
        }
    }

    public static class Qwerty
    {
        public const string LeftLetters = "QWERASDFZXCVB";
        public const string RightLetters = "TYUIOPGHJKL;NM";
        public const string Digits = "1234567890";
        public const string Symbols = ".,;:!?-_/@()[]";
    }

    public sealed class Engine
    {
        public Mode Mode { get; private set; }
        public readonly TimeMouse Mouse;
        public readonly LetterWheel LeftWheel, RightWheel;
        public readonly ToggleGesture Toggle = new ToggleGesture();
        public bool LeftNumeric { get; private set; }
        public bool RightSymbols { get; private set; }
        private readonly Settings settings;
        private Pad previous;
        private bool havePrevious, leftDown, rightDown, waitForNeutral;
        private double lastTime = -1, scrollFractionX, scrollFractionY;
        private readonly Dictionary<Buttons, double> repeatAt = new Dictionary<Buttons, double>();
        public const Buttons ToggleButtons = Buttons.LB | Buttons.RB;

        public Engine(Settings settings)
        {
            settings.Validate(); this.settings = settings;
            Mouse = new TimeMouse(settings);
            LeftWheel = new LetterWheel(settings, Qwerty.LeftLetters);
            RightWheel = new LetterWheel(settings, Qwerty.RightLetters);
            Mode = Mode.Standby;
        }

        public List<Command> Step(Pad pad, double now)
        {
            var commands = new List<Command>();
            double dt = lastTime < 0 ? 0 : Math.Max(0, Math.Min(now - lastTime, 0.05));
            lastTime = now;
            if (!havePrevious) { previous = pad; havePrevious = true; }
            Buttons pressed = pad.Buttons & ~previous.Buttons;
            Buttons released = previous.Buttons & ~pad.Buttons;
            previous = pad;
            bool wasConsuming = Toggle.Consuming;
            bool toggle = Toggle.Update(pad.Buttons, now);
            if ((pad.Buttons & ToggleButtons) == ToggleButtons || wasConsuming || Toggle.Consuming)
            {
                ReleaseMouse(commands); Mouse.Reset(); scrollFractionX = scrollFractionY = 0;
                LeftWheel.Reset(); RightWheel.Reset();
                repeatAt.Clear();
                if (toggle) SetMode(Mode == Mode.Standby ? Mode.Mouse : Mode.Standby, commands);
                return commands;
            }
            if (Mode == Mode.Standby) return commands;
            if (waitForNeutral)
            {
                if (pad.Buttons == Buttons.None &&
                    Magnitude(pad.LX, pad.LY) <= settings.MouseDeadzone &&
                    Magnitude(pad.RX, pad.RY) <= settings.MouseDeadzone)
                    waitForNeutral = false;
                return commands;
            }
            if ((pressed & Buttons.Y) != 0)
            {
                if (Mode == Mode.Mouse)
                {
                    ReleaseMouse(commands);
                    commands.Add(new Command(CommandKind.MouseDown, 0, 0, 0));
                    commands.Add(new Command(CommandKind.MouseUp, 0, 0, 0));
                    SetMode(Mode.Keyboard, commands);
                }
                else SetMode(Mode.Mouse, commands);
                return commands;
            }
            if (Mode == Mode.Mouse)
            {
                Command movement = Mouse.Step(pad.LX, pad.LY, now, dt);
                if (movement.X != 0 || movement.Y != 0) commands.Add(movement);
                SyncMouse((pad.Buttons & Buttons.A) != 0, 0, ref leftDown, commands);
                SyncMouse((pad.Buttons & Buttons.B) != 0, 1, ref rightDown, commands);
                Scroll(pad.RY, dt, ref scrollFractionY, CommandKind.Wheel, commands);
                Scroll(pad.RX, dt, ref scrollFractionX, CommandKind.HorizontalWheel, commands);
                return commands;
            }
            bool layerChanged = false;
            if ((pressed & Buttons.Back) != 0)
            {
                LeftNumeric = !LeftNumeric;
                LeftWheel.SetKeys(LeftNumeric ? Qwerty.Digits : Qwerty.LeftLetters, false);
                layerChanged = true;
            }
            if ((pressed & Buttons.Start) != 0)
            {
                RightSymbols = !RightSymbols;
                RightWheel.SetKeys(RightSymbols ? Qwerty.Symbols : Qwerty.RightLetters, RightSymbols);
                layerChanged = true;
            }
            if (layerChanged) return commands;
            LeftWheel.Update(pad.LX, pad.LY); RightWheel.Update(pad.RX, pad.RY);
            // A fresh trigger press confirms once; simultaneous presses send left before right.
            if ((pressed & Buttons.LT) != 0 && LeftWheel.VirtualKey != 0) commands.Add(LeftWheel.Confirm());
            if ((pressed & Buttons.RT) != 0 && RightWheel.VirtualKey != 0) commands.Add(RightWheel.Confirm());
            TapOrRepeat(Buttons.A, 0x20, pad, pressed, now, commands, false);
            TapOrRepeat(Buttons.B, 0x1B, pad, pressed, now, commands, false);
            TapOrRepeat(Buttons.X, 0x08, pad, pressed, now, commands, true);
            // Defer single bumper actions until release so the toggle chord cannot page candidates.
            if ((released & Buttons.LB) != 0) commands.Add(Command.Key(0x21));
            if ((released & Buttons.RB) != 0) commands.Add(Command.Key(0x22));
            TapOrRepeat(Buttons.Up, 0x26, pad, pressed, now, commands, true);
            TapOrRepeat(Buttons.Down, 0x28, pad, pressed, now, commands, true);
            TapOrRepeat(Buttons.Left, 0x25, pad, pressed, now, commands, true);
            TapOrRepeat(Buttons.Right, 0x27, pad, pressed, now, commands, true);
            TapOrRepeat(Buttons.L3, 0x0D, pad, pressed, now, commands, false);
            TapOrRepeat(Buttons.R3, 0x09, pad, pressed, now, commands, false);
            return commands;
        }

        public List<Command> Stop()
        {
            var commands = new List<Command>();
            SetMode(Mode.Standby, commands);
            havePrevious = false; lastTime = -1; Toggle.Reset();
            return commands;
        }

        private void SetMode(Mode mode, List<Command> commands)
        {
            ReleaseMouse(commands); Mouse.Reset(); LeftWheel.Reset(); RightWheel.Reset();
            if (mode == Mode.Standby)
            {
                LeftNumeric = RightSymbols = false;
                LeftWheel.SetKeys(Qwerty.LeftLetters, false); RightWheel.SetKeys(Qwerty.RightLetters, false);
            }
            repeatAt.Clear(); scrollFractionX = scrollFractionY = 0; waitForNeutral = true; Mode = mode;
        }

        private void Scroll(double value, double dt, ref double fraction, CommandKind kind, List<Command> commands)
        {
            if (Math.Abs(value) <= settings.MouseDeadzone) { fraction = 0; return; }
            double strength = Math.Sign(value) * (Math.Abs(value) - settings.MouseDeadzone) / (1 - settings.MouseDeadzone);
            fraction += strength * 720 * dt;
            int amount = (int)fraction; fraction -= amount;
            if (amount != 0) commands.Add(new Command(kind, amount, 0, 0));
        }

        private void TapOrRepeat(Buttons button, int vk, Pad pad, Buttons pressed, double now,
            List<Command> commands, bool repeat)
        {
            if ((pad.Buttons & button) == 0) { repeatAt.Remove(button); return; }
            double due;
            if ((pressed & button) != 0)
            { commands.Add(Command.Key(vk)); repeatAt[button] = now + 0.45; }
            else if (repeat && repeatAt.TryGetValue(button, out due) && now >= due)
            { commands.Add(Command.Key(vk)); repeatAt[button] = now + 0.07; }
        }

        private static double Magnitude(double x, double y) { return Math.Sqrt(x * x + y * y); }
        private static void SyncMouse(bool down, int code, ref bool held, List<Command> commands)
        {
            if (held == down) return;
            held = down;
            commands.Add(new Command(down ? CommandKind.MouseDown : CommandKind.MouseUp, code, 0, 0));
        }
        private void ReleaseMouse(List<Command> commands)
        {
            SyncMouse(false, 0, ref leftDown, commands); SyncMouse(false, 1, ref rightDown, commands);
        }
    }
}
