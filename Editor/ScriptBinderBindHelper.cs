#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
namespace NuoYan.ScriptBinder
{
    /// <summary>
    /// ScriptBinder 绑定管线：把“生成绑定脚本”拆成四个独立步骤依次执行——
    ///   步骤 1/4 代码生成 -> 步骤 2/4 等待编译 -> 步骤 3/4 挂载组件 -> 步骤 4/4 填充引用
    ///
    /// 目标对象与当前步骤持久化在 EditorPrefs 中（域重载/编辑器重启都不丢失），
    /// 由 [InitializeOnLoad] + [DidReloadScripts] 驱动 Flush 状态机自动推进：
    ///   - 只有确认“编译完成且域已重载”后才挂载，绝不用编译前的旧类型提前挂载（否则新字段会漏填）；
    ///   - 场景对象按实例 ID 快照定位，预制体无需打开 Prefab Stage（自动写回 .prefab 资产）；
    ///   - 任一步骤失败只保留任务并给出可操作提示，绝不静默放弃；修正后自动继续。
    /// 另有分步菜单（ScriptBinder/步骤 1..4）可手动补做任意一步。
    /// </summary>
    public static class ScriptBinderBindHelper
    {
        private const string TasksKey = "ScriptBinder.PendingTasks";
        private const string CompileStateKey = "ScriptBinder.CompileState"; // 0=未开始 1=编译中 2=编译完成(已域重载)
        private const string FilesChangedKey = "ScriptBinder.FilesChanged"; // 1=生成代码有变更、需要编译
        private const string LegacyKey = "ScriptBinder.PendingBind";        // 旧版本单任务键，启动时清理

        // 步骤 1（代码生成）在 StartBind 内同步完成，任务入队后从步骤 2 开始
        private enum BindStage { AwaitCompile = 2, Mount = 3, Bind = 4, Done = 5 }

        [Serializable]
        private class BindTask
        {
            public string className;   // 类名 = GameObject 名 = 生成文件名
            public string genDir;      // 生成目录（相对 Assets，BindRules.SavePath）
            public int kind;           // 0=场景对象 1=预制体（Stage 或资产）
            public string scenePath;   // kind=0：场景路径
            public string assetPath;   // kind=1：.prefab 资产路径
            public string hierarchy;   // '/' 拼接的相对层级路径（相对场景根 / Stage 根 / 预制体根）
            public int instanceId;     // 请求时捕获的场景对象实例 ID（域重载后仍有效）
            public int stage;          // BindStage
            [NonSerialized] public GameObject live; // 运行时已解析目标（不持久化）
        }

        [Serializable]
        private class TaskList { public List<BindTask> items = new List<BindTask>(); }

        private static List<BindTask> m_Tasks;
        private static readonly Dictionary<string, Type> m_TypeCache = new Dictionary<string, Type>();
        private static bool m_Applying;
        private static bool m_GateLogged;          // “编译完成”只提示一次
        private static int m_Tick;                 // 轮询节拍（节流 + 节流告警）
        private static int m_WarnCompileIdle;      // 编译迟迟未开始
        private static int m_WarnCompileFail;      // 编译结束未见域重载 / 类型缺失
        private static int m_WarnTarget;           // 目标未定位
        private static GameObject m_ContentsRoot;  // LoadPrefabContents 打开的预制体内容根
        private static string m_ContentsPath;

        // =====================================================================
        // 生命周期：域重载后自动恢复待办任务
        // =====================================================================

        [InitializeOnLoadMethod]
        private static void OnEditorLoad()
        {
            if (EditorPrefs.HasKey(LegacyKey))
            {
                EditorPrefs.DeleteKey(LegacyKey);
            }
            LoadTasks();
            if (m_Tasks.Count > 0)
            {
                EditorApplication.delayCall += Flush;
            }
        }

        [DidReloadScripts]
        private static void OnScriptsReloaded()
        {
            // 一次域重载 = 一轮脚本编译完整结束（新类型此时才可用）
            if (EditorPrefs.GetInt(CompileStateKey, 0) == 1)
            {
                EditorPrefs.SetInt(CompileStateKey, 2);
                EditorPrefs.DeleteKey(FilesChangedKey);
            }
            LoadTasks();
            if (m_Tasks.Count > 0)
            {
                EditorApplication.delayCall += Flush;
            }
        }

