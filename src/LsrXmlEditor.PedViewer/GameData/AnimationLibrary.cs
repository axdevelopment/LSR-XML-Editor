using System.Runtime.CompilerServices;
using CodeWalker.GameFiles;

namespace LsrXmlEditor.PedViewer.GameData;

public sealed class AnimationLibrary
{
    private const string Extension = ".ycd";
    private const string GesturePrefix = "gestures@";
    private const string FemaleMarker = "@female@";
    private const string MaleMarker = "@male@";
    private const string FemaleGesture = "@f@";

    private static readonly ConditionalWeakTable<GameArchive, AnimationLibrary> Libraries = new();

    private readonly GameArchive _archive;
    private readonly Lazy<Dictionary<string, List<string>>> _gestures;

    private AnimationLibrary(GameArchive archive)
    {
        _archive = archive;
        _gestures = new(IndexGestures);
    }

    public static AnimationLibrary For(GameArchive archive) => Libraries.GetValue(archive, a => new AnimationLibrary(a));

    public (string Dictionary, ClipBase Clip)? Find(string dictionary, string clipName, bool female)
    {
        foreach (var candidate in Candidates(dictionary.Trim().ToLowerInvariant(), clipName.Trim().ToLowerInvariant(), female))
        {
            if (Load(candidate) is { } ycd && FindClip(ycd, clipName) is { } clip)
            {
                return (candidate, clip);
            }
        }

        return null;
    }

    private IEnumerable<string> Candidates(string dictionary, string clip, bool female)
    {
        if (dictionary.Length > 0)
        {
            yield return dictionary;
            if (dictionary.Contains(FemaleMarker, StringComparison.Ordinal))
            {
                yield return dictionary.Replace(FemaleMarker, MaleMarker, StringComparison.Ordinal);
            }
            else if (dictionary.Contains(MaleMarker, StringComparison.Ordinal))
            {
                yield return dictionary.Replace(MaleMarker, FemaleMarker, StringComparison.Ordinal);
            }

            yield break;
        }

        if (_gestures.Value.TryGetValue(clip, out var owners))
        {
            foreach (var owner in owners.OrderBy(o => o.Contains(FemaleGesture, StringComparison.Ordinal) == female ? 0 : 1))
            {
                yield return owner;
            }
        }
    }

    private YcdFile? Load(string dictionary) =>
        _archive.FindFile(dictionary + Extension) is { } path ? _archive.Load<YcdFile>(path) : null;

    private Dictionary<string, List<string>> IndexGestures()
    {
        var result = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var dictionaries = _archive.Paths
            .Select(GameArchive.FileName)
            .Where(n => n.StartsWith(GesturePrefix, StringComparison.Ordinal) && n.EndsWith(Extension, StringComparison.Ordinal))
            .Select(n => n[..^Extension.Length])
            .Distinct(StringComparer.Ordinal);

        foreach (var dictionary in dictionaries)
        {
            foreach (var clip in Load(dictionary)?.ClipMapEntries?.Select(e => e.Clip?.ShortName).OfType<string>() ?? [])
            {
                var key = clip.ToLowerInvariant();
                if (!result.TryGetValue(key, out var owners))
                {
                    owners = [];
                    result[key] = owners;
                }

                owners.Add(dictionary);
            }
        }

        return result;
    }

    private static ClipBase? FindClip(YcdFile ycd, string name)
    {
        var lower = name.Trim().ToLowerInvariant();
        if (ycd.ClipMap?.TryGetValue(JenkHash.GenHash(lower), out var entry) == true && entry.Clip is not null)
        {
            return entry.Clip;
        }

        return ycd.ClipMapEntries?
            .Select(e => e.Clip)
            .FirstOrDefault(c => c is not null && string.Equals(c.ShortName, lower, StringComparison.OrdinalIgnoreCase));
    }
}
