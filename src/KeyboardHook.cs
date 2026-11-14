using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace CodingEffect;

internal enum KeyKind { None, Char, Space, Delete, Enter }

/// <summary>
/// システム全体の低レベルキーボードフック。
/// 押されたキーの「種類」だけを通知し、入力内容は保存しない。
/// </summary>
internal sealed class KeyboardHook : IDisposable
{
    private readonly Native.LowLevelKeyboardProc _proc; // GC されないよう保持
    private IntPtr _hook;

    public event Action<int>? KeyDown;

    public KeyboardHook()
    {
        _proc = HookProc;
        _hook = Native.SetWindowsHookEx(Native.WH_KEYBOARD_LL, _proc, Native.GetModuleHandle(null), 0);
        if (_hook == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    private IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && (wParam == Native.WM_KEYDOWN || wParam == Native.WM_SYSKEYDOWN))
        {
            int vk = Marshal.ReadInt32(lParam); // KBDLLHOOKSTRUCT.vkCode
            try { KeyDown?.Invoke(vk); } catch { /* フック内で例外を出さない */ }
        }
        return Native.CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero)
        {
            Native.UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
    }
}

internal static class KeyClassifier
{
    public static KeyKind Classify(int vk) => vk switch
    {
        0x08 or 0x2E => KeyKind.Delete,                 // Backspace / Delete
        0x0D => KeyKind.Enter,
        0x20 => KeyKind.Space,
        0x09 => KeyKind.Char,                           // Tab
        >= 0x30 and <= 0x39 => KeyKind.Char,            // 0-9
        >= 0x41 and <= 0x5A => KeyKind.Char,            // A-Z
        >= 0x60 and <= 0x6F => KeyKind.Char,            // テンキー
        >= 0xBA and <= 0xC0 => KeyKind.Char,            // ; = , - . / `
        >= 0xDB and <= 0xDF => KeyKind.Char,            // [ \ ] ' など
        0xE2 => KeyKind.Char,                           // JIS の \ _
        _ => KeyKind.None,
    };

    /// <summary>Ctrl / Alt / Win が押されている＝ショートカット操作とみなす。</summary>
    public static bool ShortcutModifierHeld() =>
        IsDown(0x11) || IsDown(0x12) || IsDown(0x5B) || IsDown(0x5C);

    private static bool IsDown(int vk) => (Native.GetAsyncKeyState(vk) & 0x8000) != 0;
}
