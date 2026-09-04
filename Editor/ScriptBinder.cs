#if UNITY_EDITOR
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.Events;

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

        if (!hasButton)
        {
            CreateButton(window);
            OnCreateButton?.Invoke(window);
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
        if (selectedGameObjects.Length <= 0) return;

        for (int i = 0; i < selectedGameObjects.Length; i++)
        {
            var go = selectedGameObjects[i];
            if (go != null)
            {
                BindRules.Instance.GenerateBindCode(go);
                // 代码写好并触发重编译后，等编译完成自动挂载组件并填入引用
                ScriptBinderBindHelper.RequestBind(go);
                Debug.Log($"{go.name}: Bind Script");
            }
        }
    }
}
#endif