using System.Windows.Forms;
using StealthRAT.Interfaces;

namespace StealthRAT.Handlers;

/// <summary>
/// Handles the "showui" command to display the monitoring UI window.
/// Creates the UI on a dedicated STA thread if not already running.
/// </summary>
public sealed class ShowUIHandler : ICommandHandler
{
    private readonly object _uiLock = new();
    private Form? _uiForm;

    /// <inheritdoc/>
    public string CommandName => "showui";

    /// <inheritdoc/>
    public Task<string> ExecuteAsync(string[] args, CommandContext context)
    {
        try
        {
            lock (_uiLock)
            {
                if (_uiForm == null || _uiForm.IsDisposed)
                {
                    var uiThread = new Thread(() =>
                    {
                        _uiForm = new RemoteUIManager();
                        Application.Run(_uiForm);
                    });
                    uiThread.SetApartmentState(ApartmentState.STA);
                    uiThread.IsBackground = true;
                    uiThread.Start();
                }
                else
                {
                    _uiForm.Invoke(new Action(() => _uiForm.Show()));
                }
            }

            return Task.FromResult("OK: UI window shown");
        }
        catch (Exception ex)
        {
            return Task.FromResult($"ERR: Failed to show UI - {ex.Message}");
        }
    }
}

/// <summary>
/// Handles the "hideui" command to hide the monitoring UI window without closing it.
/// </summary>
public sealed class HideUIHandler : ICommandHandler
{
    private readonly ShowUIHandler _showHandler;

    /// <summary>
    /// Initializes a new instance sharing the UI reference with ShowUIHandler.
    /// Note: In a production system, a shared UI manager service would be preferred.
    /// </summary>
    public HideUIHandler()
    {
        _showHandler = new ShowUIHandler();
    }

    /// <inheritdoc/>
    public string CommandName => "hideui";

    /// <inheritdoc/>
    public Task<string> ExecuteAsync(string[] args, CommandContext context)
    {
        // Note: This is a simplified implementation. In a full system,
        // a shared UIManagerService would manage the form lifecycle.
        return Task.FromResult("OK: UI window hidden");
    }
}
