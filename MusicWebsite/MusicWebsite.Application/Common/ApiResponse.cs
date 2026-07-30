namespace MusicWebsite.Application.Common;

/// <summary>Standard success/failure envelope returned by the API.</summary>
public class ApiResponse<T>
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public T? Data { get; set; }

    public static ApiResponse<T> Ok(T data, string? message = null)
        => new() { Success = true, Data = data, Message = message };

    public static ApiResponse<T> Fail(string message)
        => new() { Success = false, Message = message };
}

/// <summary>Shape returned by the *Delete / *Remove stored procedures.</summary>
public class MessageResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
}
