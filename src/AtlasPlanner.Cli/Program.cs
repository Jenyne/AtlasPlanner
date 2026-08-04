using AtlasPlanner.Cli;
using AtlasPlanner.Cli.Commands;

try
{
    var hasCommand = args.Length > 0 && !args[0].StartsWith("--", StringComparison.Ordinal);
    var command = hasCommand ? args[0].ToLowerInvariant() : "help";
    var cli = new CommandLine(args.Skip(hasCommand ? 1 : 0));

    return command switch
    {
        "tally" => TallyCommand.Run(cli),
        "search" => SearchCommand.Run(cli),
        "categories" => CategoriesCommand.Run(cli),
        "validate" => ValidateCommand.Run(cli),
        "node" => NodeCommand.Run(cli),
        "scores" => ScoresCommand.Run(cli),
        "profile" => ProfileCommand.Run(cli),
        "solve" => SolveCommand.Run(cli),
        "plan" => PlanCommand.Run(cli),
        "art" => ArtCommand.Run(cli),
        "help" or "--help" or "-h" => Help(),
        _ => Unknown(command),
    };
}
catch (Exception e)
{
    Console.Error.WriteLine($"error: {e.Message}");
    return 1;
}

static int Unknown(string command)
{
    Console.Error.WriteLine($"error: unknown command '{command}'");
    Help();
    return 1;
}

static int Help()
{
    Console.WriteLine(
        """
        atlasplanner - Path of Exile 1 atlas tree planning tools

        Planning
          profile     Inspect a solve profile, or --init <path> to write an example
          solve       Plan a route from a profile and write an AtlasPlan.json
          plan        Print an existing plan: order, tally, importable URL

        Inspection
          tally       Add up what a set of allocated nodes gives you
          search      Find nodes by name, stat text, or region
          categories  List every stat template and the category it classifies as
          validate    Sanity-check the tree data and URL round-tripping
          node        Print everything known about one node
          scores      Show the score table in use, or write an editable copy
          art         Report sprite art coverage, or --fetch to warm the image cache

        Common options
          --tree <path>    Path to AtlasTreeData.json (auto-located if omitted)
          --scores <path>  Path to atlasscores.json (built-in defaults if omitted)

        Examples
          atlasplanner profile --init myplan.profile.json
          atlasplanner profile myplan.profile.json --explain
          atlasplanner solve --profile myplan.profile.json --out myplan.json
          atlasplanner solve --profile myplan.profile.json --budget 60 --no-tally
          atlasplanner solve --profile myplan.profile.json --unwavering include
          atlasplanner plan myplan.json --steps 20
          atlasplanner tally --url "https://www.pathofexile.com/fullscreen-atlas-skill-tree/AAAABgAA..."
          atlasplanner search "Scarab" --stats
          atlasplanner categories --uncategorised
          atlasplanner node 65225
        """);
    return 0;
}
