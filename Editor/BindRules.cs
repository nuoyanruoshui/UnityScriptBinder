#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEditor;

#if ODIN_INSPECTOR
using Sirenix.OdinInspector;
#endif
namespace NuoYan.ScriptBinder
{
    /// <summary>
    /// 绑定代码生成模式：
    /// Reference = 引用赋值（[SerializeField]，编辑器自动填充，运行时零查找）；
    /// Runtime = 运行时绑定（不序列化，生成 BindComponents() 由开发者手动调用，运行时按节点路径查找）；
    /// Both = 两者兼有（序列化 + 编辑器填充优先，BindComponents() 仅对为 null 的字段运行时兜底）。
    /// </summary>
    public enum BindMode
    {
        Reference = 0,
        Runtime = 1,
        Both = 2,
    }
    public enum SaveFileMode
    {
        /// <summary>
        /// 将生成的文件写入默认或者指定的文件夹中
        /// </summary>
        FileByFile,
        /// <summary>
        /// 将生成的文件写入同名文件夹中，如果文件夹不存在则创建
        /// </summary>
        FolderByFolder,
    }
    public enum VisibleType
    {
        Private,
        Protected,
        Public,
    }
    [System.Serializable]
    public class BindRule
    {
        public string Prefix;
#if ODIN_INSPECTOR
        [ValueDropdown(nameof(GetDropdownItems))]
#endif
        public string Type;
#if ODIN_INSPECTOR
        private ValueDropdownList<string> GetDropdownItems()
        {
            var list = new ValueDropdownList<string>();
            foreach (var type in BindRules.Instance.BindTypes)
            {
                list.Add(type);
            }
            return list;
        }
#endif
    }
    public enum Language
    {
        English = 0,
        Chinese = 1,
        Japanese = 2,
    }
    [System.Serializable]
    public class FieldVisibleRule
    {
        public string Prefix;
        public VisibleType Visible;
    }

    // [CreateAssetMenu(fileName = "BindRules", menuName = "ScriptBinder/BindRules")]
    public class BindRules : ScriptableObject
    {
        private static BindRules m_Instance;
        public static BindRules Instance
        {
            get
            {
                if (m_Instance == null)
                {
                    m_Instance = Resources.Load<BindRules>("ScriptBinder/BindRules");
                    if (m_Instance == null)
                    {
                        m_Instance = ScriptableObject.CreateInstance<BindRules>();
                        if (!Directory.Exists("Assets/Resources/ScriptBinder"))
                        {
                            Directory.CreateDirectory("Assets/Resources/ScriptBinder");
                        }
                        AssetDatabase.CreateAsset(m_Instance, "Assets/Resources/ScriptBinder/BindRules.asset");
                        AssetDatabase.SaveAssets();
                        AssetDatabase.Refresh();
                    }
                }
                return m_Instance;
            }
        }

        public static Language CurrentLanguage()
        {
            var rules = m_Instance != null ? m_Instance : LoadRulesOnceForLanguage();
            return rules != null ? rules.DisplayLanguage : Language.English;
        }

        private static BindRules s_LangRules;
        private static bool s_LangLoadAttempted;

        private static BindRules LoadRulesOnceForLanguage()
        {
            if (!s_LangLoadAttempted)
            {
                s_LangLoadAttempted = true;
                s_LangRules = Resources.Load<BindRules>("ScriptBinder/BindRules");
            }
            return s_LangRules;
        }

        public string Namespace = "GameLogic";
#if ODIN_INSPECTOR
        [FolderPath]
#endif
        public string SavePath = "Scripts/UI";

        [Tooltip("默认绑定模式：Reference=引用赋值（SerializeField+编辑器填充）；Runtime=运行时绑定（BindComponents 手动调用）；Both=两者兼有（填充优先，运行时兜底）。生成弹窗内可临时切换")]
        public BindMode DefaultMode = BindMode.Reference;
        [Tooltip("生成文件时写入的模式：FileByFile=将生成的文件写入默认或者指定的文件夹中；FolderByFolder=将生成的文件写入同名文件夹中，如果文件夹不存在则创建")]
        public SaveFileMode SaveFileMode = SaveFileMode.FileByFile;
#if ODIN_INSPECTOR
        [EnumToggleButtons]
#endif
        [Tooltip("插件界面与日志使用的语言")]
        public Language DisplayLanguage = Language.English;
#if ODIN_INSPECTOR
        [SerializeField]
        public List<string> BindTypes = new List<string>()
        {
            "UnityEngine.UI.Text",
            "TMPro.TMP_Text",
            "UnityEngine.UI.Image",
            "UnityEngine.UI.Button",
            "UnityEngine.UI.Toggle",
            "UnityEngine.UI.Slider",
            "UnityEngine.UI.Scrollbar",
            "UnityEngine.UI.Dropdown",
            "UnityEngine.UI.InputField",
            "UnityEngine.RectTransform",
            "UnityEngine.Transform",
            "UnityEngine.GameObject",
        };
#endif
        public List<FieldVisibleRule> FieldVisibleRules = new List<FieldVisibleRule>()
        {
            new FieldVisibleRule() { Prefix = "m_", Visible = VisibleType.Private },
            new FieldVisibleRule() { Prefix = "M_", Visible = VisibleType.Protected },
            new FieldVisibleRule() { Prefix = "_", Visible = VisibleType.Public },
        };

