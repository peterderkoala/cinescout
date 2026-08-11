using System.Globalization;
using cinescout.contracts;
using Microsoft.AspNetCore.Components;

namespace cinescout.web.Client.Shared;

public partial class ZoneSeatGrid
{
    [Parameter, EditorRequired]
    public required ZoneSeatGridModel Matrix { get; set; }

    // RowStart/RowEnd are single Kinoheld row letters by domain convention (cinescout.model's own
    // doc comment), but nothing at the type level enforces that — guard against an empty string
    // reaching this component (e.g. from an editor that doesn't validate length) rather than
    // throwing IndexOutOfRangeException on a malformed matrix.
    private int RowCount => Matrix.RowStart.Length > 0 && Matrix.RowEnd.Length > 0
        ? Matrix.RowEnd[0] - Matrix.RowStart[0] + 1
        : 0;
    private int SeatCount => Matrix.SeatNumberEnd - Matrix.SeatNumberStart + 1;
    private SeatGridCappingResult Capping => SeatGridCapping.Compute(RowCount, SeatCount);

    private static string Opacity(double value) => value.ToString("0.####", CultureInfo.InvariantCulture);
}
