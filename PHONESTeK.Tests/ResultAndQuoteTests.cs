using Xunit;

namespace PHONESTeK.Tests;

/// <summary>Assignment 7: Result, Maybe, Map, Bind and the quote chain (no files involved).</summary>
public class ResultAndQuoteTests
{
    private const int Year = 2026;
    private static readonly EligibilityCriteria Criteria = new(2022, 8, 150_000m, 700_000m);
    private static readonly Func<Smartphone, decimal> Policy = DiscountPolicies.Standard(Year);

    private static Result<QuoteView> Quote(
        string model,
        int quantity,
        Maybe<decimal>? budget = null,
        Func<Smartphone, decimal>? policy = null) =>
        QuoteService.Quote(
            TestData.Catalog(),
            new QuoteRequest(model, quantity, budget ?? Maybe.None<decimal>()),
            Criteria,
            policy ?? Policy);

    // ---------- RESULT MODEL ----------

    [Fact]
    public void Result_Ok_CarriesItsValue()
    {
        Assert.Equal(42, ExpectResult.Ok(Result.Ok(42)));
    }

    [Fact]
    public void Result_Fail_CarriesItsError_AndIsNotASuccess()
    {
        var error = AppError.Domain("some_code", "some message");

        var result = Result.Fail<int>(error);

        Assert.Same(error, ExpectResult.Fail(result));
        Assert.IsNotType<Result<int>.Ok>(result);
    }

    [Fact]
    public void Result_CannotBeBuiltWithMissingParts()
    {
        Assert.Throws<ArgumentNullException>(() => Result.Ok<string>(null!));
        Assert.Throws<ArgumentNullException>(() => Result.Fail<int>(null!));
    }

    // ---------- MAP ----------

    [Fact]
    public void Map_Success_PassesTheValueToTheFunction_AndWrapsTheNewValue()
    {
        int? received = null;

        var mapped = Result.Ok(21).Map(v =>
        {
            received = v;
            return v * 2;
        });

        Assert.Equal(21, received);
        Assert.Equal(42, ExpectResult.Ok(mapped));
    }

    [Fact]
    public void Map_Failure_IsPropagated_AndTheFunctionIsNotCalled()
    {
        var calls = 0;
        var error = AppError.Domain("bad", "bad input");

        var mapped = Result.Fail<int>(error).Map(v =>
        {
            calls++;
            return v * 2;
        });

        Assert.Equal(0, calls);
        Assert.Same(error, ExpectResult.Fail(mapped));
    }

    // ---------- BIND ----------

    [Fact]
    public void Bind_Success_PassesTheValue_AndKeepsTheInnerResult()
    {
        int? received = null;

        var ok = Result.Ok(5).Bind(v =>
        {
            received = v;
            return Result.Ok($"value {v}");
        });

        Assert.Equal(5, received);
        Assert.Equal("value 5", ExpectResult.Ok(ok));

        // the next step may itself fail: that failure is kept as it is
        var innerError = AppError.Domain("inner", "inner step failed");
        var failed = Result.Ok(5).Bind(_ => Result.Fail<string>(innerError));
        Assert.Same(innerError, ExpectResult.Fail(failed));
    }

    [Fact]
    public void Bind_Failure_IsPropagated_AndTheFunctionIsNotCalled()
    {
        var calls = 0;
        var error = AppError.Domain("bad", "bad input");

        var bound = Result.Fail<int>(error).Bind(v =>
        {
            calls++;
            return Result.Ok(v);
        });

        Assert.Equal(0, calls);
        Assert.Same(error, ExpectResult.Fail(bound));
    }

    [Fact]
    public void Chain_StopsAtTheFirstFailure_AndSkipsEveryLaterStep()
    {
        var calls = new List<string>();
        var error = AppError.Domain("stop", "stopped at the second step");

        var result = Result.Ok(1)
            .Map(v => { calls.Add("map1"); return v + 1; })
            .Bind(_ => { calls.Add("bind2"); return Result.Fail<int>(error); })
            .Map(v => { calls.Add("map3"); return v + 1; })
            .Bind(v => { calls.Add("bind4"); return Result.Ok(v); });

        Assert.Equal(new[] { "map1", "bind2" }, calls.ToArray());
        Assert.Same(error, ExpectResult.Fail(result));
    }

