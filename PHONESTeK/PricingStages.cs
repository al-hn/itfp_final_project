using System.Collections.Immutable;

namespace PHONESTeK;

/// <summary>
/// The three pure stages. None of them reads input/output, a clock, or global state, and none
/// modifies its arguments; each returns a new immutable value.
/// </summary>
public static class PricingStages
{
    /// <summary>
    /// Stage 1: IEnumerable&lt;Smartphone&gt; + EligibilityCriteria -> ImmutableArray&lt;Smartphone&gt;.
    /// Keeps phones with ReleaseYear &gt;= min, RamGb &gt;= min and MinPrice &lt;= price &lt;= MaxPrice.
    /// </summary>
    public static ImmutableArray<Smartphone> SelectEligible(
        IEnumerable<Smartphone> phones,
        EligibilityCriteria criteria)
    {
        ArgumentNullException.ThrowIfNull(phones);
        ArgumentNullException.ThrowIfNull(criteria);
        if (criteria.MinPrice > criteria.MaxPrice)
            throw new ArgumentException("MinPrice must not exceed MaxPrice.", nameof(criteria));

        return phones.Where(p => IsEligible(p, criteria)).ToImmutableArray();
    }

    public static bool IsEligible(Smartphone phone, EligibilityCriteria criteria) =>
        phone.ReleaseYear >= criteria.MinReleaseYear
        && phone.RamGb >= criteria.MinRamGb
        && phone.OriginalPrice >= criteria.MinPrice
        && phone.OriginalPrice <= criteria.MaxPrice;

    /// <summary>
    /// Stage 2 (higher-order): IEnumerable&lt;Smartphone&gt; + Func&lt;Smartphone, decimal&gt; -> ImmutableArray&lt;PhoneQuote&gt;.
    /// The delegate returns a discount percent (0..100) and is invoked once per phone.
    /// Discount amount is rounded to whole currency units, midpoint away from zero.
    /// </summary>
    public static ImmutableArray<PhoneQuote> ApplyDiscounts(
        IEnumerable<Smartphone> phones,
        Func<Smartphone, decimal> discountPolicy)
    {
        ArgumentNullException.ThrowIfNull(phones);
        ArgumentNullException.ThrowIfNull(discountPolicy);

        var quotes = ImmutableArray.CreateBuilder<PhoneQuote>();
        foreach (var phone in phones)
        {
            var percent = discountPolicy(phone); // delegate invocation
            if (percent < 0m || percent > 100m)
                throw new InvalidOperationException(
                    $"Discount policy returned {percent}% for '{phone.Model}'; it must be between 0 and 100.");

            var amount = Math.Round(phone.OriginalPrice * percent / 100m, 0, MidpointRounding.AwayFromZero);
            quotes.Add(new PhoneQuote(phone, percent, amount, phone.OriginalPrice - amount));
        }
        return quotes.ToImmutable();
    }

    /// <summary>
    /// Stage 3: IEnumerable&lt;PhoneQuote&gt; -> QuoteReport, ordered by final price ascending,
    /// ties broken by model name (ordinal) so the order is deterministic.
    /// </summary>
    public static QuoteReport BuildReport(IEnumerable<PhoneQuote> quotes)
    {
        ArgumentNullException.ThrowIfNull(quotes);

        var ordered = quotes
            .OrderBy(q => q.FinalPrice)
            .ThenBy(q => q.Phone.Model, StringComparer.Ordinal)
            .ToImmutableArray();
        return new QuoteReport(ordered);
    }
}
