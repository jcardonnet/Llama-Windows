# Per-model preset options — implementation plan (rev. 2, post-review)

Status: reviewed and revised. Ready to implement.

Rev. 1 was reviewed by four independent adversarial reviewers (security,
architecture, UX, testing/build). This revision incorporates every accepted
finding and records the empirical checks that forced the redesign.

## 0. Review outcome (what changed)

| # | Finding (severity) | Resolution in this revision |
|---|---|---|
| 1 | **Blocker:** one unknown-key typo aborts the whole router; no `/models`, so the field is unreachable to fix it. Verified: `n-gpu-layer = 20` → exit 1. | Replace "any ident key" with a **typed allowlist**. Only canonical allowlisted keys are ever rendered; unknown keys are rejected at Save and skipped at render. |
| 2 | Alias/env spellings bypass a long-name denylist (`hft`, `HF_TOKEN`, `m`, `c`, `LLAMA_API_KEY`). | Allowlist + alias map. No environment or short-form spellings are accepted except the explicit aliases of allowed options. |
| 3 | `#`/`;` silently truncate INI values (verified). | Reject `#`/`;` in values; also validate enrolled model/sidecar paths (pre-existing latent bug). |
| 4 | Duplicate `[section]` headers and case-only ids collide (verified: `repo:q4_0` + `repo:Q4_0` collapse). | Canonical tag casing, case-insensitive section identity, one composer, one section per model, collision logging. |
| 5 | Enrolled spec-only entries render twice; ordinal vs case-insensitive mismatch. | Single `RenderPresetFile` composer; all lookups case-insensitive; tests for enrolled ctx/spec/extras and case renames. |
| 6 | Router-level flags override per-model values. Verified: base `--cache-type-k q8_0`/`--threads 8` beat preset `f16`/`6`. | Exclude `cache-type-k/v` from per-model options; document that global custom serve args win; warn on conflicts. |
| 7 | Saving calls `/models?reload=1`, which **unloads running/loading models whose preset changed**; the old doc claimed otherwise. | UI confirms unload for an active model; Save disabled while downloading/loading; corrected doc; no auto-reload. |
| 8 | `Settings.Save()` returns `bool` (does not throw); write/reload failures are swallowed. | Save checks the bool; `ReloadModelPresetsAsync` returns a result with HTTP status; UI reports saved / pending / failed; rollback on settings-save failure. |
| 9 | `ModelExtraArguments` (and other collections) can be explicit `null` in JSON → `lock(null)` crashes Save and the details view. | Normalize all collection properties in `Settings.Load`; add an internal `FromJson` seam and tests. |
| 10 | Saving under the dictionary lock inverts `Settings.Save`'s lock order (deadlock with the draft-head poller). | Mutate under lock, release, then Save; explicit rule + tests/comment. |
| 11 | Extras are not pruned on delete or migrated on id adoption. | `PruneModelSettings` removes extras; `AdoptServerId` migrates them; id captured per details session. |
| 12 | Per-keystroke validation punishes typing; Back/Esc silently discards edits; no success feedback. | Validate on Save; dirty guard with discard prompt; Save (not Apply); transient "Saved"/error feedback; a11y live region. |
| 13 | CRLF from a WinUI multiline TextBox would be rejected as control characters. | Normalize CRLF/CR→LF at parse entry; tests for all endings. |
| 14 | Layout: 150 DIP section pushes Delete below the fold; nested scroll. | Collapsed `Expander` with a compact header; 48–120 DIP text box; verify at 100%/150% scale. |
| 15 | Build/rollout: bare `dotnet` is SDK 9; app tests compile the WinUI project; installed app is 0.14.1. | Use `C:\Users\JC\.dotnet10\dotnet.exe`; drop redundant `dotnet build`; required smoke procedure and README/release note. |

Empirically verified against the local build (llama.cpp b12155):
- unknown key → router exits 1 before `/health`;
- `#`/`;` in a value → value truncated in `status.preset`;
- duplicate tag casing → one canonical section;
- base `--cache-type-k`/`--threads` → child args show the base value, not the preset's.

## 1. Objective and non-goals

**Objective.** Let the user set a curated set of llama.cpp options per model,
edit them in the model details view, persist them, render them into the
app-owned `--models-preset` INI, and apply them at the model's next load —
without any possibility that a hand-typed option bricks the server.

**Non-goals.**

- Arbitrary llama.cpp flags. Only the typed allowlist in §3 is supported;
  expanding it is a code change with tests.
- Per-model `cache-type-k`/`cache-type-v` (the app's global KV setting wins;
  verified) or `jinja` (app-owned).
