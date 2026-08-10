namespace cinescout.contracts;

/// <summary>
/// The §6.3 <c>DaysOfWeekFlags</c> ↔ day-code convention: Mo/Tu/We/Th/Fr/Sa/Su, always emitted in
/// that fixed order regardless of input order. Mutation payloads carry the <c>string[]</c>
/// <see cref="ToDayCodes"/> produces; read DTOs carry that same shape as a pre-computed chip label
/// (each code renders as one chip) instead of the raw flags value.
/// </summary>
public static class DayCodeConverter
{
    /// <summary>Fixed Mo→Su rendering/emission order, per §6.3.</summary>
    private static readonly (Weekday Day, string Code)[] OrderedDays =
    [
        (Weekday.Monday, "Mo"),
        (Weekday.Tuesday, "Tu"),
        (Weekday.Wednesday, "We"),
        (Weekday.Thursday, "Th"),
        (Weekday.Friday, "Fr"),
        (Weekday.Saturday, "Sa"),
        (Weekday.Sunday, "Su"),
    ];

    /// <summary>Converts flags to day codes, always in fixed Mo..Su order regardless of which bits are set first.</summary>
    public static string[] ToDayCodes(Weekday days) =>
        OrderedDays.Where(d => days.HasFlag(d.Day)).Select(d => d.Code).ToArray();

    /// <summary>
    /// Converts day codes back to flags. Order-independent — duplicates are harmless, but any code
    /// outside Mo/Tu/We/Th/Fr/Sa/Su throws, since the wire vocabulary is a fixed, closed set.
    /// </summary>
    public static Weekday FromDayCodes(IEnumerable<string> codes)
    {
        ArgumentNullException.ThrowIfNull(codes);

        var remaining = new HashSet<string>(codes, StringComparer.Ordinal);
        var result = Weekday.None;

        foreach (var (day, code) in OrderedDays)
        {
            if (remaining.Remove(code))
            {
                result |= day;
            }
        }

        if (remaining.Count > 0)
        {
            throw new ArgumentException(
                $"Unrecognized day code(s): {string.Join(", ", remaining)}. Expected only Mo/Tu/We/Th/Fr/Sa/Su.",
                nameof(codes));
        }

        return result;
    }
}
