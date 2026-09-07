#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
namespace NuoYan.ScriptBinder
{
    /// <summary>
    /// 在绑定代码编译完成(域重载)之后，把生成的 partial MonoBehaviour 挂到目标
    /// GameObject 上，并用 SerializedObject 把每个绑定字段直接填上子物体的组件引用，
    /// 随场景/预制体保存（编辑器内赋值，运行时零查找开销）。
    /// </summary>
    public static class ScriptBinderBindHelper
    {
        private const string PendingKey = "ScriptBinder.PendingBind";
        private static bool m_Applying;
        private static int m_Attempts;

        [Serializable]
        private class PendingInfo
        {
            public int kind;            // 0=场景对象 1=预制体 Stage 对象
            public string name;         // GameObject 名 = 类名
            public string gen;          // BindRules.Gen 生成目录
            public string scenePath;    // 场景路径（kind=0）
            public string assetPath;    // 预制体资源路径（kind=1）
            public string hierarchy;    // '/' 拼接的相对层级路径
        }

        [InitializeOnLoadMethod]
        private static void OnEditorLoad()
        {
            // 覆盖“没有触发重编译”的情况
            EditorApplication.delayCall += TryApplyPending;
        }

        [DidReloadScripts]
        private static void OnScriptsReloaded()
        {
            // 生成脚本触发域重载后，新类型才可用，此时挂载
            if (!string.IsNullOrEmpty(EditorPrefs.GetString(PendingKey, string.Empty)))
            {
                EditorApplication.delayCall += TryApplyPending;
            }
        }

        /// <summary>生成代码后调用：登记目标，编译完成后自动挂载并填引用。</summary>
        public static void RequestBind(GameObject go)
        {
            if (go == null)
            {
                return;
            }
            var rules = BindRules.Instance;
            var info = new PendingInfo
            {
                kind = 0,
                name = go.name,
                gen = rules != null ? rules.SavePath : string.Empty,
            };

            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null && (go == stage.prefabContentsRoot || go.transform.IsChildOf(stage.prefabContentsRoot.transform)))
            {
                // 预制体编辑 Stage 中绑定的对象
                info.kind = 1;
                info.assetPath = stage.assetPath;
                info.hierarchy = BuildHierarchy(go.transform, stage.prefabContentsRoot.transform);
            }
            else
            {
                // 场景对象
                info.scenePath = go.scene.path;
                info.hierarchy = BuildHierarchy(go.transform, null);
            }

            EditorPrefs.SetString(PendingKey, JsonUtility.ToJson(info));
            m_Attempts = 0;
            TryApplyPending();
        }

        private static void TryApplyPending()
        {
            if (m_Applying)
            {
                return;
            }
            var json = EditorPrefs.GetString(PendingKey, string.Empty);
            if (string.IsNullOrEmpty(json))
            {
                return;
            }
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                // 还在编译，等编译完成（触发重载后 DidReloadScripts 会再来）
                EditorApplication.delayCall += TryApplyPending;
                return;
            }

