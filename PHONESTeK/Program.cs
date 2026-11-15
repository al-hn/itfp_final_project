using System.Collections.Immutable;
using Phones = PHONESTeK.Result<System.Collections.Immutable.ImmutableArray<PHONESTeK.Smartphone>>;

namespace PHONESTeK;

/// <summary>
/// Entry point. Running the program with no arguments (<c>dotnet run</c>) runs every demonstration in order:
///   Part 1  pricing pipeline with two discount policies   (Assignment 5)
///   Part 2  trade-in program                               (Assignment 5)
///   Part 3  validated purchases and order states           (Assignment 6)
///   Part 4  Result / Maybe / Map / Bind and file failures  (Assignment 7)
/// All input and output happens here and in PhoneLoader / Presenter; the domain code is pure.
/// </summary>
internal static class Program
{
    // Explicit configuration passed into the pure functions (no clock, no mutable globals).
    private const int ReferenceYear = 2026;
    private static readonly EligibilityCriteria Criteria = new(
        MinReleaseYear: 2022, MinRamGb: 8, MinPrice: 150_000m, MaxPrice: 700_000m);

    // Data/ is copied next to the executable, so this works from any working directory.
    private static string DataPath(string fileName) =>
        Path.Combine(AppContext.BaseDirectory, "Data", fileName);

    private static int Main()
    {
        // The main catalog goes through the same file boundary as everything else.
        switch (PhoneLoader.LoadPipeText(DataPath("phones.txt")))
        {
            case Phones.Ok loaded:
                Console.WriteLine($"Loaded {loaded.Value.Length} phone(s) from Data/phones.txt.");
                RunAll(loaded.Value);
                return 0;
            case Phones.Fail failed:
                Console.Error.WriteLine($"Cannot start: [{failed.Error.Code}] {failed.Error.Message}");
                return 1;
            default:
                Console.Error.WriteLine("Cannot start: unknown load result.");
                return 1;
        }
    }

    private static void RunAll(ImmutableArray<Smartphone> phones)
    {
        Presenter.Banner("PART 1: pricing pipeline, two discount policies (Assignment 5)");
        RunPricing(phones);

        Presenter.Banner("PART 2: trade-in program (Assignment 5)");
        RunTradeIn(phones);

        Presenter.Banner("PART 3: validated purchases and order states (Assignment 6)");
        RunOrders(phones);

        Presenter.Banner("PART 4: Result, Maybe, Map/Bind and file failures (Assignment 7)");
        RunResults();

        Presenter.Banner("PART 5: extensions, LINQ, currying, deferred execution and native C (Assignment 8)");
        RunAssignment8FromJson();
    }

    // ---------- Part 1 ----------

    private static void RunPricing(IReadOnlyList<Smartphone> phones)
    {
        // Policy selection happens HERE, outside the pure pipeline.
        var standard = PricingPipeline.Run(phones, Criteria, DiscountPolicies.Standard(ReferenceYear));
        var clearance = PricingPipeline.Run(phones, Criteria, DiscountPolicies.Clearance(ReferenceYear));

        Console.WriteLine($"Eligibility: year >= {Criteria.MinReleaseYear}, RAM >= {Criteria.MinRamGb} GB, " +
                          $"price {Presenter.Money(Criteria.MinPrice)}..{Presenter.Money(Criteria.MaxPrice)} KZT.");
        Presenter.PrintReport("Policy: standard", standard);
        Presenter.PrintReport("Policy: clearance", clearance);
        Presenter.PrintComparison("standard", standard, "clearance", clearance);
    }

    // ---------- Part 2 ----------

    private static void RunTradeIn(IReadOnlyList<Smartphone> phones)
    {
        var demos = new (string Old, string New)[]
        {
            ("Galaxy S22", "Galaxy S24 Ultra"), // accepted
            ("iPhone 14", "iPhone 15 Pro"),     // rejected: gap and price
            ("Pixel 6", "Xiaomi 14"),           // rejected: brand
        };

        foreach (var (oldName, newName) in demos)
        {
            var oldPhone = phones.FirstOrDefault(p => p.Model == oldName);
            var newPhone = phones.FirstOrDefault(p => p.Model == newName);
            if (oldPhone is null || newPhone is null)
            {
                Console.WriteLine($"Skipping '{oldName}' -> '{newName}': a model is missing from the data file.");
                continue;
            }

            var accepted = TradeInProgram.TryTradeIn(oldPhone, newPhone, ReferenceYear, out var offer, out var rejection);
            Presenter.PrintTradeIn(oldPhone, newPhone, accepted, offer, rejection);
        }
    }

    // ---------- Part 3 ----------

