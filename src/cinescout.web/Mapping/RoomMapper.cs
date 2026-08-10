using cinescout.contracts;
using cinescout.model;
using Riok.Mapperly.Abstractions;

namespace cinescout.web.Mapping;

/// <summary>
/// The worked 1:1 Mapperly example issue #89 asks for: <c>Room</c> (cinescout.model) to
/// <c>RoomDto</c> (cinescout.contracts). Every field lines up by name and type, so Mapperly needs
/// no configuration beyond the attribute.
///
/// Lives here, not in cinescout.contracts: that project is a true leaf and must never reference
/// cinescout.model, so a mapper needing both <c>Room</c> and <c>RoomDto</c> visible in the same
/// compilation can't be defined there. cinescout.web already references both cinescout.contracts
/// directly and cinescout.model transitively (via cinescout.persistence/cinescout.core), so it's
/// the natural home — and the one later endpoint tickets should extend with further `[Mapper]`
/// classes for their own entity/DTO pairs, following this exact shape.
/// </summary>
[Mapper]
public static partial class RoomMapper
{
    public static partial RoomDto ToDto(Room room);
}
