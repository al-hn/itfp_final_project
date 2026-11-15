using System.Globalization;

namespace PHONESTeK;

/// <summary>Display only. Receives finished results; computes nothing about the domain.</summary>
internal static class Presenter
{
    public static string Money(decimal value) => value.ToString("N0", CultureInfo.InvariantCulture);

    private static string Percent(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture) + "%";

    public static void PrintReport(string title, QuoteReport report)
    {
        Console.WriteLine();
        Console.WriteLine($"=== {title} ===");
        if (report.Count == 0)
        {
            Console.WriteLine("No phones qualify.");
            return;
        }

        Console.WriteLine(
            "Model".PadRight(18) + "Year".PadLeft(5) + "RAM".PadLeft(5) + "Storage".PadLeft(9) + "mAh".PadLeft(6) +
            "Original".PadLeft(11) + "Disc".PadLeft(7) + "Discount".PadLeft(10) + "Final".PadLeft(11));

        foreach (var q in report.Quotes)
        {
            var p = q.Phone;
            Console.WriteLine(
                p.Model.PadRight(18) + p.ReleaseYear.ToString(CultureInfo.InvariantCulture).PadLeft(5) +
                p.RamGb.ToString(CultureInfo.InvariantCulture).PadLeft(5) +
                p.StorageGb.ToString(CultureInfo.InvariantCulture).PadLeft(9) +
                p.BatteryMah.ToString(CultureInfo.InvariantCulture).PadLeft(6) +
                Money(p.OriginalPrice).PadLeft(11) + Percent(q.DiscountPercent).PadLeft(7) +
                Money(q.DiscountAmount).PadLeft(10) + Money(q.FinalPrice).PadLeft(11));
        }
        Console.WriteLine($"{report.Count} phone(s), total final price {Money(report.TotalFinalPrice)} KZT");
    }

    public static void PrintComparison(string nameA, QuoteReport a, string nameB, QuoteReport b)
    {
        Console.WriteLine();
        Console.WriteLine($"=== Same data, same pipeline: {nameA} vs {nameB} (final price) ===");
        var byModelB = b.Quotes.ToDictionary(q => q.Phone.Model);
        foreach (var qa in a.Quotes)
        {
            if (!byModelB.TryGetValue(qa.Phone.Model, out var qb)) continue;
            var diff = qb.FinalPrice - qa.FinalPrice;
            Console.WriteLine(
                qa.Phone.Model.PadRight(18) + Money(qa.FinalPrice).PadLeft(11) + Money(qb.FinalPrice).PadLeft(11) +
                ("  (" + (diff > 0 ? "+" : "") + Money(diff) + ")"));
        }
        Console.WriteLine("Total".PadRight(18) + Money(a.TotalFinalPrice).PadLeft(11) + Money(b.TotalFinalPrice).PadLeft(11));
    }

    public static void PrintTradeIn(Smartphone oldPhone, Smartphone newPhone, bool accepted, TradeInOffer? offer, string rejection)
    {
        Console.WriteLine();
        Console.WriteLine($"Trade-in: {oldPhone.Model} -> {newPhone.Model}");
        if (accepted && offer is not null)
        {
            Console.WriteLine($"  ACCEPTED. Credit {Money(offer.Credit)} KZT; pay {Money(offer.PriceAfterCredit)} KZT " +
                              $"(list price {Money(offer.NewPhone.OriginalPrice)}).");
        }
        else
        {
            Console.WriteLine($"  REJECTED. {rejection}");
        }
    }

    public static void PrintStep(string label, TransitionResult result)
    {
        Console.WriteLine();
        Console.WriteLine(label);
        switch (result)
        {
            case TransitionResult.Moved moved:
                Console.WriteLine("  OK. " + Describe(moved.Next));
                break;
            case TransitionResult.Rejected rejected:
                Console.WriteLine("  REJECTED. " + rejected.Reason);
                break;
            default:
                Console.WriteLine("  REJECTED. Unknown outcome.");
                break;
        }
    }

    public static void PrintBook(OrderBook book)
    {
        Console.WriteLine();
        Console.WriteLine($"=== Order book: {book.Orders.Count} order(s) ===");
        foreach (var state in book.Orders.Values.OrderBy(s => s.OrderId, StringComparer.Ordinal))
            Console.WriteLine("  " + Describe(state));
    }

    private static string Describe(OrderState state) => state switch
    {
        OrderState.Quoted q =>
            $"{q.OrderId} quoted for {q.Purchase.Customer}: {q.Purchase.Phone.Model}, " +
            $"discount {Money(q.Purchase.DiscountAmount)}, trade-in credit {Money(q.Purchase.TradeInCredit)}, " +
            $"pay {Money(q.Purchase.Total)} KZT.",
        OrderState.Confirmed c =>
            $"{c.OrderId} confirmed for {c.Purchase.Customer}, confirmation #{c.ConfirmationNumber}.",
        OrderState.Collected c =>
            $"{c.OrderId} collected by {c.Purchase.Customer}, confirmation #{c.ConfirmationNumber}.",
        _ => "unknown order state",
    };

    public static void Banner(string title)
    {
        Console.WriteLine();
        Console.WriteLine(new string('=', 78));
        Console.WriteLine(title);
        Console.WriteLine(new string('=', 78));
    }

    /// <summary>
    /// The single place where a Result is handled at the application boundary: one explicit branch per case.
    /// </summary>
    public static void PrintOutcome<T>(string label, Result<T> result, Func<T, string> describeSuccess)
    {
        Console.WriteLine();
        Console.WriteLine(label);
        switch (result)
        {
            case Result<T>.Ok ok:
                Console.WriteLine("  OK. " + describeSuccess(ok.Value));
                break;
            case Result<T>.Fail fail:
                Console.WriteLine($"  FAILED [{fail.Error.Kind}: {fail.Error.Code}] {fail.Error.Message}");
                break;
            default:
                Console.WriteLine("  FAILED. Unknown result case.");
                break;
        }
    }

    public static string DescribeQuote(QuoteView view) =>
        $"{view.Quantity} x {view.Model}: {view.Discount} off, {view.UnitPrice} each, total {view.Total} KZT.";
}
