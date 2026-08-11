using cinescout.contracts;

namespace cinescout.web.Client.Pages;

public partial class SeatMatrices
{
    private SeatMatricesPageDto? _page;
    private bool _isOverridesTab;

    private int? _filterCinemaId;
    private int? _filterRoomId;

    private int? _editingId;
    private int? _editorCinemaId;
    private int? _editorRoomId;
    private int? _editorFilmId;
    private string _name = "";
    private string _rowStart = "";
    private string _rowEnd = "";
    private int? _seatNumberStart;
    private int? _seatNumberEnd;
    private int? _partySize;

    private string? _error;
    private bool _saving;

    private bool FilterActive => _filterCinemaId is not null || _filterRoomId is not null;

    private IReadOnlyList<RoomOptionDto> FilterRoomOptions => RoomsForCinema(_filterCinemaId);

    private IReadOnlyList<RoomOptionDto> FilteredRooms => FilterRoomOptions
        .Where(r => _filterRoomId is null || r.Id == _filterRoomId)
        .ToList();

    private IReadOnlyList<SeatMatrixDto> FilteredOverrideMatrices => _page!.Matrices
        .Where(m => m.FilmId is not null)
        .Where(m => _filterCinemaId is null || RoomOf(m.RoomId)?.CinemaId == _filterCinemaId)
        .Where(m => _filterRoomId is null || m.RoomId == _filterRoomId)
        .ToList();

    private int TotalCount => _isOverridesTab ? _page!.Matrices.Count(m => m.FilmId is not null) : _page!.Rooms.Count;
    private int FilteredCount => _isOverridesTab ? FilteredOverrideMatrices.Count : FilteredRooms.Count;

    private IReadOnlyList<RoomOptionDto> EditorRoomOptions => RoomsForCinema(_editorCinemaId);

    private string EditorTitle => _editingId is not null
        ? "Edit seat matrix"
        : (_isOverridesTab ? "New film override" : "New general matrix");

    protected override async Task OnInitializedAsync()
    {
        if (RendererInfo.IsInteractive)
        {
            _page = await Api.GetPageAsync();
        }
    }

    private RoomOptionDto? RoomOf(int roomId) => _page!.Rooms.FirstOrDefault(r => r.Id == roomId);

    private string CinemaNameOf(int cinemaId) => _page!.Cinemas.FirstOrDefault(c => c.Id == cinemaId)?.Name ?? "";

    private string FilmTitleOf(int filmId) => _page!.Films.FirstOrDefault(f => f.Id == filmId)?.Title ?? "";

    private List<string> OverrideFilmTitlesFor(int roomId) => _page!.Matrices
        .Where(m => m.RoomId == roomId && m.FilmId is not null)
        .Select(m => FilmTitleOf(m.FilmId!.Value))
        .ToList();

    // Resets the editor on every switch, not just tab bookkeeping: SaveAsync derives FilmId from
    // _isOverridesTab, so leaving an in-progress edit open across a tab switch would let a
    // film-specific override's edit silently save as a general matrix (or vice versa) on the same
    // row — the Film select isn't even visible once the tab flips to hint that anything changed.
    private void SwitchTab(bool overrides)
    {
        _isOverridesTab = overrides;
        ResetEditor();
    }

    private void ClearFilters()
    {
        _filterCinemaId = null;
        _filterRoomId = null;
    }

    private IReadOnlyList<RoomOptionDto> RoomsForCinema(int? cinemaId) =>
        _page!.Rooms.Where(r => cinemaId is null || r.CinemaId == cinemaId).ToList();

    private int? ResetRoomIfWrongCinema(int? roomId, int? cinemaId) =>
        roomId is { } id && RoomOf(id)?.CinemaId != cinemaId ? null : roomId;

    private void OnFilterCinemaChanged() => _filterRoomId = ResetRoomIfWrongCinema(_filterRoomId, _filterCinemaId);

