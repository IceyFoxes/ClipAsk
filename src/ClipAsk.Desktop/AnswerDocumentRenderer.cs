using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Navigation;
using Markdig;
using Markdig.Extensions.Mathematics;
using Markdig.Extensions.Tables;
using Markdig.Extensions.TaskLists;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using WpfMath.Controls;
using MarkdownBlock = Markdig.Syntax.Block;
using MarkdownTable = Markdig.Extensions.Tables.Table;
using MarkdownTableCell = Markdig.Extensions.Tables.TableCell;
using MarkdownTableRow = Markdig.Extensions.Tables.TableRow;
using WpfList = System.Windows.Documents.List;
using WpfTable = System.Windows.Documents.Table;
using WpfTableCell = System.Windows.Documents.TableCell;
using WpfTableRow = System.Windows.Documents.TableRow;

namespace ClipAsk.Desktop;

internal static class AnswerDocumentRenderer
{
    private static readonly Brush Primary = FrozenBrush(242, 244, 247);
    private static readonly Brush Secondary = FrozenBrush(174, 182, 194);
    private static readonly Brush Accent = FrozenBrush(120, 169, 255);
    private static readonly Brush CodeBackground = FrozenBrush(35, 39, 47);
    private static readonly Brush SubtleBackground = FrozenBrush(40, 45, 54);
    private static readonly Brush BorderBrush = FrozenBrush(70, 80, 93);
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAutoLinks()
        .UseEmphasisExtras()
        .UseMathematics()
        .UsePipeTables()
        .UseTaskLists()
        .DisableHtml()
        .Build();

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

