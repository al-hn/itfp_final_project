using System.Collections.Immutable;
using Xunit;

namespace PHONESTeK.Tests;

public class PricingWorkflowTests
{
    private const int Year = 2026;
    private static readonly EligibilityCriteria Criteria = new(2022, 8, 150_000m, 700_000m);

    // Same 9 rows as data/phones.txt
    private static readonly string[] SampleLines =
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

    private static ImmutableArray<Smartphone> Sample()
    {
        var parsed = PhoneParser.ParseAll(SampleLines);
        Assert.True(parsed.IsValid);
        return parsed.Phones;
    }

    private static Smartphone Phone(string model, int year, int ram, decimal price) =>
        new(model, year, ram, 128, 4000, price);

    // ---------- NORMAL WORKFLOW ----------

    [Fact]
    public void NormalWorkflow_StandardPolicy_ProducesDocumentedReport()
    {
        var report = PricingPipeline.Run(Sample(), Criteria, DiscountPolicies.Standard(Year));

        Assert.Equal(5, report.Count);
        Assert.Equal(
            new[] { "Redmi Note 12", "Galaxy S22", "Xiaomi 14", "iPhone 15 Pro", "Galaxy S24 Ultra" },
            report.Quotes.Select(q => q.Phone.Model).ToArray());
        Assert.Equal(
            new[] { 15m, 20m, 5m, 15m, 5m },
            report.Quotes.Select(q => q.DiscountPercent).ToArray());
        Assert.Equal(
            new[] { 22_500m, 70_000m, 22_500m, 90_000m, 35_000m },
            report.Quotes.Select(q => q.DiscountAmount).ToArray());
        Assert.Equal(
            new[] { 127_500m, 280_000m, 427_500m, 510_000m, 665_000m },
            report.Quotes.Select(q => q.FinalPrice).ToArray());
        Assert.Equal(2_010_000m, report.TotalFinalPrice);
    }

    [Fact]
    public void ApplyDiscounts_TenPercentOf500000_Gives450000()
    {
        var phones = new[] { Phone("Test", 2023, 8, 500_000m) };

        var quotes = PricingStages.ApplyDiscounts(phones, _ => 10m);

        var quote = Assert.Single(quotes);
        Assert.Equal(50_000m, quote.DiscountAmount);
        Assert.Equal(450_000m, quote.FinalPrice);
    }

    // ---------- EMPTY DATASET ----------

    [Fact]
    public void EmptyDataset_IsValid_AndProducesEmptyReport()
    {
        var parsed = PhoneParser.ParseAll(Array.Empty<string>());

        Assert.True(parsed.IsValid);
        Assert.Empty(parsed.Phones);

        var report = PricingPipeline.Run(parsed.Phones, Criteria, DiscountPolicies.Standard(Year));

        Assert.Empty(report.Quotes);
        Assert.Equal(0, report.Count);
        Assert.Equal(0m, report.TotalFinalPrice);
    }

    // ---------- BOUNDARY VALUE ----------

    [Fact]
    public void Boundary_ExactThresholdsAreIncluded_JustOutsideIsExcluded()
    {
        var included = new[]
        {
            Phone("AtMinPrice", 2022, 8, 150_000m),
            Phone("AtMaxPrice", 2022, 8, 700_000m),
        };
        var excluded = new[]
        {
            Phone("YearTooOld", 2021, 8, 300_000m),
            Phone("RamTooSmall", 2022, 7, 300_000m),
            Phone("PriceTooLow", 2022, 8, 149_999m),
            Phone("PriceTooHigh", 2022, 8, 700_001m),
        };

        var selected = PricingStages.SelectEligible(included.Concat(excluded), Criteria);

        Assert.Equal(new[] { "AtMinPrice", "AtMaxPrice" }, selected.Select(p => p.Model).ToArray());
    }

    [Fact]
    public void Boundary_ZeroDiscountIsLegitimate_FinalEqualsOriginal()
    {
        var phones = new[] { Phone("Brand new", 2026, 8, 300_000m) };

        var quote = Assert.Single(PricingStages.ApplyDiscounts(phones, DiscountPolicies.Standard(Year)));

        Assert.Equal(0m, quote.DiscountPercent);
        Assert.Equal(0m, quote.DiscountAmount);
        Assert.Equal(300_000m, quote.FinalPrice);
    }

    // ---------- INVALID AND MISSING INPUT ----------

