using HighwaySpeedEnforcement.Generator;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);
var check = GeneratorHost.Check(builder.Configuration);

if (check.ExitCode != 0)
{
    await Console.Error.WriteLineAsync("Invalid generator settings:");

    foreach (string error in check.Errors)
    {
        await Console.Error.WriteLineAsync($"  {error}");
    }

    return check.ExitCode;
}

GeneratorHost.Configure(builder, check.Settings);

// Exit code 0 after MaxTicks (the runner stops the host) and after a stop signal.
await builder.Build().RunAsync();

return 0;
