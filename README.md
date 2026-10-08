# UnityScriptBinder（com.unityscriptbinder.nuoyan）

> 版本：1.1.5 ｜ Unity：2021.3+（在 2021.3.44 上验证）｜ 授权：MIT

[English](./README.en.md) | 中文

UnityScriptBinder 是一个**脚本绑定（UI 自动挂载）工具**：为界面根节点一键生成与其 **GameObject 同名**的 `partial MonoBehaviour`，声明 UI 字段并自动挂载组件。支持两种赋值风格、三种模式：

- **引用赋值（Reference）**：字段以 `[SerializeField]` 声明，工具在**编辑器内**直接填入子物体组件引用，随场景 / 预制体保存——运行期零查找开销；
- **运行时绑定（Runtime）**：字段不序列化，生成 `BindComponents()`，运行时按节点路径 `transform.Find(...)` 查找赋值；
- **两者兼有（Both）**：编辑器填充优先，`BindComponents()` 仅对为 null 的字段运行时兜底。

- 命名空间：`NuoYan.ScriptBinder`
- 程序集定义：`NuoYan.ScriptBinder.asmdef`（Editor 专用）
- 入口：**选中任意 GameObject**（不限 UI；含不带动画的 3D 物体、空节点等）后，Inspector 上的 **Bind Script** 按钮

---

## 特性

- **一键绑定（四步管线）**：选中节点 → 点 Inspector 的 `Bind Script` → 自动依次执行 **① 代码生成 → ② 等待编译 → ③ 挂载组件 → ④ 填充引用**，全程有分步日志；失败只保留任务并提示，修正后自动继续（也提供分步菜单可手动补做任意一步）。
- **三种绑定模式**：引用赋值 / 运行时绑定 / 两者兼有，弹窗内可切换并记住上次选择，项目级默认在 `BindRules` 资产里配置（详见"绑定模式"）。
- **本次生成位置可覆盖**：生成弹窗里可直接改**命名空间**、**生成文件夹**、**生成文件模式**（全部平铺在一个目录 / 每个类一个同名子文件夹），三项与自定义父类一样会被记住，下次弹窗沿用；留空即回退 `BindRules` 资产上的默认值。
- **生成前预览**：弹窗内每个目标是一个 Foldout（默认展开第一个），展开后在 ScrollView 中列出将要绑定的每一个字段（字段名 + 可见性 + 类型），所见即所生成。
- **partial + Logic 分离**：生成的字段声明文件（`xxx.cs`）每次绑定都重新生成、可覆盖；空的逻辑文件（`xxx.Logic.cs`）只在首次生成，**已存在就永不被插件改动** —— 既不覆盖、也不删除，你的逻辑代码绝对安全。
- **命名即规则**：按子物体命名前缀决定“可见性 / 字段类型”，无需手动拖引用。
- **编辑器内赋值**：引用赋值 / 两者兼有模式下，引用在编辑期通过 `SerializedObject` 写入并随场景 / 预制体保存，Inspector 中可直接看到引用，方便把组件拖到 `Button.onClick` 等事件槽位。
- **编译时序自动处理**：任务与当前步骤持久化在 `EditorPrefs`（域重载 / 编辑器重启都不丢失），只有确认“编译完成且域已重载”后才挂载，不会出现“代码已生成但类型还没编译就提前用旧类型挂载、新字段漏填”的问题；播放模式下暂停、退出播放后自动继续。

---

## 安装

方式一：作为 **UPM git 包**（Package Manager → `+` → Add package from git URL）：

```
https://github.com/nuoyanruoshui/UnityScriptBinder.git
```

方式二：直接把 `Assets/Plugins/UnityScriptBinder` 整个目录放入工程。

### 依赖

- UGUI（`UnityEngine.UI`）
- TextMeshPro（`TMPro`，生成 `tmp` 前缀字段时用到）
- 可选：**Odin Inspector**（宏 `ODIN_INSPECTOR`）——存在时：`SavePath` 用文件夹选择器；`Rules` 里的 `Type` 提供下拉候选（数据源见下方 `BindTypes`）

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

