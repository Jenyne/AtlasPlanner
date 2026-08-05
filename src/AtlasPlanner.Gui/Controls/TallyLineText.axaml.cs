using AtlasPlanner.Gui.Rendering;
using AtlasPlanner.Gui.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;

namespace AtlasPlanner.Gui.Controls;

public partial class TallyLineText : UserControl
{
    private readonly Dictionary<string, IBrush> _brushCache = new(StringComparer.OrdinalIgnoreCase);

    public TallyLineText()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Rebuild();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Rebuild();
    }

    private void Rebuild()
    {
        if (Line?.Inlines is not { } inlines)
            return;

        inlines.Clear();

        if (DataContext is not TallyRow row || row.Segments.Count == 0)
            return;

        var accent = BrushFor(row.AccentColor);

        foreach (var segment in row.Segments)
        {
            var run = new Run { Text = segment.Text };
            if (segment.Highlight)
                run.Foreground = accent;
            inlines.Add(run);
        }
    }

    private IBrush BrushFor(string hex)
    {
        if (_brushCache.TryGetValue(hex, out var cached))
            return cached;

        cached = new SolidColorBrush(Color.Parse(hex));
        _brushCache[hex] = cached;
        return cached;
    }
}