- Per-model `ctx-size`/`spec-type` (the context picker and draft-head detector
  own them), model identity (`model`, `alias`, `hf*`, `mmproj`,
  `spec-draft-model`), bind/auth (`host`, `port`, `api-key*`, `ssl*`), router
  lifecycle (`models-*`, `sleep-idle-seconds`), or path/log options
  (`log-file`, `slot-save-path`, ...).
- Hub-only rows (the details view is unreachable for them).
- Immediate re-load of an active model (reload unloads it; the UI says so).

## 2. Data model

### `Settings`

```csharp
public Dictionary<string, string> ModelExtraArguments { get; set; } = new();
```

- Key: the details view's `ServerModelId` (`repo:quant` or enrolled name).
- Value: raw user text (one option per line), trimmed; empty = no options.
- `Save()` adds `lock (ModelExtraArguments)` to the existing chain, in
  declaration order (last). **Never call `Save()` while holding a collection
  lock**; every caller mutates under lock, releases, then saves.
- `Load()` normalizes all collection properties (`ModelContextLengths`,
  `ModelSpeculativeTypes`, `ModelFolders`, `ModelExtraArguments`) with `??= new()`.
- Add `internal static Settings FromJson(string json)` (used by `Load` and
  tests) so explicit-null/missing-property behavior is testable without the
  user's settings file.
- Source-generated JSON picks the property up automatically.

### `LlamaManager`

```csharp
public IReadOnlyDictionary<string, string>? ModelExtraArguments { get; set; }
```

Assigned by `App.OnLaunched` from `Settings.Current.ModelExtraArguments`
(shared by reference, like the other live dictionaries).

## 3. Typed allowlist and parser (`LlamaApp.Common/ModelPresetOptions.cs`)

### Supported options

| Canonical key | Aliases | Value |
|---|---|---|
| `n-gpu-layers` | `ngl`, `gpu-layers` | int 0–999, `auto`, `all` |
| `threads` | `t` | int 1–256 |
| `threads-batch` | `tb` | int 1–256 |
| `flash-attn` | `fa` | `on` / `off` / `auto` |
| `special` | `sp` | `true` / `false` (bare flag = true); keeps special/control tokens in output — some lightonai models require it |
| `batch-size` | `b` | int 1–8192 |
| `ubatch-size` | `ub` | int 1–8192 |
| `n-cpu-moe` | `ncmoe` | int 0–999 |
| `cache-reuse` | — | int 0–65536 |
| `fit` | — | `on` / `off` |
| `fit-target` | `fitt` | int 0–1048576 |
| `fit-ctx` | `fitc` | int 0–1048576 |
| `load-mode` | `lm` | `auto` / `none` / `mmap` / `mlock` / `mmap+mlock` / `dio` |
| `parallel` | `np` | int 1–256 |

Not supported, with specific errors: `cache-type-k/v` ("set globally in
Settings → Llama"), `ctx-size`/`c` ("use the Context length picker"),
`spec-type` ("detected automatically"), `model`/`m`, `alias`/`a`, `hf*`/`hft`,
`host`, `port`, `api-key*`, `ssl*`, `models-*`, `sleep-idle-seconds`.

`special` is deliberately per-model rather than a global default: it keeps the
model's special/control tokens in the generated text (some lightonai models
need this to parse their output), and enabling it for every model can leak
control tokens into ordinary chat. The checker accepts `special = true` or the
bare `--special`/`-sp`, and `special = false` disables it (verified against the
router: the child gets `--special` only when true).

**Built-in family default:** any model id containing `colgrep-agent`
(case-insensitive — HF cache ids like
`lightonai/colgrep-agent-2B-GGUF:Q4_K_M`/`Q8_0` and enrolled names like
`colgrep-agent-2B-Q8_0`) gets `special = true` at render time via
`ModelPresetDefaults`, so every variant works without a manual entry. The
details view shows the default as a hint; an explicit `special = false`
overrides it. This is the one family-specific rule; it is narrow on purpose and
keyed by the model id substring, not by vendor.

### Grammar

- Input is normalized: CRLF/CR → LF; leading/trailing whitespace trimmed; blank
  and comment-only lines (`#…`, `;…`) ignored.
- **One option per line**, in either form:
  - `key = value` (canonical key or alias; optional leading `--`/`-` on the key);
  - `--key value`, `--key=value`, `-alias value`.
- Key matching: case-insensitive, `_` normalized to `-`, dashes stripped, then
  resolved through the alias table to the canonical key (rendered lowercase).
