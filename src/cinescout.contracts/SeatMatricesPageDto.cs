namespace cinescout.contracts;

/// <summary>Filter-bar/editor cinema option (§5.2) — a purpose-cut projection, not <c>CinemaListItemDto</c>: this screen's `Endpoints/` file is self-contained per #83 and doesn't need the Cinemas screen's extra fields (RoomCount, IsActive, ExternalCinemaId).</summary>
public sealed record CinemaOptionDto
{
    public required int Id { get; init; }
    public required string Name { get; init; }
}

/// <summary>Filter-bar/editor room option (§5.2) — also drives the General tab's per-room card iteration. Purpose-cut like <see cref="CinemaOptionDto"/>: not <c>RoomDto</c>, which carries an ExternalAuditoriumId this screen never shows.</summary>
public sealed record RoomOptionDto
{
    public required int Id { get; init; }
    public required int CinemaId { get; init; }
    public required string Name { get; init; }
}

/// <summary>Editor's Film select option, Overrides tab only (§5.2).</summary>
public sealed record FilmOptionDto
{
    public required int Id { get; init; }
    public required string Title { get; init; }
}

/// <summary>
/// The Seat Matrices screen's one read model (#83's "per-screen read model" convention) — a single
/// GET populates both pills, the filter bar, and the editor's selects; the pill switch and the
/// cinema/room filter are both client-side-only over this one payload (§5.2: "Filter bar
/// (client-side, applies to both tabs)"), so no further round trip is needed to switch tabs or filter.
/// </summary>
public sealed record SeatMatricesPageDto
{
    public required IReadOnlyList<CinemaOptionDto> Cinemas { get; init; }
    public required IReadOnlyList<RoomOptionDto> Rooms { get; init; }
    public required IReadOnlyList<FilmOptionDto> Films { get; init; }
    public required IReadOnlyList<SeatMatrixDto> Matrices { get; init; }
}