> 例：子物体命名为 `m_img`（`m_` → `private`，`img` 前缀命中默认规则 → `UnityEngine.UI.Image`），会生成：
> ```csharp
> [SerializeField] private UnityEngine.UI.Image m_Img = null;
> ```

**类型前缀可自由扩展（无需改包代码）**：`BindRule.Type` 是字符串，填任意 C# 组件类型表达式即可——内置规则用全限定名（如 `UnityEngine.UI.Button`），自定义组件直接写完整类型名（如 `MyGame.MyComponent`），生成的代码按原文输出。简单名（如 `Button`、`TMP_Text`）也支持：工具会把类型解析成真实 `Type`，按解析出的命名空间自动补 `using`。通过 UPM 安装时枚举无法扩展，字符串规则正好解决该问题：只需在工程内的 `BindRules` 资产里增删/修改规则行，无需改动包内代码（类型匹配完全由资产上的字符串规则驱动）。

**类型目录（`BindTypes`）**：`BindRules` 资产上的一个类型清单，内置 12 个常用 UI 类型。安装了 Odin 时，编辑 `Rules` 的 `Type` 会弹出下拉，候选即来自该目录——把自定义组件全名追加到 `BindTypes`，就能在 `Type` 下拉里直接选用（`Type` 本身仍可手填任意表达式，不依赖目录）。

**字段命名会自动驼峰（帕斯卡）化**：保留可见性前缀 `m_` / `M_` / `_`，把其后内容按单词（`_` 或大小写转折处切分）逐词首字母大写。例：`m_imgIcon` → `m_ImgIcon`、`m_img_icon` → `m_ImgIcon`、`m_btnClose` → `m_BtnClose`。子物体名不变，只影响生成的字段标识符。

前缀规则均可改（见下方“规则配置”）。未命中任何类型前缀的绑定字段按 `GameObject` 处理；未命中可见性前缀的物体不会生成字段。

### 2. 执行绑定

1. 在场景 / 预制体编辑模式中**选中要绑定的根节点**（任意 GameObject；绑 UI 时通常选带 `RectTransform` 的面板根）。
2. 在 Inspector 里点 **Bind Script** 按钮。
3. 弹出生成窗口：
   - 提示同名脚本会被覆盖；
   - **命名空间**：本次生成写入的命名空间（默认取 `BindRules.Namespace`，留空即回退该默认值）；填非法标识符会实时标红并拦截"确定生成"；
   - **生成文件夹**：本次生成写入的目录，相对 `Assets`（默认取 `BindRules.SavePath`，留空即回退该默认值）。点右侧 `...` 可选择目录，**只接受 Assets 内的路径**；
   - **绑定模式**下拉框：引用赋值 / 运行时绑定 / 两者兼有（默认取 `BindRules.DefaultMode`，选择会被记住）；
   - **字段分组**开关：勾选后把**相同类型的字段排布到一起**，每组加 `[Header("短类型名")]`（详见"字段分组"）。它没有资产级默认值，选择会被记住；分步菜单（`一键` / `步骤 1`）沿用这个记住的选择，避免两个入口生成出不同排布的脚本；
   - **生成文件模式**下拉框：`FileByFile` 全部文件平铺在生成文件夹里 / `FolderByFolder` 每个类建一个同名子文件夹（默认取 `BindRules.SaveFileMode`，选择会被记住）。下方会实时显示"生成路径：`Assets/.../类名.cs`"，两项差异一眼可见；
   - **自定义父类**输入框：留空 = 不继承自定义父类（默认继承 `MonoBehaviour`）。填写**简单类名**即可（如 `MyPanelBase`）：工具会自动解析类型，与生成文件**同命名空间**时直接用简单名，**跨命名空间时自动在文件头补 `using <父类命名空间>;`**；也可以直接写带命名空间的全名（如 `GameLogic.MyPanelBase`）。注意该父类需自身继承自 `MonoBehaviour` 才能挂载为组件。输入时会**实时解析校验**：类不存在、或不是 `MonoBehaviour` 派生类会给出错误提示，"确定生成"也会被拦截；
   - **目标预览**：每个选中目标是一个 Foldout（默认展开第一个），展开后列出该目标将要绑定的每个字段，可先确认命名规则是否正确再生成。
