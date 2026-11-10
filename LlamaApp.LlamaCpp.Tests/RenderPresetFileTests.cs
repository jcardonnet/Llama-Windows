using LlamaApp.Llama;
using Xunit;

namespace LlamaApp.LlamaCpp.Tests;

/// <summary>
/// The composed <c>--models-preset</c> file: app context/spec entries, curated
/// per-model options, and enrolled folder models. The critical rule is one
/// section per model — llama.cpp resets a duplicate section header, so a
/// second one silently drops the first's lines — plus canonical quant casing
/// and allowlist-only options.
/// </summary>
public sealed class RenderPresetFileTests
{
    private static string Render(
        IReadOnlyDictionary<string, int>? contexts = null,
        IReadOnlyDictionary<string, string>? specs = null,
        IReadOnlyDictionary<string, string>? extras = null,
        IReadOnlyList<EnrolledFolderModel>? enrolled = null,
        IReadOnlyList<LlamaManager.ServerModel>? serverModels = null)
        => LlamaManager.RenderPresetFile(
            (contexts ?? new Dictionary<string, int>()).ToList(),
            (specs ?? new Dictionary<string, string>()).ToList(),
            (extras ?? new Dictionary<string, string>()).ToList(),
            enrolled ?? [],
            serverModels);

    [Fact]
    public void NoInputs_RendersEmpty()
        => Assert.Equal("", Render());

    [Fact]
    public void WithoutExtrasAndEnrolled_IsByteIdenticalToTheOldRenderer()
    {
        var contexts = new Dictionary<string, int>
        {
            ["org/repo:Q4_0"] = 8192,
            ["org/other:Q8_0"] = 4096,
        };
        var specs = new Dictionary<string, string>
        {
            ["org/repo:Q4_0"] = "draft-mtp",
            ["org/spec-only:Q4_0"] = "draft-dflash",
        };

        Assert.Equal(
            LlamaManager.RenderModelsPresetIni(contexts, specs),
            Render(contexts, specs));
    }

    [Fact]
    public void ContextSpecAndExtras_MergeIntoOneSection()
    {
        var ini = Render(
            contexts: new Dictionary<string, int> { ["org/repo:Q4_0"] = 8192 },
            specs: new Dictionary<string, string> { ["org/repo:Q4_0"] = "draft-mtp" },
            extras: new Dictionary<string, string>
            {
                ["org/repo:Q4_0"] = "n-gpu-layers = 20\nflash-attn = on",
            });

        Assert.Equal(
            "[org/repo:Q4_0]\n" +
            "ctx-size = 8192\n" +
            "spec-type = draft-mtp\n" +
            "n-gpu-layers = 20\n" +
            "flash-attn = on\n" +
            "\n",
            ini);
    }

    [Fact]
    public void ExtrasOnlySections_SortDeterministically()
    {
        var ini = Render(extras: new Dictionary<string, string>
        {
            ["org/b:Q4_0"] = "threads = 8",
            ["org/a:Q4_0"] = "n-gpu-layers = 20",
        });

        Assert.Equal(
            "[org/a:Q4_0]\n" +
            "n-gpu-layers = 20\n" +
            "\n" +
            "[org/b:Q4_0]\n" +
            "threads = 8\n" +
            "\n",
            ini);
    }

    [Fact]
    public void InvalidExtras_AreNeverRendered()
    {
        var ini = Render(extras: new Dictionary<string, string>
        {
            ["org/bad:Q4_0"] = "n-gpu-layer = 20",
            ["org/reserved:Q4_0"] = "ctx-size = 4096",
            ["org/ok:Q4_0"] = "threads = 8",
        });

        Assert.Equal("[org/ok:Q4_0]\nthreads = 8\n\n", ini);
        Assert.DoesNotContain("n-gpu-layer", ini);
        Assert.DoesNotContain("ctx-size", ini);
    }

    [Fact]
    public void KeysAreCanonicalizedToTheRouterSpelling()
    {
        var ini = Render(extras: new Dictionary<string, string>
        {
            ["org/repo:q4_0"] = "threads = 8",
        });

        Assert.Equal("[org/repo:Q4_0]\nthreads = 8\n\n", ini);
    }

