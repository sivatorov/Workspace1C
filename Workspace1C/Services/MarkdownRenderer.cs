using System;
using System.Collections.Generic;
using System.Text;

// Платформенные using-директивы изолированы блоками #if: в сборке WPF (Windows)
// доступны только типы WPF, в сборке Avalonia (Linux) — только типы Avalonia.
// Общая часть (парсер и IR) не зависит от UI-фреймворков и компилируется в обеих.
#if WINDOWS
using System.Diagnostics;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
#elif LINUX
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using Workspace1C.Themes;
#endif

namespace Workspace1C.Services
{
    // ----------------------------------------------------------------------
    // Общее представление разобранного markdown (платформонезависимое).
    // ----------------------------------------------------------------------

    internal enum MdBlockKind
    {
        Header,
        Paragraph,
        BulletList,
        OrderedList,
        CodeBlock,
        HorizontalRule
    }

    internal enum MdInlineKind
    {
        Text,
        Bold,
        Italic,
        Code,
        Link
    }

    /// <summary>Фрагмент inline-разметки (строка, жирный, курсив, код, ссылка).</summary>
    internal sealed class MdInline
    {
        public MdInlineKind Kind;
        public string Text = "";
        public string Url = "";
        public List<MdInline>? Children;
    }

    /// <summary>Блок markdown (заголовок, абзац, список, код, разделитель).</summary>
    internal sealed class MdBlock
    {
        public MdBlockKind Kind;
        public int Level;
        public List<MdInline> Inlines = new();
        public List<List<MdInline>> Items = new();
        public List<string> CodeLines = new();
    }

    /// <summary>
    /// Лёгкий парсер подмножества markdown, необходимого для текста «Что нового»:
    /// заголовки (#, ##, ###), жирный (**), курсив (*), инлайн-код (`), код-блоки (```),
    /// маркированные/нумерованные списки, ссылки [текст](url), переносы строк и
    /// горизонтальные разделители. Не использует внешних зависимостей.
    /// </summary>
    internal static class MarkdownParser
    {
        public static List<MdBlock> Parse(string markdown)
        {
            var blocks = new List<MdBlock>();
            if (markdown is null)
                return blocks;

            var lines = markdown.Replace("\r\n", "\n").Split('\n');
            MdBlock? paragraph = null;
            MdBlock? list = null;
            var codeLines = new List<string>();
            var inCode = false;

            void FlushParagraph()
            {
                if (paragraph is not null) { blocks.Add(paragraph); paragraph = null; }
            }

            void FlushList()
            {
                if (list is not null) { blocks.Add(list); list = null; }
            }

            foreach (var raw in lines)
            {
                if (inCode)
                {
                    if (raw.TrimStart().StartsWith("```"))
                    {
                        inCode = false;
                        blocks.Add(new MdBlock { Kind = MdBlockKind.CodeBlock, CodeLines = codeLines });
                        codeLines = new List<string>();
                    }
                    else
                    {
                        codeLines.Add(raw);
                    }
                    continue;
                }

                var trimmed = raw.Trim();

                // Открытие блока кода.
                if (trimmed.StartsWith("```"))
                {
                    FlushParagraph();
                    FlushList();
                    inCode = true;
                    codeLines = new List<string>();
                    continue;
                }

                if (string.IsNullOrWhiteSpace(trimmed))
                {
                    FlushParagraph();
                    FlushList();
                    continue;
                }

                if (IsHorizontalRule(trimmed))
                {
                    FlushParagraph();
                    FlushList();
                    blocks.Add(new MdBlock { Kind = MdBlockKind.HorizontalRule });
                    continue;
                }

                // Заголовок (#, ##, ...).
                if (trimmed[0] == '#' && IsHeader(trimmed, out var level, out var headerRest))
                {
                    FlushParagraph();
                    FlushList();
                    blocks.Add(new MdBlock
                    {
                        Kind = MdBlockKind.Header,
                        Level = level,
                        Inlines = ParseInlines(headerRest)
                    });
                    continue;
                }

                // Маркированный список.
                var bulletPrefix = MatchBullet(trimmed);
                if (bulletPrefix > 0)
                {
                    FlushParagraph();
                    if (list is null || list.Kind != MdBlockKind.BulletList)
                    {
                        FlushList();
                        list = new MdBlock { Kind = MdBlockKind.BulletList };
                    }
                    list.Items.Add(ParseInlines(trimmed.Substring(bulletPrefix)));
                    continue;
                }

                // Нумерованный список.
                var orderedPrefix = MatchOrdered(trimmed);
                if (orderedPrefix > 0)
                {
                    FlushParagraph();
                    if (list is null || list.Kind != MdBlockKind.OrderedList)
                    {
                        FlushList();
                        list = new MdBlock { Kind = MdBlockKind.OrderedList };
                    }
                    list.Items.Add(ParseInlines(trimmed.Substring(orderedPrefix)));
                    continue;
                }

                // Обычный абзац; соседние строки склеиваются пробелом.
                FlushList();
                if (paragraph is null)
                    paragraph = new MdBlock { Kind = MdBlockKind.Paragraph };

                if (paragraph.Inlines.Count > 0)
                    paragraph.Inlines.Add(new MdInline { Kind = MdInlineKind.Text, Text = " " });
                paragraph.Inlines.AddRange(ParseInlines(trimmed));
            }

            if (inCode)
                blocks.Add(new MdBlock { Kind = MdBlockKind.CodeBlock, CodeLines = codeLines });
            FlushParagraph();
            FlushList();
            return blocks;
        }

