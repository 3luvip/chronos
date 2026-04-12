using System;

namespace Chronos.Core.Common;

public readonly record struct Result<T, TError>
{
    private readonly T?      _value;
    private readonly TError? _error;

    public bool   IsOk  { get; }
    public T      Value => IsOk   ? _value! : throw new InvalidOperationException("Result is error.");
    public TError Error => !IsOk  ? _error! : throw new InvalidOperationException("Result is ok.");

    private Result(T value)      { IsOk = true;  _value = value;   _error = default; }
    private Result(TError error) { IsOk = false; _value = default; _error = error;   }

    public static Result<T, TError> Ok(T value)        => new(value);
    public static Result<T, TError> Fail(TError error) => new(error);

    public Result<U, TError> Map<U>(Func<T, U> f) =>
        IsOk ? Result<U, TError>.Ok(f(Value)) : Result<U, TError>.Fail(Error);

    public TOut Match<TOut>(Func<T, TOut> onOk, Func<TError, TOut> onError) =>
        IsOk ? onOk(Value) : onError(Error);
}

public readonly record struct Result<TError>
{
    public bool    IsOk  { get; }
    public TError? Error { get; }

    private Result(bool ok, TError? err) { IsOk = ok; Error = err; }

    public static Result<TError> Ok()            => new(true,  default);
    public static Result<TError> Fail(TError e)  => new(false, e);
}
