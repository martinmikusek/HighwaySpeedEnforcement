// Throwaway spike for ADR-0001: verify MQTT (Mosquitto + MQTTnet) ordering, redelivery,
// broker queue limits and backpressure at the assignment's load (100 new vehicles/s, 1,000 msg/s).
// Usage: MqttSpike <order|redeliver|queue|slow> [label]
using System.Diagnostics;
using System.Text;
using MQTTnet;
using MQTTnet.Formatter;
using MQTTnet.Protocol;

const string Topic = "highway/telemetry/measurements";
var factory = new MqttClientFactory();
var scenario = args.Length > 0 ? args[0] : "order";
var label = args.Length > 1 ? args[1] : "";
var checker = new SequenceChecker(); // mirrors BR-ING-02: classify each message of a plate against the sequence rule
Console.WriteLine($"=== scenario '{scenario}' {label} ===");

switch (scenario)
{
    case "order": await Order(); break;
    case "redeliver": await Redeliver(); break;
    case "queue": await Queue(); break;
    case "slow": await Slow(); break;
    default: Console.WriteLine("unknown scenario"); break;
}

async Task<IMqttClient> ConnectAsync(string clientId, bool clean, Func<MqttApplicationMessageReceivedEventArgs, Task>? handler = null)
{
    var c = factory.CreateMqttClient();
    var o = new MqttClientOptionsBuilder()
        .WithTcpServer(Environment.GetEnvironmentVariable("MQTT_HOST") ?? "127.0.0.1", 1883)
        .WithClientId(clientId)
        .WithCleanSession(clean)
        .WithProtocolVersion(MqttProtocolVersion.V311)
        .WithKeepAlivePeriod(TimeSpan.FromSeconds(30))
        .Build();
    if (handler != null) c.ApplicationMessageReceivedAsync += handler;   // must be attached before CONNECT: the broker delivers queued messages immediately
    Console.WriteLine($"connecting {clientId}..."); await c.ConnectAsync(o); Console.WriteLine($"connected {clientId}");
    return c;
}

async Task SubscribeAsync(IMqttClient c) =>
    await c.SubscribeAsync(factory.CreateSubscribeOptionsBuilder()
        .WithTopicFilter(t => t.WithTopic(Topic).WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce))
        .Build());

Task PublishAsync(IMqttClient c, string payload) =>
    c.PublishAsync(new MqttApplicationMessageBuilder()
        .WithTopic(Topic)
        .WithPayload(Encoding.UTF8.GetBytes(payload))
        .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
        .Build());


// ---------- S1: ordering and throughput under the assignment's load ----------
async Task Order()
{
    var latencies = new List<double>(20000);
    var sub = await ConnectAsync("spike-sub", clean: true);
    sub.ApplicationMessageReceivedAsync += e =>
    {
        var p = Encoding.UTF8.GetString(e.ApplicationMessage.Payload).Split(',');
        var lat = (DateTime.UtcNow.Ticks - long.Parse(p[2])) / (double)TimeSpan.TicksPerMillisecond;
        lock (latencies) latencies.Add(lat);
        checker.Add(p[0], int.Parse(p[1]), e.ApplicationMessage.Dup);
        return Task.CompletedTask;
    };
    await SubscribeAsync(sub);

    var pub = await ConnectAsync("spike-pub", clean: true);
    var ticks = 15;
    var sent = 0;
    var active = new List<(string plate, int cam)>();
    long nextPlate = 0;
    var tickWatch = new List<double>();
    using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
    for (var t = 1; t <= ticks; t++)
    {
        await timer.WaitForNextTickAsync();
        var sw = Stopwatch.StartNew();
        for (var i = 0; i < 100; i++) active.Add(($"P{nextPlate++:D8}", 0));
        for (var i = 0; i < active.Count; i++)
        {
            active[i] = (active[i].plate, active[i].cam + 1);
            await PublishAsync(pub, $"{active[i].plate},{active[i].cam},{DateTime.UtcNow.Ticks}");
            sent++;
        }
        active.RemoveAll(v => v.cam == 10);
        tickWatch.Add(sw.Elapsed.TotalMilliseconds); Console.WriteLine($"tick {t}: sent so far {sent}, {sw.ElapsedMilliseconds} ms");
    }
    await Task.Delay(1000);
    Console.WriteLine($"sent={sent} received={checker.Total}");
    Console.WriteLine($"publish time per tick (ms): max={tickWatch.Max():F0} steady-state avg={tickWatch.Skip(10).Average():F0}");
    latencies.Sort();
    Console.WriteLine($"end-to-end latency ms: p50={latencies[latencies.Count / 2]:F1} p99={latencies[(int)(latencies.Count * 0.99)]:F1} max={latencies[^1]:F1}");
    checker.Print();
}

