using cinescout.core.Kinoheld;

namespace cinescout.core.Tests;

public class KinoheldCircuitBreakerTests
{
    [Fact]
    public void First_trip_wins_and_stays_tripped()
    {
        var breaker = new KinoheldCircuitBreaker();
        Assert.False(breaker.IsTripped);

        breaker.Trip("first");
        var firstAt = breaker.TrippedAt;
        breaker.Trip("second");

        Assert.True(breaker.IsTripped);
        Assert.Equal("first", breaker.TripReason);
        Assert.Equal(firstAt, breaker.TrippedAt);
    }
}
