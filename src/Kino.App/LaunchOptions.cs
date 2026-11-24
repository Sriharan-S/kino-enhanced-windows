namespace Kino;

/// <summary>
/// Command-line options. Mostly for development and automated sync tests:
///   Kino.exe [media] [--profile p] [--theatre url] [--name n] [--create "Screen"] [--join CODE]
///            [--autoplay] [--codefile path] [--synclog path] [--perturb seconds] [--perturb-after seconds]
///            [--window x,y,w,h] [--volume 0-200]
/// </summary>
public sealed record LaunchOptions
{
    public string? Media { get; init; }
    public string Profile { get; init; } = "default";
    public string Theatre { get; init; } = "http://127.0.0.1:8787";
    public string Name { get; init; } = Environment.UserName;
    public string? Create { get; init; }
    public string? Join { get; init; }
    public bool AutoPlay { get; init; }
    public string? CodeFile { get; init; }
    public string? SyncLog { get; init; }
    public double Perturb { get; init; }
    public double PerturbAfter { get; init; } = 20;
    public int[]? Window { get; init; }
    public double Volume { get; init; } = 100;

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
                _ when !a.StartsWith("--") && o.Media is null => o with { Media = a },
                _ => o,
            };
        }
        return o;
    }
}
