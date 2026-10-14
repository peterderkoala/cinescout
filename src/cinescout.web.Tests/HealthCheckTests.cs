using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace cinescout.web.Tests;

public sealed class HealthCheckTests : IClassFixture<HealthCheckTests.Factory>
{
    private readonly Factory _factory;

    public HealthCheckTests(Factory factory) => _factory = factory;

    // The "Testing" environment skips Hangfire wiring entirely, so no HangfireHeartbeatHealthCheck
    // is registered — /healthz should still respond 200 (an empty check set aggregates healthy),
    // not throw, and doesn't need a real Postgres since nothing on this path touches the DbContext.
    [Fact]
    public async Task Healthz_InTestingEnvironment_ReturnsHealthy()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/healthz");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    public sealed class Factory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) =>
            builder.UseEnvironment("Testing");
    }
}