        private static bool IsHeader(string trimmed, out int level, out string rest)
        {
            var lvl = 0;
            while (lvl < trimmed.Length && trimmed[lvl] == '#')
                lvl++;
            rest = trimmed.Substring(lvl).Trim();
            if (lvl >= 1 && lvl <= 6 && rest.Length > 0)
            {
                level = lvl;
                return true;
            }
            level = 0;
            rest = string.Empty;
            return false;
        }

        private static bool IsHorizontalRule(string trimmed)
        {
            var compact = trimmed.Replace(" ", "").Replace("\t", "");
            if (compact.Length < 3)
                return false;
            var c = compact[0];
            return (c == '-' || c == '*' || c == '_') && AllSame(compact, c);
        }

        private static bool AllSame(string s, char c)
        {
            foreach (var ch in s)
                if (ch != c)
                    return false;
            return true;
        }

        /// <summary>Длина префикса маркера маркированного списка («- », «* », «+ ») или 0.</summary>
        private static int MatchBullet(string trimmed)
        {
            if (trimmed.Length >= 2 && (trimmed[0] == '-' || trimmed[0] == '*' || trimmed[0] == '+'))
            {
                if (trimmed[1] == ' ')
                    return 2;
                if (trimmed.Length == 1)
                    return 1;
            }
            return 0;
        }

        /// <summary>Длина префикса нумерованного элемента («1. », «1) ») или 0.</summary>
        private static int MatchOrdered(string trimmed)
        {
            var i = 0;
            while (i < trimmed.Length && char.IsDigit(trimmed[i]))
                i++;
            if (i == 0)
                return 0;
            if (i < trimmed.Length && (trimmed[i] == '.' || trimmed[i] == ')'))
            {
                i++;
                while (i < trimmed.Length && trimmed[i] == ' ')
                    i++;
                return i;
            }
            return 0;
        }

