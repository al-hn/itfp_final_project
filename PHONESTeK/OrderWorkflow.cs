namespace PHONESTeK;

/// <summary>
/// Pure state transitions: current state in, <see cref="TransitionResult"/> out. No I/O, no stored data,
/// no mutation. The prerequisite of each step is visible in its signature and in its switch.
///
/// Repeated-operation rules (documented, tested):
///   Confirm on a Confirmed order -> returns the SAME existing Confirmed state (idempotent, no new number).
///   Collect on a Collected order -> rejected (a phone can only be handed over once).
/// </summary>
public static class OrderTransitions
{
    /// <summary>Starts an order. Takes a <see cref="ValidatedPurchase"/>, never a raw request.</summary>
    public static OrderState.Quoted Quote(string orderId, ValidatedPurchase purchase)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orderId);
        ArgumentNullException.ThrowIfNull(purchase);
        return new OrderState.Quoted(orderId, purchase);
    }

    /// <summary>Quoted -> Confirmed. The confirmation number is an explicit input.</summary>
    public static TransitionResult Confirm(OrderState state, int confirmationNumber) => state switch
    {
        OrderState.Quoted quoted =>
            new TransitionResult.Moved(new OrderState.Confirmed(quoted.OrderId, quoted.Purchase, confirmationNumber)),
        OrderState.Confirmed confirmed =>
            new TransitionResult.Moved(confirmed),
        OrderState.Collected collected =>
            new TransitionResult.Rejected($"Order {collected.OrderId} was already collected and cannot be confirmed again."),
        _ =>
            new TransitionResult.Rejected("Unknown order state; the order was not confirmed."),
    };

    /// <summary>Confirmed -> Collected. Collecting before confirming is an invalid sequence.</summary>
    public static TransitionResult Collect(OrderState state) => state switch
    {
        OrderState.Confirmed confirmed =>
            new TransitionResult.Moved(new OrderState.Collected(confirmed.OrderId, confirmed.Purchase, confirmed.ConfirmationNumber)),
        OrderState.Quoted quoted =>
            new TransitionResult.Rejected($"Order {quoted.OrderId} has not been confirmed yet; confirm it before handing the phone over."),
        OrderState.Collected collected =>
            new TransitionResult.Rejected($"Order {collected.OrderId} was already collected."),
        _ =>
            new TransitionResult.Rejected("Unknown order state; the order was not collected."),
    };
}

/// <summary>
/// The boundary where results are committed to the <see cref="OrderBook"/>. Every operation first decides
/// (validation and pure transition), and only then builds the new book in one step. On any rejection it
/// returns the original book instance untouched, so a failure can never leave a half-updated book.
/// </summary>
public static class OrderService
{
    public static (OrderBook Book, TransitionResult Result) Place(
        OrderBook book,
        PurchaseRequest request,
        IReadOnlyCollection<Smartphone> catalog,
        EligibilityCriteria criteria,
        Func<Smartphone, decimal> discountPolicy,
        int referenceYear)
    {
        ArgumentNullException.ThrowIfNull(book);

        var validation = ValidatedPurchase.Create(request, catalog, criteria, discountPolicy, referenceYear);

        return validation switch
        {
            PurchaseValidation.Valid valid => AddQuoted(book, valid.Purchase),
            PurchaseValidation.Invalid invalid => Outcome(book, new TransitionResult.Rejected(string.Join(" ", invalid.Reasons))),
            _ => Outcome(book, new TransitionResult.Rejected("Unknown validation outcome; no order was placed.")),
        };
    }

    public static (OrderBook Book, TransitionResult Result) Confirm(OrderBook book, string? orderId)
    {
        ArgumentNullException.ThrowIfNull(book);

        var current = book.Find(orderId);
        if (current is null)
            return Outcome(book, new TransitionResult.Rejected($"Order '{orderId}' does not exist; place the order first."));

        return OrderTransitions.Confirm(current, book.NextConfirmationNumber) switch
        {
            // Repeated confirmation: the existing outcome is returned, nothing is stored, no number is used up.
            TransitionResult.Moved moved when ReferenceEquals(moved.Next, current) =>
                Outcome(book, moved),
            TransitionResult.Moved moved =>
                Outcome(
                    book with
                    {
                        Orders = book.Orders.SetItem(moved.Next.OrderId, moved.Next),
                        NextConfirmationNumber = book.NextConfirmationNumber + 1,
                    },
                    moved),
            TransitionResult.Rejected rejected =>
                Outcome(book, rejected),
            _ =>
                Outcome(book, new TransitionResult.Rejected("Unknown transition outcome; nothing was changed.")),
        };
    }

    public static (OrderBook Book, TransitionResult Result) Collect(OrderBook book, string? orderId)
    {
        ArgumentNullException.ThrowIfNull(book);

        var current = book.Find(orderId);
        if (current is null)
            return Outcome(book, new TransitionResult.Rejected($"Order '{orderId}' does not exist; place the order first."));

        return OrderTransitions.Collect(current) switch
        {
            TransitionResult.Moved moved =>
                Outcome(book with { Orders = book.Orders.SetItem(moved.Next.OrderId, moved.Next) }, moved),
            TransitionResult.Rejected rejected =>
                Outcome(book, rejected),
            _ =>
                Outcome(book, new TransitionResult.Rejected("Unknown transition outcome; nothing was changed.")),
        };
    }

    private static (OrderBook Book, TransitionResult Result) AddQuoted(OrderBook book, ValidatedPurchase purchase)
    {
        var orderId = $"ORD-{book.NextOrderNumber:D4}";
        var quoted = OrderTransitions.Quote(orderId, purchase);
        var next = book with
        {
            Orders = book.Orders.Add(orderId, quoted),
            NextOrderNumber = book.NextOrderNumber + 1,
        };
        return Outcome(next, new TransitionResult.Moved(quoted));
    }

    private static (OrderBook Book, TransitionResult Result) Outcome(OrderBook book, TransitionResult result) =>
        (book, result);
}
