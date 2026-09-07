# UnityScriptBinder（com.unityscriptbinder.nuoyan）

> 版本：1.0.0 ｜ Unity：2022.3+ ｜ 授权：MIT

UnityScriptBinder 是一个**脚本绑定（UI 自动挂载）工具**：为界面根节点一键生成与其 **GameObject 同名**的 `partial MonoBehaviour`，声明 UI 字段，并把每个字段在**编辑器内**直接填入对应子物体的组件引用，随场景 / 预制体一并保存——运行期零查找开销。

- 命名空间：`NuoYan.ScriptBinder`
- 程序集定义：`NuoYan.ScriptBinder.asmdef`（Editor 专用）
- 入口：选中带 `RectTransform` 的 GameObject 后，Inspector 上的 **Bind Script** 按钮

---

## 特性

- **一键绑定**：选中 UI 根节点 → 点 Inspector 的 `Bind Script` → 自动生成代码 → 编译完成后**自动把组件挂到目标节点**并填好全部字段引用。
- **partial + Logic 分离**：生成的字段声明文件（`xxx.cs`）每次重新生成、可覆盖；空的逻辑文件（`xxx.Logic.cs`）只在首次生成，之后**不会覆盖**，供开发者写自己的逻辑代码。
- **命名即规则**：按子物体命名前缀决定“可见性 / 字段类型”，无需手动拖引用。
- **编辑器内赋值**：引用在编辑期通过 `SerializedObject` 写入并随场景 / 预制体保存，Inspector 中可直接看到引用，方便把组件拖到 `Button.onClick` 等事件槽位。
- **编译时序自动处理**：新增脚本触发的域重载结束后才挂载组件，不会出现“代码已生成但类型还没编译、`AddComponent(null)`”的问题。

---

## 安装

方式一：作为 **UPM git 包**（Package Manager → `+` → Add package from git URL）：

```
https://github.com/NuoYanRuoShui/UnityScriptBinder.git
```

方式二：直接把 `Assets/Plugins/ScriptBinder` 整个目录放入工程（如本工程即放在 `Assets/Plugins/` 下）。

### 依赖

- UGUI（`UnityEngine.UI`）
- TextMeshPro（`TMPro`，生成 `tmp` 前缀字段时用到）
- 可选：**Odin Inspector**（宏 `ODIN_INSPECTOR`）——存在时 `SavePath` 会用文件夹选择器

---

## 快速上手

### 1. 命名子物体

直接子物体的命名由两段前缀组成：**可见性前缀 + 类型前缀**。

- 可见性前缀（决定字段访问级别）：

| 前缀 | 访问级别 |
|------|----------|
| `m_` | `private`（默认推荐，配合 `[SerializeField]`）|
| `M_` | `protected` |
| `_`  | `public` |

- 类型前缀（决定字段类型，默认内置规则）：

| 前缀 | 字段类型 | 说明 |
|------|----------|------|
| `txt` | `Text` | uGUI 文本 |
| `tmp` | `TMP_Text` | TMP 文本（匹配 `TextMeshProUGUI` / `TextMeshPro`）|
| `img` | `Image` | |
| `btn` | `Button` | |
| `tgl` | `Toggle` | |
| `sld` | `Slider` | |
| `sbr` | `Scrollbar` | |
| `drp` | `Dropdown` | |
| `ipt` | `InputField` | |
| `rect` | `RectTransform` | |
| `trans`| `Transform` | |
| `go`  | `GameObject` | 绑定整个子物体 |

> 例：子物体命名为 `m_imgIcon`（`m_` → `private`，`img` → `Image`），会生成：
> ```csharp
> [SerializeField] private Image m_imgIcon = null;
> ```

前缀规则均可改（见下方“规则配置”）。未命中任何类型前缀的绑定字段按 `GameObject` 处理；未命中可见性前缀的物体不会生成字段。

### 2. 执行绑定

1. 在场景 / 预制体编辑模式中**选中 UI 根节点**（含 `RectTransform`，即有 `Canvas` 布局的节点）。
2. 在 Inspector 里点 **Bind Script** 按钮。
3. 弹出确认框（提示会生成与节点同名脚本、同名脚本会被覆盖），点确定。
4. 工具生成代码并触发重编译；**编译完成后**自动把同名组件挂到该节点，并把字段引用填好。

