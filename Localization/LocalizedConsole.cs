using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using System.Text;
using Spectre.Console;
using Spectre.Console.Rendering;
using SpectrePanel = Spectre.Console.Panel;

namespace XeCli.Localization;

internal static class LocalizedConsole
{
    public static void Initialize()
    {
        if (Console.Out is not TranslatingTextWriter)
        {
            Console.SetOut(new TranslatingTextWriter(Console.Out));
        }

        if (Console.Error is not TranslatingTextWriter)
        {
            Console.SetError(new TranslatingTextWriter(Console.Error));
        }

        if (AnsiConsole.Console is not LocalizedAnsiConsole)
        {
            AnsiConsole.Console = new LocalizedAnsiConsole(AnsiConsole.Console);
        }
    }

    private sealed class LocalizedAnsiConsole : IAnsiConsole
    {
        private readonly IAnsiConsole _inner;

        public LocalizedAnsiConsole(IAnsiConsole inner)
        {
            _inner = inner;
        }

        public IAnsiConsoleCursor Cursor => _inner.Cursor;

        public IExclusivityMode ExclusivityMode => _inner.ExclusivityMode;

        public IAnsiConsoleInput Input => _inner.Input;

        public RenderPipeline Pipeline => _inner.Pipeline;

        public Profile Profile => _inner.Profile;

        public void Clear(bool home) => _inner.Clear(home);

        public void Write(IRenderable renderable)
        {
            RenderableLocalizer.Localize(renderable);
            _inner.Write(renderable);
        }
    }

    private sealed class TranslatingTextWriter : TextWriter
    {
        private readonly TextWriter _inner;

        public TranslatingTextWriter(TextWriter inner)
        {
            _inner = inner;
        }

        public override Encoding Encoding => _inner.Encoding;

        public override void Write(string? value) => _inner.Write(LocalizedText.Translate(value));

        public override void WriteLine(string? value) => _inner.WriteLine(LocalizedText.Translate(value));

        public override void Write(char value) => _inner.Write(value);

        public override void Flush() => _inner.Flush();
    }

    private static class RenderableLocalizer
    {
        private const BindingFlags InstanceFieldBindings = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly;

        private static readonly FieldInfo? MarkupParagraphField = typeof(Markup).GetField("_paragraph", BindingFlags.NonPublic | BindingFlags.Instance);

        private static readonly FieldInfo? TextParagraphField = typeof(Text).GetField("_paragraph", BindingFlags.NonPublic | BindingFlags.Instance);

        private static readonly FieldInfo? ParagraphLinesField = typeof(Paragraph).GetField("_lines", BindingFlags.NonPublic | BindingFlags.Instance);

