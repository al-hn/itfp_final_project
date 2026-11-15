namespace PHONESTeK;

/// <summary>The visible application workflow: the three stages composed with ordinary calls.</summary>
public static class PricingPipeline
{
    public static QuoteReport Run(
        IEnumerable<Smartphone> phones,
        EligibilityCriteria criteria,
        Func<Smartphone, decimal> discountPolicy)
    {
        var eligible = PricingStages.SelectEligible(phones, criteria);   // Stage 1
        var quotes = PricingStages.ApplyDiscounts(eligible, discountPolicy); // Stage 2
        return PricingStages.BuildReport(quotes);                        // Stage 3
    }
}