> 生成的脚本会以 `类名 = GameObject 名` 写入 `Assets/{BindRules.SavePath}/`（默认 `Scripts/UI`），并自动放进 `BindRules.Namespace`（默认 `GameLogic`）。

### 3. 写逻辑

生成两个文件（以节点 `HeroPanel` 为例，`Assets/Scripts/UI/HeroPanel.cs`）：

```csharp
// HeroPanel.cs —— 由工具自动生成，请勿直接修改
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
        [SerializeField] private Image m_imgIcon = null;
        [SerializeField] private Button m_btnClose = null;
        [SerializeField] private TMP_Text m_tmpName = null;
    }
}
```

```csharp
// HeroPanel.Logic.cs —— 开发者逻辑文件，只在首次生成，重新绑定不会被覆盖
namespace GameLogic
{
    public partial class HeroPanel
    {
        // 在这里访问 m_imgIcon / m_btnClose / m_tmpName ...
    }
}
```

在 `.Logic.cs` 里写自己的方法，通过 `partial` 与自动生成的字段声明合并。

---

## 规则配置

规则存在 **ScriptableObject** `BindRules.asset` 中。

- 首次执行绑定时会**自动创建**于工程 `Assets/Resources/ScriptBinder/BindRules.asset`。
- 也可手动创建：右键 → `Create → ScriptBinder/BindRules` 后放入该路径。

配置项：

| 字段 | 说明 |
|------|------|
| `Namespace` | 生成代码的命名空间（默认 `GameLogic`）|
| `SavePath`  | 生成脚本目录，相对 `Assets/`（默认 `Scripts/UI`；有 Odin 时显示文件夹选择器）|
| `FieldVisibleRules` | 可见性前缀表：`Prefix` + `Visible(Private/Protected/Public)` |
| `Rules` | 类型前缀表：`Prefix` + `Type(BindType)` |

修改后立即对后续生成生效；**字段引用为编辑期写入，改规则后重新点一次 Bind Script 即可重挂并刷新引用**。

---

## 目录结构

```
ScriptBinder/
├── package.json                 # UPM 包描述（com.unityscriptbinder.nuoyan）
├── LICENSE                      # MIT
├── Editor/
│   ├── NuoYan.ScriptBinder.asmdef
│   ├── BindRules.cs             # 规则资产 + 代码生成逻辑
│   ├── ScriptBinder.cs          # Inspector “Bind Script” 按钮（[InitializeOnLoad]）
│   └── ScriptBinderBindHelper.cs# 编译完成后自动挂载 + 编辑器内填引用
```

实现要点：

- `BindRules.GenerateBindCode(go)`：扫描**直接子物体**，生成 `.cs` 与 `.Logic.cs` 并触发 `AssetDatabase.Refresh()`。
- `ScriptBinderBindHelper.RequestBind(go)`：把目标记录到 `EditorPrefs`，由 `[DidReloadScripts]` 在**域重载结束后**用 `MonoScript.GetClass()` 拿到真实类型 → `Undo.AddComponent` 挂到目标 → `SerializedObject` 填字段 → `MarkSceneDirty` 保存。支持普通场景对象与预制体 Stage 对象。
- `ScriptBinder`：向 Inspector 注入按钮，并只在选中了含 `RectTransform` 的物体时显示。

---

## 注意事项 / 限制

- **同名覆盖**：`xxx.cs`（字段声明文件）每次绑定都会覆盖。若目标节点已有一个同名但**非工具生成**的脚本，会被覆盖，请先确认。
- **类名 = GameObject 名**：节点名称需要是合法 C# 标识符（无空格 / 特殊字符）。
- **仅直接子物体**：只扫描目标节点的第一层子物体，不递归。
- **绑定目录**：生成的文件放在 `Assets/{SavePath}`，而非 ScriptBinder 包内部。
- **预制体编辑模式**：若在预制体 Stage 中绑定，请保持 Stage 打开到编译完成；若 Unity 在重编译时关闭了 Stage，工具会有限重试，失败时请在控制台提示后手动把生成的脚本挂到根节点再保存。
- **TMP**：`tmp` 前缀生成 `TMP_Text` 基类字段，可承接 `TextMeshProUGUI` / `TextMeshPro`，需工程装有 TextMeshPro。

---

## License

[MIT](./LICENSE) © NuoYan
