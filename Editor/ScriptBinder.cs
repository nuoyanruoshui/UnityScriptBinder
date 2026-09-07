#if UNITY_EDITOR
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine.Events;
namespace NuoYan.ScriptBinder
{

    [InitializeOnLoad]
    public static class ScriptBinder
    {
        private static Dictionary<EditorWindow, VisualElement> m_bindButtonsByWindow = new();
        public static UnityAction<EditorWindow> OnCreateButton;
        public static UnityAction<EditorWindow> OnDestroyButton;

        static ScriptBinder()
        {
            EditorApplication.update -= UpdateBindButtons;
            EditorApplication.update += UpdateBindButtons;
        }

        static void UpdateBindButtons()
        {
            // 获取所有Inspector窗口
            var inspectorWindows = Resources.FindObjectsOfTypeAll<EditorWindow>()
                .Where(w => w.GetType().Name == "InspectorWindow")
                .ToList();

            foreach (var window in inspectorWindows)
            {
                if (window != null && window.rootVisualElement != null)
                {
                    UpdateButtonForWindow(window);
                }
            }

            // 清理不存在的窗口引用
            var windowsToRemove = new List<EditorWindow>();
            foreach (var kvp in m_bindButtonsByWindow)
            {
                if (kvp.Key == null)
                {
                    windowsToRemove.Add(kvp.Key);
                }
            }
            foreach (var window in windowsToRemove)
            {
                m_bindButtonsByWindow.Remove(window);
            }
        }

        static void UpdateButtonForWindow(EditorWindow window)
        {
            if (window == null || window.rootVisualElement == null) return;

            var hasButton = m_bindButtonsByWindow.ContainsKey(window);

            if (!hasButton && ShouldShowBindButton())
            {
                CreateButton(window);
                OnCreateButton?.Invoke(window);
            }
            else if (hasButton && !ShouldShowBindButton())
            {
                DestroyButton(window);
                OnDestroyButton?.Invoke(window);
            }
        }

        static bool ShouldShowBindButton()
        {
            var selectedGameObjects = Selection.gameObjects;
            return selectedGameObjects.Length > 0 &&
                   selectedGameObjects.All(go => go != null && go.TryGetComponent<RectTransform>(out _));
        }
        static void DestroyButton(EditorWindow window)
        {
            if (m_bindButtonsByWindow.TryGetValue(window, out var buttonHolder))
            {
                buttonHolder.RemoveFromHierarchy();
                m_bindButtonsByWindow.Remove(window);
            }
        }


        static void CreateButton(EditorWindow window)
        {
            if (window.rootVisualElement == null) return;

            // 查找Add Component按钮
            var addComponentButton = window.rootVisualElement.Q(className: "unity-inspector-add-component-button");
            if (addComponentButton == null)
            {
                // 延迟一帧再尝试，确保UI已经构建完成
                EditorApplication.delayCall += () => CreateButton(window);
                return;
            }

            // 检查是否已经存在我们的按钮
            if (window.rootVisualElement.Q("bind-ui-component-button-holder") != null)
                return;

            // 创建按钮容器
            var buttonHolder = new VisualElement();
            buttonHolder.name = "bind-ui-component-button-holder";
            buttonHolder.style.flexDirection = FlexDirection.Row;
            buttonHolder.style.justifyContent = Justify.Center;
            buttonHolder.style.marginTop = 5f;
            buttonHolder.style.marginBottom = 5f;

            // 创建按钮
            var button = new Button(OnBindButtonClicked);
            button.name = "bind-ui-component-button";
            button.text = "Bind Script";
            button.style.height = 24f;
            button.style.unityTextAlign = TextAnchor.MiddleCenter;

            button.style.width = 230f;
            button.style.height = 25f;
            button.style.marginLeft = 2f;
            button.style.marginRight = 2f;
            button.style.marginTop = -3f;
            button.style.marginBottom = 15f;

            buttonHolder.Add(button);

            // 找到Add Component按钮的父容器，并在其后插入我们的按钮
            var addComponentParent = addComponentButton.parent;
            if (addComponentParent != null)
            {
                // 找到Add Component按钮在父容器中的索引
                int addComponentIndex = addComponentParent.IndexOf(addComponentButton);

                // 在Add Component按钮后面插入我们的按钮
                if (addComponentIndex >= 0)
                {
                    addComponentParent.Insert(addComponentIndex + 1, buttonHolder);
                }
                else
                {
                    addComponentParent.Add(buttonHolder);
                }

                m_bindButtonsByWindow[window] = buttonHolder;
            }
        }

