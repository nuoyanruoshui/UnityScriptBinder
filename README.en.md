# UnityScriptBinder (com.unityscriptbinder.nuoyan)

> Version: 1.1.5 | Unity: 2021.3+ (verified on 2021.3.44) | License: MIT

English | [中文](./README.md)

UnityScriptBinder is a **script binding (automatic UI mounting) tool**: it generates a `partial MonoBehaviour` **named after the GameObject** for a UI root with one click, declares UI fields and mounts the components. Two assignment styles, three modes:

- **Reference**: fields are declared with `[SerializeField]` and the tool fills in the child component references **inside the editor**, saved with the scene / prefab — zero lookup cost at runtime;
- **Runtime**: fields are not serialized; a `BindComponents()` method is generated that looks components up by node path (`transform.Find(...)`) at runtime;
- **Both**: editor fill wins, and `BindComponents()` only falls back at runtime for fields that are still null.

- Namespace: `NuoYan.ScriptBinder`
- Assembly definition: `NuoYan.ScriptBinder.asmdef` (Editor only)
- Entry point: select **any GameObject** (not limited to UI — plain 3D objects and empty nodes work too), then press **Bind Script** in the Inspector

The plugin UI, console logs and generated code comments are available in **English / 中文 / 日本語** — see [Localization](#localization-en--ja--zh).

---

## Features

- **One-click binding (4-step pipeline)**: select a node → press `Bind Script` in the Inspector → the tool runs **1) generate code → 2) wait for compile → 3) mount component → 4) fill references** with per-step logging. A failure keeps the task and tells you what to fix, then continues automatically (step-by-step menu items are also provided).
- **Three bind modes**: Reference / Runtime / Both. Switchable in the dialog and remembered; the project-wide default lives in the `BindRules` asset (see "Bind modes").
- **Per-run output overrides**: the dialog lets you change the **namespace**, the **output folder** and the **file layout** (all files flat in one folder / a same-named subfolder per class). Like the custom base class, these are remembered for the next run; leaving them empty falls back to the `BindRules` asset defaults.
- **Preview before generating**: each selected target is a foldout (first one expanded by default) listing every field that will be bound (field name + visibility + type) — what you see is what gets generated.
- **partial + Logic split**: the field declaration file (`xxx.cs`) is regenerated on every bind and may be overwritten; the empty logic file (`xxx.Logic.cs`) is created only on first generation and is then **never touched by the plugin again** — neither overwritten nor deleted, so your own code is always safe.
- **Naming is the rule**: the prefix of a child's name decides its visibility and field type — no dragging references by hand.
- **Editor-side assignment**: in Reference / Both mode the references are written at edit time through `SerializedObject` and saved with the scene / prefab, so they are visible in the Inspector and can be dragged into event slots such as `Button.onClick`.
- **Compile timing handled for you**: tasks and the current step are persisted in `EditorPrefs` (surviving domain reloads and editor restarts), and mounting only happens after **compilation is confirmed finished and the domain reloaded** — so you never get a new field silently left unassigned because an old type was mounted too early. Tasks pause while in play mode and resume when you exit it.

---

## Installation

Option 1 — as a **UPM git package** (Package Manager → `+` → Add package from git URL):

```
https://github.com/nuoyanruoshui/UnityScriptBinder.git
```

Option 2 — drop the whole `Assets/Plugins/UnityScriptBinder` folder into your project.

### Dependencies

- UGUI (`UnityEngine.UI`)
- TextMeshPro (`TMPro`, used by the `tmp` prefix)
- Optional: **Odin Inspector** (macro `ODIN_INSPECTOR`) — when present: `SavePath` gets a folder picker, and `Rules.Type` gets a dropdown of candidates (source: `BindTypes` below). Note that with Odin installed the asset's field tooltips are drawn by Odin and stay in the fixed text from the attributes (see "Localization").

---

## Getting started

### 1. Name your children

Every child's name is made of two prefixes: **visibility prefix + type prefix**. The tool walks **all descendants recursively**.

