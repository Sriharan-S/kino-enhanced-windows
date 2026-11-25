namespace Kino;

/// <summary>
/// Command-line options. Mostly for development and automated sync tests:
///   Kino.exe [media | kino://join/theatre/CODE] [--profile p] [--theatre url | --directory url] [--name n] [--create "Screen"] [--join CODE]
///            [--autoplay] [--codefile path] [--synclog path] [--perturb seconds] [--perturb-after seconds]
///            [--window x,y,w,h] [--volume 0-200] [--seeks after=to,...] [--panel viewers|roles|screen|playlist|chat] [--sub file [--sub-share]] [--sub-pick shared]
/// By default the theatre list comes from the directory (servers.json). --theatre uses only that one theatre instead,
/// e.g. a local `wrangler dev` at http://127.0.0.1:8787.
/// </summary>
public sealed record LaunchOptions
{
    public string? Media { get; init; }
    public string Profile { get; init; } = "default";
    public string? Theatre { get; init; }
    public string? Directory { get; init; }
    public string Name { get; init; } = Environment.UserName;
    public string? Create { get; init; }
    public string? Join { get; init; }
    public bool AutoPlay { get; init; }
    public string? CodeFile { get; init; }
    public string? SyncLog { get; init; }
    public double Perturb { get; init; }
    public double PerturbAfter { get; init; } = 20;
    public int[]? Window { get; init; }
    /// <summary>Overrides the saved volume (tests use 0).</summary>
    public double? Volume { get; init; }

    /// <summary>An invite link to join (kino://join/... or the web form), e.g. from clicking a link.</summary>
    public Kino.Core.InviteLink? Invite { get; init; }

    /// <summary>Test hooks: load (and share) a subtitle file once the item is in; or pick the first shared one.</summary>
    public string? Sub { get; init; }
    public bool SubShare { get; init; }
    public string? SubPick { get; init; }

    /// <summary>Opens a panel once in a screen: "viewers", "roles" or "screen" (for screenshots and tests).</summary>
    public string? Panel { get; init; }

    /// <summary>Host test hook: (seconds after autoplay, seek target) pairs, from "--seeks 20=226.8,28=99.1".</summary>
    public (double After, double To)[] Seeks { get; init; } = [];

    public static LaunchOptions Parse(string[] args)
    {
        var o = new LaunchOptions();
        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i];
            string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{a} needs a value");
            o = a switch
            {
                "--profile" => o with { Profile = Next() },
                "--theatre" => o with { Theatre = Next() },
                "--directory" => o with { Directory = Next() },
                "--panel" => o with { Panel = Next() },
                "--sub" => o with { Sub = Next() },
                "--sub-share" => o with { SubShare = true },
                "--sub-pick" => o with { SubPick = Next() },
                "--name" => o with { Name = Next() },
                "--create" => o with { Create = Next() },
                "--join" => o with { Join = Next() },
                "--autoplay" => o with { AutoPlay = true },
                "--codefile" => o with { CodeFile = Next() },
                "--synclog" => o with { SyncLog = Next() },
                "--perturb" => o with { Perturb = double.Parse(Next(), System.Globalization.CultureInfo.InvariantCulture) },
                "--perturb-after" => o with { PerturbAfter = double.Parse(Next(), System.Globalization.CultureInfo.InvariantCulture) },
                "--volume" => o with { Volume = double.Parse(Next(), System.Globalization.CultureInfo.InvariantCulture) },
                "--window" => o with { Window = Next().Split(',').Select(int.Parse).ToArray() },
                "--seeks" => o with
                {
                    Seeks = Next().Split(',').Select(p => p.Split('=')).Select(p => (
                        double.Parse(p[0], System.Globalization.CultureInfo.InvariantCulture),
                        double.Parse(p[1], System.Globalization.CultureInfo.InvariantCulture))).ToArray(),
                },
                _ when Kino.Core.InviteLink.TryParse(a) is { } link => o with { Invite = link },
                _ when !a.StartsWith("--") && o.Media is null => o with { Media = a },
                _ => o,
            };
        }
        return o;
    }
}
