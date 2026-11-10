namespace LlamaApp.Common;

/// <summary>
/// Shared rules for the <c>--models-preset</c> INI syntax (llama.cpp's
/// <c>common/preset.cpp</c>): what can safely head a section and what can
/// safely sit after a key.
///
/// <para><b>Why this exists.</b> The app writes that INI itself, but model
/// ids, enrolled file stems, and hand-edited settings all reach it as data.
/// A section named <c>*</c> is llama.cpp's global preset; a duplicate header
/// resets the earlier section; a value containing <c>#</c> or <c>;</c> is
/// silently truncated at the comment marker. One function per sink, shared by
/// the app renderer and the enrolled-folder renderer, so the rules cannot
/// drift apart.</para>
/// </summary>
public static class PresetIniSyntax
{
    /// <summary>Longest section name the app will render.</summary>
    public const int MaxSectionIdChars = 512;

    /// <summary>Longest value the app will render.</summary>
    public const int MaxValueChars = 1024;

    /// <summary>Comparer used for section identity (case-only renames).</summary>
    public static readonly StringComparer SectionComparer = StringComparer.OrdinalIgnoreCase;

    /// <summary>
    /// Whether <paramref name="id"/> can head a preset section. Rejects
    /// empty/whitespace, leading or trailing whitespace (the parser trims a
    /// header, which would silently apply the section to another id), the
    /// global section names <c>*</c> and <c>default</c>, brackets (a header
    /// ends at the first <c>]</c>), control characters, and overlong ids.
    /// </summary>
    public static bool IsSafeSectionId(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return false;
        if (id.Length > MaxSectionIdChars) return false;
        if (!string.Equals(id, id.Trim(), StringComparison.Ordinal)) return false;
        if (id is "*") return false;
        if (string.Equals(id, "default", StringComparison.OrdinalIgnoreCase)) return false;
        foreach (var c in id)
            if (c is '[' or ']' || char.IsControl(c))
                return false;
        return true;
    }

    /// <summary>
    /// The section spelling the router itself uses: llama.cpp uppercases the
    /// segment after the last <c>:</c> (the quant tag), so <c>repo:q4_0</c>
    /// and <c>repo:Q4_0</c> are one preset. Applied before grouping so a
    /// hand-edited case variant cannot create a second section that resets
    /// the first.
    /// </summary>
    public static string CanonicalSectionId(string id)
    {
        if (string.IsNullOrEmpty(id)) return id;
        var colon = id.LastIndexOf(':');
        if (colon < 0 || colon == id.Length - 1) return id;
        var tag = id[(colon + 1)..];
        // Only canonicalize id-shaped tags; a colon in an enrolled filename is
        // impossible on Windows, so this is always a model id in practice.
        foreach (var c in tag)
            if (!(char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-' or '+'))
                return id;
        return id[..(colon + 1)] + tag.ToUpperInvariant();
    }

    /// <summary>
    /// Whether <paramref name="value"/> can follow a preset key. The parser
    /// ends a value at <c>#</c>/<c>;</c> (comment start), at a newline, and
    /// ignores a trailing comment — so those characters must never be
    /// rendered. Edge whitespace is rejected because the parser trims it;
    /// internal spaces (enrolled paths) are allowed.
    /// </summary>
    public static bool IsSafeValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        if (value.Length > MaxValueChars) return false;
        if (!string.Equals(value, value.Trim(), StringComparison.Ordinal)) return false;
        foreach (var c in value)
            if (char.IsControl(c) || c is '#' or ';')
                return false;
        return true;
    }
}
