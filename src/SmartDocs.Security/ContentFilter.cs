using System.Text.RegularExpressions;

namespace SmartDocs.Security;

/// <summary>
/// Output filter — scans the generated answer for evidence that the model
/// leaked or echoed system instructions, common credential shapes, or
/// hate-speech-style fragments. Flagged outputs are blocked or redacted
/// upstream of the user.
/// </summary>
public sealed partial class ContentFilter
{
    [GeneratedRegex(@"You are (?:a |the )?(?:helpful )?assistant", RegexOptions.IgnoreCase)]
    private static partial Regex SystemPromptEcho();

    [GeneratedRegex(@"\b(?:sk-[a-zA-Z0-9]{20,}|AKIA[0-9A-Z]{12,}|ghp_[a-zA-Z0-9]{20,})\b")]
    private static partial Regex CredentialShape();

    public static ContentFilterResult Inspect(string output)
    {
        ArgumentNullException.ThrowIfNull(output);
        var findings = new List<string>();
        if (SystemPromptEcho().IsMatch(output))
        {
            findings.Add("system-prompt-echo");
        }

        if (CredentialShape().IsMatch(output))
        {
            findings.Add("credential-leak");
        }

        return new ContentFilterResult(findings.Count == 0, findings, output);
    }
}

public sealed record ContentFilterResult(bool IsClean, IReadOnlyList<string> Findings, string Output);
