using System.Collections.Concurrent;
using System.Xml;
using System.Xml.Linq;

namespace LsrXmlEditor.Core.Search;

public sealed record SearchSource(string FilePath, string RelativeFile, string FileName, XElement? Edited);

public sealed record SearchResults(IReadOnlyList<SearchHit> Hits, IReadOnlyList<string> Skipped);

public sealed class SearchRunner(SearchSourceCache cache, int maxHits)
{
    public SearchResults Run(IReadOnlyList<SearchSource> sources, SearchQuery query, IProgress<string> progress, CancellationToken token)
    {
        var results = new FileResult[sources.Count];
        var finished = new bool[sources.Count];
        var gate = new Lock();
        var finishedPrefix = 0;
        var prefixHits = 0;

        bool CompletesPrefix(int index)
        {
            lock (gate)
            {
                finished[index] = true;
                while (finishedPrefix < finished.Length && finished[finishedPrefix])
                {
                    prefixHits += results[finishedPrefix].Hits.Count;
                    finishedPrefix++;
                }

                return prefixHits >= maxHits;
            }
        }

        var order = Partitioner.Create(Enumerable.Range(0, sources.Count), EnumerablePartitionerOptions.NoBuffering);
        Parallel.ForEach(order, new ParallelOptions { CancellationToken = token }, (index, state) =>
        {
            progress.Report(sources[index].FileName);
            results[index] = SearchFile(sources[index], query, token);
            if (CompletesPrefix(index))
            {
                state.Stop();
            }
        });

        return Merge(sources, results);
    }

    private FileResult SearchFile(SearchSource source, SearchQuery query, CancellationToken token)
    {
        try
        {
            var root = source.Edited ?? cache.Load(source.FilePath);
            var hits = new List<SearchHit>();
            ConfigSearch.Run(root, source.FilePath, source.RelativeFile, query, hit =>
            {
                if (hits.Count < maxHits)
                {
                    hits.Add(hit);
                }
            }, token);
            return new FileResult(hits, false);
        }
        catch (Exception ex) when (ex is XmlException or IOException or UnauthorizedAccessException)
        {
            return new FileResult([], true);
        }
    }

    private SearchResults Merge(IReadOnlyList<SearchSource> sources, FileResult[] results)
    {
        var hits = new List<SearchHit>();
        var skipped = new List<string>();
        for (var i = 0; i < results.Length && hits.Count < maxHits; i++)
        {
            if (results[i].Unreadable)
            {
                skipped.Add(sources[i].FileName);
            }
            else
            {
                hits.AddRange(results[i].Hits.Take(maxHits - hits.Count));
            }
        }

        return new SearchResults(hits, skipped);
    }

    private readonly record struct FileResult(IReadOnlyList<SearchHit> Hits, bool Unreadable);
}
