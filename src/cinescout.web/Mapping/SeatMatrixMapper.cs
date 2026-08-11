using cinescout.contracts;
using cinescout.model;
using Riok.Mapperly.Abstractions;

namespace cinescout.web.Mapping;

/// <summary>A second 1:1 Mapperly mapping, following <see cref="RoomMapper"/>'s exact shape: <c>FavoriteSeatMatrix</c> to <c>SeatMatrixDto</c> — every field lines up by name and type.</summary>
[Mapper]
public static partial class SeatMatrixMapper
{
    public static partial SeatMatrixDto ToDto(FavoriteSeatMatrix matrix);
}