        public List<BindRule> Rules = new List<BindRule>()
        {
            new BindRule() { Prefix = "img", Type = "UnityEngine.UI.Image" },
            new BindRule() { Prefix = "btn", Type = "UnityEngine.UI.Button" },
            new BindRule() { Prefix = "tgl", Type = "UnityEngine.UI.Toggle" },
            new BindRule() { Prefix = "sld", Type = "UnityEngine.UI.Slider" },
            new BindRule() { Prefix = "sbr", Type = "UnityEngine.UI.Scrollbar" },
            new BindRule() { Prefix = "drp", Type = "UnityEngine.UI.Dropdown" },
            new BindRule() { Prefix = "ipt", Type = "UnityEngine.UI.InputField" },
            new BindRule() { Prefix = "txt", Type = "UnityEngine.UI.Text" },
            new BindRule() { Prefix = "tmp", Type = "TMPro.TMP_Text" },
            new BindRule() { Prefix = "rect", Type = "UnityEngine.RectTransform" },
            new BindRule() { Prefix = "trans", Type = "UnityEngine.Transform" },
            new BindRule() { Prefix = "go", Type = "UnityEngine.GameObject" },
        };

        /// <summary>该子物体是否会被生成为绑定字段（前缀命中 FieldVisibleRules）</summary>
        public bool IsBindField(string childName)
        {
            return MatchRule(childName);
        }

        /// <summary>该子物体的字段类型表达式（BindRule.Type 原文，如 UnityEngine.UI.Image / 自定义组件全名）</summary>
        public string GetBindFieldTypeName(string childName)
        {
            return GetFieldType(childName);
        }

        /// <summary>
        /// 生成字段标识符：可见性前缀 + 帕斯卡（驼峰）化的语义名。
        /// 例如 m_imgIcon -> m_ImgIcon，m_img -> m_Img，M_btnClose -> M_BtnClose。
        /// </summary>
        public string GetBindFieldName(string childName)
        {
            return GetFieldPrefix(childName) + ToPascalCase(GetFieldName(childName));
        }

        /// <summary>该子物体的字段可见性关键字（private/protected/public），与代码生成一致，供 UI 预览使用。</summary>
        public string GetBindFieldVisible(string childName)
        {
            return GetFieldVisible(childName);
        }

        // 命中的可见性前缀（例如 "m_" / "M_" / "_"），未命中返回空
        private string GetFieldPrefix(string name)
        {
            foreach (var rule in FieldVisibleRules)
            {
                if (name.StartsWith(rule.Prefix))
                {
                    return rule.Prefix;
                }
            }
            return string.Empty;
        }

