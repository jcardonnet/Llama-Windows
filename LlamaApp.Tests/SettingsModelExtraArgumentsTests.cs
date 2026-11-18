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
        Assert.Empty(new Settings().ModelAliases);
    }

    [Fact]
    public void MissingProperty_DefaultsToEmpty()
    {
        var settings = Settings.FromJson("""{"ServerPort":9931,"IdleUnloadSeconds":-1}""");
        Assert.Empty(settings.ModelExtraArguments);
        Assert.Empty(settings.ModelAliases);
    }

    [Fact]
    public void ExplicitNullCollections_AreNormalized()
    {
        var settings = Settings.FromJson("""
            {
              "ModelContextLengths": null,
              "ModelSpeculativeTypes": null,
              "ModelFolders": null,
              "ModelExtraArguments": null,
              "ModelAliases": null
            }
            """);

        Assert.NotNull(settings.ModelContextLengths);
        Assert.NotNull(settings.ModelSpeculativeTypes);
        Assert.NotNull(settings.ModelFolders);
        Assert.NotNull(settings.ModelExtraArguments);
        Assert.NotNull(settings.ModelAliases);
        Assert.Empty(settings.ModelExtraArguments);
        Assert.Empty(settings.ModelAliases);
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
            ModelAliases =
            {
                ["org/repo:Q4_0"] = "my-model, gpt-4o",
            },
        };

        var json = JsonSerializer.Serialize(original, SettingsJsonContext.Default.Settings);
        var loaded = Settings.FromJson(json);

        Assert.Equal(
            "n-gpu-layers = 20\r\n--flash-attn on",
            loaded.ModelExtraArguments["org/repo:Q4_0"]);
        Assert.Equal("my-model, gpt-4o", loaded.ModelAliases["org/repo:Q4_0"]);
    }

    [Fact]
    public void Serialization_IncludesTheDictionaries()
    {
        var settings = new Settings();
        settings.ModelExtraArguments["fam"] = "threads = 8";
        settings.ModelAliases["fam"] = "my-model";

        var json = JsonSerializer.Serialize(settings, SettingsJsonContext.Default.Settings);

        Assert.Contains("\"ModelExtraArguments\"", json);
        Assert.Contains("threads = 8", json);
        Assert.Contains("\"ModelAliases\"", json);
        Assert.Contains("my-model", json);
    }
}
