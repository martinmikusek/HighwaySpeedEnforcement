using HighwaySpeedEnforcement.Processor.Domain;

namespace HighwaySpeedEnforcement.Unit.Tests.Processor.Domain;

/// <summary>
/// Model-based tests of <see cref="RankedBoard"/> against a naive oracle that keeps every entry in a list and recomputes the
/// live top 10 after each operation (BR-LB-09), plus order independence across workers (BR-LB-13).
/// </summary>
public sealed class RankedBoardModelTests
{
    private const int Sequences = 200;
    private const int OperationsPerSequence = 200;
    private const int Plates = 30;

    [Fact]
    [Trait("BR", "BR-LB-09")]
    [Trait("BR", "BR-LB-07")]
    public void Board_equals_the_naive_oracle_after_every_operation()
    {
        for (int seed = 1; seed <= Sequences; seed++)
        {
            var board = new RankedBoard();
            var oracle = new Oracle();
            int step = 0;

            foreach (var operation in Operations(seed, finiteOnly: true))
            {
                operation.ApplyTo(board);
                operation.ApplyTo(oracle);
                step++;

                Assert.True(oracle.Top10.SequenceEqual(board.Top(10).Entries), $"top 10 differs: seed {seed}, step {step}, {operation}");
                Assert.True(oracle.Entries.SequenceEqual(All(board)), $"entry set differs: seed {seed}, step {step}, {operation}");
                Assert.True(oracle.Watermark == board.LatestEventTicks, $"watermark differs: seed {seed}, step {step}");
            }
        }
    }

    [Fact]
    [Trait("BR", "BR-LB-09")]
    [Trait("BR", "BR-LB-10")]
    public void Pruning_of_infinite_entries_never_changes_the_top_10()
    {
        for (int seed = 1; seed <= Sequences; seed++)
        {
            var board = new RankedBoard();
            var oracle = new Oracle();
            int step = 0;

            foreach (var operation in Operations(seed, finiteOnly: false))
            {
                operation.ApplyTo(board);
                operation.ApplyTo(oracle);
                step++;

                Assert.True(oracle.Top10.SequenceEqual(board.Top(10).Entries), $"top 10 differs: seed {seed}, step {step}, {operation}");
                Assert.True(board.Count <= oracle.Entries.Count, $"board holds more than the oracle: seed {seed}, step {step}");
            }
        }
    }

    [Fact]
    [Trait("AC", "AC-09")]
    [Trait("AC", "AC-30")]
    [Trait("BR", "BR-LB-13")]
    public async Task Final_board_is_the_same_for_one_and_four_workers_when_each_plate_keeps_its_order()
    {
        const int workers = 4;

        for (int seed = 1; seed <= 20; seed++)
        {
            var perWorker = Enumerable.Range(0, workers).Select(_ => new List<Operation>()).ToArray();

            foreach (var operation in Operations(seed, finiteOnly: true, operations: 2000))
            {
                perWorker[operation.Worker(workers)].Add(operation);
            }

            var sequential = new RankedBoard();

            foreach (var operation in perWorker.SelectMany(list => list))
            {
                operation.ApplyTo(sequential);
            }

            var parallel = new RankedBoard();
            var gate = new TaskCompletionSource();
            var tasks = perWorker.Select(list => Task.Run(async () =>
            {
                await gate.Task;

                foreach (var operation in list)
                {
                    operation.ApplyTo(parallel);
                }
            }, TestContext.Current.CancellationToken)).ToArray();

            gate.SetResult();
            await Task.WhenAll(tasks);

            Assert.True(All(sequential).SequenceEqual(All(parallel)), $"entry set differs: seed {seed}");
            Assert.True(sequential.Top(10).Entries.SequenceEqual(parallel.Top(10).Entries), $"top 10 differs: seed {seed}");
            Assert.Equal(sequential.LatestEventTicks, parallel.LatestEventTicks);
        }
    }