        static void OnBindButtonClicked()
        {
            var selectedGameObjects = Selection.gameObjects;
            if (selectedGameObjects == null || selectedGameObjects.Length <= 0) return;

            var targets = new List<GameObject>();
            foreach (var go in selectedGameObjects)
            {
                if (go != null)
                {
                    targets.Add(go);
                }
            }
            if (targets.Count == 0) return;

            // 弹窗：确认生成 + 填写自定义父类（留空则不继承自定义父类，默认继承 MonoBehaviour）
            BindDialogWindow.ShowDialog(targets);
        }
    }

    public class BindDialogWindow : EditorWindow
    {
        private const string BindModePrefsKey = "ScriptBinder.BindMode"; // 记住上次选择的绑定模式
        private static readonly string[] BindModeLabels =
        {
            "引用赋值（SerializeField，编辑器填充）",
            "运行时绑定（BindComponents 手动调用）",
            "两者兼有（编辑器填充 + 运行时兜底）",
        };
        private List<GameObject> m_Targets;
        private string m_BaseClass = string.Empty;
        private Vector2 m_Scroll;
        private readonly List<bool> m_TargetExpanded = new List<bool>();   // 每个目标的 Foldout 展开状态
        private readonly List<Vector2> m_FieldScroll = new List<Vector2>(); // 每个目标的字段 ScrollView 滚动位置
        private BindMode m_BindMode = BindMode.Reference;
        private string m_ResolvedInput = string.Empty;   // 上次解析过的父类输入
        private Type m_ResolvedBase;                     // 解析到的父类类型（找不到为 null）

        public static void ShowDialog(List<GameObject> targets)
        {
            var win = CreateInstance<BindDialogWindow>();
            win.m_Targets = targets;
            // 绑定模式：记住上次选择；从未选过则取 BindRules 资产默认
            if (EditorPrefs.HasKey(BindModePrefsKey))
            {
                int saved = EditorPrefs.GetInt(BindModePrefsKey, 0);
                win.m_BindMode = (BindMode)Mathf.Clamp(saved, 0, BindModeLabels.Length - 1);
            }
            else
            {
                var rules = BindRules.Instance;
                win.m_BindMode = rules != null ? rules.DefaultMode : BindMode.Reference;
            }
            win.titleContent = new GUIContent("ScriptBinder - 生成绑定脚本");
            win.minSize = new Vector2(460f, 220f);
            win.ShowModal();
        }

