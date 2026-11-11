namespace NitroDrp.Api.Services;

/// <summary>An expected failure that maps to an HTTP status and a stable machine-readable code.</summary>
public sealed class ServiceException(int status, string code, string message) : Exception(message)
{
    public int Status { get; } = status;
    public string Code { get; } = code;

    public static ServiceException BadRequest(string code, string message) => new(400, code, message);
    public static ServiceException Unauthorized(string code, string message) => new(401, code, message);
    public static ServiceException Forbidden(string code, string message) => new(403, code, message);
    public static ServiceException NotFound(string code = "not_found", string message = "This link doesn't exist.") => new(404, code, message);
    public static ServiceException Gone(string code, string message) => new(410, code, message);
    public static ServiceException Conflict(string code, string message) => new(409, code, message);
}