4. 点 **确定生成**：工具开始四步管线——生成代码 → 等待 Unity 编译 → **编译完成后**把同名组件挂到该节点 → 填好全部字段引用。Console 会按 `[1/4 代码生成] / [2/4 编译] / [3/4 挂载] / [4/4 组件绑定]` 分步打印；若某一步失败（如编译报错、目标场景未打开），任务会保留并给出提示，修正后自动继续。

> 生成的脚本以 `类名 = GameObject 名` 命名，写入弹窗里的**生成文件夹**（默认 `BindRules.SavePath` = `Scripts/UI`），并放进同处指定的**命名空间**（默认 `BindRules.Namespace` = `GameLogic`）。两种文件布局：
> ```
> FileByFile      Assets/Scripts/UI/HeroPanel.cs          Assets/Scripts/UI/HeroPanel.Logic.cs
> FolderByFolder  Assets/Scripts/UI/HeroPanel/HeroPanel.cs  Assets/Scripts/UI/HeroPanel/HeroPanel.Logic.cs
> ```
> **弹窗里的三项（命名空间 / 生成文件夹 / 文件模式）只作用于本次点按钮的流程**；下方分步菜单与 `StartBind` 的默认调用仍取 `BindRules` 资产上的对应字段。

**分步手动菜单**（`Tools/NuoYan/ScriptBinder/` 菜单栏，与自动管线共用同一套实现，供失败后补救）：

| 菜单 | 作用 |
|------|------|
| `一键 生成→编译→挂载→绑定（选中）` | 完整四步（默认父类 `MonoBehaviour`）|
| `步骤 1：仅生成代码（选中）` | 只生成 `.cs` / `.Logic.cs` |
| `步骤 3：挂载组件（选中，需已编译）` | 只把同名组件挂到选中的节点 |
| `步骤 4：重新填充引用（选中）` | 只重填字段引用（不重新生成）|
| `查看待处理任务` / `清除待处理任务` | 查看 / 清空排队中的绑定任务 |
| `校验多语言文案（自检）` | 检查中/英/日三个词典的键与 `{0}` 占位符是否一致（详见"多语言"）|

### 绑定模式（引用赋值 / 运行时绑定 / 两者兼有）

不同开发者习惯不同：有人喜欢 `[SerializeField]` 在编辑器里拖好/自动填好引用；有人喜欢在代码里运行时查找（如 `transform.Find("xxx").GetComponent<Image>()`）。生成弹窗里的 **绑定模式** 下拉框支持三种：

| 模式 | 生成代码 | 编辑器填充 |
|------|----------|-----------|
| 引用赋值（Reference） | `[SerializeField] private X m_Xxx = null;` | 自动填充（运行时零查找开销） |
| 运行时绑定（Runtime） | `private X m_Xxx;` ＋ `public void BindComponents()`（按节点路径 `transform.Find("A/B")?.GetComponent<T>()` 逐字段查找赋值） | 跳过（字段不序列化，Inspector 不显示） |
| 两者兼有（Both） | `[SerializeField] private X m_Xxx = null;` ＋ `BindComponents()`（每行 `if (m_Xxx == null) { ... }`，编辑器已赋值的引用优先，仅对缺失引用运行时兜底） | 自动填充 ＋ 运行时兜底 |

- 项目默认模式配置在 `BindRules` 资产（`DefaultMode`）；弹窗内的选择会被记住（EditorPrefs），下次沿用。
- `BindComponents()` 需要开发者在生命周期中调用一次（如 `Awake` / `OnEnable` / `OnInit(userData)`）。工具**不自动生成 Awake**，避免与开发者自己的生命周期冲突。
- 运行时查找路径 = 字段子物体相对根节点的层级路径，与编辑器填充共用同一套递归收集规则，字段与路径一一对应。

运行时绑定 / 两者兼有模式生成的文件形态（`HeroPanel` 为例）：

