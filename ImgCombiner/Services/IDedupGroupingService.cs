namespace ImgCombiner.Services;

public interface IDedupGroupingService
{
    IReadOnlyList<List<string>> FindSimilarGroups(
        IReadOnlyList<string> paths,
        int coarseThresholdManhattan,
        double ratioEps,
        int dhashHammingThreshold,
        IProgress<(double percent, string text)>? progress,
        CancellationToken ct);
}