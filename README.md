# UnityScriptBinder（com.unityscriptbinder.nuoyan）

> 版本：1.0.0 ｜ Unity：2022.3+ ｜ 授权：MIT

UnityScriptBinder 是一个**脚本绑定（UI 自动挂载）工具**：为界面根节点一键生成与其 **GameObject 同名**的 `partial MonoBehaviour`，声明 UI 字段，并把每个字段在**编辑器内**直接填入对应子物体的组件引用，随场景 / 预制体一并保存——运行期零查找开销。

- 命名空间：`NuoYan.ScriptBinder`
- 程序集定义：`NuoYan.ScriptBinder.asmdef`（Editor 专用）
- 入口：选中带 `RectTransform` 的 GameObject 后，Inspector 上的 **Bind Script** 按钮

---

## 特性

- **一键绑定（四步管线）**：选中 UI 根节点 → 点 Inspector 的 `Bind Script` → 自动依次执行 **① 代码生成 → ② 等待编译 → ③ 挂载组件 → ④ 填充引用**，全程有分步日志；失败只保留任务并提示，修正后自动继续（也提供分步菜单可手动补做任意一步）。
- **partial + Logic 分离**：生成的字段声明文件（`xxx.cs`）每次重新生成、可覆盖；空的逻辑文件（`xxx.Logic.cs`）只在首次生成，之后**不会覆盖**，供开发者写自己的逻辑代码。
- **命名即规则**：按子物体命名前缀决定“可见性 / 字段类型”，无需手动拖引用。
- **编辑器内赋值**：引用在编辑期通过 `SerializedObject` 写入并随场景 / 预制体保存，Inspector 中可直接看到引用，方便把组件拖到 `Button.onClick` 等事件槽位。
- **编译时序自动处理**：任务与当前步骤持久化在 `EditorPrefs`（域重载 / 编辑器重启都不丢失），只有确认“编译完成且域已重载”后才挂载，不会出现“代码已生成但类型还没编译就提前用旧类型挂载、新字段漏填”的问题；播放模式下暂停、退出播放后自动继续。

---

## 安装

方式一：作为 **UPM git 包**（Package Manager → `+` → Add package from git URL）：

```
https://github.com/nuoyanruoshui/UnityScriptBinder.git
```

方式二：直接把 `Assets/Plugins/UnityScriptBinder` 整个目录放入工程（如本工程即放在 `Assets/Plugins/` 下）。

### 依赖

- UGUI（`UnityEngine.UI`）
- TextMeshPro（`TMPro`，生成 `tmp` 前缀字段时用到）
- 可选：**Odin Inspector**（宏 `ODIN_INSPECTOR`）——存在时 `SavePath` 会用文件夹选择器

---

## 快速上手

### 1. 命名子物体

所有层级子物体的命名都由两段前缀组成：**可见性前缀 + 类型前缀**（工具会**递归**遍历目标节点下的全部后代）。

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

> 例：子物体命名为 `m_img`（`m_` → `private`，`img` → `Image`），会生成：
> ```csharp
> [SerializeField] private Image m_Img = null;
> ```

**字段命名会自动驼峰（帕斯卡）化**：保留可见性前缀 `m_` / `M_` / `_`，把其后内容按单词（`_` 或大小写转折处切分）逐词首字母大写。例：`m_imgIcon` → `m_ImgIcon`、`m_img_icon` → `m_ImgIcon`、`m_btnClose` → `m_BtnClose`。子物体名不变，只影响生成的字段标识符。

前缀规则均可改（见下方“规则配置”）。未命中任何类型前缀的绑定字段按 `GameObject` 处理；未命中可见性前缀的物体不会生成字段。

### 2. 执行绑定

1. 在场景 / 预制体编辑模式中**选中 UI 根节点**（含 `RectTransform`，即有 `Canvas` 布局的节点）。
2. 在 Inspector 里点 **Bind Script** 按钮。
3. 弹出生成窗口：
   - 提示同名脚本会被覆盖；
   - **自定义父类**输入框：留空 = 不继承自定义父类（默认继承 `MonoBehaviour`）。填写**简单类名**即可（如 `MyPanelBase`）：工具会自动解析类型，与生成文件**同命名空间**时直接用简单名，**跨命名空间时自动在文件头补 `using <父类命名空间>;`**；也可以直接写带命名空间的全名（如 `GameLogic.MyPanelBase`）。注意该父类需自身继承自 `MonoBehaviour` 才能挂载为组件。输入时会**实时解析校验**：类不存在、或不是 `MonoBehaviour` 派生类会给出错误提示，"确定生成"也会被拦截。
