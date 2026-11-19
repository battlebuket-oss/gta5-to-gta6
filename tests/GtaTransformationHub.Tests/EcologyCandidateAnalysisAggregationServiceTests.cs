using GtaTransformationHub.Services;
using Xunit;

namespace GtaTransformationHub.Tests;

public sealed class EcologyCandidateAnalysisAggregationServiceTests
{
    [Fact]
    public void EvaluateBatchCoverage_AcceptsEveryEligibleCandidateAcrossMultipleBatches()
    {
        var coverage = EcologyCandidateAnalysisAggregationService.EvaluateBatchCoverage(
            expectedCandidateCount: 135,
            candidateIndexes: Enumerable.Range(0, 135),
            discoveryComplete: true,
            allBatchesComplete: true);

        Assert.True(coverage.EligibleMetadataResourceCoverageComplete);
        Assert.Equal(135, coverage.UniqueCandidateIndexes);
        Assert.Empty(coverage.MissingCandidateIndexes);
        Assert.Empty(coverage.Warnings);
    }

    [Fact]
    public void EvaluateBatchCoverage_RejectsMissingCandidates()
    {
        var indexes = Enumerable.Range(0, 135).Where(index => index != 52 && index != 104);

        var coverage = EcologyCandidateAnalysisAggregationService.EvaluateBatchCoverage(
            135, indexes, discoveryComplete: true, allBatchesComplete: true);

        Assert.False(coverage.EligibleMetadataResourceCoverageComplete);
        Assert.Equal(new[] { 52, 104 }, coverage.MissingCandidateIndexes);
        Assert.Contains(coverage.Warnings, warning => warning.Contains("do not yet have", StringComparison.Ordinal));
    }

    [Fact]
    public void EvaluateBatchCoverage_RejectsIncompleteDiscoveryEvenWhenEveryFoundCandidateWasDumped()
    {
        var coverage = EcologyCandidateAnalysisAggregationService.EvaluateBatchCoverage(
            20, Enumerable.Range(0, 20), discoveryComplete: false, allBatchesComplete: true);

        Assert.False(coverage.EligibleMetadataResourceCoverageComplete);
        Assert.Contains(coverage.Warnings, warning => warning.Contains("discovery itself is incomplete", StringComparison.Ordinal));
    }

    [Fact]
    public void EvaluateBatchCoverage_RejectsExtractionFailuresEvenWhenCandidateIndexesArePresent()
    {
        var coverage = EcologyCandidateAnalysisAggregationService.EvaluateBatchCoverage(
            20, Enumerable.Range(0, 20), discoveryComplete: true, allBatchesComplete: false);

        Assert.False(coverage.EligibleMetadataResourceCoverageComplete);
        Assert.Contains(coverage.Warnings, warning => warning.Contains("dump batches contain extraction errors", StringComparison.Ordinal));
    }

    [Fact]
    public void EvaluateBatchCoverage_RejectsDuplicateCandidateIndexes()
    {
        var indexes = Enumerable.Range(0, 20).Concat(new[] { 4, 8 });

        var coverage = EcologyCandidateAnalysisAggregationService.EvaluateBatchCoverage(
            20, indexes, discoveryComplete: true, allBatchesComplete: true);

        Assert.False(coverage.EligibleMetadataResourceCoverageComplete);
        Assert.Equal(new[] { 4, 8 }, coverage.DuplicateCandidateIndexes);
    }

    [Fact]
    public void EvaluateBatchCoverage_RejectsOutOfRangeCandidateIndexes()
    {
        var indexes = Enumerable.Range(0, 20).Concat(new[] { -1, 20 });

        var coverage = EcologyCandidateAnalysisAggregationService.EvaluateBatchCoverage(
            20, indexes, discoveryComplete: true, allBatchesComplete: true);

        Assert.False(coverage.EligibleMetadataResourceCoverageComplete);
        Assert.Contains(coverage.Warnings, warning => warning.Contains("outside the discovery report", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(150001)]
    public void EvaluateBatchCoverage_RejectsInvalidExpectedCandidateCount(int count)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            EcologyCandidateAnalysisAggregationService.EvaluateBatchCoverage(
                count, Array.Empty<int>(), discoveryComplete: false, allBatchesComplete: false));
    }
}
