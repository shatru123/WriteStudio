using System.Text.Json;
using FluentAssertions;
using WriteStudio.Core.Models;
using Xunit;

namespace WriteStudio.Core.Tests;

public class ModelTests
{
    [Fact]
    public void ColorInfo_HexConversion_RoundtripsAccurately()
    {
        var color = new ColorInfo(200, 100, 50, 255);
        string hex = color.ToHex(includeAlpha: false);
        hex.Should().Be("#C86432");

        var restored = ColorInfo.FromHex(hex);
        restored.R.Should().Be(200);
        restored.G.Should().Be(100);
        restored.B.Should().Be(50);
        restored.A.Should().Be(255);
    }

    [Fact]
    public void RectBounds_FromPoints_ComputesBoundingBoxWithPadding()
    {
        var points = new List<DrawingPoint>
        {
            DrawingPoint.Create(10, 20),
            DrawingPoint.Create(100, 150),
            DrawingPoint.Create(50, 80)
        };

        var bounds = RectBounds.FromPoints(points, padding: 5.0);
        bounds.X.Should().Be(5.0);
        bounds.Y.Should().Be(15.0);
        bounds.Width.Should().Be(100.0);
        bounds.Height.Should().Be(140.0);
        bounds.Contains(50, 80).Should().BeTrue();
    }

    [Fact]
    public void TimelineEvent_PolymorphicSerialization_RoundtripsSuccessfully()
    {
        var stroke = new DrawingStroke
        {
            Id = Guid.NewGuid(),
            PageIndex = 0,
            Thickness = 4.5,
            Points = new List<DrawingPoint>
            {
                DrawingPoint.Create(10, 10, 0.8f, TimeSpan.FromSeconds(1)),
                DrawingPoint.Create(20, 25, 0.9f, TimeSpan.FromSeconds(1.2))
            }
        };

        TimelineEvent originalEvent = new StrokeStartedTimelineEvent
        {
            Timestamp = TimeSpan.FromSeconds(1.0),
            Stroke = stroke
        };

        string json = JsonSerializer.Serialize(originalEvent);
        json.Should().Contain("StrokeStarted");

        var deserialized = JsonSerializer.Deserialize<TimelineEvent>(json);
        deserialized.Should().BeOfType<StrokeStartedTimelineEvent>();
        var typed = (StrokeStartedTimelineEvent)deserialized!;
        typed.Stroke.Id.Should().Be(stroke.Id);
        typed.Stroke.Points.Should().HaveCount(2);
    }

    [Fact]
    public void WhiteboardPage_Clone_WithQuestion_DeepCopiesProperly()
    {
        var page = new WhiteboardPage
        {
            Id = Guid.NewGuid(),
            Index = 1,
            Title = "Page 2",
            Background = BackgroundStyle.Blackboard,
            Question = new QuestionItem
            {
                Id = Guid.NewGuid(),
                QuestionNumber = "Question 1",
                QuestionText = "What is the capital of France?",
                Options = new List<QuestionOptionItem>
                {
                    new() { Label = "A", Text = "Berlin" },
                    new() { Label = "B", Text = "Paris" },
                    new() { Label = "C", Text = "Rome" }
                },
                CorrectAnswer = "B",
                IsAnswerRevealed = true,
                X = 100,
                Y = 120,
                Width = 900,
                FontSize = 24
            }
        };

        var clone = page.Clone();

        clone.Id.Should().Be(page.Id);
        clone.Question.Should().NotBeNull();
        clone.Question!.QuestionText.Should().Be("What is the capital of France?");
        clone.Question.Options.Should().HaveCount(3);
        clone.Question.Options[1].Label.Should().Be("B");
        clone.Question.CorrectAnswer.Should().Be("B");
        clone.Question.IsAnswerRevealed.Should().BeTrue();

        // Mutating clone does not mutate original
        clone.Question.Options.Add(new QuestionOptionItem { Label = "D", Text = "Madrid" });
        page.Question.Options.Should().HaveCount(3);
    }
}

