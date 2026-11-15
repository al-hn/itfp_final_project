using System.Collections.Immutable;
using Xunit;

namespace PHONESTeK.Tests;

/// <summary>Assignment 6: validated factory, explicit states, pattern-matched transitions, safe operations.</summary>
public class PurchaseWorkflowTests
{
    private const int Year = 2026;
    private static readonly EligibilityCriteria Criteria = new(2022, 8, 150_000m, 700_000m);
    private static readonly Func<Smartphone, decimal> Policy = DiscountPolicies.Standard(Year);

    // Same 9 rows as Data/phones.txt
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

    private static ImmutableArray<Smartphone> Catalog()
    {
        var parsed = PhoneParser.ParseAll(SampleLines);
        Assert.True(parsed.IsValid);
        return parsed.Phones;
    }

    private static PurchaseValidation Validate(string customer, string model, string? tradeIn = null) =>
        ValidatedPurchase.Create(new PurchaseRequest(customer, model, tradeIn), Catalog(), Criteria, Policy, Year);

    private static (OrderBook Book, TransitionResult Result) Place(OrderBook book, string customer, string model, string? tradeIn = null) =>
        OrderService.Place(book, new PurchaseRequest(customer, model, tradeIn), Catalog(), Criteria, Policy, Year);

    // ---------- FACTORY SUCCESS ----------

    [Fact]
    public void Factory_ValidRequest_CreatesUsableValidatedPurchase()
    {
        var valid = Assert.IsType<PurchaseValidation.Valid>(Validate("Aigerim", "Galaxy S22"));
        var purchase = valid.Purchase;

        Assert.Equal("Aigerim", purchase.Customer);
        Assert.Equal("Galaxy S22", purchase.Phone.Model);
        Assert.Null(purchase.TradeInPhone);
        Assert.Equal(20m, purchase.DiscountPercent);
        Assert.Equal(70_000m, purchase.DiscountAmount);
        Assert.Equal(0m, purchase.TradeInCredit);
        Assert.Equal(280_000m, purchase.Total);
    }

    [Fact]
    public void Factory_ValidRequestWithTradeIn_AppliesDiscountThenCredit()
    {
        var valid = Assert.IsType<PurchaseValidation.Valid>(Validate("Daniyar", "Galaxy S24 Ultra", "Galaxy S22"));
        var purchase = valid.Purchase;

        Assert.Equal("Galaxy S22", purchase.TradeInPhone!.Model);
        Assert.Equal(35_000m, purchase.DiscountAmount);   // premium, 2 years old: 5%
        Assert.Equal(52_500m, purchase.TradeInCredit);    // min(15% of 350000, 10% of 700000)
        Assert.Equal(612_500m, purchase.Total);           // 700000 - 35000 - 52500
    }

    [Fact]
    public void Factory_TrimsCustomerName_AndMatchesModelIgnoringCase()
    {
        var valid = Assert.IsType<PurchaseValidation.Valid>(Validate("  Aigerim  ", " galaxy s22 "));

        Assert.Equal("Aigerim", valid.Purchase.Customer);
        Assert.Equal("Galaxy S22", valid.Purchase.Phone.Model);
    }

    // ---------- FACTORY REJECTION ----------

    [Theory]
    [InlineData("", "Galaxy S22", null, "Customer name must not be empty")]
    [InlineData("   ", "Galaxy S22", null, "Customer name must not be empty")]
    [InlineData("Aigerim", "Galaxy S99", null, "is not in the catalog")]
    [InlineData("Aigerim", "", null, "is not in the catalog")]
    [InlineData("Aigerim", "Pixel 6", null, "released in 2021, earliest allowed is 2022")]
    [InlineData("Aigerim", "iPhone 14", null, "6 GB RAM, at least 8 GB required")]
    [InlineData("Aigerim", "Nokia Budget", null, "is outside 150000..700000")]
    [InlineData("Aigerim", "Galaxy Z Fold 6", null, "is outside 150000..700000")]
    [InlineData("Aigerim", "Galaxy S22", "Galaxy S99", "Trade-in model 'Galaxy S99' is not in the catalog")]
    [InlineData("Timur", "iPhone 15 Pro", "iPhone 14", "Trade-in rejected")]
    [InlineData("Aigerim", "Xiaomi 14", "Pixel 6", "Brand mismatch")]
    public void Factory_InvalidRequest_IsRejected_WithTheReason(string customer, string model, string? tradeIn, string expectedFragment)
    {
        var invalid = Assert.IsType<PurchaseValidation.Invalid>(Validate(customer, model, tradeIn));

        Assert.True(
            invalid.Reasons.Any(reason => reason.Contains(expectedFragment)),
            $"Expected a reason containing '{expectedFragment}' but got: {string.Join(" | ", invalid.Reasons)}");
    }

