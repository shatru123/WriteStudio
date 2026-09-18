using System.Text.RegularExpressions;
using WriteStudio.Core.Models;

namespace WriteStudio.Core.Parsing;

public class ParsedQuestion
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string QuestionNumber { get; set; } = string.Empty;
    public string QuestionText { get; set; } = string.Empty;
    public List<QuestionOptionItem> Options { get; set; } = new();
    public string CorrectAnswer { get; set; } = string.Empty;
    public string? Explanation { get; set; }
    public bool HasWarning { get; set; }
    public string? WarningText { get; set; }
    public string? Source { get; set; }

    public QuestionItem ToQuestionItem(int defaultIndex = 1, int fontSize = 26)
    {
        var options = new List<QuestionOptionItem>();
        for (int i = 0; i < Options.Count; i++)
        {
            var opt = Options[i];
            string label = !string.IsNullOrWhiteSpace(opt.Label) ? opt.Label.ToUpperInvariant() : ((char)('A' + i)).ToString();
            options.Add(new QuestionOptionItem
            {
                Label = label,
                Text = opt.Text ?? string.Empty
            });
        }

        if (options.Count < 2)
        {
            while (options.Count < 2)
            {
                options.Add(new QuestionOptionItem
                {
                    Label = ((char)('A' + options.Count)).ToString(),
                    Text = string.Empty
                });
            }
        }

        return new QuestionItem
        {
            Id = Guid.NewGuid(),
            QuestionNumber = !string.IsNullOrWhiteSpace(QuestionNumber) ? QuestionNumber : $"Question {defaultIndex}",
            QuestionText = QuestionText,
            Options = options,
            CorrectAnswer = CorrectAnswer?.ToUpperInvariant() ?? string.Empty,
            IsAnswerRevealed = false,
            X = 80,
            Y = 80,
            Width = 1000,
            FontSize = fontSize,
            FontFamily = "sans-serif"
        };
    }
}

public static class QuestionParser
{
    private static readonly Regex ExplicitQuestionHeaderRegex = new(
        @"^(?:(?:Question|Que|Ques|Prob|Problem|MCQ)\s*[:.#-]?\s*(\d+|[A-Za-z]+)\b(?:\s*[:.-])?|Q\s*[:.#-]?\s*(\d+)\b(?:\s*[:.-])?)\s*(.*)$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex BareNumberedQuestionRegex = new(
        @"^(\d+)[\.\)]\s+(.*)$",
        RegexOptions.Compiled);

    private static readonly Regex LetterOptionRegex = new(
        @"^(?:\(?\s*([A-Fa-f])\s*[\.\)\:\-]\s*|\(\s*([A-Fa-f])\s*\)\s*|\[\s*([A-Fa-f])\s*\]\s*)(.*)$",
        RegexOptions.Compiled);

    private static readonly Regex NumericOptionRegex = new(
        @"^(?:\(?\s*([1-6])\s*[\.\)\:\-]\s*|\(\s*([1-6])\s*\)\s*|\[\s*([1-6])\s*\]\s*)(.*)$",
        RegexOptions.Compiled);

