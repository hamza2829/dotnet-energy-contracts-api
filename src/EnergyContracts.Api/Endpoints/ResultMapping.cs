using EnergyContracts.Api.Services;

namespace EnergyContracts.Api.Endpoints;

public static class ResultMapping
{
    public static IResult ToHttp<T>(this ServiceResult<T> result, Func<T, IResult>? success = null)
    {
        if (result.Error is not { } error)
            return success?.Invoke(result.Value!) ?? Results.Ok(result.Value);

        return error.Kind switch
        {
            ErrorKind.Validation => Results.ValidationProblem(error.ValidationErrors!),
            ErrorKind.NotFound => Results.Problem(statusCode: 404, title: "Not found", detail: error.Message),
            ErrorKind.Conflict => Results.Problem(statusCode: 409, title: "Conflict", detail: error.Message),
            _ => throw new InvalidOperationException("Unknown service error.")
        };
    }
}
