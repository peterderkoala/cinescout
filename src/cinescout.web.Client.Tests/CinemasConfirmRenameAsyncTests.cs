using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using cinescout.contracts;
using cinescout.web.Client.Api;
using cinescout.web.Client.Pages;

namespace cinescout.web.Client.Tests;

/// <summary>
/// Coverage for <see cref="Cinemas.ConfirmRenameAsync"/> (#111 — CRAP-score reduction, wayfinder map
/// #105). No bUnit per this repo's convention (#81): the component is constructed directly
/// (<c>ComponentBase</c>'s implicit parameterless constructor). Contrary to this ticket's original
/// playbook assumption, the Razor-SDK-generated <c>Api</c> property from <c>@inject</c> is actually
/// <c>private</c> in this SDK version (verified by reflecting over the compiled
/// <c>cinescout.web.Client.dll</c> — <c>[Inject] private CinemasApiClient Api { get; set; }</c>), so
/// it — along with <c>ConfirmRenameAsync</c> itself, also private — is reached via reflection, same as
/// the other private fields, rather than widening visibility in production code (sibling tickets
/// #109/#110 are editing the same production files in parallel worktrees).
/// </summary>
public sealed class CinemasConfirmRenameAsyncTests
{
    private static readonly PropertyInfo ApiProperty = typeof(Cinemas).GetProperty("Api", BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("Property Api not found on Cinemas.");

    private static readonly MethodInfo ConfirmRenameAsyncMethod = typeof(Cinemas).GetMethod("ConfirmRenameAsync", BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("Method ConfirmRenameAsync not found on Cinemas.");

    private static readonly FieldInfo SelectedIdField = GetField("_selectedId");
    private static readonly FieldInfo SelectedCinemaField = GetField("_selectedCinema");
    private static readonly FieldInfo RenamingRoomIdField = GetField("_renamingRoomId");
    private static readonly FieldInfo RenameValueField = GetField("_renameValue");
    private static readonly FieldInfo ErrorField = GetField("_error");
    private static readonly FieldInfo ErrorClassField = GetField("_errorClass");

    private static FieldInfo GetField(string name) =>
        typeof(Cinemas).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException($"Field {name} not found on Cinemas.");

    private static Task InvokeConfirmRenameAsync(Cinemas component, int roomId) =>
        (Task)ConfirmRenameAsyncMethod.Invoke(component, [roomId])!;

    private static CinemaDetailDto MakeCinema(params RoomDto[] rooms) => new()
    {
        Id = 1,
        Name = "Test Cinema",
        ExternalCinemaId = "ext-1",
        CrawlBaseUrl = "https://example.test",
        IsActive = true,
        Rooms = rooms,
    };

    private static RoomDto MakeRoom(int id, string name) => new()
    {
        Id = id,
        CinemaId = 1,
        ExternalAuditoriumId = $"aud-{id}",
        Name = name,
    };

    private static Cinemas CreateComponent(HttpMessageHandler handler)
    {
        var component = new Cinemas();
        ApiProperty.SetValue(component, new CinemasApiClient(
            new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") },
            new AntiforgeryTokenStore()));
        return component;
    }

    /// <summary>
    /// Fails the test loudly if the code under test ever calls the HTTP layer — used for the
    /// early-return branch, where <c>Api</c> must never be invoked.
    /// </summary>
    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException($"Unexpected HTTP call: {request.Method} {request.RequestUri}");
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(respond(request));
        }
    }

    [Fact]
    public async Task ConfirmRenameAsync_NoSelectedCinema_ReturnsWithoutCallingApiOrChangingState()
    {
        var component = CreateComponent(new ThrowingHandler());
        SelectedIdField.SetValue(component, null);
        RenamingRoomIdField.SetValue(component, 42);
        ErrorField.SetValue(component, "unchanged");

        await InvokeConfirmRenameAsync(component, 5);

        Assert.Equal(42, RenamingRoomIdField.GetValue(component));
        Assert.Equal("unchanged", ErrorField.GetValue(component));
    }

