using FImageStack.Core.Models;
using FImageStack.Core.Quality;
using Xunit;

namespace FImageStack.Core.Tests;

public class ShotQualityPredictionTests
{
    [Fact]
    public void ShotQualityPredictor_PredictQuality_PristineStack_ShouldYieldStudioQuality()
    {
        int w = 24;
        int h = 24;
        int frameCount = 5;
        var frames = new List<StackFrame>();

        for (int i = 0; i < frameCount; i++)
        {
            var frame = new StackFrame
            {
                Index = i,
                Width = w,
                Height = h,
                AlignmentConfidence = 0.98,
                FocusMap = new ImageBuffer<float>(w, h)
            };

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    // Continuous smooth focus overlap
                    bool inFocus = (y >= i * 4 && y < (i + 2) * 4);
                    frame.FocusMap.At(x, y) = inFocus ? 0.95f : 0.40f;
                }
            }
            frames.Add(frame);
        }

        var predictor = new ShotQualityPredictor();
        var scorecard = predictor.PredictQuality(frames);

        Assert.NotNull(scorecard);
        Assert.True(scorecard.ExpectedCoveragePercentage >= 90.0f, $"Coverage was {scorecard.ExpectedCoveragePercentage}%");
        Assert.True(scorecard.ExpectedSharpnessPercentage >= 85.0f, $"Sharpness was {scorecard.ExpectedSharpnessPercentage}%");
        Assert.True(scorecard.ExpectedAlignmentPercentage >= 95.0f, $"Alignment was {scorecard.ExpectedAlignmentPercentage}%");
        Assert.True(scorecard.FinalExpectedQualityScore >= 90.0f, $"Final score was {scorecard.FinalExpectedQualityScore}%");
        Assert.Equal(QualityGrade.GradeAPlus, scorecard.Grade);
        Assert.Contains("Studio Master", scorecard.GradeTitle);
        Assert.Contains("ready to render", scorecard.SummaryMessage);

        foreach (var f in frames) f.Dispose();
    }

    [Fact]
    public void ShotQualityPredictor_PredictQuality_GappyStack_ShouldRecommendAdditionalFrames()
    {
        int w = 24;
        int h = 24;
        int frameCount = 4;
        var frames = new List<StackFrame>();

        // Frames have isolated focus strips with zero overlap (Focus gaps)
        for (int i = 0; i < frameCount; i++)
        {
            var frame = new StackFrame
            {
                Index = i,
                Width = w,
                Height = h,
                AlignmentConfidence = 0.85,
                FocusMap = new ImageBuffer<float>(w, h)
            };

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    bool inFocus = (y == i * 6);
                    frame.FocusMap.At(x, y) = inFocus ? 0.95f : 0.0f;
                }
            }
            frames.Add(frame);
        }

        var predictor = new ShotQualityPredictor();
        var scorecard = predictor.PredictQuality(frames);

        Assert.True(scorecard.ExpectedCoveragePercentage < 95.0f);
        Assert.NotEmpty(scorecard.Recommendations);
        Assert.True(scorecard.Recommendations[0].RecommendedFrameCount >= 2);
        Assert.Contains("Recommend", scorecard.SummaryMessage);
        Assert.Contains("additional frames", scorecard.SummaryMessage);

        foreach (var f in frames) f.Dispose();
    }
}
