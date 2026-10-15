using System.Text.Json;
using System.Text.Json.Serialization;
using HighwaySpeedEnforcement.Generator.Domain;
using Microsoft.Extensions.Logging;

namespace HighwaySpeedEnforcement.Generator.Infrastructure;

/// <summary>
/// Keeps the plate permutation state in a small JSON file so that plates stay unique across restarts (BR-SIM-03, design
/// section 4). Writes go to a temporary file next to the target and replace it atomically, so a reader never sees a partial file.
/// </summary>
public sealed partial class FilePlateStateStore : IPlateStateStore
{
    private readonly string _path;
    private readonly ILogger<FilePlateStateStore> _logger;

    public FilePlateStateStore(string path, ILogger<FilePlateStateStore> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(logger);

        _path = Path.GetFullPath(path);
        _logger = logger;
    }

    public string FilePath => _path;

    /// <summary>The stored state; null (with a warning) when the file is missing, unreadable, not JSON or holds an invalid state.</summary>
    public PlateState? Load()
    {
        if (!File.Exists(_path))
        {
            return null;
        }

        PlateStateDocument? document;

        try
        {
            document = Attempt(() =>
            {
                // Delete sharing lets a concurrent Save replace the file while it is being read.
                using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                return JsonSerializer.Deserialize(stream, PlateStateJsonContext.Default.PlateStateDocument);
            });
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            LogUnreadable(_path, exception);
            return null;
        }

        if (document is null)
        {
            LogInvalid(_path, "the file holds no object");
            return null;
        }

        var state = new PlateState(document.A, document.B, document.NextIndex);

        if (!PlatePermutation.IsValidState(PlateFormat.Size, state))
        {
            LogInvalid(_path, $"state {state} is not valid for the plate space of {PlateFormat.Size}");
            return null;
        }

        return state;
    }

    public void Save(PlateState state)
    {
        string directory = Path.GetDirectoryName(_path)!;
        Directory.CreateDirectory(directory);

        string temporary = Path.Combine(directory, $"{Path.GetFileName(_path)}.{Guid.NewGuid():N}.tmp");
        var document = new PlateStateDocument { A = state.A, B = state.B, NextIndex = state.NextIndex };

        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, document, PlateStateJsonContext.Default.PlateStateDocument);
                stream.Flush(flushToDisk: true);
            }

            Attempt(() =>
            {
                // Replace renames the old file away instead of superseding it, which works while a reader holds it open
                // with delete sharing; Move(overwrite) does not on Windows.
                if (File.Exists(_path))
                {
                    File.Replace(temporary, _path, null);
                }
                else
                {
                    File.Move(temporary, _path);
                }

                return true;
            });
        }
        catch
        {
            File.Delete(temporary);
            throw;
        }
    }

    /// <summary>
    /// Safety net for the rare transient sharing failure on Windows when the other side (a reader during a replace, a
    /// replace during a read) holds its handle at the wrong instant; the handles live for microseconds.
    /// </summary>
    private static T Attempt<T>(Func<T> action)
    {
        const int Attempts = 10;
        const int DelayMilliseconds = 5;

        for (int attempt = 1; ; attempt++)
        {
            try
            {
                return action();
            }
            catch (Exception exception) when (attempt < Attempts && exception is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(DelayMilliseconds);
            }
        }
    }

    [LoggerMessage(EventId = 2001, Level = LogLevel.Warning, Message = "Plate state file {Path} cannot be read; a new permutation will be created.")]
    private partial void LogUnreadable(string path, Exception exception);

    [LoggerMessage(EventId = 2002, Level = LogLevel.Warning, Message = "Plate state file {Path} is invalid ({Reason}); a new permutation will be created.")]
    private partial void LogInvalid(string path, string reason);

    /// <summary>The file's shape; the domain record is not serialized directly so that the file format is explicit.</summary>
    public sealed class PlateStateDocument
    {
        public long A { get; set; }

        public long B { get; set; }

        public long NextIndex { get; set; }
    }

    [JsonSourceGenerationOptions(WriteIndented = true)]
    [JsonSerializable(typeof(PlateStateDocument))]
    private sealed partial class PlateStateJsonContext : JsonSerializerContext
    {
    }
}
