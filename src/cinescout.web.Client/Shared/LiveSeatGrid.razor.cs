using cinescout.contracts;
using Microsoft.AspNetCore.Components;

namespace cinescout.web.Client.Shared;

public partial class LiveSeatGrid
{
    [Parameter, EditorRequired]
    public required IReadOnlyList<LiveSeatDto> Seats { get; set; }

    private IReadOnlyList<LiveSeatGridRow> Rows => LiveSeatGridLayout.Build(Seats);
    private int TotalCount => Seats.Count;
    private int FreeCount => Seats.Count(s => s.Status == SeatOccupancyStatus.Free);

    private static string CellStyle(LiveSeatGridCell cell) => cell.Seat?.Status switch
    {
        SeatOccupancyStatus.Free => "background:transparent; border:2px solid var(--bs-success);",
        SeatOccupancyStatus.Sold => "background:var(--bs-secondary); border:2px solid var(--bs-secondary);",
        SeatOccupancyStatus.Other => "background:transparent; border:2px solid var(--bs-warning);",
        _ => "visibility:hidden;",
    };
}
