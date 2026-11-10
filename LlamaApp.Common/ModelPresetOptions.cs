using System.Globalization;
using System.Text;

namespace LlamaApp.Common;

/// <summary>One canonical per-model preset option: <c>key = value</c>.</summary>
public readonly record struct ModelPresetOption(string Key, string Value);

/// <summary>A user-facing parser error, addressed to the line the user typed.</summary>
public sealed record ModelPresetError(int Line, string? Key, string Message);

/// <summary>What the value of a supported option looks like.</summary>
public enum ModelPresetValueKind
{
    /// <summary>A non-negative integer within <see cref="ModelPresetSpec.Min"/>/<see cref="ModelPresetSpec.Max"/>.</summary>
    Integer,

    /// <summary>One of <see cref="ModelPresetSpec.Values"/>, case-insensitive.</summary>
    Enum,

    /// <summary>An integer in range, or one of <see cref="ModelPresetSpec.Values"/> (e.g. <c>auto</c>/<c>all</c>).</summary>
    IntegerOrEnum,

    /// <summary>A boolean flag (<c>--special</c>), accepted as true/false/on/off/1/0 or bare.</summary>
    Boolean,
}

/// <summary>
/// One entry of the curated per-model allowlist: the canonical INI key, the
/// CLI aliases users may type, and the value's type/range.
/// </summary>
public sealed record ModelPresetSpec(
    string Key,
    IReadOnlyList<string> Aliases,
    ModelPresetValueKind Kind,
    int Min,
    int Max,
    IReadOnlyList<string> Values,
    string? Note = null)
{
    public bool HasValues => Values.Count > 0;
}

/// <summary>
/// The curated allowlist of llama.cpp server options that may be set per
/// model, plus the parser and pre-save checker.
///
/// <para><b>Why an allowlist and not arbitrary flags.</b> llama.cpp's preset
/// loader throws on a key it does not recognize and the router then exits
/// before serving <c>/models</c>; the app would have no model rows and no way
/// to reach the field that caused it. A long-name denylist is also not enough:
/// the parser accepts every CLI alias and environment spelling, so a secret
/// or identity option slips through under <c>hft</c> / <c>HF_TOKEN</c> /
/// <c>m</c>. Only the canonical keys below are rendered; everything else is
/// rejected before it can reach the INI.</para>
///
/// <para><b>Deliberately excluded:</b> <c>cache-type-k/v</c> (the app's KV
/// setting is a router-level flag and wins over a per-model value — verified),
/// <c>ctx-size</c>/<c>spec-type</c> (the context picker and draft-head
/// detector own them per model), identity/bind/auth/router options, and
/// file-output options. Global custom serve arguments still win over any
/// per-model value; the UI warns about conflicts.</para>
/// </summary>
public static class ModelPresetOptions
{
    public const int MaxLines = 32;
    public const int MaxChars = 2048;
    public const int MaxLineChars = 512;
    public const int MaxValueChars = 64;

    private static readonly ModelPresetSpec[] Specs =
    [
        new("n-gpu-layers", ["ngl", "gpu-layers"], ModelPresetValueKind.IntegerOrEnum, 0, 999, ["auto", "all"]),
        new("threads", ["t"], ModelPresetValueKind.Integer, 1, 256, []),
        new("threads-batch", ["tb"], ModelPresetValueKind.Integer, 1, 256, []),
        new("flash-attn", ["fa"], ModelPresetValueKind.Enum, 0, 0, ["on", "off", "auto"]),
        new("special", ["sp"], ModelPresetValueKind.Boolean, 0, 0, ["true", "false"],
            "keeps special/control tokens in generated text (debugging)"),
        new("batch-size", ["b"], ModelPresetValueKind.Integer, 1, 8192, []),
        new("ubatch-size", ["ub"], ModelPresetValueKind.Integer, 1, 8192, []),
        new("n-cpu-moe", ["ncmoe"], ModelPresetValueKind.Integer, 0, 999, []),
        new("cache-reuse", [], ModelPresetValueKind.Integer, 0, 65536, []),
        new("fit", [], ModelPresetValueKind.Enum, 0, 0, ["on", "off"]),
        new("fit-target", ["fitt"], ModelPresetValueKind.Integer, 0, 1048576, []),
        new("fit-ctx", ["fitc"], ModelPresetValueKind.Integer, 0, 1048576, []),
        new("load-mode", ["lm"], ModelPresetValueKind.Enum, 0, 0,
            ["auto", "none", "mmap", "mlock", "mmap+mlock", "dio"]),
        new("parallel", ["np"], ModelPresetValueKind.Integer, 1, 256, []),
    ];

    private static readonly Dictionary<string, ModelPresetSpec> ByAlias;
    private static readonly Dictionary<string, string> Reserved;

