using System.Collections.Immutable;
using System.Runtime.InteropServices;

namespace PHONESTeK;

/// <summary>Reusable operations whose receivers own the data being transformed.</summary>
public static class PhoneExtensions
{
    /// <summary>Returns the amount saved on a quote; the quote has both prices needed for this calculation.</summary>
    public static decimal Savings(this PhoneQuote quote)
    {
        ArgumentNullException.ThrowIfNull(quote);
        return quote.Phone.OriginalPrice - quote.FinalPrice;
    }

    /// <summary>Deterministic quote ordering: final price, then model name (ordinal).</summary>
    public static IOrderedEnumerable<PhoneQuote> OrderByFinalPriceThenModel(this IEnumerable<PhoneQuote> quotes)
    {
        ArgumentNullException.ThrowIfNull(quotes);
        return quotes.OrderBy(q => q.FinalPrice).ThenBy(q => q.Phone.Model, StringComparer.Ordinal);
    }
}

public sealed record EligiblePhoneRow(string Model, int ReleaseYear, int RamGb, decimal OriginalPrice);
public sealed record BrandValue(string Brand, int PhoneCount, decimal TotalOriginalValue, decimal AverageOriginalPrice);
public sealed record PhoneCatalogReport(ImmutableArray<EligiblePhoneRow> EligiblePhones, ImmutableArray<BrandValue> BrandValues);
public sealed record PhoneScoreConfiguration(int RamWeight, int StorageWeight, int BatteryWeight, int BaseScore);

public static class PhoneReports
{
    // Report 1: shopper/staff; eligible catalog, projected fields, cheapest first, stable model tie-breaker.
    public static ImmutableArray<EligiblePhoneRow> EligibleCatalog(IEnumerable<Smartphone> phones, EligibilityCriteria criteria) =>
        phones.Where(p => p.ReleaseYear >= criteria.MinReleaseYear && p.RamGb >= criteria.MinRamGb &&
                          p.OriginalPrice >= criteria.MinPrice && p.OriginalPrice <= criteria.MaxPrice)
              .Select(p => new EligiblePhoneRow(p.Model, p.ReleaseYear, p.RamGb, p.OriginalPrice))
              .OrderBy(p => p.OriginalPrice)
              .ThenBy(p => p.Model, StringComparer.Ordinal)
              .ToImmutableArray();

    // Report 2: shop manager; group by the first model token as a simple manufacturer label.
    public static ImmutableArray<BrandValue> ValueByBrand(IEnumerable<Smartphone> phones) =>
        phones.GroupBy(p => p.Model.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0], StringComparer.Ordinal)
              .Select(g => new BrandValue(g.Key, g.Count(), g.Sum(p => p.OriginalPrice), g.Average(p => p.OriginalPrice)))
              .OrderByDescending(g => g.TotalOriginalValue)
              .ThenBy(g => g.Brand, StringComparer.Ordinal)
              .ToImmutableArray();

    public static PhoneCatalogReport Create(IEnumerable<Smartphone> phones, EligibilityCriteria criteria) =>
        new(EligibleCatalog(phones, criteria), ValueByBrand(phones));
}

public static class PhoneScoring
{
    /// <summary>Currying: Configuration -> (Smartphone -> integer score).</summary>
    public static Func<PhoneScoreConfiguration, Func<Smartphone, int>> CreateScore =>
        config => phone => checked(config.BaseScore + phone.RamGb * config.RamWeight +
                                   phone.StorageGb * config.StorageWeight + phone.BatteryMah * config.BatteryWeight);

    public static Func<Smartphone, int> Balanced { get; } = CreateScore(new(10, 1, 1, 0));
    public static Func<Smartphone, int> BatteryFocused { get; } = CreateScore(new(5, 1, 2, 0));
}

public static class PhoneStreams
{
    /// <summary>Deferred project workflow. The body runs only as the caller enumerates it.</summary>
    public static IEnumerable<Smartphone> EligibleDeferred(IEnumerable<Smartphone> phones, EligibilityCriteria criteria)
    {
        foreach (var phone in phones)
            if (phone.ReleaseYear >= criteria.MinReleaseYear && phone.RamGb >= criteria.MinRamGb &&
                phone.OriginalPrice >= criteria.MinPrice && phone.OriginalPrice <= criteria.MaxPrice)
                yield return phone;
    }
}

/// <summary>Native boundary: validates before P/Invoke and converts loader/binding errors to Result.</summary>
public static class NativePhoneScorer
{
    public const string LibraryName = "phonestek_native";
    public const string NativeFailureCode = "native_unavailable";
    public const string InvalidInputCode = "native_input_invalid";

    [DllImport(LibraryName, EntryPoint = "phonestek_phone_score", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
    private static extern int NativeScore(int ramGb, int storageGb, int batteryMah);

    public static Result<int> Score(Smartphone phone) => Score(phone, NativeScore);

    // Injectable boundary lets the failure path be tested without pretending a mock is the real native call.
    public static Result<int> Score(Smartphone phone, Func<int, int, int, int> nativeCall)
    {
        ArgumentNullException.ThrowIfNull(phone);
        ArgumentNullException.ThrowIfNull(nativeCall);
        if (phone.RamGb is < 1 or > 1024 || phone.StorageGb is < 1 or > 1_000_000 || phone.BatteryMah is < 1 or > 100_000)
            return Result.Fail<int>(AppError.Domain(InvalidInputCode, "RAM, storage, or battery is outside the native function's supported range."));
        try
        {
            return Result.Ok(nativeCall(phone.RamGb, phone.StorageGb, phone.BatteryMah));
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            return Result.Fail<int>(AppError.Operational(NativeFailureCode, $"Native phone scoring is unavailable: {ex.Message}"));
        }
    }
}