        private static void LoadTasks()
        {
            if (m_Tasks != null)
            {
                return;
            }
            m_Tasks = new List<BindTask>();
            var json = EditorPrefs.GetString(TasksKey, string.Empty);
            if (string.IsNullOrEmpty(json))
            {
                return;
            }
            try
            {
                var list = JsonUtility.FromJson<TaskList>(json);
                if (list != null && list.items != null)
                {
                    m_Tasks = list.items;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[ScriptBinder] 待处理任务解析失败，已清空：" + e.Message);
                m_Tasks = new List<BindTask>();
                EditorPrefs.DeleteKey(TasksKey);
            }
        }

        private static void PersistTasks()
        {
            if (m_Tasks == null || m_Tasks.Count == 0)
            {
                EditorPrefs.DeleteKey(TasksKey);
                return;
            }
            EditorPrefs.SetString(TasksKey, JsonUtility.ToJson(new TaskList { items = m_Tasks }));
        }

        // =====================================================================
        // 入口：完整四步管线（步骤 1 同步执行，2/3/4 自动推进）
        // =====================================================================

        /// <summary>对多个目标执行“生成 → 编译 → 挂载 → 绑定”完整管线。</summary>
        /// <param name="targets">选中的 GameObject（场景对象 / Prefab Stage 对象 / Project 窗口预制体资产）</param>
        /// <param name="baseClass">自定义父类表达式，如 "MonoBehaviour"、"CardGame.UGuiForm"</param>
        /// <param name="extraUsings">额外 using 行（如父类所在命名空间）</param>
        public static void StartBind(List<GameObject> targets, string baseClass = "MonoBehaviour", List<string> extraUsings = null)
        {
            if (targets == null || targets.Count == 0)
            {
                return;
            }
            LoadTasks();
            var rules = BindRules.Instance;
            if (rules == null)
            {
                Debug.LogError("[ScriptBinder] 找不到 BindRules 配置，无法生成绑定代码。");
                return;
            }
            string genDir = rules.SavePath == null ? string.Empty : rules.SavePath;

            // Prefab Stage 有未保存修改时提醒：编译期间若 Stage 被关闭，自动挂载将基于已保存的
            // 资产内容直接写回 .prefab，未保存的 Stage 修改可能丢失。
            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            bool stageTargetsDirty = stage != null && stage.scene.isDirty;
            if (stageTargetsDirty)
            {
                stageTargetsDirty = false;
                foreach (var go in targets)
                {
                    if (go != null && (go == stage.prefabContentsRoot || go.transform.IsChildOf(stage.prefabContentsRoot.transform)))
                    {
                        stageTargetsDirty = true;
                        break;
                    }
                }
            }
            if (stageTargetsDirty)
            {
                if (!EditorUtility.DisplayDialog("ScriptBinder",
                    "当前 Prefab Stage 存在未保存的修改。\n\n若编译期间 Stage 被关闭，自动挂载会直接写回 .prefab 资产（基于已保存版本），未保存的修改可能丢失。\n\n建议先按 Ctrl+S 保存预制体再生成。",
                    "仍然继续", "取消"))
                {
                    return;
                }
            }

            var created = new List<BindTask>();
            var seen = new HashSet<string>();
            bool anyChanged = false;
            bool anyGenerated = false;
            foreach (var go in targets)
            {
                if (go == null)
                {
                    continue;
                }
                string name = go.name;
                if (!IsValidClassName(name))
                {
                    Debug.LogWarning("[ScriptBinder] 目标名 <b>" + name + "</b> 不是合法类名，已跳过（请先重命名 GameObject）。");
                    continue;
                }
                if (!seen.Add(name))
                {
                    Debug.LogWarning("[ScriptBinder] 同名目标 <b>" + name + "</b> 已跳过：同名脚本只生成一次，避免相互覆盖。");
                    continue;
                }

                var task = Capture(go, genDir);

                // —— 步骤 1/4：代码生成（先比对内容，无变化时不触发重编译）——
                string rel = RelScriptPath(task);
                string abs = Path.Combine(Application.dataPath, rel.Substring("Assets/".Length).Replace('/', Path.DirectorySeparatorChar));
                string oldText = File.Exists(abs) ? File.ReadAllText(abs) : null;
                rules.GenerateBindCode(go, baseClass, extraUsings, false);
                string newText = File.Exists(abs) ? File.ReadAllText(abs) : null;
                bool changed = oldText != newText;
                anyChanged |= changed;
                anyGenerated = true;

                int fieldCount = rules.CollectBindChildren(go).Count;
                Debug.Log(string.Format("[ScriptBinder] [1/4 代码生成] <b>{0}</b>：{1} 个绑定字段 → {2}{3}",
                    name, fieldCount, rel, changed ? string.Empty : "（内容未变化，无需重新编译）"));
                created.Add(task);
            }
            if (!anyGenerated)
            {
                return;
            }

            // 同一类的旧排队任务作废（新生成的内容以本次为准），避免重复入队
            foreach (var t in created)
            {
                m_Tasks.RemoveAll(x => x.className == t.className && x.stage < (int)BindStage.Done);
            }
            m_Tasks.AddRange(created);
            if (anyChanged)
            {
                EditorPrefs.SetInt(FilesChangedKey, 1);
                EditorPrefs.SetInt(CompileStateKey, 0); // 新一轮变更需要新一轮编译
            }
            PersistTasks();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(); // 整批只触发一次编译
            m_Tick = 0;
            m_GateLogged = false;
            Debug.Log("[ScriptBinder] [2/4 等待编译] 已登记 " + created.Count + " 个任务，编译完成后自动挂载组件并填充引用。");
            EditorApplication.delayCall += Flush;
        }

        /// <summary>把当前选中对象转成任务描述（记录时对象仍存活，可快照实例 ID / 层级）。</summary>
        private static BindTask Capture(GameObject go, string genDir)
        {
            var task = new BindTask
            {
                className = go.name,
                genDir = genDir,
                kind = 0,
                stage = (int)BindStage.AwaitCompile,
            };

            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null && (go == stage.prefabContentsRoot || go.transform.IsChildOf(stage.prefabContentsRoot.transform)))
            {
                task.kind = 1;
                task.assetPath = stage.assetPath;
                task.hierarchy = BuildHierarchy(go.transform, stage.prefabContentsRoot.transform);
                return task;
            }
            if (IsPrefabAssetSelection(go))
            {
                task.kind = 1;
                task.assetPath = AssetDatabase.GetAssetPath(go);
                task.hierarchy = BuildHierarchy(go.transform, go.transform); // 选中资产根 → 空层级 = 根对象
                return task;
            }
            task.scenePath = go.scene.path;
            task.hierarchy = BuildHierarchy(go.transform, null);
            task.instanceId = go.GetInstanceID(); // 场景对象在域重载后实例 ID 不变
            return task;
        }