    [Fact]
    public void Factory_ReportsAllReasonsAtOnce()
    {
        var invalid = Assert.IsType<PurchaseValidation.Invalid>(Validate("", "Galaxy S99"));

        Assert.Equal(2, invalid.Reasons.Length);
    }

    [Fact]
    public void Factory_MissingInput_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            ValidatedPurchase.Create(null!, Catalog(), Criteria, Policy, Year));
        Assert.Throws<ArgumentNullException>(() =>
            ValidatedPurchase.Create(new PurchaseRequest("A", "Galaxy S22", null), null!, Criteria, Policy, Year));
    }

    // ---------- BOUNDARY VALUE ----------

    [Fact]
    public void Boundary_PriceExactlyAtLimits_IsAccepted_OneUnitOutsideIsRejected()
    {
        var catalog = new[]
        {
            new Smartphone("Edge Max", 2022, 8, 128, 4000, 700_000m),
            new Smartphone("Edge Min", 2022, 8, 128, 4000, 150_000m),
            new Smartphone("Edge Over", 2022, 8, 128, 4000, 700_001m),
            new Smartphone("Edge Under", 2022, 8, 128, 4000, 149_999m),
        };

        PurchaseValidation Check(string model) =>
            ValidatedPurchase.Create(new PurchaseRequest("Aigerim", model, null), catalog, Criteria, Policy, Year);

        Assert.IsType<PurchaseValidation.Valid>(Check("Edge Max"));
        Assert.IsType<PurchaseValidation.Valid>(Check("Edge Min"));
        Assert.IsType<PurchaseValidation.Invalid>(Check("Edge Over"));
        Assert.IsType<PurchaseValidation.Invalid>(Check("Edge Under"));
    }

    [Fact]
    public void Boundary_RamAndYearExactlyAtLimits_AreAccepted()
    {
        var catalog = new[] { new Smartphone("Edge Spec", 2022, 8, 128, 4000, 300_000m) };

        var validation = ValidatedPurchase.Create(
            new PurchaseRequest("Aigerim", "Edge Spec", null), catalog, Criteria, Policy, Year);

        Assert.IsType<PurchaseValidation.Valid>(validation);
    }

    // ---------- DISTINCT CASES (pattern matching results) ----------

    [Fact]
    public void ValidationCases_ValidAndInvalid_ProduceDifferentWorkflowOutcomes()
    {
        var (bookAfterValid, validResult) = Place(OrderBook.Empty, "Aigerim", "Galaxy S22");
        var (bookAfterInvalid, invalidResult) = Place(OrderBook.Empty, "Madina", "Nokia Budget");

        // Valid case -> Moved(Quoted) and the order is stored
        var moved = Assert.IsType<TransitionResult.Moved>(validResult);
        Assert.IsType<OrderState.Quoted>(moved.Next);
        Assert.Single(bookAfterValid.Orders);

        // Invalid case -> Rejected with the reasons, and nothing is stored
        var rejected = Assert.IsType<TransitionResult.Rejected>(invalidResult);
        Assert.Contains("does not qualify", rejected.Reason);
        Assert.Empty(bookAfterInvalid.Orders);
    }

    [Fact]
    public void OrderStates_EveryCase_IsHandledExplicitlyByEveryTransition()
    {
        var purchase = Assert.IsType<PurchaseValidation.Valid>(Validate("Aigerim", "Galaxy S22")).Purchase;
        var quoted = OrderTransitions.Quote("ORD-0001", purchase);
        var confirmed = Assert.IsType<OrderState.Confirmed>(
            Assert.IsType<TransitionResult.Moved>(OrderTransitions.Confirm(quoted, 1001)).Next);
        var collected = Assert.IsType<OrderState.Collected>(
            Assert.IsType<TransitionResult.Moved>(OrderTransitions.Collect(confirmed)).Next);

        // Quoted carries no confirmation number; Confirmed and Collected carry it
        Assert.Equal(1001, confirmed.ConfirmationNumber);
        Assert.Equal(1001, collected.ConfirmationNumber);

        // Confirm: Quoted moves, Confirmed returns itself, Collected is rejected
        var confirmQuoted = Assert.IsType<TransitionResult.Moved>(OrderTransitions.Confirm(quoted, 1001));
        Assert.IsType<OrderState.Confirmed>(confirmQuoted.Next);
        var confirmConfirmed = Assert.IsType<TransitionResult.Moved>(OrderTransitions.Confirm(confirmed, 2002));
        Assert.Same(confirmed, confirmConfirmed.Next);
        Assert.IsType<TransitionResult.Rejected>(OrderTransitions.Confirm(collected, 3003));

        // Collect: Confirmed moves, Quoted and Collected are rejected
        var collectConfirmed = Assert.IsType<TransitionResult.Moved>(OrderTransitions.Collect(confirmed));
        Assert.IsType<OrderState.Collected>(collectConfirmed.Next);
        var collectQuoted = Assert.IsType<TransitionResult.Rejected>(OrderTransitions.Collect(quoted));
        Assert.Contains("not been confirmed", collectQuoted.Reason);
        Assert.IsType<TransitionResult.Rejected>(OrderTransitions.Collect(collected));
    }

    [Fact]
    public void UnknownState_IsRejected_NeverTreatedAsSuccess()
    {
        Assert.IsType<TransitionResult.Rejected>(OrderTransitions.Confirm(null!, 1001));
        Assert.IsType<TransitionResult.Rejected>(OrderTransitions.Collect(null!));
    }

    // ---------- NORMAL OPERATION ----------

    [Fact]
    public void NormalOperation_PlaceConfirmCollect_MovesThroughEveryState()
    {
        var (book1, placed) = Place(OrderBook.Empty, "Aigerim", "Galaxy S22");
        var quoted = Assert.IsType<OrderState.Quoted>(Assert.IsType<TransitionResult.Moved>(placed).Next);
        Assert.Equal("ORD-0001", quoted.OrderId);
        Assert.Equal(280_000m, quoted.Purchase.Total);

        var (book2, confirmedResult) = OrderService.Confirm(book1, "ORD-0001");
        var confirmed = Assert.IsType<OrderState.Confirmed>(Assert.IsType<TransitionResult.Moved>(confirmedResult).Next);
        Assert.Equal(1001, confirmed.ConfirmationNumber);
        Assert.Equal(1002, book2.NextConfirmationNumber);

        var (book3, collectedResult) = OrderService.Collect(book2, "ORD-0001");
        var collected = Assert.IsType<OrderState.Collected>(Assert.IsType<TransitionResult.Moved>(collectedResult).Next);
        Assert.Equal(1001, collected.ConfirmationNumber);
        Assert.IsType<OrderState.Collected>(book3.Find("ORD-0001"));
    }

    [Fact]
    public void NormalOperation_OrderNumbersAreSequential()
    {
        var (book1, _) = Place(OrderBook.Empty, "Aigerim", "Galaxy S22");
        var (book2, second) = Place(book1, "Daniyar", "Xiaomi 14");

        var quoted = Assert.IsType<OrderState.Quoted>(Assert.IsType<TransitionResult.Moved>(second).Next);
        Assert.Equal("ORD-0002", quoted.OrderId);
        Assert.Equal(2, book2.Orders.Count);
    }

    // ---------- MISSING PREREQUISITE / INVALID SEQUENCE ----------

    [Fact]
    public void MissingPrerequisite_CollectBeforeConfirm_IsRejected_AndBookIsUnchanged()
    {
        var (book, _) = Place(OrderBook.Empty, "Aigerim", "Galaxy S22");

        var (after, result) = OrderService.Collect(book, "ORD-0001");

        var rejected = Assert.IsType<TransitionResult.Rejected>(result);
        Assert.Contains("not been confirmed", rejected.Reason);
        Assert.Same(book, after);
        Assert.IsType<OrderState.Quoted>(after.Find("ORD-0001"));
    }

    [Fact]
    public void MissingPrerequisite_UnknownOrder_IsRejected_ForConfirmAndCollect()
    {
        var (afterConfirm, confirmResult) = OrderService.Confirm(OrderBook.Empty, "ORD-9999");
        var (afterCollect, collectResult) = OrderService.Collect(OrderBook.Empty, "ORD-9999");
        var (afterNull, nullResult) = OrderService.Confirm(OrderBook.Empty, null);

        Assert.Contains("does not exist", Assert.IsType<TransitionResult.Rejected>(confirmResult).Reason);
        Assert.Contains("does not exist", Assert.IsType<TransitionResult.Rejected>(collectResult).Reason);
        Assert.IsType<TransitionResult.Rejected>(nullResult);
        Assert.Same(OrderBook.Empty, afterConfirm);
        Assert.Same(OrderBook.Empty, afterCollect);
        Assert.Same(OrderBook.Empty, afterNull);
    }

    // ---------- REPEATED OPERATION ----------

    [Fact]
    public void RepeatedConfirm_ReturnsTheExistingConfirmation_WithoutUsingAnotherNumber()
    {
        var (book1, _) = Place(OrderBook.Empty, "Aigerim", "Galaxy S22");
        var (book2, first) = OrderService.Confirm(book1, "ORD-0001");

        var (book3, second) = OrderService.Confirm(book2, "ORD-0001");

        var firstConfirmed = Assert.IsType<TransitionResult.Moved>(first).Next;
        var secondConfirmed = Assert.IsType<TransitionResult.Moved>(second).Next;
        Assert.Same(firstConfirmed, secondConfirmed);   // the existing outcome
        Assert.Same(book2, book3);                      // nothing stored again
        Assert.Equal(1002, book3.NextConfirmationNumber);
    }

    [Fact]
    public void RepeatedCollect_IsRejected_AndBookIsUnchanged()
    {
        var (book1, _) = Place(OrderBook.Empty, "Aigerim", "Galaxy S22");
        var (book2, _) = OrderService.Confirm(book1, "ORD-0001");
        var (book3, _) = OrderService.Collect(book2, "ORD-0001");

        var (book4, result) = OrderService.Collect(book3, "ORD-0001");

        Assert.Contains("already collected", Assert.IsType<TransitionResult.Rejected>(result).Reason);
        Assert.Same(book3, book4);
    }

    [Fact]
    public void ConfirmAfterCollect_IsRejected_AndStaysCollected()
    {
        var (book1, _) = Place(OrderBook.Empty, "Aigerim", "Galaxy S22");
        var (book2, _) = OrderService.Confirm(book1, "ORD-0001");
        var (book3, _) = OrderService.Collect(book2, "ORD-0001");

        var (book4, result) = OrderService.Confirm(book3, "ORD-0001");

        Assert.IsType<TransitionResult.Rejected>(result);
        Assert.Same(book3, book4);
        Assert.IsType<OrderState.Collected>(book4.Find("ORD-0001"));
    }

    // ---------- STATE PRESERVATION ----------

    [Fact]
    public void FailedPlace_LeavesTheOriginalBookUnchanged_AndBurnsNoOrderNumber()
    {
        var (book, _) = Place(OrderBook.Empty, "Aigerim", "Galaxy S22");
        var ordersBefore = book.Orders;
        var numberBefore = book.NextOrderNumber;

        var (afterBad, result) = Place(book, "Madina", "Nokia Budget");

        Assert.IsType<TransitionResult.Rejected>(result);
        Assert.Same(book, afterBad);
        Assert.Same(ordersBefore, afterBad.Orders);
        Assert.Equal(numberBefore, afterBad.NextOrderNumber);

        // the next valid order still gets the next number, with no gap
        var (_, next) = Place(afterBad, "Daniyar", "Xiaomi 14");
        var quoted = Assert.IsType<OrderState.Quoted>(Assert.IsType<TransitionResult.Moved>(next).Next);
        Assert.Equal("ORD-0002", quoted.OrderId);
    }

    [Fact]
    public void SuccessfulOperations_DoNotModifyTheEarlierBook()
    {
        var (book1, _) = Place(OrderBook.Empty, "Aigerim", "Galaxy S22");

        var (book2, _) = OrderService.Confirm(book1, "ORD-0001");

        Assert.IsType<OrderState.Quoted>(book1.Find("ORD-0001"));      // the old book still says Quoted
        Assert.IsType<OrderState.Confirmed>(book2.Find("ORD-0001"));   // the new one says Confirmed
        Assert.Empty(OrderBook.Empty.Orders);
        Assert.Equal(1001, book1.NextConfirmationNumber);
    }

    [Fact]
    public void FailedPlace_DoesNotModifyTheCallersCatalog()
    {
        var catalog = new List<Smartphone>(Catalog());
        var snapshot = catalog.ToArray();

        OrderService.Place(
            OrderBook.Empty, new PurchaseRequest("Madina", "Nokia Budget", null), catalog, Criteria, Policy, Year);

        Assert.True(snapshot.SequenceEqual(catalog));
    }
}
