using System.Net.Http.Json;
using System.Text.Json;
using HighwaySpeedEnforcement.Client.Application;
using HighwaySpeedEnforcement.Processor.Contracts.Rest;

namespace HighwaySpeedEnforcement.Client.Infrastructure;

/// <summary>
/// Adapter of <see cref="ISnapshotClient"/> over <c>GET api/traffic-snapshot</c> (<c>docs/contracts/rest-api.md</c>). The
/// <see cref="HttpClient"/> is injected with its base address set; every failure becomes <see cref="SnapshotUnavailableException"/>
/// with a message short enough for the status bar, except cancellation requested by the caller.
/// </summary>
public sealed class HttpSnapshotClient : ISnapshotClient
{
    public const string Route = "api/traffic-snapshot";

    private readonly HttpClient _http;

    public HttpSnapshotClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<TrafficSnapshot> GetSnapshotAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _http.GetAsync(Route, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                throw new SnapshotUnavailableException($"processor answered {(int)response.StatusCode} {response.ReasonPhrase}".TrimEnd());
            }

            var dto = await response.Content.ReadFromJsonAsync(RestJsonContext.Default.TrafficSnapshotDto, cancellationToken)
                ?? throw new SnapshotUnavailableException("response body is empty");

            return SnapshotMapper.ToModel(dto);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException exception)
        {
            // HttpClient.Timeout cancels the request without the caller's token.
            throw new SnapshotUnavailableException("request timed out", exception);
        }
        catch (HttpRequestException exception)
        {
            throw new SnapshotUnavailableException($"processor unreachable ({exception.HttpRequestError})", exception);
        }
        catch (JsonException exception)
        {
            throw new SnapshotUnavailableException("response is not a valid snapshot", exception);
        }
    }
}
