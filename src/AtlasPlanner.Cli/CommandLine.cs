namespace AtlasPlanner.Cli;

/// <summary>Minimal <c>--flag value</c> / <c>--switch</c> parser, plus positional arguments.</summary>
internal sealed class CommandLine
{
    private readonly Dictionary<string, string?> _options = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _positional = [];

    public CommandLine(IEnumerable<string> args)
    {
        string? pending = null;

        foreach (var arg in args)
        {
            if (arg.StartsWith("--", StringComparison.Ordinal))
            {
                if (pending is not null)
                    _options[pending] = null;

                var body = arg[2..];
                var split = body.IndexOf('=');
                if (split >= 0)
                {
                    _options[body[..split]] = body[(split + 1)..];
                    pending = null;
                }
                else
                {
                    pending = body;
                }
            }
            else if (pending is not null)
            {
                _options[pending] = arg;
                pending = null;
            }
            else
            {
                _positional.Add(arg);
            }
        }

        if (pending is not null)
            _options[pending] = null;
    }

    public IReadOnlyList<string> Positional => _positional;

    public string? Positional0 => _positional.Count > 0 ? _positional[0] : null;

    public bool Has(string name) => _options.ContainsKey(name);

    public string? Value(string name) => _options.GetValueOrDefault(name);

    public string Value(string name, string fallback) =>
        _options.TryGetValue(name, out var value) && !string.IsNullOrEmpty(value) ? value : fallback;

    public int Value(string name, int fallback) =>
        _options.TryGetValue(name, out var value) && int.TryParse(value, out var parsed) ? parsed : fallback;
}
