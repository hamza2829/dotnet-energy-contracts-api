namespace EnergyContracts.Api.Services;

public enum ErrorKind { Validation, NotFound, Conflict }

public sealed record ServiceError(
    ErrorKind Kind, string Message, Dictionary<string, string[]>? ValidationErrors = null);

public sealed record ServiceResult<T>(T? Value, ServiceError? Error)
{
    public static ServiceResult<T> Success(T value) => new(value, null);
    public static ServiceResult<T> NotFound(string message) => new(default, new(ErrorKind.NotFound, message));
    public static ServiceResult<T> Conflict(string message) => new(default, new(ErrorKind.Conflict, message));
    public static ServiceResult<T> Invalid(Dictionary<string, string[]> errors) =>
        new(default, new(ErrorKind.Validation, "One or more validation errors occurred.", errors));
}
