using AtlasPlanner.Core.Planning;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace AtlasPlanner.Gui.Views;

/// <summary>Asks the user to pick one option per unresolved specialization group before solving.</summary>
public sealed class SpecializationPromptWindow : Window
{
    private readonly Dictionary<string, ComboBox> _combos = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, string> Choices { get; private set; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public SpecializationPromptWindow(IReadOnlyList<SpecializationGroup> groups)
    {
        Title = "Specialization";
        Width = 420;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;

        var root = new StackPanel
        {
            Margin = new Avalonia.Thickness(16),
            Spacing = 12,
        };

        root.Children.Add(new TextBlock
        {
            Text = "Pick a path for each chased mechanic before solving.",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.75,
        });

        foreach (var group in groups)
        {
            var block = new StackPanel { Spacing = 4 };
            block.Children.Add(new TextBlock
            {
                Text = $"{group.Mechanic} — {group.Prompt}",
                FontWeight = FontWeight.SemiBold,
            });

            var combo = new ComboBox
            {
                ItemsSource = group.Options,
                DisplayMemberBinding = new Avalonia.Data.Binding(nameof(SpecializationOption.Label)),
                SelectedIndex = 0,
                HorizontalAlignment = HorizontalAlignment.Stretch,
            };
            _combos[group.Id] = combo;
            block.Children.Add(combo);
            root.Children.Add(block);
        }

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Avalonia.Thickness(0, 8, 0, 0),
        };

        var cancel = new Button { Content = "Cancel", Width = 88 };
        cancel.Click += (_, _) => Close(false);

        var ok = new Button { Content = "Continue", Width = 96, Classes = { "primary" } };
        ok.Click += (_, _) =>
        {
            var choices = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (groupId, combo) in _combos)
            {
                if (combo.SelectedItem is SpecializationOption option)
                    choices[groupId] = option.Id;
            }

            Choices = choices;
            Close(true);
        };

        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        root.Children.Add(buttons);

        Content = root;
    }
}
