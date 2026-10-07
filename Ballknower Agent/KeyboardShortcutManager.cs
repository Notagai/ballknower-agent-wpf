using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Ballknower.Voice;

namespace Ballknower;

public sealed class KeyboardShortcutManager : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;
    private const int VK_LWIN = 0x5B;
    private const int VK_RWIN = 0x5C;
    private const int VK_LCTRL = 0xA2;
    private const int VK_RCTRL = 0xA3;
    private const int VK_LALT = 0xA4;
    private const int VK_RALT = 0xA5;
    private const uint LLKHF_INJECTED = 0x00000010;
    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    private readonly Action<LaunchMode> _onLaunch;
    private readonly LowLevelKeyboardProc _hookCallback;
    private readonly bool[] _keysDown = new bool[256];
    private IntPtr _hookHandle;
    private bool _winHeld;
    private bool _winDownReplayed;
    private int _activeWinKey;
    private LaunchMode? _pendingMode;
    private bool _disposed;

    public KeyboardShortcutManager(Action<LaunchMode> onLaunch)
    {
        _onLaunch = onLaunch ?? throw new ArgumentNullException(nameof(onLaunch));
        _hookCallback = HookCallback;
        using var process = Process.GetCurrentProcess();
        var module = process.MainModule;
        var moduleHandle = module is null ? IntPtr.Zero : GetModuleHandle(module.ModuleName);
        _hookHandle = SetWindowsHookEx(WH_KEYBOARD_LL, _hookCallback, moduleHandle, 0);
        if (_hookHandle == IntPtr.Zero)
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Could not install the global keyboard shortcut hook.");
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0 || _disposed)
            return CallNextHookEx(_hookHandle, nCode, wParam, lParam);

        var data = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
        if ((data.flags & LLKHF_INJECTED) != 0)
            return CallNextHookEx(_hookHandle, nCode, wParam, lParam);

        int message = wParam.ToInt32();
        bool isDown = message == WM_KEYDOWN || message == WM_SYSKEYDOWN;
        bool isUp = message == WM_KEYUP || message == WM_SYSKEYUP;
        bool isWin = data.vkCode == VK_LWIN || data.vkCode == VK_RWIN;

        if (!isWin)
        {
            if (data.vkCode < _keysDown.Length)
            {
                if (isDown) _keysDown[data.vkCode] = true;
                if (isUp) _keysDown[data.vkCode] = false;
            }

            if (isDown && _winHeld && _pendingMode is not null &&
                data.vkCode != VK_LCTRL && data.vkCode != VK_RCTRL &&
                data.vkCode != VK_LALT && data.vkCode != VK_RALT &&
                data.vkCode != 0x11 && data.vkCode != 0x12)
            {
                ReplayWinDownIfNeeded();
                _winDownReplayed = true;
                _pendingMode = null;
            }

            return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
        }

        if (isDown)
        {
            if (_winHeld)
                return IntPtr.Zero;

            _winHeld = true;
            _activeWinKey = (int)data.vkCode;
            _pendingMode = DetermineMode();

            if (_pendingMode is not null)
                return IntPtr.Zero;

            ReplayWinDownIfNeeded();
            _winDownReplayed = true;
            return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
        }

        if (isUp)
        {
            if (!_winHeld)
                return CallNextHookEx(_hookHandle, nCode, wParam, lParam);

            var mode = _pendingMode;
            if (mode is not null)
                _onLaunch(mode.Value);

            if (_winDownReplayed)
                ReplayWinUp(_activeWinKey);

            _winHeld = false;
            _winDownReplayed = false;
            _activeWinKey = 0;
            _pendingMode = null;
            return mode is not null ? IntPtr.Zero : CallNextHookEx(_hookHandle, nCode, wParam, lParam);
        }

        return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    private LaunchMode? DetermineMode()
    {
        bool ctrl = _keysDown[0x11] || _keysDown[VK_LCTRL] || _keysDown[VK_RCTRL];
        bool alt = _keysDown[0x12] || _keysDown[VK_LALT] || _keysDown[VK_RALT];
        if (ctrl && alt) return LaunchMode.VoiceOutput;
        if (ctrl) return LaunchMode.VoiceInputOutput;
        if (alt) return LaunchMode.Text;
        return null;
    }

    private void ReplayWinDownIfNeeded() => SendWinInput((ushort)_activeWinKey, false);
    private static void ReplayWinUp(int virtualKey) => SendWinInput((ushort)virtualKey, true);

    private static void SendWinInput(ushort virtualKey, bool keyUp)
    {
        var input = new INPUT { type = INPUT_KEYBOARD, U = new InputUnion { ki = new KEYBDINPUT
        {
            wVk = virtualKey, wScan = 0, dwFlags = keyUp ? KEYEVENTF_KEYUP : 0, time = 0, dwExtraInfo = UIntPtr.Zero
        }}};
        SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_hookHandle != IntPtr.Zero) { UnhookWindowsHookEx(_hookHandle); _hookHandle = IntPtr.Zero; }
    }

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
    [StructLayout(LayoutKind.Sequential)] private struct KBDLLHOOKSTRUCT { public uint vkCode; public uint scanCode; public uint flags; public uint time; public UIntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] private struct INPUT { public uint type; public InputUnion U; }
    [StructLayout(LayoutKind.Explicit)] private struct InputUnion { [FieldOffset(0)] public KEYBDINPUT ki; }
    [StructLayout(LayoutKind.Sequential)] private struct KEYBDINPUT { public ushort wVk; public ushort wScan; public uint dwFlags; public uint time; public UIntPtr dwExtraInfo; }

    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? lpModuleName);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);
}
