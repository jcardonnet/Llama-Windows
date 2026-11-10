using LlamaApp.Common;
using Xunit;

namespace LlamaApp.LlamaCpp.Tests;

/// <summary>
/// Built-in family defaults. LightOn's colgrep-agent variants need the router's
/// <c>--special</c> flag; every variant matches by id substring, and the user
/// can still override it with <c>special = false</c>.
/// </summary>
public sealed class ModelPresetDefaultsTests
{
    [Theory]
    [InlineData("lightonai/colgrep-agent-2B-GGUF:Q8_0")]
    [InlineData("lightonai/colgrep-agent-2B-GGUF:Q4_K_M")]
    [InlineData("colgrep-agent-2B-Q8_0")]
    [InlineData("COLGREP-AGENT-7B-Q4_0")]
    public void ColgrepVariants_Match(string id)
    {
        Assert.True(ModelPresetDefaults.HasDefaults(id));
        var option = Assert.Single(ModelPresetDefaults.For(id));
        Assert.Equal(new ModelPresetOption("special", "true"), option);
    }

    [Theory]
    [InlineData("org/Qwen3-4B-GGUF:Q4_K_M")]
    [InlineData("lightonai/other-model-GGUF:Q8_0")]
    [InlineData(null)]
    [InlineData("")]
    public void OtherModels_DoNotMatch(string? id)
    {
        Assert.False(ModelPresetDefaults.HasDefaults(id));
        Assert.Empty(ModelPresetDefaults.For(id));
    }

    [Fact]
    public void Merge_AddsTheDefaultUnderUserOptions()
    {
        var merged = ModelPresetDefaults.Merge(
            "lightonai/colgrep-agent-2B-GGUF:Q8_0",
            [new ModelPresetOption("n-gpu-layers", "20")]);

        Assert.Equal(
            new[]
            {
                new ModelPresetOption("special", "true"),
                new ModelPresetOption("n-gpu-layers", "20"),
            },
            merged);
    }

    [Fact]
    public void Merge_LetsTheUserOverrideSpecial()
    {
        var merged = ModelPresetDefaults.Merge(
            "colgrep-agent-2B-Q8_0",
            [new ModelPresetOption("special", "false")]);

        var only = Assert.Single(merged);
        Assert.Equal(new ModelPresetOption("special", "false"), only);
    }

    [Fact]
    public void Merge_LeavesOtherModelsAlone()
    {
        var user = new[] { new ModelPresetOption("threads", "8") };
        Assert.Same(user, ModelPresetDefaults.Merge("org/Qwen3-4B-GGUF:Q4_K_M", user));
    }

    [Fact]
    public void DescribeFor_MentionsTheOverride()
    {
        var text = ModelPresetDefaults.DescribeFor("lightonai/colgrep-agent-2B-GGUF:Q4_K_M");
        Assert.NotNull(text);
        Assert.Contains("special = true", text);
        Assert.Contains("special = false", text);
        Assert.Null(ModelPresetDefaults.DescribeFor("org/Qwen3-4B-GGUF:Q4_K_M"));
    }
}
