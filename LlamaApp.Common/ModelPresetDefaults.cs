namespace LlamaApp.Common;

/// <summary>
/// Built-in per-model preset defaults for model families whose chat template
/// needs a router flag to work at all.
///
/// <para><b>colgrep-agent.</b> LightOn's colgrep-agent GGUFs require
/// <c>--special</c> so their control tokens survive detokenization; without it
/// the model's output is not parseable. The rule is deliberately narrow — the
/// model id must contain <c>colgrep-agent</c> — and applies to every variant
/// (any quant, an HF-cache id like
/// <c>lightonai/colgrep-agent-2B-GGUF:Q8_0</c>, or an enrolled file named
/// <c>colgrep-agent-2B-Q8_0</c>). It is a render-time default, not a settings
/// entry: the user's own <c>special = false</c> overrides it, and clearing the
/// field returns to the default.</para>
/// </summary>
public static class ModelPresetDefaults
{
    private const string ColgrepMarker = "colgrep-agent";

    private static readonly ModelPresetOption[] ColgrepDefaults =
    [
        new("special", "true"),
    ];

    /// <summary>True when this model id belongs to a family with a built-in default.</summary>
    public static bool HasDefaults(string? modelId)
        => !string.IsNullOrEmpty(modelId) &&
           modelId.Contains(ColgrepMarker, StringComparison.OrdinalIgnoreCase);

    /// <summary>The built-in options for the model (empty for most models).</summary>
    public static IReadOnlyList<ModelPresetOption> For(string? modelId)
        => HasDefaults(modelId) ? ColgrepDefaults : [];

    /// <summary>
    /// Merges the built-in defaults under the user's options; a user option
    /// with the same key wins (so <c>special = false</c> disables it).
    /// </summary>
    public static IReadOnlyList<ModelPresetOption> Merge(
        string? modelId,
        IReadOnlyList<ModelPresetOption> userOptions)
    {
        if (!HasDefaults(modelId)) return userOptions;

        var byKey = new Dictionary<string, ModelPresetOption>(StringComparer.OrdinalIgnoreCase);
        foreach (var option in ColgrepDefaults) byKey[option.Key] = option;
        foreach (var option in userOptions) byKey[option.Key] = option;
        return byKey.Values.ToList();
    }

    /// <summary>User-facing explanation shown in the details view, or null.</summary>
    public static string? DescribeFor(string? modelId)
        => HasDefaults(modelId)
            ? "Built-in default: special = true (required by colgrep-agent models). Set special = false to override."
            : null;
}