    [Theory]
    [InlineData("Phone|2022|8|128|4000", "Expected 6 fields")]
    [InlineData("|2022|8|128|4000|300000", "Model must not be empty")]
    [InlineData("Phone|abc|8|128|4000|300000", "ReleaseYear 'abc' is not a whole number")]
    [InlineData("Phone|1850|8|128|4000|300000", "ReleaseYear must be between")]
    [InlineData("Phone|2022|0|128|4000|300000", "RamGb must be greater than 0")]
    [InlineData("Phone|2022|8|-128|4000|300000", "StorageGb must be greater than 0")]
    [InlineData("Phone|2022|8|128|4000|-5", "OriginalPrice must be greater than 0")]
    [InlineData("Phone|2022|8|128|4000|12.5.3", "OriginalPrice '12.5.3' is not a valid number")]
    [InlineData("", "Line is empty")]
    [InlineData("   ", "Line is empty")]
    public void InvalidLine_IsRejected_WithClearMessage(string line, string expectedFragment)
    {
        var ok = PhoneParser.TryParseLine(line, out var phone, out var error);

        Assert.False(ok);
        Assert.Null(phone);
        Assert.Contains(expectedFragment, error);
    }

    [Fact]
    public void MissingLine_IsRejected()
    {
        var ok = PhoneParser.TryParseLine(null, out var phone, out var error);

        Assert.False(ok);
        Assert.Null(phone);
        Assert.Contains("missing", error);
    }

    [Fact]
    public void ParseAll_CollectsErrorsWithLineNumbers_AndFabricatesNothing()
    {
        var lines = new[]
        {
            "Galaxy S22|2022|8|128|3700|350000",
            "iPhone 14|2022|six|128|3279|500000",
            "Pixel 7|2022|8|128|4355",
        };

        var parsed = PhoneParser.ParseAll(lines);

        Assert.False(parsed.IsValid);
        Assert.Single(parsed.Phones);
        Assert.Equal(new[] { 2, 3 }, parsed.Errors.Select(e => e.LineNumber).ToArray());
    }

    [Fact]
    public void MissingDataset_Throws_NotTreatedAsEmpty()
    {
        Assert.Throws<ArgumentNullException>(() => PhoneParser.ParseAll(null!));
        Assert.Throws<ArgumentNullException>(() => PricingStages.SelectEligible(null!, Criteria));
        Assert.Throws<ArgumentNullException>(() => PricingStages.ApplyDiscounts(Sample(), null!));
    }

    [Fact]
    public void PolicyReturningInvalidPercent_IsRejected()
    {
        Assert.Throws<InvalidOperationException>(() => PricingStages.ApplyDiscounts(Sample(), _ => 101m));
        Assert.Throws<InvalidOperationException>(() => PricingStages.ApplyDiscounts(Sample(), _ => -1m));
    }

    // ---------- TWO POLICIES ----------

    [Fact]
    public void TwoPolicies_SameInput_DifferInDocumentedWay()
    {
        var phones = Sample();

        var standard = PricingPipeline.Run(phones, Criteria, DiscountPolicies.Standard(Year));
        var clearance = PricingPipeline.Run(phones, Criteria, DiscountPolicies.Clearance(Year));

        Assert.Equal(
            new[] { 127_500m, 280_000m, 427_500m, 510_000m, 665_000m },
            standard.Quotes.Select(q => q.FinalPrice).ToArray());
        Assert.Equal(
            new[] { "Redmi Note 12", "Galaxy S22", "Xiaomi 14", "iPhone 15 Pro", "Galaxy S24 Ultra" },
            clearance.Quotes.Select(q => q.Phone.Model).ToArray());
        Assert.Equal(
            new[] { 114_000m, 238_000m, 378_000m, 456_000m, 588_000m },
            clearance.Quotes.Select(q => q.FinalPrice).ToArray());

        Assert.Equal(2_010_000m, standard.TotalFinalPrice);
        Assert.Equal(1_774_000m, clearance.TotalFinalPrice);
        Assert.NotEqual(standard.TotalFinalPrice, clearance.TotalFinalPrice);
    }

    [Fact]
    public void HigherOrderStage_InvokesTheDelegate_OncePerPhone()
    {
        var calls = 0;
        Func<Smartphone, decimal> counting = _ =>
        {
            calls++;
            return 10m;
        };

        var quotes = PricingStages.ApplyDiscounts(Sample(), counting);

        Assert.Equal(9, calls);
        Assert.Equal(9, quotes.Length);
    }

    // ---------- PURITY AND STAGES ----------

    [Fact]
    public void Stages_DoNotModifyCallerData_AndResultsAreIndependentCopies()
    {
        var input = new List<Smartphone>(Sample());
        var snapshot = input.ToArray();

        var eligible = PricingStages.SelectEligible(input, Criteria);
        var quotes = PricingStages.ApplyDiscounts(eligible, DiscountPolicies.Standard(Year));
        var report = PricingStages.BuildReport(quotes);

        Assert.True(snapshot.SequenceEqual(input));   // caller's list untouched

        input.Clear();                                 // caller changes their list later...
        Assert.Equal(5, eligible.Length);              // ...results are unaffected
        Assert.Equal(5, report.Count);
    }

