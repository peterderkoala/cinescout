using cinescout.contracts;
using cinescout.model;
using cinescout.web.Client.Shared;

namespace cinescout.web.Client.Pages;

public partial class Notifications
{
    private static readonly (NotificationTypeFlags Flag, string Label)[] AllTypes =
    [
        (NotificationTypeFlags.TrackStarted, "Track started"),
        (NotificationTypeFlags.TrackStopped, "Track stopped"),
        (NotificationTypeFlags.RulesMatched, "Rules matched"),
        (NotificationTypeFlags.SeatAvailabilityChanged, "Seat availability"),
        (NotificationTypeFlags.NewFilmAdded, "New film"),
    ];

    private IReadOnlyList<NotificationChannelDto>? _channels;
    private NotificationChannelDto? _selected;
    private int? _selectedId;

    private string _name = "";
    private string _webhookUrl = "";
    private string _displayName = "";
    private string _avatarUrl = "";
    private string _prefix = "";
    private bool _suppress;
    private bool _isEnabled = true;
    private NotificationTypeFlags _types = NotificationTypeFlags.All;

    private string? _error;
    private bool _saving;
    private bool _testing;
    private (bool Success, string Text)? _testResult;

    protected override async Task OnInitializedAsync()
    {
        _channels = await Api.GetListAsync();
        if (_channels.Count > 0)
        {
            Select(_channels[0]);
        }
    }

    private static IEnumerable<string> TypeLabels(NotificationTypeFlags types) =>
        types == NotificationTypeFlags.All ? ["All types"]
        : types == NotificationTypeFlags.None ? ["No types"]
        : AllTypes.Where(t => types.HasFlag(t.Flag)).Select(t => t.Label);

    private static string StatusColor(NotificationChannelDto channel) =>
        channel.LastError is not null ? "var(--bs-warning)" : channel.IsEnabled ? "var(--bs-success)" : "var(--bs-border-color)";

    private static string LastDelivery(NotificationChannelDto channel) => channel.LastDeliveryStatus is { } status
        ? $"{status} · {RelativeTimeFormatter.Format(channel.LastDeliveryAt, DateTimeOffset.UtcNow)}"
        : "No deliveries yet";

    private void Select(NotificationChannelDto channel) => LoadForm(channel);

    private void StartAdd() => LoadForm(null);

    /// <summary>Fills the edit card from <paramref name="channel"/>, or with a new channel's defaults (all types, enabled) when null.</summary>
    private void LoadForm(NotificationChannelDto? channel)
    {
        ResetMessages();
        _selectedId = channel?.Id;
        _selected = channel;
        _name = channel?.Name ?? "";
        _webhookUrl = "";
        _displayName = channel?.DisplayName ?? "";
        _avatarUrl = channel?.AvatarUrl ?? "";
        _prefix = channel?.Prefix ?? "";
        _suppress = channel?.Suppress ?? false;
        _isEnabled = channel?.IsEnabled ?? true;
        _types = channel?.Types ?? NotificationTypeFlags.All;
    }

    private void ResetMessages()
    {
        _error = null;
        _testResult = null;
    }

    private void ToggleType(NotificationTypeFlags flag, bool on) => _types = on ? _types | flag : _types & ~flag;

    private NotificationChannelWriteRequest BuildRequest() => new()
    {
        Name = _name,
        WebhookUrl = _webhookUrl,
        DisplayName = _displayName,
        AvatarUrl = _avatarUrl,
        Prefix = _prefix,
        Suppress = _suppress,
        IsEnabled = _isEnabled,
        Types = _types,
    };

    /// <summary>Immediate feedback before the round trip — the same NotificationChannelValidation the server uses.</summary>
    private bool TryValidate(NotificationChannelWriteRequest request)
    {
        _error = NotificationChannelValidation.Validate(request, webhookUrlRequired: _selectedId is null);
        return _error is null;
    }

    private async Task SaveAsync()
    {
        ResetMessages();
        var request = BuildRequest();
        if (!TryValidate(request))
        {
            return;
        }

        _saving = true;
        var (channel, problem) = _selectedId is { } id ? await Api.UpdateAsync(id, request) : await Api.CreateAsync(request);
        _saving = false;

        if (problem is not null)
        {
            _error = problem.MessageOr("Save failed.");
            return;
        }

        _channels = await Api.GetListAsync();
        if (channel is not null)
        {
            Select(channel);
        }
    }

    private async Task SendTestAsync()
    {
        ResetMessages();
        var request = BuildRequest();
        if (!TryValidate(request))
        {
            return;
        }

        _testing = true;
        var (result, problem) = await Api.TestAsync(_selectedId, request);
        _testing = false;

        _testResult = problem is not null ? (false, problem.MessageOr("Test failed."))
            : result is { Success: true } ? (true, "Sent ✓")
            // A 429's detail is already prose ("Rate limited by Discord, …"); other statuses read "400 Bad Request: {body}".
            : result is { HttpStatus: not (null or 429) } ? (false, $"Discord returned {result.Detail}")
            : (false, result?.Detail ?? "Test failed.");
    }

    private async Task DeleteAsync()
    {
        if (_selectedId is not { } id)
        {
            return;
        }

        ResetMessages();
        var problem = await Api.DeleteAsync(id);
        if (problem is not null)
        {
            _error = problem.MessageOr("Delete failed.");
            return;
        }

        _channels = await Api.GetListAsync();
        if (_channels.Count > 0)
        {
            Select(_channels[0]);
        }
        else
        {
            StartAdd();
        }
    }
}
