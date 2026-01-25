namespace IntelligenceX.Utils;

public readonly struct OptionalValue<T> {
    public OptionalValue(bool isSpecified, T? value) {
        IsSpecified = isSpecified;
        Value = value;
    }

    public bool IsSpecified { get; }
    public T? Value { get; }

    public static OptionalValue<T> Unspecified => new(false, default);
    public static OptionalValue<T> FromValue(T? value) => new(true, value);
}
