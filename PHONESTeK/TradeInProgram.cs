using System.Diagnostics.CodeAnalysis;

namespace PHONESTeK;

public sealed record TradeInOffer(
    Smartphone OldPhone,
    Smartphone NewPhone,
    decimal Credit,
    decimal PriceAfterCredit
);

/// <summary>
/// A deliberately inconvenient trade-in program (satire of real retail trade-ins).
/// Every rule must pass. When several fail, all reasons are reported.
/// Pure: the reference year is passed in, nothing is read or written.
/// </summary>
public static class TradeInProgram
{
    /// <summary>The old phone must be released strictly before this year.</summary>
    public const int OldPhoneMustBeBeforeYear = 2024;
    /// <summary>...but must not be older than this many years (we do not accept antiques).</summary>
    public const int MaxOldPhoneAgeYears = 6;
    /// <summary>The new phone must be at least this many years newer than the old one.</summary>
    public const int MinYearGap = 2;
    /// <summary>The new phone must cost at least this multiple of the old phone's original price.</summary>
    public const decimal MinPriceMultiplier = 1.5m;
    /// <summary>The old phone's battery must be at least this large ("acceptable condition").</summary>
    public const int MinOldBatteryMah = 3000;
    /// <summary>Credit = 15% of the old price, capped at 10% of the new price.</summary>
    public const decimal CreditRateOfOldPrice = 0.15m;
    public const decimal CreditCapRateOfNewPrice = 0.10m;

    public static bool TryTradeIn(
        Smartphone oldPhone,
        Smartphone newPhone,
        int referenceYear,
        [NotNullWhen(true)] out TradeInOffer? offer,
        out string rejection)
    {
        ArgumentNullException.ThrowIfNull(oldPhone);
        ArgumentNullException.ThrowIfNull(newPhone);

        var reasons = new List<string>();

        if (!string.Equals(Brand(oldPhone), Brand(newPhone), StringComparison.OrdinalIgnoreCase))
            reasons.Add($"Brand mismatch: a '{Brand(oldPhone)}' cannot be traded for a '{Brand(newPhone)}'.");

        if (oldPhone.ReleaseYear >= OldPhoneMustBeBeforeYear)
            reasons.Add($"Old phone must be released before {OldPhoneMustBeBeforeYear} (it is from {oldPhone.ReleaseYear}).");

        var oldAge = referenceYear - oldPhone.ReleaseYear;
        if (oldAge > MaxOldPhoneAgeYears)
            reasons.Add($"Old phone is too vintage: at most {MaxOldPhoneAgeYears} years old allowed (it is {oldAge}).");

        var gap = newPhone.ReleaseYear - oldPhone.ReleaseYear;
        if (gap < MinYearGap)
            reasons.Add($"New phone must be at least {MinYearGap} years newer than the old one (gap is {gap}).");

        if (newPhone.OriginalPrice < oldPhone.OriginalPrice * MinPriceMultiplier)
            reasons.Add($"New phone must cost at least {MinPriceMultiplier}x the old phone's original price.");

        if (oldPhone.BatteryMah < MinOldBatteryMah)
            reasons.Add($"Old phone battery must be at least {MinOldBatteryMah} mAh (it has {oldPhone.BatteryMah}).");

        if (reasons.Count > 0)
        {
            offer = null;
            rejection = string.Join(" ", reasons);
            return false;
        }

        var credit = Math.Min(
            Math.Round(oldPhone.OriginalPrice * CreditRateOfOldPrice, 0, MidpointRounding.AwayFromZero),
            Math.Round(newPhone.OriginalPrice * CreditCapRateOfNewPrice, 0, MidpointRounding.AwayFromZero));

        offer = new TradeInOffer(oldPhone, newPhone, credit, newPhone.OriginalPrice - credit);
        rejection = string.Empty;
        return true;
    }

    /// <summary>Brand = the first word of the model name ("iPhone 14" -> "iPhone").</summary>
    public static string Brand(Smartphone phone)
    {
        var model = phone.Model.Trim();
        var space = model.IndexOf(' ');
        return space < 0 ? model : model[..space];
    }
}
