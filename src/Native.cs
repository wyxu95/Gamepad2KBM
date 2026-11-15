using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace Gamepad2KBM
{
    [StructLayout(LayoutKind.Sequential)]
    public struct XState
    {
        public uint Packet;
        public ushort Buttons;
        public byte LT, RT;
        public short LX, LY, RX, RY;

        public Pad ToPad(ref bool leftTrigger, ref bool rightTrigger)
        {
            // Separate press/release thresholds prevent trigger noise from generating repeated letters.
            leftTrigger = LT >= (leftTrigger ? 90 : 150);
            rightTrigger = RT >= (rightTrigger ? 90 : 150);
            return new Pad {
                Buttons = (Buttons)Buttons | (leftTrigger ? Gamepad2KBM.Buttons.LT : 0) |
                    (rightTrigger ? Gamepad2KBM.Buttons.RT : 0),
                LX = Axis(LX), LY = Axis(LY), RX = Axis(RX), RY = Axis(RY)
            };
        }
        private static double Axis(short value) { return value < 0 ? value / 32768.0 : value / 32767.0; }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct XVibration { public ushort LeftMotor, RightMotor; }

    public static class Native
    {
        [DllImport("xinput1_4.dll")] public static extern uint XInputGetState(uint index, out XState state);
        [DllImport("xinput1_4.dll")] public static extern uint XInputSetState(uint index, ref XVibration vibration);
        [DllImport("user32.dll")] public static extern bool GetCursorPos(out Point point);
        [DllImport("user32.dll", SetLastError = true)] public static extern bool SetCursorPos(int x, int y);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr window, StringBuilder text, int length);
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr window);
        [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int vk);
        [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, Input[] input, int size);
        [DllImport("user32.dll")] private static extern uint MapVirtualKey(uint code, uint mapType);
        [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
        [DllImport("winmm.dll")] public static extern uint timeBeginPeriod(uint period);
        [DllImport("winmm.dll")] public static extern uint timeEndPeriod(uint period);
        [DllImport("kernel32.dll")] public static extern bool AttachConsole(int pid);
        [DllImport("user32.dll")] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);

        [StructLayout(LayoutKind.Sequential)] public struct Point { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] private struct Input { public uint Type; public InputUnion Data; }
        [StructLayout(LayoutKind.Explicit)] private struct InputUnion
        {
            [FieldOffset(0)] public MouseInput Mouse;
            [FieldOffset(0)] public KeyboardInput Keyboard;
        }
        [StructLayout(LayoutKind.Sequential)] private struct MouseInput
        {
            public int X, Y;
            public uint Data, Flags, Time;
            public UIntPtr Extra;
        }
        [StructLayout(LayoutKind.Sequential)] private struct KeyboardInput
        {
            public ushort VirtualKey, Scan;
            public uint Flags, Time;
            public UIntPtr Extra;
        }

        public static void Execute(Command command)
        {
            if (command.Kind == CommandKind.Move)
            {
                Point point;
                if (!GetCursorPos(out point) || !SetCursorPos(point.X + command.X, point.Y + command.Y))
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "Cursor movement failed");
                return;
            }
            if (command.Kind == CommandKind.Key) { Tap(command.Code); return; }
            if (command.Kind == CommandKind.Text) { Text((char)command.Code); return; }
            uint flags;
            if (command.Kind == CommandKind.Wheel) flags = 0x0800;
            else if (command.Kind == CommandKind.HorizontalWheel) flags = 0x1000;
            else if (command.Code == 0) flags = command.Kind == CommandKind.MouseDown ? 0x0002U : 0x0004U;
            else flags = command.Kind == CommandKind.MouseDown ? 0x0008U : 0x0010U;
            Send(new Input { Type = 0, Data = new InputUnion { Mouse = new MouseInput {
                Flags = flags, Data = command.Kind == CommandKind.Wheel || command.Kind == CommandKind.HorizontalWheel ? unchecked((uint)command.Code) : 0
            } } });
        }

        public static void Tap(int vk)
        {
            // Physical key events go through the foreground application's IME.
            // Unicode text injection would bypass the user's shuangpin composition.
            // AHK's KEY_IGNORE tag keeps its keyboard hooks from remapping these keys.
            // https://github.com/AutoHotkey/AutoHotkey/blob/v2.0/source/keyboard_mouse.h
            bool extended = vk >= 0x21 && vk <= 0x2E;
            uint flags = 0x0008U | (extended ? 0x0001U : 0);
            var down = new Input { Type = 1, Data = new InputUnion { Keyboard = new KeyboardInput {
                Scan = (ushort)MapVirtualKey((uint)vk, 0), Flags = flags, Extra = new UIntPtr(0xFFC3D44FU)
            } } };
            var up = down; up.Data.Keyboard.Flags |= 0x0002;
            Send(down, up);
        }

        public static void Text(char character)
        {
            var down = new Input { Type = 1, Data = new InputUnion { Keyboard = new KeyboardInput {
                Scan = character, Flags = 0x0004, Extra = new UIntPtr(0xFFC3D44FU)
            } } };
            var up = down; up.Data.Keyboard.Flags |= 0x0002;
            Send(down, up);
        }

        private static void Send(params Input[] events)
        {
            if (SendInput((uint)events.Length, events, Marshal.SizeOf(typeof(Input))) != events.Length)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "SendInput failed; check foreground app permissions");
        }

        public static int InputSize { get { return Marshal.SizeOf(typeof(Input)); } }
    }
}
