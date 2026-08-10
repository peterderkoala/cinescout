using cinescout.contracts;

namespace cinescout.contracts.Tests;

public class DayCodeConverterTests
{
    [Fact]
    public void ToDayCodes_EmitsCodesInFixedOrder_RegardlessOfFlagBitOrder()
    {
        // Bits set out of "natural" order (Friday, then Monday, then Wednesday) — output must still be Mo, We, Fr.
        var days = Weekday.Friday | Weekday.Monday | Weekday.Wednesday;

        var codes = DayCodeConverter.ToDayCodes(days);

        Assert.Equal(["Mo", "We", "Fr"], codes);
    }

    [Fact]
    public void ToDayCodes_AllDays_EmitsFullFixedOrder()
    {
        var days = Weekday.Monday | Weekday.Tuesday | Weekday.Wednesday | Weekday.Thursday
            | Weekday.Friday | Weekday.Saturday | Weekday.Sunday;

        var codes = DayCodeConverter.ToDayCodes(days);

        Assert.Equal(["Mo", "Tu", "We", "Th", "Fr", "Sa", "Su"], codes);
    }

    [Fact]
    public void ToDayCodes_None_EmitsEmptyArray()
    {
        var codes = DayCodeConverter.ToDayCodes(Weekday.None);

        Assert.Empty(codes);
    }

    [Theory]
    [InlineData(new object[] { new[] { "Mo", "We", "Fr" } })]
    [InlineData(new object[] { new[] { "Fr", "Mo", "We" } })]
    [InlineData(new object[] { new[] { "We", "Fr", "Mo" } })]
    public void FromDayCodes_IsOrderIndependent(string[] codes)
    {
        var result = DayCodeConverter.FromDayCodes(codes);

        Assert.Equal(Weekday.Monday | Weekday.Wednesday | Weekday.Friday, result);
    }

    [Fact]
    public void FromDayCodes_EmptyInput_ReturnsNone()
    {
        var result = DayCodeConverter.FromDayCodes([]);

        Assert.Equal(Weekday.None, result);
    }

    [Fact]
    public void FromDayCodes_UnrecognizedCode_Throws()
    {
        Assert.Throws<ArgumentException>(() => DayCodeConverter.FromDayCodes(["Mo", "Xx"]));
    }

    [Fact]
    public void FromDayCodes_NullInput_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => DayCodeConverter.FromDayCodes(null!));
    }

    [Fact]
    public void FromDayCodes_DuplicateCode_IsHarmless()
    {
        var result = DayCodeConverter.FromDayCodes(["Mo", "Mo"]);

        Assert.Equal(Weekday.Monday, result);
    }

    [Theory]
    [InlineData("mo")]
    [InlineData("MO")]
    [InlineData("Mo ")]
    [InlineData(" Mo")]
    public void FromDayCodes_WrongCaseOrWhitespace_Throws(string code)
    {
        Assert.Throws<ArgumentException>(() => DayCodeConverter.FromDayCodes([code]));
    }

    [Theory]
    [InlineData(Weekday.None)]
    [InlineData(Weekday.Monday)]
    [InlineData(Weekday.Monday | Weekday.Wednesday | Weekday.Friday)]
    [InlineData(Weekday.Saturday | Weekday.Sunday)]
    public void RoundTrip_ToDayCodes_ThenFromDayCodes_IsLossless(Weekday original)
    {
        var codes = DayCodeConverter.ToDayCodes(original);
        var roundTripped = DayCodeConverter.FromDayCodes(codes);

        Assert.Equal(original, roundTripped);
    }

    [Fact]
    public void RoundTrip_FromDayCodes_ThenToDayCodes_NormalizesToFixedOrder()
    {
        var shuffledInput = new[] { "Su", "Mo", "Fr" };

        var flags = DayCodeConverter.FromDayCodes(shuffledInput);
        var normalized = DayCodeConverter.ToDayCodes(flags);

        Assert.Equal(["Mo", "Fr", "Su"], normalized);
    }
}
