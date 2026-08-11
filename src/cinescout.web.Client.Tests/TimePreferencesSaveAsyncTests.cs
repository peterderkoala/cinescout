using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using cinescout.contracts;
using cinescout.web.Client.Api;
using cinescout.web.Client.Pages;

namespace cinescout.web.Client.Tests;

/// <summary>
/// Covers <c>TimePreferences.SaveAsync()</c>'s branches (issue #110 — CRAP-score reduction, map #105).
/// No bUnit (this repo's deliberate convention, #81): the component is constructed directly (Razor
/// components get an implicit public parameterless constructor via <c>ComponentBase</c>). Note this
/// SDK's Razor compiler (net10.0) generates <em>every</em> <c>@code</c>/<c>@inject</c> member —
/// including <c>Api</c> and <c>SaveAsync</c> itself — as <c>private</c>, not the historically-public
/// injected-property shape; both the editor-state fields and <c>Api</c> are set via reflection, and
/// <c>SaveAsync</c> is invoked via reflection too. <c>SaveAsync</c> itself never touches
/// <c>RendererInfo</c>, so it's safe to call without attaching the component to a real renderer.
/// </summary>
public sealed class TimePreferencesSaveAsyncTests
{
    // ApiClientBase serializes outgoing request bodies with System.Net.Http.Json's default options
    // (camelCase property names), so reading them back into the PascalCase-property request records
    // needs case-insensitive matching.
    private static readonly JsonSerializerOptions CaseInsensitive = new() { PropertyNameCaseInsensitive = true };

    private static readonly TimeOnly DefaultStart = new(18, 0);
    private static readonly TimeOnly DefaultEnd = new(22, 0);

    [Fact]
    public async Task SaveAsync_MissingStartTime_SetsErrorAndDoesNotCallApi()
    {
        var (component, _) = CreateComponent(FailingHandler());
        SetField<TimeOnly?>(component, "_startTime", null);
        SetField(component, "_endTime", DefaultEnd);
        SetField(component, "_windows", new List<TimeWindowDto>());

        await InvokeSaveAsync(component);

        Assert.Equal("Enter a valid start and end time.", GetField<string?>(component, "_error"));
    }

    [Fact]
    public async Task SaveAsync_MissingEndTime_SetsErrorAndDoesNotCallApi()
    {
        var (component, _) = CreateComponent(FailingHandler());
        SetField(component, "_startTime", DefaultStart);
        SetField<TimeOnly?>(component, "_endTime", null);
        SetField(component, "_windows", new List<TimeWindowDto>());

        await InvokeSaveAsync(component);

        Assert.Equal("Enter a valid start and end time.", GetField<string?>(component, "_error"));
    }

    [Fact]
    public async Task SaveAsync_NoDaysSelected_SetsValidationErrorAndDoesNotCallApi()
    {
        var (component, _) = CreateComponent(FailingHandler());
        SetField(component, "_startTime", DefaultStart);
        SetField(component, "_endTime", DefaultEnd);
        SetField(component, "_selectedDayCodes", new HashSet<string>());
        SetField(component, "_windows", new List<TimeWindowDto>());

        await InvokeSaveAsync(component);

        Assert.Equal("Select at least one day.", GetField<string?>(component, "_error"));
    }

    [Fact]
    public async Task SaveAsync_EndTimeNotAfterStartTime_SetsValidationErrorAndDoesNotCallApi()
    {
        var (component, _) = CreateComponent(FailingHandler());
        SetField(component, "_startTime", DefaultEnd);
        SetField(component, "_endTime", DefaultStart);
        SetField(component, "_selectedDayCodes", new HashSet<string> { "Mo" });
        SetField(component, "_windows", new List<TimeWindowDto>());

        await InvokeSaveAsync(component);

        Assert.Equal("End time must be later than start time.", GetField<string?>(component, "_error"));
    }

