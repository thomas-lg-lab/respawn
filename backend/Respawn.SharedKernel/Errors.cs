namespace Respawn.SharedKernel;

/// <summary>The one error format of the API, whichever module answers: {"error": {"code", "message"}}.</summary>
public sealed record ErrorResponse(ErrorBody Error)
{
    public ErrorResponse(string code, string message)
        : this(new ErrorBody(code, message))
    {
    }
}

/// <summary>A stable code a client can act on, and an English message for logs and direct callers.</summary>
public sealed record ErrorBody(string Code, string Message);