// ---------- S2: crash before ack -> redelivery, duplicates, ordering ----------
async Task Redeliver()
{
    var pub = await ConnectAsync("spike-pub", clean: true);
    var sub1 = await ConnectAsync("spike-persistent", clean: false);
    var acked = 0;
    var stopAcking = false;
    sub1.ApplicationMessageReceivedAsync += async e =>
    {
        e.AutoAcknowledge = false;
        var p = Encoding.UTF8.GetString(e.ApplicationMessage.Payload).Split(',');
        checker.Add(p[0], int.Parse(p[1]), e.ApplicationMessage.Dup);                              // state is updated first...
        if (stopAcking) return;                       // ...then "crash": applied but never acknowledged
        await e.AcknowledgeAsync(CancellationToken.None);
        if (Interlocked.Increment(ref acked) == 300) stopAcking = true;
    };
    await SubscribeAsync(sub1);

    // 4 ticks of 100 vehicles x ... simple sequence: plate i gets cameras 1..5 interleaved by "tick"
    var plates = Enumerable.Range(0, 200).Select(i => $"R{i:D4}").ToArray();
    async Task Tick(int cam) { foreach (var pl in plates) await PublishAsync(pub, $"{pl},{cam},{DateTime.UtcNow.Ticks}"); }

    await Tick(1); await Tick(2);                     // 400 messages: consumer acks 300, then "crashes"
    await Task.Delay(500);
    Console.WriteLine($"before crash: acked={acked}, checker total={checker.Total}");
    sub1.Dispose();                                   // abrupt: unacknowledged messages stay in the broker session
    await Tick(3);                                    // consumer offline: broker queues these
    await Task.Delay(300);

    var sub2 = await ConnectAsync("spike-persistent", clean: false, async e =>   // same client id, session resumes
    {
        e.AutoAcknowledge = false;
        var p = Encoding.UTF8.GetString(e.ApplicationMessage.Payload).Split(',');
        checker.Add(p[0], int.Parse(p[1]), e.ApplicationMessage.Dup);
        await e.AcknowledgeAsync(CancellationToken.None);
    });
    await SubscribeAsync(sub2);
    await Tick(4);
    await Task.Delay(1500);
    Console.WriteLine("(sent 800 messages in total, 200 plates x cameras 1..4)");
    checker.Print();
}

