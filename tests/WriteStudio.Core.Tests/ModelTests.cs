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

    [Fact]
    public void QuestionParser_SingleQuestion_ParsesPromptOptionsAndAnswer()
    {
        string text = """
            1. What is the capital of Maharashtra?

            A. Mumbai
            B. Pune
            C. Nagpur
            D. Nashik

            Answer: A
            """;

        var questions = WriteStudio.Core.Parsing.QuestionParser.ParseFromText(text, "Slide 1");
        questions.Should().HaveCount(1);

        var q = questions[0];
        q.QuestionNumber.Should().Be("Question 1");
        q.QuestionText.Should().Be("What is the capital of Maharashtra?");
        q.Options.Should().HaveCount(4);
        q.Options[0].Label.Should().Be("A");
        q.Options[0].Text.Should().Be("Mumbai");
        q.Options[1].Label.Should().Be("B");
        q.Options[1].Text.Should().Be("Pune");
        q.CorrectAnswer.Should().Be("A");

        var item = q.ToQuestionItem(1);
        item.QuestionText.Should().Be("What is the capital of Maharashtra?");
        item.CorrectAnswer.Should().Be("A");
        item.Options.Should().HaveCount(4);
    }

    [Fact]
    public void QuestionParser_MultipleQuestionsInSingleDocument_ParsesAllInOrder()
    {
        string text = """
            Question 1
            What is C#?

            A) Programming Language
            B) Database
            C) OS
            D) Browser
            Ans: A


            Question 2
            What is .NET?

            (a) Framework
            (b) Database
            (c) Browser
            (d) Operating System
            Correct Answer: A. Framework


            Question 3
            What is ASP.NET Core?

            1. Web Framework
            2. Language
            3. Database
            4. IDE
            Key: 1
            """;

        var questions = WriteStudio.Core.Parsing.QuestionParser.ParseFromText(text, "Document.pdf");
        questions.Should().HaveCount(3);

        questions[0].QuestionNumber.Should().Be("Question 1");
        questions[0].QuestionText.Should().Be("What is C#?");
        questions[0].Options.Should().HaveCount(4);
        questions[0].CorrectAnswer.Should().Be("A");

        questions[1].QuestionNumber.Should().Be("Question 2");
        questions[1].QuestionText.Should().Be("What is .NET?");
        questions[1].Options.Should().HaveCount(4);
        questions[1].Options[0].Label.Should().Be("A");
        questions[1].CorrectAnswer.Should().Be("A");

        questions[2].QuestionNumber.Should().Be("Question 3");
        questions[2].QuestionText.Should().Be("What is ASP.NET Core?");
        questions[2].Options.Should().HaveCount(4);
        questions[2].Options[0].Label.Should().Be("A");
        questions[2].CorrectAnswer.Should().Be("A");
    }

    [Fact]
    public void QuestionParser_BareNumberedConsecutiveQuestions_ParsesSuccessfully()
    {
        string text = """
            1. What does HTML stand for?
            A. Hyper Text Markup Language
            B. High Text Marking Language
            Ans: A

            2. What does CSS stand for?
            A. Creative Style Sheets
            B. Cascading Style Sheets
            Ans: B
            """;

        var questions = WriteStudio.Core.Parsing.QuestionParser.ParseFromText(text, "Slide 1");
        questions.Should().HaveCount(2);

        questions[0].QuestionNumber.Should().Be("Question 1");
        questions[0].QuestionText.Should().Be("What does HTML stand for?");
        questions[0].Options.Should().HaveCount(2);
        questions[0].CorrectAnswer.Should().Be("A");

        questions[1].QuestionNumber.Should().Be("Question 2");
        questions[1].QuestionText.Should().Be("What does CSS stand for?");
        questions[1].Options.Should().HaveCount(2);
        questions[1].CorrectAnswer.Should().Be("B");
    }

    [Fact]
    public void QuestionParser_MalformedTextWithoutOptions_SetsWarningFlag()
    {
        string text = "This is a random slide title with no options.";

        var questions = WriteStudio.Core.Parsing.QuestionParser.ParseFromText(text, "Slide 10");
        questions.Should().HaveCount(1);
        questions[0].HasWarning.Should().BeTrue();
        questions[0].WarningText.Should().NotBeNullOrEmpty();

        var item = questions[0].ToQuestionItem(1);
        item.QuestionNumber.Should().Be("Question 1");
        item.Options.Should().HaveCount(2); // padded with default empty options
    }
}