        private static bool IsPrefabAssetSelection(GameObject go)
        {
            string assetPath = AssetDatabase.GetAssetPath(go);
            return !string.IsNullOrEmpty(assetPath)
                   && assetPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase);
        }

        // =====================================================================
        // 状态机：轮询推进步骤 2/3/4
        // =====================================================================

        private static void Flush()
        {
            if (m_Tasks == null || m_Tasks.Count == 0)
            {
                return;
            }
            if (m_Applying)
            {
                return;
            }
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                // 播放模式下不动场景/资产，退出播放后自动继续
                EditorApplication.delayCall += Flush;
                return;
            }
            if (EditorApplication.isCompiling)
            {
                EditorPrefs.SetInt(CompileStateKey, 1);
                EditorUtility.DisplayProgressBar("ScriptBinder", "步骤 2/4：等待 Unity 编译完成…", 0.35f);
                EditorApplication.delayCall += Flush;
                return;
            }
            if (EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += Flush;
                return;
            }

            m_Applying = true;
            try
            {
                bool changed = false;
                for (int i = 0; i < m_Tasks.Count; i++)
                {
                    var t = m_Tasks[i];
                    if (t.stage >= (int)BindStage.Done)
                    {
                        continue;
                    }
                    switch (t.stage)
                    {
                        case (int)BindStage.AwaitCompile:
                            if (CanProceedAfterCompile(t))
                            {
                                t.stage = (int)BindStage.Mount;
                                changed = true;
                            }
                            break;
                        case (int)BindStage.Mount:
                            if (EnsureMounted(t))
                            {
                                if (m_ContentsRoot != null)
                                {
                                    // 预制体资产内容模式：挂载+填充+保存必须同一轮原子完成，
                                    // 否则下一个内容任务会顶掉本任务打开的 contents
                                    if (FillRefs(t))
                                    {
                                        t.stage = (int)BindStage.Done;
                                        changed = true;
                                    }
                                }
                                else
                                {
                                    t.stage = (int)BindStage.Bind;
                                    changed = true;
                                }
                            }
                            break;
                        case (int)BindStage.Bind:
                            if (FillRefs(t))
                            {
                                t.stage = (int)BindStage.Done;
                                changed = true;
                            }
                            break;
                    }
                }
                if (changed)
                {
                    m_Tasks.RemoveAll(x => x.stage >= (int)BindStage.Done);
                    PersistTasks();
                }
                if (m_Tasks.Count == 0)
                {
                    EditorUtility.ClearProgressBar();
                    m_Tick = 0;
                    m_GateLogged = false;
                    Debug.Log("[ScriptBinder] 绑定管线全部完成。");
                    return;
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
            finally
            {
                m_Applying = false;
            }

            // 仍有任务：编译等待期每帧轮询，其余按 ~20 帧节流（防止空闲时每帧全场景搜索）
            m_Tick++;
            bool anyAwait = m_Tasks.Exists(x => x.stage == (int)BindStage.AwaitCompile);
            if (anyAwait || m_Tick % 20 == 1)
            {
                EditorApplication.delayCall += Flush;
            }
        }

        /// <summary>
        /// 步骤 2/4：只有确认编译真正结束（域已重载）才放行。
        /// 绝不在“文件刚写入、编译尚未开始”时用旧程序集的同名旧类型提前挂载，
        /// 否则新增/改名的序列化字段永远不会被填上。
        /// </summary>
        private static bool CanProceedAfterCompile(BindTask t)
        {
            int cs = EditorPrefs.GetInt(CompileStateKey, 0);
            bool filesChanged = EditorPrefs.GetInt(FilesChangedKey, 0) == 1;

            if (cs == 1)
            {
                // 编译已开始但迟迟没有域重载 → 大概率编译失败
                m_WarnCompileFail++;
                if (m_WarnCompileFail % 400 == 1)
                {
                    EditorUtility.ClearProgressBar();
                    Debug.LogWarning("[ScriptBinder] [2/4 编译] 编译结束后未检测到域重载（可能编译失败）。\n" +
                                     "请查看 Console 中的编译错误；任务已保留，修复后会自动继续。");
                }
                return false;
            }
            if (cs == 0 && filesChanged)
            {
                // 生成代码有变更，但编译还没开始（Unity 通常在下一帧启动编译）
                m_WarnCompileIdle++;
                if (m_WarnCompileIdle % 400 == 1)
                {
                    EditorUtility.ClearProgressBar();
                    Debug.LogWarning("[ScriptBinder] [2/4 编译] 写入生成代码后编译迟迟未开始，仍在等待…（任务已保留）");
                }
                return false;
            }

            var type = GetCompiledType(t);
            if (type == null)
            {
                m_WarnCompileFail++;
                if (m_WarnCompileFail % 400 == 1)
                {
                    EditorUtility.ClearProgressBar();
                    Debug.LogWarning(string.Format("[ScriptBinder] [2/4 编译] 无法解析编译后的类型 <b>{0}</b>" +
                                                   "（可能编译失败或生成文件未导入）。请检查 Console；任务已保留，编译成功后自动继续。",
                        t.className));
                }
                return false;
            }

            if (!m_GateLogged)
            {
                m_GateLogged = true;
                EditorUtility.ClearProgressBar();
                Debug.Log("[ScriptBinder] [2/4 编译] 编译完成，开始步骤 3/4 挂载组件…");
            }
            return true;
        }

        /// <summary>步骤 3/4：把编译好的组件挂到目标 GameObject（已挂过则复用）。</summary>
        private static bool EnsureMounted(BindTask t)
        {
            var go = ResolveTarget(t);
            if (go == null)
            {
                m_WarnTarget++;
                if (m_WarnTarget % 300 == 1)
                {
                    EditorUtility.ClearProgressBar();
                    Debug.LogWarning("[ScriptBinder] [3/4 挂载] 目标未定位：" + TargetDesc(t) + "\n" +
                                     "场景对象需位于已打开的对应场景中；预制体无需打开 Stage（会自动写回资产）。" +
                                     "任务已保留，目标可见后自动继续；也可用菜单 [ScriptBinder/步骤 3：挂载组件（选中）] 手动挂载。");
                }
                return false;
            }
            var type = GetCompiledType(t);
            if (type == null)
            {
                return false;
            }

            var existing = go.GetComponent(type);
            Component comp;
            bool contents = m_ContentsRoot != null; // LoadPrefabContents 内容里不支持 Undo
            if (existing != null)
            {
                comp = existing;
                if (!contents)
                {
                    Undo.RecordObject(comp, "ScriptBinder Rebind");
                }
            }
            else if (contents)
            {
                comp = go.AddComponent(type);
            }
            else
            {
                comp = Undo.AddComponent(go, type);
            }
            if (comp == null)
            {
                Debug.LogWarning("[ScriptBinder] [3/4 挂载] 向 <b>" + go.name + "</b> 添加组件 " + type.Name + " 失败。");
                return false;
            }
            t.live = go;
            Debug.Log(string.Format("[ScriptBinder] [3/4 挂载] 已{0}组件 <b>{1}</b> 到 <b>{2}</b>",
                existing != null ? "复用" : "挂载", type.Name, go.name));
            return true;
        }

        /// <summary>步骤 4/4：把绑定字段逐一填上子物体组件的引用并随场景/预制体保存。</summary>
        private static bool FillRefs(BindTask t)
        {
            var go = t.live;
            if (go == null)
            {
                // 跨轮次对象失效（如编译期间 Stage 被关闭）→ 重新定位并补挂载
                if (!EnsureMounted(t))
                {
                    return false;
                }
                go = t.live;
            }
            var type = GetCompiledType(t);
            if (type == null)
            {
                return false; // 类型消失（如编译失败回退），留到步骤 2 逻辑再处理
            }
            var comp = go.GetComponent(type);
            if (comp == null)
            {
                return false; // 理论上不可达，下一轮会补挂载
            }

            var rules = BindRules.Instance;
            int total = rules != null ? rules.CollectBindChildren(go).Count : 0;
            int filled = BindFields(go, comp);

            if (m_ContentsRoot != null)
            {
                SaveAndUnloadContents(); // 预制体内容挂载：直接保存回资产
            }
            else
            {
                MarkDirty(go);
                if (t.kind == 1)
                {
                    var stage = PrefabStageUtility.GetCurrentPrefabStage();
                    if (stage != null)
                    {
                        Debug.Log("[ScriptBinder] [4/4 组件绑定] 已挂载到 Prefab Stage，请按 Ctrl+S 保存预制体：" + stage.assetPath);
                    }
                }
            }
            t.live = null;
            Debug.Log(string.Format("[ScriptBinder] [4/4 组件绑定] <b>{0}</b> 字段填充 {1}/{2}", go.name, filled, total));
            return true;
        }

        // =====================================================================
        // 目标定位：场景（实例 ID 快照 + 路径兜底）/ 预制体（Stage 优先，未开则写回资产）
        // =====================================================================

        private static GameObject ResolveTarget(BindTask t)
        {
            if (t.kind == 0)
            {
                // 1) 实例 ID 快速路径：场景对象在脚本域重载后实例 ID 保持不变
                if (t.instanceId != 0)
                {
                    var obj = EditorUtility.InstanceIDToObject(t.instanceId) as GameObject;
                    if (obj != null && obj.name == t.className && obj.scene.IsValid() && obj.scene.path == t.scenePath)
                    {
                        return obj;
                    }
                }
                // 2) 场景路径 + 层级兜底
                for (int i = 0; i < SceneManager.sceneCount; i++)
                {
                    var scene = SceneManager.GetSceneAt(i);
                    if (!scene.isLoaded || scene.path != t.scenePath)
                    {
                        continue;
                    }
                    foreach (var root in scene.GetRootGameObjects())
                    {
                        var go = FindByHierarchy(root, t.hierarchy);
                        if (go != null)
                        {
                            return go;
                        }
                    }
                }
                return null;
            }

            // 预制体：优先定位到已打开的 Prefab Stage（保留 Stage 内未保存的编辑）
            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null && stage.assetPath == t.assetPath)
            {
                return FindByHierarchy(stage.prefabContentsRoot, t.hierarchy);
            }

            // Stage 未打开：直接对预制体资产操作（无需用户进入 Stage）
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(t.assetPath);
            if (asset == null)
            {
                return null;
            }
            if (PrefabUtility.GetPrefabAssetType(asset) == PrefabAssetType.Variant)
            {
                // 变体不能直接写回资产（会压平覆盖关系），必须进入 Stage
                if (m_WarnTarget % 300 == 0)
                {
                    Debug.LogWarning("[ScriptBinder] [3/4 挂载] 预制体变体 " + t.assetPath +
                                     " 不能直接写回资产，请双击进入 Prefab Stage（任务已保留，进入后自动继续）。");
                }
                m_WarnTarget++;
                return null;
            }

            if (m_ContentsRoot != null)
            {
                SaveAndUnloadContents(); // 防御：不应同时打开两份内容
            }
            m_ContentsRoot = PrefabUtility.LoadPrefabContents(t.assetPath);
            m_ContentsPath = t.assetPath;
            var go2 = FindByHierarchy(m_ContentsRoot, t.hierarchy);
            if (go2 == null)
            {
                Debug.LogWarning("[ScriptBinder] [3/4 挂载] 预制体 " + t.assetPath + " 中找不到层级 " + t.hierarchy + "，请检查预制体结构。");
                SaveAndUnloadContents();
                return null;
            }
            return go2;
        }