// ---------- S3: broker queue limit while the consumer is offline ----------
async Task Queue()
{
    var sub = await ConnectAsync("spike-offline", clean: false);
    await SubscribeAsync(sub);
    await sub.DisconnectAsync();                      // session persists, subscription stays
    var pub = await ConnectAsync("spike-pub", clean: true);
    const int n = 5000;
    var sw = Stopwatch.StartNew();
    for (var i = 0; i < n; i++) await PublishAsync(pub, $"Q{i:D6},1,{DateTime.UtcNow.Ticks}");
    Console.WriteLine($"published {n} messages while consumer offline in {sw.ElapsedMilliseconds} ms (broker acked all)");

    var seen = new HashSet<string>();
    var sub2 = factory.CreateMqttClient();
    sub2.ApplicationMessageReceivedAsync += e =>
    {
        lock (seen) seen.Add(Encoding.UTF8.GetString(e.ApplicationMessage.Payload).Split(',')[0]);
        return Task.CompletedTask;
    };
    await sub2.ConnectAsync(new MqttClientOptionsBuilder().WithTcpServer(Environment.GetEnvironmentVariable("MQTT_HOST") ?? "127.0.0.1", 1883)
        .WithClientId("spike-offline").WithCleanSession(false).WithProtocolVersion(MqttProtocolVersion.V311).Build());
    await Task.Delay(3000);
    var first = seen.OrderBy(x => x).FirstOrDefault();
    var last = seen.OrderBy(x => x).LastOrDefault();
    Console.WriteLine($"delivered after reconnect: {seen.Count} of {n}  (first={first}, last={last})");
}

// ---------- S4: slow consumer, does the producer feel any backpressure? ----------
async Task Slow()
{
    var received = 0;
    var maxLag = 0.0;
    var sub = await ConnectAsync("spike-slow", clean: true);
    sub.ApplicationMessageReceivedAsync += async e =>
    {
        e.AutoAcknowledge = false;
        { var spin = Stopwatch.StartNew(); while (spin.Elapsed.TotalMilliseconds < 4) { } }   // exactly ~250 msg/s consumer (Task.Delay rounds to ~15 ms on Windows); assignment load is 1,000 msg/s
        var p = Encoding.UTF8.GetString(e.ApplicationMessage.Payload).Split(',');
        var lag = (DateTime.UtcNow.Ticks - long.Parse(p[2])) / (double)TimeSpan.TicksPerMillisecond;
        if (lag > maxLag) maxLag = lag;
        Interlocked.Increment(ref received);
        await e.AcknowledgeAsync(CancellationToken.None);
    };
    await SubscribeAsync(sub);
    var pub = await ConnectAsync("spike-pub", clean: true);
    const int perSecond = 1000, seconds = 10;
    var pubLatencies = new List<double>();
    using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
    for (var s = 0; s < seconds; s++)
    {
        await timer.WaitForNextTickAsync();
        for (var i = 0; i < perSecond; i++)
        {
            var sw = Stopwatch.StartNew();
            await PublishAsync(pub, $"S{s * perSecond + i:D6},1,{DateTime.UtcNow.Ticks}");
            pubLatencies.Add(sw.Elapsed.TotalMilliseconds);
        }
    }
    var sent = perSecond * seconds;
    pubLatencies.Sort();
    Console.WriteLine($"publisher: sent {sent}, publish ack latency ms p50={pubLatencies[sent / 2]:F2} max={pubLatencies[^1]:F1} (no slowdown = no backpressure)");
    await Task.Delay(TimeSpan.FromSeconds(45));       // allow the slow consumer to drain
    Console.WriteLine($"consumer: received {received} of {sent}, lost {sent - received}, max end-to-end lag {maxLag / 1000:F1} s");
}

class SequenceChecker
{
    readonly Dictionary<string, int> _last = new();
    readonly object _gate = new();
    int _ok, _duplicate, _gap, _dupFlag;
    public int Total => _ok + _duplicate + _gap;

    public void Add(string plate, int camera, bool dupFlag)
    {
        lock (_gate)
        {
            if (dupFlag) _dupFlag++;
            var has = _last.TryGetValue(plate, out var last);
            if (has && camera <= last) { _duplicate++; return; }                 // repeated / stale
            if ((!has && camera != 1) || (has && camera != last + 1)) { _gap++; _last[plate] = camera; return; } // missing or reordered
            _ok++; _last[plate] = camera;
        }
    }

    public void Print() =>
        Console.WriteLine($"sequence rule: accepted={_ok} rejected-as-duplicate={_duplicate} rejected-as-gap/out-of-order={_gap} (MQTT DUP flag seen on {_dupFlag})");
}