- Visibility prefix (decides the field's access level):

| Prefix | Access level |
|--------|--------------|
| `m_` | `private` (recommended, together with `[SerializeField]`) |
| `M_` | `protected` |
| `_`  | `public` |

- Type prefix (decides the field type; built-in rules):

| Prefix | Field type | Notes |
|--------|------------|-------|
| `txt` | `Text` | uGUI text |
| `tmp` | `TMP_Text` | TMP text (matches `TextMeshProUGUI` / `TextMeshPro`) |
| `img` | `Image` | |
| `btn` | `Button` | |
| `tgl` | `Toggle` | |
| `sld` | `Slider` | |
| `sbr` | `Scrollbar` | |
| `drp` | `Dropdown` | |
| `ipt` | `InputField` | |
| `rect` | `RectTransform` | |
| `trans`| `Transform` | |
| `go`  | `GameObject` | binds the whole child object |

> Example: a child named `m_img` (`m_` → `private`, `img` matches a built-in rule → `UnityEngine.UI.Image`) generates:
> ```csharp
> [SerializeField] private UnityEngine.UI.Image m_Img = null;
> ```

**Type prefixes are freely extensible (no need to touch the package code)**: `BindRule.Type` is a plain string, so you can put any C# component type expression in it — built-in rules use fully qualified names (such as `UnityEngine.UI.Button`), custom components just use their full name (such as `MyGame.MyComponent`), and the generated code emits it verbatim. Simple names (such as `Button` or `TMP_Text`) also work: the tool resolves them to a real `Type` and adds the `using` for the resolved namespace. This is what makes the rules work when the package is installed via UPM, where an enum could not be extended: just add / edit / remove rule rows in the project's own `BindRules` asset (type matching is driven entirely by the string rules on the asset).

**Type catalogue (`BindTypes`)**: a list of types on the `BindRules` asset with 12 common UI types built in. When Odin is installed, editing `Rules.Type` shows a dropdown whose candidates come from this list — append a custom component's full name and it becomes selectable (`Type` itself can still be typed by hand and does not depend on the catalogue).

**Field names are pascal-cased automatically**: the visibility prefix (`m_` / `M_` / `_`) is kept and the rest is split into words (on `_` and on lower-to-upper case transitions) with each word capitalized. Examples: `m_imgIcon` → `m_ImgIcon`, `m_img_icon` → `m_ImgIcon`, `m_btnClose` → `m_BtnClose`. The GameObject name is untouched; only the generated field identifier is affected.

All prefix rules can be changed (see "Rule configuration" below). A bound field whose type prefix matches nothing is treated as `GameObject`; an object whose visibility prefix matches nothing generates no field.

### 2. Run the binding

1. In scene or prefab edit mode, select the **root node** you want to bind (any GameObject; for UI this is usually a panel root with a `RectTransform`).
2. Press **Bind Script** in the Inspector.
3. The generation window opens:
   - it warns that a script with the same name will be overwritten;
   - **Namespace**: the namespace written for this run (defaults to `BindRules.Namespace`; leave it empty to fall back to that default). An invalid identifier is flagged live and blocks "Generate";
   - **Output folder**: the folder written to for this run, relative to `Assets` (defaults to `BindRules.SavePath`; empty falls back to that default). The `...` button picks a folder — only paths **inside Assets** are accepted;
   - **Bind mode** dropdown: Reference / Runtime / Both (defaults to `BindRules.DefaultMode`, the choice is remembered);
   - **Group fields** toggle: arranges fields **of the same type together**, each group preceded by `[Header("short type name")]` (see "Field grouping"). It has no asset-level default; the choice is remembered, and the step menu items (`一键 生成→编译→挂载→绑定` / `步骤 1`) reuse that remembered choice so both entry points produce the same layout;
   - **File layout** dropdown: `FileByFile` puts every file directly in the output folder / `FolderByFolder` creates a same-named subfolder per class (defaults to `BindRules.SaveFileMode`, the choice is remembered). The resulting path (`Assets/.../ClassName.cs`) is shown live underneath;
   - **Custom base class** field: empty means no custom base class (inherits `MonoBehaviour`). A **simple class name** is enough (such as `MyPanelBase`): the tool resolves the type, uses the simple name when the base class lives in the **same namespace** as the generated file, and **adds `using <base namespace>;` to the file header when it does not**; a fully qualified name (such as `GameLogic.MyPanelBase`) also works. The base class itself must derive from `MonoBehaviour` to be mountable. The input is validated live: a missing class or a non-`MonoBehaviour` class shows an error and blocks "Generate";
   - **Target preview**: each selected target is a foldout (first one expanded by default) listing every field that will be bound, so you can confirm the naming rules before generating.
4. Press **Generate**. The 4-step pipeline runs — generate code → wait for Unity to compile → **once compilation finished** mount the component on that node → fill in all field references. The Console logs `[1/4 generate] / [2/4 compile] / [3/4 mount] / [4/4 bind]`; if a step fails (compile error, target scene not open, ...) the task is kept, you get a hint, and it continues automatically once you fix it.

> The generated script is named `ClassName = GameObject name` and is written to the dialog's **Output folder** (default `BindRules.SavePath` = `Scripts/UI`), inside the **namespace** specified there (default `BindRules.Namespace` = `GameLogic`). Two file layouts:
> ```
> FileByFile      Assets/Scripts/UI/HeroPanel.cs            Assets/Scripts/UI/HeroPanel.Logic.cs
> FolderByFolder  Assets/Scripts/UI/HeroPanel/HeroPanel.cs  Assets/Scripts/UI/HeroPanel/HeroPanel.Logic.cs
> ```
> **The three dialog settings (namespace / output folder / file layout) only apply to the run you started by pressing the button**; the step-by-step menu items and plain `StartBind` calls still use the corresponding fields on the `BindRules` asset.

**Step-by-step menu** (`Tools/NuoYan/ScriptBinder/`, sharing the same implementation as the automatic pipeline, for recovering from a failure). The menu item names are **fixed Chinese** (see the note below), so they are quoted verbatim here with an English gloss:

| Menu item (as it appears) | What it does |
|---------------------------|--------------|
| `一键 生成→编译→挂载→绑定（选中）`<br>*(All-in-one: generate -> compile -> mount -> bind)* | the full 4 steps (base class `MonoBehaviour`) |
| `步骤 1：仅生成代码（选中）`<br>*(Step 1: generate code only)* | only writes `.cs` / `.Logic.cs` |
| `步骤 3：挂载组件（选中，需已编译）`<br>*(Step 3: mount component, needs compiled code)* | only mounts the same-named component on the selected nodes |
| `步骤 4：重新填充引用（选中）`<br>*(Step 4: refill references)* | only refills the field references (without regenerating) |
| `查看待处理任务` / `清除待处理任务`<br>*(Show / Clear pending tasks)* | inspect / clear the queued bind tasks |
| `校验多语言文案（自检）`<br>*(Validate localization tables)* | checks that the three language dictionaries have the same keys and `{0}` placeholders (see "Localization") |

> Menu item names cannot be localized: a `[MenuItem("...")]` path is a C# attribute argument and must be a compile-time constant, and Unity 2021.3 has no public API for registering Tools menu items at runtime. Everything the menu **logs** follows the selected language, and the messages that point you at a menu quote these same Chinese names.

### Bind modes (Reference / Runtime / Both)

Developers differ: some prefer `[SerializeField]` references dragged or filled in the editor, others prefer runtime lookup in code (such as `transform.Find("xxx").GetComponent<Image>()`). The **Bind mode** dropdown in the dialog offers three:

| Mode | Generated code | Editor fill |
|------|----------------|-------------|
| Reference | `[SerializeField] private X m_Xxx = null;` | automatic (zero runtime lookup cost) |
| Runtime | `private X m_Xxx;` plus `public void BindComponents()` (looks up and assigns every field by node path: `transform.Find("A/B")?.GetComponent<T>()`) | skipped (fields are not serialized and do not show in the Inspector) |
| Both | `[SerializeField] private X m_Xxx = null;` plus `BindComponents()` (each line is `if (m_Xxx == null) { ... }` — references already filled in the editor win, lookup is only a fallback) | automatic + runtime fallback |

- The project-wide default lives on the `BindRules` asset (`DefaultMode`); the dialog's choice is remembered (`EditorPrefs`) and reused next time.
- `BindComponents()` must be called once from your lifecycle (`Awake` / `OnEnable` / `OnInit(userData)`). The tool **does not generate an `Awake`** so it never conflicts with your own lifecycle.
- The runtime lookup path is the field child's hierarchy path relative to the root, produced by the same recursive collection rules as the editor fill, so fields and paths correspond one-to-one.

What a Runtime / Both file looks like (`HeroPanel`):

```csharp
// HeroPanel.cs — generated by the tool (Runtime mode)
using UnityEngine;
using TMPro;

namespace GameLogic
{
    public partial class HeroPanel : MonoBehaviour
    {
        private TMP_Text m_TmpName;          // not serialized, not shown in the Inspector
        private Button m_BtnClose;

        /// <summary>
        /// Runtime binding: fields are not serialized; components are looked up by node path and assigned at runtime.
        /// Call it once from the lifecycle of your logic file (.Logic.cs): Awake / OnEnable / OnInit(userData).
        /// </summary>
        public void BindComponents()
        {
            m_TmpName = transform.Find("m_tmpName")?.GetComponent<TMP_Text>();
            m_BtnClose = transform.Find("m_btnClose")?.GetComponent<Button>();
        }
    }
}
```

```csharp
// HeroPanel.Logic.cs — your logic file
namespace GameLogic
{
    public partial class HeroPanel
    {
        private void Awake()
        {
            BindComponents(); // or inside OnInit/OnEnable/whatever fits your lifecycle
            // m_TmpName / m_BtnClose are available from here on
        }
    }
}
```

> In Both mode each line looks like `if (m_BtnClose == null) { m_BtnClose = ...; }` — references already filled in the editor stay untouched, only missing ones are looked up at runtime.

### 3. Write your logic

Two files are generated (for a node named `HeroPanel`, in `Assets/Scripts/UI/`):

```csharp
// HeroPanel.cs — generated by the tool, do not edit directly
/// <summary>
/// Auto generated code for HeroPanel by ScriptBinder
/// ...
/// </summary>
using UnityEngine;
using UnityEngine.UI;

namespace GameLogic
{
    public partial class HeroPanel : MonoBehaviour
    {
        [SerializeField] private Image m_ImgIcon = null;
        [SerializeField] private Button m_BtnClose = null;
        [SerializeField] private TMP_Text m_TmpName = null;
    }
}
```

```csharp
// HeroPanel.Logic.cs — your logic file, created once and never overwritten by re-binding
namespace GameLogic
{
    public partial class HeroPanel
    {
        // access m_ImgIcon / m_BtnClose / m_TmpName here ...
    }
}
```

Write your own methods in `.Logic.cs`; `partial` merges them with the generated field declarations.

> The example above is the default **Reference** mode; the full shape of Runtime / Both files (including the `BindComponents()` example) is in "Bind modes" above.

---

## Rule configuration

The rules live in the **ScriptableObject** `BindRules.asset`.

- It is **created automatically** at `Assets/Resources/ScriptBinder/BindRules.asset` the first time you run a binding.

Settings:

| Field | Description |
|-------|-------------|
| `Namespace` | namespace of the generated code (default `GameLogic`); can be overridden per run in the dialog and remembered |
| `SavePath` | output folder, relative to `Assets/` (default `Scripts/UI`; shows a folder picker with Odin); can be overridden per run in the dialog and remembered |
| `DisplayLanguage` | language of the plugin UI / console logs / generated code comments: `English` (default) / `Chinese` / `Japanese` (see "Localization") |
| `DefaultMode` | default bind mode: `Reference` / `Runtime` / `Both` (switchable per run in the dialog and remembered) |
| `SaveFileMode` | default file layout: `FileByFile` keeps every file flat in `SavePath` / `FolderByFolder` creates a same-named subfolder per class (switchable per run in the dialog and remembered) |
| `BindTypes` | type catalogue: the dropdown candidates when editing `Rules.Type` (**needs Odin**). 12 common types built in; append a custom component's full name to pick it from the dropdown (`Type` can still be typed by hand and does not depend on this list) |
| `FieldVisibleRules` | visibility prefix table: `Prefix` + `Visible(Private/Protected/Public)` |
| `Rules` | type prefix table: `Prefix` + `Type` (any C# component type expression; fully qualified names such as `UnityEngine.UI.Button` are recommended, simple names get the `using` added automatically; add or remove rows freely to support custom components without touching the package code) |

Changes take effect for subsequent generations immediately; **references are written at edit time, so after changing the rules press Bind Script once more to re-mount and refresh the references**.

Dialog preferences are stored in `EditorPrefs` (per project) and reused next time: **namespace / output folder / custom base class** are saved when you press "Generate" (cancelling does not change what was remembered), while the **bind mode / file layout** dropdowns are remembered the moment you change them.

---

## Localization (en / ja / zh)

The plugin UI, console logs and comments in the generated code all support **English / 中文 / 日本語**. Switch with the **`DisplayLanguage`** field on the `BindRules` asset (with Odin it is an `[EnumToggleButtons]` triple button; without Odin it is a **Language** dropdown whose options are shown in the current language). The change takes effect immediately, no restart needed.

**The default is `English`** — when upgrading from an older version this field is unset in the asset, so the UI starts out in English until you pick a language.

Coverage:

| Location | Follows the language |
|----------|----------------------|
| Dialog field labels / buttons / hints, and the Inspector's `Bind Script` button | yes |
| All confirmation dialogs and the progress bar text | yes |
| Console logs and warnings (the `[1/4 generate]` step logs, failure and warning messages) | yes |
| Tooltips of the `BindRules` asset fields | yes (**only without Odin**, see the limitations below) |
| Comments in the generated code (`BindComponents` summary, `.Logic.cs` note, "do not edit directly") | yes (**as of generation time** — re-bind to refresh after switching language) |
| `Tools/NuoYan/ScriptBinder/*` menu item names | no, see the limitations below |
| The generated **code** itself (class name, field names, `[Header]`, `[SerializeField]`, the `Auto generated ... by ScriptBinder` / `Time` / `Machine` header lines) | no, fixed English |

**Self check**: `Tools/NuoYan/ScriptBinder/Validate localization tables (self check)` verifies that the three languages have the **same key set** and **aligned `{0}` placeholders**, printing every problem to the Console. Run it after adding or editing strings — a missing translation does not throw, it silently falls back (see below).

**Adding a language / a string**: edit the three `Dictionary<string,string>` in `Editor/LocalizationConstant.cs` (key naming conventions are documented at the top of that file: `Mode.*` / `Field.*` / `Dialog.*` / `Log.*` / `Gen.*` and so on). Look strings up with `LocalizationConstant.Get("Key")`, or with `LocalizationConstant.Format("Key", args...)` when `string.Format` is needed. **The fallback order for a missing key is: the caller's fallback → the key name itself**, so a missing translation never crashes — the UI just shows the raw key, which is easy to spot.

**Two things cannot be localized (hard Unity limitations)**:

- **Menu item names**: a `[MenuItem("...")]` path is a C# attribute argument and must be a **compile-time constant**, and Unity 2021.3 has no public API for registering Tools menu items at runtime, so the six menu items stay in Chinese. What they **log** follows the language.
- **Asset tooltips with Odin installed**: `[Tooltip]` is likewise an attribute constant. Without Odin, `BindRulesEditor` (a custom Inspector) draws them in the selected language; with Odin installed Odin takes over the drawing and the tooltips keep the fixed text from the attributes.

---

## Field grouping

With the **Group fields** toggle in the dialog checked, the generated fields are re-arranged **by type**: fields of the same type sit next to each other and each group is preceded by a `[Header("short type name")]`, which shows up in the Inspector as one collapsible title per group.

The toggle has no asset-level default — only the dialog's choice, stored in `EditorPrefs` (per project); the corresponding code parameter is `sameInAPart`.

- **Group order** = the order in which each type **first appears** (matching hierarchy pre-order, so it reads in the same order as the node tree); **within a group** hierarchy pre-order is kept, so the relative order of fields inside a group never changes.
- **The grouping key** is `BindRule.Type` verbatim (`UnityEngine.UI.Image` and `TMPro.TMP_Text` form two groups; two rules pointing at the same type are merged into one group). The header text uses the short type name: `Image` / `Button` / `TMP_Text` / `GameObject` ...
- **Runtime mode only sorts, it adds no `[Header]`**: those fields are not serialized and do not exist in the Inspector, which makes the attribute dead code.
- The dialog's field preview **shares the same grouping** as the generator (the preview shows the `[Header(...)]` lines too), keeping the "what you see is what gets generated" promise.
- Only the **arrangement of the field declarations** is affected; the assignment order in `BindComponents()` and the editor fill order (both keyed by field name) are unchanged.

With hierarchy pre-order `m_imgIcon → m_btnClose → m_tmpName → m_btnOK → m_imgBg`, checking "Group fields" generates:

```csharp
// With "Group fields" checked (HeroPanel example)
[Header("Image")]
[SerializeField] private Image m_ImgIcon = null;
[SerializeField] private Image m_ImgBg = null;

[Header("Button")]
[SerializeField] private Button m_BtnClose = null;
[SerializeField] private Button m_BtnOK = null;

[Header("TMP_Text")]
[SerializeField] private TMP_Text m_TmpName = null;
```

(**Every** group gets a `[Header(...)]`, including the first one, with a blank line between groups.)

---

## Directory structure

```
ScriptBinder/
├── package.json                 # UPM package description (com.unityscriptbinder.nuoyan)
├── LICENSE                      # MIT
├── Editor/
│   ├── NuoYan.ScriptBinder.asmdef
│   ├── BindRules.cs             # rules asset + code generation (ResolveRuleType, file layout and stale-file detection, Language enum)
│   ├── LocalizationConstant.cs  # localization tables (en/zh/ja) + Get/Format + Validate self check
│   ├── BindRulesEditor.cs       # custom Inspector for BindRules (non-Odin only: localized field tooltips, language dropdown)
│   ├── ScriptBinder.cs          # Inspector "Bind Script" button + generation dialog (namespace / folder / mode / base class / field preview)
│   └── ScriptBinderBindHelper.cs# 4-step pipeline state machine (generate->compile->mount->fill) + step menu items
```

Implementation notes:

- `BindRules.GenerateBindCode(go, baseClass, extraUsings, refresh, cusns, cussf, saveFileMode, mode)`: walks **all descendants recursively**, generating `.cs` (field declarations, plus `BindComponents()` in Runtime/Both) and `.Logic.cs` according to the bind mode; `cusns` / `cussf` / `saveFileMode` are this run's output overrides (**empty falls back to `Namespace` / `SavePath` / `SaveFileMode` on the asset**). Files are compared by content before writing and the method returns whether anything actually changed on disk, so the pipeline can decide whether a recompile is needed; with `refresh=false` files are only written and the pipeline issues a single batched `AssetDatabase.Refresh()` (one compile). `BindComponents()`'s lookup paths come from `BuildRelativePath` and the type mapping from `BuildRuntimeLookup`.
- `ScriptBinderBindHelper.StartBind(targets, baseClass, extraUsings, cusns, cussf, saveFileMode, mode)`: pipeline entry point. Tasks (target location info + **output folder + bind mode** + current step) are persisted in `EditorPrefs` and advanced by a `Flush` state machine driven by `[InitializeOnLoad]` + `[DidReloadScripts]`: only **after compilation is confirmed finished (domain reloaded)** does it fetch the new type via `MonoScript.GetClass()` → `Undo.AddComponent` on the target → fill fields with `SerializedObject` (unless Runtime) → save. A task's `genDir` is either the output root or `output root/class name` depending on the layout, and steps 3/4 locate the script through it (with `FolderByFolder` the subfolder is mandatory, otherwise the compiled type is never found). Scene objects are located through an instance-ID snapshot (still valid after a domain reload); prefabs are preferentially located in an open Prefab Stage and otherwise written back to the `.prefab` asset with `LoadPrefabContents` (variants excepted — they need the Stage).
- `ScriptBinder`: injects the button into the Inspector (**shown for any selected GameObject**) and `BindDialogWindow` provides the namespace / output folder / file layout / bind mode / custom base class choices plus the target field preview.

---

## Notes / limitations

- **Switching bind mode**: press Bind Script again with the other mode — `Runtime → Reference/Both` regenerates the `[SerializeField]` fields and fills them automatically; after `Reference → Runtime` the fields are no longer serialized, so not seeing references in the Inspector is expected (they are assigned by `BindComponents()` at runtime).
- **Every bind rewrites the generated file and costs one compile round**: the output depends only on "node names + rules + this run's parameters", and the file header **carries no generation timestamp** (only the fixed `Machine` / `Author` lines), so the same input produces a byte-identical file that diffs cleanly. The **write itself happens on every run** though, so each bind triggers a reimport and a compile — which also means the file's mtime always equals "when it was last generated" (use mtime or git to find out when a generated file appeared).
- **Switching the output location (folder / file layout) offers to clean up the old files**: after switching **File layout** or changing the **output folder**, the old location keeps a **class with the same name**, and both existing at once causes `CS0101 duplicate definition` — the whole project fails to compile. Before writing the new file the tool checks "the other layout + the last generation location" (paths are compared after full-path canonicalisation, so the current location is never mistaken for an old one) and, when it finds leftovers, asks whether to **delete the old file or handle it yourself** — it only deletes the `.cs` and its `.meta`; **the `.Logic.cs` is never touched** (it may hold your own code). If that file is left behind it still declares the class, so the dialog/log tells you to move or delete it yourself. A folder is removed only when empty. Choosing "keep it" stops the asking for the rest of the editor session.
- **The step menu uses the asset defaults**: the namespace / output folder / file layout in the dialog are overrides for **that run**; the `一键 生成→编译→挂载→绑定` and `步骤 1` menu items have no dialog and read `Namespace` / `SavePath` / `SaveFileMode` straight from the `BindRules` asset (**field grouping is the exception**: it has no asset-level default, so the menu reuses the choice remembered from the dialog). When mixing both entry points, remember that a mismatch produces scripts in different places.
- **Runtime lookup depends on child names / hierarchy**: the paths in `BindComponents()` are baked in at generation time. If you later rename children or restructure the hierarchy, press Bind Script again to refresh the paths; a failed lookup only leaves a null field (Runtime) or falls back to the editor reference (Both) and never throws.
- **Overwriting**: `xxx.cs` (the field declaration file) is overwritten on every binding. If the target node already has a script with that name that was **not** generated by this tool, it will be overwritten — check first.
- **Class name = GameObject name**: the node name must be a valid C# identifier (no spaces or special characters).
- **All descendants are traversed, containers are boundaries**: the tool walks every level below the target; but a node whose type prefix resolves to `rect` / `go` (such as `m_rectPanel`, `M_goList`) is treated as a **container boundary** — it gets its own field but its children are not traversed (bind them separately with that container as the new root). If the same name occurs at different levels, only the first one in pre-order is kept (the rest are ignored with a warning) to avoid generating duplicate fields.
- **Output folder**: generated files go to `Assets/{SavePath}` (or the folder given in the dialog), never inside the ScriptBinder package; with `FolderByFolder` one more same-named subfolder is created underneath.
- **Prefab edit mode**: for a task bound inside a Stage, if Unity closes the Stage during recompilation the pipeline falls back to "write the `.prefab` asset directly" (based on the saved version); if the Stage is still open it mounts inside the Stage (preserving unsaved edits — press Ctrl+S to save the prefab afterwards). You are warned before generating when the Stage has unsaved changes. Selecting a `.prefab` asset in the Project window also works (the asset itself is written). **Prefab variants** cannot be written back directly — enter their Stage and bind there.
- **TMP**: the `tmp` prefix generates a `TMP_Text` base-class field that accepts `TextMeshProUGUI` / `TextMeshPro`; TextMeshPro must be installed in the project.

### Support Project

If the tool is helpful to you, welcome to support project development

[☕ Thank me a coffee](Donate.md)
---

- If you have any suggestions or feedback, please open an issue or send an email to me.
- Gmail: nuoyanruoshui@gmail.com
- QQ: 2939213244@qq.com

## License

[MIT](./LICENSE) © NuoYan
