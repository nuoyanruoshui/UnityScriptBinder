#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
namespace NuoYan.ScriptBinder
{
    /// <summary>
    /// ScriptBinder 的多语言文案表：中 / 英 / 日。
    ///
    /// 键名约定（点分层级，按“出现位置”归组，便于查找与避免跨区重名）：
    ///   Lang.*      语言自身的显示名
    ///   Tooltip.*   BindRules 资产字段的 Tooltip（仅非 Odin 路径的 Inspector 用得到，见 BindRulesEditor）
    ///   Mode.*      BindMode 下拉项的文案；Mode.Hint.* 为选中后的说明
    ///   FileMode.*  SaveFileMode 下拉项的文案
    ///   Window.* / Btn.* / Field.*  生成弹窗的标题、按钮、字段标签
    ///   Hint.*      弹窗内的 HelpBox 文本
    ///   Dialog.*    弹窗/确认对话框（EditorUtility.DisplayDialog）的标题与正文
    ///   Menu.*      Tools/NuoYan/ScriptBinder 菜单项
    ///   Progress.*  进度条文字
    ///   Log.*       控制台日志与警告
    ///   Gen.*       生成到用户工程里的代码注释（跟随生成时的语言）
    ///
    /// 带 {0} {1} 的条目是 string.Format 模板，各语言必须保留同名占位符（可用 LocalizationConstant.Validate() 自检）。
    /// 语言取自 BindRules.CurrentLanguage()；缺键时回退到调用方给的 fallback，再缺则原样返回键名（一眼看出漏翻）。
    /// </summary>
    public class LocalizationConstant
    {
        // 显示顺序与 Language 枚举一致，供 Inspector 以当前语言渲染下拉
        private static readonly Language[] s_Languages =
        {
            Language.English,
            Language.Chinese,
            Language.Japanese,
        };