    [Fact]
    public void LightOnModel_SpecialFlagIsRendered()
    {
        // Some lightonai models need the router's --special flag; the curated
        // checker accepts the short and long spellings and the writer emits the
        // canonical INI key.
        var ini = Render(extras: new Dictionary<string, string>
        {
            ["lightonai/colgrep-agent-2B-GGUF:Q8_0"] =
                "special = true\nn-gpu-layers = 20",
        });

        Assert.Equal(
            "[lightonai/colgrep-agent-2B-GGUF:Q8_0]\n" +
            "special = true\n" +
            "n-gpu-layers = 20\n" +
            "\n",
            ini);
    }

    [Fact]
    public void ColgrepEnrolledModel_GetsTheSpecialDefault()
    {
        var ini = Render(
            enrolled:
            [
                new EnrolledFolderModel(
                    "colgrep-agent-2B-Q8_0", @"D:\colgrep-agent-2B-Q8_0.gguf", null, null),
            ]);

        Assert.Equal(
            "[colgrep-agent-2B-Q8_0]\n" +
            "model = D:\\colgrep-agent-2B-Q8_0.gguf\n" +
            "special = true\n" +
            "\n",
            ini);
    }

    [Fact]
    public void ColgrepCacheVariant_GetsTheSpecialDefault()
    {
        var ini = Render(
            serverModels:
            [
                new LlamaManager.ServerModel
                {
                    Id = "lightonai/colgrep-agent-2B-GGUF:Q4_K_M",
                    Source = "cache",
                },
            ]);

        Assert.Equal(
            "[lightonai/colgrep-agent-2B-GGUF:Q4_K_M]\n" +
            "special = true\n" +
            "\n",
            ini);
    }

    [Fact]
    public void ColgrepUserSpecialFalse_OverridesTheDefault()
    {
        var ini = Render(
            extras: new Dictionary<string, string>
            {
                ["lightonai/colgrep-agent-2B-GGUF:Q8_0"] = "special = false",
            },
            serverModels:
            [
                new LlamaManager.ServerModel
                {
                    Id = "lightonai/colgrep-agent-2B-GGUF:Q8_0",
                    Source = "cache",
                },
            ]);

        Assert.Equal(
            "[lightonai/colgrep-agent-2B-GGUF:Q8_0]\n" +
            "special = false\n" +
            "\n",
            ini);
    }

    [Fact]
    public void ColgrepUserOptions_MergeWithTheDefault()
    {
        var ini = Render(extras: new Dictionary<string, string>
        {
            ["lightonai/colgrep-agent-2B-GGUF:Q8_0"] = "n-gpu-layers = 20",
        });

        Assert.Equal(
            "[lightonai/colgrep-agent-2B-GGUF:Q8_0]\n" +
            "special = true\n" +
            "n-gpu-layers = 20\n" +
            "\n",
            ini);
    }

    [Fact]
    public void CaseVariantContexts_RenderOneSectionAndOneCtxLine()
    {
        // A hand-edited settings dictionary can contain both spellings; the
        // router canonicalizes the quant tag and treats them as one preset, so
        // a second ctx-size line would silently change the choice.
        var ini = LlamaManager.RenderPresetFile(
            [
                new KeyValuePair<string, int>("org/repo:q4_0", 4096),
                new KeyValuePair<string, int>("org/repo:Q4_0", 8192),
            ],
            [],
            [],
            []);

        Assert.Equal("[org/repo:Q4_0]\nctx-size = 4096\n\n", ini);
    }

    [Theory]
    [InlineData("*")]
    [InlineData("default")]
    [InlineData(" default ")]
    [InlineData("")]
    [InlineData("org/x]y:Q4_0")]
    public void UnsafeSectionIds_AreSkipped(string id)
    {
        var ini = Render(
            contexts: new Dictionary<string, int> { [id] = 4096 },
            extras: new Dictionary<string, string> { [id] = "threads = 8" });
        Assert.Equal("", ini);
    }

    // ---- enrolled models ----

