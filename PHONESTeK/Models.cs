using System.Collections.Immutable;

namespace PHONESTeK;

/// <summary>A validated smartphone. Only the parser creates these from raw text.</summary>
public sealed record Smartphone(
    string Model,
    int ReleaseYear,
    int RamGb,
    int StorageGb,
    int BatteryMah,
    decimal OriginalPrice
);

/// <summary>Stage 1 rule: which phones qualify. All bounds are inclusive.</summary>
public sealed record EligibilityCriteria(
    int MinReleaseYear,
    int MinRamGb,
    decimal MinPrice,
    decimal MaxPrice
);

/// <summary>Stage 2 output: a phone with its discount applied.</summary>
public sealed record PhoneQuote(
    Smartphone Phone,
    decimal DiscountPercent,
    decimal DiscountAmount,
    decimal FinalPrice
);

/// <summary>Stage 3 output: quotes ordered by final price (lowest first).</summary>
public sealed record QuoteReport(ImmutableArray<PhoneQuote> Quotes)
{
    public int Count => Quotes.Length;
    public decimal TotalFinalPrice => Quotes.Sum(q => q.FinalPrice);
}

/// <summary>A raw line that failed validation, with a human-readable reason.</summary>
public sealed record ParseError(int LineNumber, string? Line, string Message);

/// <summary>Result of parsing a whole dataset. An empty dataset is valid and has no errors.</summary>
public sealed record ParsedDataset(
    ImmutableArray<Smartphone> Phones,
    ImmutableArray<ParseError> Errors)
{
    public bool IsValid => Errors.IsEmpty;
}