    private static readonly Regex AnswerRegex = new(
        @"^(?:Correct\s*Answer|Answer|Ans|Key|Correct\s*Option)\s*[:=\-]?\s*(?:\(\s*([A-Fa-f1-6])\s*\)|\[\s*([A-Fa-f1-6])\s*\]|([A-Fa-f1-6]))",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static List<ParsedQuestion> ParseFromText(string rawText, string? source = null)
    {
        var results = new List<ParsedQuestion>();
        if (string.IsNullOrWhiteSpace(rawText)) return results;

        var lines = rawText.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None)
            .Select(l => l.Trim())
            .ToList();

        ParsedQuestion? current = null;
        var promptLines = new List<string>();
        bool inOptions = false;
        string? optionType = null;
        bool hasAnswer = false;
        int autoNumber = 1;

        void CommitCurrent()
        {
            if (current != null)
            {
                current.QuestionText = string.Join("\n", promptLines).Trim();
                if (string.IsNullOrWhiteSpace(current.QuestionNumber))
                {
                    current.QuestionNumber = $"Question {autoNumber++}";
                }

                if (current.Options.Count < 2)
                {
                    current.HasWarning = true;
                    current.WarningText = "Could not confidently identify 2 or more options.";
                }

                if (!string.IsNullOrWhiteSpace(current.QuestionText) || current.Options.Count > 0)
                {
                    results.Add(current);
                }
            }
            current = null;
            promptLines.Clear();
            inOptions = false;
            optionType = null;
            hasAnswer = false;
        }

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            // 1. Check for Correct Answer line
            var ansMatch = AnswerRegex.Match(line);
            if (ansMatch.Success && current != null)
            {
                string ansVal = ansMatch.Groups[1].Value;
                if (string.IsNullOrEmpty(ansVal)) ansVal = ansMatch.Groups[2].Value;
                if (string.IsNullOrEmpty(ansVal)) ansVal = ansMatch.Groups[3].Value;

                if (!string.IsNullOrEmpty(ansVal))
                {
                    ansVal = ansVal.ToUpperInvariant();
                    if (int.TryParse(ansVal, out int numVal) && numVal >= 1 && numVal <= 6)
                    {
                        ansVal = ((char)('A' + numVal - 1)).ToString();
                    }
                    current.CorrectAnswer = ansVal;
                }
                hasAnswer = true;
                continue;
            }

            // 2. Check for Explicit Question Header (e.g. "Question 1", "Q1.", "MCQ 1:")
            var explicitQMatch = ExplicitQuestionHeaderRegex.Match(line);
            if (explicitQMatch.Success)
            {
                CommitCurrent();
                current = new ParsedQuestion { Source = source };
                string numPart = explicitQMatch.Groups[1].Value;
                if (string.IsNullOrEmpty(numPart)) numPart = explicitQMatch.Groups[2].Value;
                current.QuestionNumber = !string.IsNullOrWhiteSpace(numPart) ? $"Question {numPart}" : $"Question {autoNumber++}";

                string promptPart = explicitQMatch.Groups[3].Value.Trim();
                if (!string.IsNullOrEmpty(promptPart))
                {
                    promptLines.Add(promptPart);
                }
                continue;
            }

            // 3. Check for Letter Option line (e.g. "A. Option", "(b) Option", "[C] Option")
            var letterOptMatch = LetterOptionRegex.Match(line);
            if (letterOptMatch.Success)
            {
                if (current == null)
                {
                    current = new ParsedQuestion { Source = source, QuestionNumber = $"Question {autoNumber++}" };
                }

                inOptions = true;
                optionType = "letter";
                string label = letterOptMatch.Groups[1].Value;
                if (string.IsNullOrEmpty(label)) label = letterOptMatch.Groups[2].Value;
                if (string.IsNullOrEmpty(label)) label = letterOptMatch.Groups[3].Value;
                string optText = letterOptMatch.Groups[4].Value.Trim();

                current.Options.Add(new QuestionOptionItem
                {
                    Label = label.ToUpperInvariant(),
                    Text = optText
                });
                continue;
            }

            // 4. Check for Numeric Option line (1..6)
            var numOptMatch = NumericOptionRegex.Match(line);
            if (numOptMatch.Success && int.TryParse(numOptMatch.Groups[1].Value, out int optNum))
            {
                if (inOptions && optionType == "numeric")
                {
                    string label = ((char)('A' + optNum - 1)).ToString();
                    string optText = numOptMatch.Groups[4].Value.Trim();
                    current!.Options.Add(new QuestionOptionItem
                    {
                        Label = label,
                        Text = optText
                    });
                    continue;
                }

                if (current != null && !inOptions && promptLines.Count > 0 && !hasAnswer)
                {
                    inOptions = true;
                    optionType = "numeric";
                    string label = ((char)('A' + optNum - 1)).ToString();
                    string optText = numOptMatch.Groups[4].Value.Trim();
                    current.Options.Add(new QuestionOptionItem
                    {
                        Label = label,
                        Text = optText
                    });
                    continue;
                }
            }

            // 5. Check for Bare Numbered Question (e.g. "1. What is...", "2. What is...")
            var bareQMatch = BareNumberedQuestionRegex.Match(line);
            if (bareQMatch.Success && (current == null || hasAnswer || (inOptions && optionType == "letter")))
            {
                CommitCurrent();
                current = new ParsedQuestion
                {
                    Source = source,
                    QuestionNumber = $"Question {bareQMatch.Groups[1].Value}"
                };
                string promptPart = bareQMatch.Groups[2].Value.Trim();
                if (!string.IsNullOrEmpty(promptPart))
                {
                    promptLines.Add(promptPart);
                }
                continue;
            }

            // 6. Continuation of Option or Prompt
            if (current != null)
            {
                if (inOptions && current.Options.Count > 0)
                {
                    var lastOpt = current.Options[^1];
                    lastOpt.Text += " " + line;
                }
                else
                {
                    promptLines.Add(line);
                }
            }
            else
            {
                current = new ParsedQuestion { Source = source };
                promptLines.Add(line);
            }
        }

        CommitCurrent();

        // If no questions detected, create a fallback draft if there was content
        if (results.Count == 0 && lines.Count > 0)
        {
            var fallback = new ParsedQuestion
            {
                Source = source,
                QuestionNumber = $"Question 1",
                QuestionText = string.Join("\n", lines.Take(10)),
                HasWarning = true,
                WarningText = "Auto-detection could not parse options. Please edit manually."
            };
            results.Add(fallback);
        }

        return results;
    }
}