        public static readonly Dictionary<string, string> Chinese = new Dictionary<string, string>()
        {
            // ---- 语言自身 ----
            { "Lang.English", "英文" },
            { "Lang.Chinese", "中文" },
            { "Lang.Japanese", "日文" },
            // ---- BindRules 资产字段的 Tooltip ----
            { "Tooltip.DisplayLanguage", "插件界面与日志使用的语言" },
            { "Tooltip.SameInAPart", "会将相同类型的字段排布在一起" },
            { "Tooltip.DefaultMode", "默认绑定模式：Reference=引用赋值（SerializeField+编辑器填充）；Runtime=运行时绑定（BindComponents 手动调用）；Both=两者兼有（填充优先，运行时兜底）。生成弹窗内可临时切换" },
            { "Tooltip.SaveFileMode", "生成文件时写入的模式：FileByFile=将生成的文件写入默认或者指定的文件夹中；FolderByFolder=将生成的文件写入同名文件夹中，如果文件夹不存在则创建" },
            // ---- 绑定模式 ----
            { "Mode.Reference", "引用赋值（SerializeField，编辑器填充）" },
            { "Mode.Runtime", "运行时绑定（BindComponents 手动调用）" },
            { "Mode.Both", "两者兼有（编辑器填充 + 运行时兜底）" },
            { "Mode.Hint.Reference", "字段以 [SerializeField] 声明，生成后由工具在编辑器内自动填充引用（运行时零查找开销）。" },
            { "Mode.Hint.Runtime", "字段不序列化，生成 BindComponents() 运行时查找方法。\n请在逻辑代码（.Logic.cs）的生命周期中调用一次：如 Awake / OnEnable / OnInit(userData)。" },
            { "Mode.Hint.Both", "字段序列化并编辑器填充；同时生成 BindComponents()，仅对为 null 的字段运行时查找（编辑器引用优先，实例缺失引用时兜底）。\n需要时在生命周期中调用一次 BindComponents()。" },
            // ---- 生成文件模式 ----
            { "FileMode.FileByFile", "将生成的文件全写入主文件夹中(FileByFile)" },
            { "FileMode.FolderByFolder", "在主文件夹中创建同名文件夹写入文件(FolderByFolder)" },
            // ---- 弹窗标题 / 按钮 ----
            { "Window.Title", "ScriptBinder - 生成绑定脚本" },
            { "Btn.BindScript", "绑定脚本" },
            { "Btn.Cancel", "取消" },
            { "Btn.Confirm", "确定生成" },
            { "Btn.OK", "知道了" },
            { "Btn.DeleteOldFile", "删除旧文件" },
            { "Btn.KeepOldFile", "保留（我自己处理）" },
            { "Btn.ContinueAnyway", "仍然继续" },
            // ---- 弹窗字段标签 ----
            { "Field.Namespace", "命名空间" },
            { "Field.BaseClass", "自定义父类（可选）" },
            { "Field.SaveFolder", "生成文件夹" },
            { "Field.SameInAPart", "字段分组" },
            { "Field.BindMode", "绑定模式" },
            { "Field.FileMode", "生成文件模式" },
            { "Field.Language", "语言" },
            // ---- 弹窗内的提示 ----
            { "Hint.Main", "将按选中 GameObject 的名字生成同名脚本，自动执行 4 步：\n① 代码生成 → ② 等待编译 → ③ 挂载组件 → ④ 填充引用\n注意：请填写带命名空间的全路径，同名脚本会被【覆盖】！" },
            { "Hint.BadNamespace", "命名空间 {0} 不是合法的 C# 命名空间（每段需以字母或下划线开头，仅含字母/数字/下划线，用 . 分隔）。" },
            { "Hint.BadSaveFolder", "生成文件夹需为 Assets 下的相对路径（如 Scripts/UI），不能是绝对路径或含 ..。" },
            { "Hint.NoFields", "未发现可绑定字段：子物体命名需带可见性前缀（m_ / M_ / _），类型前缀见 BindRules 配置。" },
            { "Hint.Output", "生成路径：{0}.cs{1}\n命名空间：{2}\n（.Logic.cs 同目录，仅首次生成时创建，之后不覆盖）" },
            { "Hint.BaseClassEmpty", "留空：不继承自定义父类（默认继承 MonoBehaviour）" },
            { "Hint.BaseClassNotFound", "未找到父类：{0}\n请填写完整类名（含命名空间），或确认该类已编译。" },
            { "Hint.BaseClassNotMono", "父类 {0}（{1}）不是 MonoBehaviour 派生类，无法作为组件挂载。" },
            { "Hint.BaseClassOk", "将生成：public partial class {0} : {1}{2}" },
            { "Hint.TargetHeader", "目标：{0}（{1} 个绑定字段）" },
            { "Hint.TargetCount", "（共 {0} 个目标，规则相同）" },
            { "Hint.ClassNameHolder", "类名" },
            { "Hint.NoNamespace", "（无）" },
            { "Hint.AutoUsing", "\n自动补 using {0};" },
            // ---- 对话框 ----
            { "Dialog.PickSaveFolder", "选择生成文件夹（需位于 Assets 下）" },
            { "Dialog.NamespaceInvalid", "命名空间 {0} 不合法：\n每段需以字母或下划线开头，仅含字母 / 数字 / 下划线，用 . 分隔。\n\n留空表示回退 BindRules 资产里的默认命名空间。" },
            { "Dialog.SaveFolderInvalid", "生成文件夹 {0} 不合法：\n需为 Assets 下的相对路径（如 Scripts/UI），不能是绝对路径或含 ..。\n\n留空表示回退 BindRules 资产里的 SavePath。" },
            { "Dialog.SaveFolderOutsideAssets", "生成文件夹必须位于当前工程的 Assets 目录下：\n{0}\n\n请重新选择。" },
            { "Dialog.BaseClassNotFound", "找不到自定义父类：{0}\n请填写完整类名（含命名空间），或确认该类已编译后再生成。" },
            { "Dialog.BaseClassNotMono", "自定义父类 {0}（{1}）必须继承自 MonoBehaviour，否则无法作为组件挂载。" },
            { "Dialog.PrefabStageDirty", "当前 Prefab Stage 存在未保存的修改。\n\n若编译期间 Stage 被关闭，自动挂载会直接写回 .prefab 资产（基于已保存版本），未保存的修改可能丢失。\n\n建议先按 Ctrl+S 保存预制体再生成。" },
            { "Dialog.StaleFileTitle", "ScriptBinder - 发现旧位置残留文件" },
            { "Dialog.StaleFile", "在旧位置发现同名生成文件：\n\n{0}\n\n它与本次生成的文件同名，同时存在会导致 CS0101 重复定义、整个工程编译失败。\n\n是否删除该旧文件（连同它的 .meta）？" },
            { "Dialog.StaleFileLogicKept", "\n\n注意：同位置的 .Logic.cs 不会被改动（它可能含你手写的逻辑）—— 只要它还在，同名类依旧会重复定义，请自行把它移走或删除。" },
            { "Dialog.StaleFileNotToolGenerated", "\n\n注意：该文件不含本工具的生成标记，可能不是本工具生成的，删除前请自行确认内容。" },
            // ---- 菜单 ----
            // 注意：菜单项路径在 #region 之外的 [MenuItem] 特性里，必须是编译期常量，
            // 无法本地化（Unity 也没有公开的运行时注册 Tools 菜单的 API），故这里不设键。
            // ---- 进度条 ----
            { "Progress.WaitCompile", "步骤 2/4：等待 Unity 编译完成…" },
            // ---- 控制台日志 ----
            { "Log.DupField", "[ScriptBinder] 检测到重复命名的绑定字段：{0}，已忽略，同名节点只绑定最早遍历到的一个。" },
            { "Log.Generated", "[ScriptBinder] 已生成绑定代码 <b>{0}</b>（等待编译后挂载/填引用）" },
            { "Log.StaleFileDeleted", "[ScriptBinder] 已删除旧位置的同名生成文件：{0}" },
            { "Log.StaleFileDeleteFailed", "[ScriptBinder] 旧位置的同名生成文件未能删除：{0}（可能被占用或只读）。该文件与本次生成的同名类会导致 CS0101 重复定义，请手动删除后重新绑定。" },
            { "Log.StaleLogicOnly", "[ScriptBinder] 旧位置只剩 .Logic.cs：{0}。本工具不会改动它（可能含你手写的逻辑），请手动移走或删除，否则同名类会重复定义（CS0101）。" },
            { "Log.DeleteFileFailed", "[ScriptBinder] 删除文件失败：{0}\n{1}" },
            { "Log.CleanEmptyDirFailed", "[ScriptBinder] 清理空目录失败：{0}\n{1}" },
            { "Log.TasksParseFailed", "[ScriptBinder] 待处理任务解析失败，已清空：{0}" },
            { "Log.NoBindRules", "[ScriptBinder] 找不到 BindRules 配置，无法生成绑定代码。" },
            { "Log.InvalidClassName", "[ScriptBinder] 目标名 <b>{0}</b> 不是合法类名，已跳过（请先重命名 GameObject）。" },
            { "Log.DuplicateTarget", "[ScriptBinder] 同名目标 <b>{0}</b> 已跳过：同名脚本只生成一次，避免相互覆盖。" },
            { "Log.Step1Generated", "[ScriptBinder] [1/4 代码生成] <b>{0}</b>：{1} 个绑定字段（{2}）→ {3}" },
            { "Log.Step2Queued", "[ScriptBinder] [2/4 等待编译] 已登记 {0} 个任务（{1}），编译完成后自动挂载组件并填充引用。" },
            { "Log.Step2CompileEnded", "[ScriptBinder] [2/4 编译] 编译结束后未检测到域重载（可能编译失败）。\n请查看 Console 中的编译错误；任务已保留，修复后会自动继续。" },
            { "Log.Step2CompileIdle", "[ScriptBinder] [2/4 编译] 写入生成代码后编译迟迟未开始，仍在等待…（任务已保留）" },
            { "Log.Step2TypeUnresolved", "[ScriptBinder] [2/4 编译] 无法解析编译后的类型 <b>{0}</b>（可能编译失败或生成文件未导入）。请检查 Console；任务已保留，编译成功后自动继续。" },
            { "Log.Step2Compiled", "[ScriptBinder] [2/4 编译] 编译完成，开始步骤 3/4 挂载组件…" },
            { "Log.Step3TargetMissing", "[ScriptBinder] [3/4 挂载] 目标未定位：{0}\n场景对象需位于已打开的对应场景中；预制体无需打开 Stage（会自动写回资产）。任务已保留，目标可见后自动继续；也可用菜单 [ScriptBinder/步骤 3：挂载组件（选中）] 手动挂载。" },
            { "Log.Step3AddFailed", "[ScriptBinder] [3/4 挂载] 向 <b>{0}</b> 添加组件 {1} 失败。" },
            { "Log.Step3Mounted", "[ScriptBinder] [3/4 挂载] 已{0}组件 <b>{1}</b> 到 <b>{2}</b>" },
            { "Log.Step3Mount", "挂载" },
            { "Log.Step3Reuse", "复用" },
            { "Log.Step4NoEditorFill", "（运行时绑定模式：无需编辑器填充）" },
            { "Log.Step4StageSave", "[ScriptBinder] [4/4 组件绑定] 已挂载到 Prefab Stage，请按 Ctrl+S 保存预制体：{0}" },
            { "Log.Step4Filled", "[ScriptBinder] [4/4 组件绑定] <b>{0}</b> 字段填充 {1}/{2}{3}" },
            { "Log.VariantNoWriteback", "[ScriptBinder] [3/4 挂载] 预制体变体 {0} 不能直接写回资产，请双击进入 Prefab Stage（任务已保留，进入后自动继续）。" },
            { "Log.VariantNoWritebackShort", "[ScriptBinder] [3/4 挂载] 预制体变体 {0} 不能直接写回资产，请双击进入 Prefab Stage 后挂载。" },
            { "Log.HierarchyNotFound", "[ScriptBinder] [3/4 挂载] 预制体 {0} 中找不到层级 {1}，请检查预制体结构。" },
            { "Log.PrefabSaved", "[ScriptBinder] [4/4 组件绑定] 已保存预制体资产：{0}" },
            { "Log.TargetDescPrefab", "预制体资产未定位:{0}" },
            { "Log.TargetDescScene", "场景对象未定位:{0} / {1}" },
            { "Log.FieldNotFound", "[ScriptBinder] {0} 上找不到序列化字段 {1}（来源节点：{2}），可能生成代码与当前规则不一致。" },
            { "Log.ComponentNotFound", "[ScriptBinder] 子物体 {0} 上未找到 {1} 组件，字段 {2} 置空。" },
            { "Log.NoSelection", "[ScriptBinder] 请先选中要绑定的 UI 根节点。" },
            { "Log.Step1Done", "[ScriptBinder] 步骤 1 完成：仅生成代码。\n如需继续请等待编译完成后执行 [步骤 3：挂载组件（选中）] 和 [步骤 4：重新填充引用（选中）]，或直接用 [一键 生成→编译→挂载→绑定]。" },
            { "Log.MountTypeNotFound", "[ScriptBinder] [3/4 挂载] 未找到已编译类型 <b>{0}</b>：请先执行步骤 1 生成代码并等待编译完成（检查 Console 编译错误）。" },
            { "Log.MountedToPrefabAsset", "[ScriptBinder] [3/4 挂载] 已向预制体资产挂载 <b>{0}</b>：{1}" },
            { "Log.MountedToTarget", "[ScriptBinder] [3/4 挂载] 已挂载组件 <b>{0}</b> 到 <b>{1}</b>" },
            { "Log.PipelineDone", "[ScriptBinder] 绑定管线全部完成。" },
            { "Log.Refilled", "[ScriptBinder] [4/4 组件绑定] 已重新填充 <b>{0}</b> 的引用 {1}/{2}" },
            { "Log.NoPendingTasks", "[ScriptBinder] 当前没有待处理任务。" },
            { "Log.PendingTasksHeader", "[ScriptBinder] 待处理任务 {0} 个（编译状态：{1}{2}）：" },
            { "Log.PendingTaskItem", "  - {0}（{1}）：{2}" },
            { "Log.HasPendingChanges", "，有待编译变更" },
            { "Log.ValidateMissingKey", "{0} 缺少 {1}" },
            { "Log.ValidateExtraKey", "{0} 多出 {1}（英文里没有）" },
            { "Log.ValidatePlaceholderMismatch", "{0} 的 {1} 占位符不一致：英文 [{2}] vs {3} [{4}]" },
            { "Log.Cleared", "[ScriptBinder] 已清除全部待处理任务。" },
            { "Log.ModeLabel.Reference", "引用赋值" },
            { "Log.ModeLabel.Runtime", "运行时绑定" },
            { "Log.ModeLabel.Both", "两者兼有" },
            { "Log.Stage.AwaitCompile", "2 等待编译" },
            { "Log.Stage.Mount", "3 挂载组件" },
            { "Log.Stage.Bind", "4 填充引用" },
            { "Log.Stage.Done", "完成" },
            { "Log.Stage.Unknown", "未知" },
            { "Log.CompileState.NotStarted", "未开始" },
            { "Log.CompileState.Compiling", "编译中" },
            { "Log.CompileState.Compiled", "已编译" },
            { "Log.LocalizationOk", "[ScriptBinder] 多语言文案自检通过：三个语种的键完全一致。" },
            { "Log.LocalizationMissing", "[ScriptBinder] 多语言文案缺失（{0}）：\n{1}" },
            // ---- 生成代码里的注释 ----
            // 文件头的前 4 行（Auto generated / Time / Machine / Author）三种语言下都保持英文原文，故不设键：
            // "Auto generated code ... by ScriptBinder" 是 LooksToolGenerated 用来识别“本工具生成”的标记，不能随语言变化。
            { "Gen.HeaderNoEdit", "此文件由工具自动生成，请勿直接修改" },
            { "Gen.LogicSummary", "只会在第一次生成时创建，之后不会覆盖，请在此文件中写逻辑代码" },
            { "Gen.BindSummary.Runtime", "运行时绑定：字段不序列化，运行时按节点路径查找组件并赋值。" },
            { "Gen.BindSummary.Both", "运行时兜底：编辑器已填充的引用保持不变，仅对为 null 的字段按节点路径查找（适配预制体实例等缺失引用场景）。" },
            { "Gen.BindCallHint", "请在逻辑代码（.Logic.cs）的生命周期中调用一次：如 Awake / OnEnable / OnInit(userData)。" },
        };