        var source = NormalizeMathDelimiters((markdown ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal));
        RenderBlocks(Markdown.Parse(source, Pipeline), document.Blocks);
        return document;
    }

    internal static string NormalizeMathDelimiters(string source)
    {
        var output = new StringBuilder(source.Length);
        var inFence = false;
        var fenceCharacter = '\0';
        var fenceLength = 0;

        for (var lineStart = 0; lineStart <= source.Length;)
        {
            var lineEnd = source.IndexOf('\n', lineStart);
            var hasNewline = lineEnd >= 0;
            if (!hasNewline)
                lineEnd = source.Length;
            var line = source.AsSpan(lineStart, lineEnd - lineStart);
            var trimmed = line.TrimStart();
            var marker = trimmed.Length > 0 ? trimmed[0] : '\0';
            var markerLength = 0;
            while (markerLength < trimmed.Length && trimmed[markerLength] == marker)
                markerLength++;

            var isFenceMarker = marker is '`' or '~' && markerLength >= 3;
            if (!inFence && isFenceMarker)
            {
                inFence = true;
                fenceCharacter = marker;
                fenceLength = markerLength;
                output.Append(line);
            }
            else if (inFence)
            {
                output.Append(line);
                if (marker == fenceCharacter && markerLength >= fenceLength)
                    inFence = false;
            }
            else
            {
                NormalizeInlineMathDelimiters(line, output);
            }

            if (!hasNewline)
                break;
            output.Append('\n');
            lineStart = lineEnd + 1;
        }

        return output.ToString();
    }

    private static void NormalizeInlineMathDelimiters(ReadOnlySpan<char> line, StringBuilder output)
    {
        var inlineCodeTicks = 0;
        for (var index = 0; index < line.Length;)
        {
            if (line[index] == '`')
            {
                var runLength = 1;
                while (index + runLength < line.Length && line[index + runLength] == '`')
                    runLength++;
                output.Append(line.Slice(index, runLength));
                if (inlineCodeTicks == 0)
                    inlineCodeTicks = runLength;
                else if (inlineCodeTicks == runLength)
                    inlineCodeTicks = 0;
                index += runLength;
                continue;
            }

            if (inlineCodeTicks == 0 && line[index] == '\\' && index + 1 < line.Length)
            {
                if (line[index + 1] == '\\')
                {
                    output.Append("\\\\");
                    index += 2;
                    continue;
                }
                var delimiter = line[index + 1];
                if (delimiter is '(' or ')')
                {
                    output.Append('$');
                    index += 2;
                    continue;
                }
                if (delimiter is '[' or ']')
                {
                    output.Append("$$");
                    index += 2;
                    continue;
                }
            }

            output.Append(line[index]);
            index++;
        }
    }

    private static void RenderBlocks(IEnumerable<MarkdownBlock> source, BlockCollection target)
    {
        foreach (var block in source)
        {
            switch (block)
            {
                case HeadingBlock heading:
                    AddHeading(target, heading);
                    break;
                case ParagraphBlock paragraph:
                    AddParagraph(target, paragraph);
                    break;
                case MathBlock math:
                    AddDisplayFormula(target, math.Lines.ToString().Trim());
                    break;
                case FencedCodeBlock fenced:
                    AddCodeBlock(target, fenced.Info?.ToString() ?? string.Empty, fenced.Lines.ToString());
                    break;
                case CodeBlock code:
                    AddCodeBlock(target, string.Empty, code.Lines.ToString());
                    break;
                case QuoteBlock quote:
                    AddQuote(target, quote);
                    break;
                case ListBlock list:
                    AddList(target, list);
                    break;
                case MarkdownTable table:
                    AddTable(target, table);
                    break;
                case ThematicBreakBlock:
                    target.Add(new BlockUIContainer(new Border
                    {
                        Height = 1,
                        Background = BorderBrush,
                        Margin = new Thickness(0, 8, 0, 12)
                    }));
                    break;
                case ContainerBlock container:
                    RenderBlocks(container, target);
                    break;
                case LeafBlock leaf when leaf.Lines.Count > 0:
                    target.Add(new Paragraph(new Run(leaf.Lines.ToString())) { Margin = new Thickness(0, 0, 0, 8) });
                    break;
            }
        }
    }

    private static void AddHeading(BlockCollection target, HeadingBlock heading)
    {
        var sizes = new[] { 0d, 24d, 21d, 18d, 16d, 15d, 14d };
        var paragraph = new Paragraph
        {
            FontSize = sizes[Math.Clamp(heading.Level, 1, 6)],
            FontWeight = heading.Level <= 3 ? FontWeights.SemiBold : FontWeights.Medium,
            Margin = new Thickness(0, heading.Level <= 2 ? 8 : 4, 0, 8),
            KeepWithNext = true
        };
        RenderInlines(heading.Inline, paragraph.Inlines);
        target.Add(paragraph);
    }

    private static void AddParagraph(BlockCollection target, ParagraphBlock paragraph)
    {
        if (TryGetDisplayFormula(paragraph.Inline, out var formula))
        {
            AddDisplayFormula(target, formula);
            return;
        }

        var rendered = new Paragraph { Margin = new Thickness(0, 0, 0, 8) };
        RenderInlines(paragraph.Inline, rendered.Inlines);
        target.Add(rendered);
    }

    private static void AddCodeBlock(BlockCollection target, string language, string code)
    {
        if (!string.IsNullOrWhiteSpace(language))
        {
            target.Add(new Paragraph(new Run(language.Trim().ToUpperInvariant()))
            {
                Foreground = Secondary,
                FontSize = 11,
                Margin = new Thickness(8, 4, 8, 2)
            });
        }
        target.Add(new Paragraph(new Run(code.TrimEnd('\r', '\n')))
        {
            FontFamily = new FontFamily("Cascadia Mono, Consolas"),
            FontSize = 13,
            LineHeight = 20,
            Background = CodeBackground,
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 0, 0, 10)
        });
    }

    private static void AddQuote(BlockCollection target, QuoteBlock quote)
    {
        var section = new Section
        {
            Foreground = Secondary,
            BorderBrush = Accent,
            BorderThickness = new Thickness(3, 0, 0, 0),
            Padding = new Thickness(10, 0, 0, 0),
            Margin = new Thickness(0, 2, 0, 10)
        };
        RenderBlocks(quote, section.Blocks);
        target.Add(section);
    }

    private static void AddList(BlockCollection target, ListBlock list)
    {
        var rendered = new WpfList
        {
            MarkerStyle = list.IsOrdered ? TextMarkerStyle.Decimal : TextMarkerStyle.Disc,
            MarkerOffset = 8,
            Padding = new Thickness(22, 0, 0, 0),
            Margin = new Thickness(0, 0, 0, 8)
        };
        foreach (var item in list.OfType<ListItemBlock>())
        {
            var renderedItem = new ListItem { Margin = new Thickness(0, 0, 0, 3) };
            RenderBlocks(item, renderedItem.Blocks);
            rendered.ListItems.Add(renderedItem);
        }
        target.Add(rendered);
    }

    private static void AddTable(BlockCollection target, MarkdownTable table)
    {
        var rendered = new WpfTable
        {
            CellSpacing = 0,
            BorderBrush = BorderBrush,
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 2, 0, 10)
        };
        var group = new TableRowGroup();
        rendered.RowGroups.Add(group);

        foreach (var row in table.OfType<MarkdownTableRow>())
        {
            var renderedRow = new WpfTableRow
            {
                Background = row.IsHeader ? SubtleBackground : Brushes.Transparent,
                FontWeight = row.IsHeader ? FontWeights.SemiBold : FontWeights.Normal
            };
            foreach (var cell in row.OfType<MarkdownTableCell>())
            {
                var renderedCell = new WpfTableCell
                {
                    BorderBrush = BorderBrush,
                    BorderThickness = new Thickness(0, 0, 1, 1),
                    Padding = new Thickness(8, 5, 8, 5)
                };
                RenderBlocks(cell, renderedCell.Blocks);
                renderedRow.Cells.Add(renderedCell);
            }
            group.Rows.Add(renderedRow);
        }
        target.Add(rendered);
    }

    private static void RenderInlines(ContainerInline? source, InlineCollection target)
    {
        if (source is null)
            return;
        for (var current = source.FirstChild; current is not null; current = current.NextSibling)
        {
            switch (current)
            {
                case LiteralInline literal:
                    AddTextWithMath(target, literal.Content.ToString());
                    break;
                case CodeInline code:
                    target.Add(new Run(code.Content)
                    {
                        FontFamily = new FontFamily("Cascadia Mono, Consolas"),
                        FontSize = 13,
                        Background = CodeBackground,
                        Foreground = Primary
                    });
                    break;
                case MathInline math:
                    var formula = CreateFormula(math.Content.ToString(), 15);
                    if (formula is null)
                        target.Add(new Run("$" + math.Content + "$"));
                    else
                        target.Add(new InlineUIContainer(formula) { BaselineAlignment = BaselineAlignment.Center });
                    break;
                case TaskList task:
                    target.Add(new Run(task.Checked ? "☑ " : "☐ ") { Foreground = Secondary });
                    break;
                case EmphasisInline emphasis:
                    var span = new Span();
                    if (emphasis.DelimiterChar is '*' or '_')
                    {
                        if (emphasis.DelimiterCount >= 2)
                            span.FontWeight = FontWeights.SemiBold;
                        else
                            span.FontStyle = FontStyles.Italic;
                    }
                    else if (emphasis.DelimiterChar == '~' && emphasis.DelimiterCount >= 2)
                    {
                        span.TextDecorations = TextDecorations.Strikethrough;
                    }
                    else if (emphasis.DelimiterChar == '~')
                    {
                        span.BaselineAlignment = BaselineAlignment.Subscript;
                        span.FontSize = 11;
                    }
                    else if (emphasis.DelimiterChar == '^')
                    {
                        span.BaselineAlignment = BaselineAlignment.Superscript;
                        span.FontSize = 11;
                    }
                    RenderInlines(emphasis, span.Inlines);
                    target.Add(span);
                    break;
                case LinkInline link when link.IsImage:
                    target.Add(new Run("[Image: ") { Foreground = Secondary });
                    RenderInlines(link, target);
                    target.Add(new Run("]") { Foreground = Secondary });
                    break;
                case LinkInline link:
                    var hyperlink = new Hyperlink
                    {
                        Foreground = Accent,
                        TextDecorations = TextDecorations.Underline,
                        ToolTip = link.Url
                    };
                    if (Uri.TryCreate(link.Url, UriKind.Absolute, out var linkUri) && linkUri.Scheme is "https" or "http")
                    {
                        hyperlink.NavigateUri = linkUri;
                        hyperlink.RequestNavigate += OpenLink;
                    }
                    RenderInlines(link, hyperlink.Inlines);
                    target.Add(hyperlink);
                    break;
                case AutolinkInline autoLink:
                    target.Add(new Run(autoLink.Url) { Foreground = Accent, TextDecorations = TextDecorations.Underline });
                    break;
                case LineBreakInline lineBreak:
                    target.Add(lineBreak.IsHard ? new LineBreak() : new Run(" "));
                    break;
                case HtmlInline html:
                    target.Add(new Run(html.Tag) { Foreground = Secondary });
                    break;
                case HtmlEntityInline entity:
                    target.Add(new Run(entity.Transcoded.ToString()));
                    break;
                case ContainerInline container:
                    var nested = new Span();
                    RenderInlines(container, nested.Inlines);
                    target.Add(nested);
                    break;
            }
        }
    }

    private static void AddTextWithMath(InlineCollection target, string text)
    {
        var cursor = 0;
        while (cursor < text.Length)
        {
            var dollar = text.IndexOf('$', cursor);
            var slash = text.IndexOf("\\(", cursor, StringComparison.Ordinal);
            var start = dollar < 0 ? slash : slash < 0 ? dollar : Math.Min(dollar, slash);
            if (start < 0)
            {
                target.Add(new Run(text[cursor..]));
                return;
            }
            if (start > cursor)
                target.Add(new Run(text[cursor..start]));

            var slashDelimited = start == slash;
            var contentStart = start + (slashDelimited ? 2 : 1);
            var end = slashDelimited
                ? text.IndexOf("\\)", contentStart, StringComparison.Ordinal)
                : text.IndexOf('$', contentStart);
            if (end < 0)
            {
                target.Add(new Run(text[start..]));
                return;
            }

            var formula = text[contentStart..end];
            var control = CreateFormula(formula, 15);
            if (control is null)
                target.Add(new Run(text[start..(end + (slashDelimited ? 2 : 1))]));
            else
                target.Add(new InlineUIContainer(control) { BaselineAlignment = BaselineAlignment.Center });
            cursor = end + (slashDelimited ? 2 : 1);
        }
    }

    private static bool TryGetDisplayFormula(ContainerInline? inline, out string formula)
    {
        var text = GetPlainText(inline).Trim();
        if (text.Length > 4 && text.StartsWith("$$", StringComparison.Ordinal) && text.EndsWith("$$", StringComparison.Ordinal))
        {
            formula = text[2..^2].Trim();
            return true;
        }
        if (text.Length > 4 && text.StartsWith("\\[", StringComparison.Ordinal) && text.EndsWith("\\]", StringComparison.Ordinal))
        {
            formula = text[2..^2].Trim();
            return true;
        }
        formula = string.Empty;
        return false;
    }

    private static string GetPlainText(ContainerInline? source)
    {
        if (source is null)
            return string.Empty;
        var builder = new StringBuilder();
        AppendPlainText(source, builder);
        return builder.ToString();
    }

    private static void AppendPlainText(ContainerInline source, StringBuilder builder)
    {
        for (var current = source.FirstChild; current is not null; current = current.NextSibling)
        {
            switch (current)
            {
                case LiteralInline literal:
                    builder.Append(literal.Content.ToString());
                    break;
                case CodeInline code:
                    builder.Append(code.Content);
                    break;
                case MathInline math:
                    builder.Append('$').Append(math.Content.ToString()).Append('$');
                    break;
                case LineBreakInline:
                    builder.AppendLine();
                    break;
                case ContainerInline container:
                    AppendPlainText(container, builder);
                    break;
            }
        }
    }

    private static void AddDisplayFormula(BlockCollection target, string formula)
    {
        var control = CreateFormula(formula, 18);
        if (control is null)
        {
            target.Add(new Paragraph(new Run("$$" + formula + "$$")) { Margin = new Thickness(0, 2, 0, 10) });
            return;
        }
        target.Add(new BlockUIContainer(control) { Margin = new Thickness(0, 4, 0, 12) });
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

    private static void OpenLink(object sender, RequestNavigateEventArgs args)
    {
        if (args.Uri.Scheme is "https" or "http")
            Process.Start(new ProcessStartInfo(args.Uri.AbsoluteUri) { UseShellExecute = true });
        args.Handled = true;
    }

    private static Brush FrozenBrush(byte red, byte green, byte blue)
    {
        var brush = new SolidColorBrush(Color.FromRgb(red, green, blue));
        brush.Freeze();
        return brush;
    }
}
