using System.Net.Http.Json;

namespace cinescout.core.HallOfFame;

/// <summary>
/// Real HTTP implementation of <see cref="IHallOfFameClient"/>, registered as a typed client.
/// </summary>
public class HallOfFameClient(HttpClient httpClient) : IHallOfFameClient
{
    public async Task<HallOfFameScheduleResponse> GetScheduleAsync(string baseUrl, CancellationToken cancellationToken)
    {
        var response = await httpClient.GetFromJsonAsync<HallOfFameScheduleResponse>(baseUrl, cancellationToken);

        if (response is null)
        {
            throw new InvalidOperationException($"Hall-of-Fame schedule request to '{baseUrl}' returned no content.");
        }

        return response;
    }
}