        private void OnGUI()
        {
            if (m_Targets == null || m_Targets.Count == 0)
            {
                Close();
                return;
            }

            EditorGUILayout.HelpBox("将按选中 GameObject 的名字生成同名脚本，自动执行 4 步：\n① 代码生成 → ② 等待编译 → ③ 挂载组件 → ④ 填充引用\n注意：同名脚本会被【覆盖】！", MessageType.Warning);

            EditorGUILayout.Space(8f);
            m_BaseClass = EditorGUILayout.TextField("自定义父类（可选）", m_BaseClass);
            DrawBaseClassHint();

            EditorGUILayout.Space(6f);
            int modeIndex = EditorGUILayout.Popup("绑定模式", (int)m_BindMode, BindModeLabels);
            if (modeIndex != (int)m_BindMode)
            {
                m_BindMode = (BindMode)modeIndex;
                EditorPrefs.SetInt(BindModePrefsKey, modeIndex); // 记住选择，下次弹窗沿用
            }
            EditorGUILayout.HelpBox(GetBindModeHint(m_BindMode), MessageType.Info);

            EditorGUILayout.Space(8f);
            DrawTargets();

            GUILayout.FlexibleSpace();
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("取消", GUILayout.Width(90f)))
                {
                    Close();
                }
                if (GUILayout.Button("确定生成", GUILayout.Width(100f)))
                {
                    Confirm();
                }
            }
        }

        // 目标区：每个目标一个 Foldout（默认展开第一个），展开后在下方 ScrollView 中
        // 列出该目标将要绑定的每一个字段（字段名 + 可见性 + 类型，与代码生成同规则）
        private void DrawTargets()
        {
            while (m_TargetExpanded.Count < m_Targets.Count)
            {
                m_TargetExpanded.Add(m_TargetExpanded.Count == 0); // 默认只展开第一个目标
                m_FieldScroll.Add(Vector2.zero);
            }

            var rules = BindRules.Instance;
            m_Scroll = EditorGUILayout.BeginScrollView(m_Scroll);
            for (int i = 0; i < m_Targets.Count; i++)
            {
                var go = m_Targets[i];
                if (go == null)
                {
                    continue;
                }
                var children = rules != null ? rules.CollectBindChildren(go) : null;
                int fieldCount = children != null ? children.Count : 0;

                m_TargetExpanded[i] = EditorGUILayout.Foldout(
                    m_TargetExpanded[i],
                    string.Format("目标：{0}（{1} 个绑定字段）", go.name, fieldCount),
                    true,
                    EditorStyles.foldoutHeader);
                if (!m_TargetExpanded[i])
                {
                    continue;
                }

                // 下方 ScrollView：显示该目标需要绑定的每一个字段
                using (new EditorGUI.IndentLevelScope(1))
                {
                    float innerHeight = Mathf.Clamp(24f + fieldCount * 18f, 44f, 220f);
                    m_FieldScroll[i] = EditorGUILayout.BeginScrollView(m_FieldScroll[i], GUILayout.Height(innerHeight));
                    if (fieldCount == 0 || rules == null)
                    {
                        EditorGUILayout.HelpBox("未发现可绑定字段：子物体命名需带可见性前缀（m_ / M_ / _），类型前缀见 BindRules 配置。", MessageType.Info);
                    }
                    else
                    {
                        foreach (var child in children)
                        {
                            string fieldName = rules.GetBindFieldName(child.name);
                            string visible = rules.GetBindFieldVisible(child.name);
                            string typeName = rules.GetBindFieldTypeName(child.name);
                            EditorGUILayout.LabelField(fieldName, visible + " " + BindRules.GetTypeDisplayName(typeName));
                        }
                    }
                    EditorGUILayout.EndScrollView();
                }
            }
            EditorGUILayout.EndScrollView();
        }

        // 各绑定模式的说明（弹窗内展示）
        private static string GetBindModeHint(BindMode mode)
        {
            switch (mode)
            {
                case BindMode.Runtime:
                    return "字段不序列化，生成 BindComponents() 运行时查找方法。\n请在逻辑代码（.Logic.cs）的生命周期中调用一次：如 Awake / OnEnable / OnInit(userData)。";
                case BindMode.Both:
                    return "字段序列化并编辑器填充；同时生成 BindComponents()，仅对为 null 的字段运行时查找（编辑器引用优先，实例缺失引用时兜底）。\n需要时在生命周期中调用一次 BindComponents()。";
                default:
                    return "字段以 [SerializeField] 声明，生成后由工具在编辑器内自动填充引用（运行时零查找开销）。";
            }
        }

        // 根据输入实时反馈：留空默认 MonoBehaviour；填写则提示是否解析成功 / 是否可作为组件
        private void DrawBaseClassHint()
        {
            if (string.IsNullOrWhiteSpace(m_BaseClass))
            {
                EditorGUILayout.HelpBox("留空：不继承自定义父类（默认继承 MonoBehaviour）", MessageType.Info);
                return;
            }
            var entered = m_BaseClass.Trim();
            if (entered != m_ResolvedInput)
            {
                m_ResolvedInput = entered;
                m_ResolvedBase = ResolveBaseType(entered);
            }

            if (m_ResolvedBase == null)
            {
                EditorGUILayout.HelpBox(string.Format("未找到父类：{0}\n请填写完整类名（含命名空间），或确认该类已编译。", entered), MessageType.Error);
                return;
            }
            if (!typeof(MonoBehaviour).IsAssignableFrom(m_ResolvedBase))
            {
                EditorGUILayout.HelpBox(string.Format("父类 {0}（{1}）不是 MonoBehaviour 派生类，无法作为组件挂载。", entered, m_ResolvedBase.FullName), MessageType.Error);
                return;
            }
            var baseExpr = entered.IndexOf('.') >= 0 ? entered : m_ResolvedBase.Name;
            var extraNs = GetExtraBaseNamespaceUsing(entered);
            EditorGUILayout.HelpBox(string.Format("将生成：public partial class xxx : {0}{1}",
                baseExpr, string.IsNullOrEmpty(extraNs) ? string.Empty : "\n自动补 using " + extraNs + ";"), MessageType.Info);
        }

        // 简单名父类且所在命名空间与生成文件（BindRules.Namespace）不一致时，需要追加的 using 命名空间；否则返回 null
        private string GetExtraBaseNamespaceUsing(string entered)
        {
            if (m_ResolvedBase == null || string.IsNullOrEmpty(entered) || entered.IndexOf('.') >= 0)
            {
                return null;
            }
            var typeNs = m_ResolvedBase.Namespace;
            if (string.IsNullOrEmpty(typeNs))
            {
                return null;
            }
            var fileNs = BindRules.Instance != null ? BindRules.Instance.Namespace : string.Empty;
            return typeNs == fileNs ? null : typeNs;
        }

        // 尝试在已编译程序集中解析父类类型。
        // 简单名会先按 BindRules.Namespace 拼接查找，再回退到全局；带点的按完整名查找。
        private static Type ResolveBaseType(string baseClass)
        {
            var name = baseClass == null ? string.Empty : baseClass.Trim();
            if (name.Length == 0)
            {
                return null;
            }
            var candidates = new List<string>();
            if (name.IndexOf('.') >= 0)
            {
                candidates.Add(name);
            }
            else
            {
                var ns = BindRules.Instance != null ? BindRules.Instance.Namespace : string.Empty;
                if (!string.IsNullOrEmpty(ns))
                {
                    candidates.Add(ns + "." + name);
                }
                candidates.Add(name);
            }
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                foreach (var candidate in candidates)
                {
                    var type = asm.GetType(candidate);
                    if (type != null)
                    {
                        return type;
                    }
                }
            }
            return null;
        }

        private void Confirm()
        {
            var entered = m_BaseClass == null ? string.Empty : m_BaseClass.Trim();
            if (!string.IsNullOrEmpty(entered))
            {
                if (entered != m_ResolvedInput)
                {
                    m_ResolvedInput = entered;
                    m_ResolvedBase = ResolveBaseType(entered);
                }
                if (m_ResolvedBase == null)
                {
                    EditorUtility.DisplayDialog("ScriptBinder", "找不到自定义父类：" + entered + "\n请填写完整类名（含命名空间），或确认该类已编译后再生成。", "知道了");
                    return;
                }
                if (!typeof(MonoBehaviour).IsAssignableFrom(m_ResolvedBase))
                {
                    EditorUtility.DisplayDialog("ScriptBinder", "自定义父类 " + entered + "（" + m_ResolvedBase.FullName + "）必须继承自 MonoBehaviour，否则无法作为组件挂载。", "知道了");
                    return;
                }
            }

            var extraUsings = new List<string>();
            var baseClass = "MonoBehaviour";
            if (!string.IsNullOrEmpty(entered))
            {
                baseClass = entered.IndexOf('.') >= 0 ? entered : m_ResolvedBase.Name;
                var extraNs = GetExtraBaseNamespaceUsing(entered);
                if (!string.IsNullOrEmpty(extraNs))
                {
                    extraUsings.Add("using " + extraNs + ";");
                }
            }

            // 完整四步管线：代码生成在 StartBind 内同步执行，编译/挂载/填充由管线自动推进
            ScriptBinderBindHelper.StartBind(m_Targets, baseClass, extraUsings, m_BindMode);
            Close();
        }
    }
}
#endif