    private static void RunOrders(IReadOnlyList<Smartphone> phones)
    {
        var policy = DiscountPolicies.Standard(ReferenceYear);
        var book = OrderBook.Empty;

        // Each step takes the current book and returns a new one (the same one if the step is rejected).
        void Step(string label, Func<OrderBook, (OrderBook Book, TransitionResult Result)> action)
        {
            var change = action(book);
            book = change.Book;
            Presenter.PrintStep(label, change.Result);
        }

        Step("3.1 Place: Aigerim buys Galaxy S22 (valid)",
            b => OrderService.Place(b, new PurchaseRequest("Aigerim", "Galaxy S22", null), phones, Criteria, policy, ReferenceYear));
        Step("3.2 Collect ORD-0001 before confirming (invalid sequence)",
            b => OrderService.Collect(b, "ORD-0001"));
        Step("3.3 Confirm ORD-0001",
            b => OrderService.Confirm(b, "ORD-0001"));
        Step("3.4 Confirm ORD-0001 again (repeat: returns the existing confirmation)",
            b => OrderService.Confirm(b, "ORD-0001"));
        Step("3.5 Collect ORD-0001",
            b => OrderService.Collect(b, "ORD-0001"));
        Step("3.6 Collect ORD-0001 again (repeat: rejected)",
            b => OrderService.Collect(b, "ORD-0001"));
        Step("3.7 Place: Madina buys Nokia Budget (invalid: not eligible)",
            b => OrderService.Place(b, new PurchaseRequest("Madina", "Nokia Budget", null), phones, Criteria, policy, ReferenceYear));
        Step("3.8 Place: Timur buys iPhone 15 Pro, trading in iPhone 14 (invalid: trade-in rules)",
            b => OrderService.Place(b, new PurchaseRequest("Timur", "iPhone 15 Pro", "iPhone 14"), phones, Criteria, policy, ReferenceYear));
        Step("3.9 Place: Daniyar buys Galaxy S24 Ultra, trading in Galaxy S22 (valid)",
            b => OrderService.Place(b, new PurchaseRequest("Daniyar", "Galaxy S24 Ultra", "Galaxy S22"), phones, Criteria, policy, ReferenceYear));
        Step("3.10 Confirm ORD-9999 (invalid: order does not exist)",
            b => OrderService.Confirm(b, "ORD-9999"));

        Presenter.PrintBook(book);
    }


    // ---------- Part 4 ----------

    private static void RunResults()
    {
        // 4.1 normal path through the file boundary: the JSON catalog feeds the quote chain.
        var jsonLoad = PhoneLoader.LoadJson(DataPath("phones.json"));
        Presenter.PrintOutcome("4.1 Load Data/phones.json (file boundary)", jsonLoad, p => $"{p.Length} phone(s) loaded.");

        switch (jsonLoad)
        {
            case Phones.Ok loaded:
                RunQuoteChain(loaded.Value);
                break;
            default:
                Console.WriteLine("  Quote demonstrations skipped: the JSON catalog could not be loaded (see above).");
                break;
        }

        RunFileFailures();
    }

    private static void RunQuoteChain(ImmutableArray<Smartphone> catalog)
    {
        var policy = DiscountPolicies.Standard(ReferenceYear);

        // Find (Maybe) -> ToResult -> Bind(validate) -> Bind(price) -> Map(display); the outcome is handled once, in Presenter.
        void Show(string label, QuoteRequest request) =>
            Presenter.PrintOutcome(label, QuoteService.Quote(catalog, request, Criteria, policy), Presenter.DescribeQuote);

        Show("4.2 Normal path: 2 x Galaxy S22, no budget",
            new QuoteRequest("Galaxy S22", 2, Maybe.None<decimal>()));
        Show("4.3 Domain failure, invalid quantity: 0 x Galaxy S22",
            new QuoteRequest("Galaxy S22", 0, Maybe.None<decimal>()));
        Show("4.4 Domain failure, not eligible: Nokia Budget",
            new QuoteRequest("Nokia Budget", 1, Maybe.None<decimal>()));
        Show("4.5 Domain failure, over budget: 2 x Galaxy S22 (560,000) with budget 500,000",
            new QuoteRequest("Galaxy S22", 2, Maybe.Some(500_000m)));
        Show("4.6 Lookup absent: Galaxy S99 is not in the catalog",
            new QuoteRequest("Galaxy S99", 1, Maybe.None<decimal>()));
        Show("4.7 Lookup present, budget exactly equal to the total (boundary: allowed)",
            new QuoteRequest("galaxy s22", 2, Maybe.Some(560_000m)));
        Show("4.8 Budget 0 is a real value, not 'no budget': 1 x Galaxy S22",
            new QuoteRequest("Galaxy S22", 1, Maybe.Some(0m)));
    }

