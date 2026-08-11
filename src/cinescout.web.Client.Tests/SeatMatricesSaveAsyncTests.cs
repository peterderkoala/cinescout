using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using cinescout.contracts;
using cinescout.web.Client.Api;
using cinescout.web.Client.Pages;

namespace cinescout.web.Client.Tests;

/// <summary>
/// Covers <c>SeatMatrices.SaveAsync()</c> (issue #109 — CRAP-score reduction, wayfinder map #105):
/// the code-behind private state is set via reflection (the field names it targets are pinned below),
/// and the HTTP layer is faked with a custom <see cref="HttpMessageHandler"/> since
/// <see cref="SeatMatricesApiClient"/> has no interface and this test project has no mocking library.
/// No bUnit — this repo's deliberate convention for <c>cinescout.web.Client.Tests</c>.
/// </summary>
public sealed class SeatMatricesSaveAsyncTests
{
    private static readonly SeatMatricesPageDto EmptyPage = new()
    {
        Cinemas = [],
        Rooms = [],
        Films = [],
        Matrices = [],
    };

    [Fact]
    public async Task SaveAsync_NoRoomSelected_SetsErrorAndDoesNotCallApi()
    {
        var component = CreateComponent(new ThrowingHandler());
        SetPage(component, EmptyPage);
        SetField<int?>(component, "_editorRoomId", null);

        await InvokeSaveAsync(component);

        Assert.Equal("Select a room.", GetField<string?>(component, "_error"));
    }

    [Fact]
    public async Task SaveAsync_OverridesTabNoFilmSelected_SetsErrorAndDoesNotCallApi()
    {
        var component = CreateComponent(new ThrowingHandler());
        SetPage(component, EmptyPage);
        SetField(component, "_editorRoomId", 1);
        SetField(component, "_isOverridesTab", true);
        SetField<int?>(component, "_editorFilmId", null);

        await InvokeSaveAsync(component);

        Assert.Equal("Select a film for a film-specific override.", GetField<string?>(component, "_error"));
    }

    [Fact]
    public async Task SaveAsync_MissingSeatNumbersOrPartySize_SetsErrorAndDoesNotCallApi()
    {
        var component = CreateComponent(new ThrowingHandler());
        SetPage(component, EmptyPage);
        SetField(component, "_editorRoomId", 1);
        SetField(component, "_isOverridesTab", false);
        SetField<int?>(component, "_seatNumberStart", null);
        SetField(component, "_seatNumberEnd", 10);
        SetField(component, "_partySize", 2);

        await InvokeSaveAsync(component);

        Assert.Equal("Seat numbers and party size are required.", GetField<string?>(component, "_error"));
    }

    [Fact]
    public async Task SaveAsync_ValidationRejectsRequest_SetsErrorToValidationMessageAndDoesNotCallApi()
    {
        var component = CreateComponent(new ThrowingHandler());
        SetPage(component, EmptyPage);
        SetField(component, "_editorRoomId", 1);
        SetField(component, "_isOverridesTab", false);
        SetField(component, "_name", ""); // blank name fails SeatMatrixValidation first
        SetField(component, "_rowStart", "A");
        SetField(component, "_rowEnd", "C");
        SetField(component, "_seatNumberStart", 1);
        SetField(component, "_seatNumberEnd", 10);
        SetField(component, "_partySize", 2);

        await InvokeSaveAsync(component);

        Assert.Equal("Name is required.", GetField<string?>(component, "_error"));
    }

    [Fact]
    public async Task SaveAsync_CreatePath_CallsCreateAsyncMergesMatrixAndResetsEditor()
    {
        var returned = new SeatMatrixDto
        {
            Id = 42,
            RoomId = 1,
            FilmId = null,
            Name = "Sweet spot",
            RowStart = "A",
            RowEnd = "C",
            SeatNumberStart = 1,
            SeatNumberEnd = 10,
            PartySize = 2,
            IsEnabled = true,
        };

        var handler = new RecordingHandler(HttpStatusCode.OK, JsonContent.Create(returned));
        var component = CreateComponent(handler);
        SetPage(component, EmptyPage);
        SetField<int?>(component, "_editingId", null);
        SetField(component, "_editorRoomId", 1);
        SetField(component, "_isOverridesTab", false);
        SetField(component, "_name", "Sweet spot");
        SetField(component, "_rowStart", "A");
        SetField(component, "_rowEnd", "C");
        SetField(component, "_seatNumberStart", 1);
        SetField(component, "_seatNumberEnd", 10);
        SetField(component, "_partySize", 2);

        await InvokeSaveAsync(component);

        Assert.NotNull(handler.LastRequest);
        Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
        Assert.Equal("/api/seat-matrices", handler.LastRequest.RequestUri!.AbsolutePath);

        var page = GetField<SeatMatricesPageDto?>(component, "_page");
        Assert.NotNull(page);
        Assert.Contains(page!.Matrices, m => m.Id == 42 && m.Name == "Sweet spot");

        Assert.Null(GetField<string?>(component, "_error"));
        Assert.Null(GetField<int?>(component, "_editingId"));
        Assert.Equal("", GetField<string>(component, "_name"));
        Assert.Null(GetField<int?>(component, "_editorRoomId"));
        Assert.Null(GetField<int?>(component, "_seatNumberStart"));
        Assert.Null(GetField<int?>(component, "_seatNumberEnd"));
        Assert.Null(GetField<int?>(component, "_partySize"));
    }

