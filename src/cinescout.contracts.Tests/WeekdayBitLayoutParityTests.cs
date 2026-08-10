using cinescout.contracts;
using cinescout.model;

namespace cinescout.contracts.Tests;

/// <summary>
/// <see cref="Weekday"/>'s doc comment promises its bit layout stays identical to
/// <see cref="DaysOfWeekFlags"/>'s so callers (e.g. cinescout.web) can convert between the two with
/// a plain <c>(Weekday)(int)value</c> cast. cinescout.contracts itself can't reference
/// cinescout.model to enforce that at compile time (true leaf), but this test project already
/// references cinescout.model for <c>Room</c> sample data, so it can pin the parity here — if either
/// enum's bit shifts ever drift apart, this breaks instead of the cast silently mismapping days.
/// </summary>
public class WeekdayBitLayoutParityTests
{
    [Theory]
    [InlineData(Weekday.None, DaysOfWeekFlags.None)]
    [InlineData(Weekday.Monday, DaysOfWeekFlags.Monday)]
    [InlineData(Weekday.Tuesday, DaysOfWeekFlags.Tuesday)]
    [InlineData(Weekday.Wednesday, DaysOfWeekFlags.Wednesday)]
    [InlineData(Weekday.Thursday, DaysOfWeekFlags.Thursday)]
    [InlineData(Weekday.Friday, DaysOfWeekFlags.Friday)]
    [InlineData(Weekday.Saturday, DaysOfWeekFlags.Saturday)]
    [InlineData(Weekday.Sunday, DaysOfWeekFlags.Sunday)]
    public void Weekday_And_DaysOfWeekFlags_ShareTheSameBitValue(Weekday contractsValue, DaysOfWeekFlags modelValue)
    {
        Assert.Equal((int)modelValue, (int)contractsValue);
    }

    [Fact]
    public void CombinedFlags_CastCleanlyBetweenTheTwoEnums()
    {
        var modelValue = DaysOfWeekFlags.Monday | DaysOfWeekFlags.Wednesday | DaysOfWeekFlags.Friday;

        var contractsValue = (Weekday)(int)modelValue;

        Assert.Equal(Weekday.Monday | Weekday.Wednesday | Weekday.Friday, contractsValue);
        Assert.Equal(modelValue, (DaysOfWeekFlags)(int)contractsValue);
    }
}
