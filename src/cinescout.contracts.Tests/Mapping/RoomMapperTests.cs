using cinescout.model;
using cinescout.web.Mapping;

namespace cinescout.contracts.Tests.Mapping;

/// <summary>
/// Proves the Room -> RoomDto Mapperly convention (<c>cinescout.web.Mapping.RoomMapper</c>) against
/// real sample values. The mapper itself lives in cinescout.web, not here: cinescout.contracts is a
/// true leaf and can never see <c>Room</c>, so the mapper needs a project that can see both
/// <c>Room</c> and <c>RoomDto</c>, which cinescout.web already is. This test project references
/// cinescout.web the same way cinescout.web.Tests already does.
/// </summary>
public class RoomMapperTests
{
    [Fact]
    public void ToDto_MapsAllFieldsOneToOne()
    {
        var room = new Room
        {
            Id = 7,
            CinemaId = 3,
            ExternalAuditoriumId = "kino-1",
            Name = "Kino 1",
        };

        var dto = RoomMapper.ToDto(room);

        Assert.Equal(room.Id, dto.Id);
        Assert.Equal(room.CinemaId, dto.CinemaId);
        Assert.Equal(room.ExternalAuditoriumId, dto.ExternalAuditoriumId);
        Assert.Equal(room.Name, dto.Name);
    }

    [Fact]
    public void ToDto_DifferentRoom_MapsIndependently()
    {
        var room = new Room
        {
            Id = 42,
            CinemaId = 1,
            ExternalAuditoriumId = "saal-7",
            Name = "Kino 7",
        };

        var dto = RoomMapper.ToDto(room);

        Assert.Equal(42, dto.Id);
        Assert.Equal(1, dto.CinemaId);
        Assert.Equal("saal-7", dto.ExternalAuditoriumId);
        Assert.Equal("Kino 7", dto.Name);
    }
}
