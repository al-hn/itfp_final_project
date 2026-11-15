using System.Globalization;

namespace PHONESTeK;

/// <summary>The one real lookup: find a phone in the catalog. Absence is a normal outcome, not an error or a null.</summary>
public static class PhoneCatalog
{
    /// <summary>Matches the trimmed model name, ignoring case. Blank or null names are simply not found.</summary>
    public static Maybe<Smartphone> Find(IEnumerable<Smartphone> catalog, string? model)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        if (string.IsNullOrWhiteSpace(model))
            return Maybe.None<Smartphone>();

        var wanted = model.Trim();
        foreach (var phone in catalog)
        {
            if (string.Equals(phone.Model, wanted, StringComparison.OrdinalIgnoreCase))
                return Maybe.Some(phone);
        }
        return Maybe.None<Smartphone>();
    }
}

/// <summary>
/// A request for a price quote. <c>Budget</c> is OPTIONAL: <c>None</c> means "no limit", while
/// <c>Some(0)</c> is a real budget of zero (nothing can be afforded), so zero is never mistaken for absence.
/// </summary>
public sealed record QuoteRequest(string Model, int Quantity, Maybe<decimal> Budget);

/// <summary>Request that passed validation: the phone exists and is eligible, the quantity is in range.</summary>
public sealed record ValidQuoteRequest(Smartphone Phone, int Quantity, Maybe<decimal> Budget);

/// <summary>Priced request, within the budget if one was given.</summary>
public sealed record PriceQuote(
    Smartphone Phone,
    int Quantity,
    decimal DiscountPercent,
    decimal UnitPrice,
    decimal Total);

/// <summary>Display model: what the customer sees. Built from a PriceQuote by a pure function.</summary>
public sealed record QuoteView(string Model, int Quantity, string Discount, string UnitPrice, string Total)
{
    public static QuoteView From(PriceQuote quote) => new(
        quote.Phone.Model,
        quote.Quantity,
        quote.DiscountPercent.ToString("0.##", CultureInfo.InvariantCulture) + "%",
        quote.UnitPrice.ToString("N0", CultureInfo.InvariantCulture),
        quote.Total.ToString("N0", CultureInfo.InvariantCulture));
}

/// <summary>
/// The quote chain. Each step either continues with a value or stops with a domain error; later steps
/// are not run after a failure. All functions here are pure (no I/O, no clock, no mutation).
///   Find (Maybe) -> ToResult -> Bind(validate) -> Bind(price) -> Map(display)
/// </summary>
public static class QuoteService
{
    public const int MaxQuantity = 5;

    public static Result<QuoteView> Quote(
        IReadOnlyCollection<Smartphone> catalog,
        QuoteRequest request,
        EligibilityCriteria criteria,
        Func<Smartphone, decimal> discountPolicy)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(criteria);
        ArgumentNullException.ThrowIfNull(discountPolicy);

        return PhoneCatalog.Find(catalog, request.Model)
            .ToResult(AppError.Domain(
                ErrorCodes.PhoneNotFound,
                $"Model '{request.Model}' is not in the catalog."))
            .Bind(phone => Validate(phone, request, criteria))
            .Bind(valid => Price(valid, discountPolicy))
            .Map(QuoteView.From);
    }

    /// <summary>Bind step 1: may reject (invalid quantity, phone not eligible).</summary>
    public static Result<ValidQuoteRequest> Validate(Smartphone phone, QuoteRequest request, EligibilityCriteria criteria)
    {
        if (request.Quantity < 1 || request.Quantity > MaxQuantity)
            return Result.Fail<ValidQuoteRequest>(AppError.Domain(
                ErrorCodes.InvalidQuantity,
                $"Quantity must be between 1 and {MaxQuantity} (got {request.Quantity})."));

        if (!PricingStages.IsEligible(phone, criteria))
            return Result.Fail<ValidQuoteRequest>(AppError.Domain(
                ErrorCodes.NotEligible,
                $"'{phone.Model}' does not meet the shop requirements " +
                $"(year >= {criteria.MinReleaseYear}, RAM >= {criteria.MinRamGb} GB, " +
                $"price {criteria.MinPrice}..{criteria.MaxPrice})."));

        return Result.Ok(new ValidQuoteRequest(phone, request.Quantity, request.Budget));
    }

    /// <summary>Bind step 2: prices with the existing discount stage, may reject (over budget).</summary>
    public static Result<PriceQuote> Price(ValidQuoteRequest valid, Func<Smartphone, decimal> discountPolicy)
    {
        var line = PricingStages.ApplyDiscounts(new[] { valid.Phone }, discountPolicy)[0];
        var quote = new PriceQuote(
            valid.Phone,
            valid.Quantity,
            line.DiscountPercent,
            line.FinalPrice,
            line.FinalPrice * valid.Quantity);

        return CheckBudget(quote, valid.Budget);
    }

    private static Result<PriceQuote> CheckBudget(PriceQuote quote, Maybe<decimal> budget) => budget switch
    {
        // optional value, absent: no limit applies
        Maybe<decimal>.None => Result.Ok(quote),
        // present (including zero): the total must not exceed it; exactly equal is allowed
        Maybe<decimal>.Some some when quote.Total > some.Value => Result.Fail<PriceQuote>(AppError.Domain(
            ErrorCodes.OverBudget,
            $"Total {quote.Total.ToString("N0", CultureInfo.InvariantCulture)} for {quote.Quantity} x '{quote.Phone.Model}' " +
            $"exceeds the budget of {some.Value.ToString("N0", CultureInfo.InvariantCulture)}.")),
        Maybe<decimal>.Some => Result.Ok(quote),
        _ => Result.Fail<PriceQuote>(AppError.Operational(ErrorCodes.UnknownCase, "Unknown budget case; nothing was priced.")),
    };
}
