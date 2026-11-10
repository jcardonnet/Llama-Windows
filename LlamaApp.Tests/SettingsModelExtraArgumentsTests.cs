using System.Text.Json;
using Xunit;

namespace LlamaApp.Tests;

/// <summary>
/// Persistence for the curated per-model preset options. The critical cases
/// are an older settings.json (property missing), an explicit JSON null (the
/// collection would otherwise crash Save and the details view), and the raw
/// text surviving the source-generated round-trip exactly.
/// </summary>
public sealed class SettingsModelExtraArgumentsTests
{
    [Fact]
    public void DefaultsToEmpty()
    {
        Assert.Empty(new Settings().ModelExtraArguments);
    }

    [Fact]
    public void MissingProperty_DefaultsToEmpty()
    {
        var settings = Settings.FromJson("""{"ServerPort":9931,"IdleUnloadSeconds":-1}""");
        Assert.Empty(settings.ModelExtraArguments);
    }

    [Fact]
    public void ExplicitNullCollections_AreNormalized()
    {
        var settings = Settings.FromJson("""
            {
              "ModelContextLengths": null,
              "ModelSpeculativeTypes": null,
              "ModelFolders": null,
              "ModelExtraArguments": null
            }
            """);

        Assert.NotNull(settings.ModelContextLengths);
        Assert.NotNull(settings.ModelSpeculativeTypes);
        Assert.NotNull(settings.ModelFolders);
        Assert.NotNull(settings.ModelExtraArguments);
        Assert.Empty(settings.ModelExtraArguments);
    }

    [Fact]
    public void RawText_RoundTripsThroughTheSourceContext()
    {
        var original = new Settings
        {
            ModelExtraArguments =
            {
                ["org/repo:Q4_0"] = "n-gpu-layers = 20\r\n--flash-attn on",
            },
        };

        var json = JsonSerializer.Serialize(original, SettingsJsonContext.Default.Settings);
        var loaded = Settings.FromJson(json);

        Assert.Equal(
            "n-gpu-layers = 20\r\n--flash-attn on",
            loaded.ModelExtraArguments["org/repo:Q4_0"]);
    }

    [Fact]
    public void Serialization_IncludesTheDictionary()
    {
        var settings = new Settings();
        settings.ModelExtraArguments["fam"] = "threads = 8";

        var json = JsonSerializer.Serialize(settings, SettingsJsonContext.Default.Settings);

        Assert.Contains("\"ModelExtraArguments\"", json);
        Assert.Contains("threads = 8", json);
    }
}