```csharp
// HeroPanel.cs —— 由工具自动生成（运行时绑定模式）
using UnityEngine;
using TMPro;

namespace GameLogic
{
    public partial class HeroPanel : MonoBehaviour
    {
        private TMP_Text m_TmpName;          // 不序列化，Inspector 不显示
        private Button m_BtnClose;

        /// <summary>
        /// 运行时绑定：字段不序列化，运行时按节点路径查找组件并赋值。
        /// 请在逻辑代码（.Logic.cs）的生命周期中调用一次：如 Awake / OnEnable / OnInit(userData)。
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
// HeroPanel.Logic.cs —— 开发者逻辑文件
namespace GameLogic
{
    public partial class HeroPanel
    {
        private void Awake()
        {
            BindComponents(); // 或放到 OnInit/OnEnable 等你的生命周期里
            // 之后即可访问 m_TmpName / m_BtnClose ...
        }
    }
}
```

> 两者的 `BindComponents()` 中每行形如 `if (m_BtnClose == null) { m_BtnClose = ...; }`——编辑器已填充的引用保持不变，仅对缺失引用运行时兜底。

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

> 上例为默认的**引用赋值**模式；运行时绑定 / 两者兼有的完整文件形态见上方"绑定模式"一节（含 `BindComponents()` 示例）。

---

## 规则配置

规则存在 **ScriptableObject** `BindRules.asset` 中。

- 首次执行绑定时会**自动创建**于工程 `Assets/Resources/ScriptBinder/BindRules.asset`。

配置项：

| 字段 | 说明 |
|------|------|
| `Namespace` | 生成代码的命名空间（默认 `GameLogic`）；生成弹窗内可临时覆盖并记住 |
| `SavePath`  | 生成脚本目录，相对 `Assets/`（默认 `Scripts/UI`；有 Odin 时显示文件夹选择器）；生成弹窗内可临时覆盖并记住 |
| `DisplayLanguage` | 插件界面 / 控制台日志 / 生成代码注释的语言：`English`（默认）/ `Chinese` / `Japanese`（详见"多语言"）|
| `DefaultMode` | 默认绑定模式：`Reference` 引用赋值 / `Runtime` 运行时绑定 / `Both` 两者兼有（生成弹窗内可临时切换并记住）|
| `SaveFileMode` | 默认文件布局：`FileByFile` 全部文件平铺在 `SavePath` 里 / `FolderByFolder` 每个类建一个同名子文件夹（生成弹窗内可临时切换并记住）|
| `BindTypes` | 类型目录：编辑 `Rules.Type` 时的下拉候选（**需 Odin**）。内置 12 个常用类型；自定义组件把全名加进来即可下拉选用（`Type` 手填不依赖此目录）|
| `FieldVisibleRules` | 可见性前缀表：`Prefix` + `Visible(Private/Protected/Public)` |
| `Rules` | 类型前缀表：`Prefix` + `Type`（**任意 C# 组件类型表达式**，建议全限定名如 `UnityEngine.UI.Button`；简单名会自动补 using。可自由增删以支持自定义组件，无需改包代码）|

修改后立即对后续生成生效；**字段引用为编辑期写入，改规则后重新点一次 Bind Script 即可重挂并刷新引用**。

绑定弹窗内的偏好都存在 `EditorPrefs`（按工程隔离），下次弹窗沿用：**命名空间 / 生成文件夹 / 自定义父类**在点"确定生成"时落盘（点"取消"不会改动上次记住的设置），**绑定模式 / 生成文件模式**两个下拉框在改动的那一刻就记住。

---

## 多语言（中 / 英 / 日）

插件界面、控制台日志、生成代码里的注释都支持 **中文 / English / 日本語**，在 `BindRules` 资产的 **`DisplayLanguage`** 字段里切换（装了 Odin 时是 `[EnumToggleButtons]` 三连按钮；没装 Odin 时是"语言"下拉，选项按当前语言显示为 `英文 / 中文 / 日文` 之类）。改完立即生效，无需重启。

**默认是 `English`** —— 从旧版本升级上来时，资产里这个字段为空 / 未设置，界面会先变成英文，需要手动选一次中文。

覆盖范围：