    [Fact]
    public void EnrolledModelPlusAppOptions_IsOneSectionWithModelFirst()
    {
        var ini = Render(
            contexts: new Dictionary<string, int> { ["fam-model-Q4_0"] = 16384 },
            specs: new Dictionary<string, string> { ["fam-model-Q4_0"] = "draft-mtp" },
            extras: new Dictionary<string, string>
            {
                ["fam-model-Q4_0"] = "n-gpu-layers = 20",
            },
            enrolled:
            [
                new EnrolledFolderModel(
                    "fam-model-Q4_0", @"D:\models\fam-model-Q4_0.gguf", null, null),
            ]);

        Assert.Equal(
            "[fam-model-Q4_0]\n" +
            "model = D:\\models\\fam-model-Q4_0.gguf\n" +
            "ctx-size = 16384\n" +
            "spec-type = draft-mtp\n" +
            "n-gpu-layers = 20\n" +
            "\n",
            ini);
    }

    [Fact]
    public void EnrolledOnly_MatchesTheEnrolledRenderer()
    {
        var models = new[]
        {
            new EnrolledFolderModel("plain", @"E:\solo.gguf", null, null),
        };

        Assert.Equal(
            EnrolledFolderScanner.RenderSections(models),
            Render(enrolled: models));
    }

    [Fact]
    public void EnrolledSpecOnly_WithExtras_StaysOneSection()
    {
        var ini = Render(
            specs: new Dictionary<string, string> { ["fam"] = "draft-dflash" },
            extras: new Dictionary<string, string> { ["fam"] = "threads = 6" },
            enrolled: [new EnrolledFolderModel("fam", @"D:\fam.gguf", null, null)]);

        Assert.Equal(
            "[fam]\n" +
            "model = D:\\fam.gguf\n" +
            "spec-type = draft-dflash\n" +
            "threads = 6\n" +
            "\n",
            ini);
        Assert.Equal(1, ini.Split('[').Length - 1);
    }

    [Fact]
    public void CaseOnlyRename_MergesIntoTheEnrolledSection()
    {
        // The scanner and _enrolledModelPaths are case-insensitive; the
        // composer must be too, or a rename (foo -> Foo) leaves a ghost
        // section with the old options and no model path.
        var ini = Render(
            contexts: new Dictionary<string, int> { ["foo"] = 4096 },
            extras: new Dictionary<string, string> { ["foo"] = "threads = 8" },
            enrolled: [new EnrolledFolderModel("Foo", @"D:\Foo.gguf", null, null)]);

        Assert.Equal(
            "[Foo]\n" +
            "model = D:\\Foo.gguf\n" +
            "ctx-size = 4096\n" +
            "threads = 8\n" +
            "\n",
            ini);
    }

    [Fact]
    public void EnrolledCollisionWithServerModel_KeepsServerPathAndOnlyAppOptions()
    {
        var ini = Render(
            extras: new Dictionary<string, string> { ["repo:Q4_0"] = "threads = 8" },
            enrolled: [new EnrolledFolderModel("repo:Q4_0", @"D:\other.gguf", null, null)],
            serverModels: [new LlamaManager.ServerModel { Id = "repo:q4_0" }]);

        Assert.Equal("[repo:Q4_0]\nthreads = 8\n\n", ini);
        Assert.DoesNotContain("model =", ini);
    }

    [Fact]
    public void EnrolledModelReportedAsPreset_IsNotTreatedAsACollision()
    {
        // The router lists the preset's own sections as models with
        // source "preset"; those must not suppress the model = path on the
        // next reload (the app would then unload the router).
        var ini = Render(
            enrolled: [new EnrolledFolderModel("fam", @"D:\fam.gguf", null, null)],
            serverModels:
            [
                new LlamaManager.ServerModel { Id = "fam", Source = "preset" },
            ]);

        Assert.Equal("[fam]\nmodel = D:\\fam.gguf\n\n", ini);
    }

    [Fact]
    public void EnrolledPathWithCommentCharacter_IsSkipped()
    {
        var ini = Render(
            extras: new Dictionary<string, string> { ["fam"] = "threads = 8" },
            enrolled: [new EnrolledFolderModel("fam", @"D:\models\a#b.gguf", null, null)]);

        // The file path can't be rendered (the router would truncate it), but
        // the app options for that id still render as a normal section.
        Assert.Equal("[fam]\nthreads = 8\n\n", ini);
    }

    [Fact]
    public void EnrolledNameThatCannotHeadASection_IsSkipped()
    {
        var ini = Render(
            enrolled: [new EnrolledFolderModel("*", @"D:\all.gguf", null, null)]);
        Assert.Equal("", ini);
    }
}
