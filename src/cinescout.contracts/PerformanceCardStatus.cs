namespace cinescout.contracts;

/// <summary>
/// The §6.1 performance status badge, precedence-derived (Cancelled beats sold-out beats seats
/// available beats no badge) — never a raw model field, always computed server-side before crossing
/// the wire.
/// </summary>
public enum PerformanceCardStatus
{
    None,
    Available,
    SoldOut,
    Cancelled,
}