- Value: trimmed; non-empty; no whitespace inside (all supported values are
  ints/enums); no `#`, `;`, or control characters; length ≤ 64; parsed per the
  option's type/range/enum.
- Duplicate canonical key → error.
- Bounds: ≤ 32 lines, ≤ 2048 characters total, ≤ 512 characters per line.
- Errors are structured and line-addressed:
  `ModelPresetError(int Line, string Key, string Message)`; the first error is
  shown in the UI; Save is blocked while any error exists.
- Unknown keys get "did you mean" suggestions (ordinal distance over canonical
  keys/aliases).
- The parser never echoes values in error messages.

### API

```csharp
internal static bool TryParse(
    string? text,
    out IReadOnlyList<ModelPresetOption> options,
    out IReadOnlyList<ModelPresetError> errors);

internal static string ToIniLine(ModelPresetOption option); // "key = value"
```

(Pure, static, no I/O; unit-tested in `LlamaApp.LlamaCpp.Tests`.)

## 4. INI composition

### Shared syntax helpers (`LlamaApp.Common/PresetIniSyntax.cs`)

- `IsSafeSectionId`: non-empty, ≤ 512, no leading/trailing whitespace, no
  `[`/`]`, no control chars, not `*`, not `default` (case-insensitive).
- `IsSafeValue`: no control chars, no `#`/`;`, length 1–1024; used for all
  rendered values, including enrolled model/mmproj/draft paths.
- `CanonicalSectionId`: uppercase the segment after the last `:` (quant tag);
  leave enrolled names otherwise. Section identity compares
  `OrdinalIgnoreCase` (fixes case-only renames and the existing enrolled-vs-
  settings mismatch); collisions are logged.

### `LlamaManager.RenderPresetFile` (new, pure)

```csharp
internal static string RenderPresetFile(
    IReadOnlyList<KeyValuePair<string, int>> contexts,
    IReadOnlyList<KeyValuePair<string, string>> specs,
    IReadOnlyList<KeyValuePair<string, string>> extras,
    IReadOnlyList<EnrolledFolderModel> enrolled,
    IReadOnlyList<LlamaManager.ServerModel>? serverModels = null);
```

- Parse extras via `ModelPresetOptions`; drop entries with errors (log id +
  first error; never render an unknown key).
- Build one section per canonical id (case-insensitive):
  - app lines: `ctx-size`, `spec-type`, then extra `key = value` lines;
  - enrolled lines: `model`, `mmproj`, `spec-draft-model`, then the same app
    lines.
