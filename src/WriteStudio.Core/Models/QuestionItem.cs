namespace WriteStudio.Core.Models;

/// <summary>
/// Represents a multiple-choice question (MCQ) or query attached to a whiteboard page.
/// </summary>
public class QuestionItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string? QuestionNumber { get; set; }
    public string QuestionText { get; set; } = string.Empty;
    public List<QuestionOptionItem> Options { get; set; } = new();
    public string? CorrectAnswer { get; set; }
    public bool IsAnswerRevealed { get; set; }
    public double X { get; set; } = 80;
    public double Y { get; set; } = 60;
    public double Width { get; set; } = 880;
    public double FontSize { get; set; } = 24;
    public string FontFamily { get; set; } = "Arial";
    public string Theme { get; set; } = "auto";

    public QuestionItem Clone()
    {
        return new QuestionItem
        {
            Id = Id,
            QuestionNumber = QuestionNumber,
            QuestionText = QuestionText,
            Options = Options.Select(o => o.Clone()).ToList(),
            CorrectAnswer = CorrectAnswer,
            IsAnswerRevealed = IsAnswerRevealed,
            X = X,
            Y = Y,
            Width = Width,
            FontSize = FontSize,
            FontFamily = FontFamily,
            Theme = Theme
        };
    }
}

/// <summary>
/// Represents an individual option choice in a multiple-choice question.
/// </summary>
public class QuestionOptionItem
{
    public string Id { get; set; } = string.Empty;
    public string Label { get; set; } = "A";
    public string Text { get; set; } = string.Empty;

    public QuestionOptionItem Clone()
    {
        return new QuestionOptionItem
        {
            Id = Id,
            Label = Label,
            Text = Text
        };
    }
}