| 位置 | 是否跟随语言 |
|------|-------------|
| 生成弹窗的字段名 / 按钮 / 提示、Inspector 的 `Bind Script` 按钮 | ✅ |
| 所有 `DisplayDialog` 确认框、进度条文字 | ✅ |
| 控制台日志与警告（`[1/4 代码生成]` 等分步日志、失败与警告提示）| ✅ |
| `BindRules` 资产字段的 Tooltip | ✅（**仅非 Odin 路径**，见下方限制）|
| 生成到工程的代码注释（`BindComponents` 摘要、`.Logic.cs` 说明、"请勿直接修改"）| ✅（**跟生成时的语言**，切语言后重新绑定才会刷新）|
| `Tools/NuoYan/ScriptBinder/*` 菜单项名称 | ❌ 见下方限制 |
| 生成的**代码本身**（类名、字段名、`[Header]`、`[SerializeField]`、文件头里的 `Auto generated ... by ScriptBinder` / `Time` / `Machine`）| ❌ 固定英文 |

**语言表的自检**：`Tools/NuoYan/ScriptBinder/校验多语言文案（自检）` 会检查三个语种的**键集合是否一致**、**`{0}` 占位符是否对齐**，不一致时把每条问题打到 Console。新增 / 修改文案后建议跑一次 —— 漏翻的键不会报错，只会静默回退（见下）。

**加一种语言 / 加一条文案**：编辑 `Editor/LocalizationConstant.cs` 里的三个 `Dictionary<string,string>`（键名约定见该文件头部注释：`Mode.*` / `Field.*` / `Dialog.*` / `Log.*` / `Gen.*` 等）。查表用 `LocalizationConstant.Get("Key")`，需要 `string.Format` 的用 `LocalizationConstant.Format("Key", 参数…)`。**缺键时的回退顺序是：调用方给的 fallback → 键名本身**（所以漏翻不会崩，但界面上会直接显示键名，一眼看得出来）。

**两处无法本地化（Unity 的硬限制）**：

- **菜单项名称**：`[MenuItem("...")]` 的路径是 C# 特性参数，必须是**编译期常量**，而 Unity 在 2021.3 没有公开的"运行时注册 Tools 菜单"API，所以 6 个菜单项名称固定为中文。菜单**点开后的日志反馈**跟随语言，日志里引用菜单时用的也是真实的中文菜单名（翻译了反而照不到）。
- **装了 Odin 时的资产 Tooltip**：`[Tooltip]` 同样是特性常量。没装 Odin 时由 `BindRulesEditor`（自定义 Inspector）画成多语言；装了 Odin 时 Odin 接管绘制，Tooltip 保持特性里的固定文案。

---

## 字段分组

生成弹窗里勾上**字段分组**后，生成的字段会按**类型**重新排布：相同类型的字段连续放在一起，每组前面加一条 `[Header("短类型名")]`，在 Inspector 里就是一组一组的折叠标题。

这个开关没有资产级默认值，只有弹窗里的选择（记在 `EditorPrefs`，按工程隔离）；代码里对应的参数名是 `sameInAPart`。

- **组间顺序** = 该类型**首次出现**的先后顺序（与层级先序一致，读起来和节点树同序）；**组内**保持层级先序，因此组内字段的相对顺序不变。
- **分组键**是 `BindRule.Type` 的原文（`UnityEngine.UI.Image` 与 `TMPro.TMP_Text` 是两个组；若两条规则指向同一个类型，则合并为一组）。Header 文本取短类型名：`Image` / `Button` / `TMP_Text` / `GameObject` …
- **Runtime 模式只排序、不加 Header**：该模式字段不序列化，Inspector 里根本没有这个字段，`[Header]` 是无效特性。
- 生成弹窗的字段预览与生成结果**共用同一套分组**（分组时预览里也会显示 `[Header(...)]` 行），保持"所见即所生成"。
- 只影响**字段声明的排布**；`BindComponents()` 的赋值顺序与编辑器填充顺序（都按字段名定位）不受影响。

层级先序为 `m_imgIcon → m_btnClose → m_tmpName → m_btnOK → m_imgBg` 时，勾上字段分组生成：

