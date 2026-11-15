using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace PHONESTeK;

/// <summary>
/// The exception boundary for phone data files. This is the ONLY place where file and JSON exceptions are
/// caught. Each one becomes an explicit <see cref="Result{T}"/> failure of kind Operational that names the
/// file and the problem. Nothing is swallowed, and nothing is returned unless the whole file is valid:
/// there is no partial dataset, so no other data can be left half-updated.
/// Everything after loading (PhoneParser, pricing, quotes) is pure and never touches files.
/// </summary>
public static class PhoneLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    /// <summary>Loads a text file with one "Model|ReleaseYear|RamGb|StorageGb|BatteryMah|OriginalPrice" line per phone.</summary>
    public static Result<ImmutableArray<Smartphone>> LoadPipeText(string path) =>
        ReadFile(path, p => File.ReadAllLines(p))
            .Bind(lines => ParseLines(path, lines));

    /// <summary>
    /// Loads a JSON file: an array of objects with model, releaseYear, ramGb, storageGb, batteryMah, originalPrice
    /// (property names are case-insensitive).
    /// </summary>
    public static Result<ImmutableArray<Smartphone>> LoadJson(string path) =>
        ReadFile(path, p => File.ReadAllText(p))
            .Bind(text => ParseJson(path, text));

    // ---------- exception boundary: file access ----------

    private static Result<T> ReadFile<T>(string path, Func<string, T> read)
    {
        try
        {
            return Result.Ok(read(path));
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return Failure<T>(ErrorCodes.FileMissing, $"Cannot load '{path}': the file was not found.");
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return Failure<T>(ErrorCodes.FileUnreadable, $"Cannot load '{path}': the file could not be read ({ex.Message}).");
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            return Failure<T>(ErrorCodes.FilePathInvalid, $"Cannot load '{path}': the path is not valid ({ex.Message}).");
        }
    }

    // ---------- exception boundary: malformed content ----------

    private static Result<ImmutableArray<Smartphone>> ParseLines(string path, string[] lines)
    {
        var dataset = PhoneParser.ParseAll(lines);
        if (dataset.IsValid)
            return Result.Ok(dataset.Phones);

        var details = string.Join("; ", dataset.Errors.Select(e => $"line {e.LineNumber}: {e.Message}"));
        return Failure<ImmutableArray<Smartphone>>(
            ErrorCodes.FileMalformed,
            $"'{path}' has {dataset.Errors.Length} invalid line(s) and was not loaded. {details}");
    }

    private static Result<ImmutableArray<Smartphone>> ParseJson(string path, string json)
    {
        List<PhoneDto?>? entries;
        try
        {
            entries = JsonSerializer.Deserialize<List<PhoneDto?>>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            return Failure<ImmutableArray<Smartphone>>(
                ErrorCodes.FileMalformed,
                $"'{path}' is not valid JSON for a phone list and was not loaded ({ex.Message}).");
        }

        if (entries is null)
        {
            return Failure<ImmutableArray<Smartphone>>(
                ErrorCodes.FileMalformed,
                $"'{path}' must contain a JSON array of phones and was not loaded.");
        }

        var phones = ImmutableArray.CreateBuilder<Smartphone>();
        var problems = new List<string>();

        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            if (entry is null)
            {
                problems.Add($"entry {i + 1}: is null");
            }
            else if (TryToSmartphone(entry, out var phone, out var error))
            {
                phones.Add(phone);
            }
            else
            {
                problems.Add($"entry {i + 1}: {error}");
            }
        }

        if (problems.Count > 0)
        {
            return Failure<ImmutableArray<Smartphone>>(
                ErrorCodes.FileMalformed,
                $"'{path}' has {problems.Count} invalid entr{(problems.Count == 1 ? "y" : "ies")} and was not loaded. {string.Join("; ", problems)}");
        }

        return Result.Ok(phones.ToImmutable());
    }

    private static bool TryToSmartphone(PhoneDto dto, [NotNullWhen(true)] out Smartphone? phone, out string error)
    {
        phone = null;

        // A missing property is not the same as 0: nullable fields tell "absent" from a real value.
        if (dto.ReleaseYear is not int year) { error = "ReleaseYear is missing."; return false; }
        if (dto.RamGb is not int ram) { error = "RamGb is missing."; return false; }
        if (dto.StorageGb is not int storage) { error = "StorageGb is missing."; return false; }
        if (dto.BatteryMah is not int battery) { error = "BatteryMah is missing."; return false; }
        if (dto.OriginalPrice is not decimal price) { error = "OriginalPrice is missing."; return false; }

        return PhoneParser.TryCreate(dto.Model, year, ram, storage, battery, price, out phone, out error);
    }

    private static Result<T> Failure<T>(string code, string message) =>
        Result.Fail<T>(AppError.Operational(code, message));

    /// <summary>Shape of one JSON entry. Every field is nullable so that "missing" can be detected.</summary>
    internal sealed class PhoneDto
    {
        public string? Model { get; set; }
        public int? ReleaseYear { get; set; }
        public int? RamGb { get; set; }
        public int? StorageGb { get; set; }
        public int? BatteryMah { get; set; }
        public decimal? OriginalPrice { get; set; }
    }
}
