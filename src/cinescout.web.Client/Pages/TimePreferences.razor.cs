using cinescout.contracts;

namespace cinescout.web.Client.Pages;

public partial class TimePreferences
{
    /// <summary>Fixed Mo..Su rendering order, per §6.3 — matches DayCodeConverter server-side.</summary>
    private static readonly string[] DayCodesInOrder = ["Mo", "Tu", "We", "Th", "Fr", "Sa", "Su"];

    private IReadOnlyList<TimeWindowDto>? _windows;
    private int? _editingId;
    private HashSet<string> _selectedDayCodes = [];
    // Nullable, not TimeOnly: a native <input type="time"> whose value the user clears fails
    // BindConverter's parse and Blazor's generated @bind setter is simply never invoked for a
    // non-nullable target — the field would silently keep its last value with no error shown.
    // Binding to TimeOnly? instead lets a cleared field become null, which SaveAsync below can
    // detect and reject explicitly, porting the deleted SSR page's own "Enter a valid start and
    // end time." guard forward.
    private TimeOnly? _startTime = new TimeOnly(18, 0);
    private TimeOnly? _endTime = new TimeOnly(22, 0);

    private string? _error;
    private bool _saving;

    protected override async Task OnInitializedAsync()
    {
        if (RendererInfo.IsInteractive)
        {
            _windows = await Api.GetListAsync();
        }
    }

    private void ToggleDay(string code)
    {
        if (!_selectedDayCodes.Remove(code))
        {
            _selectedDayCodes.Add(code);
        }
    }

    private void StartEdit(TimeWindowDto window)
    {
        _error = null;
        _editingId = window.Id;
        _selectedDayCodes = [.. window.DayCodes];
        _startTime = window.StartTime;
        _endTime = window.EndTime;
    }

    private void ResetEditor()
    {
        _error = null;
        _editingId = null;
        _selectedDayCodes = [];
        _startTime = new TimeOnly(18, 0);
        _endTime = new TimeOnly(22, 0);
    }

    private async Task SaveAsync()
    {
        if (_startTime is not { } start || _endTime is not { } end)
        {
            _error = "Enter a valid start and end time.";
            return;
        }

        var request = new TimeWindowWriteRequest { DayCodes = [.. _selectedDayCodes], StartTime = start, EndTime = end };
        if (TimeWindowValidation.Validate(request) is { } validationError)
        {
            _error = validationError;
            return;
        }

        _error = null;
        _saving = true;

        var (window, problem) = _editingId is { } id
            ? await Api.UpdateAsync(id, request)
            : await Api.CreateAsync(request);

        _saving = false;

        if (problem is not null)
        {
            _error = problem.Detail ?? problem.Title ?? "Save failed.";
            return;
        }

        if (window is not null)
        {
            ReplaceWindow(window);
        }

        ResetEditor();
    }

    private async Task DeleteAsync(int id)
    {
        _error = null;

        var problem = await Api.DeleteAsync(id);
        if (problem is not null)
        {
            _error = problem.Detail ?? problem.Title ?? "Delete failed.";
            return;
        }

        _windows = _windows!.Where(w => w.Id != id).ToList();

        if (_editingId == id)
        {
            ResetEditor();
        }
    }

    /// <summary>
    /// Patches the single created/updated row into the already-loaded list in place, re-sorted to
    /// match the server's StartTime-then-Id order — cheaper than a second round trip to re-fetch the
    /// whole list just to show one changed row (the mutation response already carries it).
    /// </summary>
    private void ReplaceWindow(TimeWindowDto window)
    {
        _windows = _windows!
            .Where(w => w.Id != window.Id)
            .Append(window)
            .OrderBy(w => w.StartTime)
            .ThenBy(w => w.Id)
            .ToList();
    }
}