        /// <summary>
        /// Разбирает inline-разметку строки в список фрагментов.
        /// Поддерживает **жирный**, *курсив*, `код`, [текст](ссылка) и экранирование
        /// обратным слэшем базовых спецсимволов.
        /// </summary>
        private static List<MdInline> ParseInlines(string text)
        {
            var result = new List<MdInline>();
            var buf = new StringBuilder();
            var i = 0;

            void FlushText()
            {
                if (buf.Length > 0)
                {
                    result.Add(new MdInline { Kind = MdInlineKind.Text, Text = buf.ToString() });
                    buf.Clear();
                }
            }

            while (i < text.Length)
            {
                var c = text[i];

                // Экранирование спецсимвола: \*, \#, \[ и т.п. — выводим как есть.
                if (c == '\\' && i + 1 < text.Length)
                {
                    buf.Append(text[i + 1]);
                    i += 2;
                    continue;
                }

                if (c == '`')
                {
                    var close = text.IndexOf('`', i + 1);
                    if (close > i)
                    {
                        FlushText();
                        result.Add(new MdInline
                        {
                            Kind = MdInlineKind.Code,
                            Text = text.Substring(i + 1, close - i - 1)
                        });
                        i = close + 1;
                        continue;
                    }
                }
                else if (c == '*' && i + 1 < text.Length && text[i + 1] == '*')
                {
                    var close = text.IndexOf("**", i + 2, StringComparison.Ordinal);
                    if (close > i)
                    {
                        FlushText();
                        result.Add(new MdInline
                        {
                            Kind = MdInlineKind.Bold,
                            Children = ParseInlines(text.Substring(i + 2, close - i - 2))
                        });
                        i = close + 2;
                        continue;
                    }
                }
                else if (c == '*' || c == '_')
                {
                    var marker = c;
                    var close = FindClosingChar(text, i + 1, marker);
                    if (close > i)
                    {
                        FlushText();
                        result.Add(new MdInline
                        {
                            Kind = MdInlineKind.Italic,
                            Children = ParseInlines(text.Substring(i + 1, close - i - 1))
                        });
                        i = close + 1;
                        continue;
                    }
                }
                else if (c == '[')
                {
                    var closeBracket = text.IndexOf(']', i + 1);
                    if (closeBracket > i && closeBracket + 1 < text.Length && text[closeBracket + 1] == '(')
                    {
                        var closeParen = text.IndexOf(')', closeBracket + 2);
                        if (closeParen > closeBracket)
                        {
                            FlushText();
                            var linkText = text.Substring(i + 1, closeBracket - i - 1);
                            var url = text.Substring(closeBracket + 2, closeParen - closeBracket - 2).Trim();
                            result.Add(new MdInline
                            {
                                Kind = MdInlineKind.Link,
                                Text = linkText,
                                Url = url,
                                Children = ParseInlines(linkText)
                            });
                            i = closeParen + 1;
                            continue;
                        }
                    }
                }

                buf.Append(c);
                i++;
            }

            FlushText();
            return result;
        }

        private static int FindClosingChar(string text, int start, char marker)
        {
            for (var i = start; i < text.Length; i++)
                if (text[i] == marker)
                    return i;
            return -1;
        }
    }

#if WINDOWS
    /// <summary>
    /// Преобразует markdown в <see cref="FlowDocument"/> для WPF-окна обновления:
    /// заголовки, абзацы, маркированные/нумерованные списки, код-блоки, разделители
    /// и кликабельные ссылки.
    /// </summary>
    internal static class MarkdownWpfRenderer
    {
        private static readonly FontFamily MonospaceFont = new("Cascadia Mono, Consolas, monospace");
        private static readonly SolidColorBrush CodeBrush = new(Color.FromArgb(35, 0, 0, 0));
        private static readonly SolidColorBrush RuleBrush = new(Color.FromArgb(60, 0, 0, 0));

        public static FlowDocument Render(string markdown)
        {
            var doc = new FlowDocument
            {
                PagePadding = new Thickness(0),
                FontSize = 12,
                LineHeight = 18,
                FontFamily = new FontFamily("Segoe UI")
            };
            // Issue #107 п.1 (v1.0.1.50): текст «Что нового» обязан следовать теме.
            // Без явной кисти FlowDocument наследовал дефолт контрола (чёрный), и в
            // тёмной теме markdown выглядел «тёмным текстом на тёмном фоне». Кисть
            // берётся из ресурса темы при открытии окна (окна краткоживущие, этого
            // достаточно). Цвет наследуют все Run/Paragraph/Span, включая
            // код-блоки и инлайн-код — они остаются читаемыми в тёмной теме.
            if (Application.Current?.TryFindResource("TextPrimaryBrush") is Brush primary)
                doc.Foreground = primary;
            foreach (var block in MarkdownParser.Parse(markdown))
                Append(doc, block);
            return doc;
        }

