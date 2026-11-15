namespace SiteRoof.Api.Auth;

/// <summary>Base for errors that map to an RFC 9457 ProblemDetails response.</summary>
public abstract class AppException(int status, string code, string title, string detail) : Exception(detail)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
    public string Title { get; } = title;
    public IDictionary<string, object?> Extensions { get; } = new Dictionary<string, object?>();
}

public sealed class NotFoundException(string detail = "The requested resource was not found.")
    : AppException(StatusCodes.Status404NotFound, "not-found", "Not found", detail);

public sealed class ForbiddenException(string detail = "You do not have permission to perform this action.")
    : AppException(StatusCodes.Status403Forbidden, "forbidden", "Forbidden", detail);

/// <summary>State conflict: stale version, locked record, contradictory decision.</summary>
public sealed class ConflictException(string code, string detail)
    : AppException(StatusCodes.Status409Conflict, code, "Conflict", detail);

/// <summary>A request that is well-formed but breaks a business rule.</summary>
public sealed class BusinessRuleException(string code, string detail)
    : AppException(StatusCodes.Status422UnprocessableEntity, code, "Business rule violated", detail);

public sealed class RequestValidationException(IDictionary<string, string[]> errors)
    : AppException(StatusCodes.Status400BadRequest, "validation", "One or more fields are invalid.", "One or more fields are invalid.")
{
    public IDictionary<string, string[]> Errors { get; } = errors;

    public static RequestValidationException For(string field, string message) =>
        new(new Dictionary<string, string[]> { [field] = [message] });
}