```csharp
// 勾上"字段分组"时（HeroPanel 为例）
[Header("Image")]
[SerializeField] private Image m_ImgIcon = null;
[SerializeField] private Image m_ImgBg = null;

[Header("Button")]
[SerializeField] private Button m_BtnClose = null;
[SerializeField] private Button m_BtnOK = null;

[Header("TMP_Text")]
[SerializeField] private TMP_Text m_TmpName = null;
```

（**每一组**都会带 `[Header(...)]`，包括第一组，组间空一行。）

---

## 目录结构

```
ScriptBinder/
├── package.json                 # UPM 包描述（com.unityscriptbinder.nuoyan）
├── LICENSE                      # MIT
├── Editor/
│   ├── NuoYan.ScriptBinder.asmdef
│   ├── BindRules.cs             # 规则资产 + 代码生成逻辑（含类型表达式解析 ResolveRuleType、文件布局与残留检测、Language 枚举）
│   ├── LocalizationConstant.cs  # 多语言文案表（中/英/日）+ 查表 Get/Format + 自检 Validate
│   ├── BindRulesEditor.cs       # BindRules 的自定义 Inspector（仅非 Odin：把字段 Tooltip 画成多语言、语言用下拉）
│   ├── ScriptBinder.cs          # Inspector “Bind Script” 按钮 + 生成对话框（命名空间 / 目录 / 模式 / 父类 / 字段预览）
│   └── ScriptBinderBindHelper.cs# 四步绑定管线状态机（生成→编译→挂载→填充）＋分步菜单
```

实现要点：

- `BindRules.GenerateBindCode(go, baseClass, extraUsings, refresh, cusns, cussf, saveFileMode, mode)`：**递归扫描全部后代**，按绑定模式生成 `.cs`（字段声明 ＋ Runtime/Both 时的 `BindComponents()`）与 `.Logic.cs`；`cusns` / `cussf` / `saveFileMode` 为本次生成的位置覆盖（**留空即回退资产上的 `Namespace` / `SavePath` / `SaveFileMode`**）。按内容比对后再写盘，返回"磁盘是否真的有变化"供管线判断是否需要重编译；`refresh=false` 时只写文件不刷新，由管线整批统一 `AssetDatabase.Refresh()`（一次编译）。`BindComponents()` 的查找路径由 `BuildRelativePath` 计算，类型映射见 `BuildRuntimeLookup`。
- `ScriptBinderBindHelper.StartBind(targets, baseClass, extraUsings, cusns, cussf, saveFileMode, mode)`：管线入口。任务（目标定位信息 + **生成目录 + 绑定模式** + 当前步骤）持久化在 `EditorPrefs`，由 `[InitializeOnLoad]` + `[DidReloadScripts]` 驱动 `Flush` 状态机推进：**确认编译完成（域已重载）后才**用 `MonoScript.GetClass()` 拿新类型 → `Undo.AddComponent` 挂到目标 → 非 Runtime 模式用 `SerializedObject` 填字段 → 保存。任务的 `genDir` 按布局取 `生成根` 或 `生成根/类名`，步骤 3/4 据此定位脚本（`FolderByFolder` 下必须带上子文件夹，否则永远找不到已编译类型）。场景对象按实例 ID 快照定位（域重载后仍有效）；预制体优先定位到已打开的 Prefab Stage，未打开时自动用 `LoadPrefabContents` 写回 `.prefab` 资产（变体除外，需进 Stage）。
- `ScriptBinder`：向 Inspector 注入按钮（**选中任意 GameObject 即显示**），`BindDialogWindow` 提供命名空间 / 生成文件夹 / 文件模式 / 绑定模式 / 自定义父类选择与目标字段预览。

---

## 注意事项 / 限制

