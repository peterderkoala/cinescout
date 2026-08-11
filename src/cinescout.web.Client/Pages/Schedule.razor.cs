using cinescout.contracts;

namespace cinescout.web.Client.Pages;

public partial class Schedule
{
    private ScheduleDto? _schedule;
    private int _weekOffset;
    private bool _busy;

    protected override async Task OnInitializedAsync()
    {
        if (RendererInfo.IsInteractive)
        {
            _schedule = await Api.GetAsync(_weekOffset);
        }
    }

    private async Task PreviousWeekAsync()
    {
        if (_weekOffset == 0)
        {
            return;
        }

        await LoadWeekAsync(_weekOffset - 1);
    }

    private Task NextWeekAsync() => LoadWeekAsync(_weekOffset + 1);

    private async Task LoadWeekAsync(int weekOffset)
    {
        _busy = true;

        try
        {
            _schedule = await Api.GetAsync(weekOffset);
            _weekOffset = _schedule.WeekOffset;
        }
        finally
        {
            _busy = false;
        }
    }

    private void GoToPerformance(int performanceId) => NavigationManager.NavigateTo($"/performances/{performanceId}");
}