    [Fact]
    public void SameInputAndPolicy_GiveSameOutput()
    {
        var phones = Sample();

        var first = PricingPipeline.Run(phones, Criteria, DiscountPolicies.Standard(Year));
        var second = PricingPipeline.Run(phones, Criteria, DiscountPolicies.Standard(Year));

        Assert.True(first.Quotes.SequenceEqual(second.Quotes));
    }

    [Fact]
    public void BuildReport_SortsByFinalPrice_TiesByModelName()
    {
        var a = new PhoneQuote(Phone("B", 2023, 8, 100_000m), 0m, 0m, 100_000m);
        var b = new PhoneQuote(Phone("A", 2023, 8, 100_000m), 0m, 0m, 100_000m);
        var c = new PhoneQuote(Phone("C", 2023, 8, 50_000m), 0m, 0m, 50_000m);

        var report = PricingStages.BuildReport(new[] { a, b, c });

        Assert.Equal(new[] { "C", "A", "B" }, report.Quotes.Select(q => q.Phone.Model).ToArray());
    }

    // ---------- TRADE-IN ----------

    [Fact]
    public void TradeIn_GalaxyS22ToS24Ultra_IsAccepted_WithCredit()
    {
        var phones = Sample();
        var oldPhone = phones.First(p => p.Model == "Galaxy S22");
        var newPhone = phones.First(p => p.Model == "Galaxy S24 Ultra");

        var ok = TradeInProgram.TryTradeIn(oldPhone, newPhone, Year, out var offer, out var rejection);

        Assert.True(ok);
        Assert.Equal(string.Empty, rejection);
        Assert.NotNull(offer);
        Assert.Equal(52_500m, offer.Credit);              // min(15% of 350000, 10% of 700000)
        Assert.Equal(647_500m, offer.PriceAfterCredit);
    }

    [Fact]
    public void TradeIn_IPhone14ToIPhone15Pro_IsRejected_WithAllFailingReasons()
    {
        var phones = Sample();
        var oldPhone = phones.First(p => p.Model == "iPhone 14");
        var newPhone = phones.First(p => p.Model == "iPhone 15 Pro");

        var ok = TradeInProgram.TryTradeIn(oldPhone, newPhone, Year, out var offer, out var rejection);

        Assert.False(ok);
        Assert.Null(offer);
        Assert.Contains("2 years newer", rejection);
        Assert.Contains("1.5x", rejection);
        Assert.DoesNotContain("Brand mismatch", rejection);
    }

    [Fact]
    public void TradeIn_DifferentBrand_IsRejected()
    {
        var phones = Sample();
        var oldPhone = phones.First(p => p.Model == "Pixel 6");
        var newPhone = phones.First(p => p.Model == "Xiaomi 14");

        var ok = TradeInProgram.TryTradeIn(oldPhone, newPhone, Year, out _, out var rejection);

        Assert.False(ok);
        Assert.Contains("Brand mismatch", rejection);
    }

    [Fact]
    public void TradeIn_OldPhoneYearBoundary_2023Accepted_2024Rejected()
    {
        var newPhone = new Smartphone("Galaxy New", 2026, 8, 128, 4000, 450_000m);

        var from2023 = new Smartphone("Galaxy Old", 2023, 8, 128, 4000, 300_000m);
        var from2024 = new Smartphone("Galaxy Old", 2024, 8, 128, 4000, 300_000m);

        var ok2023 = TradeInProgram.TryTradeIn(from2023, newPhone, Year, out var offer, out _);
        var ok2024 = TradeInProgram.TryTradeIn(from2024, newPhone, Year, out _, out var rejection);

        Assert.True(ok2023);                   // price 450000 == exactly 1.5 x 300000 is allowed
        Assert.NotNull(offer);
        Assert.Equal(45_000m, offer.Credit);
        Assert.False(ok2024);
        Assert.Contains("before 2024", rejection);
        Assert.DoesNotContain("years newer", rejection);
    }

    [Fact]
    public void TradeIn_TooVintageOldPhone_IsRejected()
    {
        var oldPhone = new Smartphone("Galaxy Antique", 2019, 4, 64, 3500, 100_000m);
        var newPhone = new Smartphone("Galaxy New", 2025, 8, 128, 4000, 300_000m);

        var ok = TradeInProgram.TryTradeIn(oldPhone, newPhone, Year, out _, out var rejection);

        Assert.False(ok);
        Assert.Contains("too vintage", rejection);
    }
}