    private void OnEditorCinemaChanged() => _editorRoomId = ResetRoomIfWrongCinema(_editorRoomId, _editorCinemaId);

    private void StartEdit(SeatMatrixDto matrix)
    {
        _error = null;
        _editingId = matrix.Id;
        _editorCinemaId = RoomOf(matrix.RoomId)?.CinemaId;
        _editorRoomId = matrix.RoomId;
        _editorFilmId = matrix.FilmId;
        _name = matrix.Name;
        _rowStart = matrix.RowStart;
        _rowEnd = matrix.RowEnd;
        _seatNumberStart = matrix.SeatNumberStart;
        _seatNumberEnd = matrix.SeatNumberEnd;
        _partySize = matrix.PartySize;
    }

    private void StartAddForRoom(RoomOptionDto room)
    {
        ResetEditor();
        _editorCinemaId = room.CinemaId;
        _editorRoomId = room.Id;
    }

    private void ResetEditor()
    {
        _error = null;
        _editingId = null;
        _editorCinemaId = null;
        _editorRoomId = null;
        _editorFilmId = null;
        _name = "";
        _rowStart = "";
        _rowEnd = "";
        _seatNumberStart = null;
        _seatNumberEnd = null;
        _partySize = null;
    }

    private async Task SaveAsync()
    {
        if (_editorRoomId is not { } roomId)
        {
            _error = "Select a room.";
            return;
        }

        if (_isOverridesTab && _editorFilmId is null)
        {
            _error = "Select a film for a film-specific override.";
            return;
        }

        if (_seatNumberStart is not { } seatStart || _seatNumberEnd is not { } seatEnd || _partySize is not { } partySize)
        {
            _error = "Seat numbers and party size are required.";
            return;
        }

        var request = new SeatMatrixWriteRequest
        {
            RoomId = roomId,
            FilmId = _isOverridesTab ? _editorFilmId : null,
            // Trimmed, not raw: RowStart/RowEnd feed ZoneSeatGrid's Matrix.RowEnd[0] - Matrix.RowStart[0]
            // row-count math directly, so stray whitespace (a trailing space pasted in) would corrupt
            // it silently — the deleted static-SSR page trimmed all three for the same reason.
            Name = _name.Trim(),
            RowStart = _rowStart.Trim(),
            RowEnd = _rowEnd.Trim(),
            SeatNumberStart = seatStart,
            SeatNumberEnd = seatEnd,
            PartySize = partySize,
        };

        if (SeatMatrixValidation.Validate(request) is { } validationError)
        {
            _error = validationError;
            return;
        }

        _error = null;
        _saving = true;

        var (matrix, problem) = _editingId is { } id
            ? await Api.UpdateAsync(id, request)
            : await Api.CreateAsync(request);

        _saving = false;

        if (problem is not null)
        {
            _error = problem.Detail ?? problem.Title ?? "Save failed.";
            return;
        }

        if (matrix is not null)
        {
            ReplaceMatrix(matrix);
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

        RemoveMatrix(id);

        if (_editingId == id)
        {
            ResetEditor();
        }
    }

    private async Task ToggleAsync(int id)
    {
        _error = null;

        var (matrix, problem) = await Api.ToggleEnabledAsync(id);
        if (problem is not null)
        {
            _error = problem.Detail ?? problem.Title ?? "Update failed.";
            return;
        }

        if (matrix is not null)
        {
            ReplaceMatrix(matrix);
        }
    }

    private void ReplaceMatrix(SeatMatrixDto matrix)
    {
        var matrices = _page!.Matrices.Where(m => m.Id != matrix.Id).Append(matrix).OrderBy(m => m.Id).ToList();
        _page = _page with { Matrices = matrices };
    }

    private void RemoveMatrix(int id)
    {
        _page = _page! with { Matrices = _page.Matrices.Where(m => m.Id != id).ToList() };
    }
}