- **绑定模式切换**：用另一模式重新点 Bind Script 即可整体切换——`Runtime → Reference/Both` 会重新生成 `[SerializeField]` 字段并自动填充；`Reference → Runtime` 后字段不再序列化，Inspector 中看不到引用属正常现象（由 `BindComponents()` 运行时赋值）。
- **每次绑定都重写生成文件，并走一轮编译**：生成内容只由「节点命名 + 规则 + 本次参数」决定，文件头**不写生成时间**（只留 `Machine` / `Author` 两行固定元信息），所以同样的输入产出逐字节相同的文件、便于 diff；但**写盘本身每轮都发生**，因此每次绑定都会触发一次重导入与编译，文件 mtime 也就始终等于"最后一次生成时间"（想知道某个生成文件什么时候产出的，看 mtime 或 git 即可）。
- **生成位置切换（目录 / 文件模式）会提示清理旧文件**：切换 `生成文件模式` 或改掉 `生成文件夹` 后，旧位置会残留一份**同名类**，两份同时存在会直接导致 `CS0101 重复定义`、整个工程编译失败。工具在写入新文件前会检测"另一布局 + 上次生成位置"（路径按全路径规范化后比较，不会把当前位置误判成旧位置），若发现残留会弹窗让你决定**删除旧文件 / 自行处理** —— **只删 `.cs` 与它的 `.meta`**，`.Logic.cs` 一个字节都不动（可能含你手写的逻辑），它若残留会继续声明同名类，弹窗/日志会明确提示你手动移走或删除。目录为空时才一并清理。选择"自行处理"后，本次编辑器会话内不再重复打扰。
- **分步菜单用资产默认值**：弹窗里的命名空间 / 生成文件夹 / 文件模式是**本次生成**的覆盖；菜单 `一键 生成→编译→挂载→绑定` 与 `步骤 1` 没有弹窗，直接取 `BindRules` 资产上的 `Namespace` / `SavePath` / `SaveFileMode`（**字段分组是例外**：它没有资产级默认值，菜单沿用弹窗里记住的选择）。混用两种入口时注意两者不一致会产生不同位置的脚本。
- **运行时查找依赖子物体命名/层级**：`BindComponents()` 的查找路径在生成时固化。若之后改动了子物体名字或层级结构，需要重新点一次 Bind Script 刷新路径；查找失败只会留下 null 字段（Runtime 模式）或由编辑器引用兜底（Both 模式），不会抛异常。
- **同名覆盖**：`xxx.cs`（字段声明文件）每次绑定都会覆盖。若目标节点已有一个同名但**非工具生成**的脚本，会被覆盖，请先确认。
- **类名 = GameObject 名**：节点名称需要是合法 C# 标识符（无空格 / 特殊字符）。
- **递归所有后代，容器即边界**：工具会遍历目标节点下所有层级的后代；但命中 `rect` / `go` 类型前缀（如 `m_rectPanel`、`M_goList`）的节点会被当作**容器边界**——自身生成字段后不再深入其子节点（子节点应由该容器作为新的根另行绑定）。若不同层级出现**同名**绑定物体，只保留先序最早遍历到的一个（同名其余会被忽略并打印警告），以避免生成重复字段。
- **绑定目录**：生成的文件放在 `Assets/{SavePath}`（或弹窗指定的目录），而非 ScriptBinder 包内部；`FolderByFolder` 时再往下一层建同名子文件夹。
- **预制体编辑模式**：在 Stage 中绑定的任务，若 Unity 在重编译时关闭了 Stage，管线会自动改用“直接写回 .prefab 资产”的方式完成挂载（基于已保存的版本）；若 Stage 仍打开则优先在 Stage 内挂载（保留未保存编辑，完成后请 Ctrl+S 保存预制体）。Stage 有未保存修改时生成前会弹窗提醒。直接选中 Project 窗口的 `.prefab` 资产也可绑定（会写回资产本身）。**预制体变体**不能直接写回资产，请进入 Stage 后绑定。
- **TMP**：`tmp` 前缀生成 `TMP_Text` 基类字段，可承接 `TextMeshProUGUI` / `TextMeshPro`，需工程装有 TextMeshPro。

---

- 如果你有什么建议或意见，欢迎在 Issues 提出，或发送邮件至邮箱。
- Gmail: nuoyanruoshui@gmail.com
- QQ: 2939213244@qq.com

## License

[MIT](./LICENSE) © NuoYan

