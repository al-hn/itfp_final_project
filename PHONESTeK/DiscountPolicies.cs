namespace PHONESTeK;

/// <summary>
/// Two interchangeable discount policies for the same rule ("how big is the discount for this phone?").
/// Each factory takes the reference year explicitly (no hidden clock) and returns a delegate that
/// captures only that immutable value.
/// </summary>
public static class DiscountPolicies
{
    /// <summary>Premium spec = at least 12 GB RAM and 256 GB storage.</summary>
    public static bool IsPremium(Smartphone phone) => phone.RamGb >= 12 && phone.StorageGb >= 256;

    /// <summary>
    /// Standard: 5% per year of age, capped at 30%. Premium phones get 5 points less (never below 0).
    /// </summary>
    public static Func<Smartphone, decimal> Standard(int referenceYear)
    {
        return phone =>
        {
            var age = Math.Max(0, referenceYear - phone.ReleaseYear);
            var percent = Math.Min(age * 5m, 30m);
            if (IsPremium(phone))
                percent = Math.Max(0m, percent - 5m);
            return percent;
        };
    }

    /// <summary>
    /// Clearance: 8% per year of age, capped at 50%. No premium adjustment.
    /// </summary>
    public static Func<Smartphone, decimal> Clearance(int referenceYear)
    {
        return phone =>
        {
            var age = Math.Max(0, referenceYear - phone.ReleaseYear);
            return Math.Min(age * 8m, 50m);
        };
    }
}
