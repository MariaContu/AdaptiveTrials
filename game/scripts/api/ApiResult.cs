namespace AdaptiveTrials.Game.Api;

public sealed class ApiResult<T>
{
    public bool IsSuccess { get; init; }

    public int? StatusCode { get; init; }

    public T? Data { get; init; }

    public string ErrorMessage { get; init; } =
        string.Empty;

    public static ApiResult<T> Success(
        T data,
        int statusCode)
    {
        return new ApiResult<T>
        {
            IsSuccess = true,
            StatusCode = statusCode,
            Data = data
        };
    }

    public static ApiResult<T> Failure(
        string errorMessage,
        int? statusCode = null)
    {
        return new ApiResult<T>
        {
            IsSuccess = false,
            StatusCode = statusCode,
            ErrorMessage = errorMessage
        };
    }
}