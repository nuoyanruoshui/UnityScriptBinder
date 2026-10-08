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

        /// <summary>
        /// “字段分组（SameInAPart）”偏好的 EditorPrefs 键：这个选项没有资产级默认值，
        /// 只有用户在生成弹窗里的选择，弹窗与菜单共用这一个来源（定义放在这里，避免两边各写一份字符串）。
        /// </summary>
        public const string SameInAPartPrefsKey = "ScriptBinder.SameInAPart";

        /// <summary>读取上次在生成弹窗里选择的字段分组；从未选过时 false（不分组）。</summary>
        public static bool SameInAPartPreference()
        {
            return EditorPrefs.GetInt(SameInAPartPrefsKey, 0) == 1;
        }

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
            public int mode;           // BindMode：0=引用赋值 1=运行时绑定 2=两者兼有
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
                Debug.LogWarning(LocalizationConstant.Format("Log.TasksParseFailed", e.Message));
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
        /// <param name="cusns">本次生成使用的命名空间；空白时用 BindRules.Namespace</param>
        /// <param name="cussf">本次生成写入的目录（相对 Assets）；空白时用 BindRules.SavePath</param>
        /// <param name="saveFileMode">本次生成的文件布局；null 时用 BindRules.SaveFileMode</param>
        /// <param name="mode">绑定模式；null 时取 BindRules.DefaultMode</param>
        /// <param name="sameInAPart">是否把相同类型的字段排布在一起（生成弹窗里的“字段分组”）；无资产级默认值，菜单路径传 SameInAPartPreference()</param>
        public static void StartBind(List<GameObject> targets, string baseClass = "MonoBehaviour", List<string> extraUsings = null, string cusns = null, string cussf = null, SaveFileMode? saveFileMode = null, BindMode? mode = null, bool sameInAPart = false)
        {
            if (targets == null || targets.Count == 0)
            {
                return;
            }
            LoadTasks();
            var rules = BindRules.Instance;
            if (rules == null)
            {
                Debug.LogError(LocalizationConstant.Get("Log.NoBindRules"));
                return;
            }

            BindMode bindMode = mode ?? rules.DefaultMode;
            // 与 GenerateBindCode 的回退规则保持一致：空白 = 用资产默认值
            SaveFileMode fileMode = saveFileMode ?? rules.SaveFileMode;
            string saveRoot = string.IsNullOrWhiteSpace(cussf) ? (rules.SavePath ?? string.Empty) : cussf.Trim();
            string genRoot = saveRoot.Replace('\\', '/').Trim().Trim('/');

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
                    LocalizationConstant.Get("Dialog.PrefabStageDirty"),
                    LocalizationConstant.Get("Btn.ContinueAnyway"), LocalizationConstant.Get("Btn.Cancel")))
                {
                    return;
                }
            }

            var created = new List<BindTask>();
            var seen = new HashSet<string>();
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
                    Debug.LogWarning(LocalizationConstant.Format("Log.InvalidClassName", name));
                    continue;
                }
                if (!seen.Add(name))
                {
                    Debug.LogWarning(LocalizationConstant.Format("Log.DuplicateTarget", name));
                    continue;
                }

                var task = Capture(go, GenDirOf(fileMode, genRoot, name));
                task.mode = (int)bindMode;

                // —— 步骤 1/4：代码生成（每轮都写盘，随后统一编译）——
                string rel = RelScriptPath(task);
                rules.GenerateBindCode(go, baseClass, extraUsings, false, cusns, cussf, saveFileMode, bindMode, sameInAPart);
                anyGenerated = true;

                int fieldCount = rules.CollectBindChildren(go).Count;
                Debug.Log(LocalizationConstant.Format("Log.Step1Generated",
                    name, fieldCount, BindModeLabel(bindMode), rel));
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
            // 每轮生成都写了盘，所以总按“需要一轮新编译”处理：绝不用编译前的旧类型提前挂载（否则新字段会漏填）
            EditorPrefs.SetInt(FilesChangedKey, 1);
            EditorPrefs.SetInt(CompileStateKey, 0);
            PersistTasks();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(); // 整批只触发一次编译
            m_Tick = 0;
            m_GateLogged = false;
            Debug.Log(LocalizationConstant.Format("Log.Step2Queued", created.Count, BindModeLabel(bindMode)));
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
                EditorUtility.DisplayProgressBar("ScriptBinder", LocalizationConstant.Get("Progress.WaitCompile"), 0.35f);
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
                    Debug.Log(LocalizationConstant.Get("Log.PipelineDone"));
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
                    Debug.LogWarning(LocalizationConstant.Get("Log.Step2CompileEnded"));
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
                    Debug.LogWarning(LocalizationConstant.Get("Log.Step2CompileIdle"));
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
                    Debug.LogWarning(LocalizationConstant.Format("Log.Step2TypeUnresolved",
                        t.className));
                }
                return false;
            }

            if (!m_GateLogged)
            {
                m_GateLogged = true;
                EditorUtility.ClearProgressBar();
                Debug.Log(LocalizationConstant.Get("Log.Step2Compiled"));
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
                    Debug.LogWarning(LocalizationConstant.Format("Log.Step3TargetMissing", TargetDesc(t)));
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
                Debug.LogWarning(LocalizationConstant.Format("Log.Step3AddFailed", go.name, type.Name));
                return false;
            }
            t.live = go;
            Debug.Log(LocalizationConstant.Format("Log.Step3Mounted",
                existing != null ? LocalizationConstant.Get("Log.Step3Reuse") : LocalizationConstant.Get("Log.Step3Mount"), type.Name, go.name));
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
            string modeNote = string.Empty;
            int filled;
            if (t.mode == (int)BindMode.Runtime)
            {
                // 运行时绑定模式：字段不序列化，无需编辑器填充（由生成的 BindComponents() 在运行时赋值）
                filled = 0;
                modeNote = LocalizationConstant.Get("Log.Step4NoEditorFill");
            }
            else
            {
                filled = BindFields(go, comp);
            }

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
                        Debug.Log(LocalizationConstant.Format("Log.Step4StageSave", stage.assetPath));
                    }
                }
            }
            t.live = null;
            Debug.Log(LocalizationConstant.Format("Log.Step4Filled", go.name, filled, total, modeNote));
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
                    Debug.LogWarning(LocalizationConstant.Format("Log.VariantNoWriteback", t.assetPath));
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
                Debug.LogWarning(LocalizationConstant.Format("Log.HierarchyNotFound", t.assetPath, t.hierarchy));
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
                Debug.Log(LocalizationConstant.Format("Log.PrefabSaved", m_ContentsPath));
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
            return t.kind == 1 ? LocalizationConstant.Format("Log.TargetDescPrefab", t.assetPath)
                               : LocalizationConstant.Format("Log.TargetDescScene", t.scenePath, t.hierarchy);
        }

        // =====================================================================
        // 编译类型与生成文件路径
        // =====================================================================

        private static Type GetCompiledType(BindTask t)
        {
            return GetCompiledTypeByPath(RelScriptPath(t));
        }

        private static Type GetCompiledTypeByPath(string path)
        {
            if (m_TypeCache.TryGetValue(path, out var cached))
            {
                return cached;
            }
            var monoScript = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
            var type = monoScript != null ? monoScript.GetClass() : null;
            m_TypeCache[path] = type; // 域重载后静态缓存自动失效并重建
            return type;
        }

        /// <summary>
        /// 按类名（=生成文件名）从生成目录加载已编译类型；未编译/路径不符返回 null。
        /// 分步菜单没有任务里的 genDir，这里把两种布局都试一遍（与 BindRules.SaveFileMode 两种取值对应）。
        /// </summary>
        private static Type LoadTypeByName(string className)
        {
            var rules = BindRules.Instance;
            string root = rules != null ? (rules.SavePath ?? string.Empty) : string.Empty;
            foreach (var rel in new[] { AssetPathOf(root, className), AssetPathOf(Path.Combine(root, className), className) })
            {
                var type = GetCompiledTypeByPath(rel);
                if (type != null)
                {
                    return type;
                }
            }
            return null;
        }

        /// <summary>按文件布局算出任务实际所在目录：FileByFile = 生成根；FolderByFolder = 生成根/类名。</summary>
        private static string GenDirOf(SaveFileMode fileMode, string genRoot, string className)
        {
            return fileMode == SaveFileMode.FolderByFolder ? Path.Combine(genRoot, className) : genRoot;
        }

        private static string RelScriptPath(BindTask t)
        {
            return AssetPathOf(t.genDir, t.className);
        }

        // 生成目录（相对 Assets，可为空 / 含子文件夹）+ 类名 → Assets 下的脚本路径
        private static string AssetPathOf(string genDir, string className)
        {
            var gen = string.IsNullOrEmpty(genDir) ? string.Empty : genDir.Replace('\\', '/').Trim('/');
            return string.IsNullOrEmpty(gen)
                ? "Assets/" + className + ".cs"
                : "Assets/" + gen + "/" + className + ".cs";
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
                    Debug.LogWarning(LocalizationConstant.Format("Log.FieldNotFound",
                        comp.GetType().Name, fieldName, child.name));
                    continue;
                }
                var value = ResolveBindValue(child.gameObject, fieldTypeName);
                if (value == null)
                {
                    Debug.LogWarning(LocalizationConstant.Format("Log.ComponentNotFound",
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

        // 由字段类型表达式找子物体上真实对应的组件：
        // 优先把表达式解析为真实类型后按“可赋值”匹配——支持自定义组件，且 TMP_Text 等基类
        // 能自动承接 TextMeshProUGUI 等派生组件；解析不到时退化为按类型名（短名/全名）比对。
        private static UnityEngine.Object ResolveBindValue(GameObject child, string fieldTypeName)
        {
            if (string.IsNullOrWhiteSpace(fieldTypeName))
            {
                return null;
            }
            var type = BindRules.ResolveRuleType(fieldTypeName);
            if (type != null)
            {
                if (type == typeof(GameObject))
                {
                    return child;
                }
                foreach (var comp in child.GetComponents<Component>())
                {
                    if (comp == null)
                    {
                        continue;
                    }
                    if (type.IsAssignableFrom(comp.GetType()))
                    {
                        return comp;
                    }
                }
                return null;
            }
            // 解析失败（如类型尚未编译）：按名字兜底比对
            foreach (var comp in child.GetComponents<Component>())
            {
                if (comp == null)
                {
                    continue;
                }
                var compType = comp.GetType();
                if (compType.Name == fieldTypeName || compType.FullName == fieldTypeName)
                {
                    return comp;
                }
            }
            return null;
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
                Debug.Log(LocalizationConstant.Get("Log.NoSelection"));
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
            // 分组没有资产级默认值，菜单沿用弹窗里记住的那个选择，避免两个入口生成出不同排布的脚本
            StartBind(gos, "MonoBehaviour", null, sameInAPart: SameInAPartPreference());
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
                rules.GenerateBindCode(go, "MonoBehaviour", null, false, sameInAPart: SameInAPartPreference());
                any = true;
            }
            if (!any)
            {
                return;
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log(LocalizationConstant.Get("Log.Step1Done"));
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
                    Debug.LogWarning(LocalizationConstant.Format("Log.MountTypeNotFound", go.name));
                    continue;
                }
                if (IsPrefabAssetSelection(go))
                {
                    // 直接对 Project 窗口选中的预制体资产根挂载：走内容写回
                    string assetPath = AssetDatabase.GetAssetPath(go);
                    var asset = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
                    if (asset != null && PrefabUtility.GetPrefabAssetType(asset) == PrefabAssetType.Variant)
                    {
                        Debug.LogWarning(LocalizationConstant.Format("Log.VariantNoWritebackShort", assetPath));
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
                        Debug.Log(LocalizationConstant.Format("Log.MountedToPrefabAsset", type.Name, assetPath));
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
                    Debug.Log(LocalizationConstant.Format("Log.MountedToTarget", type.Name, go.name));
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
                    Debug.Log(LocalizationConstant.Format("Log.Refilled", go.name, filled, total));
                }
            }
        }

        [MenuItem("Tools/NuoYan/ScriptBinder/查看待处理任务")]
        private static void MenuShowTasks()
        {
            LoadTasks();
            if (m_Tasks.Count == 0)
            {
                Debug.Log(LocalizationConstant.Get("Log.NoPendingTasks"));
                return;
            }
            int cs = EditorPrefs.GetInt(CompileStateKey, 0);
            int fc = EditorPrefs.GetInt(FilesChangedKey, 0);
            var lines = new List<string> { LocalizationConstant.Format("Log.PendingTasksHeader", m_Tasks.Count, CompileStateName(cs), fc == 1 ? LocalizationConstant.Get("Log.HasPendingChanges") : string.Empty) };
            foreach (var t in m_Tasks)
            {
                lines.Add(LocalizationConstant.Format("Log.PendingTaskItem", t.className, StageName(t.stage), TargetDesc(t)));
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
            Debug.Log(LocalizationConstant.Get("Log.Cleared"));
        }

        private static string BindModeLabel(BindMode mode)
        {
            switch (mode)
            {
                case BindMode.Runtime: return LocalizationConstant.Get("Log.ModeLabel.Runtime");
                case BindMode.Both: return LocalizationConstant.Get("Log.ModeLabel.Both");
                default: return LocalizationConstant.Get("Log.ModeLabel.Reference");
            }
        }

        private static string StageName(int stage)
        {
            switch (stage)
            {
                case (int)BindStage.AwaitCompile: return LocalizationConstant.Get("Log.Stage.AwaitCompile");
                case (int)BindStage.Mount: return LocalizationConstant.Get("Log.Stage.Mount");
                case (int)BindStage.Bind: return LocalizationConstant.Get("Log.Stage.Bind");
                case (int)BindStage.Done: return LocalizationConstant.Get("Log.Stage.Done");
                default: return LocalizationConstant.Get("Log.Stage.Unknown");
            }
        }

        private static string CompileStateName(int cs)
        {
            switch (cs)
            {
                case 1: return LocalizationConstant.Get("Log.CompileState.Compiling");
                case 2: return LocalizationConstant.Get("Log.CompileState.Compiled");
                default: return LocalizationConstant.Get("Log.CompileState.NotStarted");
            }
        }
    }
}
#endif