    /// <summary>
    /// Random operations. Few distinct scores so that ties are frequent; timestamps near the watermark so that expiry is frequent.
    /// Infinite entries get plates of their own that are never replaced or removed: pruning (BR-LB-10) is only sound under that
    /// condition, which the pipeline guarantees because plates are never reused (BR-SIM-03).
    /// </summary>
    private static IEnumerable<Operation> Operations(int seed, bool finiteOnly, int operations = OperationsPerSequence)
    {
        var random = new Random(seed);
        long watermark = 0;
        int infinitePlates = 0;

        for (int i = 0; i < operations; i++)
        {
            int kind = random.Next(10);
            string plate = $"P{random.Next(Plates):00}";

            if (kind < 6)
            {
                double score = random.Next(5) * 10;
                long timestamp = Math.Max(0, watermark + random.Next(-3, 4));

                if (!finiteOnly && random.Next(4) == 0)
                {
                    yield return new Operation.Upsert(new BoardEntry($"I{infinitePlates++:00}", score, timestamp, long.MaxValue, MeasurementCount: 0));
                }
                else
                {
                    yield return new Operation.Upsert(new BoardEntry(plate, score, timestamp, timestamp + random.Next(0, 8), MeasurementCount: 0));
                }
            }
            else if (kind < 8)
            {
                yield return new Operation.Remove(plate);
            }
            else
            {
                watermark += random.Next(0, 4);
                yield return new Operation.AdvanceTo(Math.Max(0, watermark + random.Next(-2, 1)));
            }
        }
    }

    private static IEnumerable<BoardEntry> All(RankedBoard board)
    {
        return board.Top(int.MaxValue).Entries.Select(e => e.Entry);
    }

    private abstract record Operation
    {
        public abstract void ApplyTo(RankedBoard board);

        public abstract void ApplyTo(Oracle oracle);

        /// <summary>Operations of one plate go to one worker; advances go to a worker chosen by the watermark.</summary>
        public abstract int Worker(int workers);

        public sealed record Upsert(BoardEntry Entry) : Operation
        {
            public override void ApplyTo(RankedBoard board)
            {
                board.Upsert(Entry);
            }

            public override void ApplyTo(Oracle oracle)
            {
                oracle.Upsert(Entry);
            }

            public override int Worker(int workers)
            {
                return int.Parse(Entry.Plate[1..], System.Globalization.CultureInfo.InvariantCulture) % workers;
            }
        }

        public sealed record Remove(string Plate) : Operation
        {
            public override void ApplyTo(RankedBoard board)
            {
                board.Remove(Plate);
            }

            public override void ApplyTo(Oracle oracle)
            {
                oracle.Remove(Plate);
            }

            public override int Worker(int workers)
            {
                return int.Parse(Plate[1..], System.Globalization.CultureInfo.InvariantCulture) % workers;
            }
        }

        public sealed record AdvanceTo(long Watermark) : Operation
        {
            public override void ApplyTo(RankedBoard board)
            {
                board.AdvanceTo(Watermark);
            }

            public override void ApplyTo(Oracle oracle)
            {
                oracle.AdvanceTo(Watermark);
            }

            public override int Worker(int workers)
            {
                return (int)(Watermark % workers);
            }
        }
    }

    private sealed class Oracle
    {
        private readonly List<BoardEntry> _entries = [];

        public long? Watermark { get; private set; }

        public IReadOnlyList<BoardEntry> Entries => _entries.Order(RankOrder.Instance).ToArray();

        public IEnumerable<RankedEntry> Top10 => Entries.Take(10).Select((entry, index) => new RankedEntry(index + 1, entry));

        public void Upsert(BoardEntry entry)
        {
            Remove(entry.Plate);

            if (Watermark is null || entry.ExpiresAtTicks > Watermark)
            {
                _entries.Add(entry);
            }
        }

        public void Remove(string plate)
        {
            _entries.RemoveAll(e => e.Plate == plate);
        }

        public void AdvanceTo(long watermark)
        {
            Watermark = Math.Max(Watermark ?? long.MinValue, watermark);
            _entries.RemoveAll(e => e.ExpiresAtTicks <= Watermark);
        }
    }
}