        private static void SaveAndUnloadContents()
        {
            if (m_ContentsRoot == null)
            {
                return;
            }
            try
            {
                PrefabUtility.SaveAsPrefabAsset(m_ContentsRoot, m_ContentsPath);
                Debug.Log("[ScriptBinder] [4/4 组件绑定] 已保存预制体资产：" + m_ContentsPath);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(m_ContentsRoot);
                m_ContentsRoot = null;
                m_ContentsPath = null;
            }
        }

        private static string TargetDesc(BindTask t)
        {
            return t.kind == 1 ? ("预制体资产未定位:" + t.assetPath)
                               : ("场景对象未定位:" + t.scenePath + " / " + t.hierarchy);
        }

        // =====================================================================
        // 编译类型与生成文件路径
        // =====================================================================

        private static Type GetCompiledType(BindTask t)
        {
            string path = RelScriptPath(t);
            if (m_TypeCache.TryGetValue(path, out var cached))
            {
                return cached;
            }
            var monoScript = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
            var type = monoScript != null ? monoScript.GetClass() : null;
            m_TypeCache[path] = type; // 域重载后静态缓存自动失效并重建
            return type;
        }

        /// <summary>按类名（=生成文件名）从生成目录加载已编译类型；未编译/路径不符返回 null。</summary>
        private static Type LoadTypeByName(string className)
        {
            var rules = BindRules.Instance;
            string gen = rules != null ? rules.SavePath : string.Empty;
            var t = new BindTask { className = className, genDir = gen };
            return GetCompiledType(t);
        }

