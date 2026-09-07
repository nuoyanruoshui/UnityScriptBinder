using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif
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
        public string Namespace = "GameLogic";
#if ODIN_INSPECTOR
        [FolderPath]
#endif
        public string SavePath = "Scripts/UI";

        [Tooltip("默认绑定模式：Reference=引用赋值（SerializeField+编辑器填充）；Runtime=运行时绑定（BindComponents 手动调用）；Both=两者兼有（填充优先，运行时兜底）。生成弹窗内可临时切换")]
        public BindMode DefaultMode = BindMode.Reference;
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
                        Debug.LogWarning(string.Format("[ScriptBinder] 检测到重复命名的绑定字段：{0}，已忽略，同名节点只绑定最早遍历到的一个。",
                            BuildPath(child)));
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
        /// <param name="mode">绑定模式（Reference/Runtime/Both）；null 时取资产默认 BindRules.DefaultMode</param>
        public void GenerateBindCode(GameObject go, string baseClass = "MonoBehaviour", List<string> extraUsings = null, bool refresh = true, BindMode? mode = null)
        {
            if (go == null)
            {
                return;
            }
            var className = go.name;
            var ns = string.IsNullOrEmpty(Namespace) ? string.Empty : Namespace;
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
            var fieldLines = new List<string>();
            foreach (Transform child in CollectBindChildren(go))
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

            // 生成主 partial 文件（字段声明）
            var gen = new StringBuilder();
            BuildHeader(gen, go);
            var usings = CollectBindUsings(go);
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
            logic.AppendLine("/// 只会在第一次生成时创建，之后不会覆盖，请在此文件中写逻辑代码");
            logic.Append(classPad);
            logic.AppendLine("/// </summary>");
            logic.Append(classPad);
            logic.AppendLine(string.Format("public partial class {0}", className));
            logic.Append(classPad);
            logic.AppendLine("{");
            logic.Append(classPad);
            logic.AppendLine("}");
            CloseNamespace(logic, ns);

            var dir = Path.Combine(Application.dataPath, SavePath);
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            var basePath = Path.Combine(dir, className);
            File.WriteAllText(basePath + ".cs", gen.ToString());
            if (!File.Exists(basePath + ".Logic.cs"))
            {
                File.WriteAllText(basePath + ".Logic.cs", logic.ToString());
            }

            if (refresh)
            {
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                Debug.Log(string.Format("[ScriptBinder] 已生成绑定代码 <b>{0}</b>（等待编译后挂载/填引用）", className));
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
                gen.AppendLine("/// 运行时绑定：字段不序列化，运行时按节点路径查找组件并赋值。");
            }
            else
            {
                gen.Append(pad);
                gen.AppendLine("/// 运行时兜底：编辑器已填充的引用保持不变，仅对为 null 的字段按节点路径查找（适配预制体实例等缺失引用场景）。");
            }
            gen.Append(pad);
            gen.AppendLine("/// 请在逻辑代码（.Logic.cs）的生命周期中调用一次：如 Awake / OnEnable / OnInit(userData)。");
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
        private static List<string> CollectBindUsings(GameObject go)
        {
            var list = new List<string> { "using UnityEngine;" };
            var rules = Instance;
            if (rules == null)
            {
                return list;
            }
            string fileNs = rules.Namespace;
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

        private static void BuildHeader(StringBuilder sb, GameObject go)
        {
            sb.AppendLine("/// <summary>");
            sb.AppendLine("/// Auto generated code for " + go.name + " by ScriptBinder");
            sb.AppendLine("/// Time: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("/// Machine: " + Environment.MachineName);
            sb.AppendLine("/// Author: NuoYan");
            sb.AppendLine("/// 此文件由工具自动生成，请勿直接修改");
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
