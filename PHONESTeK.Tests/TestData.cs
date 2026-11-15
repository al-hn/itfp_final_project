using System.Collections.Immutable;
using Xunit;

namespace PHONESTeK.Tests;

/// <summary>Shared test helpers: the sample catalog and short assertions for the two Result cases.</summary>
internal static class TestData
{
    // Same 9 rows as Data/phones.txt
    public static readonly string[] SampleLines =
    {
        "Galaxy S22|2022|8|128|3700|350000",
        "iPhone 14|2022|6|128|3279|500000",
        "Pixel 6|2021|8|128|4614|280000",
        "iPhone 15 Pro|2023|8|256|3274|600000",
        "Galaxy S24 Ultra|2024|12|512|5000|700000",
        "Xiaomi 14|2024|12|256|4610|450000",
        "Nokia Budget|2023|8|64|4000|120000",
        "Galaxy Z Fold 6|2024|12|512|4400|900000",
        "Redmi Note 12|2023|8|128|5000|150000",
    };

    public static ImmutableArray<Smartphone> Catalog()
    {
        var parsed = PhoneParser.ParseAll(SampleLines);
        Assert.True(parsed.IsValid);
        return parsed.Phones;
    }
}

internal static class ExpectResult
{
    /// <summary>Asserts the result is a success and returns its value.</summary>
    public static T Ok<T>(Result<T> result) => Assert.IsType<Result<T>.Ok>(result).Value;

    /// <summary>Asserts the result is a failure and returns its error.</summary>
    public static AppError Fail<T>(Result<T> result) => Assert.IsType<Result<T>.Fail>(result).Error;
}
