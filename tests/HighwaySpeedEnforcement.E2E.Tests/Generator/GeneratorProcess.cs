using System.Diagnostics;
using System.Reflection;
using System.Text;
using HighwaySpeedEnforcement.E2E.Tests.Support;

namespace HighwaySpeedEnforcement.E2E.Tests.Generator;

/// <summary>What a finished generator process left behind: exit code, standard output (JSON log lines) and standard error.</summary>
public sealed record GeneratorRunResult(int ExitCode, IReadOnlyList<string> StandardOutput, string StandardError);

/// <summary>
/// Starts the real generator (<c>dotnet HighwaySpeedEnforcement.Generator.dll</c> from the host project's build output) with
/// settings on the command line, captures its output and makes sure it never outlives the test.
/// </summary>
public static class GeneratorProcess
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(90);

    public static string AssemblyPath { get; } = LocateAssembly();

    public static async Task<GeneratorRunResult> RunAsync(IEnumerable<string> settings, CancellationToken cancellationToken, TimeSpan? timeout = null)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(AssemblyPath)!,
        };
        startInfo.ArgumentList.Add(AssemblyPath);

        foreach (string setting in settings)
        {
            startInfo.ArgumentList.Add(setting);
        }

        using var process = new Process { StartInfo = startInfo };
        var output = new List<string>();
        var error = new StringBuilder();
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                lock (output)
                {
                    output.Add(e.Data);
                }
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                lock (error)
                {
                    error.AppendLine(e.Data);
                }
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout ?? DefaultTimeout);

        try
        {
            await process.WaitForExitAsync(deadline.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
            throw new TimeoutException($"The generator did not exit in time; output so far:{Environment.NewLine}{string.Join(Environment.NewLine, output)}");
        }

        return new GeneratorRunResult(process.ExitCode, output, error.ToString());
    }

    /// <summary>The command-line settings of a run against the test broker on a unique topic.</summary>
    public static List<string> Settings(MosquittoBroker broker, string topic, string statePath, params string[] more)
    {
        var settings = new List<string>
        {
            $"--Generator:Mqtt:Host={broker.Host}",
            $"--Generator:Mqtt:Port={broker.Port}",
            $"--Generator:Mqtt:Topic={topic}",
            $"--Generator:StatePath={statePath}",
        };
        settings.AddRange(more);
        return settings;
    }

    private static string LocateAssembly()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && directory.GetFiles("*.slnx").Length == 0)
        {
            directory = directory.Parent;
        }

        if (directory is null)
        {
            throw new InvalidOperationException("Repository root (the folder with the .slnx file) not found.");
        }

        string configuration = typeof(GeneratorProcess).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration ?? "Debug";
        string path = Path.Combine(directory.FullName, "src", "Generator", "HighwaySpeedEnforcement.Generator", "bin", configuration, "net10.0", "HighwaySpeedEnforcement.Generator.dll");

        return File.Exists(path) ? path : throw new FileNotFoundException("The generator must be built before the E2E tests run.", path);
    }
}