4. 点 **确定生成**：工具开始四步管线——生成代码 → 等待 Unity 编译 → **编译完成后**把同名组件挂到该节点 → 填好全部字段引用。Console 会按 `[1/4 代码生成] / [2/4 编译] / [3/4 挂载] / [4/4 组件绑定]` 分步打印；若某一步失败（如编译报错、目标场景未打开），任务会保留并给出提示，修正后自动继续。

> 生成的脚本会以 `类名 = GameObject 名` 写入 `Assets/{BindRules.SavePath}/`（默认 `Scripts/UI`），并自动放进 `BindRules.Namespace`（默认 `GameLogic`）。

**分步手动菜单**（`ScriptBinder/` 菜单栏，与自动管线共用同一套实现，供失败后补救）：

| 菜单 | 作用 |
|------|------|
| `一键 生成→编译→挂载→绑定（选中）` | 完整四步（默认父类 `MonoBehaviour`）|
| `步骤 1：仅生成代码（选中）` | 只生成 `.cs` / `.Logic.cs` |
| `步骤 3：挂载组件（选中，需已编译）` | 只把同名组件挂到选中的节点 |
| `步骤 4：重新填充引用（选中）` | 只重填字段引用（不重新生成）|
| `查看待处理任务` / `清除待处理任务` | 查看 / 清空排队中的绑定任务 |

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
        [SerializeField] private Image m_ImgIcon = null;
        [SerializeField] private Button m_BtnClose = null;
        [SerializeField] private TMP_Text m_TmpName = null;
    }
}
```

```csharp
// HeroPanel.Logic.cs —— 开发者逻辑文件，只在首次生成，重新绑定不会被覆盖
namespace GameLogic
{
    public partial class HeroPanel
    {
        // 在这里访问 m_ImgIcon / m_BtnClose / m_TmpName ...
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
│   └── ScriptBinderBindHelper.cs# 四步绑定管线状态机（生成→编译→挂载→填充）＋分步菜单
```

实现要点：

- `BindRules.GenerateBindCode(go, baseClass, extraUsings, refresh)`：**递归扫描全部后代**，生成 `.cs` 与 `.Logic.cs`；`refresh=false` 时只写文件不刷新，由管线整批统一 `AssetDatabase.Refresh()`（一次编译）。
- `ScriptBinderBindHelper.StartBind(targets, baseClass, extraUsings)`：管线入口。任务（目标定位信息 + 当前步骤）持久化在 `EditorPrefs`，由 `[InitializeOnLoad]` + `[DidReloadScripts]` 驱动 `Flush` 状态机推进：**确认编译完成（域已重载）后才**用 `MonoScript.GetClass()` 拿新类型 → `Undo.AddComponent` 挂到目标 → `SerializedObject` 填字段 → 保存。场景对象按实例 ID 快照定位（域重载后仍有效）；预制体优先定位到已打开的 Prefab Stage，未打开时自动用 `LoadPrefabContents` 写回 `.prefab` 资产（变体除外，需进 Stage）。
- `ScriptBinder`：向 Inspector 注入按钮，并只在选中了含 `RectTransform` 的物体时显示。

---

## 注意事项 / 限制

- **同名覆盖**：`xxx.cs`（字段声明文件）每次绑定都会覆盖。若目标节点已有一个同名但**非工具生成**的脚本，会被覆盖，请先确认。
- **类名 = GameObject 名**：节点名称需要是合法 C# 标识符（无空格 / 特殊字符）。
- **递归所有后代，容器即边界**：工具会遍历目标节点下所有层级的后代；但命中 `rect` / `go` 类型前缀（如 `m_rectPanel`、`M_goList`）的节点会被当作**容器边界**——自身生成字段后不再深入其子节点（子节点应由该容器作为新的根另行绑定）。若不同层级出现**同名**绑定物体，只保留先序最早遍历到的一个（同名其余会被忽略并打印警告），以避免生成重复字段。
- **绑定目录**：生成的文件放在 `Assets/{SavePath}`，而非 ScriptBinder 包内部。
- **预制体编辑模式**：在 Stage 中绑定的任务，若 Unity 在重编译时关闭了 Stage，管线会自动改用“直接写回 .prefab 资产”的方式完成挂载（基于已保存的版本）；若 Stage 仍打开则优先在 Stage 内挂载（保留未保存编辑，完成后请 Ctrl+S 保存预制体）。Stage 有未保存修改时生成前会弹窗提醒。直接选中 Project 窗口的 `.prefab` 资产也可绑定（会写回资产本身）。**预制体变体**不能直接写回资产，请进入 Stage 后绑定。
- **TMP**：`tmp` 前缀生成 `TMP_Text` 基类字段，可承接 `TextMeshProUGUI` / `TextMeshPro`，需工程装有 TextMeshPro。

---

## License

[MIT](./LICENSE) © NuoYan
