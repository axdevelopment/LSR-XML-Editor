using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LsrXmlEditor.Core.Changes;

public sealed class ChangeLogStore(string filePath)
{
    private const string CorruptStampFormat = "yyyyMMdd-HHmmss";
    private const int LockAttempts = 50;
    private const int LockDelayMs = 100;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly Lock _gate = new();

    public string FilePath { get; } = filePath;

    public IReadOnlyList<ChangeSet> Load()
    {
        lock (_gate)
        {
            return Read();
        }
    }

    public void Append(ChangeSet changeSet)
    {
        lock (_gate)
        {
            using var fileLock = AcquireLock();
            Write([.. Read(), changeSet]);
        }
    }

    public void Remove(IEnumerable<Guid> ids)
    {
        lock (_gate)
        {
            using var fileLock = AcquireLock();
            var remove = ids.ToHashSet();
            Write(Read().Where(c => !remove.Contains(c.Id)).ToList());
        }
    }

    private FileStream AcquireLock()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return new FileStream(FilePath + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
            }
            catch (IOException) when (attempt < LockAttempts)
            {
                Thread.Sleep(LockDelayMs);
            }
        }
    }

    private List<ChangeSet> Read()
    {
        if (!File.Exists(FilePath))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<ChangeSet>>(File.ReadAllText(FilePath), Options) ?? [];
        }
        catch (JsonException)
        {
            MoveAside();
            return [];
        }
    }

    private void MoveAside()
    {
        var stamp = DateTime.Now.ToString(CorruptStampFormat, CultureInfo.InvariantCulture);
        File.Move(FilePath, $"{FilePath}.corrupt-{stamp}", overwrite: true);
    }

    private void Write(List<ChangeSet> all)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var temp = FilePath + ".tmp";
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(all, Options));
            File.Move(temp, FilePath, overwrite: true);
        }
        finally
        {
            File.Delete(temp);
        }
    }
}
