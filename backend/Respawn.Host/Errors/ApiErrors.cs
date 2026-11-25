using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Respawn.Persistence;
using Respawn.SharedKernel;

namespace Respawn.Host.Errors;

/// <summary>The errors the pipeline itself answers, in the API's one format.</summary>
internal static class ApiErrors
{
    public static Task WriteAsync(HttpContext context, int status, string code, string message)
    {
        context.Response.StatusCode = status;
        return context.Response.WriteAsJsonAsync(new ErrorResponse(code, message), context.RequestAborted);
    }

    /// <summary>A request that does not have the shape the route expects.</summary>
    public static IActionResult InvalidRequest(ActionContext context)
    {
        var fields = context.ModelState
            .Where(entry => entry.Value is { Errors.Count: > 0 })
            .Select(entry => entry.Key is "" or "$" ? "body" : entry.Key.TrimStart('$', '.'))
            .Distinct()
            .Order(StringComparer.Ordinal);
        var error = new ErrorResponse("invalid_request", $"Check these fields: {string.Join(", ", fields)}.");
        return new ObjectResult(error) { StatusCode = StatusCodes.Status422UnprocessableEntity };
    }
}

/// <summary>What no module answered: the database not being there is a 503 the caller can retry;
/// anything else is a 500 that says nothing of what went wrong, the log does. A request its client
/// gave up is neither: nobody is there to answer, and nothing went wrong.</summary>
internal sealed class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        if (exception is OperationCanceledException && context.RequestAborted.IsCancellationRequested)
        {
            return true;
        }

        if (DatabaseFailures.MeanUnavailable(exception))
        {
            await ApiErrors.WriteAsync(context, 503, "database_unavailable", "The database is not available yet.");
            return true;
        }

        // What answered, as the app names it, and never the request's own words: a path or a method
        // the client wrote could forge lines of the log.
        logger.LogError(exception, "unhandled error in {Endpoint}", context.GetEndpoint()?.DisplayName ?? "no endpoint");
        await ApiErrors.WriteAsync(context, 500, "internal_error", "Something went wrong on the server.");
        return true;
    }
}