    [Fact]
    public async Task SaveAsync_CreatePath_PostsAndMergesWindowSortedAndResetsEditor()
    {
        var created = new TimeWindowDto { Id = 5, DayCodes = ["Mo"], StartTime = new TimeOnly(9, 0), EndTime = new TimeOnly(10, 0) };
        var existing = new TimeWindowDto { Id = 1, DayCodes = ["Tu"], StartTime = new TimeOnly(12, 0), EndTime = new TimeOnly(13, 0) };

        var (component, handler) = CreateComponent(SuccessHandler(created));
        // Deliberately not DefaultStart/DefaultEnd: ResetEditor() resets to those exact same
        // constants, so priming with them would make the post-save "reset" assertions below pass
        // even if ResetEditor were never called.
        var editedStart = new TimeOnly(10, 0);
        var editedEnd = new TimeOnly(11, 0);
        SetField(component, "_startTime", editedStart);
        SetField(component, "_endTime", editedEnd);
        SetField(component, "_selectedDayCodes", new HashSet<string> { "Mo" });
        SetField<int?>(component, "_editingId", null);
        SetField(component, "_windows", new List<TimeWindowDto> { existing });

        await InvokeSaveAsync(component);

        Assert.Null(GetField<string?>(component, "_error"));
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("http://localhost/api/time-preferences", request.RequestUri!.ToString());

        // Confirms the request actually carries the edited window, not just that some POST landed
        // on the right route — method/URL alone can't distinguish that bug from a correct one.
        var sentBody = JsonSerializer.Deserialize<TimeWindowWriteRequest>(handler.RequestBodies[request]!, CaseInsensitive);
        Assert.Equal(["Mo"], sentBody!.DayCodes);
        Assert.Equal(editedStart, sentBody.StartTime);
        Assert.Equal(editedEnd, sentBody.EndTime);

        // TimeWindowDto is a record whose DayCodes field is a string[] — record-generated equality
        // compares arrays by reference, not content, and the "created" window round-tripped through
        // JSON (a new array instance) — so assert field-by-field instead of via record Equals/Assert.Equal.
        var windows = GetField<IReadOnlyList<TimeWindowDto>?>(component, "_windows");
        Assert.NotNull(windows);
        Assert.Equal(2, windows!.Count);
        Assert.Equal(5, windows[0].Id);
        Assert.Equal(new TimeOnly(9, 0), windows[0].StartTime);
        Assert.Equal(["Mo"], windows[0].DayCodes);
        Assert.Equal(1, windows[1].Id);
        Assert.Equal(new TimeOnly(12, 0), windows[1].StartTime);
        Assert.Equal(["Tu"], windows[1].DayCodes);

        Assert.Null(GetField<int?>(component, "_editingId"));
        Assert.Empty(GetField<HashSet<string>>(component, "_selectedDayCodes")!);
        Assert.Equal(DefaultStart, GetField<TimeOnly?>(component, "_startTime"));
        Assert.Equal(DefaultEnd, GetField<TimeOnly?>(component, "_endTime"));
        Assert.False(GetField<bool>(component, "_saving"));
    }

