namespace Healthcare.SharedKernel;

/// <summary>
/// Outcome of an operation that can fail in an expected way.
/// Expected failures are values, not exceptions — this keeps the happy path
/// visible and forces callers to handle failure.
/// </summary>
public readonly record struct Result
{
    private Result(bool isSuccess, string? errorCode, string? errorMessage)
    {
        IsSuccess = isSuccess;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
    }

    public bool IsSuccess { get; }

    public bool IsFailure => !IsSuccess;

    public string? ErrorCode { get; }

    public string? ErrorMessage { get; }

    public static Result Success() => new(true, null, null);

    public static Result Failure(string errorCode, string errorMessage) =>
        new(false, errorCode, errorMessage);
}

public readonly record struct Result<T>
{
    private readonly T? _value;

    private Result(bool isSuccess, T? value, string? errorCode, string? errorMessage)
    {
        IsSuccess = isSuccess;
        _value = value;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
    }

    public bool IsSuccess { get; }

    public bool IsFailure => !IsSuccess;

    /// <summary>
    /// The value, guaranteed non-null when <see cref="IsSuccess"/>.
    ///
    /// Reads the backing field rather than exposing a <c>T?</c> property. A
    /// nullable property would leave the compiler unable to prove that a
    /// successful result carries a value, so every caller would need a
    /// null-forgiving operator after checking IsSuccess — and one missed check
    /// would become a NullReferenceException at runtime instead of a compile
    /// error.
    ///
    /// Throwing rather than returning null makes the mistake loud in tests,
    /// which is where it is cheap to fix.
    /// </summary>
    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException(
            "Cannot read Value on a failed Result. Check IsSuccess first.");

    public string? ErrorCode { get; }

    public string? ErrorMessage { get; }

    public static Result<T> Success(T value) => new(true, value, null, null);

    public static Result<T> Failure(string errorCode, string errorMessage) =>
        new(false, default, errorCode, errorMessage);

    public static implicit operator Result<T>(T value) => Success(value);
}
