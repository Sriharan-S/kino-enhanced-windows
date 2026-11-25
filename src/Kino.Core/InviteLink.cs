using System.Text.RegularExpressions;

namespace Kino.Core;

/// <summary>
/// An invite to a screen (spec §11): <c>kino://join/&lt;theatreId&gt;/&lt;code&gt;</c>, or the web form
/// <c>https://kino-directory.srie.workers.dev/join#&lt;theatreId&gt;/&lt;code&gt;</c>, which opens the app.
/// </summary>
public sealed partial record InviteLink(string TheatreId, string Code)
{
    public const string Scheme = "kino";
    public static readonly Uri WebPage = new("https://kino-directory.srie.workers.dev/join");

    public string AppLink => $"{Scheme}://join/{TheatreId}/{Code}";
    public string WebLink => $"{WebPage}#{TheatreId}/{Code}";

    /// <summary>Parses an app link, a web link, or "theatre/code". Returns null for anything else.</summary>
    public static InviteLink? TryParse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var m = LinkPattern().Match(text.Trim());
        if (!m.Success) return null;
        string code = NormaliseCode(m.Groups["code"].Value);
        return CodePattern().IsMatch(code) ? new InviteLink(m.Groups["theatre"].Value.ToLowerInvariant(), code) : null;
    }

    /// <summary>Same normalisation as the theatre: upper case, and Crockford look-alikes I→1, L→1, O→0.</summary>
    public static string NormaliseCode(string code) =>
        code.Trim().ToUpperInvariant().Replace('I', '1').Replace('L', '1').Replace('O', '0');

    [GeneratedRegex(@"^(?:kino://join/|https://[^/#?\s]+/join/?#|)(?<theatre>[a-zA-Z][a-zA-Z0-9-]{1,30})/(?<code>[0-9A-Za-z]{6})/?$")]
    private static partial Regex LinkPattern();

    [GeneratedRegex("^[0-9A-HJKMNP-TV-Z]{6}$")]
    private static partial Regex CodePattern();
}
