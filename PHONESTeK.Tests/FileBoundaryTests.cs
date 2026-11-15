using Xunit;

namespace PHONESTeK.Tests;

/// <summary>
/// Assignment 7: the file boundary. Every test uses its own temporary folder (created in the constructor,
/// deleted in Dispose), so nothing depends on files that happen to exist on a particular computer.
/// </summary>
public sealed class FileBoundaryTests : IDisposable
{
    private const int Year = 2026;
    private static readonly EligibilityCriteria Criteria = new(2022, 8, 150_000m, 700_000m);
    private static readonly Func<Smartphone, decimal> Policy = DiscountPolicies.Standard(Year);

    private const string ValidJson = """
        [
          {"model": "Galaxy S22", "releaseYear": 2022, "ramGb": 8, "storageGb": 128, "batteryMah": 3700, "originalPrice": 350000},
          {"model": "Xiaomi 14", "releaseYear": 2024, "ramGb": 12, "storageGb": 256, "batteryMah": 4610, "originalPrice": 450000}
        ]
        """;

    private readonly string _dir;

    public FileBoundaryTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "phonestek-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    private string PathFor(string fileName) => Path.Combine(_dir, fileName);

    private string WriteFile(string fileName, string content)
    {
        var path = PathFor(fileName);
        File.WriteAllText(path, content);
        return path;
    }

    // ---------- SUCCESS ----------

    [Fact]
    public void LoadJson_ValidFile_ReturnsAllPhonesWithTheirData()
    {
        var path = WriteFile("phones.json", ValidJson);

        var phones = ExpectResult.Ok(PhoneLoader.LoadJson(path));

        Assert.Equal(2, phones.Length);
        Assert.Equal(new Smartphone("Galaxy S22", 2022, 8, 128, 3700, 350_000m), phones[0]);
        Assert.Equal("Xiaomi 14", phones[1].Model);
    }

    [Fact]
    public void LoadJson_EmptyArray_IsAValidEmptyDataset()
    {
        var path = WriteFile("empty.json", "[]");

        var phones = ExpectResult.Ok(PhoneLoader.LoadJson(path));

        Assert.Empty(phones);
    }

    [Fact]
    public void LoadPipeText_ValidAndEmptyFiles_AreSuccesses()
    {
        var valid = WriteFile("phones.txt", string.Join(Environment.NewLine, TestData.SampleLines));
        var empty = WriteFile("empty.txt", string.Empty);

        Assert.Equal(9, ExpectResult.Ok(PhoneLoader.LoadPipeText(valid)).Length);
        Assert.Empty(ExpectResult.Ok(PhoneLoader.LoadPipeText(empty)));
    }

    [Fact]
    public void FileToQuote_CompleteChain_LoadsThenQuotes()
    {
        var path = WriteFile("phones.json", ValidJson);
        var request = new QuoteRequest("Galaxy S22", 2, Maybe.None<decimal>());

        var result = PhoneLoader.LoadJson(path)
            .Bind(phones => QuoteService.Quote(phones, request, Criteria, Policy));

        Assert.Equal(new QuoteView("Galaxy S22", 2, "20%", "280,000", "560,000"), ExpectResult.Ok(result));
    }

    // ---------- MISSING FILE ----------