        private static readonly FieldInfo? SegmentTextField = typeof(Segment).GetField("<Text>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance);

        private static readonly FieldInfo? PanelChildField = typeof(SpectrePanel).GetField("_child", BindingFlags.NonPublic | BindingFlags.Instance);

        private static readonly FieldInfo? RowsChildrenField = typeof(Rows).GetField("_children", BindingFlags.NonPublic | BindingFlags.Instance);

        private static readonly FieldInfo? ColumnsItemsField = typeof(Columns).GetField("_items", BindingFlags.NonPublic | BindingFlags.Instance);

        private static readonly FieldInfo? PadderChildField = typeof(Padder).GetField("_child", BindingFlags.NonPublic | BindingFlags.Instance);

        private static readonly FieldInfo? AlignChildField = typeof(Align).GetField("_renderable", BindingFlags.NonPublic | BindingFlags.Instance);

        private static readonly FieldInfo? GridRowsField = typeof(Grid).GetField("_rows", BindingFlags.NonPublic | BindingFlags.Instance);

        private static readonly FieldInfo? GridRowItemsField = typeof(GridRow).GetField("_items", BindingFlags.NonPublic | BindingFlags.Instance);

        private static readonly ConcurrentDictionary<Type, FieldInfo[]> TraversableFieldCache = new();

        public static void Localize(IRenderable? renderable)
        {
            if (renderable is null || !LocalizedText.IsSpanish)
            {
                return;
            }

            LocalizeObject(renderable, new HashSet<object>(ReferenceEqualityComparer.Instance));
        }

        private static void LocalizeObject(object? value, HashSet<object> visited)
        {
            if (value is null || value is string)
            {
                return;
            }

            Type type = value.GetType();
            if (type.IsValueType || typeof(Delegate).IsAssignableFrom(type) || !visited.Add(value))
            {
                return;
            }

            if (value is IRenderable renderable)
            {
                switch (renderable)
                {
                    case Markup markup:
                        LocalizeParagraph(MarkupParagraphField?.GetValue(markup) as Paragraph);
                        return;
                    case Text text:
                        LocalizeParagraph(TextParagraphField?.GetValue(text) as Paragraph);
                        return;
                    case SpectrePanel panel:
                        if (panel.Header != null)
                        {
                            panel.Header = new PanelHeader(LocalizedText.TranslateMarkup(panel.Header.Text), panel.Header.Justification);
                        }

                        LocalizeObject(PanelChildField?.GetValue(panel), visited);
                        return;
                    case Table table:
                        if (table.Title != null)
                        {
                            table.Title = new TableTitle(LocalizedText.TranslateMarkup(table.Title.Text), table.Title.Style);
                        }

                        if (table.Caption != null)
                        {
                            table.Caption = new TableTitle(LocalizedText.TranslateMarkup(table.Caption.Text), table.Caption.Style);
                        }

                        foreach (TableColumn column in table.Columns)
                        {
                            LocalizeObject(column.Header, visited);
                            LocalizeObject(column.Footer, visited);
                        }

                        foreach (TableRow row in table.Rows)
                        {
                            for (int cellIndex = 0; cellIndex < row.Count; cellIndex++)
                            {
                                LocalizeObject(row[cellIndex], visited);
                            }
                        }

                        return;
                    case Grid grid:
                        if (GridRowsField?.GetValue(grid) is IEnumerable gridRows)
                        {
                            foreach (object? row in gridRows)
                            {
                                if (row != null && GridRowItemsField?.GetValue(row) is IEnumerable gridItems)
                                {
                                    foreach (object? gridItem in gridItems)
                                    {
                                        LocalizeObject(gridItem, visited);
                                    }
                                }
                            }
                        }

                        return;
                    case Rows rows:
                        if (RowsChildrenField?.GetValue(rows) is IEnumerable childItems)
                        {
                            foreach (object? child in childItems)
                            {
                                LocalizeObject(child, visited);
                            }
                        }

                        return;
                    case Columns columns:
                        if (ColumnsItemsField?.GetValue(columns) is IEnumerable columnItems)
                        {
                            foreach (object? columnItem in columnItems)
                            {
                                LocalizeObject(columnItem, visited);
                            }
                        }

                        return;
                    case Padder padder:
                        LocalizeObject(PadderChildField?.GetValue(padder), visited);
                        return;
                    case Align align:
                        LocalizeObject(AlignChildField?.GetValue(align), visited);
                        return;
                    case Rule rule:
                        rule.Title = LocalizedText.TranslateMarkup(rule.Title);
                        return;
                }
            }

            TraverseChildren(value, visited);
        }

        private static void LocalizeParagraph(Paragraph? paragraph)
        {
            if (paragraph is null || ParagraphLinesField?.GetValue(paragraph) is not System.Collections.IEnumerable lines)
            {
                return;
            }

            foreach (object? lineObject in lines)
            {
                if (lineObject is not SegmentLine line || line.Count == 0)
                {
                    continue;
                }

                int index = 0;
                while (index < line.Count)
                {
                    Segment first = line[index];
                    int end = index + 1;
                    StringBuilder combined = new();
                    combined.Append(first.Text);
                    while (end < line.Count && SameRun(first, line[end]))
                    {
                        combined.Append(line[end].Text);
                        end++;
                    }

                    string translated = LocalizedText.Translate(combined.ToString());
                    if (!string.Equals(translated, combined.ToString(), StringComparison.Ordinal))
                    {
                        SetSegmentText(line[index], translated);
                        for (int clearIndex = index + 1; clearIndex < end; clearIndex++)
                        {
                            SetSegmentText(line[clearIndex], string.Empty);
                        }
                    }

                    index = end;
                }
            }
        }

        private static bool SameRun(Segment left, Segment right)
        {
            return left.Style.Equals(right.Style)
                && left.IsControlCode == right.IsControlCode
                && left.IsLineBreak == right.IsLineBreak;
        }

        private static void SetSegmentText(Segment segment, string value)
        {
            SegmentTextField?.SetValue(segment, value);
        }

        private static void TraverseChildren(object value, HashSet<object> visited)
        {
            foreach (FieldInfo field in GetTraversableFields(value.GetType()))
            {
                object? child = field.GetValue(value);
                if (child is null || child is string)
                {
                    continue;
                }

                if (child is IEnumerable enumerable)
                {
                    foreach (object? item in enumerable)
                    {
                        LocalizeObject(item, visited);
                    }

                    continue;
                }

                LocalizeObject(child, visited);
            }
        }

        private static FieldInfo[] GetTraversableFields(Type type) =>
            TraversableFieldCache.GetOrAdd(type, static currentType =>
            {
                List<FieldInfo> fields = new();
                for (Type? cursor = currentType; cursor != null; cursor = cursor.BaseType)
                {
                    foreach (FieldInfo field in cursor.GetFields(InstanceFieldBindings))
                    {
                        if (ShouldTraverseField(field.FieldType))
                        {
                            fields.Add(field);
                        }
                    }
                }

                return fields.ToArray();
            });

        private static bool ShouldTraverseField(Type fieldType)
        {
            if (fieldType == typeof(string) || typeof(Delegate).IsAssignableFrom(fieldType))
            {
                return false;
            }

            if (typeof(IRenderable).IsAssignableFrom(fieldType) || typeof(IEnumerable).IsAssignableFrom(fieldType))
            {
                return true;
            }

            return !fieldType.IsValueType
                && fieldType.Namespace != null
                && fieldType.Namespace.StartsWith("Spectre.Console", StringComparison.Ordinal);
        }
    }
}
