namespace cinescout.contracts.Tests;

public class ValidationTests
{
    private static readonly CinemaWriteRequest ValidCinema = new()
    {
        Name = "Kino", ExternalCinemaId = "123", CrawlBaseUrl = "https://www.kinoheld.de/kino/x", IsActive = true,
    };

    private static readonly SeatMatrixWriteRequest ValidMatrix = new()
    {
        RoomId = 1, Name = "Middle", RowStart = "E", RowEnd = "H", SeatNumberStart = 5, SeatNumberEnd = 12, PartySize = 2,
    };

    private static readonly TimeWindowWriteRequest ValidWindow = new()
    {
        DayCodes = ["Mo"], StartTime = new TimeOnly(18, 0), EndTime = new TimeOnly(23, 0),
    };

    [Fact]
    public void Validate_ValidRequests_ReturnNull()
    {
        Assert.Null(CinemaValidation.Validate(ValidCinema));
        Assert.Null(SeatMatrixValidation.Validate(ValidMatrix));
        Assert.Null(TimeWindowValidation.Validate(ValidWindow));
    }

    [Theory]
    [InlineData(" ", "123", "https://x.de", "Name is required.")]
    [InlineData("Kino", "", "https://x.de", "External cinema id is required.")]
    [InlineData("Kino", "123", "", "Crawl base URL must be a valid http(s) URL.")]
    [InlineData("Kino", "123", "not a url", "Crawl base URL must be a valid http(s) URL.")]
    [InlineData("Kino", "123", "ftp://x.de", "Crawl base URL must be a valid http(s) URL.")]
    public void CinemaValidate_InvalidField_ReturnsMessage(string name, string externalId, string url, string expected) =>
        Assert.Equal(expected, CinemaValidation.Validate(
            ValidCinema with { Name = name, ExternalCinemaId = externalId, CrawlBaseUrl = url }));

    [Fact]
    public void CinemaValidate_HttpUrl_IsAccepted() =>
        Assert.Null(CinemaValidation.Validate(ValidCinema with { CrawlBaseUrl = "http://x.de" }));

    [Fact]
    public void SeatMatrixValidate_BlankName_ReturnsMessage() =>
        Assert.Equal("Name is required.", SeatMatrixValidation.Validate(ValidMatrix with { Name = "" }));

    [Theory]
    [InlineData("", "H")]
    [InlineData("E", " ")]
    public void SeatMatrixValidate_MissingRowBound_ReturnsMessage(string start, string end) =>
        Assert.Equal("Both row bounds are required.",
            SeatMatrixValidation.Validate(ValidMatrix with { RowStart = start, RowEnd = end }));

    [Fact]
    public void SeatMatrixValidate_ReversedRows_ReturnsMessage() =>
        Assert.Equal("Row from must not come after row to.",
            SeatMatrixValidation.Validate(ValidMatrix with { RowStart = "H", RowEnd = "E" }));

    [Fact]
    public void SeatMatrixValidate_SingleRowAndSeat_IsAccepted() =>
        Assert.Null(SeatMatrixValidation.Validate(
            ValidMatrix with { RowStart = "E", RowEnd = "E", SeatNumberStart = 7, SeatNumberEnd = 7 }));

    [Theory]
    [InlineData(0, 5)]
    [InlineData(6, 5)]
    public void SeatMatrixValidate_BadSeatRange_ReturnsMessage(int start, int end) =>
        Assert.Equal("Seat numbers must be at least 1, with seat from not after seat to.",
            SeatMatrixValidation.Validate(ValidMatrix with { SeatNumberStart = start, SeatNumberEnd = end }));

    [Fact]
    public void SeatMatrixValidate_PartySizeBelowOne_ReturnsMessage() =>
        Assert.Equal("Party size must be at least 1.", SeatMatrixValidation.Validate(ValidMatrix with { PartySize = 0 }));

    [Fact]
    public void TimeWindowValidate_NoDays_ReturnsMessage() =>
        Assert.Equal("Select at least one day.", TimeWindowValidation.Validate(ValidWindow with { DayCodes = [] }));

    [Theory]
    [InlineData(18, 18)]
    [InlineData(18, 17)]
    public void TimeWindowValidate_EndNotAfterStart_ReturnsMessage(int startHour, int endHour) =>
        Assert.Equal("End time must be later than start time.", TimeWindowValidation.Validate(
            ValidWindow with { StartTime = new TimeOnly(startHour, 0), EndTime = new TimeOnly(endHour, 0) }));
}
