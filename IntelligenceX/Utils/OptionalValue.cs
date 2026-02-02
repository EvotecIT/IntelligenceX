namespace IntelligenceX.Utils;

/// <summary>
/// Represents an optional value with explicit "specified" state.
/// </summary>
public readonly struct OptionalValue<T> {
    /// <summary>
    /// Initializes a new optional value.
    /// </summary>
    /// <param name="isSpecified">Whether the value is specified.</param>
    /// <param name="value">The value when specified.</param>
    public OptionalValue(bool isSpecified, T? value) {
        IsSpecified = isSpecified;
        Value = value;
    }

    /// <summary>
    /// Gets a value indicating whether the value is specified.
    /// </summary>
    public bool IsSpecified { get; }
    /// <summary>
    /// Gets the underlying value when specified.
    /// </summary>
    public T? Value { get; }

    /// <summary>
    /// Returns an unspecified optional value.
    /// </summary>
    public static OptionalValue<T> Unspecified => new(false, default);
    /// <summary>
    /// Returns a specified optional value with the provided payload.
    /// </summary>
    public static OptionalValue<T> FromValue(T? value) => new(true, value);
}
