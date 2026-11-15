using System.Collections.Immutable;

namespace PHONESTeK;

/// <summary>
/// Where an order is in its life. Three mutually exclusive cases (closed hierarchy):
/// <c>Quoted</c>    - validated and priced, waiting for confirmation (no confirmation number yet),
/// <c>Confirmed</c> - confirmed, carries the confirmation number,
/// <c>Collected</c> - the phone was handed over, carries the confirmation number.
/// Every case carries a <see cref="ValidatedPurchase"/>, so an order can never exist for unchecked data.
/// Case constructors are internal: states are only created by <see cref="OrderTransitions"/>.
/// </summary>
public abstract record OrderState
{
    public string OrderId { get; }
    public ValidatedPurchase Purchase { get; }

    private OrderState(string orderId, ValidatedPurchase purchase)
    {
        OrderId = orderId;
        Purchase = purchase;
    }

    public sealed record Quoted : OrderState
    {
        internal Quoted(string orderId, ValidatedPurchase purchase)
            : base(orderId, purchase) { }
    }

    public sealed record Confirmed : OrderState
    {
        public int ConfirmationNumber { get; }

        internal Confirmed(string orderId, ValidatedPurchase purchase, int confirmationNumber)
            : base(orderId, purchase)
        {
            ConfirmationNumber = confirmationNumber;
        }
    }

    public sealed record Collected : OrderState
    {
        public int ConfirmationNumber { get; }

        internal Collected(string orderId, ValidatedPurchase purchase, int confirmationNumber)
            : base(orderId, purchase)
        {
            ConfirmationNumber = confirmationNumber;
        }
    }
}

/// <summary>
/// Result of a state transition: <c>Moved</c> carries the next state, <c>Rejected</c> carries the reason.
/// A rejection never carries a state, so it cannot be mistaken for success.
/// </summary>
public abstract record TransitionResult
{
    private TransitionResult() { }

    public sealed record Moved(OrderState Next) : TransitionResult;

    public sealed record Rejected(string Reason) : TransitionResult;
}

/// <summary>
/// Immutable store of all orders. Operations never change an existing book; they return a new one,
/// or the very same instance when nothing changed.
/// </summary>
public sealed record OrderBook(
    ImmutableDictionary<string, OrderState> Orders,
    int NextOrderNumber,
    int NextConfirmationNumber)
{
    public static OrderBook Empty { get; } =
        new(ImmutableDictionary<string, OrderState>.Empty, 1, 1001);

    public OrderState? Find(string? orderId) =>
        orderId is not null && Orders.TryGetValue(orderId, out var state) ? state : null;
}
