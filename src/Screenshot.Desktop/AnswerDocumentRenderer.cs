using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using WpfMath.Controls;

namespace Screenshot.Desktop;

internal static class AnswerDocumentRenderer
{
    private static readonly Brush Primary = new SolidColorBrush(Color.FromRgb(242, 244, 247));
    private static readonly Brush Secondary = new SolidColorBrush(Color.FromRgb(174, 182, 194));
    private static readonly Brush CodeBackground = new SolidColorBrush(Color.FromRgb(35, 39, 47));

    public static FlowDocument Create(string markdown)
    {
        var document = new FlowDocument
        {
            PagePadding = new Thickness(0),
            FontFamily = new FontFamily("Aptos, Segoe UI Variable Text, Segoe UI"),
            FontSize = 15,
            Foreground = Primary,
            LineHeight = 22
        };

        var lines = (markdown ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var paragraphLines = new List<string>();
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            if (line.StartsWith("```", StringComparison.Ordinal))
            {
                FlushParagraph(document, paragraphLines);
                var language = line[3..].Trim();
                var code = new List<string>();
                while (++index < lines.Length && !lines[index].StartsWith("```", StringComparison.Ordinal))
                    code.Add(lines[index]);
                AddCodeBlock(document, language, string.Join("\n", code));
                continue;
            }

            if (line.TrimStart().StartsWith("$$", StringComparison.Ordinal) || line.TrimStart().StartsWith("\\[", StringComparison.Ordinal))
            {
                FlushParagraph(document, paragraphLines);
                var trimmed = line.Trim();
                var bracketed = trimmed.StartsWith("\\[", StringComparison.Ordinal);
                var closing = bracketed ? "\\]" : "$$";
                var openingLength = 2;
                var formula = trimmed[openingLength..];
                if (formula.EndsWith(closing, StringComparison.Ordinal))
                {
                    formula = formula[..^closing.Length];
                }
                else
                {
                    var parts = new List<string> { formula };
                    while (++index < lines.Length)
                    {
                        var candidate = lines[index].Trim();
                        if (candidate.EndsWith(closing, StringComparison.Ordinal))
                        {
                            parts.Add(candidate[..^closing.Length]);
                            break;
                        }
                        parts.Add(lines[index]);
                    }
                    formula = string.Join("\n", parts);
                }
                AddDisplayFormula(document, formula.Trim());
                continue;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                FlushParagraph(document, paragraphLines);
                continue;
            }

            paragraphLines.Add(line);
        }
        FlushParagraph(document, paragraphLines);
        return document;
    }

    private static void FlushParagraph(FlowDocument document, List<string> lines)
    {
        if (lines.Count == 0)
            return;
        var paragraph = new Paragraph { Margin = new Thickness(0, 0, 0, 8) };
        AddInlineContent(paragraph, string.Join("\n", lines));
        document.Blocks.Add(paragraph);
        lines.Clear();
    }

    private static void AddCodeBlock(FlowDocument document, string language, string code)
    {
        if (!string.IsNullOrWhiteSpace(language))
        {
            document.Blocks.Add(new Paragraph(new Run(language.ToUpperInvariant()))
            {
                Foreground = Secondary,
                FontSize = 11,
                Margin = new Thickness(8, 4, 8, 2)
            });
        }
        document.Blocks.Add(new Paragraph(new Run(code))
        {
            FontFamily = new FontFamily("Cascadia Mono, Consolas"),
            FontSize = 13,
            LineHeight = 20,
            Background = CodeBackground,
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 0, 0, 10)
        });
    }

    private static void AddDisplayFormula(FlowDocument document, string formula)
    {
        var control = CreateFormula(formula, 18);
        if (control is null)
        {
            document.Blocks.Add(new Paragraph(new Run("$$" + formula + "$$")) { Margin = new Thickness(0, 2, 0, 10) });
            return;
        }
        document.Blocks.Add(new BlockUIContainer(control) { Margin = new Thickness(0, 4, 0, 12) });
    }

    private static void AddInlineContent(Paragraph paragraph, string text)
    {
        var cursor = 0;
        while (cursor < text.Length)
        {
            var dollar = text.IndexOf('$', cursor);
            var slash = text.IndexOf("\\(", cursor, StringComparison.Ordinal);
            var start = dollar < 0 ? slash : slash < 0 ? dollar : Math.Min(dollar, slash);
            if (start < 0)
            {
                paragraph.Inlines.Add(new Run(text[cursor..]));
                return;
            }
            if (start > cursor)
                paragraph.Inlines.Add(new Run(text[cursor..start]));

            var slashDelimited = start == slash;
            var contentStart = start + (slashDelimited ? 2 : 1);
            var end = slashDelimited
                ? text.IndexOf("\\)", contentStart, StringComparison.Ordinal)
                : text.IndexOf('$', contentStart);
            if (end < 0)
            {
                paragraph.Inlines.Add(new Run(text[start..]));
                return;
            }

            var formula = text[contentStart..end];
            var control = CreateFormula(formula, 15);
            if (control is null)
                paragraph.Inlines.Add(new Run(text[start..(end + (slashDelimited ? 2 : 1))]));
            else
                paragraph.Inlines.Add(new InlineUIContainer(control) { BaselineAlignment = BaselineAlignment.Center });
            cursor = end + (slashDelimited ? 2 : 1);
        }
    }

    private static FormulaControl? CreateFormula(string formula, double scale)
    {
        if (string.IsNullOrWhiteSpace(formula))
            return null;
        var control = new FormulaControl
        {
            Formula = formula,
            Scale = scale,
            Foreground = Primary,
            Background = Brushes.Transparent,
            SystemTextFontName = "Segoe UI"
        };
        return control.HasError ? null : control;
    }
}