        /// <summary>Кисть из ресурсов темы или null (issue #107, v1.0.1.50).</summary>
        private static Brush? ThemeBrush(string key) =>
            Application.Current?.TryFindResource(key) as Brush;

        private static void Append(FlowDocument doc, MdBlock b)
        {
            switch (b.Kind)
            {
                case MdBlockKind.Header:
                    var heading = new Paragraph
                    {
                        FontWeight = FontWeights.Bold,
                        Margin = new Thickness(0, 8, 0, 4)
                    };
                    heading.FontSize = b.Level switch { 1 => 16.0, 2 => 15.0, _ => 13.5 };
                    foreach (var inl in b.Inlines)
                        heading.Inlines.Add(BuildInline(inl));
                    doc.Blocks.Add(heading);
                    break;

                case MdBlockKind.Paragraph:
                    var para = new Paragraph { Margin = new Thickness(0, 0, 0, 6) };
                    foreach (var inl in b.Inlines)
                        para.Inlines.Add(BuildInline(inl));
                    doc.Blocks.Add(para);
                    break;

                case MdBlockKind.BulletList:
                    doc.Blocks.Add(BuildList(b, TextMarkerStyle.Disc));
                    break;

                case MdBlockKind.OrderedList:
                    doc.Blocks.Add(BuildList(b, TextMarkerStyle.Decimal));
                    break;

                case MdBlockKind.CodeBlock:
                    var code = new Paragraph
                    {
                        FontFamily = MonospaceFont,
                        Background = CodeBrush,
                        Padding = new Thickness(8, 6, 8, 6),
                        Margin = new Thickness(0, 0, 0, 6),
                        TextAlignment = TextAlignment.Left
                    };
                    code.Inlines.Add(new Run(string.Join(Environment.NewLine, b.CodeLines)));
                    doc.Blocks.Add(code);
                    break;

                case MdBlockKind.HorizontalRule:
                    doc.Blocks.Add(new Paragraph
                    {
                        BorderBrush = RuleBrush,
                        BorderThickness = new Thickness(0, 1, 0, 0),
                        Padding = new Thickness(0, 4, 0, 0),
                        Margin = new Thickness(0, 4, 0, 6)
                    });
                    break;
            }
        }

        private static List BuildList(MdBlock b, TextMarkerStyle style)
        {
            var list = new List
            {
                MarkerStyle = style,
                Margin = new Thickness(14, 0, 0, 6)
            };
            foreach (var item in b.Items)
            {
                var para = new Paragraph { Margin = new Thickness(0) };
                foreach (var inl in item)
                    para.Inlines.Add(BuildInline(inl));
                var li = new ListItem();
                li.Blocks.Add(para);
                list.ListItems.Add(li);
            }
            return list;
        }

        private static Inline BuildInline(MdInline i)
        {
            switch (i.Kind)
            {
                case MdInlineKind.Text:
                    return new Run(i.Text);

                case MdInlineKind.Bold:
                    var bold = new Span { FontWeight = FontWeights.Bold };
                    AddChildren(bold, i.Children);
                    return bold;

                case MdInlineKind.Italic:
                    var italic = new Span { FontStyle = FontStyles.Italic };
                    AddChildren(italic, i.Children);
                    return italic;

                case MdInlineKind.Code:
                    return new Run(i.Text)
                    {
                        FontFamily = MonospaceFont,
                        Background = CodeBrush
                    };

                case MdInlineKind.Link:
                    var link = new Hyperlink();
                    if (Uri.TryCreate(i.Url, UriKind.Absolute, out var uri))
                    {
                        link.NavigateUri = uri;
                        link.RequestNavigate += OnRequestNavigate;
                    }
                    // Цвет ссылки из темы (issue #51, v1.0.1.50 через общий ThemeBrush):
                    // системный синий Hyperlink не следует теме приложения.
                    if (ThemeBrush("AccentBrush") is Brush accent)
                        link.Foreground = accent;
                    AddChildren(link, i.Children);
                    return link;

                default:
                    return new Run(i.Text ?? string.Empty);
            }
        }

        private static void AddChildren(Span span, List<MdInline>? children)
        {
            if (children is null)
                return;
            foreach (var child in children)
                span.Inlines.Add(BuildInline(child));
        }

