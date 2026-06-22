using System.Text.RegularExpressions;

namespace SmartDocs.Security;

/// <summary>Verdict from <see cref="InputSanitizer"/>.</summary>
public sealed record SanitizationResult(bool IsSafe, IReadOnlyList<string> Findings, string SafeInput);

/// <summary>
/// Heuristic sanitizer for user input — catches the book's red-team
/// reference patterns (direct prompt injection, role-play jailbreaks,
/// system-prompt extraction). Length-bounded; strips zero-width and
/// other control characters; collapses whitespace.
/// </summary>
public sealed partial class InputSanitizer
{
    /// <summary>
    /// Maximum accepted input length in characters. Defaults to
    /// 8,192 characters (8 KB) — the chapter rejects queries above 8 KB as a
    /// cheap resource-exhaustion guard; anything longer is truncated and flagged.
    /// </summary>
    public int MaxLength { get; }

    public InputSanitizer(int maxLength = 8192)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxLength);
        MaxLength = maxLength;
    }

    [GeneratedRegex(@"\b(?:ignore|disregard|forget)\s+(?:all\s+|the\s+)?(?:previous|prior|above|earlier)\b", RegexOptions.IgnoreCase)]
    private static partial Regex IgnoreInstructions();

    [GeneratedRegex(@"\b(?:you\s+are\s+now|act\s+as|roleplay\s+as|pretend\s+to\s+be)\s+(?:a|an)?\s*(?:dan|jailbroken|unrestricted|sudo|dev\s*mode)\b", RegexOptions.IgnoreCase)]
    private static partial Regex Jailbreak();

    [GeneratedRegex(@"\b(?:repeat|reveal|print|show)\s+(?:your|the)\s+(?:system\s+prompt|initial\s+instructions|hidden\s+instructions)\b", RegexOptions.IgnoreCase)]
    private static partial Regex SystemPromptExtraction();

    // Zero-width + bidi-override + format chars often used in encoding tricks.
    [GeneratedRegex(@"[​‌‍‎‏‪-‮⁠-⁯﻿]")]
    private static partial Regex InvisibleChars();

    public SanitizationResult Sanitize(string input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var findings = new List<string>();

        var stripped = InvisibleChars().Replace(input, "");
        if (stripped.Length != input.Length)
        {
            findings.Add("invisible-characters-stripped");
        }

        if (stripped.Length > MaxLength)
        {
            findings.Add($"input-truncated-from-{stripped.Length}");
            stripped = stripped[..MaxLength];
        }

        if (IgnoreInstructions().IsMatch(stripped))
        {
            findings.Add("direct-prompt-injection");
        }
        if (Jailbreak().IsMatch(stripped))
        {
            findings.Add("jailbreak-attempt");
        }
        if (SystemPromptExtraction().IsMatch(stripped))
        {
            findings.Add("system-prompt-extraction");
        }

        var isSafe = findings.All(f => f.EndsWith("stripped", StringComparison.Ordinal) || f.StartsWith("input-truncated", StringComparison.Ordinal));
        return new SanitizationResult(IsSafe: isSafe, Findings: findings, SafeInput: stripped);
    }
}