    [Fact]
    public async Task SaveAsync_UpdatePath_CallsUpdateAsyncInsteadOfCreate()
    {
        var updated = new TimeWindowDto { Id = 5, DayCodes = ["Mo"], StartTime = new TimeOnly(9, 0), EndTime = new TimeOnly(10, 0) };

        var (component, handler) = CreateComponent(SuccessHandler(updated));
        SetField(component, "_startTime", DefaultStart);
        SetField(component, "_endTime", DefaultEnd);
        SetField(component, "_selectedDayCodes", new HashSet<string> { "Mo" });
        SetField(component, "_editingId", 5);
        SetField(component, "_windows", new List<TimeWindowDto>());

        await InvokeSaveAsync(component);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Put, request.Method);
        Assert.Equal("http://localhost/api/time-preferences/5", request.RequestUri!.ToString());
        Assert.Null(GetField<int?>(component, "_editingId"));
    }

    [Fact]
    public async Task SaveAsync_ApiReturnsProblemWithDetail_SetsErrorFromDetailAndDoesNotResetEditor()
    {
        var (component, _) = CreateComponent(ProblemHandler(HttpStatusCode.BadRequest, "Bad Title", "Bad Detail"));
        // Deliberately not DefaultStart/DefaultEnd, same reasoning as the create-path test: priming
        // with the exact values ResetEditor() would reset to makes the "untouched on failure"
        // assertions below pass even if ResetEditor() were incorrectly called on this path.
        var editedStart = new TimeOnly(10, 0);
        var editedEnd = new TimeOnly(11, 0);
        var editedDays = new HashSet<string> { "Mo" };
        SetField(component, "_startTime", editedStart);
        SetField(component, "_endTime", editedEnd);
        SetField(component, "_selectedDayCodes", editedDays);
        SetField(component, "_editingId", 7);
        SetField(component, "_windows", new List<TimeWindowDto>());

        await InvokeSaveAsync(component);

        Assert.Equal("Bad Detail", GetField<string?>(component, "_error"));
        // Editor state untouched — user's in-progress input is preserved on failure. Re-checks every
        // field the setup above primed, not just a couple of them, since a regression that reset
        // only some fields on the failure path would otherwise slip through undetected.
        Assert.Equal(7, GetField<int?>(component, "_editingId"));
        Assert.Equal(editedStart, GetField<TimeOnly?>(component, "_startTime"));
        Assert.Equal(editedEnd, GetField<TimeOnly?>(component, "_endTime"));
        Assert.Equal(editedDays, GetField<HashSet<string>>(component, "_selectedDayCodes"));
        Assert.False(GetField<bool>(component, "_saving"));
    }

    [Fact]
    public async Task SaveAsync_ApiReturnsProblemWithOnlyTitle_SetsErrorFromTitle()
    {
        var (component, _) = CreateComponent(ProblemHandler(HttpStatusCode.BadRequest, "Only Title", detail: null));
        SetField(component, "_startTime", DefaultStart);
        SetField(component, "_endTime", DefaultEnd);
        SetField(component, "_selectedDayCodes", new HashSet<string> { "Mo" });
        SetField(component, "_windows", new List<TimeWindowDto>());

        await InvokeSaveAsync(component);

        Assert.Equal("Only Title", GetField<string?>(component, "_error"));
    }

    [Fact]
    public async Task SaveAsync_ApiReturnsProblemWithNoTitleOrDetail_SetsFallbackErrorMessage()
    {
        var (component, _) = CreateComponent(ProblemHandler(HttpStatusCode.BadRequest, title: null, detail: null));
        SetField(component, "_startTime", DefaultStart);
        SetField(component, "_endTime", DefaultEnd);
        SetField(component, "_selectedDayCodes", new HashSet<string> { "Mo" });
        SetField(component, "_windows", new List<TimeWindowDto>());

        await InvokeSaveAsync(component);

        Assert.Equal("Save failed.", GetField<string?>(component, "_error"));
    }

    private static (TimePreferences Component, FakeHttpMessageHandler Handler) CreateComponent(FakeHttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        var api = new TimePreferencesApiClient(httpClient, new AntiforgeryTokenStore());
        var component = new TimePreferences();
        SetApi(component, api);
        return (component, handler);
    }

    private static void SetApi(TimePreferences component, TimePreferencesApiClient api)
    {
        var property = typeof(TimePreferences).GetProperty("Api", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException($"Property 'Api' not found on {nameof(TimePreferences)}.");
        property.SetValue(component, api);
    }

    private static async Task InvokeSaveAsync(TimePreferences component)
    {
        var method = typeof(TimePreferences).GetMethod("SaveAsync", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException($"Method 'SaveAsync' not found on {nameof(TimePreferences)}.");
        await (Task)method.Invoke(component, null)!;
    }

    private static FakeHttpMessageHandler SuccessHandler(TimeWindowDto response) =>
        new(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(response) });

    private static FakeHttpMessageHandler ProblemHandler(HttpStatusCode statusCode, string? title, string? detail) =>
        new(_ => new HttpResponseMessage(statusCode) { Content = JsonContent.Create(new ApiProblem(title, detail, null)) });

    private static FakeHttpMessageHandler FailingHandler() =>
        new(_ => throw new InvalidOperationException("Api should not be called for this branch of SaveAsync."));

    private static void SetField<T>(TimePreferences component, string fieldName, T value)
    {
        var field = typeof(TimePreferences).GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException($"Field '{fieldName}' not found on {nameof(TimePreferences)}.");
        field.SetValue(component, value);
    }

    private static T GetField<T>(TimePreferences component, string fieldName)
    {
        var field = typeof(TimePreferences).GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException($"Field '{fieldName}' not found on {nameof(TimePreferences)}.");
        return (T)field.GetValue(component)!;
    }

    private sealed class FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        /// <summary>
        /// Bodies read eagerly here, not lazily off a captured <see cref="HttpRequestMessage"/> — <see
        /// cref="ApiClientBase"/> wraps its outgoing request in a <c>using</c>, so by the time a test
        /// assertion runs after <c>await</c>ing the call, its <c>Content</c> is already disposed.
        /// Keyed by reference to the same request stored in <see cref="Requests"/>.
        /// </summary>
        public Dictionary<HttpRequestMessage, string?> RequestBodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            RequestBodies[request] = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return respond(request);
        }
    }
}
