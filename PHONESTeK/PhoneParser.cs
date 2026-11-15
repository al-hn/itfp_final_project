using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace PHONESTeK;

/// <summary>
/// Boundary parsing and validation: raw text (or already-typed fields from another format such as JSON)
/// becomes a validated <see cref="Smartphone"/>. Invalid fields are rejected with a message;
/// nothing is silently defaulted. Both entry points share the same rules.
/// </summary>
public static class PhoneParser
{
    public const char Separator = '|';
    public const int FieldCount = 6;
    public const int MinYear = 2000;
    public const int MaxYear = 2100;

    /// <summary>Parses "Model|ReleaseYear|RamGb|StorageGb|BatteryMah|OriginalPrice".</summary>
    public static bool TryParseLine(
        string? line,
        [NotNullWhen(true)] out Smartphone? phone,
        out string error)
    {
        phone = null;

        if (line is null)
        {
            error = "Line is missing (null).";
            return false;
        }
        if (string.IsNullOrWhiteSpace(line))
        {
            error = "Line is empty.";
            return false;
        }

        var parts = line.Split(Separator);
        if (parts.Length != FieldCount)
        {
            error = $"Expected {FieldCount} fields separated by '{Separator}' but found {parts.Length}.";
            return false;
        }

        var model = parts[0].Trim();
        if (!CheckModel(model, out error)) return false;

        if (!TryParseWhole(parts[1], "ReleaseYear", out var year, out error)) return false;
        if (!CheckYear(year, out error)) return false;

        if (!TryParseWhole(parts[2], "RamGb", out var ram, out error)) return false;
        if (!IsPositive(ram, "RamGb", out error)) return false;

        if (!TryParseWhole(parts[3], "StorageGb", out var storage, out error)) return false;
        if (!IsPositive(storage, "StorageGb", out error)) return false;

        if (!TryParseWhole(parts[4], "BatteryMah", out var battery, out error)) return false;
        if (!IsPositive(battery, "BatteryMah", out error)) return false;

        var priceText = parts[5].Trim();
        if (!decimal.TryParse(
                priceText,
                NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture,
                out var price))
        {
            error = $"OriginalPrice '{priceText}' is not a valid number.";
            return false;
        }
        if (!CheckPrice(price, out error)) return false;

        phone = new Smartphone(model, year, ram, storage, battery, price);
        error = string.Empty;
        return true;
    }

    /// <summary>
    /// Validates fields that are already typed (for example read from JSON) with the same rules as
    /// <see cref="TryParseLine"/>.
    /// </summary>
    public static bool TryCreate(
        string? model,
        int releaseYear,
        int ramGb,
        int storageGb,
        int batteryMah,
        decimal originalPrice,
        [NotNullWhen(true)] out Smartphone? phone,
        out string error)
    {
        phone = null;

        var trimmedModel = model?.Trim() ?? string.Empty;
        if (!CheckModel(trimmedModel, out error)) return false;
        if (!CheckYear(releaseYear, out error)) return false;
        if (!IsPositive(ramGb, "RamGb", out error)) return false;
        if (!IsPositive(storageGb, "StorageGb", out error)) return false;
        if (!IsPositive(batteryMah, "BatteryMah", out error)) return false;
        if (!CheckPrice(originalPrice, out error)) return false;

        phone = new Smartphone(trimmedModel, releaseYear, ramGb, storageGb, batteryMah, originalPrice);
        error = string.Empty;
        return true;
    }

    /// <summary>
    /// Parses every line. A null sequence is missing input and throws; an empty sequence is a valid,
    /// empty dataset. Line numbers in errors are 1-based.
    /// </summary>
    public static ParsedDataset ParseAll(IEnumerable<string?> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var phones = ImmutableArray.CreateBuilder<Smartphone>();
        var errors = ImmutableArray.CreateBuilder<ParseError>();
        var lineNumber = 0;

        foreach (var line in lines)
        {
            lineNumber++;
            if (TryParseLine(line, out var phone, out var error))
                phones.Add(phone);
            else
                errors.Add(new ParseError(lineNumber, line, error));
        }

        return new ParsedDataset(phones.ToImmutable(), errors.ToImmutable());
    }

    private static bool CheckModel(string model, out string error)
    {
        if (model.Length == 0)
        {
            error = "Model must not be empty.";
            return false;
        }
        error = string.Empty;
        return true;
    }

    private static bool CheckYear(int year, out string error)
    {
        if (year < MinYear || year > MaxYear)
        {
            error = $"ReleaseYear must be between {MinYear} and {MaxYear} (got {year}).";
            return false;
        }
        error = string.Empty;
        return true;
    }

    private static bool CheckPrice(decimal price, out string error)
    {
        if (price <= 0m)
        {
            error = $"OriginalPrice must be greater than 0 (got {price}).";
            return false;
        }
        error = string.Empty;
        return true;
    }

    private static bool TryParseWhole(string text, string field, out int value, out string error)
    {
        var trimmed = text.Trim();
        if (!int.TryParse(trimmed, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value))
        {
            error = $"{field} '{trimmed}' is not a whole number.";
            return false;
        }
        error = string.Empty;
        return true;
    }

    private static bool IsPositive(int value, string field, out string error)
    {
        if (value <= 0)
        {
            error = $"{field} must be greater than 0 (got {value}).";
            return false;
        }
        error = string.Empty;
        return true;
    }
}
