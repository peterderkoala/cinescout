using cinescout.contracts;
using Microsoft.AspNetCore.Components;

namespace cinescout.web.Client.Shared;

public partial class FilmPerformanceCard
{
    [Parameter, EditorRequired]
    public required FilmPerformanceCardModel Card { get; set; }

    [Parameter, EditorRequired]
    public required FilmPerformanceCardVariant Variant { get; set; }

    [Parameter]
    public EventCallback OnSelect { get; set; }

    private Task HandleSelect() => OnSelect.InvokeAsync();

    private string? FeaturedCardStyle => Card.IsMatch
        ? "border-left:4px solid var(--cinescout-accent-match); box-shadow:0 0 0 1px rgba(184,134,11,.2), 0 .25rem .6rem rgba(0,0,0,.08);"
        : null;

    private string CinemaRoomLine => string.Join(" · ", new[] { Card.Cinema, Card.Room }.Where(s => !string.IsNullOrEmpty(s)));

    private (string Class, string Text)? StatusBadge => Card.Status switch
    {
        PerformanceCardStatus.Cancelled => ("text-bg-secondary", "Cancelled"),
        PerformanceCardStatus.SoldOut => ("text-bg-danger", "Sold out"),
        PerformanceCardStatus.Available => ("text-bg-success", "Seats available"),
        _ => null,
    };

    private static bool IsSeatRelated(string reason) =>
        reason.Contains("seat", StringComparison.OrdinalIgnoreCase) || reason.Contains("free", StringComparison.OrdinalIgnoreCase);
}