    // ---------- MAYBE (MISSING VALUES) ----------

    [Fact]
    public void Maybe_PrimitiveValuesThatLookEmpty_ArePresent_NotAbsent()
    {
        Assert.Equal(0, Assert.IsType<Maybe<int>.Some>(Maybe.Some(0)).Value);
        Assert.Equal(0m, Assert.IsType<Maybe<decimal>.Some>(Maybe.Some(0m)).Value);
        Assert.False(Assert.IsType<Maybe<bool>.Some>(Maybe.Some(false)).Value);
        Assert.Equal(string.Empty, Assert.IsType<Maybe<string>.Some>(Maybe.Some(string.Empty)).Value);

        Assert.IsType<Maybe<int>.None>(Maybe.None<int>());
    }

    [Fact]
    public void Maybe_Some_RejectsNull()
    {
        Assert.Throws<ArgumentNullException>(() => Maybe.Some<string>(null!));
    }

    [Fact]
    public void Maybe_ToResult_PresentBecomesOk_AbsentBecomesTheGivenFailure()
    {
        var error = AppError.Domain("missing_thing", "the thing is missing");

        Assert.Equal(0, ExpectResult.Ok(Maybe.Some(0).ToResult(error)));
        Assert.Same(error, ExpectResult.Fail(Maybe.None<int>().ToResult(error)));
    }

    // ---------- LOOKUP: PRESENT AND ABSENT ----------

    [Fact]
    public void PhoneCatalog_Find_HandlesPresentAndAbsent()
    {
        var catalog = TestData.Catalog();

        var found = Assert.IsType<Maybe<Smartphone>.Some>(PhoneCatalog.Find(catalog, "Galaxy S22"));
        Assert.Equal(350_000m, found.Value.OriginalPrice);

        var loose = Assert.IsType<Maybe<Smartphone>.Some>(PhoneCatalog.Find(catalog, "  galaxy s22 "));
        Assert.Equal("Galaxy S22", loose.Value.Model);

        Assert.IsType<Maybe<Smartphone>.None>(PhoneCatalog.Find(catalog, "Galaxy S99"));
        Assert.IsType<Maybe<Smartphone>.None>(PhoneCatalog.Find(catalog, ""));
        Assert.IsType<Maybe<Smartphone>.None>(PhoneCatalog.Find(catalog, null));
        Assert.IsType<Maybe<Smartphone>.None>(PhoneCatalog.Find(Array.Empty<Smartphone>(), "Galaxy S22"));
    }

    // ---------- QUOTE CHAIN: COMPLETE SUCCESS ----------

    [Fact]
    public void Quote_CompleteSuccess_ProducesTheDisplayModel()
    {
        var view = ExpectResult.Ok(Quote("Galaxy S22", 2));

        Assert.Equal(new QuoteView("Galaxy S22", 2, "20%", "280,000", "560,000"), view);
    }

    [Theory]
    [InlineData(1, "280,000")]
    [InlineData(5, "1,400,000")]
    public void Quote_QuantityBoundaries_OneAndFiveAreAccepted(int quantity, string expectedTotal)
    {
        var view = ExpectResult.Ok(Quote("Galaxy S22", quantity));

        Assert.Equal(expectedTotal, view.Total);
    }

