using cinescout.contracts;
using cinescout.model;
using Riok.Mapperly.Abstractions;

namespace cinescout.web.Mapping;

/// <summary>
/// The worked 1:1 Mapperly example issue #89 asks for: <c>Room</c> (cinescout.model) to
/// <c>RoomDto</c> (cinescout.contracts). Every field lines up by name and type, so Mapperly needs
/// no configuration beyond the attribute.
///
/// Every <c>[Mapper]</c> lives in cinescout.web/Mapping; later endpoint tickets add further mappers for
/// their own entity/DTO pairs here, following this exact shape.
/// </summary>
[Mapper]
public static partial class RoomMapper
{
    public static partial RoomDto ToDto(Room room);
}