            m_Applying = true;
            try
            {
                var info = JsonUtility.FromJson<PendingInfo>(json);
                if (Apply(info))
                {
                    EditorPrefs.DeleteKey(PendingKey);
                    m_Attempts = 0;
                }
                else
                {
                    m_Attempts++;
                    if (m_Attempts < 300)
                    {
                        EditorApplication.delayCall += TryApplyPending;
                    }
                    else
                    {
                        EditorPrefs.DeleteKey(PendingKey);
                        m_Attempts = 0;
                        Debug.LogWarning("[ScriptBinder] 多次重试后仍未能自动挂载，请手动把生成的脚本拖到目标 GameObject 上。");
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                EditorPrefs.DeleteKey(PendingKey);
                m_Attempts = 0;
            }
            finally
            {
                m_Applying = false;
            }
        }

        private static bool Apply(PendingInfo info)
        {
            var go = ResolveTarget(info);
            if (go == null)
            {
                return false;
            }

            var monoScript = AssetDatabase.LoadAssetAtPath<MonoScript>(GetRelScriptPath(info));
            var type = monoScript != null ? monoScript.GetClass() : null;
            if (type == null)
            {
                // 生成的脚本还没编译好 / 有编译错误
                return false;
            }

            var existing = go.GetComponent(type);
            Component comp;
            if (existing != null)
            {
                comp = existing;
                Undo.RecordObject(comp, "ScriptBinder Rebind");
            }
            else
            {
                comp = Undo.AddComponent(go, type);
            }
            if (comp == null)
            {
                return false;
            }

            BindFields(go, comp);
            MarkDirty(go);
            return true;
        }

        private static void BindFields(GameObject go, Component comp)
        {
            var rules = BindRules.Instance;
            if (rules == null)
            {
                return;
            }
            var so = new SerializedObject(comp);
            so.Update();
            foreach (Transform child in go.transform)
            {
                if (!rules.IsBindField(child.name))
                {
                    continue;
                }
                var fieldTypeName = rules.GetBindFieldTypeName(child.name);
                var prop = so.FindProperty(child.name);
                if (prop == null)
                {
                    Debug.LogWarning(string.Format("[ScriptBinder] {0} 上找不到序列化字段 {1}，可能生成代码与当前规则不一致。",
                        comp.GetType().Name, child.name));
                    continue;
                }
                prop.objectReferenceValue = ResolveBindValue(child.gameObject, fieldTypeName);
            }
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(comp);
        }

        // 由字段类型找子物体上真实对应的组件
        private static UnityEngine.Object ResolveBindValue(GameObject child, string fieldTypeName)
        {
            if (fieldTypeName == nameof(BindType.GameObject))
            {
                return child;
            }
            if (fieldTypeName == nameof(BindType.Transform))
            {
                return child.transform;
            }

            var realNames = GetRealComponentNames(fieldTypeName);
            foreach (var comp in child.GetComponents<Component>())
            {
                if (comp == null)
                {
                    continue;
                }
                foreach (var realName in realNames)
                {
                    if (comp.GetType().Name == realName)
                    {
                        return comp;
                    }
                }
            }
            return null;
        }

        // 枚举名 -> 真实组件类型名。BindType.TextMeshPro 对应 TMP 的 TextMeshProUGUI / TextMeshPro
        private static IEnumerable<string> GetRealComponentNames(string fieldTypeName)
        {
            if (fieldTypeName == nameof(BindType.TMP_Text))
            {
                return new[] { "TextMeshProUGUI", "TextMeshPro" };
            }
            return new[] { fieldTypeName };
        }

        private static GameObject ResolveTarget(PendingInfo info)
        {
            if (info.kind == 1)
            {
                var stage = PrefabStageUtility.GetCurrentPrefabStage();
                if (stage == null || stage.assetPath != info.assetPath)
                {
                    // 预制体 Stage 没有打开时无法自动定位，需要用户重新进入预制体编辑
                    return null;
                }
                return FindByHierarchy(stage.prefabContentsRoot, info.hierarchy);
            }

            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded || scene.path != info.scenePath)
                {
                    continue;
                }
                foreach (var root in scene.GetRootGameObjects())
                {
                    var go = FindByHierarchy(root, info.hierarchy);
                    if (go != null)
                    {
                        return go;
                    }
                }
            }
            return null;
        }

        private static string GetRelScriptPath(PendingInfo info)
        {
            var gen = string.IsNullOrEmpty(info.gen) ? string.Empty : info.gen.Replace('\\', '/').Trim('/');
            return string.IsNullOrEmpty(gen)
                ? "Assets/" + info.name + ".cs"
                : "Assets/" + gen + "/" + info.name + ".cs";
        }

        // 从 go 逐级向上收集名字直到 stop（不含 stop），拼接成 '/xxx/yyy'
        private static string BuildHierarchy(Transform t, Transform stop)
        {
            var names = new List<string>();
            while (t != null && t != stop)
            {
                names.Add(t.name);
                t = t.parent;
            }
            names.Reverse();
            return string.Join("/", names);
        }

        private static GameObject FindByHierarchy(GameObject root, string hierarchy)
        {
            var parts = hierarchy.Split('/');
            var cur = root.transform;
            int start = 0;
            if (parts.Length > 0 && cur.name == parts[0])
            {
                start = 1;
            }
            for (int i = start; i < parts.Length; i++)
            {
                Transform next = null;
                foreach (Transform child in cur)
                {
                    if (child.name == parts[i])
                    {
                        next = child;
                        break;
                    }
                }
                if (next == null)
                {
                    return null;
                }
                cur = next;
            }
            return cur.gameObject;
        }

        private static void MarkDirty(GameObject go)
        {
            if (go.scene.IsValid())
            {
                EditorSceneManager.MarkSceneDirty(go.scene);
            }
            EditorUtility.SetDirty(go);
            AssetDatabase.SaveAssets();
        }
    }
}
#endif
