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

    private void Select(NotificationChannelDto channel)
    {
        ResetMessages();
        _selectedId = channel.Id;
        _selected = channel;
        _name = channel.Name;
        _webhookUrl = "";
        _displayName = channel.DisplayName ?? "";
        _avatarUrl = channel.AvatarUrl ?? "";
        _prefix = channel.Prefix ?? "";
        _suppress = channel.Suppress;
        _isEnabled = channel.IsEnabled;
        _types = channel.Types;
    }

    private void StartAdd()
    {
        ResetMessages();
        _selectedId = null;
        _selected = null;
        _name = "";
        _webhookUrl = "";
        _displayName = "";
        _avatarUrl = "";
        _prefix = "";
        _suppress = false;
        _isEnabled = true;
        _types = NotificationTypeFlags.All;
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
            : (false, result?.Detail is { } detail && detail.Length > 0 && char.IsDigit(detail[0]) ? $"Discord returned {detail}" : result?.Detail ?? "Test failed.");
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
