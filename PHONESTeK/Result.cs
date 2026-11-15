namespace PHONESTeK;

/// <summary>
/// Two different meanings of failure:
/// <c>Domain</c>      - an expected business outcome (unknown model, invalid quantity, over budget, ...);
/// <c>Operational</c> - a problem with the environment (file missing, unreadable, malformed content).
/// </summary>
public enum ErrorKind
{
    Domain,
    Operational,
}

/// <summary>Structured error: a stable code for programs, a message for people, and the kind.</summary>
public sealed record AppError(string Code, string Message, ErrorKind Kind)
{
    public static AppError Domain(string code, string message) => new(code, message, ErrorKind.Domain);

    public static AppError Operational(string code, string message) => new(code, message, ErrorKind.Operational);
}

public static class ErrorCodes
{
    // domain failures
    public const string PhoneNotFound = "phone_not_found";
    public const string InvalidQuantity = "invalid_quantity";
    public const string NotEligible = "not_eligible";
    public const string OverBudget = "over_budget";

    // operational failures (files)
    public const string FileMissing = "file_missing";
    public const string FileUnreadable = "file_unreadable";
    public const string FilePathInvalid = "file_path_invalid";
    public const string FileMalformed = "file_malformed";

    // a case nobody declared (defensive branch only)
    public const string UnknownCase = "unknown_case";
}

/// <summary>
/// Explicit outcome of an operation that may fail for an expected reason.
/// Exactly two cases (closed hierarchy, private base constructor):
/// <c>Ok</c> carries a value that is never null; <c>Fail</c> carries an <see cref="AppError"/> and has no value at all.
/// A failure therefore cannot be read as a success, and a success cannot be missing its payload.
/// </summary>
public abstract record Result<T>
{
    private Result() { }

    public sealed record Ok : Result<T>
    {
        public T Value { get; }

        internal Ok(T value)
        {
            ArgumentNullException.ThrowIfNull(value);
            Value = value;
        }
    }

    public sealed record Fail : Result<T>
    {
        public AppError Error { get; }

        internal Fail(AppError error)
        {
            ArgumentNullException.ThrowIfNull(error);
            Error = error;
        }
    }

    /// <summary>
    /// Map: Result&lt;T&gt; + (T -> U) -> Result&lt;U&gt;. Transforms a success with a pure function.
    /// On failure the error is passed along and <paramref name="transform"/> is NOT called.
    /// </summary>
    public Result<TNext> Map<TNext>(Func<T, TNext> transform)
    {
        ArgumentNullException.ThrowIfNull(transform);

        return this switch
        {
            Ok ok => new Result<TNext>.Ok(transform(ok.Value)),
            Fail fail => new Result<TNext>.Fail(fail.Error),
            _ => new Result<TNext>.Fail(UnknownCase()),
        };
    }

    /// <summary>
    /// Bind: Result&lt;T&gt; + (T -> Result&lt;U&gt;) -> Result&lt;U&gt;. Continues with a step that can itself fail;
    /// that step's own Result is returned as is. On failure the error is passed along and
    /// <paramref name="next"/> is NOT called.
    /// </summary>
    public Result<TNext> Bind<TNext>(Func<T, Result<TNext>> next)
    {
        ArgumentNullException.ThrowIfNull(next);

        return this switch
        {
            Ok ok => next(ok.Value),
            Fail fail => new Result<TNext>.Fail(fail.Error),
            _ => new Result<TNext>.Fail(UnknownCase()),
        };
    }

    private static AppError UnknownCase() =>
        AppError.Operational(ErrorCodes.UnknownCase, "Unknown Result case; the operation was not performed.");
}

/// <summary>Construction helpers so callers can write <c>Result.Ok(value)</c> and let the compiler infer T.</summary>
public static class Result
{
    public static Result<T> Ok<T>(T value) => new Result<T>.Ok(value);

    public static Result<T> Fail<T>(AppError error) => new Result<T>.Fail(error);
}

/// <summary>
/// Explicit "found / not found". <c>Some</c> carries the value; <c>None</c> carries nothing.
/// Absence is its own case, never a magic value: <c>Some(0)</c>, <c>Some("")</c> and <c>Some(false)</c>
/// are present values, and only <c>None</c> means "missing".
/// </summary>
public abstract record Maybe<T>
{
    private Maybe() { }

    public sealed record Some : Maybe<T>
    {
        public T Value { get; }

        internal Some(T value)
        {
            ArgumentNullException.ThrowIfNull(value);
            Value = value;
        }
    }

    public sealed record None : Maybe<T>
    {
        internal None() { }
    }

    /// <summary>For a required item: present becomes Ok, absent becomes the given meaningful failure.</summary>
    public Result<T> ToResult(AppError ifMissing)
    {
        ArgumentNullException.ThrowIfNull(ifMissing);

        return this switch
        {
            Some some => Result.Ok(some.Value),
            None => Result.Fail<T>(ifMissing),
            _ => Result.Fail<T>(AppError.Operational(ErrorCodes.UnknownCase, "Unknown Maybe case.")),
        };
    }
}

public static class Maybe
{
    public static Maybe<T> Some<T>(T value) => new Maybe<T>.Some(value);

    public static Maybe<T> None<T>() => new Maybe<T>.None();
}
