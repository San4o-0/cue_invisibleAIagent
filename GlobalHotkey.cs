using System.Runtime.InteropServices;
using System.Windows.Input;

namespace LiveTranscriber;

public sealed class GlobalHotkey : IDisposable
{
    private const uint NativeAlt = 0x0001;
    private const uint NativeControl = 0x0002;
    private const uint NativeShift = 0x0004;
    private const uint NativeWindows = 0x0008;
    private const uint NoRepeat = 0x4000;
    private const uint WmHotkey = 0x0312;
    private const uint WmQuit = 0x0012;
    private const int HotkeyId = 1;

    private uint threadId;

    public GlobalHotkey(ModifierKeys modifiers, Key key)
    {
        Modifiers = modifiers;
        Key = key;

        var nativeModifiers = ToNative(modifiers) | NoRepeat;
        var virtualKey = (uint)KeyInterop.VirtualKeyFromKey(key);
        using var ready = new ManualResetEventSlim();
        var registered = false;

        var thread = new Thread(() =>
        {
            threadId = GetCurrentThreadId();
            registered = RegisterHotKey(IntPtr.Zero, HotkeyId, nativeModifiers, virtualKey);
            ready.Set();

            if (!registered)
            {
                return;
            }

            while (GetMessage(out var message, IntPtr.Zero, 0, 0) > 0)
            {
                if (message.Message == WmHotkey)
                {
                    Pressed?.Invoke();
                }
            }

            UnregisterHotKey(IntPtr.Zero, HotkeyId);
        })
        {
            IsBackground = true
        };

        thread.Start();
        ready.Wait();

        if (!registered)
        {
            throw new InvalidOperationException($"Комбінація {Describe(modifiers, key)} вже зайнята іншою програмою");
        }
    }

    public event Action? Pressed;

    public ModifierKeys Modifiers { get; }

    public Key Key { get; }

    public string Description => Describe(Modifiers, Key);

    public static string Describe(ModifierKeys modifiers, Key key)
    {
        var parts = new List<string>();

        if (modifiers.HasFlag(ModifierKeys.Control))
        {
            parts.Add("Ctrl");
        }

        if (modifiers.HasFlag(ModifierKeys.Alt))
        {
            parts.Add("Alt");
        }

        if (modifiers.HasFlag(ModifierKeys.Shift))
        {
            parts.Add("Shift");
        }

        if (modifiers.HasFlag(ModifierKeys.Windows))
        {
            parts.Add("Win");
        }

        parts.Add(KeyName(key));
        return string.Join("+", parts);
    }

    private static string KeyName(Key key) => key switch
    {
        >= Key.D0 and <= Key.D9 => ((int)(key - Key.D0)).ToString(),
        >= Key.NumPad0 and <= Key.NumPad9 => $"Num{(int)(key - Key.NumPad0)}",
        Key.Oem3 => "~",
        Key.OemMinus => "-",
        Key.OemPlus => "=",
        Key.Oem4 => "[",
        Key.Oem6 => "]",
        Key.Oem5 => "\\",
        Key.Oem1 => ";",
        Key.Oem7 => "'",
        Key.OemComma => ",",
        Key.OemPeriod => ".",
        Key.Oem2 => "/",
        Key.Space => "Пробіл",
        Key.Enter => "Enter",
        Key.Back => "Backspace",
        Key.Escape => "Esc",
        Key.Delete => "Del",
        Key.Insert => "Ins",
        Key.PageUp => "PgUp",
        Key.PageDown => "PgDn",
        _ => key.ToString()
    };

    public void Dispose() => PostThreadMessage(threadId, WmQuit, IntPtr.Zero, IntPtr.Zero);

    private static uint ToNative(ModifierKeys modifiers)
    {
        var result = 0u;

        if (modifiers.HasFlag(ModifierKeys.Alt))
        {
            result |= NativeAlt;
        }

        if (modifiers.HasFlag(ModifierKeys.Control))
        {
            result |= NativeControl;
        }

        if (modifiers.HasFlag(ModifierKeys.Shift))
        {
            result |= NativeShift;
        }

        if (modifiers.HasFlag(ModifierKeys.Windows))
        {
            result |= NativeWindows;
        }

        return result;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMessage
    {
        public IntPtr Window;
        public uint Message;
        public IntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public int X;
        public int Y;
        public uint Private;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr window, int id);

    [DllImport("user32.dll")]
    private static extern int GetMessage(out NativeMessage message, IntPtr window, uint filterMin, uint filterMax);

    [DllImport("user32.dll")]
    private static extern bool PostThreadMessage(uint threadId, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();
}