    [Fact]
    public async Task ConfirmRenameAsync_Success_ClearsRenamingIdAndPatchesRoomInPlace()
    {
        var renamedRoom = MakeRoom(2, "New Name");
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(renamedRoom),
        });
        var component = CreateComponent(handler);

        var cinema = MakeCinema(MakeRoom(1, "Room One"), MakeRoom(2, "Old Name"));
        SelectedIdField.SetValue(component, 1);
        SelectedCinemaField.SetValue(component, cinema);
        RenamingRoomIdField.SetValue(component, 2);
        RenameValueField.SetValue(component, "New Name");

        await InvokeConfirmRenameAsync(component, 2);

        Assert.Null(RenamingRoomIdField.GetValue(component));

        var updatedCinema = Assert.IsType<CinemaDetailDto>(SelectedCinemaField.GetValue(component), exactMatch: false);
        Assert.Equal(2, updatedCinema.Rooms.Count);
        Assert.Equal("Room One", updatedCinema.Rooms.Single(r => r.Id == 1).Name);
        Assert.Equal("New Name", updatedCinema.Rooms.Single(r => r.Id == 2).Name);

        Assert.NotNull(handler.LastRequest);
        Assert.Equal(HttpMethod.Put, handler.LastRequest!.Method);
        Assert.Equal("http://localhost/api/cinemas/1/rooms/2", handler.LastRequest.RequestUri!.ToString());
    }

    [Fact]
    public async Task ConfirmRenameAsync_SuccessButSelectedCinemaBecameNull_ClearsRenamingIdWithoutPatching()
    {
        // Edge case in the boolean condition (`room is not null && _selectedCinema is not null`):
        // if the selection was cleared while the rename round trip was in flight, no patch happens.
        // Worth its own test since it's a distinct branch outcome (renaming id still clears, but
        // _selectedCinema stays null rather than being reconstructed from nothing).
        var renamedRoom = MakeRoom(2, "New Name");
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(renamedRoom),
        });
        var component = CreateComponent(handler);

        SelectedIdField.SetValue(component, 1);
        SelectedCinemaField.SetValue(component, null);
        RenamingRoomIdField.SetValue(component, 2);
        RenameValueField.SetValue(component, "New Name");

        await InvokeConfirmRenameAsync(component, 2);

        Assert.Null(RenamingRoomIdField.GetValue(component));
        Assert.Null(SelectedCinemaField.GetValue(component));
    }

    [Fact]
    public async Task ConfirmRenameAsync_ApiReturnsProblem_SetsErrorAndClearsRenamingIdRegardless()
    {
        var problem = new ApiProblem("Rename Title", "Rename Detail", null);
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = JsonContent.Create(problem),
        });
        var component = CreateComponent(handler);

        var cinema = MakeCinema(MakeRoom(2, "Old Name"));
        SelectedIdField.SetValue(component, 1);
        SelectedCinemaField.SetValue(component, cinema);
        RenamingRoomIdField.SetValue(component, 2);
        RenameValueField.SetValue(component, "Bad Name");

        await InvokeConfirmRenameAsync(component, 2);

        Assert.Null(RenamingRoomIdField.GetValue(component));
        Assert.Equal("alert-danger", ErrorClassField.GetValue(component));
        Assert.Equal("Rename Detail", ErrorField.GetValue(component));

        // Rooms list untouched since the call failed.
        var untouchedCinema = Assert.IsType<CinemaDetailDto>(SelectedCinemaField.GetValue(component), exactMatch: false);
        Assert.Equal("Old Name", untouchedCinema.Rooms.Single().Name);
    }

    [Fact]
    public async Task ConfirmRenameAsync_ApiReturnsProblemWithNoDetail_FallsBackToTitle()
    {
        var problem = new ApiProblem("Rename Title Only", null, null);
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = JsonContent.Create(problem),
        });
        var component = CreateComponent(handler);

        SelectedIdField.SetValue(component, 1);
        SelectedCinemaField.SetValue(component, MakeCinema(MakeRoom(2, "Old Name")));
        RenamingRoomIdField.SetValue(component, 2);
        RenameValueField.SetValue(component, "Bad Name");

        await InvokeConfirmRenameAsync(component, 2);

        Assert.Equal("Rename Title Only", ErrorField.GetValue(component));
    }

    [Fact]
    public async Task ConfirmRenameAsync_ApiReturnsProblemWithNoTitleOrDetail_FallsBackToDefaultMessage()
    {
        var problem = new ApiProblem(null, null, null);
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = JsonContent.Create(problem),
        });
        var component = CreateComponent(handler);

        SelectedIdField.SetValue(component, 1);
        SelectedCinemaField.SetValue(component, MakeCinema(MakeRoom(2, "Old Name")));
        RenamingRoomIdField.SetValue(component, 2);
        RenameValueField.SetValue(component, "Bad Name");

        await InvokeConfirmRenameAsync(component, 2);

        Assert.Equal("Rename failed.", ErrorField.GetValue(component));
    }
}
