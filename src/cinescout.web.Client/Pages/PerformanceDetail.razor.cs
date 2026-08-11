using cinescout.contracts;
using cinescout.web.Client.Api;
using Microsoft.AspNetCore.Components;

namespace cinescout.web.Client.Pages;

public partial class PerformanceDetail
{
    private static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(30);

    [Parameter]
    public int Id { get; set; }

    private PerformanceDetailDto? _page;
    private bool _loaded;
    private bool _refreshing;
    private bool _coolingDown;
    private string? _error;
    private string _errorClass = "alert-danger";

    protected override async Task OnInitializedAsync()
    {
        if (RendererInfo.IsInteractive)
        {
            _page = await Api.GetAsync(Id);
            _loaded = true;
        }
    }

    private async Task ForceRefreshAsync()
    {
        _error = null;
        _refreshing = true;

        var (detail, problem) = await Api.ForceRefreshAsync(Id);

        _refreshing = false;

        if (problem is not null)
        {
            var alert = KinoheldStatusParser.Parse(problem.KinoheldStatus);
            _errorClass = alert.AlertClass;
            _error = problem.KinoheldStatus is null ? (problem.Detail ?? alert.Message) : alert.Message;
        }
        else if (detail is not null)
        {
            _page = detail;
        }

        // Whatever the outcome, an outgoing getSeats call was almost certainly just attempted
        // (KinoheldSeatCrawlService's cooldown tracker records it before dispatching) — disabling
        // for the same 30s window client-side avoids a guaranteed-to-fail immediate retry rather
        // than trying to mirror the server's exact per-outcome consumption rules here.
        _coolingDown = true;
        _ = ClearCooldownAfterDelayAsync();
    }

    private async Task ClearCooldownAfterDelayAsync()
    {
        await Task.Delay(Cooldown);
        _coolingDown = false;
        await InvokeAsync(StateHasChanged);
    }
}
