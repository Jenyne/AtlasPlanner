using AtlasPlanner.Core.Categorization;

namespace AtlasPlanner.Cli.Commands;

/// <summary>Writes the built-in score table out so it can be edited, or shows which one is in use.</summary>
internal static class ScoresCommand
{
    public static int Run(CommandLine cli)
    {
        if (cli.Value("init") is { Length: > 0 } target)
        {
            if (File.Exists(target) && !cli.Has("force"))
                throw new InvalidOperationException($"'{target}' already exists. Pass --force to overwrite.");

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(target))!);
            ScoreTable.Default().Save(target);
            Console.WriteLine($"wrote built-in score table to {target}");
            return 0;
        }

        var context = Context.Load(cli);
        Console.WriteLine($"Scores    {context.ScoresPath}");
        Console.WriteLine($"Rules     {context.Scores.KeywordRules.Count} keyword, " +
                          $"{context.Scores.TemplateOverrides.Count} template override(s), " +
                          $"{context.Scores.IgnoredTemplates.Count} ignored");
        Console.WriteLine();

        foreach (var rule in context.Scores.KeywordRules)
            Console.WriteLine($"  {rule.Category,-20} {string.Join(" | ", rule.Contains)}");

        Console.WriteLine();
        Console.WriteLine("Categories reachable for this tree:");
        foreach (var category in context.Scores.KnownCategories(context.Tree))
            Console.WriteLine($"  {category}");

        Console.WriteLine();
        Console.WriteLine("Write an editable copy with: atlasplanner scores --init data/atlasscores.json");
        return 0;
    }
}