        public static readonly Dictionary<string, string> English = new Dictionary<string, string>()
        {
            // ---- 语言自身 ----
            { "Lang.English", "English" },
            { "Lang.Chinese", "Chinese" },
            { "Lang.Japanese", "Japanese" },
            // ---- BindRules 资产字段的 Tooltip ----
            { "Tooltip.DisplayLanguage", "Language used by the plugin UI and console logs" },
            { "Tooltip.SameInAPart", "Arrange fields of the same type together, each group gets a [Header]" },
            { "Tooltip.DefaultMode", "Default bind mode: Reference = declared with [SerializeField] and filled in by the tool inside the editor; Runtime = not serialized, looked up by the generated BindComponents() at runtime; Both = serialized + filled in the editor, with runtime lookup as fallback for null fields. Can be switched for a single run in the bind dialog." },
            { "Tooltip.SaveFileMode", "How generated files are laid out: FileByFile = write all files into the default or specified folder; FolderByFolder = create a same-named subfolder per class, creating it when missing" },
            // ---- 绑定模式 ----
            { "Mode.Reference", "Reference (SerializeField, filled in editor)" },
            { "Mode.Runtime", "Runtime (BindComponents called manually)" },
            { "Mode.Both", "Both (editor fill + runtime fallback)" },
            { "Mode.Hint.Reference", "Fields are declared with [SerializeField] and the tool fills the references in the editor, so there is no runtime lookup cost." },
            { "Mode.Hint.Runtime", "Fields are not serialized; a BindComponents() lookup method is generated.\nCall it once from the lifecycle of your logic file (.Logic.cs): Awake / OnEnable / OnInit(userData)." },
            { "Mode.Hint.Both", "Fields are serialized and filled in the editor; BindComponents() is also generated and only looks up fields that are still null (editor references win, runtime lookup is the fallback).\nCall BindComponents() once from your lifecycle when needed." },
            // ---- 生成文件模式 ----
            { "FileMode.FileByFile", "Write all generated files into the main folder (FileByFile)" },
            { "FileMode.FolderByFolder", "Create a same-named subfolder per class (FolderByFolder)" },
            // ---- 弹窗标题 / 按钮 ----
            { "Window.Title", "ScriptBinder - Generate binding script" },
            { "Btn.BindScript", "Bind Script" },
            { "Btn.Cancel", "Cancel" },
            { "Btn.Confirm", "Generate" },
            { "Btn.OK", "OK" },
            { "Btn.DeleteOldFile", "Delete old file" },
            { "Btn.KeepOldFile", "Keep it (I will handle it)" },
            { "Btn.ContinueAnyway", "Continue anyway" },
            // ---- 弹窗字段标签 ----
            { "Field.Namespace", "Namespace" },
            { "Field.BaseClass", "Custom base class (optional)" },
            { "Field.SaveFolder", "Output folder" },
            { "Field.SameInAPart", "Group fields" },
            { "Field.BindMode", "Bind mode" },
            { "Field.FileMode", "File layout" },
            { "Field.Language", "Language" },
            // ---- 弹窗内的提示 ----
            { "Hint.Main", "A script named after each selected GameObject will be generated, running 4 steps:\n1) generate code -> 2) wait for compile -> 3) mount component -> 4) fill references\nNote: existing scripts with the same name will be OVERWRITTEN!" },
            { "Hint.BadNamespace", "{0} is not a valid C# namespace (each part must start with a letter or underscore and contain only letters, digits or underscores, separated by '.')." },
            { "Hint.BadSaveFolder", "The output folder must be a relative path under Assets (such as Scripts/UI); absolute paths and '..' are not allowed." },
            { "Hint.NoFields", "No bindable field found: child names need a visibility prefix (m_ / M_ / _); type prefixes are configured in BindRules." },
            { "Hint.Output", "Output path: {0}.cs{1}\nNamespace: {2}\n(.Logic.cs sits next to it; it is created only on first generation and never overwritten)" },
            { "Hint.BaseClassEmpty", "Empty: no custom base class (inherits MonoBehaviour)" },
            { "Hint.BaseClassNotFound", "Base class not found: {0}\nEnter the full class name including its namespace, or make sure the class is compiled." },
            { "Hint.BaseClassNotMono", "{0} ({1}) does not derive from MonoBehaviour and cannot be mounted as a component." },
            { "Hint.BaseClassOk", "Will generate: public partial class {0} : {1}{2}" },
            { "Hint.TargetHeader", "Target: {0} ({1} bindable fields)" },
            { "Hint.TargetCount", " ({0} targets in total, same rules)" },
            { "Hint.ClassNameHolder", "ClassName" },
            { "Hint.NoNamespace", " (none)" },
            { "Hint.AutoUsing", "\nadds using {0};" },
            // ---- 对话框 ----
            { "Dialog.PickSaveFolder", "Choose the output folder (must be under Assets)" },
            { "Dialog.NamespaceInvalid", "Namespace {0} is invalid:\neach part must start with a letter or underscore and contain only letters, digits or underscores, separated by '.'.\n\nLeave it empty to fall back to the default namespace in the BindRules asset." },
            { "Dialog.SaveFolderInvalid", "Output folder {0} is invalid:\nit must be a relative path under Assets (such as Scripts/UI); absolute paths and '..' are not allowed.\n\nLeave it empty to fall back to SavePath in the BindRules asset." },
            { "Dialog.SaveFolderOutsideAssets", "The output folder must be inside this project's Assets folder:\n{0}\n\nPlease choose again." },
            { "Dialog.BaseClassNotFound", "Custom base class not found: {0}\nEnter the full class name including its namespace, or make sure the class is compiled before generating." },
            { "Dialog.BaseClassNotMono", "Custom base class {0} ({1}) must derive from MonoBehaviour, otherwise it cannot be mounted as a component." },
            { "Dialog.PrefabStageDirty", "The current Prefab Stage has unsaved changes.\n\nIf the Stage gets closed during compilation, mounting falls back to writing the .prefab asset directly (based on the saved version) and unsaved changes may be lost.\n\nSaving the prefab with Ctrl+S first is recommended." },
            { "Dialog.StaleFileTitle", "ScriptBinder - Leftover file in the old location" },
            { "Dialog.StaleFile", "A generated file with the same name was found in the old location:\n\n{0}\n\nIt declares the same class as the file being generated now; keeping both causes CS0101 duplicate definition and the whole project fails to compile.\n\nDelete that old file (along with its .meta)?" },
            { "Dialog.StaleFileLogicKept", "\n\nNote: the .Logic.cs next to it will NOT be touched (it may contain your own code) - as long as it stays there the class is still declared twice, so please move or delete it yourself." },
            { "Dialog.StaleFileNotToolGenerated", "\n\nNote: this file does not carry the tool's generation marker, so it may not have been generated by this tool. Please check its contents before deleting." },
            // ---- 菜单 ----
            // 同中文表：菜单项路径是 [MenuItem] 特性的编译期常量，无法本地化，故不设键。
            // ---- 进度条 ----
            { "Progress.WaitCompile", "Step 2/4: waiting for Unity to finish compiling..." },
            // ---- 控制台日志 ----
            { "Log.DupField", "[ScriptBinder] Duplicate bindable field name detected: {0}. Ignored - only the first node visited in pre-order is bound for a given name." },
            { "Log.Generated", "[ScriptBinder] Generated binding code <b>{0}</b> (waiting for compilation to mount/fill references)" },
            { "Log.StaleFileDeleted", "[ScriptBinder] Deleted the leftover generated file in the old location: {0}" },
            { "Log.StaleFileDeleteFailed", "[ScriptBinder] Could not delete the leftover generated file in the old location: {0} (it may be in use or read-only). It declares the same class as the file just generated, which causes CS0101 duplicate definition - please delete it manually and bind again." },
            { "Log.StaleLogicOnly", "[ScriptBinder] Only a .Logic.cs is left in the old location: {0}. The tool will not touch it (it may contain your own code); please move or delete it yourself, otherwise the class is declared twice (CS0101)." },
            { "Log.DeleteFileFailed", "[ScriptBinder] Failed to delete file: {0}\n{1}" },
            { "Log.CleanEmptyDirFailed", "[ScriptBinder] Failed to clean up empty directory: {0}\n{1}" },
            { "Log.TasksParseFailed", "[ScriptBinder] Failed to parse pending tasks, cleared: {0}" },
            { "Log.NoBindRules", "[ScriptBinder] BindRules asset not found; cannot generate binding code." },
            { "Log.InvalidClassName", "[ScriptBinder] Target name <b>{0}</b> is not a valid class name, skipped (rename the GameObject first)." },
            { "Log.DuplicateTarget", "[ScriptBinder] Duplicate target <b>{0}</b> skipped: a script is generated only once per name to avoid overwriting each other." },
            { "Log.Step1Generated", "[ScriptBinder] [1/4 generate] <b>{0}</b>: {1} bindable fields ({2}) -> {3}" },
            { "Log.Step2Queued", "[ScriptBinder] [2/4 waiting for compile] Queued {0} task(s) ({1}); mounting and filling references start automatically once compilation finishes." },
            { "Log.Step2CompileEnded", "[ScriptBinder] [2/4 compile] No domain reload detected after compilation ended (compilation probably failed).\nCheck the compile errors in the Console; the task is kept and resumes automatically once fixed." },
            { "Log.Step2CompileIdle", "[ScriptBinder] [2/4 compile] Compilation has not started yet after the generated code was written; still waiting... (task kept)" },
            { "Log.Step2TypeUnresolved", "[ScriptBinder] [2/4 compile] Cannot resolve the compiled type <b>{0}</b> (compilation may have failed, or the generated file was not imported). Check the Console; the task is kept and resumes automatically once compilation succeeds." },
            { "Log.Step2Compiled", "[ScriptBinder] [2/4 compile] Compilation finished, starting step 3/4 (mount component)..." },
            { "Log.Step3TargetMissing", "[ScriptBinder] [3/4 mount] Target not found: {0}\nScene objects must be in the corresponding opened scene; prefabs do not need an open Stage (the asset is written back automatically). The task is kept and resumes automatically once the target is visible; you can also mount manually with the menu [ScriptBinder/步骤 3：挂载组件（选中）]." },
            { "Log.Step3AddFailed", "[ScriptBinder] [3/4 mount] Failed to add component {1} to <b>{0}</b>." },
            { "Log.Step3Mounted", "[ScriptBinder] [3/4 mount] {0} component <b>{1}</b> on <b>{2}</b>" },
            { "Log.Step3Mount", "Mounted" },
            { "Log.Step3Reuse", "Reused" },
            { "Log.Step4NoEditorFill", " (runtime mode: no editor fill needed)" },
            { "Log.Step4StageSave", "[ScriptBinder] [4/4 bind] Mounted in the Prefab Stage; press Ctrl+S to save the prefab: {0}" },
            { "Log.Step4Filled", "[ScriptBinder] [4/4 bind] <b>{0}</b> filled {1}/{2} references{3}" },
            { "Log.VariantNoWriteback", "[ScriptBinder] [3/4 mount] Prefab variant {0} cannot be written back directly; double-click to open its Prefab Stage (task kept, resumes once opened)." },
            { "Log.VariantNoWritebackShort", "[ScriptBinder] [3/4 mount] Prefab variant {0} cannot be written back directly; open its Prefab Stage first and mount there." },
            { "Log.HierarchyNotFound", "[ScriptBinder] [3/4 mount] Hierarchy {1} not found in prefab {0}; please check the prefab structure." },
            { "Log.PrefabSaved", "[ScriptBinder] [4/4 bind] Saved prefab asset: {0}" },
            { "Log.TargetDescPrefab", "prefab asset not found: {0}" },
            { "Log.TargetDescScene", "scene object not found: {0} / {1}" },
            { "Log.FieldNotFound", "[ScriptBinder] Serialized field {1} not found on {0} (source node: {2}); the generated code may not match the current rules." },
            { "Log.ComponentNotFound", "[ScriptBinder] Component {1} not found on child {0}; field {2} left empty." },
            { "Log.NoSelection", "[ScriptBinder] Select the UI root you want to bind first." },
            { "Log.Step1Done", "[ScriptBinder] Step 1 done: code generated only.\nTo continue, wait for compilation and then run [步骤 3：挂载组件（选中）] and [步骤 4：重新填充引用（选中）], or use [一键 生成→编译→挂载→绑定]. (Menu names are not localized.)" },
            { "Log.MountTypeNotFound", "[ScriptBinder] [3/4 mount] Compiled type <b>{0}</b> not found: run step 1 to generate the code first and wait for compilation (check the compile errors in the Console)." },
            { "Log.MountedToPrefabAsset", "[ScriptBinder] [3/4 mount] Mounted <b>{0}</b> onto the prefab asset: {1}" },
            { "Log.MountedToTarget", "[ScriptBinder] [3/4 mount] Mounted component <b>{0}</b> on <b>{1}</b>" },
            { "Log.PipelineDone", "[ScriptBinder] Bind pipeline finished." },
            { "Log.Refilled", "[ScriptBinder] [4/4 bind] Refilled {1}/{2} references of <b>{0}</b>" },
            { "Log.NoPendingTasks", "[ScriptBinder] There is no pending task." },
            { "Log.PendingTasksHeader", "[ScriptBinder] {0} pending task(s) (compile state: {1}{2}):" },
            { "Log.PendingTaskItem", "  - {0} ({1}): {2}" },
            { "Log.HasPendingChanges", ", pending changes to compile" },
            { "Log.ValidateMissingKey", "{0} is missing {1}" },
            { "Log.ValidateExtraKey", "{0} has an extra {1} (not present in English)" },
            { "Log.ValidatePlaceholderMismatch", "{0} / {1} placeholders differ: English [{2}] vs {3} [{4}]" },
            { "Log.Cleared", "[ScriptBinder] Cleared all pending tasks." },
            { "Log.ModeLabel.Reference", "Reference" },
            { "Log.ModeLabel.Runtime", "Runtime" },
            { "Log.ModeLabel.Both", "Both" },
            { "Log.Stage.AwaitCompile", "2 awaiting compile" },
            { "Log.Stage.Mount", "3 mount component" },
            { "Log.Stage.Bind", "4 fill references" },
            { "Log.Stage.Done", "done" },
            { "Log.Stage.Unknown", "unknown" },
            { "Log.CompileState.NotStarted", "not started" },
            { "Log.CompileState.Compiling", "compiling" },
            { "Log.CompileState.Compiled", "compiled" },
            { "Log.LocalizationOk", "[ScriptBinder] Localization self check passed: all three languages have exactly the same keys." },
            { "Log.LocalizationMissing", "[ScriptBinder] Missing localization entries ({0}):\n{1}" },
            // ---- 生成代码里的注释 ----
            // 同中文表：文件头前 4 行保持英文原文，不设键。
            { "Gen.HeaderNoEdit", "This file is generated by the tool; do not edit it directly" },
            { "Gen.LogicSummary", "Created only on first generation and never overwritten afterwards; write your logic code in this file" },
            { "Gen.BindSummary.Runtime", "Runtime binding: fields are not serialized; components are looked up by node path and assigned at runtime." },
            { "Gen.BindSummary.Both", "Runtime fallback: references already filled in the editor stay untouched; only null fields are looked up by node path (covers missing references such as prefab instances)." },
            { "Gen.BindCallHint", "Call it once from the lifecycle of your logic file (.Logic.cs): Awake / OnEnable / OnInit(userData)." },
        };

