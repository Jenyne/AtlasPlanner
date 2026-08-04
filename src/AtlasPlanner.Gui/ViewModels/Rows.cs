using AtlasPlanner.Core.Planning;
using AtlasPlanner.Core.Tally;
using AtlasPlanner.Core.Tree;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AtlasPlanner.Gui.ViewModels;

/// <summary>
/// One mechanic in the Goals panel. Mode is the primary control; Weight/IsOn remain so older tests
/// and the solve profile builder stay simple.
/// </summary>
public sealed partial class WeightRow : ObservableObject
{
    private MechanicMode _mode = MechanicMode.Ignore;
    private ChasePriority _priority = ChasePriority.Normal;

    public WeightRow(string category, double weight = 0d)
    {
        Category = category;
        ApplyLegacy(weight, isOn: true);
    }

    public string Category { get; }

    public MechanicMode Mode
    {
        get => _mode;
        set
        {
            if (!SetProperty(ref _mode, value))
                return;

            NotifyDerived();
        }
    }

    public ChasePriority Priority
    {
        get => _priority;
        set
        {
            if (!SetProperty(ref _priority, value))
                return;

            OnPropertyChanged(nameof(Weight));
            OnPropertyChanged(nameof(IsActive));
        }
    }

    /// <summary>Decimal so NumericUpDown can bind in Advanced without a converter.</summary>
    public decimal Weight
    {
        get => Mode == MechanicMode.Chase ? (decimal)(int)Priority : 0m;
        set
        {
            if (value == 0m)
            {
                if (Mode == MechanicMode.Chase)
                    Mode = MechanicMode.Ignore;
                return;
            }

            _priority = ChasePriorityMap.Nearest(value);
            OnPropertyChanged(nameof(Priority));
            Mode = MechanicMode.Chase;
        }
    }

    /// <summary>False means Block. Kept for settings migration and older call sites.</summary>
    public bool IsOn
    {
        get => Mode != MechanicMode.Block;
        set
        {
            if (!value)
                Mode = MechanicMode.Block;
            else if (Mode == MechanicMode.Block)
                Mode = MechanicMode.Ignore;
        }
    }

    public bool IsActive => Mode == MechanicMode.Chase;

    public bool IsWeightEditable => Mode == MechanicMode.Chase;

    public bool ShowsPriority => Mode == MechanicMode.Chase;

    public string PriorityLabel => Priority.ToString();

    /// <summary>Apply a saved weight + switched-off flag from PlannerSettings.</summary>
    public void ApplyLegacy(double weight, bool isOn)
    {
        if (!isOn)
        {
            _mode = MechanicMode.Block;
            _priority = ChasePriority.Normal;
        }
        else if (weight != 0d)
        {
            _mode = MechanicMode.Chase;
            _priority = ChasePriorityMap.Nearest((decimal)weight);
        }
        else
        {
            _mode = MechanicMode.Ignore;
            _priority = ChasePriority.Normal;
        }

        OnPropertyChanged(nameof(Mode));
        OnPropertyChanged(nameof(Priority));
        NotifyDerived();
    }

    public void ChaseWith(ChasePriority priority)
    {
        _priority = priority;
        OnPropertyChanged(nameof(Priority));
        Mode = MechanicMode.Chase;
    }

    public void Block() => Mode = MechanicMode.Block;

    public void Clear() => Mode = MechanicMode.Ignore;

    private void NotifyDerived()
    {
        OnPropertyChanged(nameof(Weight));
        OnPropertyChanged(nameof(IsOn));
        OnPropertyChanged(nameof(IsActive));
        OnPropertyChanged(nameof(IsWeightEditable));
        OnPropertyChanged(nameof(ShowsPriority));
        OnPropertyChanged(nameof(PriorityLabel));
    }
}

/// <summary>A Must-take or Never-take entry shown as a chip.</summary>
public sealed class MarkChip
{
    public required string Label { get; init; }
    public required string Reference { get; init; }
}

/// <summary>A line of the tally: a stat total and how many nodes contributed to it.</summary>
public sealed class TallyRow
{
    public required string Category { get; init; }
    public required string Text { get; init; }
    public required int NodeCount { get; init; }

    public string NodeCountLabel => NodeCount > 1 ? $"x{NodeCount}" : string.Empty;

    public static TallyRow From(TallyEntry entry) => new()
    {
        Category = entry.Category,
        Text = entry.Rendered,
        NodeCount = entry.NodeCount,
    };
}

/// <summary>A step in the allocation order.</summary>
public sealed class OrderRow
{
    public required int Step { get; init; }
    public required int NodeId { get; init; }
    public required string Name { get; init; }
    public required string Detail { get; init; }
    public required bool IsMilestone { get; init; }

    public static OrderRow From(PlanStep step, AtlasTree tree)
    {
        var node = tree.TryGet(step.NodeId, out var found) ? found : null;
        var pieces = new List<string>();

        if (step.Region is { Length: > 0 })
            pieces.Add(step.Region);
        if (step.Towards is { Length: > 0 })
            pieces.Add($"towards {step.Towards}");
        if (step.GrantsPoints is > 0)
            pieces.Add($"+{step.GrantsPoints} points");

        return new OrderRow
        {
            Step = step.Step,
            NodeId = step.NodeId,
            Name = step.Name.Length > 0 ? step.Name : $"#{step.NodeId}",
            Detail = string.Join("  ·  ", pieces),
            IsMilestone = node?.Kind is NodeKind.Notable or NodeKind.Keystone,
        };
    }
}

/// <summary>Everything shown about the node under the cursor.</summary>
public sealed class NodeDetail
{
    public required string Name { get; init; }
    public required string Subtitle { get; init; }
    public required IReadOnlyList<string> Stats { get; init; }
    public required IReadOnlyList<string> Reminders { get; init; }
    public required bool IsAllocated { get; init; }
    public string AllocationLabel => IsAllocated ? "Allocated" : "Not allocated";

    public static NodeDetail From(AtlasNode node, bool isAllocated, int distanceFromStart, string? mark = null)
    {
        var subtitle = new List<string> { node.Kind.ToString() };
        if (node.Region.Length > 0)
            subtitle.Add(node.Region);
        if (node.IsGateway)
            subtitle.Add("Gateway");
        if (node.GrantedPoints > 0)
            subtitle.Add($"grants {node.GrantedPoints} points");
        if (mark is { Length: > 0 })
            subtitle.Add(mark);
        subtitle.Add(distanceFromStart >= 0 ? $"{distanceFromStart} from start" : "unreachable");

        return new NodeDetail
        {
            Name = node.Name.Length > 0 ? node.Name : $"Node #{node.Id}",
            Subtitle = string.Join("  ·  ", subtitle),
            Stats = node.Stats.Select(stat => stat.Text).ToArray(),
            Reminders = node.ReminderText,
            IsAllocated = isAllocated,
        };
    }
}
