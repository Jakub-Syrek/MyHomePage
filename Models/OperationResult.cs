namespace MyHomePage.Models;

/// <summary>
/// Encapsulates the outcome of a void operation.
/// Replaces raw boolean + string tuples with an expressive, type-safe Result pattern.
/// </summary>
public sealed class OperationResult
{
    public bool IsSuccess { get; }
    public string Message { get; }

    private OperationResult(bool success, string message)
    {
        IsSuccess = success;
        Message = message;
    }

    public static OperationResult Success(string message = "Operation completed successfully.") =>
        new(true, message);

    public static OperationResult Failure(string message) =>
        new(false, message);
}

/// <summary>
/// Encapsulates the outcome of an operation that produces a value.
/// </summary>
public sealed class OperationResult<T>
{
    public bool IsSuccess { get; }
    public string Message { get; }
    public T? Value { get; }

    private OperationResult(bool success, string message, T? value)
    {
        IsSuccess = success;
        Message = message;
        Value = value;
    }

    public static OperationResult<T> Success(T value, string message = "Operation completed successfully.") =>
        new(true, message, value);

    public static OperationResult<T> Failure(string message) =>
        new(false, message, default);
}
