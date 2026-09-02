using System.Runtime.InteropServices;

namespace StealthRAT.Utilities;

/// <summary>
/// Provides managed wrappers around Windows native input APIs (user32.dll).
/// Encapsulates P/Invoke declarations and provides type-safe methods
/// for simulating mouse and keyboard input.
/// </summary>
public static class NativeInputHelper
{
    #region P/Invoke Declarations

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern void mouse_event(
        uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern void keybd_event(
        byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    #endregion

    #region Mouse Event Constants

    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    private const uint MOUSEEVENTF_RIGHTUP = 0x0010;

    #endregion

    #region Keyboard Event Constants

    private const uint KEYEVENTF_KEYDOWN = 0x0000;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    #endregion

    #region Virtual Key Code Mappings

    /// <summary>
    /// Maps human-readable key names to their virtual key codes.
    /// Supports common control keys and single alphanumeric characters.
    /// </summary>
    private static readonly Dictionary<string, byte> SpecialKeyMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ENTER"] = 0x0D,
        ["SPACE"] = 0x20,
        ["TAB"] = 0x09,
        ["BACKSPACE"] = 0x08,
        ["ESC"] = 0x1B,
        ["DELETE"] = 0x2E,
        ["HOME"] = 0x24,
        ["END"] = 0x23,
        ["UP"] = 0x26,
        ["DOWN"] = 0x28,
        ["LEFT"] = 0x25,
        ["RIGHT"] = 0x27,
        ["F1"] = 0x70,
        ["F2"] = 0x71,
        ["F3"] = 0x72,
        ["F4"] = 0x73,
        ["F5"] = 0x74,
        ["F6"] = 0x75,
        ["F7"] = 0x76,
        ["F8"] = 0x77,
        ["F9"] = 0x78,
        ["F10"] = 0x79,
        ["F11"] = 0x7A,
        ["F12"] = 0x7B
    };

    #endregion

    /// <summary>
    /// Moves the mouse cursor to the specified screen coordinates.
    /// </summary>
    /// <param name="x">The horizontal position in pixels from the left edge.</param>
    /// <param name="y">The vertical position in pixels from the top edge.</param>
    /// <returns>True if the cursor was successfully moved; otherwise, false.</returns>
    public static bool MoveCursor(int x, int y)
    {
        return SetCursorPos(x, y);
    }

    /// <summary>
    /// Simulates a mouse click at the current cursor position.
    /// </summary>
    /// <param name="isRightClick">If true, performs a right-click; otherwise, a left-click.</param>
    /// <param name="clickDelayMs">Delay between button down and up events in milliseconds.</param>
    public static void SimulateClick(bool isRightClick, int clickDelayMs)
    {
        uint downFlag = isRightClick ? MOUSEEVENTF_RIGHTDOWN : MOUSEEVENTF_LEFTDOWN;
        uint upFlag = isRightClick ? MOUSEEVENTF_RIGHTUP : MOUSEEVENTF_LEFTUP;

        mouse_event(downFlag, 0, 0, 0, UIntPtr.Zero);
        Thread.Sleep(clickDelayMs);
        mouse_event(upFlag, 0, 0, 0, UIntPtr.Zero);
    }

    /// <summary>
    /// Simulates a key press (down and up) for the specified key.
    /// </summary>
    /// <param name="keyName">The key name (single character or special key name like "ENTER").</param>
    /// <param name="pressDelayMs">Delay between key down and up events in milliseconds.</param>
    /// <returns>True if the key was recognized and pressed; false if the key is unsupported.</returns>
    public static bool SimulateKeyPress(string keyName, int pressDelayMs)
    {
        byte virtualKeyCode = ResolveVirtualKeyCode(keyName);
        if (virtualKeyCode == 0) return false;

        keybd_event(virtualKeyCode, 0, KEYEVENTF_KEYDOWN, UIntPtr.Zero);
        Thread.Sleep(pressDelayMs);
        keybd_event(virtualKeyCode, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        return true;
    }

    /// <summary>
    /// Resolves a key name string to its corresponding virtual key code.
    /// </summary>
    /// <param name="keyName">The key name to resolve.</param>
    /// <returns>The virtual key code, or 0 if the key is not recognized.</returns>
    public static byte ResolveVirtualKeyCode(string keyName)
    {
        // Check special key mappings first
        if (SpecialKeyMap.TryGetValue(keyName, out byte specialKey))
        {
            return specialKey;
        }

        // Single alphanumeric character
        string upper = keyName.ToUpperInvariant();
        if (upper.Length == 1 && char.IsLetterOrDigit(upper[0]))
        {
            return (byte)upper[0];
        }

        return 0; // Unrecognized key
    }

    /// <summary>
    /// Gets a list of all supported key names for documentation purposes.
    /// </summary>
    /// <returns>An enumerable of supported key name strings.</returns>
    public static IEnumerable<string> GetSupportedKeys()
    {
        return SpecialKeyMap.Keys
            .Concat(Enumerable.Range('A', 26).Select(c => ((char)c).ToString()))
            .Concat(Enumerable.Range('0', 10).Select(c => ((char)c).ToString()));
    }
}
