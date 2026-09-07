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
    public enum BindType
    {
        Text,
        TMP_Text,
        Image,
        Button,
        Toggle,
        Slider,
        Scrollbar,
        Dropdown,
        InputField,
        RectTransform,
        Transform,
        GameObject,
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
        public BindType Type;
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

        public List<FieldVisibleRule> FieldVisibleRules = new List<FieldVisibleRule>()
    {
        new FieldVisibleRule() { Prefix = "m_", Visible = VisibleType.Private },
        new FieldVisibleRule() { Prefix = "M_", Visible = VisibleType.Protected },
        new FieldVisibleRule() { Prefix = "_", Visible = VisibleType.Public },
    };
        public List<BindRule> Rules = new List<BindRule>()
    {
        new BindRule() { Prefix = "img", Type = BindType.Image },
        new BindRule() { Prefix = "btn", Type = BindType.Button },
        new BindRule() { Prefix = "tgl", Type = BindType.Toggle },
        new BindRule() { Prefix = "sld", Type = BindType.Slider },
        new BindRule() { Prefix = "sbr", Type = BindType.Scrollbar },
        new BindRule() { Prefix = "drp", Type = BindType.Dropdown },
        new BindRule() { Prefix = "ipt", Type = BindType.InputField },
        new BindRule() { Prefix = "txt", Type = BindType.Text },
        new BindRule() { Prefix = "tmp", Type = BindType.TMP_Text },
        new BindRule() { Prefix = "rect", Type = BindType.RectTransform },
        new BindRule() { Prefix = "trans", Type = BindType.Transform },
        new BindRule() { Prefix = "go", Type = BindType.GameObject },
    };

        /// <summary>该子物体是否会被生成为绑定字段（前缀命中 FieldVisibleRules）</summary>
        public bool IsBindField(string childName)
        {
            return MatchRule(childName);
        }

        /// <summary>该子物体的字段类型名，例如 m_imgIcon -> Image</summary>
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
                    // 容器型绑定（rect / go）作为边界：自身绑上字段，但不再深入其子节点
                    var type = GetFieldType(child.name);
                    if (type == nameof(BindType.RectTransform) || type == nameof(BindType.GameObject))
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

        // 获得字段类型 例如 m_BtnStart -> Button
        private string GetFieldType(string name)
        {
            foreach (var rule in Rules)
            {
                if (GetFieldName(name).StartsWith(rule.Prefix))
                {
                    return rule.Type.ToString();
                }
            }
            return "GameObject";
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

#if UNITY_EDITOR
        /// <param name="baseClass">自定义父类表达式（留空/空白默认 MonoBehaviour）</param>
        /// <param name="extraUsings">额外追加到生成文件头部的 using 行（如父类所在命名空间）</param>
        /// <param name="refresh">是否立即刷新资源（触发编译）。批量生成时传 false，由调用方统一刷新一次，避免触发多次编译</param>
        public void GenerateBindCode(GameObject go, string baseClass = "MonoBehaviour", List<string> extraUsings = null, bool refresh = true)
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

            // 缩进层级：类所在层级 = 有无命名空间；字段比类多一级
            var classLevel = string.IsNullOrEmpty(ns) ? 0 : 1;
            var classPad = Pad(classLevel);
            var bodyPad = Pad(classLevel + 1);

            // 递归所有后代，前缀命中 FieldVisibleRules 的才会生成字段（同名只保留首个）
            var fieldLines = new List<string>();
            foreach (Transform child in CollectBindChildren(go))
            {
                fieldLines.Add(string.Format("[SerializeField] {0} {1} {2} = null;",
                    GetFieldVisible(child.name), GetFieldType(child.name), GetBindFieldName(child.name)));
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

        // 收集生成文件需要的 using（去重、固定顺序）
        private static List<string> CollectBindUsings(GameObject go)
        {
            var list = new List<string> { "using UnityEngine;" };
            bool needUI = false;
            bool needTMP = false;
            if (Instance == null)
            {
                return list;
            }
            foreach (Transform child in Instance.CollectBindChildren(go))
            {
                var type = Instance.GetBindFieldTypeName(child.name);
                if (type == nameof(BindType.TMP_Text))
                {
                    needTMP = true;
                }
                else if (type != nameof(BindType.Transform)
                         && type != nameof(BindType.RectTransform)
                         && type != nameof(BindType.GameObject))
                {
                    needUI = true;
                }
            }
            if (needTMP)
            {
                list.Add("using TMPro;");
            }
            if (needUI)
            {
                list.Add("using UnityEngine.UI;");
            }
            return list;
        }

        private static void BuildHeader(StringBuilder sb, GameObject go)
        {
            sb.AppendLine("/// <summary>");
            sb.AppendLine("/// Auto generated code for " + go.name + " by ScriptBinder");
            sb.AppendLine("/// Time: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("/// Author: " + Environment.MachineName);
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
