namespace cinescout.contracts;

/// <summary>
/// A self-contained mirror of cinescout.model's <c>DaysOfWeekFlags</c> [Flags] enum.
/// cinescout.contracts is a true leaf (see the project file's header comment) and must never
/// reference cinescout.model, so it cannot use that type directly. The bit values below are
/// deliberately identical to the model's (Monday = 1&lt;&lt;0 ... Sunday = 1&lt;&lt;6), so a caller
/// that CAN see both types (e.g. cinescout.web) converts between them with a plain cast:
/// <c>(Weekday)(int)modelValue</c> / <c>(DaysOfWeekFlags)(int)contractsValue</c>. Keep the two enums'
/// bit layouts in lockstep if either ever changes.
/// </summary>
[Flags]
public enum Weekday
{
    None = 0,
    Monday = 1 << 0,
    Tuesday = 1 << 1,
    Wednesday = 1 << 2,
    Thursday = 1 << 3,
    Friday = 1 << 4,
    Saturday = 1 << 5,
    Sunday = 1 << 6,
}
