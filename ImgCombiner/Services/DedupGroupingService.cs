using System.Collections.Concurrent;
using System.IO;

namespace ImgCombiner.Services;

public sealed class DedupGroupingService : IDedupGroupingService
{
    private readonly IImageSignatureService _sig;
    public DedupGroupingService(IImageSignatureService sig) => _sig = sig;
    private readonly record struct CacheKey(string Path, long Ticks, long Length);
    private CacheKey MakeKey(string path)
    {
        var fi = new FileInfo(path);
        return new CacheKey(fi.FullName, fi.LastWriteTimeUtc.Ticks, fi.Length);
    }
    // 每次 FindSimilarGroups 调用内部使用的缓存（避免缓存无限增长）
    private sealed class SigCache
    {
        public ConcurrentDictionary<CacheKey, Lazy<byte[]>> Block4 { get; } = new();
        public ConcurrentDictionary<CacheKey, Lazy<byte[]>> DHash { get; } = new();
    }
    public IReadOnlyList<List<string>> FindSimilarGroups(
    IReadOnlyList<string> paths,
    int coarseThresholdManhattan,
    double ratioEps,
    int dhashHammingThreshold,
    IProgress<(double percent, string text)>? progress,
    CancellationToken ct)
    {
        if (paths.Count < 2) return Array.Empty<List<string>>();

        void Report(double p, string t)
            => progress?.Report((Math.Clamp(p, 0, 100), t));

        // ---------- 20%：读 meta ----------
        var items = new List<ImageSignature>(paths.Count);
        int metaDone = 0;

        for (int k = 0; k < paths.Count; k++)
        {
            ct.ThrowIfCancellationRequested();
            var p = paths[k];
            try
            {
                var meta = _sig.ReadMetaFast(p);
                items.Add(new ImageSignature(p, meta));
            }
            catch
            {
                // ignore
            }
            metaDone++;

            if (metaDone % 50 == 0 || metaDone == paths.Count)
            {
                var percent = (metaDone * 20.0) / paths.Count;
                Report(percent, $"读取元信息 {metaDone}/{paths.Count}");
            }
        }

        if (items.Count < 2) return Array.Empty<List<string>>();

        // ---------- 建 ratio 桶（不计入进度或算在 20% 尾部） ----------
        var buckets = new Dictionary<int, List<int>>();
        for (int i = 0; i < items.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var r = items[i].Meta.Ratio;
            var key = (int)Math.Round(r / ratioEps);
            if (!buckets.TryGetValue(key, out var list))
            {
                list = new List<int>();
                buckets[key] = list;
            }
            list.Add(i);
        }

        var dsu = new Dsu(items.Count);

        // ---------- 缓存准备 ----------
        var cache = new SigCache();

        var keys = new CacheKey[items.Count];
        var keyOk = new bool[items.Count];
        for (int i = 0; i < items.Count; i++)
        {
            try { keys[i] = MakeKey(items[i].Path); keyOk[i] = true; }
            catch { keyOk[i] = false; }
        }

        object dsuLock = new();
        // ---------- 70%：桶内两重循环 ----------
        // 先估算总比较次数（只算 bucket 内的组合数）
        long totalPairs = 0;
        foreach (var bucket in buckets.Values)
        {
            int m = bucket.Count;
            if (m >= 2) totalPairs += (long)m * (m - 1) / 2;
        }
        if (totalPairs == 0) totalPairs = 1;

        long donePairs = 0;
        // donePairs 用于进度（全局）
        // 桶之间并行
        Parallel.ForEach(
            buckets.Values,
            new ParallelOptions { CancellationToken = ct, MaxDegreeOfParallelism = Math.Min(Math.Max(Environment.ProcessorCount - 2, 1), 8) },
            bucket =>
            {
                ct.ThrowIfCancellationRequested();
                if (bucket.Count <= 1) return;

                for (int a = 0; a < bucket.Count; a++)
                {
                    int i = bucket[a];

                    // 若 i 的 key 不可用，则跳过与它相关的 pair，并把 donePairs 加上（保证进度不“卡住”）
                    if (!keyOk[i])
                    {
                        Interlocked.Add(ref donePairs, bucket.Count - 1 - a);
                        continue;
                    }

                    var pi = items[i].Path;
                    var ri = items[i].Meta.Ratio;
                    CacheKey keyI = keys[i];

                    for (int b = a + 1; b < bucket.Count; b++)
                    {
                        int j = bucket[b];
                        Interlocked.Increment(ref donePairs);

                        var dp = Volatile.Read(ref donePairs);
                        if ((dp %128) == 0 || dp == totalPairs)
                        {
                            var percent = 20.0 + (dp * 70.0) / totalPairs;
                            Report(percent, $"相似比对 {dp}/{totalPairs}（并行桶）");
                        }

                        if (!keyOk[j]) continue;

                        var rj = items[j].Meta.Ratio;
                        if (Math.Abs(ri - rj) > ratioEps) continue;

                        var pj = items[j].Path;
                        CacheKey keyJ = keys[j];

                        // 4x4（缓存）
                        var blockI = cache.Block4.GetOrAdd(
                            keyI,
                            _ => new Lazy<byte[]>(() => _sig.Compute2x2Rgb(pi), LazyThreadSafetyMode.ExecutionAndPublication)
                        ).Value;

                        var blockJ = cache.Block4.GetOrAdd(
                            keyJ,
                            _ => new Lazy<byte[]>(() => _sig.Compute2x2Rgb(pj), LazyThreadSafetyMode.ExecutionAndPublication)
                        ).Value;

                        var dist4 = _sig.DistanceManhattan(blockI, blockJ);
                        if (dist4 > coarseThresholdManhattan) continue;

                        // dHash（缓存）
                        var dhI = cache.DHash.GetOrAdd(
                            keyI,
                            _ => new Lazy<byte[]>(() => _sig.ComputeDHash256(pi), LazyThreadSafetyMode.ExecutionAndPublication)
                        ).Value;

                        var dhJ = cache.DHash.GetOrAdd(
                            keyJ,
                            _ => new Lazy<byte[]>(() => _sig.ComputeDHash256(pj), LazyThreadSafetyMode.ExecutionAndPublication)
                        ).Value;

                        var hd = _sig.HammingDistance(dhI, dhJ);
                        if (hd > dhashHammingThreshold) continue;

                        // DSU 合并需要线程安全
                        lock (dsuLock)
                        {
                            dsu.Union(i, j);
                        }
                    }
                }
            });

        // 并行结束后补一次进度
        Report(90.0, $"相似比对 {Volatile.Read(ref donePairs)}/{totalPairs}（完成）");

        // ---------- 10%：输出连通分量 ----------
        var groupMap = new Dictionary<int, List<string>>();
        for (int i = 0; i < items.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            int root = dsu.Find(i);
            if (!groupMap.TryGetValue(root, out var list))
            {
                list = new List<string>();
                groupMap[root] = list;
            }
            list.Add(items[i].Path);

            if (i % 200 == 0 || i == items.Count - 1)
            {
                var percent = 90.0 + ((i + 1) * 10.0) / items.Count;
                Report(percent, $"生成结果 {i + 1}/{items.Count}");
            }
        }

        Report(100, "完成");
        return groupMap.Values.Where(g => g.Count >= 2).ToList();
    }

    private sealed class Dsu
    {
        private readonly int[] _p;
        private readonly int[] _r;

        public Dsu(int n)
        {
            _p = new int[n];
            _r = new int[n];
            for (int i = 0; i < n; i++) _p[i] = i;
        }

        public int Find(int x)
        {
            while (_p[x] != x)
            {
                _p[x] = _p[_p[x]];
                x = _p[x];
            }
            return x;
        }

        public void Union(int a, int b)
        {
            a = Find(a); b = Find(b);
            if (a == b) return;
            if (_r[a] < _r[b]) (a, b) = (b, a);
            _p[b] = a;
            if (_r[a] == _r[b]) _r[a]++;
        }
    }
}