        public static readonly Dictionary<string, string> Japanese = new Dictionary<string, string>()
        {
            // ---- 语言自身 ----
            { "Lang.English", "英語" },
            { "Lang.Chinese", "中国語" },
            { "Lang.Japanese", "日本語" },
            // ---- BindRules 资产字段的 Tooltip ----
            { "Tooltip.DisplayLanguage", "プラグインの UI とコンソールログで使用する言語" },
            { "Tooltip.SameInAPart", "同じタイプのフィールドをまとめて配置し、グループごとに [Header] を付けます" },
            { "Tooltip.DefaultMode", "既定のバインドモード：Reference=[SerializeField] で宣言しエディタで参照を設定（実行時の検索コストなし）；Runtime=シリアライズせず、生成された BindComponents() が実行時に検索；Both=シリアライズ＋エディタで設定し、null のフィールドのみ実行時にフォールバック検索。バインドダイアログで今回のみ切り替えられます。" },
            { "Tooltip.SaveFileMode", "生成ファイルの配置方法：FileByFile=既定または指定したフォルダにそのまま書き込む；FolderByFolder=クラスごとに同名のサブフォルダを作成する（存在しない場合は作成）" },
            // ---- 绑定模式 ----
            { "Mode.Reference", "参照を割り当て（SerializeField、エディタで設定）" },
            { "Mode.Runtime", "ランタイムバインディング（BindComponents を手動で呼ぶ）" },
            { "Mode.Both", "両方（エディタで設定＋実行時フォールバック）" },
            { "Mode.Hint.Reference", "フィールドは [SerializeField] で宣言され、生成後にツールがエディタ内で参照を自動設定します（実行時の検索コストはゼロ）。" },
            { "Mode.Hint.Runtime", "フィールドはシリアライズされず、BindComponents() の検索メソッドを生成します。\nロジックコード（.Logic.cs）のライフサイクルで1度だけ呼び出してください：Awake / OnEnable / OnInit(userData)。" },
            { "Mode.Hint.Both", "フィールドはシリアライズしてエディタで設定し、同時に BindComponents() も生成します（null のフィールドのみ実行時に検索。エディタの参照が優先され、実行時検索はフォールバックです）。\n必要なときにライフサイクルで BindComponents() を1度呼び出してください。" },
            // ---- 生成文件模式 ----
            { "FileMode.FileByFile", "生成したファイルをすべてメインフォルダに書き込む(FileByFile)" },
            { "FileMode.FolderByFolder", "メインフォルダ内に同名フォルダを作成して書き込む(FolderByFolder)" },
            // ---- 弹窗标题 / 按钮 ----
            { "Window.Title", "ScriptBinder - バインドスクリプトの生成" },
            { "Btn.BindScript", "スクリプトをバインド" },
            { "Btn.Cancel", "キャンセル" },
            { "Btn.Confirm", "生成" },
            { "Btn.OK", "OK" },
            { "Btn.DeleteOldFile", "古いファイルを削除" },
            { "Btn.KeepOldFile", "残す（自分で対応する）" },
            { "Btn.ContinueAnyway", "そのまま続行" },
            // ---- 弹窗字段标签 ----
            { "Field.Namespace", "名前空間" },
            { "Field.BaseClass", "カスタム基底クラス（任意）" },
            { "Field.SaveFolder", "出力フォルダ" },
            { "Field.SameInAPart", "フィールドをグループ化" },
            { "Field.BindMode", "バインドモード" },
            { "Field.FileMode", "ファイル配置" },
            { "Field.Language", "言語" },
            // ---- 弹窗内的提示 ----
            { "Hint.Main", "選択した GameObject と同じ名前のスクリプトを生成し、4つのステップを自動実行します：\n① コード生成 → ② コンパイル待ち → ③ コンポーネントのアタッチ → ④ 参照の設定\n注意：同名のスクリプトは【上書き】されます！" },
            { "Hint.BadNamespace", "{0} は有効な C# の名前空間ではありません（各部分は英字またはアンダースコアで始まり、英数字とアンダースコアのみを含み、. で区切る必要があります）。" },
            { "Hint.BadSaveFolder", "出力フォルダは Assets 配下の相対パス（例：Scripts/UI）である必要があります。絶対パスや .. は使用できません。" },
            { "Hint.NoFields", "バインド可能なフィールドが見つかりません：子オブジェクト名に可視性プレフィックス（m_ / M_ / _）が必要です。型プレフィックスは BindRules で設定します。" },
            { "Hint.Output", "出力パス：{0}.cs{1}\n名前空間：{2}\n（.Logic.cs は同じ場所に置かれ、初回生成時のみ作成され以後上書きされません）" },
            { "Hint.BaseClassEmpty", "空の場合：カスタム基底クラスを継承しません（既定で MonoBehaviour を継承）" },
            { "Hint.BaseClassNotFound", "基底クラスが見つかりません：{0}\n名前空間を含む完全なクラス名を入力するか、そのクラスがコンパイル済みであることを確認してください。" },
            { "Hint.BaseClassNotMono", "{0}（{1}）は MonoBehaviour 派生ではないため、コンポーネントとしてアタッチできません。" },
            { "Hint.BaseClassOk", "生成内容：public partial class {0} : {1}{2}" },
            { "Hint.TargetHeader", "対象：{0}（バインド可能なフィールド {1} 個）" },
            { "Hint.TargetCount", "（全 {0} 件の対象、ルールは同じ）" },
            { "Hint.ClassNameHolder", "ClassName" },
            { "Hint.NoNamespace", "（なし）" },
            { "Hint.AutoUsing", "\nusing {0}; を自動追加" },
            // ---- 对话框 ----
            { "Dialog.PickSaveFolder", "出力フォルダを選択（Assets 配下である必要があります）" },
            { "Dialog.NamespaceInvalid", "名前空間 {0} が無効です：\n各部分は英字またはアンダースコアで始まり、英数字とアンダースコアのみを含み、. で区切る必要があります。\n\n空欄にすると BindRules アセットの既定の名前空間にフォールバックします。" },
            { "Dialog.SaveFolderInvalid", "出力フォルダ {0} が無効です：\nAssets 配下の相対パス（例：Scripts/UI）である必要があり、絶対パスや .. は使用できません。\n\n空欄にすると BindRules アセットの SavePath にフォールバックします。" },
            { "Dialog.SaveFolderOutsideAssets", "出力フォルダはこのプロジェクトの Assets フォルダ内である必要があります：\n{0}\n\nもう一度選択してください。" },
            { "Dialog.BaseClassNotFound", "カスタム基底クラスが見つかりません：{0}\n名前空間を含む完全なクラス名を入力するか、そのクラスがコンパイル済みであることを確認してから生成してください。" },
            { "Dialog.BaseClassNotMono", "カスタム基底クラス {0}（{1}）は MonoBehaviour を継承している必要があります。継承していないとコンポーネントとしてアタッチできません。" },
            { "Dialog.PrefabStageDirty", "現在の Prefab Stage に未保存の変更があります。\n\nコンパイル中に Stage が閉じられると、アタッチは保存済みバージョンを基に .prefab アセットへ直接書き戻されるため、未保存の変更が失われる可能性があります。\n\n先に Ctrl+S でプレハブを保存してから生成することをおすすめします。" },
            { "Dialog.StaleFileTitle", "ScriptBinder - 古い場所に残ったファイル" },
            { "Dialog.StaleFile", "古い場所に同名の生成ファイルが見つかりました：\n\n{0}\n\nこれから生成するファイルと同じクラスを宣言しているため、両方が存在すると CS0101 の重複定義となりプロジェクト全体がコンパイルできなくなります。\n\nこの古いファイル（およびその .meta）を削除しますか？" },
            { "Dialog.StaleFileLogicKept", "\n\n注意：同じ場所にある .Logic.cs は一切変更しません（あなたが書いたコードが入っている可能性があるため）。ただしそれが残っている限りクラスは二重に宣言されるので、手動で移動または削除してください。" },
            { "Dialog.StaleFileNotToolGenerated", "\n\n注意：このファイルには本ツールの生成マークがありません。本ツールが生成したものではない可能性があるため、内容を確認してから削除してください。" },
            // ---- 菜单 ----
            // 中国語表と同じ：メニューパスは [MenuItem] のコンパイル時定数のためローカライズ不可。
            // ---- 进度条 ----
            { "Progress.WaitCompile", "ステップ 2/4：Unity のコンパイル完了を待機中..." },
            // ---- 控制台日志 ----
            { "Log.DupField", "[ScriptBinder] 同名のバインドフィールドを検出しました：{0}。無視します（同じ名前では、先行順で最初に見つかったノードのみをバインドします）。" },
            { "Log.Generated", "[ScriptBinder] バインドコード <b>{0}</b> を生成しました（コンパイル後にアタッチと参照設定を行います）" },
            { "Log.StaleFileDeleted", "[ScriptBinder] 古い場所の同名生成ファイルを削除しました：{0}" },
            { "Log.StaleFileDeleteFailed", "[ScriptBinder] 古い場所の同名生成ファイルを削除できませんでした：{0}（使用中か読み取り専用の可能性があります）。今生成したファイルと同じクラスを宣言しているため CS0101 の重複定義になります。手動で削除してから再度バインドしてください。" },
            { "Log.StaleLogicOnly", "[ScriptBinder] 古い場所には .Logic.cs だけが残っています：{0}。本ツールは変更しません（あなたが書いたコードが入っている可能性があるため）。手動で移動または削除してください。そのままだとクラスが二重宣言になります（CS0101）。" },
            { "Log.DeleteFileFailed", "[ScriptBinder] ファイルの削除に失敗しました：{0}\n{1}" },
            { "Log.CleanEmptyDirFailed", "[ScriptBinder] 空フォルダの削除に失敗しました：{0}\n{1}" },
            { "Log.TasksParseFailed", "[ScriptBinder] 保留中タスクの解析に失敗したためクリアしました：{0}" },
            { "Log.NoBindRules", "[ScriptBinder] BindRules アセットが見つからないため、バインドコードを生成できません。" },
            { "Log.InvalidClassName", "[ScriptBinder] 対象名 <b>{0}</b> は有効なクラス名ではないためスキップしました（先に GameObject をリネームしてください）。" },
            { "Log.DuplicateTarget", "[ScriptBinder] 同名の対象 <b>{0}</b> をスキップしました：同名スクリプトは1度だけ生成し、相互上書きを防ぎます。" },
            { "Log.Step1Generated", "[ScriptBinder] [1/4 コード生成] <b>{0}</b>：バインドフィールド {1} 個（{2}）→ {3}" },
            { "Log.Step2Queued", "[ScriptBinder] [2/4 コンパイル待ち] タスクを {0} 件登録しました（{1}）。コンパイル完了後にコンポーネントのアタッチと参照設定を自動で行います。" },
            { "Log.Step2CompileEnded", "[ScriptBinder] [2/4 コンパイル] コンパイル終了後にドメインリロードを検出できませんでした（コンパイル失敗の可能性があります）。\nConsole のコンパイルエラーを確認してください。タスクは保持されており、修正すると自動的に再開します。" },
            { "Log.Step2CompileIdle", "[ScriptBinder] [2/4 コンパイル] 生成コードを書き込んだ後もコンパイルが始まりません。引き続き待機しています…（タスクは保持）" },
            { "Log.Step2TypeUnresolved", "[ScriptBinder] [2/4 コンパイル] コンパイル後の型 <b>{0}</b> を解決できません（コンパイル失敗、または生成ファイルが未インポートの可能性があります）。Console を確認してください。タスクは保持されており、コンパイル成功後に自動で再開します。" },
            { "Log.Step2Compiled", "[ScriptBinder] [2/4 コンパイル] コンパイル完了。ステップ 3/4 のコンポーネントのアタッチを開始します…" },
            { "Log.Step3TargetMissing", "[ScriptBinder] [3/4 アタッチ] 対象が見つかりません：{0}\nシーンオブジェクトは開いている対応シーン内に必要です。プレハブは Stage を開く必要がありません（アセットへ自動で書き戻します）。タスクは保持されており、対象が見えるようになると自動で再開します。メニュー [ScriptBinder/步骤 3：挂载组件（选中）] から手動でアタッチすることもできます。" },
            { "Log.Step3AddFailed", "[ScriptBinder] [3/4 アタッチ] <b>{0}</b> へのコンポーネント {1} の追加に失敗しました。" },
            { "Log.Step3Mounted", "[ScriptBinder] [3/4 アタッチ] <b>{2}</b> にコンポーネント <b>{1}</b> を{0}しました" },
            { "Log.Step3Mount", "アタッチ" },
            { "Log.Step3Reuse", "再利用" },
            { "Log.Step4NoEditorFill", "（ランタイムモード：エディタでの設定は不要）" },
            { "Log.Step4StageSave", "[ScriptBinder] [4/4 バインド] Prefab Stage にアタッチしました。Ctrl+S でプレハブを保存してください：{0}" },
            { "Log.Step4Filled", "[ScriptBinder] [4/4 バインド] <b>{0}</b> の参照を {1}/{2} 設定しました{3}" },
            { "Log.VariantNoWriteback", "[ScriptBinder] [3/4 アタッチ] プレハブバリアント {0} はアセットへ直接書き戻せません。ダブルクリックして Prefab Stage を開いてください（タスクは保持、開くと自動で再開します）。" },
            { "Log.VariantNoWritebackShort", "[ScriptBinder] [3/4 アタッチ] プレハブバリアント {0} はアセットへ直接書き戻せません。Prefab Stage を開いてからアタッチしてください。" },
            { "Log.HierarchyNotFound", "[ScriptBinder] [3/4 アタッチ] プレハブ {0} 内に階層 {1} が見つかりません。プレハブの構造を確認してください。" },
            { "Log.PrefabSaved", "[ScriptBinder] [4/4 バインド] プレハブアセットを保存しました：{0}" },
            { "Log.TargetDescPrefab", "プレハブアセットが見つかりません: {0}" },
            { "Log.TargetDescScene", "シーンオブジェクトが見つかりません: {0} / {1}" },
            { "Log.FieldNotFound", "[ScriptBinder] {0} にシリアライズフィールド {1} が見つかりません（元ノード：{2}）。生成コードと現在のルールが一致していない可能性があります。" },
            { "Log.ComponentNotFound", "[ScriptBinder] 子オブジェクト {0} に {1} コンポーネントが見つからないため、フィールド {2} は空のままにします。" },
            { "Log.NoSelection", "[ScriptBinder] 先にバインドする UI のルートを選択してください。" },
            { "Log.Step1Done", "[ScriptBinder] ステップ 1 完了：コード生成のみ実行しました。\n続ける場合はコンパイル完了を待ってから [步骤 3：挂载组件（选中）] と [步骤 4：重新填充引用（选中）] を実行するか、[一键 生成→编译→挂载→绑定] を使用してください。（メニュー名はローカライズされません）" },
            { "Log.MountTypeNotFound", "[ScriptBinder] [3/4 アタッチ] コンパイル済みの型 <b>{0}</b> が見つかりません。先にステップ 1 でコードを生成し、コンパイル完了を待ってください（Console のコンパイルエラーを確認）。" },
            { "Log.MountedToPrefabAsset", "[ScriptBinder] [3/4 アタッチ] プレハブアセットに <b>{0}</b> をアタッチしました：{1}" },
            { "Log.MountedToTarget", "[ScriptBinder] [3/4 アタッチ] <b>{1}</b> にコンポーネント <b>{0}</b> をアタッチしました" },
            { "Log.PipelineDone", "[ScriptBinder] バインドパイプラインがすべて完了しました。" },
            { "Log.Refilled", "[ScriptBinder] [4/4 バインド] <b>{0}</b> の参照を {1}/{2} 再設定しました" },
            { "Log.NoPendingTasks", "[ScriptBinder] 保留中のタスクはありません。" },
            { "Log.PendingTasksHeader", "[ScriptBinder] 保留中のタスク {0} 件（コンパイル状態：{1}{2}）：" },
            { "Log.PendingTaskItem", "  - {0}（{1}）：{2}" },
            { "Log.HasPendingChanges", "、コンパイル待ちの変更あり" },
            { "Log.ValidateMissingKey", "{0} に {1} がありません" },
            { "Log.ValidateExtraKey", "{0} に余分な {1} があります（英語表に存在しません）" },
            { "Log.ValidatePlaceholderMismatch", "{0} の {1} のプレースホルダーが一致しません：英語 [{2}] と {3} [{4}]" },
            { "Log.Cleared", "[ScriptBinder] 保留中のタスクをすべてクリアしました。" },
            { "Log.ModeLabel.Reference", "参照を割り当て" },
            { "Log.ModeLabel.Runtime", "ランタイムバインディング" },
            { "Log.ModeLabel.Both", "両方" },
            { "Log.Stage.AwaitCompile", "2 コンパイル待ち" },
            { "Log.Stage.Mount", "3 コンポーネントをアタッチ" },
            { "Log.Stage.Bind", "4 参照を設定" },
            { "Log.Stage.Done", "完了" },
            { "Log.Stage.Unknown", "不明" },
            { "Log.CompileState.NotStarted", "未開始" },
            { "Log.CompileState.Compiling", "コンパイル中" },
            { "Log.CompileState.Compiled", "コンパイル済み" },
            { "Log.LocalizationOk", "[ScriptBinder] ローカライズのセルフチェックに合格しました：3つの言語のキーが完全に一致しています。" },
            { "Log.LocalizationMissing", "[ScriptBinder] ローカライズの項目が不足しています（{0}）：\n{1}" },
            // ---- 生成代码里的注释 ----
            // 中国語表と同じ：ファイルヘッダの先頭4行は英語のまま固定、キーは設けない。
            { "Gen.HeaderNoEdit", "このファイルはツールが自動生成したものです。直接編集しないでください" },
            { "Gen.LogicSummary", "初回生成時のみ作成され、以後は上書きされません。ロジックコードはこのファイルに書いてください" },
            { "Gen.BindSummary.Runtime", "ランタイムバインディング：フィールドはシリアライズされず、実行時にノードパスからコンポーネントを検索して代入します。" },
            { "Gen.BindSummary.Both", "実行時フォールバック：エディタで設定済みの参照はそのまま保持し、null のフィールドのみノードパスから検索します（プレハブインスタンスなど参照が欠けたケースに対応）。" },
            { "Gen.BindCallHint", "ロジックコード（.Logic.cs）のライフサイクルで1度だけ呼び出してください：Awake / OnEnable / OnInit(userData)。" },
        };