    [Fact]
    public void LoadJson_MissingFile_ReportsAnOperationalFailure_AndCreatesNothing()
    {
        var path = PathFor("does_not_exist.json");

        var error = ExpectResult.Fail(PhoneLoader.LoadJson(path));

        Assert.Equal(ErrorCodes.FileMissing, error.Code);
        Assert.Equal(ErrorKind.Operational, error.Kind);
        Assert.Contains(path, error.Message);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void LoadPipeText_MissingFile_ReportsAnOperationalFailure()
    {
        var path = PathFor("does_not_exist.txt");

        var error = ExpectResult.Fail(PhoneLoader.LoadPipeText(path));

        Assert.Equal(ErrorCodes.FileMissing, error.Code);
        Assert.Equal(ErrorKind.Operational, error.Kind);
        Assert.Contains(path, error.Message);
    }

    [Fact]
    public void MissingFile_LeavesAnAlreadyLoadedCatalogUnchanged_AndSkipsTheDependentStep()
    {
        var goodPath = WriteFile("phones.json", ValidJson);
        var catalog = ExpectResult.Ok(PhoneLoader.LoadJson(goodPath));
        var before = catalog.ToArray();

        var policyCalls = 0;
        Func<Smartphone, decimal> countingPolicy = _ =>
        {
            policyCalls++;
            return 10m;
        };
        var request = new QuoteRequest("Galaxy S22", 1, Maybe.None<decimal>());

        var failed = PhoneLoader.LoadJson(PathFor("does_not_exist.json"))
            .Bind(phones => QuoteService.Quote(phones, request, Criteria, countingPolicy));

        Assert.Equal(ErrorCodes.FileMissing, ExpectResult.Fail(failed).Code);
        Assert.Equal(0, policyCalls);                    // the quote step never ran
        Assert.True(before.SequenceEqual(catalog));      // the existing data is untouched
        Assert.Equal(2, catalog.Length);
    }

    [Fact]
    public void LoadJson_DirectoryInsteadOfFile_IsReportedAsUnreadable()
    {
        var error = ExpectResult.Fail(PhoneLoader.LoadJson(_dir));

        Assert.Equal(ErrorCodes.FileUnreadable, error.Code);
        Assert.Equal(ErrorKind.Operational, error.Kind);
        Assert.Contains(_dir, error.Message);
    }

    [Fact]
    public void Load_EmptyPath_IsReportedAsAnInvalidPath()
    {
        Assert.Equal(ErrorCodes.FilePathInvalid, ExpectResult.Fail(PhoneLoader.LoadJson(string.Empty)).Code);
        Assert.Equal(ErrorCodes.FilePathInvalid, ExpectResult.Fail(PhoneLoader.LoadPipeText(string.Empty)).Code);
    }

    // ---------- MALFORMED FILE ----------

    [Fact]
    public void LoadJson_BrokenSyntax_BecomesAClearFailure_WithoutAnyPhones()
    {
        var path = WriteFile("broken.json", """
            [
              {"model": "Galaxy S22", "releaseYear": 2022, "ramGb": 8,
            """);

        var error = ExpectResult.Fail(PhoneLoader.LoadJson(path));

        Assert.Equal(ErrorCodes.FileMalformed, error.Code);
        Assert.Equal(ErrorKind.Operational, error.Kind);
        Assert.Contains(path, error.Message);
        Assert.Contains("not valid JSON", error.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{}")]
    [InlineData("42")]
    [InlineData("\"text\"")]
    [InlineData("null")]
    public void LoadJson_ContentThatIsNotAPhoneArray_IsMalformed(string content)
    {
        var path = WriteFile("wrong_shape.json", content);

        var error = ExpectResult.Fail(PhoneLoader.LoadJson(path));

        Assert.Equal(ErrorCodes.FileMalformed, error.Code);
        Assert.Contains(path, error.Message);
    }

    [Fact]
    public void LoadJson_WrongValueType_IsMalformed()
    {
        var path = WriteFile("wrong_type.json", """
            [{"model": "Galaxy S22", "releaseYear": "twenty twenty-two", "ramGb": 8, "storageGb": 128, "batteryMah": 3700, "originalPrice": 350000}]
            """);

        var error = ExpectResult.Fail(PhoneLoader.LoadJson(path));

        Assert.Equal(ErrorCodes.FileMalformed, error.Code);
    }

    [Fact]
    public void LoadJson_InvalidEntries_AreAllReported_AndNothingIsLoaded()
    {
        var path = WriteFile("invalid_values.json", """
            [
              {"model": "Galaxy S22", "releaseYear": 2022, "ramGb": 8, "storageGb": 128, "batteryMah": 3700, "originalPrice": 350000},
              {"model": "Old Phone", "releaseYear": 1850, "ramGb": 8, "storageGb": 128, "batteryMah": 3700, "originalPrice": 350000},
              {"model": "No Ram", "releaseYear": 2023, "storageGb": 128, "batteryMah": 3700, "originalPrice": 350000},
              null,
              {"model": "Free Phone", "releaseYear": 2023, "ramGb": 8, "storageGb": 128, "batteryMah": 3700, "originalPrice": 0}
            ]
            """);

        var error = ExpectResult.Fail(PhoneLoader.LoadJson(path));   // a failure carries no phones at all

        Assert.Equal(ErrorCodes.FileMalformed, error.Code);
        Assert.Contains("4 invalid entries", error.Message);
        Assert.Contains("entry 2: ReleaseYear must be between", error.Message);
        Assert.Contains("entry 3: RamGb is missing", error.Message);
        Assert.Contains("entry 4: is null", error.Message);
        Assert.Contains("entry 5: OriginalPrice must be greater than 0", error.Message);
        Assert.DoesNotContain("entry 1", error.Message);   // the valid entry is not blamed
    }

    [Fact]
    public void LoadPipeText_InvalidLines_AreReportedWithLineNumbers_AndNothingIsLoaded()
    {
        var path = WriteFile("invalid.txt", string.Join(Environment.NewLine, new[]
        {
            "Galaxy S22|2022|8|128|3700|350000",
            "iPhone 14|2022|six|128|3279|500000",
            "Pixel 7|2022|8|128|4355",
        }));

        var error = ExpectResult.Fail(PhoneLoader.LoadPipeText(path));

        Assert.Equal(ErrorCodes.FileMalformed, error.Code);
        Assert.Equal(ErrorKind.Operational, error.Kind);
        Assert.Contains("2 invalid line(s)", error.Message);
        Assert.Contains("line 2: RamGb 'six' is not a whole number", error.Message);
        Assert.Contains("line 3: Expected 6 fields", error.Message);
        Assert.Contains(path, error.Message);
    }

    // ---------- SHARED RULES ----------

    [Theory]
    [InlineData("Phone", 1999, 8, 128, 4000, 300_000, "ReleaseYear must be between")]
    [InlineData("Phone", 2022, 0, 128, 4000, 300_000, "RamGb must be greater than 0")]
    [InlineData("Phone", 2022, 8, 128, 4000, 0, "OriginalPrice must be greater than 0")]
    [InlineData("   ", 2022, 8, 128, 4000, 300_000, "Model must not be empty")]
    public void TryCreate_AppliesTheSameRulesAsTheTextParser(
        string model, int year, int ram, int storage, int battery, int price, string expectedFragment)
    {
        var ok = PhoneParser.TryCreate(model, year, ram, storage, battery, price, out var phone, out var error);

        Assert.False(ok);
        Assert.Null(phone);
        Assert.Contains(expectedFragment, error);
    }

    [Fact]
    public void TryCreate_BoundaryYear2000_IsAccepted()
    {
        var ok = PhoneParser.TryCreate(" Edge ", PhoneParser.MinYear, 1, 1, 1, 1m, out var phone, out _);

        Assert.True(ok);
        Assert.Equal(new Smartphone("Edge", 2000, 1, 1, 1, 1m), phone);
    }
}