    private static void RunFileFailures()
    {
        Presenter.PrintOutcome(
            "4.9 File failure, missing file: Data/phones_does_not_exist.json",
            PhoneLoader.LoadJson(DataPath("phones_does_not_exist.json")),
            p => $"{p.Length} phone(s) loaded.");

        Presenter.PrintOutcome(
            "4.10 File failure, malformed JSON: Data/phones_malformed.json",
            PhoneLoader.LoadJson(DataPath("phones_malformed.json")),
            p => $"{p.Length} phone(s) loaded.");

        Presenter.PrintOutcome(
            "4.11 File failure, invalid lines in a text file: Data/phones_invalid.txt (nothing is loaded)",
            PhoneLoader.LoadPipeText(DataPath("phones_invalid.txt")),
            p => $"{p.Length} phone(s) loaded.");

        // An empty file is a valid, empty dataset: a success, not a failure.
        var emptyLoad = PhoneLoader.LoadPipeText(DataPath("phones_empty.txt"));
        Presenter.PrintOutcome("4.12 Empty text file: Data/phones_empty.txt (valid, zero phones)", emptyLoad, p => $"{p.Length} phone(s) loaded.");
        if (emptyLoad is Phones.Ok empty)
        {
            Presenter.PrintReport(
                "Empty dataset through the pricing pipeline",
                PricingPipeline.Run(empty.Value, Criteria, DiscountPolicies.Standard(ReferenceYear)));
        }
    }

    // ---------- Part 5: Assignment 8 ----------

    private static void RunAssignment8FromJson()
    {
        // Assignment 8 reports explicitly use the project's JSON input, not a hard-coded sample collection.
        switch (PhoneLoader.LoadJson(DataPath("phones.json")))
        {
            case Phones.Ok loaded:
                RunAssignment8(loaded.Value);
                break;
            case Phones.Fail failed:
                Presenter.PrintOutcome("Assignment 8 JSON catalog", Result.Fail<ImmutableArray<Smartphone>>(failed.Error), p => $"{p.Length} phone(s)");
                break;
        }
    }

    private static void RunAssignment8(ImmutableArray<Smartphone> phones)
    {
        // Query transformations are pure and return data; only this method formats Console output.
        var report = PhoneReports.Create(phones, Criteria);
        Console.WriteLine("Report 1 — eligible phones for shoppers (price ascending, model tie-breaker):");
        if (report.EligiblePhones.IsEmpty) Console.WriteLine("  No matching phones.");
        foreach (var phone in report.EligiblePhones)
            Console.WriteLine($"  {phone.Model}: {Presenter.Money(phone.OriginalPrice)} KZT, {phone.ReleaseYear}, {phone.RamGb} GB RAM");

        Console.WriteLine("Report 2 — original catalog value by model prefix:");
        foreach (var group in report.BrandValues)
            Console.WriteLine($"  {group.Brand}: {group.PhoneCount} phone(s), total {Presenter.Money(group.TotalOriginalValue)} KZT, average {Presenter.Money(group.AverageOriginalPrice)} KZT");

        // Both extensions are called by real application code.
        var standard = PricingPipeline.Run(phones, Criteria, DiscountPolicies.Standard(ReferenceYear));
        Console.WriteLine("Savings extension (standard policy):");
        foreach (var quote in standard.Quotes.OrderByFinalPriceThenModel())
            Console.WriteLine($"  {quote.Phone.Model}: saved {Presenter.Money(quote.Savings())} KZT");

        // Currying and partial application: two fixed configurations, same input phone.
        if (!phones.IsEmpty)
        {
            var sample = phones[0];
            Console.WriteLine($"Configured scores for {sample.Model}: balanced={PhoneScoring.Balanced(sample)}, battery-focused={PhoneScoring.BatteryFocused(sample)}");
        }

        // Deferred iterator: constructing the sequence does not enumerate it. Materialise once for stable reuse.
        var processed = 0;
        IEnumerable<Smartphone> ProbeSource()
        {
            foreach (var phone in phones)
            {
                processed++;
                yield return phone;
            }
        }
        var deferred = PhoneStreams.EligibleDeferred(ProbeSource(), Criteria);
        Console.WriteLine($"Deferred sequence created; source items processed: {processed} (expected 0).");
        var snapshot = deferred.ToArray();
        Console.WriteLine($"After ToArray: source items processed: {processed}; eligible snapshot: {snapshot.Length}.");
        Console.WriteLine($"Snapshot reused without re-reading: {snapshot.Length} item(s).");

        // Real P/Invoke call. If the binary is missing, the adapter returns an explicit operational failure.
        if (!phones.IsEmpty)
        {
            var native = NativePhoneScorer.Score(phones[0]);
            Presenter.PrintOutcome("Native C phone score (P/Invoke)", native, score => $"{phones[0].Model}: {score}");
        }
    }
}