        // 按 "_" 与 大小写转折 分词，每词首字母大写：imgIcon/img_icon/img -> ImgIcon/ImgIcon/Img
        private static string ToPascalCase(string name)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                if (c == '_')
                {
                    continue;
                }
                bool startOfWord = i == 0
                                   || name[i - 1] == '_'
                                   || (char.IsLower(name[i - 1]) && char.IsUpper(c));
                sb.Append(startOfWord ? char.ToUpperInvariant(c) : c);
            }
            return sb.ToString();
        }

        /// <summary>
        /// 收集 go 下所有需要绑定的后代（递归遍历全部层级，先序）。
        /// 同名节点只保留最早遍历到的一个，避免生成重复字段导致编译失败。
        /// </summary>
        public List<Transform> CollectBindChildren(GameObject go)
        {
            var list = new List<Transform>();
            var seen = new HashSet<string>();
            if (go != null)
            {
                CollectBindChildrenRecursive(go.transform, list, seen);
            }
            return list;
        }

        private void CollectBindChildrenRecursive(Transform parent, List<Transform> list, HashSet<string> seen)
        {
            foreach (Transform child in parent)
            {
                if (child == null)
                {
                    continue;
                }
                if (IsBindField(child.name))
                {
                    if (seen.Add(GetBindFieldName(child.name)))
                    {
                        list.Add(child);
                    }
                    else
                    {
                        Debug.LogWarning(LocalizationConstant.Format("Log.DupField", BuildPath(child)));
                    }
                    // 容器型绑定（规则类型解析为 RectTransform / GameObject，如默认的 rect / go 前缀）
                    // 作为边界：自身绑上字段，但不再深入其子节点
                    if (IsContainerBoundary(GetFieldType(child.name)))
                    {
                        continue;
                    }
                }
                // 其余情况继续向下递归所有层级
                CollectBindChildrenRecursive(child, list, seen);
            }
        }

        private static string BuildPath(Transform t)
        {
            var names = new List<string>();
            while (t != null)
            {
                names.Add(t.name);
                t = t.parent;
            }
            names.Reverse();
            return string.Join("/", names);
        }

        // 获得字段类型表达式（BindRule.Type 原文），例如 m_btnStart 命中 btn 规则 -> UnityEngine.UI.Button
        private string GetFieldType(string name)
        {
            foreach (var rule in Rules)
            {
                if (GetFieldName(name).StartsWith(rule.Prefix))
                {
                    return string.IsNullOrWhiteSpace(rule.Type) ? "UnityEngine.GameObject" : rule.Type.Trim();
                }
            }
            // 未命中任何类型前缀：默认绑整个子物体（与旧版 GameObject 兜底一致）
            return "UnityEngine.GameObject";
        }
        //根据前缀获取字段的可见性 例如 m_BtnStart -> private
        private string GetFieldVisible(string name)
        {
            foreach (var rule in FieldVisibleRules)
            {
                if (name.StartsWith(rule.Prefix))
                {
                    return rule.Visible.ToString().ToLower();
                }
            }
            return "private";
        }
        //获得去除前缀的字段名 例如 m_BtnStart -> BtnStart
        private string GetFieldName(string name)
        {
            foreach (var rule in FieldVisibleRules)
            {
                if (name.StartsWith(rule.Prefix))
                {
                    return name.Substring(rule.Prefix.Length);
                }
            }
            return name;
        }
        // 前缀命中 FieldVisibleRules 才算绑定字段
        private bool MatchRule(string name)
        {
            foreach (var item in FieldVisibleRules)
            {
                if (name.StartsWith(item.Prefix))
                {
                    return true;
                }
            }
            return false;
        }

        // =====================================================================
        // 类型表达式解析：BindRule.Type 是字符串（可全限定名，也可简单名），
        // 由这里解析成真实 Type，供容器边界 / using 收集 / 编辑器填充 / 运行时查找共用。
        // =====================================================================

        private static readonly Dictionary<string, Type> s_TypeCache = new Dictionary<string, Type>();

        /// <summary>
        /// 把 BindRule.Type 的类型表达式解析为真实 Type。
        /// 含 '.' 视为全限定名（跨程序集查找）；简单名在所有已加载程序集中按名匹配。
        /// 找不到返回 null（生成时按原文字面量输出，编译错误会提示用户修正）。
        /// </summary>
        public static Type ResolveRuleType(string typeExpr)
        {
            if (string.IsNullOrWhiteSpace(typeExpr))
            {
                return null;
            }
            typeExpr = typeExpr.Trim();
            if (s_TypeCache.TryGetValue(typeExpr, out var cached))
            {
                return cached;
            }
            Type result = null;
            if (typeExpr.IndexOf('.') >= 0)
            {
                result = Type.GetType(typeExpr);
                if (result == null)
                {
                    foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        result = asm.GetType(typeExpr);
                        if (result != null)
                        {
                            break;
                        }
                    }
                }
            }
            else
            {
                // 简单名：按类名在已加载程序集里查找
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (asm.IsDynamic)
                    {
                        continue;
                    }
                    Type[] types;
                    try
                    {
                        types = asm.GetTypes();
                    }
                    catch (Exception)
                    {
                        continue; // 某些程序集无法反射（如内置/动态），跳过
                    }
                    foreach (var t in types)
                    {
                        if (t != null && t.Name == typeExpr)
                        {
                            result = t;
                            break;
                        }
                    }
                    if (result != null)
                    {
                        break;
                    }
                }
            }
            s_TypeCache[typeExpr] = result;
            return result;
        }

        /// <summary>去掉命名空间后的展示名（UnityEngine.UI.Image -> Image），用于 UI 预览等。</summary>
        public static string GetTypeDisplayName(string typeExpr)
        {
            if (string.IsNullOrWhiteSpace(typeExpr))
            {
                return typeExpr;
            }
            int idx = typeExpr.LastIndexOf('.');
            return idx >= 0 ? typeExpr.Substring(idx + 1) : typeExpr;
        }

        /// <summary>类型解析为 GameObject / RectTransform 的规则视为容器边界（绑自身、不再深入子节点）。</summary>
        private bool IsContainerBoundary(string typeExpr)
        {
            var t = ResolveRuleType(typeExpr);
            if (t != null)
            {
                return t == typeof(GameObject) || t == typeof(RectTransform);
            }
            // 解析不到时按名字兜底判断
            return typeExpr != null && (typeExpr.EndsWith("GameObject", StringComparison.Ordinal)
                                        || typeExpr.EndsWith("RectTransform", StringComparison.Ordinal));
        }