    static ModelPresetOptions()
    {
        ByAlias = new Dictionary<string, ModelPresetSpec>(StringComparer.OrdinalIgnoreCase);
        foreach (var spec in Specs)
        {
            ByAlias[spec.Key] = spec;
            foreach (var alias in spec.Aliases)
                ByAlias[alias] = spec;
        }

        Reserved = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["cache-type-k"] = "KV cache type is set globally in Settings → Llama; it can't be set per model.",
            ["ctk"] = "KV cache type is set globally in Settings → Llama; it can't be set per model.",
            ["cache-type-v"] = "KV cache type is set globally in Settings → Llama; it can't be set per model.",
            ["ctv"] = "KV cache type is set globally in Settings → Llama; it can't be set per model.",
            ["ctx-size"] = "Context length is set with the Context length picker above.",
            ["c"] = "Context length is set with the Context length picker above.",
            ["spec-type"] = "Speculative decoding is detected automatically; it can't be set here.",
            ["model"] = "The model file is managed by Llama.",
            ["m"] = "The model file is managed by Llama.",
            ["alias"] = "The model alias is managed by Llama.",
            ["a"] = "The model alias is managed by Llama.",
            ["hf"] = "The model source is managed by Llama.",
            ["hf-repo"] = "The model source is managed by Llama.",
            ["hfr"] = "The model source is managed by Llama.",
            ["hf-file"] = "The model source is managed by Llama.",
            ["hff"] = "The model source is managed by Llama.",
            ["hf-token"] = "Tokens can't be set per model.",
            ["hft"] = "Tokens can't be set per model.",
            ["host"] = "Server bind options are managed by Llama.",
            ["port"] = "Server bind options are managed by Llama.",
            ["api-key"] = "Server auth options are managed by Llama.",
            ["api-key-file"] = "Server auth options are managed by Llama.",
            ["ssl-key-file"] = "Server TLS options are managed by Llama.",
            ["ssl-cert-file"] = "Server TLS options are managed by Llama.",
            ["ssl-key-pass"] = "Server TLS options are managed by Llama.",
            ["models-dir"] = "Router options are managed by Llama.",
            ["models-preset"] = "Router options are managed by Llama.",
            ["models-max"] = "Router options are managed by Llama.",
            ["models-memory-margin"] = "Router options are managed by Llama.",
            ["models-autoload"] = "Router options are managed by Llama.",
            ["sleep-idle-seconds"] = "Router options are managed by Llama.",
            ["mmproj"] = "Vision/sidecar files are wired automatically.",
            ["spec-draft-model"] = "Draft heads are wired automatically.",
            ["log-file"] = "File output options can't be set per model.",
            ["slot-save-path"] = "File output options can't be set per model.",
            ["version"] = "Not a per-model option.",
        };
    }

    /// <summary>The curated allowlist, for UI documentation.</summary>
    public static IReadOnlyList<ModelPresetSpec> Supported => Specs;

    /// <summary>
    /// Parses the details-view text. One option per line, either
    /// <c>key = value</c> or <c>--key value</c> / <c>-alias value</c>
    /// (<c>--key=value</c> works too). Blank and comment (<c>#</c>/<c>;</c>)
    /// lines are ignored. Returns false when any line is invalid; callers must
    /// not render or save on false.
    /// </summary>
    public static bool TryParse(
        string? text,
        out IReadOnlyList<ModelPresetOption> options,
        out IReadOnlyList<ModelPresetError> errors)
    {
        var parsed = new List<ModelPresetOption>();
        var failures = new List<ModelPresetError>();
        options = parsed;
        errors = failures;

        if (string.IsNullOrWhiteSpace(text)) return true;

        // WinUI TextBox line endings are CRLF/CR depending on the input path.
        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal)
                             .Replace('\r', '\n');
        if (normalized.Length > MaxChars)
        {
            failures.Add(new ModelPresetError(0, null,
                $"Extra options are too long (max {MaxChars} characters)."));
            return false;
        }

        var lines = normalized.Split('\n');
        if (lines.Length > MaxLines)
        {
            failures.Add(new ModelPresetError(0, null,
                $"Too many lines (max {MaxLines})."));
            return false;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < lines.Length && failures.Count < 12; i++)
        {
            var lineNumber = i + 1;
            var line = lines[i].Trim();
            if (line.Length == 0 || line[0] is '#' or ';') continue;
            if (line.Length > MaxLineChars)
            {
                failures.Add(new ModelPresetError(lineNumber, null,
                    $"Line is too long (max {MaxLineChars} characters)."));
                continue;
            }

            if (!TrySplit(line, out var rawKey, out var rawValue, out var splitError))
            {
                failures.Add(new ModelPresetError(lineNumber, null, splitError));
                continue;
            }

            var key = NormalizeKey(rawKey);
            if (key.Length == 0 || key.Any(char.IsWhiteSpace))
            {
                failures.Add(new ModelPresetError(lineNumber, null,
                    "Invalid option name. Write name = value, e.g. n-gpu-layers = 20."));
                continue;
            }

            if (FindReserved(key) is { } reserved)
            {
                failures.Add(new ModelPresetError(lineNumber, key, reserved));
                continue;
            }

            if (!ByAlias.TryGetValue(key, out var spec))
            {
                var suggestion = Suggest(key);
                failures.Add(new ModelPresetError(lineNumber, key,
                    suggestion is null
                        ? $"Unknown option '{key}'. Supported: {DescribeKeysCompact()}."
                        : $"Unknown option '{key}' — did you mean '{suggestion}'?"));
                continue;
            }

            if (!TryNormalizeValue(spec, rawValue, out var value, out var valueError))
            {
                failures.Add(new ModelPresetError(lineNumber, spec.Key, valueError));
                continue;
            }

            if (!seen.Add(spec.Key))
            {
                failures.Add(new ModelPresetError(lineNumber, spec.Key,
                    $"Duplicate option '{spec.Key}'. Set it once."));
                continue;
            }

            parsed.Add(new ModelPresetOption(spec.Key, value));
        }

        return failures.Count == 0;
    }

    /// <summary>Renders one canonical line the INI writer emits.</summary>
    public static string ToIniLine(ModelPresetOption option)
        => $"{option.Key} = {option.Value}";

    /// <summary>Formats a parser error for inline display (line-addressed).</summary>
    public static string FormatError(ModelPresetError error)
        => error.Line > 0 ? $"Line {error.Line}: {error.Message}" : error.Message;

    /// <summary>
    /// Canonical allowlist keys that also appear in the global custom serve
    /// arguments. The router's base preset wins over per-model sections, so
    /// the UI warns instead of silently doing nothing.
    /// </summary>
    public static IReadOnlyList<string> FindGlobalConflicts(
        string? globalArguments,
        IReadOnlyList<ModelPresetOption> options)
    {
        if (string.IsNullOrWhiteSpace(globalArguments) || options.Count == 0) return [];

        IReadOnlyList<string> tokens;
        try { tokens = ArgumentTokenizer.Tokenize(globalArguments); }
        catch (FormatException) { return []; }

        var globalKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var token in tokens)
        {
            if (token.Length < 2 || token[0] != '-') continue;
            var key = token.TrimStart('-');
            var eq = key.IndexOf('=');
            if (eq >= 0) key = key[..eq];
            if (ByAlias.TryGetValue(NormalizeKey(key), out var spec))
                globalKeys.Add(spec.Key);
        }

        return options
            .Select(o => o.Key)
            .Where(globalKeys.Contains)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Human-readable allowlist for the info flyout.</summary>
    public static string DescribeSupported()
    {
        var sb = new StringBuilder();
        foreach (var spec in Specs)
        {
            sb.Append(spec.Key);
            if (spec.Aliases.Count > 0)
                sb.Append(" (").Append(string.Join(", ", spec.Aliases)).Append(')');
            sb.Append(": ").Append(DescribeValue(spec));
            if (!string.IsNullOrEmpty(spec.Note))
                sb.Append(" — ").Append(spec.Note);
            sb.Append('\n');
        }
        return sb.ToString().TrimEnd();
    }

    private static string DescribeValue(ModelPresetSpec spec) => spec.Kind switch
    {
        ModelPresetValueKind.Integer => $"{spec.Min}–{spec.Max}",
        ModelPresetValueKind.Enum => string.Join(" / ", spec.Values),
        ModelPresetValueKind.Boolean => "true / false",
        _ => $"{spec.Min}–{spec.Max}, {string.Join(", ", spec.Values)}",
    };

    private static string DescribeKeysCompact()
        => string.Join(", ", Specs.Select(s => s.Key));

    private static bool TrySplit(
        string line,
        out string rawKey,
        out string rawValue,
        out string error)
    {
        rawKey = "";
        rawValue = "";
        error = "";

        if (line[0] == '-')
        {
            IReadOnlyList<string> tokens;
            try
            {
                tokens = ArgumentTokenizer.Tokenize(line);
            }
            catch (FormatException)
            {
                error = "Unbalanced quote.";
                return false;
            }

            if (tokens.Count == 0)
            {
                error = "Empty option.";
                return false;
            }

            var first = tokens[0];
            var eq = first.IndexOf('=');
            if (eq >= 0)
            {
                rawKey = first[..eq];
                rawValue = first[(eq + 1)..];
                if (tokens.Count > 1)
                {
                    error = "One option per line.";
                    return false;
                }
            }
            else if (tokens.Count == 1)
            {
                rawKey = first;
                rawValue = "";
            }
            else if (tokens.Count == 2 && !tokens[1].StartsWith('-'))
            {
                rawKey = first;
                rawValue = tokens[1];
            }
            else
            {
                error = "One option per line: --name value.";
                return false;
            }

            rawKey = rawKey.TrimStart('-');
            if (rawKey.Length == 0)
            {
                error = "Invalid option name.";
                return false;
            }
            return true;
        }

        var assign = line.IndexOf('=');
        if (assign < 0)
        {
            error = "Write name = value, e.g. n-gpu-layers = 20.";
            return false;
        }

        rawKey = line[..assign].Trim().TrimStart('-');
        rawValue = line[(assign + 1)..].Trim();
        if (rawKey.Length == 0)
        {
            error = "Missing option name before '='.";
            return false;
        }
        return true;
    }

    private static string NormalizeKey(string key)
        => key.Trim().TrimStart('-').Trim().ToLowerInvariant().Replace('_', '-');

    private static string? FindReserved(string key)
    {
        if (Reserved.TryGetValue(key, out var message)) return message;
        // A negated spelling of a reserved boolean belongs to the same family
        // for messaging (e.g. no-models-autoload); the allowlist still rejects
        // it because it is not a supported key.
        if (key.StartsWith("no-", StringComparison.OrdinalIgnoreCase) &&
            Reserved.TryGetValue(key[3..], out message))
            return message;
        return null;
    }

    private static bool TryNormalizeValue(
        ModelPresetSpec spec,
        string rawValue,
        out string value,
        out string error)
    {
        value = rawValue.Trim();
        error = "";

        if (value.Length == 0)
        {
            // A bare flag (e.g. `--special` or `special =`) means "enabled".
            if (spec.Kind == ModelPresetValueKind.Boolean)
            {
                value = "true";
                return true;
            }
            error = $"'{spec.Key}' needs a value ({DescribeValue(spec)}).";
            return false;
        }
        if (value.Length > MaxValueChars)
        {
            error = $"'{spec.Key}' value is too long.";
            return false;
        }
        foreach (var c in value)
            if (char.IsControl(c) || c is '#' or ';')
            {
                error = $"'{spec.Key}' value can't contain control characters, # or ;.";
                return false;
            }
        if (value.Any(char.IsWhiteSpace))
        {
            error = $"'{spec.Key}' value can't contain spaces.";
            return false;
        }

        if (spec.Kind == ModelPresetValueKind.Boolean)
        {
            var boolean = value.ToLowerInvariant();
            if (boolean is "true" or "on" or "1")
            {
                value = "true";
                return true;
            }
            if (boolean is "false" or "off" or "0")
            {
                value = "false";
                return true;
            }
            error = $"'{spec.Key}' must be true/false (or on/off, 1/0).";
            return false;
        }

        if (spec.Kind is ModelPresetValueKind.Integer or ModelPresetValueKind.IntegerOrEnum &&
            int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number))
        {
            if (number < spec.Min || number > spec.Max)
            {
                error = $"'{spec.Key}' must be between {spec.Min} and {spec.Max}.";
                return false;
            }
            value = number.ToString(CultureInfo.InvariantCulture);
            return true;
        }

        if (spec.HasValues)
        {
            var candidate = value;
            var match = spec.Values.FirstOrDefault(v =>
                string.Equals(v, candidate, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                value = match;
                return true;
            }
        }

        error = spec.Kind switch
        {
            ModelPresetValueKind.Integer =>
                $"'{spec.Key}' must be an integer between {spec.Min} and {spec.Max}.",
            ModelPresetValueKind.Enum =>
                $"'{spec.Key}' must be one of: {string.Join(", ", spec.Values)}.",
            _ =>
                $"'{spec.Key}' must be {spec.Min}–{spec.Max}, {string.Join(", ", spec.Values)}.",
        };
        return false;
    }

    private static string? Suggest(string key)
    {
        string? best = null;
        var bestDistance = int.MaxValue;
        foreach (var candidate in ByAlias.Keys)
        {
            var distance = Distance(key, candidate);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = candidate;
            }
        }
        return bestDistance <= 2 ? ByAlias[best!].Key : null;
    }

    private static int Distance(string a, string b)
    {
        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++) previous[j] = j;

        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                current[j] = Math.Min(
                    Math.Min(current[j - 1] + 1, previous[j] + 1),
                    previous[j - 1] + cost);
            }
            (previous, current) = (current, previous);
        }
        return previous[b.Length];
    }
}

