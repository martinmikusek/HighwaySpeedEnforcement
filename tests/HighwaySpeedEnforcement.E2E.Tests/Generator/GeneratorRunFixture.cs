using HighwaySpeedEnforcement.E2E.Tests.Support;
using HighwaySpeedEnforcement.Processor.Contracts.Telemetry;

namespace HighwaySpeedEnforcement.E2E.Tests.Generator;

/// <summary>
/// One shared run of the real generator for 15 ticks against a Testcontainers broker, recorded by a subscriber, so that
/// several assertions (AC-01, AC-02, AC-03, the logs) share the seconds it costs. Further runs of the tests reuse the broker.
/// </summary>
public sealed class GeneratorRunFixture : IAsyncLifetime
{
    public const int Ticks = 15;
    public const long Seed = 20261014;

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "hse-generator-e2e", Guid.NewGuid().ToString("N"));
    private RecordingSubscriber? _subscriber;

    public MosquittoBroker Broker { get; } = new();

    public IReadOnlyList<ReceivedMessage> Messages { get; private set; } = [];

    public GeneratorRunResult Result { get; private set; } = null!;

    public string NewStatePath()
    {
        Directory.CreateDirectory(_directory);
        return Path.Combine(_directory, $"{Guid.NewGuid():N}.json");
    }

    public static string UniqueTopic()
    {
        return $"{TelemetryContract.Topic}/e2e/{Guid.NewGuid():N}";
    }

    public async ValueTask InitializeAsync()
    {
        await Broker.InitializeAsync();
        var cancellationToken = TestContext.Current.CancellationToken;
        string topic = UniqueTopic();
        _subscriber = await RecordingSubscriber.StartAsync(Broker, topic, cancellationToken);

        Result = await GeneratorProcess.RunAsync(
            GeneratorProcess.Settings(Broker, topic, NewStatePath(), $"--Generator:Seed={Seed}", $"--Generator:MaxTicks={Ticks}"),
            cancellationToken);

        // 15 ticks: 100 * (1 + 2 + ... + 10) + 5 * 1,000 measurements.
        await _subscriber.WaitForCountAsync(10_500, cancellationToken);
        Messages = _subscriber.Messages;
    }

    public async ValueTask DisposeAsync()
    {
        if (_subscriber is not null)
        {
            await _subscriber.DisposeAsync();
        }

        await Broker.DisposeAsync();

        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