#if UNITY_EDITOR
        /// <param name="baseClass">自定义父类表达式（留空/空白默认 MonoBehaviour）</param>
        /// <param name="extraUsings">额外追加到生成文件头部的 using 行（如父类所在命名空间）</param>
        /// <param name="refresh">是否立即刷新资源（触发编译）。批量生成时传 false，由调用方统一刷新一次，避免触发多次编译</param>
        /// <param name="cusns">本次生成使用的命名空间；空白时回退资产上的 Namespace</param>
        /// <param name="cussf">本次生成写入的目录（相对 Assets，如 "Scripts/UI"）；空白时回退资产上的 SavePath</param>
        /// <param name="saveFileMode">本次生成的文件布局；null 时回退资产上的 SaveFileMode</param>
        /// <param name="mode">绑定模式（Reference/Runtime/Both）；null 时取资产默认 BindRules.DefaultMode</param>
        public void GenerateBindCode(GameObject go, string baseClass = "MonoBehaviour", List<string> extraUsings = null, bool refresh = true, string cusns = null, string cussf = null, SaveFileMode? saveFileMode = null, BindMode? mode = null, bool sameInAPart = false)
        {
            if (go == null)
            {
                return;
            }
            var className = go.name;
            // 弹窗/调用方传空白 → 回退资产默认；资产上也是空白时才真的不写命名空间 / 落在 Assets 根
            var ns = string.IsNullOrWhiteSpace(cusns) ? (Namespace ?? string.Empty).Trim() : cusns.Trim();
            var saveRoot = string.IsNullOrWhiteSpace(cussf) ? (SavePath ?? string.Empty).Trim() : cussf.Trim();
            var fileMode = saveFileMode ?? SaveFileMode;
            if (string.IsNullOrWhiteSpace(baseClass))
            {
                baseClass = "MonoBehaviour";
            }
            else
            {
                baseClass = baseClass.Trim();
            }
            // 绑定模式：调用方未指定时取资产默认
            var bindMode = mode ?? DefaultMode;

            // 缩进层级：类所在层级 = 有无命名空间；字段比类多一级
            var classLevel = string.IsNullOrEmpty(ns) ? 0 : 1;
            var classPad = Pad(classLevel);
            var bodyPad = Pad(classLevel + 1);

            // 递归所有后代，前缀命中 FieldVisibleRules 的才会生成字段（同名只保留首个）
            // SameInAPart：把相同类型的字段排布到一起，逐组加 [Header("短类型名")]。
            // Runtime 模式字段不序列化、Inspector 根本看不到该字段，[Header] 是死代码，
            // 所以那里只按类型排序、不加 Header。
            var groups = CollectBindFieldGroups(go, sameInAPart);
            bool withHeader = sameInAPart && bindMode != BindMode.Runtime;

            string groupHeader = "Header";
#if ODIN_INSPECTOR
            groupHeader = "Title";
#endif

            var fieldLines = new List<string>();
            foreach (var group in groups)
            {
                if (group.Count == 0)
                {
                    continue;
                }
                if (withHeader)
                {
                    if (fieldLines.Count > 0)
                    {
                        fieldLines.Add(string.Empty); // 组间空一行
                    }
                    fieldLines.Add($"[{groupHeader}(\"{GetTypeDisplayName(GetFieldType(group[0].name))}\")]");
                }
                foreach (Transform child in group)
                {
                    string visibility = GetFieldVisible(child.name);
                    string typeName = GetFieldType(child.name);
                    string fieldName = GetBindFieldName(child.name);
                    if (bindMode == BindMode.Runtime)
                    {
                        // 运行时绑定：字段不序列化，由 BindComponents() 在运行时查找赋值
                        fieldLines.Add(string.Format("{0} {1} {2};", visibility, typeName, fieldName));
                    }
                    else
                    {
                        // 引用赋值 / 两者兼有：序列化字段，编辑器填充引用（Both 的运行时兜底见 BindComponents）
                        fieldLines.Add(string.Format("[SerializeField] {0} {1} {2} = null;", visibility, typeName, fieldName));
                    }
                }
            }

            // 生成主 partial 文件（字段声明）
            var gen = new StringBuilder();
            BuildHeader(gen, go);
            var usings = CollectBindUsings(go, ns);
            var writtenUsings = new HashSet<string>(usings);
            foreach (var usingLine in usings)
            {
                gen.AppendLine(usingLine);
            }
            if (extraUsings != null)
            {
                foreach (var usingLine in extraUsings)
                {
                    if (writtenUsings.Add(usingLine))
                    {
                        gen.AppendLine(usingLine);
                    }
                }
            }
            gen.AppendLine();
            OpenNamespace(gen, ns);
            gen.Append(classPad);
            gen.AppendLine(string.Format("public partial class {0} : {1}", className, baseClass));
            gen.Append(classPad);
            gen.AppendLine("{");
            foreach (var line in fieldLines)
            {
                if (line.Length == 0)
                {
                    gen.AppendLine(); // 分组之间的空行不补缩进，避免行尾空白
                    continue;
                }
                gen.Append(bodyPad);
                gen.AppendLine(line);
            }
            if (bindMode != BindMode.Reference)
            {
                // Runtime / Both：追加运行时查找绑定的 BindComponents()
                AppendBindComponents(gen, go.transform, bindMode, classLevel);
            }
            gen.Append(classPad);
            gen.AppendLine("}");
            CloseNamespace(gen, ns);

            // 生成空的 Logic partial 文件（给开发者在里面写逻辑）
            var logic = new StringBuilder();
            OpenNamespace(logic, ns);
            logic.Append(classPad);
            logic.AppendLine("/// <summary>");
            logic.Append(classPad);
            logic.AppendLine("/// " + LocalizationConstant.Get("Gen.LogicSummary"));
            logic.Append(classPad);
            logic.AppendLine("/// </summary>");
            logic.Append(classPad);
            logic.AppendLine(string.Format("public partial class {0}", className));
            logic.Append(classPad);
            logic.AppendLine("{");
            logic.Append(classPad);
            logic.AppendLine("}");
            CloseNamespace(logic, ns);

            var rootDir = Path.Combine(Application.dataPath, NormalizeRelativeDir(saveRoot));
            if (!Directory.Exists(rootDir))
            {
                Directory.CreateDirectory(rootDir);
            }

            // 不含扩展名的写入基路径：FileByFile = root/类名；FolderByFolder = root/类名/类名
            string basePath;
            if (fileMode == SaveFileMode.FolderByFolder)
            {
                var subDir = Path.Combine(rootDir, className);
                if (!Directory.Exists(subDir))
                {
                    Directory.CreateDirectory(subDir);
                }
                basePath = Path.Combine(subDir, className);
            }
            else
            {
                basePath = Path.Combine(rootDir, className);
            }

            // 切换布局 / 改过生成文件夹后，旧位置可能残留同名生成文件 → 与本次生成构成重复定义（CS0101）
            WarnStaleGeneratedFiles(fileMode, className, basePath);

            // 直接写盘（不比对内容）：调用方按"每轮生成都可能有变化"来安排编译等待，
            // 同时文件 mtime 始终反映"最后一次生成时间"。
            File.WriteAllText(basePath + ".cs", gen.ToString());
            // 逻辑文件只在首次生成时创建，之后不覆盖（开发者手写的逻辑不能丢）
            if (!File.Exists(basePath + ".Logic.cs"))
            {
                File.WriteAllText(basePath + ".Logic.cs", logic.ToString());
            }
            RememberGenPath(className, basePath);

            if (refresh)
            {
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                Debug.Log(LocalizationConstant.Format("Log.Generated", className));
            }
        }

        // 生成目录写法归一：统一斜杠、去掉首尾斜杠（"Scripts/UI/" 与 "\\Scripts\\UI" 等价）
        private static string NormalizeRelativeDir(string dir)
        {
            if (string.IsNullOrWhiteSpace(dir))
            {
                return string.Empty;
            }
            return dir.Replace('\\', '/').Trim().Trim('/');
        }

        /// <summary>
        /// 返回字段的分组顺序（生成弹窗预览与代码生成共用，保证“所见即所生成”）：
        /// SameInAPart=false → 单组，即层级先序；true → 按类型分组（组间 = 类型首次出现顺序，组内保持先序）。
        /// </summary>
        public List<List<Transform>> CollectBindFieldGroups(GameObject go, bool sameInAPart)
        {
            var children = CollectBindChildren(go);
            return sameInAPart ? GroupByFieldType(children) : SingleGroup(children);
        }
        private static List<List<Transform>> SingleGroup(List<Transform> children)
        {
            return new List<List<Transform>> { children };
        }

        /// <summary>
        /// 按字段类型表达式（BindRule.Type）分组。
        /// 组间顺序 = 该类型首次出现的先后顺序（与层级先序一致，读起来和节点树同序）；
        /// 组内保持先序，因此组内字段的相对顺序不变。
        /// </summary>
        private List<List<Transform>> GroupByFieldType(List<Transform> children)
        {
            var order = new List<string>();
            var map = new Dictionary<string, List<Transform>>();
            foreach (var child in children)
            {
                var typeName = GetFieldType(child.name);
                if (!map.TryGetValue(typeName, out var list))
                {
                    list = new List<Transform>();
                    map[typeName] = list;
                    order.Add(typeName);
                }
                list.Add(child);
            }
            var groups = new List<List<Transform>>();
            foreach (var key in order)
            {
                groups.Add(map[key]);
            }
            return groups;
        }

        // 绝对路径 → 相对 Assets 的正斜杠路径（用于日志与 EditorPrefs 记录）
        private static string ToAssetRelativePath(string absBasePath)
        {
            var abs = absBasePath.Replace('\\', '/');
            var root = Application.dataPath.Replace('\\', '/').TrimEnd('/');
            if (abs.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase))
            {
                return abs.Substring(root.Length + 1);
            }
            return abs; // 工程外（理论上不会出现），原样记录以便照常提示
        }

        private const string GenPathPrefsPrefix = "ScriptBinder.GenPath."; // 记录每个类上次生成位置，便于发现改目录后的残留
        private static readonly HashSet<string> s_StaleKept = new HashSet<string>(); // 本会话中用户已选择“保留”的残留文件，不再重复弹窗

        // 记录本次生成位置（不含扩展名，相对 Assets）
        private static void RememberGenPath(string className, string absBasePath)
        {
            EditorPrefs.SetString(GenPathPrefsPrefix + className, ToAssetRelativePath(absBasePath));
        }

        /// <summary>
        /// 检测“另一种布局 / 上次生成位置”下残留的同名生成文件：它与本次生成的文件同名，
        /// 同时存在会直接导致 CS0101 重复定义编译失败。弹窗由用户决定是否删除，本工具不擅自删用户的文件。
        /// </summary>
        private static void WarnStaleGeneratedFiles(SaveFileMode fileMode, string className, string currentBasePath)
        {
            var currentDir = Path.GetDirectoryName(currentBasePath) ?? string.Empty;
            // 同一个生成根下的另一种布局：
            //   本次 FolderByFolder（root/类名/类名）→ 旧文件在上一层 root/类名
            //   本次 FileByFile（root/类名）→ 旧文件在同名子文件夹 root/类名/类名
            var staleCandidates = new List<string>
            {
                fileMode == SaveFileMode.FolderByFolder ? currentDir : Path.Combine(currentBasePath, className)
            };
            // 改过“生成文件夹”的情况：上次生成位置完全在别的目录，同样会残留
            var lastRel = EditorPrefs.GetString(GenPathPrefsPrefix + className, string.Empty);
            if (!string.IsNullOrEmpty(lastRel))
            {
                var lastAbs = Path.Combine(Application.dataPath, lastRel.Replace('/', Path.DirectorySeparatorChar));
                if (!string.Equals(lastAbs, currentBasePath, StringComparison.OrdinalIgnoreCase))
                {
                    staleCandidates.Add(lastAbs);
                }
            }

            foreach (var stale in staleCandidates)
            {
                string csFile = stale + ".cs";
                string logicFile = stale + ".Logic.cs";
                if (!File.Exists(csFile) && !File.Exists(logicFile))
                {
                    s_StaleKept.Remove(stale); // 文件已不在，下次真的出现时重新提示
                    continue;
                }
                if (!s_StaleKept.Add(stale))
                {
                    continue; // 本次会话已问过且用户选择保留，不再打扰
                }

                var relCs = ToAssetRelativePath(stale) + ".cs";
                string msg = LocalizationConstant.Format("Dialog.StaleFile", relCs);
                if (!LooksToolGenerated(csFile))
                {
                    msg += LocalizationConstant.Get("Dialog.StaleFileNotToolGenerated");
                }
                if (EditorUtility.DisplayDialog(LocalizationConstant.Get("Dialog.StaleFileTitle"), msg,
                    LocalizationConstant.Get("Btn.DeleteOldFile"), LocalizationConstant.Get("Btn.KeepOldFile")))
                {
                    // 用 & 而非 &&：删不掉也要把能删的都试一遍，再统一汇报
                    bool ok = TryDeleteFile(csFile) & TryDeleteFile(csFile + ".meta")
                              & TryDeleteFile(logicFile) & TryDeleteFile(logicFile + ".meta");
                    RemoveEmptyDir(Path.GetDirectoryName(stale), currentDir);
                    s_StaleKept.Remove(stale);
                    if (ok)
                    {
                        Debug.Log(LocalizationConstant.Format("Log.StaleFileDeleted", relCs));
                    }
                    else
                    {
                        Debug.LogWarning(LocalizationConstant.Format("Log.StaleFileDeleteFailed", relCs));
                    }
                }
            }
        }

        // 生成文件头部带 “Auto generated code for xxx by ScriptBinder” 标记；用于删除前的安全提示
        private static bool LooksToolGenerated(string csFile)
        {
            if (!File.Exists(csFile))
            {
                return true; // 只有 .Logic.cs 存在时按工具生成处理
            }
            try
            {
                return File.ReadAllText(csFile).Contains("by ScriptBinder");
            }
            catch (Exception)
            {
                return true;
            }
        }

        /// <summary>
        /// 删除单个文件；不存在视为成功。
        /// 未走 AssetDatabase 而直接删文件（本工具的批量写法），所以：
        ///   - 版本控制常把脚本置为只读（如 Perforce），先清掉只读位，否则 File.Delete 会抛异常；
        ///   - 文件被 IDE 占用等情况下不能让异常冒泡打断整批绑定，这里吞掉并返回失败由调用方汇报。
        /// .meta 一并删除是 Unity 手册对“在编辑器外删除资产”的要求，否则刷新时会留一条
        /// “A meta data file (.meta) exists but its asset ... can't be found” 的警告（Unity 之后会自行清理）。
        /// </summary>
        private static bool TryDeleteFile(string path)
        {
            if (!File.Exists(path))
            {
                return true;
            }
            try
            {
                var attrs = File.GetAttributes(path);
                if ((attrs & FileAttributes.ReadOnly) != 0)
                {
                    File.SetAttributes(path, attrs & ~FileAttributes.ReadOnly);
                }
                File.Delete(path);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning(LocalizationConstant.Format("Log.DeleteFileFailed", path, e.Message));
                return false;
            }
        }

        // 删除空目录：仅在 Assets 内、目录确为空、且不是生成根（keepDir）本身时才删，顺带清掉 .meta
        private static void RemoveEmptyDir(string absDir, string keepDir)
        {
            if (string.IsNullOrEmpty(absDir) || !Directory.Exists(absDir))
            {
                return;
            }
            var dir = absDir.Replace('\\', '/').TrimEnd('/');
            var assetsRoot = Application.dataPath.Replace('\\', '/').TrimEnd('/');
            if (!dir.StartsWith(assetsRoot + "/", StringComparison.OrdinalIgnoreCase))
            {
                return; // 不在 Assets 内，不动
            }
            // 生成根是用户自己定的目录，即使空了也保留，不替用户做这个决定
            if (string.Equals(dir, keepDir.Replace('\\', '/').TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
            if (Directory.GetFileSystemEntries(dir).Length > 0)
            {
                return; // 还有别的东西，保留
            }
            try
            {
                Directory.Delete(dir);
                TryDeleteFile(dir + ".meta");
            }
            catch (Exception e)
            {
                Debug.LogWarning(LocalizationConstant.Format("Log.CleanEmptyDirFailed", dir, e.Message));
            }
        }

        // 生成 Runtime/Both 模式下的 BindComponents()：按节点相对路径在运行时查找并赋值
        private void AppendBindComponents(StringBuilder gen, Transform root, BindMode bindMode, int classLevel)
        {
            var pad = Pad(classLevel + 1);
            var bodyPad = Pad(classLevel + 2);

            gen.AppendLine();
            gen.Append(pad);
            gen.AppendLine("/// <summary>");
            gen.Append(pad);
            if (bindMode == BindMode.Runtime)
            {
                gen.Append(pad);
                gen.AppendLine("/// " + LocalizationConstant.Get("Gen.BindSummary.Runtime"));
            }
            else
            {
                gen.Append(pad);
                gen.AppendLine("/// " + LocalizationConstant.Get("Gen.BindSummary.Both"));
            }
            gen.Append(pad);
            gen.AppendLine("/// " + LocalizationConstant.Get("Gen.BindCallHint"));
            gen.Append(pad);
            gen.AppendLine("/// </summary>");
            gen.Append(pad);
            gen.AppendLine("public void BindComponents()");
            gen.Append(pad);
            gen.AppendLine("{");
            foreach (Transform child in CollectBindChildren(root.gameObject))
            {
                string fieldName = GetBindFieldName(child.name);
                string lookup = BuildRuntimeLookup(child, root, GetFieldType(child.name));
                gen.Append(bodyPad);
                if (bindMode == BindMode.Both)
                {
                    gen.AppendLine(string.Format("if ({0} == null) {{ {0} = {1}; }}", fieldName, lookup));
                }
                else
                {
                    gen.AppendLine(string.Format("{0} = {1};", fieldName, lookup));
                }
            }
            gen.Append(pad);
            gen.AppendLine("}");
        }

        // 单条运行时查找表达式：按相对根节点的路径 transform.Find(...)
        // GameObject -> Find 后取 gameObject；Transform -> Find 直赋；
        // 其余组件类型（RectTransform / TMP_Text / 自定义组件…）-> Find 后 GetComponent<T>（T 为规则原文）
        private string BuildRuntimeLookup(Transform child, Transform root, string rawType)
        {
            string path = BuildRelativePath(child, root);
            string typeExpr = string.IsNullOrWhiteSpace(rawType) ? "UnityEngine.GameObject" : rawType.Trim();
            var t = ResolveRuleType(typeExpr);
            if (t == typeof(GameObject))
            {
                return string.Format("transform.Find(\"{0}\")?.gameObject", path);
            }
            if (t == typeof(Transform))
            {
                return string.Format("transform.Find(\"{0}\")", path);
            }
            return string.Format("transform.Find(\"{0}\")?.GetComponent<{1}>()", path, typeExpr);
        }

        // 从 child 向上到 root（不含 root）拼接 '/' 相对路径
        private string BuildRelativePath(Transform child, Transform root)
        {
            var names = new List<string>();
            var t = child;
            while (t != null && t != root)
            {
                names.Add(t.name);
                t = t.parent;
            }
            names.Reverse();
            return string.Join("/", names);
        }

        // 收集生成文件需要的 using：
        // 规则类型是全限定名（含 '.'）→ 无需 using，按原文写入；
        // 规则类型是简单名 → 按其解析出的命名空间补 using（同文件命名空间 / UnityEngine 内置的除外）。
        // fileNs 必须传本次实际生效的命名空间（弹窗可覆盖资产默认值），否则会漏 using 或补多余的 using。
        private static List<string> CollectBindUsings(GameObject go, string fileNs)
        {
            var list = new List<string> { "using UnityEngine;" };
            var rules = Instance;
            if (rules == null)
            {
                return list;
            }
            var added = new HashSet<string> { "using UnityEngine;" };
            foreach (Transform child in rules.CollectBindChildren(go))
            {
                string raw = rules.GetBindFieldTypeName(child.name);
                if (string.IsNullOrWhiteSpace(raw) || raw.Trim().IndexOf('.') >= 0)
                {
                    continue; // 空 / 全限定名：无需 using
                }
                var t = ResolveRuleType(raw);
                if (t == null || string.IsNullOrEmpty(t.Namespace))
                {
                    continue; // 解析不到或全局命名空间：保持原样输出，编译错误可见
                }
                if (t.Namespace == "UnityEngine" || (!string.IsNullOrEmpty(fileNs) && t.Namespace == fileNs))
                {
                    continue; // 已内置 using UnityEngine；同文件命名空间无需 using
                }
                string line = "using " + t.Namespace + ";";
                if (added.Add(line))
                {
                    list.Add(line);
                }
            }
            return list;
        }

        // 生成文件头部。注意：
        //   - "Auto generated code for ... by ScriptBinder" 这一行是 LooksToolGenerated 用来识别“本工具生成”的标记，
        //     三种语言下都保持英文原文，免得标记随语言变化而失效；
        //   - Machine / Author 是结构化元信息，语言无关，也保持固定；
        //   - 只有“请勿直接修改”那句是人类读的说明，跟随语言。
        // 刻意不写生成时间：时间戳每次都不一样，会让"内容未变化就不重写"永远失效（每次绑定都重导入 + 域重载）。
        // 生成内容必须只由"节点命名 + 规则 + 本次参数"决定，才谈得上可复现。要看某文件何时生成，看文件 mtime / git。
        private static void BuildHeader(StringBuilder sb, GameObject go)
        {
            sb.AppendLine("/// <summary>");
            sb.AppendLine("/// Auto generated code for " + go.name + " by ScriptBinder");
            sb.AppendLine("/// Machine: " + Environment.MachineName);
            sb.AppendLine("/// Author: NuoYan");
            sb.AppendLine("/// " + LocalizationConstant.Get("Gen.HeaderNoEdit"));
            sb.AppendLine("/// </summary>");
        }

        private static void OpenNamespace(StringBuilder sb, string ns)
        {
            if (string.IsNullOrEmpty(ns))
            {
                return;
            }
            sb.AppendLine("namespace " + ns);
            sb.AppendLine("{");
        }

        private static void CloseNamespace(StringBuilder sb, string ns)
        {
            if (string.IsNullOrEmpty(ns))
            {
                return;
            }
            sb.AppendLine("}");
        }

        // 生成缩进字符串：每级 4 个空格
        private static string Pad(int level)
        {
            if (level <= 0)
            {
                return string.Empty;
            }
            return new string(' ', level * 4);
        }
#endif
    }
}
#endif