        private static void AddChildren(Hyperlink link, List<MdInline>? children)
        {
            if (children is null)
                return;
            foreach (var child in children)
                link.Inlines.Add(BuildInline(child));
        }

        private static void OnRequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = e.Uri.AbsoluteUri,
                    UseShellExecute = true
                });
            }
            catch
            {
                // Открытие браузера не должно ронять окно обновления.
            }
            e.Handled = true;
        }
    }
#endif

#if LINUX
    /// <summary>
    /// Преобразует markdown в <see cref="StackPanel"/> для Avalonia-диалога обновления:
    /// заголовки, абзацы, маркированные/нумерованные списки, код-блоки и разделители.
    /// Ссылки выводятся стилем (подчёркнутые, акцентным цветом).
    /// </summary>
    internal static class MarkdownAvaloniaRenderer
    {
        private static readonly FontFamily MonospaceFont = new("Consolas, monospace");
        private static readonly IBrush CodeBrush = new SolidColorBrush(Color.Parse("#22000000"));
        private static readonly IBrush RuleBrush = new SolidColorBrush(Color.Parse("#40000000"));

        public static StackPanel Render(string markdown)
        {
            var panel = new StackPanel { Spacing = 6 };
            foreach (var block in MarkdownParser.Parse(markdown))
                panel.Children.Add(BuildBlock(block));
            return panel;
        }

        private static Control BuildBlock(MdBlock b)
        {
            switch (b.Kind)
            {
                case MdBlockKind.Header:
                    var heading = new TextBlock
                    {
                        FontWeight = FontWeight.Bold,
                        TextWrapping = TextWrapping.Wrap
                    };
                    // Issue #107 п.1 (v1.0.1.50): текст следует теме — без явной
                    // привязки TextBlock наследовал дефолт (тёмный) и в тёмной теме
                    // был «тёмный текст на тёмном фоне».
                    ThemeBrushes.Bind(heading, TextBlock.ForegroundProperty, "TextPrimaryBrush");
                    heading.FontSize = b.Level switch { 1 => 17.0, 2 => 15.0, _ => 13.5 };
                    foreach (var inl in b.Inlines)
                        AddInline(heading, inl);
                    return heading;

                case MdBlockKind.Paragraph:
                    var para = new TextBlock { FontSize = 13, TextWrapping = TextWrapping.Wrap };
                    ThemeBrushes.Bind(para, TextBlock.ForegroundProperty, "TextPrimaryBrush");
                    foreach (var inl in b.Inlines)
                        AddInline(para, inl);
                    return para;

                case MdBlockKind.BulletList:
                    var bulletPanel = new StackPanel { Spacing = 2 };
                    foreach (var item in b.Items)
                    {
                        var tb = new TextBlock { FontSize = 13, TextWrapping = TextWrapping.Wrap };
                        ThemeBrushes.Bind(tb, TextBlock.ForegroundProperty, "TextPrimaryBrush");
                        EnsureInlines(tb).Add(new Run("•  "));
                        foreach (var inl in item)
                            AddInline(tb, inl);
                        bulletPanel.Children.Add(tb);
                    }
                    return bulletPanel;

                case MdBlockKind.OrderedList:
                    var orderedPanel = new StackPanel { Spacing = 2 };
                    var index = 1;
                    foreach (var item in b.Items)
                    {
                        var tb = new TextBlock { FontSize = 13, TextWrapping = TextWrapping.Wrap };
                        ThemeBrushes.Bind(tb, TextBlock.ForegroundProperty, "TextPrimaryBrush");
                        EnsureInlines(tb).Add(new Run(index + ".  "));
                        foreach (var inl in item)
                            AddInline(tb, inl);
                        orderedPanel.Children.Add(tb);
                        index++;
                    }
                    return orderedPanel;

                case MdBlockKind.CodeBlock:
                    var code = new TextBlock
                    {
                        Text = string.Join("\n", b.CodeLines),
                        FontFamily = MonospaceFont,
                        FontSize = 12,
                        TextWrapping = TextWrapping.NoWrap
                    };
                    ThemeBrushes.Bind(code, TextBlock.ForegroundProperty, "TextPrimaryBrush");
                    return new Border
                    {
                        Background = CodeBrush,
                        CornerRadius = new CornerRadius(4),
                        Padding = new Thickness(8, 6),
                        Child = code
                    };

                case MdBlockKind.HorizontalRule:
                    return new Border
                    {
                        Height = 1,
                        Background = RuleBrush,
                        Margin = new Thickness(0, 4, 0, 4)
                    };

                default:
                    return new TextBlock();
            }
        }

        private static void AddInline(TextBlock tb, MdInline i)
        {
            var inlines = EnsureInlines(tb);
            switch (i.Kind)
            {
                case MdInlineKind.Text:
                    inlines.Add(new Run(i.Text));
                    break;

                case MdInlineKind.Bold:
                    var bold = new Span { FontWeight = FontWeight.Bold };
                    AddChildren(bold, i.Children);
                    inlines.Add(bold);
                    break;

                case MdInlineKind.Italic:
                    var italic = new Span { FontStyle = FontStyle.Italic };
                    AddChildren(italic, i.Children);
                    inlines.Add(italic);
                    break;

                case MdInlineKind.Code:
                    inlines.Add(new Run(i.Text)
                    {
                        FontFamily = MonospaceFont,
                        Background = CodeBrush
                    });
                    break;

                case MdInlineKind.Link:
                    var link = new Span { TextDecorations = TextDecorations.Underline };
                    ThemeBrushes.Bind(link, Span.ForegroundProperty, "AccentBrush");
                    AddChildren(link, i.Children);
                    inlines.Add(link);
                    break;
            }
        }

        /// <summary>
        /// Возвращает коллекцию inline-элементов <see cref="TextBlock"/>, создавая её
        /// при необходимости (в Avalonia свойство <c>Inlines</c> объявлено nullable).
        /// </summary>
        private static InlineCollection EnsureInlines(TextBlock tb) =>
            tb.Inlines ??= new InlineCollection();

        private static void AddChildren(Span span, List<MdInline>? children)
        {
            if (children is null)
                return;
            foreach (var child in children)
                AddInlineToSpan(span, child);
        }

        private static void AddInlineToSpan(Span span, MdInline i)
        {
            switch (i.Kind)
            {
                case MdInlineKind.Text:
                    span.Inlines.Add(new Run(i.Text));
                    break;
                case MdInlineKind.Bold:
                    var bold = new Span { FontWeight = FontWeight.Bold };
                    AddChildren(bold, i.Children);
                    span.Inlines.Add(bold);
                    break;
                case MdInlineKind.Italic:
                    var italic = new Span { FontStyle = FontStyle.Italic };
                    AddChildren(italic, i.Children);
                    span.Inlines.Add(italic);
                    break;
                case MdInlineKind.Code:
                    span.Inlines.Add(new Run(i.Text) { FontFamily = MonospaceFont, Background = CodeBrush });
                    break;
                case MdInlineKind.Link:
                    var link = new Span { TextDecorations = TextDecorations.Underline };
                    ThemeBrushes.Bind(link, Span.ForegroundProperty, "AccentBrush");
                    AddChildren(link, i.Children);
                    span.Inlines.Add(link);
                    break;
            }
        }
    }
#endif

    /// <summary>
    /// Точка входа для рендера markdown. Методы различаются по платформе:
    /// <see cref="ToFlowDocument"/> — для WPF, <see cref="ToStackPanel"/> — для Avalonia.
    /// </summary>
    public static class MarkdownRenderer
    {
#if WINDOWS
        /// <summary>Преобразует markdown-строку в <see cref="FlowDocument"/> (WPF).</summary>
        public static FlowDocument ToFlowDocument(string markdown) =>
            MarkdownWpfRenderer.Render(markdown);
#endif

#if LINUX
        /// <summary>Преобразует markdown-строку в <see cref="StackPanel"/> (Avalonia).</summary>
        public static StackPanel ToStackPanel(string markdown) =>
            MarkdownAvaloniaRenderer.Render(markdown);
#endif
    }
}