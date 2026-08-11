using cinescout.contracts;
using cinescout.web.Client.Api;

namespace cinescout.web.Client.Pages;

public partial class Cinemas
{
    private IReadOnlyList<CinemaListItemDto>? _cinemas;
    private CinemaDetailDto? _selectedCinema;
    private int? _selectedId;

    private string _name = "";
    private string _externalCinemaId = "";
    private string _crawlBaseUrl = "";
    private bool _isActive = true;

    private string? _error;
    private string _errorClass = "alert-danger";
    private bool _saving;
    private bool _reseeding;

    private int? _renamingRoomId;
    private string _renameValue = "";

    protected override async Task OnInitializedAsync()
    {
        if (RendererInfo.IsInteractive)
        {
            await LoadCinemasAsync();
        }
    }

    private async Task LoadCinemasAsync()
    {
        var cinemas = await Api.GetListAsync();

        if (cinemas.Count > 0)
        {
            _selectedId = cinemas[0].Id;
            _selectedCinema = await Api.GetDetailAsync(cinemas[0].Id);
            SyncEditFieldsFromSelection();
        }

        _cinemas = cinemas;
    }

    private async Task SelectCinemaAsync(int id)
    {
        _error = null;
        _renamingRoomId = null;
        _selectedId = id;
        _selectedCinema = await Api.GetDetailAsync(id);
        SyncEditFieldsFromSelection();
    }

    private void SyncEditFieldsFromSelection()
    {
        _name = _selectedCinema?.Name ?? "";
        _externalCinemaId = _selectedCinema?.ExternalCinemaId ?? "";
        _crawlBaseUrl = _selectedCinema?.CrawlBaseUrl ?? "";
        _isActive = _selectedCinema?.IsActive ?? true;
    }

    private void StartAddCinema()
    {
        _error = null;
        _renamingRoomId = null;
        _selectedId = null;
        _selectedCinema = null;
        _name = "";
        _externalCinemaId = "";
        _crawlBaseUrl = "";
        _isActive = true;
    }

    /// <summary>
    /// Immediate client-side feedback before the round trip — delegates to the same
    /// CinemaValidation.Validate the server uses, so the two can't silently drift.
    /// </summary>
    private bool TryValidate(CinemaWriteRequest request, out string error)
    {
        if (CinemaValidation.Validate(request) is { } validationError)
        {
            error = validationError;
            return false;
        }

        error = "";
        return true;
    }

    private async Task SaveAsync()
    {
        _errorClass = "alert-danger";

        var request = new CinemaWriteRequest { Name = _name, ExternalCinemaId = _externalCinemaId, CrawlBaseUrl = _crawlBaseUrl, IsActive = _isActive };
        if (!TryValidate(request, out var validationError))
        {
            _error = validationError;
            return;
        }

        _error = null;
        _saving = true;

        var (cinema, problem) = _selectedId is { } id
            ? await Api.UpdateAsync(id, request)
            : await Api.CreateAsync(request);

        _saving = false;

        if (problem is not null)
        {
            _error = problem.Detail ?? problem.Title ?? "Save failed.";
            return;
        }

        _cinemas = await Api.GetListAsync();
        if (cinema is not null)
        {
            _selectedId = cinema.Id;
            _selectedCinema = cinema;
            SyncEditFieldsFromSelection();
        }
    }

    private async Task DeleteAsync()
    {
        if (_selectedId is not { } id)
        {
            return;
        }

        _errorClass = "alert-danger";
        _error = null;

        var problem = await Api.DeleteAsync(id);
        if (problem is not null)
        {
            _error = problem.Detail ?? problem.Title ?? "Delete failed.";
            return;
        }

        _cinemas = await Api.GetListAsync();
        if (_cinemas.Count > 0)
        {
            await SelectCinemaAsync(_cinemas[0].Id);
        }
        else
        {
            StartAddCinema();
        }
    }

    private async Task ReseedRoomsAsync()
    {
        if (_selectedId is not { } id)
        {
            return;
        }

        _error = null;
        _reseeding = true;

        var (cinema, problem) = await Api.ReseedRoomsAsync(id);

        _reseeding = false;

        if (problem is not null)
        {
            var alert = KinoheldStatusParser.Parse(problem.KinoheldStatus);
            _errorClass = alert.AlertClass;
            _error = problem.KinoheldStatus is null ? (problem.Detail ?? alert.Message) : alert.Message;
            return;
        }

        if (cinema is not null)
        {
            _selectedCinema = cinema;
            _cinemas = await Api.GetListAsync();
        }
    }

    private void StartRename(RoomDto room)
    {
        _renamingRoomId = room.Id;
        _renameValue = room.Name;
    }

    private void CancelRename() => _renamingRoomId = null;

    private async Task ConfirmRenameAsync(int roomId)
    {
        if (_selectedId is not { } cinemaId)
        {
            return;
        }

        var (room, problem) = await Api.RenameRoomAsync(cinemaId, roomId, _renameValue);
        _renamingRoomId = null;

        if (problem is not null)
        {
            _errorClass = "alert-danger";
            _error = problem.Detail ?? problem.Title ?? "Rename failed.";
            return;
        }

        // The response already carries the renamed room — patch it into the loaded list in place
        // instead of a second round trip to re-fetch the whole cinema just to show one new name.
        if (room is not null && _selectedCinema is not null)
        {
            _selectedCinema = _selectedCinema with
            {
                Rooms = _selectedCinema.Rooms.Select(r => r.Id == room.Id ? room : r).ToList(),
            };
        }
    }
}