        /// <summary>
        /// 取当前语言下的文案。key 不存在时依次回退：fallback → 键名本身（便于一眼看出漏翻）。
        /// </summary>
        public static string Get(string key, string fallback = null)
        {
            if (string.IsNullOrEmpty(key))
            {
                return fallback;
            }
            string value;
            var table = GetTable(BindRules.CurrentLanguage());
            if (table != null && table.TryGetValue(key, out value))
            {
                return value;
            }
            return fallback ?? key;
        }

        /// <summary>string.Format 版本：省得调用点再套一层。</summary>
        public static string Format(string key, params object[] args)
        {
            return string.Format(Get(key), args);
        }

        /// <summary>该键在当前语言下是否有文案（缺失时用于日志与自检）。</summary>
        public static bool HasKey(string key)
        {
            var table = GetTable(BindRules.CurrentLanguage());
            return table != null && key != null && table.ContainsKey(key);
        }

        /// <summary>当前语言下可供选择的语言列表（顺序与 Language 枚举一致），供 Inspector 下拉使用。</summary>
        public static Language[] Languages
        {
            get { return s_Languages; }
        }

        /// <summary>某个语言枚举的显示名（"中文" / "English" / "日本語"）。</summary>
        public static string LanguageLabel(Language lang)
        {
            return Get("Lang." + lang);
        }