- Enrolled section whose name collides with a server-reported model id
  (`serverModels`, case-insensitive): skip the enrolled `model` path, log a
  warning; still render app lines for the id. (Collision with models-dir stems
  the server doesn't report is undetectable; documented.)
- Enrolled path values that fail `IsSafeValue` (`#`, `;`, control chars): skip
  the whole enrolled model and log. This fixes a pre-existing truncation bug.
- Order: contexts (existing order) → spec-only (existing) → extras-only
  (ordinal sort) → enrolled (scan order). Exactly one `[id]` per model.
- With `extras = []` and no enrolled context/spec overlap, output is
  byte-identical to `RenderModelsPresetIni(contexts, specs)`; golden test.
- `RenderModelsPresetIni` keeps its signature/behavior for compatibility;
  `RenderPresetFile` is the writer's only composer.
- `EnrolledFolderScanner.RenderSections(models, perModelLines = null)` gains the
  optional lines parameter (null = current output) and validates path values.

### Writer

`WriteContextPresetsIniAsync`:

- Snapshot contexts/specs/extras/folders under their locks; release.
- Scan enrolled folders (existing), set `_enrolledModelPaths` (existing).
- Filter `contextItems`, `specItems`, `extraItems` with the existing ghost
  guard (`isBusy || exists`); extras for missing models stay in settings but
  are not rendered.
- `File.WriteAllText(ContextPresetPath, RenderPresetFile(...))`.
- Log counts only (models with extras, invalid entries); never values.
- Return the path or null on failure.

### Reload result — and the truth about reload

```csharp
public async Task<bool> ReloadModelPresetsAsync(CancellationToken cancel = default)
```

- Return `false` if the INI write failed.
- If the server is not running: return `true` (applies at next start).
- `GET /models?reload=1`; **check `IsSuccessStatusCode`**; log status at
  Warning on failure; return false on non-2xx.
- Correct the XML doc: reloading re-reads presets and the router **unloads
  running/loading/sleeping models whose preset changed**. It is not
  side-effect-free. Saving options for an active model will unload it.
- Adopted servers may not have `--models-preset`; the reload can succeed but
  ignore the file. The host reports "saved; restart the server to apply" when
  the manager did not launch it / origin is external.

## 5. Host and app wiring

### `IModelItemDetailsHost` additions

```csharp
string GetModelExtraArguments(string serverModelId);
Task<ModelExtraArgumentsSaveResult> SaveModelExtraArgumentsAsync(
    string serverModelId, string arguments);
```

`ModelExtraArgumentsSaveResult(bool Saved, bool Reloaded, string? Warning, string? Error)`.

### `MainWindow` implementation

- `Get`: under lock; return `""` if absent; guard a null dictionary.
- `Save`:
  1. Parse; invalid → `Saved:false, Error:<first error>`.
  2. Snapshot old value; mutate under lock; release.
  3. `Settings.Current.Save()`; on false restore the old value under lock and
     return `Error` ("Could not save settings").
  4. `await LlamaManager.Shared.ReloadModelPresetsAsync()`; if false, return
     `Saved:true, Reloaded:false, Warning:"Saved. The running server did not
     reload presets; restart it or it applies at next start."`
  5. Log id + set/cleared only; never the text.
- `PruneModelSettings`: add extras removal (`removed |= extras.Remove(id)`).
- `AdoptServerId`: migrate extras from old ids to the new id (merge; keep the
  new entry if both), under lock, log a warning on merge conflicts.
- Global-precedence warning: compare the parsed per-model canonical keys with
  the canonical keys in `Settings.Current.CustomServeArguments` (tokenize with
  `ArgumentTokenizer`, map aliases); return a Warning listing keys whose global
  value wins. `cache-type-*` is not in the allowlist and gets a dedicated error.

## 6. UI

### Visibility and state

- Section visible for the details view's installed/local rows; **disabled**
  while `Model.IsDownloading || Model.IsLoading || Model.DownloadPaused ||
  Model.DownloadFailed` with a one-line reason ("Available once the download
  finishes").
- Capture `ServerModelId` once per details VM; if it changes while open, rebase
  the field to the new id's saved text (or close/disable with a notice).
- `ExtraArgumentsLoadedHint` is state-aware: notify on `Model.IsLoaded` changes
  in `RefreshActionState`.

### Layout (compact, below the fold-safe)

- Collapsed `Expander` before the action rows:
  - header: "Extra preset options" + (when saved) a small "configured" dot;
  - content: TextBox (48–120 DIP, accepts returns, wrap, monospace
    `Cascadia Mono`/`Consolas`, `IsSpellCheckEnabled="False"`,
    `UpdateSourceTrigger=PropertyChanged`), visible helper, Save button,
    error text.
- Visible helper (not tooltip-only):
  "One option per line: `n-gpu-layers = 20` or `--n-gpu-layers 20`. Prefixes and
  aliases are normalized. Applies when this model next loads."
- A focusable info `Button` with a `Flyout` lists the supported options and
  explains that global Custom Serve Arguments win over per-model values.
- Error text uses a theme critical brush (not hard-coded `#F85149`), wraps, and
  is `AutomationProperties.LiveSetting="Assertive"` only after a failed Save.
- Save button: text "Save" (not Apply), `AutomationProperties.Name="Save extra
  preset options for this model"`; disabled while saving or when not dirty.
- The TextBox gets `AutomationProperties.Name`/`HelpText`; plain text, no
  markdown backticks.

### Interaction

- **Validate on Save**, not per keystroke. On text change: clear the previous
  error and update dirty state only.
- On Save:
  - invalid → show the first error, expand, focus the box;
  - valid, model active (loaded/loading/sleeping) → confirm flyout:
    "Save and unload this model? It will reload with the new options on its
    next request." Save & unload / Cancel;
  - valid, model idle → save.
- Result feedback:
  - success: transient check + "Saved" (reuse the copied-flash idiom); if the
    model was unloaded: "Saved; model unloaded — it uses the new options on its
    next load"; if not reloaded: warning text;
  - failure: red error, no baseline advance, no dirty clear;
  - if the model is loaded and the user cancels: keep editing.
- Dirty guard: Back, Esc, and the breadcrumb call `TryHideDetails()`, which
  shows a Flyout "Discard unsaved changes?" Keep editing / Discard. Light
  dismiss = keep editing. Other internal closes (delete/download) are
  unguarded and documented.
- No auto-reload of the model; the user reloads it from the list.

### Accessibility

- Real button, focusable info button, labelled TextBox, live error after save,
  keyboard order Back → context → expander → Save → actions.
- Error/help copy in plain English (app is English-only).

## 7. Tests

1. **Parser** (`LlamaApp.LlamaCpp.Tests/ModelPresetOptionsTests.cs`):
   - both forms, aliases, leading dashes, case/underscore normalization;
   - CRLF/CR/LF and mixed endings;
   - blank/comment lines;
   - unknown key + suggestion, reserved keys with specific messages;
   - `#`/`;`, control chars, values with spaces, empty values;
   - int ranges, enums (`n-gpu-layers auto/all`, load modes);
   - duplicates, 32/2048/64 bounds, line numbers;
   - no secret values in errors.
2. **Renderer**:
   - golden: `RenderPresetFile(contexts, specs, [], [])` equals
     `RenderModelsPresetIni(contexts, specs)`;
   - extras-only, merged ctx/spec/extras, invalid skipped;
   - extras-only ordinal order;
   - enrolled: ctx+spec+extras in one section; spec-only enrolled; extras-only
     enrolled; case-only rename; collision with a server-reported id;
   - `*`/`default`/edge-whitespace ids rejected; tag casing canonical;
   - enrolled path with `#`/`;` skipped + logged.
3. **Enrolled renderer**: per-model lines; null unchanged; unsafe path skipped.
4. **Settings**: missing property, explicit `null` (via `FromJson`), round-trip
   through `SettingsJsonContext`, default empty; no file I/O.
5. **ViewModel** (`FakeHost` gains both members):
   - loads saved text; invalid saved text shows an error when expanded;
   - Save success sets baseline to trimmed text, clears dirty, shows feedback;
   - invalid Save calls no host and shows the line error;
   - host failure leaves dirty and shows an error, no baseline advance;
   - active-model confirm path; cancel keeps editing;
   - visibility/enabled state across `IsDownloading`/`IsLoading`/`IsLoaded`
     changes (notify in `RefreshActionState`);
   - busy save disables Save; dirty guard flag.
6. **Host logic**: extract a testable `SaveModelExtraArguments` core (set/clear/
   rollback) or test through a fake settings writer; verify rollback on save
   failure and no lock held across Save.
7. **Existing suites**: `ModelsPresetSpecTests`, `EnrolledFolderScannerTests`,
   `GhostPresetFilterTests`, settings tests remain green; only `FakeHost`
   needs the new members (confirmed by review: the only implementers are
   `MainWindow` and the fake).
8. **Build**:
   - `& 'C:\Users\JC\.dotnet10\dotnet.exe' test LlamaApp.LlamaCpp.Tests -c Release -p:RestoreLockedMode=true`
   - `& 'C:\Users\JC\.dotnet10\dotnet.exe' test LlamaApp.Tests -c Release -p:RestoreLockedMode=true`
     (x64; this also compiles the WinUI/XAML project — no separate build).
   - Baseline: 844/845; the sole failure is the sandbox network-compartment
     artifact in `ServerLaunchTests.OccupiedLoopbackPort...`.

## 8. Rollout and documentation

- Update the README's model-details bullet: per-model extra preset options.
- Release note: curated set; global options win; saving an active model
  unloads it; changes apply on next load.
- Smoke procedure (required): close the installed app first; ensure no llama
  server on the configured port; run the built/dev app with a distinct port or
  confirm no server adoption; set two options on an unloaded model; inspect
  `%USERPROFILE%\.llama\model-context-presets.ini`; load the model and inspect
  the child argv (`Get-CimInstance Win32_Process` → `n-gpu-layers`); verify a
  loaded model is unloaded on Save; clear the field and confirm the section is
  gone; restart and confirm persistence; feed an invalid key and confirm no
  INI change.
- Installed package is 0.14.1, source is 0.15.0. The feature ships with the
  next built/published package; do not run a dev build side-by-side with the
  MSIX (single-instance key `Llama`, shared preset INI, different settings
  paths).

## 9. Residual risks (accepted / documented)

- A future llama.cpp may rename/remove an allowlisted key; the allowlist is
  validated against the bundled runtime at release. The reload result and the
  startup failure are surfaced. A runtime help-probe/self-heal can be added
  later if this becomes real; it is not required for the curated stable set.
- Enrolled folder names are derived from file stems; a rename changes the
  section id and leaves the old settings entry unrendered (ghost guard). A
  case-only rename is handled case-insensitively.
- Collisions with models-dir stems whose model the server does not report are
  undetectable; logged where detectable.
- Global custom serve arguments can override any per-model key; the UI warns.
  `cache-type-k/v` are intentionally not per-model.
