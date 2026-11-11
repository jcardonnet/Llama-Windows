using LlamaApp.Common;
using Xunit;

namespace LlamaApp.LlamaCpp.Tests;

/// <summary>
/// The pre-save checker for per-model preset options. The rules that matter:
/// only the curated allowlist is accepted (an unknown key aborts the whole
/// router, so it must never reach the INI), aliases/CLI spellings normalize to
/// canonical keys, values are type/range checked, and reserved identity/secret
/// options are rejected with a message that never echoes the value.
/// </summary>
public sealed class ModelPresetOptionsTests
{
    private static IReadOnlyList<ModelPresetOption> Parse(string text)
    {
        Assert.True(
            ModelPresetOptions.TryParse(text, out var options, out var errors),
            string.Join("; ", errors.Select(ModelPresetOptions.FormatError)));
        return options;
    }

    private static void AssertInvalid(string text, string expectedFragment)
    {
        Assert.False(ModelPresetOptions.TryParse(text, out _, out var errors));
        Assert.Contains(
            errors,
            e => e.Message.Contains(expectedFragment, StringComparison.OrdinalIgnoreCase));
    }

    // ---- accepted forms ----

    [Fact]
    public void IniAndCliForms_NormalizeToCanonicalKeys()
    {
        var options = Parse(
            "n-gpu-layers = 20\n" +
            "--flash-attn on\n" +
            "-t 8\n" +
            "--batch-size=2048\n" +
            "n_cpu_moe=4");

        Assert.Equal(
            new[]
            {
                new ModelPresetOption("n-gpu-layers", "20"),
                new ModelPresetOption("flash-attn", "on"),
                new ModelPresetOption("threads", "8"),
                new ModelPresetOption("batch-size", "2048"),
                new ModelPresetOption("n-cpu-moe", "4"),
            },
            options);
    }

    [Fact]
    public void WindowsLineEndings_AndMixedEndings_AreNormalized()
    {
        Assert.Equal(2, Parse("n-gpu-layers = 20\r\nthreads = 8\r").Count);
        Assert.Equal(3, Parse("n-gpu-layers = 20\r\nthreads = 8\nfit = on").Count);
    }

    [Fact]
    public void Alias_AcceptsSingleAndCommaSeparatedNames()
    {
        var options = Parse(
            "alias = gpt-4o, my-model, meta-llama/Llama-3.1-8B-Instruct:Q4_K_M");

        var only = Assert.Single(options);
        Assert.Equal("alias", only.Key);
        Assert.Equal(
            "gpt-4o, my-model, meta-llama/Llama-3.1-8B-Instruct:Q4_K_M",
            only.Value);
    }

    [Fact]
    public void Alias_RejectsEmptyOrOverlongNames()
    {
        AssertInvalid("alias = ", "needs a value");
        AssertInvalid("alias = foo,,bar", "non-empty");
        AssertInvalid("alias = " + new string('x', 129), "128");
    }

    [Fact]
    public void FindAliasConflicts_FlagsModelIdsAndOtherModelsAliases()
    {
        var conflicts = ModelPresetOptions.FindAliasConflicts(
            "org/model:Q4_K_M",
            Parse("alias = gpt-4o, my-model"),
            ["gpt-4o", "org/model:Q4_K_M"],
            [Parse("alias = my-model")]);

        Assert.Equal(new[] { "gpt-4o", "my-model" }, conflicts);
    }

    [Fact]
    public void FindAliasConflicts_AllowsUniqueAliases()
    {
        Assert.Empty(ModelPresetOptions.FindAliasConflicts(
            "org/model:Q4_K_M",
            Parse("alias = my-model"),
            ["org/other:Q8_0"],
            [Parse("alias = other-model")]));
    }

    [Fact]
    public void BlankAndCommentLines_AreIgnored()
    {
        var options = Parse("# comment\n; another\n\n  \nthreads = 8\n");
        Assert.Equal(new[] { new ModelPresetOption("threads", "8") }, options);
    }

    [Fact]
    public void EmptyText_IsValidAndEmpty()
    {
        Assert.True(ModelPresetOptions.TryParse(null, out var options, out var errors));
        Assert.Empty(options);
        Assert.Empty(errors);
        Assert.True(ModelPresetOptions.TryParse("  \n", out options, out errors));
        Assert.Empty(options);
    }

    [Theory]
    [InlineData("n-gpu-layers = auto", "n-gpu-layers", "auto")]
    [InlineData("n-gpu-layers = all", "n-gpu-layers", "all")]
    [InlineData("n-gpu-layers = 0", "n-gpu-layers", "0")]
    [InlineData("load-mode = mmap+mlock", "load-mode", "mmap+mlock")]
    [InlineData("fit = off", "fit", "off")]
    [InlineData("flash-attn = AUTO", "flash-attn", "auto")]
    [InlineData("special = true", "special", "true")]
    [InlineData("special = off", "special", "false")]
    [InlineData("special = 1", "special", "true")]
    [InlineData("--special", "special", "true")]
    [InlineData("-sp", "special", "true")]
    [InlineData("threads = 256", "threads", "256")]
    public void TypedValues_AreAcceptedAndCanonicalized(
        string text, string key, string value)
    {
        var option = Assert.Single(Parse(text));
        Assert.Equal(key, option.Key);
        Assert.Equal(value, option.Value);
    }

