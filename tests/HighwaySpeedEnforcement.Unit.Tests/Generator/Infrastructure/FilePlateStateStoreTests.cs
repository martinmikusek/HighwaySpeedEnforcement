using HighwaySpeedEnforcement.Generator.Domain;
using HighwaySpeedEnforcement.Generator.Infrastructure;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace HighwaySpeedEnforcement.Unit.Tests.Generator.Infrastructure;

public sealed class FilePlateStateStoreTests : IDisposable
{
    private static readonly PlateState ValidState = new(7_919, 12_345, 678_901);

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "hse-generator-tests", Guid.NewGuid().ToString("N"));
    private readonly FakeLogger<FilePlateStateStore> _logger = new();

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    [Trait("BR", "BR-SIM-03")]
    public void Save_then_Load_round_trips_the_state_and_creates_the_directory()
    {
        var store = CreateStore("nested/generator-state.json");

        store.Save(ValidState);

        Assert.Equal(ValidState, store.Load());
        Assert.Empty(_logger.Collector.GetSnapshot());
    }

    [Fact]
    [Trait("BR", "BR-SIM-03")]
    public void Save_twice_overwrites_and_leaves_no_temporary_file()
    {
        var store = CreateStore();
        var later = ValidState with { NextIndex = ValidState.NextIndex + 100 };

        store.Save(ValidState);
        store.Save(later);

        Assert.Equal(later, store.Load());
        Assert.Equal(["generator-state.json"], Directory.GetFiles(_directory).Select(Path.GetFileName));
    }

    [Fact]
    public void Load_missing_file_gives_null()
    {
        Assert.Null(CreateStore().Load());
    }

    [Fact]
    public void Load_corrupt_json_gives_null_and_logs_a_warning()
    {
        var store = CreateStore();
        Directory.CreateDirectory(_directory);
        File.WriteAllText(store.FilePath, "{ not json");

        Assert.Null(store.Load());

        var record = Assert.Single(_logger.Collector.GetSnapshot());
        Assert.Equal(LogLevel.Warning, record.Level);
        Assert.Contains(store.FilePath, record.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{ "A": 10, "B": 0, "NextIndex": 0 }""")]
    [InlineData("""{ "A": 7919, "B": 0, "NextIndex": 40320000 }""")]
    [InlineData("""{ "A": 7919, "B": -1, "NextIndex": 0 }""")]
    [InlineData("null")]
    public void Load_invalid_state_gives_null_and_logs_a_warning(string json)
    {
        var store = CreateStore();
        Directory.CreateDirectory(_directory);
        File.WriteAllText(store.FilePath, json);

        Assert.Null(store.Load());

        var record = Assert.Single(_logger.Collector.GetSnapshot());
        Assert.Equal(LogLevel.Warning, record.Level);
    }

    [Fact]
    [Trait("BR", "BR-SIM-03")]
    public async Task Load_during_concurrent_saves_never_sees_a_partial_file()
    {
        var store = CreateStore();
        store.Save(ValidState);
        using var stop = new CancellationTokenSource();
        var reads = 0;

        var reader = Task.Run(() =>
        {
            while (!stop.IsCancellationRequested)
            {
                var state = store.Load();

                if (state is { } value)
                {
                    Assert.Equal(ValidState.A, value.A);
                    Assert.Equal(ValidState.B, value.B);
                    Assert.InRange(value.NextIndex, ValidState.NextIndex, ValidState.NextIndex + 200);
                }

                reads++;
            }
        }, TestContext.Current.CancellationToken);

        for (int i = 1; i <= 200; i++)
        {
            store.Save(ValidState with { NextIndex = ValidState.NextIndex + i });
        }

        await stop.CancelAsync();
        await reader;

        Assert.Equal(ValidState with { NextIndex = ValidState.NextIndex + 200 }, store.Load());
        Assert.True(reads > 0);
        Assert.Empty(_logger.Collector.GetSnapshot());
    }

    private FilePlateStateStore CreateStore(string fileName = "generator-state.json")
    {
        return new FilePlateStateStore(Path.Combine(_directory, fileName), _logger);
    }
}
