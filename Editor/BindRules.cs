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

[CreateAssetMenu(fileName = "BindRules", menuName = "ScriptBinder/BindRules")]
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
    public void GenerateBindCode(GameObject go)
    {
        if (go == null)
        {
            return;
        }
        var className = go.name;
        var ns = string.IsNullOrEmpty(Namespace) ? string.Empty : Namespace;

        // 直接子物体里，前缀命中 FieldVisibleRules 的才会生成字段
        var fieldLines = new List<string>();
        foreach (Transform child in go.transform)
        {
            if (!IsBindField(child.name))
            {
                continue;
            }
            fieldLines.Add(string.Format("    [SerializeField] {0} {1} {2} = null;",
                GetFieldVisible(child.name), GetFieldType(child.name), child.name));
        }

        // 生成主 partial 文件（字段声明）
        var gen = new StringBuilder();
        BuildHeader(gen, go);
        foreach (var usingLine in CollectBindUsings(go))
        {
            gen.AppendLine(usingLine);
        }
        gen.AppendLine();
        OpenNamespace(gen, ns);
        gen.AppendLine(string.Format("public partial class {0} : MonoBehaviour", className));
        gen.AppendLine("{");
        foreach (var line in fieldLines)
        {
            gen.AppendLine(line);
        }
        gen.AppendLine("}");
        CloseNamespace(gen, ns);

        // 生成空的 Logic partial 文件（给开发者在里面写逻辑）
        var logic = new StringBuilder();
        OpenNamespace(logic, ns);
        logic.AppendLine("/// <summary>");
        logic.AppendLine("/// 只会在第一次生成时创建，之后不会覆盖，请在此文件中写逻辑代码");
        logic.AppendLine("/// </summary>");
        logic.AppendLine(string.Format("public partial class {0}", className));
        logic.AppendLine("{");
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

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log(string.Format("[ScriptBinder] 已生成绑定代码 <b>{0}</b>（等待编译后自动挂载并填引用）", className));
    }

    // 收集生成文件需要的 using（去重、固定顺序）
    private static List<string> CollectBindUsings(GameObject go)
    {
        var list = new List<string> { "using UnityEngine;" };
        bool needUI = false;
        bool needTMP = false;
        foreach (Transform child in go.transform)
        {
            if (Instance == null || !Instance.IsBindField(child.name))
            {
                continue;
            }
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
#endif
}
