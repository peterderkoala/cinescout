using cinescout.contracts;
using cinescout.web.Client.Api;

namespace cinescout.web.Client.Pages;

public partial class TrackedMovies
{
    private TrackedMoviesPageDto? _page;
    private bool _busy;
    private string? _error;

    protected override async Task OnInitializedAsync()
    {
        if (RendererInfo.IsInteractive)
        {
            _page = await Api.GetAsync();
        }
    }

    private Task TrackAsync(int filmId) => RunActionAsync(() => Api.TrackAsync(filmId));

    private Task UntrackAsync(int filmId) => RunActionAsync(() => Api.UntrackAsync(filmId));

    private async Task RunActionAsync(Func<Task<(TrackedMoviesPageDto? Page, ApiProblem? Problem)>> action)
    {
        _error = null;
        _busy = true;

        try
        {
            var (page, problem) = await action();

            if (problem is not null)
            {
                _error = problem.Detail ?? problem.Title ?? "Something went wrong.";
                // The acted-on film may no longer exist (e.g. removed by another session) — refetch
                // so a dead row doesn't linger in the UI indefinitely instead of leaving _page stale.
                _page = await Api.GetAsync();
                return;
            }

            _page = page;
        }
        finally
        {
            _busy = false;
        }
    }
}