    // ---------- QUOTE CHAIN: DOMAIN FAILURES ----------

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(6)]
    public void Quote_InvalidQuantity_FailsWithTheRuleAndTheValue(int quantity)
    {
        var error = ExpectResult.Fail(Quote("Galaxy S22", quantity));

        Assert.Equal(ErrorCodes.InvalidQuantity, error.Code);
        Assert.Equal(ErrorKind.Domain, error.Kind);
        Assert.Contains($"got {quantity}", error.Message);
    }

    [Theory]
    [InlineData("Nokia Budget", "price")]
    [InlineData("Pixel 6", "year >= 2022")]
    [InlineData("iPhone 14", "RAM >= 8 GB")]
    public void Quote_NotEligible_FailsAndNamesThePhoneAndTheRule(string model, string expectedRuleText)
    {
        var error = ExpectResult.Fail(Quote(model, 1));

        Assert.Equal(ErrorCodes.NotEligible, error.Code);
        Assert.Equal(ErrorKind.Domain, error.Kind);
        Assert.Contains(model, error.Message);
        Assert.Contains(expectedRuleText, error.Message);
    }

    [Fact]
    public void Quote_OverBudget_FailsAndReportsTotalAndBudget()
    {
        var error = ExpectResult.Fail(Quote("Galaxy S22", 2, Maybe.Some(500_000m)));

        Assert.Equal(ErrorCodes.OverBudget, error.Code);
        Assert.Equal(ErrorKind.Domain, error.Kind);
        Assert.Contains("560,000", error.Message);
        Assert.Contains("500,000", error.Message);
    }

    [Fact]
    public void Quote_UnknownModel_FailsAsAMissingLookup()
    {
        var error = ExpectResult.Fail(Quote("Galaxy S99", 1));

        Assert.Equal(ErrorCodes.PhoneNotFound, error.Code);
        Assert.Equal(ErrorKind.Domain, error.Kind);
        Assert.Contains("Galaxy S99", error.Message);
    }

    // ---------- QUOTE CHAIN: OPTIONAL BUDGET AND BOUNDARY ----------

    [Fact]
    public void Quote_BudgetAbsent_MeansNoLimit()
    {
        var view = ExpectResult.Ok(Quote("Galaxy S22", 5, Maybe.None<decimal>()));

        Assert.Equal("1,400,000", view.Total);
    }

    [Fact]
    public void Quote_BudgetEqualToTheTotal_IsAccepted_OneLessIsRejected()
    {
        Assert.Equal("560,000", ExpectResult.Ok(Quote("Galaxy S22", 2, Maybe.Some(560_000m))).Total);

        var error = ExpectResult.Fail(Quote("Galaxy S22", 2, Maybe.Some(559_999m)));
        Assert.Equal(ErrorCodes.OverBudget, error.Code);
    }

    [Fact]
    public void Quote_BudgetZero_IsARealLimit_NotAbsence()
    {
        var withZero = ExpectResult.Fail(Quote("Galaxy S22", 1, Maybe.Some(0m)));
        var withNone = ExpectResult.Ok(Quote("Galaxy S22", 1, Maybe.None<decimal>()));

        Assert.Equal(ErrorCodes.OverBudget, withZero.Code);
        Assert.Equal("280,000", withNone.Total);
    }

    // ---------- FAILURE PROPAGATION IN THE REAL CHAIN ----------

    [Theory]
    [InlineData("Galaxy S99", 1)]   // lookup fails: validation and pricing must not run
    [InlineData("Galaxy S22", 0)]   // validation fails: pricing must not run
    [InlineData("Nokia Budget", 1)] // validation fails: pricing must not run
    public void Quote_AfterAFailure_ThePricingStepIsNeverCalled(string model, int quantity)
    {
        var policyCalls = 0;
        Func<Smartphone, decimal> countingPolicy = _ =>
        {
            policyCalls++;
            return 10m;
        };

        ExpectResult.Fail(Quote(model, quantity, null, countingPolicy));

        Assert.Equal(0, policyCalls);
    }

    [Fact]
    public void Quote_OnSuccess_ThePricingStepIsCalledExactlyOnce()
    {
        var policyCalls = 0;
        Func<Smartphone, decimal> countingPolicy = _ =>
        {
            policyCalls++;
            return 10m;
        };

        var view = ExpectResult.Ok(Quote("Galaxy S22", 2, null, countingPolicy));

        Assert.Equal(1, policyCalls);
        Assert.Equal("10%", view.Discount);
        Assert.Equal("630,000", view.Total);   // 350000 - 10% = 315000 each, x 2
    }
}
