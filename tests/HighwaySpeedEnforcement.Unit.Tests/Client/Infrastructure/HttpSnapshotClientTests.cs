using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using HighwaySpeedEnforcement.Client.Application;
using HighwaySpeedEnforcement.Client.Infrastructure;
using HighwaySpeedEnforcement.Processor.Contracts.Rest;
using HighwaySpeedEnforcement.Unit.Tests.Contracts;

namespace HighwaySpeedEnforcement.Unit.Tests.Client.Infrastructure;

public sealed class HttpSnapshotClientTests
{
    private const string BaseUrl = "http://127.0.0.1:8080";

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    [Trait("BR", "BR-API-01")]
    public async Task Approved_example_maps_to_the_model()
    {
        var client = Client(Json(ContractExamples.Read("traffic-snapshot.json")));

        var snapshot = await client.GetSnapshotAsync(Token);

        Assert.Equal(new DateTimeOffset(2026, 10, 14, 14, 3, 10, TimeSpan.Zero), snapshot.GeneratedAt);
        Assert.Equal(TimeSpan.Zero, snapshot.GeneratedAt.Offset);
        Assert.Equal(Enumerable.Range(1, 10), snapshot.CameraBoards.Select(board => board.CameraId));

        var camera1 = snapshot.CameraBoards[0];
        Assert.Equal(new DateTimeOffset(2026, 10, 14, 14, 3, 9, 400, TimeSpan.Zero), camera1.LatestEvent);
        Assert.Equal(
            [
                new CameraEntry(1, "4S7 1234", 152.4f, new DateTimeOffset(2026, 10, 14, 14, 3, 9, 398, TimeSpan.Zero)),
                new CameraEntry(2, "2AB 9876", 149.8f, new DateTimeOffset(2026, 10, 14, 14, 3, 9, 400, TimeSpan.Zero)),
            ],
            camera1.Entries);

        Assert.Empty(snapshot.CameraBoards[2].Entries);
        Assert.Equal(new DateTimeOffset(2026, 10, 14, 14, 1, 2, TimeSpan.Zero), snapshot.CameraBoards[6].LatestEvent);
        Assert.Null(snapshot.CameraBoards[9].LatestEvent);
        Assert.Empty(snapshot.CameraBoards[9].Entries);

        Assert.Equal(new DateTimeOffset(2026, 10, 14, 14, 3, 9, 700, TimeSpan.Zero), snapshot.GlobalBoard.LatestEvent);
        Assert.Equal(
            [
                new GlobalEntry(1, "4S7 1234", 144.35, 6, new DateTimeOffset(2026, 10, 14, 14, 3, 9, 398, TimeSpan.Zero)),
                new GlobalEntry(2, "2AB 9876", 143.9, 10, new DateTimeOffset(2026, 10, 14, 14, 3, 8, 500, TimeSpan.Zero)),
            ],
            snapshot.GlobalBoard.Entries);
        Assert.All(snapshot.GlobalBoard.Entries, entry => Assert.Equal(TimeSpan.Zero, entry.MeasuredAt.Offset));
    }

    [Fact]
    [Trait("BR", "BR-API-01")]
    public async Task Requests_the_snapshot_route_with_get()
    {
        HttpRequestMessage? seen = null;
        var client = Client(request =>
        {
            seen = request;
            return Json(ContractExamples.Read("traffic-snapshot.json"));
        });

        await client.GetSnapshotAsync(Token);

        Assert.NotNull(seen);
        Assert.Equal(HttpMethod.Get, seen.Method);
        Assert.Equal(new Uri($"{BaseUrl}/api/traffic-snapshot"), seen.RequestUri);
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable, "503")]
    [InlineData(HttpStatusCode.InternalServerError, "500")]
    [Trait("BR", "BR-UI-03")]
    public async Task Non_success_status_with_problem_details_is_unavailable(HttpStatusCode status, string expectedInMessage)
    {
        const string problem = """{"type":"about:blank","title":"Not ready","status":503,"detail":"The pipeline is starting."}""";
        var client = Client(new HttpResponseMessage(status)
        {
            Content = new StringContent(problem, Encoding.UTF8, "application/problem+json"),
        });

        var exception = await Assert.ThrowsAsync<SnapshotUnavailableException>(() => client.GetSnapshotAsync(Token));

        Assert.Contains(expectedInMessage, exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("{ this is not json")]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("""{"generatedAtUtc":"2026-10-14T14:03:10Z"}""")]
    [InlineData("""{"generatedAtUtc":"2026-10-14T14:03:10Z","cameraBoards":[],"globalBoard":{"latestEventUtc":null,"entries":[]}}""")]
    [Trait("BR", "BR-UI-03")]
    public async Task Malformed_or_incomplete_body_is_unavailable(string body)
    {
        var client = Client(Json(body));

        await Assert.ThrowsAsync<SnapshotUnavailableException>(() => client.GetSnapshotAsync(Token));
    }

    [Fact]
    [Trait("BR", "BR-UI-03")]
    public async Task Nine_camera_boards_are_unavailable()
    {
        var example = JsonSerializer.Deserialize(ContractExamples.Read("traffic-snapshot.json"), RestJsonContext.Default.TrafficSnapshotDto)!;
        var nineBoards = example with { CameraBoards = example.CameraBoards.Take(9).ToArray() };
        var client = Client(Json(JsonSerializer.Serialize(nineBoards, RestJsonContext.Default.TrafficSnapshotDto)));

        var exception = await Assert.ThrowsAsync<SnapshotUnavailableException>(() => client.GetSnapshotAsync(Token));

        Assert.Contains("9 camera boards", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("BR", "BR-UI-03")]
    public async Task Network_error_is_unavailable_and_keeps_the_cause()
    {
        var cause = new HttpRequestException(HttpRequestError.ConnectionError, "No connection could be made.");
        var client = Client(_ => throw cause);

        var exception = await Assert.ThrowsAsync<SnapshotUnavailableException>(() => client.GetSnapshotAsync(Token));

        Assert.Contains("ConnectionError", exception.Message, StringComparison.Ordinal);
        Assert.Same(cause, exception.InnerException);
    }

    [Fact]
    [Trait("BR", "BR-UI-03")]
    public async Task Timeout_of_the_http_client_itself_is_unavailable()
    {
        // HttpClient.Timeout surfaces as a TaskCanceledException while the caller's token is not cancelled.
        var client = Client(_ => throw new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout.", new TimeoutException()));

        var exception = await Assert.ThrowsAsync<SnapshotUnavailableException>(() => client.GetSnapshotAsync(Token));

        Assert.Contains("timed out", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Caller_cancellation_propagates_unwrapped()
    {
        using var cancellation = new CancellationTokenSource();
        var client = Client(async (request, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return Json("{}");
        });

        var pending = client.GetSnapshotAsync(cancellation.Token);
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
    }

    private static HttpResponseMessage Json(string body)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, new MediaTypeHeaderValue("application/json", "utf-8")),
        };
    }

    private static HttpSnapshotClient Client(HttpResponseMessage response)
    {
        return Client(_ => response);
    }

    private static HttpSnapshotClient Client(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        return Client((request, _) => Task.FromResult(respond(request)));
    }

    private static HttpSnapshotClient Client(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
    {
        var http = new HttpClient(new FakeHttpHandler(respond)) { BaseAddress = new Uri(BaseUrl) };
        return new HttpSnapshotClient(http);
    }

    private sealed class FakeHttpHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _respond;

        public FakeHttpHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
        {
            _respond = respond;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return _respond(request, cancellationToken);
        }
    }
}
