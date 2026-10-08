#if UNITY_EDITOR
#if !ODIN_INSPECTOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
namespace NuoYan.ScriptBinder
{
    [CustomEditor(typeof(BindRules))]
    public class BindRulesEditor : Editor
    {
        // 有本地化 Tooltip 的字段 → 语言表键
        private static readonly Dictionary<string, string> s_TooltipKeys = new Dictionary<string, string>()
        {
            { "DisplayLanguage", "Tooltip.DisplayLanguage" },
            { "SameInAPart", "Tooltip.SameInAPart" },
            { "DefaultMode", "Tooltip.DefaultMode" },
            { "SaveFileMode", "Tooltip.SaveFileMode" },
        };

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            var prop = serializedObject.GetIterator();
            bool enterChildren = true;
            while (prop.NextVisible(enterChildren))
            {
                enterChildren = false;
                if (prop.propertyPath == "DisplayLanguage")
                {
                    DrawLanguagePopup(prop);
                    continue;
                }

                string key;
                if (s_TooltipKeys.TryGetValue(prop.propertyPath, out key))
                {
                    EditorGUILayout.PropertyField(prop, new GUIContent(prop.displayName, LocalizationConstant.Get(key)), true);
                }
                else
                {
                    // 传 true 的重载会用属性上的 [Tooltip]，不要在这里传 GUIContent，否则会盖掉它
                    EditorGUILayout.PropertyField(prop, true);
                }
            }
            serializedObject.ApplyModifiedProperties();
        }

        // 语言下拉：显示名来自语言表（Lang.*），所以它自己也跟着当前语言变
        private static void DrawLanguagePopup(SerializedProperty prop)
        {
            var langs = LocalizationConstant.Languages;
            var labels = new string[langs.Length];
            int current = 0;
            for (int i = 0; i < langs.Length; i++)
            {
                labels[i] = LocalizationConstant.LanguageLabel(langs[i]);
                // 用底层 int 值比对，而不是 enumValueIndex：枚举将来重排也不会错位
                if ((int)langs[i] == prop.intValue)
                {
                    current = i;
                }
            }
            var label = new GUIContent(LocalizationConstant.Get("Field.Language"),
                LocalizationConstant.Get("Tooltip.DisplayLanguage"));
            int picked = EditorGUILayout.Popup(label, current, labels);
            if (picked != current)
            {
                prop.intValue = (int)langs[picked];
            }
        }
    }
}
#endif
#endif