    // ---- rejected forms ----

    [Theory]
    [InlineData("n-gpu-layer = 20", "did you mean 'n-gpu-layers'")]
    [InlineData("thread = 8", "did you mean")]
    [InlineData("something-else = 1", "Unknown option")]
    public void UnknownKeys_AreRejectedWithSuggestion(string text, string fragment)
        => AssertInvalid(text, fragment);

    [Theory]
    [InlineData("cache-type-k = q8_0", "globally")]
    [InlineData("ctk = q8_0", "globally")]
    [InlineData("ctx-size = 4096", "picker")]
    [InlineData("c = 4096", "picker")]
    [InlineData("spec-type = draft-mtp", "automatically")]
    [InlineData("model = C:\\x.gguf", "managed")]
    [InlineData("m = C:\\x.gguf", "managed")]
    [InlineData("hf = other/repo", "managed")]
    [InlineData("hf-token = hf_secretvalue", "Tokens")]
    [InlineData("hft = hf_secretvalue", "Tokens")]
    [InlineData("HF_TOKEN = hf_secretvalue", "Tokens")]
    [InlineData("host = 0.0.0.0", "bind")]
    [InlineData("port = 1234", "bind")]
    [InlineData("api-key = secret", "auth")]
    [InlineData("log-file = C:\\x.log", "file output")]
    [InlineData("version = 1", "per-model")]
    public void ReservedKeys_AreRejected(string text, string fragment)
        => AssertInvalid(text, fragment);

    [Fact]
    public void ReservedErrors_NeverEchoTheValue()
    {
        const string secret = "hf_SUPERSECRET";
        Assert.False(ModelPresetOptions.TryParse(
            $"hft = {secret}", out _, out var errors));
        var message = string.Join(" ", errors.Select(ModelPresetOptions.FormatError));
        Assert.DoesNotContain(secret, message);
    }

    [Theory]
    [InlineData("threads = 0", "between 1 and 256")]
    [InlineData("threads = 257", "between 1 and 256")]
    [InlineData("threads = -1", "between 1 and 256")]
    [InlineData("threads = eight", "integer")]
    [InlineData("flash-attn = true", "one of")]
    [InlineData("special = maybe", "must be true/false")]
    [InlineData("load-mode = turbo", "one of")]
    [InlineData("n-gpu-layers = 1000", "between 0 and 999")]
    [InlineData("threads = 8 9", "spaces")]
    [InlineData("threads = 8 # note", "control characters")]
    [InlineData("threads = 8; note", "control characters")]
    [InlineData("threads =", "needs a value")]
    [InlineData("threads", "Write name = value")]
    public void InvalidValues_AreRejected(string text, string fragment)
        => AssertInvalid(text, fragment);

    [Fact]
    public void DuplicateKeys_AreRejected()
        => AssertInvalid("threads = 8\n-t 4", "Duplicate");

    [Fact]
    public void ValueLessCliOption_IsRejected()
        => AssertInvalid("--threads", "needs a value");

    [Fact]
    public void MultipleOptionsOnOneLine_AreRejected()
        => AssertInvalid("--threads 8 --flash-attn on", "One option per line");

    [Fact]
    public void OverlongInput_IsRejected()
    {
        AssertInvalid(new string('x', ModelPresetOptions.MaxChars + 1), "too long");
        AssertInvalid(string.Join('\n', Enumerable.Repeat("threads = 8", 40)), "Too many lines");
        AssertInvalid("threads = " + new string('8', ModelPresetOptions.MaxLineChars), "too long");
    }

    // ---- global precedence ----

    [Fact]
    public void FindGlobalConflicts_ReturnsOverriddenPerModelKeys()
    {
        var options = Parse("threads = 6\nn-gpu-layers = 10\nflash-attn = on");
        var conflicts = ModelPresetOptions.FindGlobalConflicts(
            "--threads 8 --flash-attn=on --fit on", options);

        Assert.Equal(new[] { "flash-attn", "threads" }, conflicts.OrderBy(x => x, StringComparer.Ordinal));
    }

    [Fact]
    public void FindGlobalConflicts_HandlesEmptyOrInvalidGlobalText()
    {
        var options = Parse("threads = 6");
        Assert.Empty(ModelPresetOptions.FindGlobalConflicts(null, options));
        Assert.Empty(ModelPresetOptions.FindGlobalConflicts("--threads 8", Array.Empty<ModelPresetOption>()));
        Assert.Empty(ModelPresetOptions.FindGlobalConflicts("\"unbalanced", options));
    }

    [Fact]
    public void DescribeSupported_ListsEveryAllowlistedKey()
    {
        var text = ModelPresetOptions.DescribeSupported();
        Assert.Contains("n-gpu-layers", text);
        Assert.Contains("flash-attn", text);
        Assert.Contains("load-mode", text);
        Assert.Contains("special", text);
        Assert.Contains("debugging", text);
        Assert.DoesNotContain("cache-type", text);
        Assert.DoesNotContain("ctx-size", text);
    }
}