    [Fact]
    public async Task SaveAsync_UpdatePath_CallsUpdateAsyncInsteadOfCreateAsync()
    {
        var existing = new SeatMatrixDto
        {
            Id = 7,
            RoomId = 1,
            FilmId = null,
            Name = "Old name",
            RowStart = "A",
            RowEnd = "B",
            SeatNumberStart = 1,
            SeatNumberEnd = 5,
            PartySize = 1,
            IsEnabled = true,
        };
        var updated = existing with { Name = "New name" };

        var handler = new RecordingHandler(HttpStatusCode.OK, JsonContent.Create(updated));
        var component = CreateComponent(handler);
        SetPage(component, EmptyPage with { Matrices = [existing] });
        SetField(component, "_editingId", 7);
        SetField(component, "_editorRoomId", 1);
        SetField(component, "_isOverridesTab", false);
        SetField(component, "_name", "New name");
        SetField(component, "_rowStart", "A");
        SetField(component, "_rowEnd", "B");
        SetField(component, "_seatNumberStart", 1);
        SetField(component, "_seatNumberEnd", 5);
        SetField(component, "_partySize", 1);

        await InvokeSaveAsync(component);

        Assert.NotNull(handler.LastRequest);
        Assert.Equal(HttpMethod.Put, handler.LastRequest!.Method);
        Assert.Equal("/api/seat-matrices/7", handler.LastRequest.RequestUri!.AbsolutePath);

        var page = GetField<SeatMatricesPageDto?>(component, "_page");
        Assert.NotNull(page);
        Assert.Contains(page!.Matrices, m => m.Id == 7 && m.Name == "New name");
        Assert.Null(GetField<int?>(component, "_editingId"));
    }

    [Fact]
    public async Task SaveAsync_ApiReturnsProblemWithDetail_SetsErrorFromDetailAndDoesNotResetEditor()
    {
        var problem = new { title = "Bad request", detail = "Room already has a general matrix.", kinoheldStatus = (string?)null };
        var handler = new RecordingHandler(HttpStatusCode.BadRequest, JsonContent.Create(problem));
        var component = CreateComponent(handler);
        SetPage(component, EmptyPage);
        SetField<int?>(component, "_editingId", null);
        SetField(component, "_editorRoomId", 1);
        SetField(component, "_isOverridesTab", false);
        SetField(component, "_name", "Sweet spot");
        SetField(component, "_rowStart", "A");
        SetField(component, "_rowEnd", "C");
        SetField(component, "_seatNumberStart", 1);
        SetField(component, "_seatNumberEnd", 10);
        SetField(component, "_partySize", 2);

        await InvokeSaveAsync(component);

        Assert.Equal("Room already has a general matrix.", GetField<string?>(component, "_error"));
        // Editor state untouched — user's in-progress input is preserved on failure.
        Assert.Equal("Sweet spot", GetField<string>(component, "_name"));
        Assert.Equal(1, GetField<int?>(component, "_editorRoomId"));
    }

    [Fact]
    public async Task SaveAsync_ApiReturnsProblemWithNoDetailOrTitle_SetsGenericErrorMessage()
    {
        var handler = new RecordingHandler(HttpStatusCode.InternalServerError, content: null);
        var component = CreateComponent(handler);
        SetPage(component, EmptyPage);
        SetField<int?>(component, "_editingId", null);
        SetField(component, "_editorRoomId", 1);
        SetField(component, "_isOverridesTab", false);
        SetField(component, "_name", "Sweet spot");
        SetField(component, "_rowStart", "A");
        SetField(component, "_rowEnd", "C");
        SetField(component, "_seatNumberStart", 1);
        SetField(component, "_seatNumberEnd", 10);
        SetField(component, "_partySize", 2);

        await InvokeSaveAsync(component);

        // No body at all -> ApiProblem(response.ReasonPhrase, null, null); a bare 500 with no reason
        // phrase set by the fake handler leaves Title null/empty too, so the "Save failed." fallback fires.
        var error = GetField<string?>(component, "_error");
        Assert.False(string.IsNullOrEmpty(error));
    }

    // --- helpers -----------------------------------------------------------------------------

    private static SeatMatrices CreateComponent(HttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") };
        var api = new SeatMatricesApiClient(httpClient, new AntiforgeryTokenStore());
        var component = new SeatMatrices();

        // The Razor-SDK-generated `[Inject]` property for `@inject SeatMatricesApiClient Api` is emitted
        // `private` (not public, contrary to older Razor SDK versions) — set it via reflection, same as
        // every other private field this test touches.
        var apiProperty = typeof(SeatMatrices).GetProperty("Api", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("Api property not found via reflection.");
        apiProperty.SetValue(component, api);

        return component;
    }

    private static Task InvokeSaveAsync(SeatMatrices component)
    {
        var method = typeof(SeatMatrices).GetMethod("SaveAsync", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("SaveAsync method not found via reflection.");
        return (Task)method.Invoke(component, null)!;
    }

    private static void SetPage(SeatMatrices component, SeatMatricesPageDto page) => SetField(component, "_page", page);

    private static void SetField<T>(SeatMatrices component, string fieldName, T value)
    {
        var field = typeof(SeatMatrices).GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException($"Field '{fieldName}' not found via reflection.");
        field.SetValue(component, value);
    }

    private static T GetField<T>(SeatMatrices component, string fieldName)
    {
        var field = typeof(SeatMatrices).GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException($"Field '{fieldName}' not found via reflection.");
        return (T)field.GetValue(component)!;
    }

    /// <summary>Safety net for the early-return branches: fails the test loudly if SaveAsync ever reaches the API call.</summary>
    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("SaveAsync should have returned before calling the API.");
    }

    /// <summary>Records the last outgoing request and returns one canned response, regardless of method/URI.</summary>
    private sealed class RecordingHandler(HttpStatusCode statusCode, HttpContent? content) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            var response = new HttpResponseMessage(statusCode) { Content = content };
            return Task.FromResult(response);
        }
    }
}