        private static Dictionary<string, string> GetTable(Language lang)
        {
            switch (lang)
            {
                case Language.Chinese:
                    return Chinese;
                case Language.Japanese:
                    return Japanese;
                default:
                    return English;
            }
        }

        /// <summary>
        /// 自检：三个语种的键集合是否完全一致、占位符（{0} {1}…）是否对齐。
        /// 缺键会让某个语言悄悄回退到英文/键名，所以这里显式报出来（菜单 [校验多语言文案] 会调用它）。
        /// </summary>
        public static bool Validate()
        {
            var problems = new List<string>();
            foreach (var lang in s_Languages)
            {
                var table = GetTable(lang);
                foreach (var kv in English)
                {
                    if (!table.ContainsKey(kv.Key))
                    {
                        problems.Add(Format("Log.ValidateMissingKey", lang, kv.Key));
                    }
                }
                foreach (var kv in table)
                {
                    if (!English.ContainsKey(kv.Key))
                    {
                        problems.Add(Format("Log.ValidateExtraKey", lang, kv.Key));
                        continue;
                    }
                    var a = Placeholders(English[kv.Key]);
                    var b = Placeholders(kv.Value);
                    if (a != b)
                    {
                        problems.Add(Format("Log.ValidatePlaceholderMismatch", lang, kv.Key, a, lang, b));
                    }
                }
            }
            if (problems.Count == 0)
            {
                return true;
            }
            Debug.LogWarning(Format("Log.LocalizationMissing", problems.Count, string.Join("\n", problems.ToArray())));
            return false;
        }

        // 提取 {n} 占位符并排序，用于跨语言比对
        private static string Placeholders(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }
            var found = new List<string>();
            int i = 0;
            while (i < text.Length)
            {
                int open = text.IndexOf('{', i);
                if (open < 0)
                {
                    break;
                }
                int close = text.IndexOf('}', open + 1);
                if (close < 0)
                {
                    break;
                }
                found.Add(text.Substring(open, close - open + 1));
                i = close + 1;
            }
            found.Sort(StringComparer.Ordinal);
            return string.Join(",", found.ToArray());
        }

        [MenuItem("Tools/NuoYan/ScriptBinder/校验多语言文案（自检）")]
        private static void MenuValidateLocalization()
        {
            if (Validate())
            {
                Debug.Log(Format("Log.LocalizationOk"));
            }
        }
    }
}
#endif
