using Xunit;

namespace PHONESTeK.Tests;

public sealed class Assignment8Tests
{
    private static readonly EligibilityCriteria Criteria = new(2022, 8, 150_000m, 700_000m);

    [Fact]
    public void EligibleReport_FiltersProjectsAndOrdersDeterministically()
    {
        var result = PhoneReports.EligibleCatalog(TestData.Catalog(), Criteria);
        Assert.Equal(new[] { "Redmi Note 12", "Galaxy S22", "Xiaomi 14", "iPhone 15 Pro", "Galaxy S24 Ultra" }, result.Select(p => p.Model));
    }

    [Fact]
    public void EligibleReport_EmptyInputReturnsEmptySnapshot() =>
        Assert.Empty(PhoneReports.EligibleCatalog(Array.Empty<Smartphone>(), Criteria));

    [Fact]
    public void EligibleReport_NoMatchesReturnsEmptySnapshot() =>
        Assert.Empty(PhoneReports.EligibleCatalog(TestData.Catalog(), new(2100, 64, 1_000_000m, 2_000_000m)));

    [Fact]
    public void EligibleReport_TiedPricesUseModelAsStableTieBreaker()
    {
        var phones = new[]
        {
            new Smartphone("Zulu", 2024, 8, 128, 4000, 200_000m),
            new Smartphone("Alpha", 2024, 8, 128, 4000, 200_000m)
        };
        Assert.Equal(new[] { "Alpha", "Zulu" }, PhoneReports.EligibleCatalog(phones, Criteria).Select(p => p.Model));
    }

    [Fact]
    public void BrandReport_GroupsAndCalculatesCountSumAndAverage()
    {
        var samsung = Assert.Single(PhoneReports.ValueByBrand(TestData.Catalog()).Where(x => x.Brand == "Galaxy"));
        Assert.Equal(3, samsung.PhoneCount);
        Assert.Equal(1_950_000m, samsung.TotalOriginalValue);
        Assert.Equal(650_000m, samsung.AverageOriginalPrice);
    }

    [Fact]
    public void BrandReport_EmptyInputIsEmpty() => Assert.Empty(PhoneReports.ValueByBrand(Array.Empty<Smartphone>()));

    [Fact]
    public void SavingsExtension_ReturnsDifferenceBetweenOriginalAndFinalPrice()
    {
        var quote = new PhoneQuote(TestData.Catalog()[0], 20m, 70_000m, 280_000m);
        Assert.Equal(70_000m, quote.Savings());
    }

    [Fact]
    public void OrderingExtension_OrdersByPriceThenModel()
    {
        var catalog = TestData.Catalog();
        var quotes = new[]
        {
            new PhoneQuote(catalog[0], 0, 0, 300_000m),
            new PhoneQuote(catalog[1], 0, 0, 300_000m)
        };
        Assert.Equal(new[] { "Galaxy S22", "iPhone 14" }, quotes.OrderByFinalPriceThenModel().Select(q => q.Phone.Model));
    }

    [Fact]
    public void CurriedConfigurations_ArePartiallyAppliedAndDifferOnSamePhone()
    {
        var phone = TestData.Catalog()[0];
        var first = PhoneScoring.CreateScore(new(10, 1, 1, 0));
        var second = PhoneScoring.CreateScore(new(5, 1, 2, 0));
        Assert.Equal(3908, first(phone));
        Assert.Equal(7568, second(phone));
        Assert.NotEqual(first(phone), second(phone));
    }

    [Fact]
    public void DeferredWorkflow_DoesNotProcessUntilEnumeration()
    {
        var calls = 0;
        IEnumerable<Smartphone> Source()
        {
            foreach (var p in TestData.Catalog()) { calls++; yield return p; }
        }
        var query = PhoneStreams.EligibleDeferred(Source(), Criteria);
        Assert.Equal(0, calls);
        using var iterator = query.GetEnumerator();
        Assert.True(iterator.MoveNext());
        Assert.True(calls > 0);
    }

