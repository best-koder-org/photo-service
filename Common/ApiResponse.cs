namespace PhotoService.Common;

/// <summary>
/// Standardized API response wrapper
/// </summary>
public class ApiResponse<T>
{
    public bool Success { get; set; }
    public T? Data { get; set; }
    public string? Message { get; set; }
    public List<string>? Errors { get; set; }
    public string? ErrorCode { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    public static ApiResponse<T> SuccessResult(T data, string? message = null) =>
        new() { Success = true, Data = data, Message = message };

    public static ApiResponse<T> FailureResult(string message, string? errorCode = null) =>
        new() { Success = false, Message = message, ErrorCode = errorCode, Errors = new List<string> { message } };

    public static ApiResponse<T> ValidationFailure(List<string> errors) =>
        new() { Success = false, Message = "Validation failed", Errors = errors, ErrorCode = "VALIDATION_ERROR" };
}
