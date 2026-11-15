using System.Collections.Immutable;

namespace PHONESTeK;

/// <summary>Raw, unchecked customer request. Safe to build freely: nothing downstream accepts it directly.</summary>
public sealed record PurchaseRequest(string Customer, string Model, string? TradeInModel);

/// <summary>
/// A purchase whose rules have all been checked. It can only be created by <see cref="Create"/>:
/// the constructor is private, the class is sealed (no subclass can skip the checks) and every
/// property is get-only (nothing can be changed after validation, and a class has no <c>with</c> copy).
/// All amounts are computed once, inside the factory, from the rules that were actually checked.
/// </summary>
public sealed class ValidatedPurchase
{
    public string Customer { get; }
    public Smartphone Phone { get; }
    public Smartphone? TradeInPhone { get; }
    public decimal DiscountPercent { get; }
    public decimal DiscountAmount { get; }
    public decimal TradeInCredit { get; }

    /// <summary>Amount to pay: price after discount, minus the trade-in credit.</summary>
    public decimal Total { get; }

    private ValidatedPurchase(
        string customer,
        Smartphone phone,
        Smartphone? tradeInPhone,
        decimal discountPercent,
        decimal discountAmount,
        decimal tradeInCredit,
        decimal total)
    {
        Customer = customer;
        Phone = phone;
        TradeInPhone = tradeInPhone;
        DiscountPercent = discountPercent;
        DiscountAmount = discountAmount;
        TradeInCredit = tradeInCredit;
        Total = total;
    }

    /// <summary>
    /// The only way to obtain a <see cref="ValidatedPurchase"/>. Checks, all reported together:
    /// 1. the customer name is not blank,
    /// 2. the model exists in the catalog,
    /// 3. the model meets the shop's eligibility criteria (year, RAM, price range),
    /// 4. if a trade-in phone is given, it exists and <see cref="TradeInProgram.TryTradeIn"/> accepts it,
    /// 5. the discount and credit leave something to pay.
    /// Returns Valid with the finished value, or Invalid with every reason. Pure: no I/O, no clock.
    /// </summary>
    public static PurchaseValidation Create(
        PurchaseRequest request,
        IReadOnlyCollection<Smartphone> catalog,
        EligibilityCriteria criteria,
        Func<Smartphone, decimal> discountPolicy,
        int referenceYear)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(criteria);
        ArgumentNullException.ThrowIfNull(discountPolicy);

        var reasons = new List<string>();

        var customer = request.Customer?.Trim() ?? string.Empty;
        if (customer.Length == 0)
            reasons.Add("Customer name must not be empty.");

        var phone = FindModel(catalog, request.Model);
        if (phone is null)
        {
            reasons.Add($"Model '{request.Model}' is not in the catalog.");
        }
        else
        {
            var problems = EligibilityProblems(phone, criteria);
            if (problems.Count > 0)
                reasons.Add($"'{phone.Model}' does not qualify: {string.Join("; ", problems)}.");
        }

        Smartphone? tradeIn = null;
        TradeInOffer? offer = null;
        if (!string.IsNullOrWhiteSpace(request.TradeInModel))
        {
            tradeIn = FindModel(catalog, request.TradeInModel);
            if (tradeIn is null)
                reasons.Add($"Trade-in model '{request.TradeInModel}' is not in the catalog.");
            else if (phone is not null
                     && !TradeInProgram.TryTradeIn(tradeIn, phone, referenceYear, out offer, out var rejection))
                reasons.Add($"Trade-in rejected: {rejection}");
        }

        if (reasons.Count > 0 || phone is null)
            return new PurchaseValidation.Invalid(reasons.ToImmutableArray());

        var quote = PricingStages.ApplyDiscounts(new[] { phone }, discountPolicy)[0];
        var credit = offer?.Credit ?? 0m;
        var total = quote.FinalPrice - credit;
        if (total <= 0m)
            return new PurchaseValidation.Invalid(
                ImmutableArray.Create("The discount and trade-in credit leave nothing to pay."));

        return new PurchaseValidation.Valid(
            new ValidatedPurchase(customer, phone, tradeIn, quote.DiscountPercent, quote.DiscountAmount, credit, total));
    }

    // One lookup rule for the whole project: PhoneCatalog.Find (Maybe). Here the Maybe is turned into
    // a nullable local only because this factory collects reasons instead of stopping at the first failure.
    private static Smartphone? FindModel(IEnumerable<Smartphone> catalog, string? model) =>
        PhoneCatalog.Find(catalog, model) is Maybe<Smartphone>.Some some ? some.Value : null;

    private static List<string> EligibilityProblems(Smartphone phone, EligibilityCriteria criteria)
    {
        var problems = new List<string>();

        if (phone.ReleaseYear < criteria.MinReleaseYear)
            problems.Add($"released in {phone.ReleaseYear}, earliest allowed is {criteria.MinReleaseYear}");
        if (phone.RamGb < criteria.MinRamGb)
            problems.Add($"{phone.RamGb} GB RAM, at least {criteria.MinRamGb} GB required");
        if (phone.OriginalPrice < criteria.MinPrice || phone.OriginalPrice > criteria.MaxPrice)
            problems.Add($"price {phone.OriginalPrice} is outside {criteria.MinPrice}..{criteria.MaxPrice}");

        return problems;
    }
}

/// <summary>
/// Outcome of validating a request. Exactly two cases, each carrying only its own data:
/// <c>Valid</c> carries the <see cref="ValidatedPurchase"/>, <c>Invalid</c> carries the reasons.
/// The private base constructor closes the hierarchy: no other case can be declared outside this type.
/// </summary>
public abstract record PurchaseValidation
{
    private PurchaseValidation() { }

    public sealed record Valid(ValidatedPurchase Purchase) : PurchaseValidation;

    public sealed record Invalid(ImmutableArray<string> Reasons) : PurchaseValidation;
}