    [Fact]
    public void MaterialisedSnapshot_CanBeEnumeratedRepeatedlyWithoutReprocessingSource()
    {
        var calls = 0;
        IEnumerable<Smartphone> Source()
        {
            foreach (var p in TestData.Catalog()) { calls++; yield return p; }
        }
        var snapshot = PhoneStreams.EligibleDeferred(Source(), Criteria).ToArray();
        var afterMaterialisation = calls;
        _ = snapshot.Count();
        _ = snapshot.Count();
        Assert.Equal(9, afterMaterialisation);
        Assert.Equal(afterMaterialisation, calls);
    }

    [Fact]
    public void NativeCall_ReturnsManuallyCalculatedScore()
    {
        // C formula: 8*100 + (128/10) + (3700/100) = 849 (integer division).
        var result = NativePhoneScorer.Score(TestData.Catalog()[0], (ram, storage, battery) =>
            ram * 100 + storage / 10 + battery / 100);
        Assert.Equal(849, ExpectResult.Ok(result));
    }

    [Fact]
    public void NativeLibrary_PInvokeCallReturnsTheCImplementationValue()
    {
        // Requires the checked-in Linux x64 runtime binary, or a freshly built one.
        var result = NativePhoneScorer.Score(TestData.Catalog()[0]);
        Assert.Equal(849, ExpectResult.Ok(result));
    }

    [Fact]
    public void NativeAdapterFailure_BecomesOperationalResult()
    {
        var result = NativePhoneScorer.Score(TestData.Catalog()[0], (_, _, _) => throw new DllNotFoundException("controlled test failure"));
        var error = ExpectResult.Fail(result);
        Assert.Equal(NativePhoneScorer.NativeFailureCode, error.Code);
        Assert.Equal(ErrorKind.Operational, error.Kind);
        Assert.Contains("controlled test failure", error.Message);
    }

    [Fact]
    public void NativeAdapter_RejectsInvalidDomainValuesBeforeCallingNativeFunction()
    {
        var phone = new Smartphone("Invalid", 2024, 0, 128, 3700, 100_000m);
        var called = false;
        var result = NativePhoneScorer.Score(phone, (_, _, _) => { called = true; return 0; });
        Assert.Equal(NativePhoneScorer.InvalidInputCode, ExpectResult.Fail(result).Code);
        Assert.False(called);
    }

    [Fact]
    public void FileLoader_ReleasesHandleAfterSuccessfulConvenienceRead()
    {
        var directory = Path.Combine(Path.GetTempPath(), "phonestek-a8-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "catalog.json");
            File.WriteAllText(path, "[]");
            Assert.Empty(ExpectResult.Ok(PhoneLoader.LoadJson(path)));
            var moved = Path.Combine(directory, "moved.json");
            File.Move(path, moved); // succeeds on Windows only when the loader has released its handle
            Assert.True(File.Exists(moved));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void FileLoader_ReleasesHandleAfterMalformedContentFailure()
    {
        var directory = Path.Combine(Path.GetTempPath(), "phonestek-a8-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "broken.json");
            File.WriteAllText(path, "{");
            Assert.Equal(ErrorCodes.FileMalformed, ExpectResult.Fail(PhoneLoader.LoadJson(path)).Code);
            File.Delete(path);
            Assert.False(File.Exists(path));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void FullAssignment8Workflow_ReportsAndConfiguredFunctionsUseActualCatalog()
    {
        var phones = TestData.Catalog();
        var reports = PhoneReports.Create(phones, Criteria);
        Assert.Equal(5, reports.EligiblePhones.Length);
        Assert.Equal(6, reports.BrandValues.Length);
        Assert.NotEqual(PhoneScoring.Balanced(phones[0]), PhoneScoring.BatteryFocused(phones[0]));
        Assert.Equal(849, ExpectResult.Ok(NativePhoneScorer.Score(phones[0], (r, s, b) => r * 100 + s / 10 + b / 100)));
    }
}
