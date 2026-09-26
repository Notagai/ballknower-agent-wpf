using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace Ballknower;

/// <summary>
/// Global Win-key shortcut:
/// - tap Win: replay the normal Win press so Start opens
/// - hold Win for one second: suppress Win and open Ballknower
/// - Win + another key: preserve the normal Windows shortcut
/// </summary>
public sealed class KeyboardShortcutManager : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;

    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;

    private const int VK_LWIN = 0x5B;
    private const int VK_RWIN = 0x5C;

    private const uint LLKHF_INJECTED = 0x00000010;

    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    private const int HoldMilliseconds = 1000;

    private readonly Action _onLongHold;
    private readonly LowLevelKeyboardProc _hookCallback;

    private IntPtr _hookHandle;
    private Timer? _holdTimer;

    private bool _winHeld;
    private bool _combinationUsed;
    private bool _longHoldTriggered;
    private bool _winDownReplayed;
    private int _activeWinKey;

    private bool _disposed;

    public KeyboardShortcutManager(Action onLongHold)
    {
        _onLongHold =
            onLongHold ??
            throw new ArgumentNullException(nameof(onLongHold));

        _hookCallback = HookCallback;

        using Process process = Process.GetCurrentProcess();
        using ProcessModule? module = process.MainModule;

        IntPtr moduleHandle =
            module is null
                ? IntPtr.Zero
                : GetModuleHandle(module.ModuleName);

        _hookHandle =
            SetWindowsHookEx(
                WH_KEYBOARD_LL,
                _hookCallback,
                moduleHandle,
                0);

        if (_hookHandle == IntPtr.Zero)
        {
            throw new System.ComponentModel.Win32Exception(
                Marshal.GetLastWin32Error(),
                "Could not install the global keyboard shortcut hook.");
        }
    }

    private IntPtr HookCallback(
        int nCode,
        IntPtr wParam,
        IntPtr lParam)
    {
        if (nCode < 0 || _disposed)
            return CallNextHookEx(_hookHandle, nCode, wParam, lParam);

        var data =
            Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);

        /*
         * Ignore the synthetic Win events that we replay ourselves.
         */
        if ((data.flags & LLKHF_INJECTED) != 0)
        {
            return CallNextHookEx(
                _hookHandle,
                nCode,
                wParam,
                lParam);
        }

        int message = wParam.ToInt32();

        bool isDown =
            message == WM_KEYDOWN ||
            message == WM_SYSKEYDOWN;

        bool isUp =
            message == WM_KEYUP ||
            message == WM_SYSKEYUP;

        bool isWin =
            data.vkCode == VK_LWIN ||
            data.vkCode == VK_RWIN;

        if (!isWin)
        {
            if (isDown && _winHeld)
            {
                _combinationUsed = true;

                /*
                 * The original Win-down was held back while we
                 * determined whether this was a standalone Win press.
                 * Replay it immediately before allowing the other
                 * key through, preserving Win+E and similar shortcuts.
                 */
                if (!_winDownReplayed)
                {
                    ReplayWinDown();
                    _winDownReplayed = true;
                }
            }

            return CallNextHookEx(
                _hookHandle,
                nCode,
                wParam,
                lParam);
        }

        if (isDown)
        {
            /*
             * Windows may repeat Win key-down messages while held.
             * Only the first physical press starts the timer.
             */
            if (_winHeld)
            {
                return IntPtr.Zero;
            }

            _winHeld = true;
            _combinationUsed = HasAnotherKeyDown();
            _longHoldTriggered = false;
            _winDownReplayed = false;
            _activeWinKey = (int)data.vkCode;

            if (_combinationUsed)
            {
                ReplayWinDown();
                _winDownReplayed = true;

                return CallNextHookEx(
                    _hookHandle,
                    nCode,
                    wParam,
                    lParam);
            }

            _holdTimer?.Dispose();

            _holdTimer =
                new Timer(
                    LongHoldTimerCallback,
                    null,
                    HoldMilliseconds,
                    Timeout.Infinite);

            /*
             * Suppress the physical Win-down until we know whether
             * this is a tap or the Ballknower shortcut.
             */
            return IntPtr.Zero;
        }

        if (isUp)
        {
            if (!_winHeld)
            {
                return CallNextHookEx(
                    _hookHandle,
                    nCode,
                    wParam,
                    lParam);
            }

            _holdTimer?.Dispose();
            _holdTimer = null;

            bool longHold =
                _longHoldTriggered &&
                !_combinationUsed;

            if (!longHold && !_winDownReplayed)
            {
                /*
                 * A short standalone Win press should behave like a
                 * normal physical Win press: down followed by up.
                 */
                ReplayWinDown();
                ReplayWinUp(_activeWinKey);
            }
            else if (_winDownReplayed)
            {
                /*
                 * Win-down was replayed for a modifier combination.
                 * Replay the corresponding Win-up now.
                 */
                ReplayWinUp(_activeWinKey);
            }

            _winHeld = false;
            _combinationUsed = false;
            _longHoldTriggered = false;
            _winDownReplayed = false;
            _activeWinKey = 0;

            /*
             * For a long hold, both Win-down and Win-up were suppressed.
             */
            return IntPtr.Zero;
        }

        return CallNextHookEx(
            _hookHandle,
            nCode,
            wParam,
            lParam);
    }

    private void LongHoldTimerCallback(object? state)
    {
        if (_disposed ||
            !_winHeld ||
            _combinationUsed ||
            _longHoldTriggered)
        {
            return;
        }

        _longHoldTriggered = true;

        _onLongHold();
    }

    private bool HasAnotherKeyDown()
    {
        /*
         * Check the other keyboard keys as well as the common
         * modifiers. This covers cases such as holding a letter
         * key and then pressing Win.
         */
        for (int virtualKey = 1; virtualKey < 256; virtualKey++)
        {
            if (virtualKey == VK_LWIN ||
                virtualKey == VK_RWIN)
            {
                continue;
            }

            if (IsKeyDown(
                    (System.Windows.Forms.Keys)virtualKey))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsKeyDown(System.Windows.Forms.Keys key)
    {
        return (GetAsyncKeyState((int)key) & 0x8000) != 0;
    }

    private void ReplayWinDown()
    {
        SendWinInput(
            (ushort)_activeWinKey,
            keyUp: false);
    }

    private void ReplayWinUp(int virtualKey)
    {
        SendWinInput(
            (ushort)virtualKey,
            keyUp: true);
    }

    private static void SendWinInput(
        ushort virtualKey,
        bool keyUp)
    {
        var input =
            new INPUT
            {
                type = INPUT_KEYBOARD,
                U = new InputUnion
                {
                    ki = new KEYBDINPUT
                    {
                        wVk = virtualKey,
                        wScan = 0,
                        dwFlags = keyUp
                            ? KEYEVENTF_KEYUP
                            : 0,
                        time = 0,
                        dwExtraInfo = UIntPtr.Zero
                    }
                }
            };

        if (SendInput(
                1,
                new[] { input },
                Marshal.SizeOf<INPUT>()) == 0)
        {
            Debug.WriteLine(
                "Ballknower could not replay the Windows key.");
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        _holdTimer?.Dispose();
        _holdTimer = null;

        if (_hookHandle != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookHandle);
            _hookHandle = IntPtr.Zero;
        }
    }

    private delegate IntPtr LowLevelKeyboardProc(
        int nCode,
        IntPtr wParam,
        IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    [DllImport(
        "user32.dll",
        SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(
        int idHook,
        LowLevelKeyboardProc lpfn,
        IntPtr hMod,
        uint dwThreadId);

    [DllImport(
        "user32.dll",
        SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(
        IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(
        IntPtr hhk,
        int nCode,
        IntPtr wParam,
        IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(
        string? lpModuleName);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(
        int vKey);

    [DllImport(
        "user32.dll",
        SetLastError = true)]
    private static extern uint SendInput(
        uint nInputs,
        INPUT[] pInputs,
        int cbSize);
}
