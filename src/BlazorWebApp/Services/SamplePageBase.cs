using Microsoft.AspNetCore.Components;

namespace BlazorWebApp.Services;

/// <summary>
/// Base class for the sample pages. It provides the worker API client, a shared
/// "calls" log the pages render at the bottom, and small helpers for busy state
/// and error handling, so each page can focus on its own sample.
/// </summary>
public abstract class SamplePageBase : ComponentBase
{
    [Inject]
    protected WorkerApi Api { get; set; } = default!;

    /// <summary>Newest-first log of the calls this page made to the worker.</summary>
    protected List<ApiCallEntry> Calls { get; } = [];

    /// <summary>True while a call is in flight, used to disable buttons.</summary>
    protected bool Busy { get; private set; }

    /// <summary>Transport-level failure, e.g. the worker is not running.</summary>
    protected string? TransportError { get; private set; }

    /// <summary>Runs an action with busy handling and error capture.</summary>
    protected async Task GuardAsync(Func<Task> action)
    {
        if (Busy)
            return;

        Busy = true;
        TransportError = null;

        try
        {
            await action();
        }
        catch (Exception ex)
        {
            TransportError = ex.Message;
        }
        finally
        {
            Busy = false;
        }
    }

    /// <summary>Records a call and surfaces a transport failure to the page.</summary>
    protected void Tracked<T>(string label, string method, string path, ApiResult<T> result)
    {
        Calls.Insert(0, result.ToLogEntry(label, method, path));

        if (result.TransportError is not null)
            TransportError = result.TransportError;
    }

    /// <summary>Clears the log.</summary>
    protected void ClearLog() => Calls.Clear();
}
