using System.Text.RegularExpressions;
using Dfe.Common.AI.Services.Application.Constants;
using GovUK.Dfe.AI.Agents.ValueObjects;

namespace Dfe.Common.AI.Services.Application.Agents;

/// <summary>
/// Checks run on every answer. Each returns why the answer is wrong, or null. A failed check is sent back to the agent
/// once; if the retry fails too, the run fails.
/// </summary>
public static partial class AnswerChecks
{
    /// <summary>Runs every check, and returns the first problem found.</summary>
    public static string? Check(AgentResult result) => HasAnswer(result) ?? ListsItsSources(result);

    public static string? HasAnswer(AgentResult result) =>
        string.IsNullOrWhiteSpace(result.Output) ? Messages.AnswerChecks.EmptyAnswer : null;

    /// <summary>
    /// A reader never sees the numbered evidence, so an answer that cites it must end with a Sources section listing
    /// each cited record as <c>[Evidence n]</c>. Where the app passes the evidence's sources (AISearch's
    /// <c>RenderCitations</c>), the library replaces each citation with the record's name and link, taken from the
    /// index, after these checks. So the model never writes a source link, and can't invent one. An answer that cites
    /// nothing needs no sources.
    /// </summary>
    public static string? ListsItsSources(AgentResult result)
    {
        var answer = result.Output ?? string.Empty;
        var sourcesHeading = SourcesHeading().Matches(answer).LastOrDefault();
        var body = sourcesHeading is null ? answer : answer[..sourcesHeading.Index];

        var cited = Citations(body);
        if (cited.Count == 0)
        {
            return null;
        }

        if (sourcesHeading is null)
        {
            return Messages.AnswerChecks.NoSources;
        }

        var sources = answer[sourcesHeading.Index..];
        if (Link().IsMatch(sources))
        {
            return Messages.AnswerChecks.LinksInSources;
        }

        var missing = cited.Except(Citations(sources)).Order().ToList();
        return missing.Count == 0 ? null : Messages.AnswerChecks.SourcesMissing(missing);
    }

    private static HashSet<int> Citations(string text) =>
        [.. Citation().Matches(text).Select(match => int.Parse(match.Groups[1].ValueSpan))];

    [GeneratedRegex(@"\[Evidence (\d{1,6})\]")]
    private static partial Regex Citation();

    /// <summary>A web address or a Markdown link.</summary>
    [GeneratedRegex(@"https?://|www\.|\]\(", RegexOptions.IgnoreCase)]
    private static partial Regex Link();

    /// <summary>"Sources", on a line of its own or starting one, as plain text, bold or a heading.</summary>
    [GeneratedRegex(@"^[ \t]*(?:#{1,6}[ \t]*)?\**Sources\**[ \t]*:?\**", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex SourcesHeading();
}