        private static string RelScriptPath(BindTask t)
        {
            var gen = string.IsNullOrEmpty(t.genDir) ? string.Empty : t.genDir.Replace('\\', '/').Trim('/');
            return string.IsNullOrEmpty(gen)
                ? "Assets/" + t.className + ".cs"
                : "Assets/" + gen + "/" + t.className + ".cs";
        }

        // =====================================================================
        // 字段填充（步骤 4 核心，与代码生成使用同一套递归收集顺序）
        // =====================================================================

        /// <summary>逐字段写入序列化引用；返回成功找到并赋值的字段数。</summary>
        private static int BindFields(GameObject go, Component comp)
        {
            var rules = BindRules.Instance;
            if (rules == null)
            {
                return 0;
            }
            var so = new SerializedObject(comp);
            so.Update();
            int filled = 0;
            foreach (Transform child in rules.CollectBindChildren(go))
            {
                var fieldName = rules.GetBindFieldName(child.name);
                var fieldTypeName = rules.GetBindFieldTypeName(child.name);
                var prop = so.FindProperty(fieldName);
                if (prop == null)
                {
                    Debug.LogWarning(string.Format("[ScriptBinder] {0} 上找不到序列化字段 {1}（来源节点：{2}），可能生成代码与当前规则不一致。",
                        comp.GetType().Name, fieldName, child.name));
                    continue;
                }
                var value = ResolveBindValue(child.gameObject, fieldTypeName);
                if (value == null)
                {
                    Debug.LogWarning(string.Format("[ScriptBinder] 子物体 {0} 上未找到 {1} 组件，字段 {2} 置空。",
                        child.name, fieldTypeName, fieldName));
                }
                prop.objectReferenceValue = value;
                filled++;
            }
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(comp);
            if (PrefabUtility.IsPartOfPrefabInstance(comp))
            {
                // 记录到预制体实例覆盖，避免保存后引用被丢弃
                PrefabUtility.RecordPrefabInstancePropertyModifications(comp);
            }
            return filled;
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

        // 枚举名 -> 真实组件类型名。BindType.TMP_Text 对应 TMP 的 TextMeshProUGUI / TextMeshPro
        private static IEnumerable<string> GetRealComponentNames(string fieldTypeName)
        {
            if (fieldTypeName == nameof(BindType.TMP_Text))
            {
                return new[] { "TextMeshProUGUI", "TextMeshPro" };
            }
            return new[] { fieldTypeName };
        }

        // =====================================================================
        // 层级路径工具
        // =====================================================================

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

        /// <summary>按层级找目标；空层级 = 根对象自身（覆盖“直接选中预制体资产根”的情况）。</summary>
        private static GameObject FindByHierarchy(GameObject root, string hierarchy)
        {
            if (root == null)
            {
                return null;
            }
            if (string.IsNullOrEmpty(hierarchy))
            {
                return root;
            }
            var parts = hierarchy.Split('/');
            var cur = root.transform;
            int start = 0;
            if (parts.Length > 0 && cur.name == parts[0])
            {
                start = 1;
            }
            if (start >= parts.Length)
            {
                return root;
            }
            for (int i = start; i < parts.Length; i++)
            {
                if (string.IsNullOrEmpty(parts[i]))
                {
                    continue;
                }
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
            return cur != null ? cur.gameObject : null;
        }

        private static void MarkDirty(GameObject go)
        {
            if (go == null)
            {
                return;
            }
            if (go.scene.IsValid())
            {
                EditorSceneManager.MarkSceneDirty(go.scene);
            }
            EditorUtility.SetDirty(go);
            AssetDatabase.SaveAssets();
        }

        private static bool IsValidClassName(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }
            if (!(char.IsLetter(name[0]) || name[0] == '_'))
            {
                return false;
            }
            foreach (var c in name)
            {
                if (!(char.IsLetterOrDigit(c) || c == '_'))
                {
                    return false;
                }
            }
            return true;
        }

        private static List<GameObject> ValidSelection()
        {
            var list = new List<GameObject>();
            foreach (var go in Selection.gameObjects)
            {
                if (go != null)
                {
                    list.Add(go);
                }
            }
            if (list.Count == 0)
            {
                Debug.Log("[ScriptBinder] 请先选中要绑定的 UI 根节点。");
            }
            return list;
        }

        // =====================================================================
        // 手动分步菜单（与自动管线共用同一套实现，便于失败后补救）
        // =====================================================================

        [MenuItem("Tools/NuoYan/ScriptBinder/一键 生成→编译→挂载→绑定（选中）")]
        private static void MenuFullBind()
        {
            var gos = ValidSelection();
            if (gos.Count == 0)
            {
                return;
            }
            StartBind(gos, "MonoBehaviour", null);
        }

        [MenuItem("Tools/NuoYan/ScriptBinder/步骤 1：仅生成代码（选中）")]
        private static void MenuGenerateOnly()
        {
            var gos = ValidSelection();
            if (gos.Count == 0)
            {
                return;
            }
            var rules = BindRules.Instance;
            bool any = false;
            var seen = new HashSet<string>();
            foreach (var go in gos)
            {
                if (go == null || !IsValidClassName(go.name))
                {
                    continue;
                }
                if (!seen.Add(go.name))
                {
                    continue;
                }
                rules.GenerateBindCode(go, "MonoBehaviour", null, false);
                any = true;
            }
            if (!any)
            {
                return;
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[ScriptBinder] 步骤 1 完成：仅生成代码。\n" +
                      "如需继续请等待编译完成后执行 [步骤 3：挂载组件（选中）] 和 [步骤 4：重新填充引用（选中）]，或直接用 [一键 生成→编译→挂载→绑定]。");
        }

        [MenuItem("Tools/NuoYan/ScriptBinder/步骤 3：挂载组件（选中，需已编译）")]
        private static void MenuMountSelected()
        {
            var gos = ValidSelection();
            if (gos.Count == 0)
            {
                return;
            }
            foreach (var go in gos)
            {
                if (go == null || !IsValidClassName(go.name))
                {
                    continue;
                }
                var type = LoadTypeByName(go.name);
                if (type == null)
                {
                    Debug.LogWarning("[ScriptBinder] [3/4 挂载] 未找到已编译类型 <b>" + go.name +
                                     "</b>：请先执行步骤 1 生成代码并等待编译完成（检查 Console 编译错误）。");
                    continue;
                }
                if (IsPrefabAssetSelection(go))
                {
                    // 直接对 Project 窗口选中的预制体资产根挂载：走内容写回
                    string assetPath = AssetDatabase.GetAssetPath(go);
                    var asset = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
                    if (asset != null && PrefabUtility.GetPrefabAssetType(asset) == PrefabAssetType.Variant)
                    {
                        Debug.LogWarning("[ScriptBinder] [3/4 挂载] 预制体变体 " + assetPath +
                                         " 不能直接写回资产，请双击进入 Prefab Stage 后挂载。");
                        continue;
                    }
                    var root = PrefabUtility.LoadPrefabContents(assetPath);
                    try
                    {
                        if (root.GetComponent(type) == null)
                        {
                            root.AddComponent(type);
                        }
                        PrefabUtility.SaveAsPrefabAsset(root, assetPath);
                        Debug.Log("[ScriptBinder] [3/4 挂载] 已向预制体资产挂载 <b>" + type.Name + "</b>：" + assetPath);
                    }
                    catch (Exception e)
                    {
                        Debug.LogException(e);
                    }
                    finally
                    {
                        PrefabUtility.UnloadPrefabContents(root);
                    }
                    continue;
                }
                var comp = go.GetComponent(type);
                if (comp == null)
                {
                    comp = Undo.AddComponent(go, type);
                }
                if (comp != null)
                {
                    MarkDirty(go);
                    Debug.Log("[ScriptBinder] [3/4 挂载] 已挂载组件 <b>" + type.Name + "</b> 到 <b>" + go.name + "</b>");
                }
            }
        }

        [MenuItem("Tools/NuoYan/ScriptBinder/步骤 4：重新填充引用（选中）")]
        private static void MenuRefillSelected()
        {
            var gos = ValidSelection();
            if (gos.Count == 0)
            {
                return;
            }
            foreach (var go in gos)
            {
                if (go == null)
                {
                    continue;
                }
                foreach (var comp in go.GetComponents<Component>())
                {
                    if (comp == null || !(comp is MonoBehaviour) || comp.GetType().Name != go.name)
                    {
                        continue;
                    }
                    var rules = BindRules.Instance;
                    int total = rules != null ? rules.CollectBindChildren(go).Count : 0;
                    int filled = BindFields(go, comp);
                    MarkDirty(go);
                    Debug.Log(string.Format("[ScriptBinder] [4/4 组件绑定] 已重新填充 <b>{0}</b> 的引用 {1}/{2}", go.name, filled, total));
                }
            }
        }

        [MenuItem("Tools/NuoYan/ScriptBinder/查看待处理任务")]
        private static void MenuShowTasks()
        {
            LoadTasks();
            if (m_Tasks.Count == 0)
            {
                Debug.Log("[ScriptBinder] 当前没有待处理任务。");
                return;
            }
            int cs = EditorPrefs.GetInt(CompileStateKey, 0);
            int fc = EditorPrefs.GetInt(FilesChangedKey, 0);
            var lines = new List<string> { "[ScriptBinder] 待处理任务 " + m_Tasks.Count + " 个（编译状态：" + CompileStateName(cs) + (fc == 1 ? "，有待编译变更" : string.Empty) + "）：" };
            foreach (var t in m_Tasks)
            {
                lines.Add("  - " + t.className + "（" + StageName(t.stage) + "）：" + TargetDesc(t));
            }
            Debug.Log(string.Join("\n", lines.ToArray()));
        }

        [MenuItem("Tools/NuoYan/ScriptBinder/清除待处理任务")]
        private static void MenuClearTasks()
        {
            m_Tasks = new List<BindTask>();
            PersistTasks();
            EditorPrefs.DeleteKey(CompileStateKey);
            EditorPrefs.DeleteKey(FilesChangedKey);
            EditorUtility.ClearProgressBar();
            Debug.Log("[ScriptBinder] 已清除全部待处理任务。");
        }

        private static string StageName(int stage)
        {
            switch (stage)
            {
                case (int)BindStage.AwaitCompile: return "2 等待编译";
                case (int)BindStage.Mount: return "3 挂载组件";
                case (int)BindStage.Bind: return "4 填充引用";
                case (int)BindStage.Done: return "完成";
                default: return "未知";
            }
        }

        private static string CompileStateName(int cs)
        {
            switch (cs)
            {
                case 1: return "编译中";
                case 2: return "已编译";
                default: return "未开始";
            }
        }
    }
}
#endif
