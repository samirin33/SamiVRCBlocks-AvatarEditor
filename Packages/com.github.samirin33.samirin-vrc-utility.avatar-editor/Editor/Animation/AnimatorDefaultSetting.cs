using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using Samirin33.Editor;
using Samirin33.AvatarEditor.Tools.Editor;

namespace Samirin33.AvatarEditor.Animation.Editor
{
    public static class AnimatorDefaultSetting
    {
        private const string PrefsKeyPrefix = "SamirinEditorTools.AnimatorDefaultSetting.";
        private const string PrefsKeyWriteDefaults = PrefsKeyPrefix + "WriteDefaults";
        private const string PrefsKeyMirror = PrefsKeyPrefix + "Mirror";
        private const string PrefsKeySpeed = PrefsKeyPrefix + "Speed";
        private const string PrefsKeyEnabled = PrefsKeyPrefix + "Enabled";
        // Transition
        private const string PrefsKeyTransitionDuration = PrefsKeyPrefix + "Transition.Duration";
        private const string PrefsKeyTransitionHasFixedDuration = PrefsKeyPrefix + "Transition.HasFixedDuration";
        private const string PrefsKeyTransitionHasExitTime = PrefsKeyPrefix + "Transition.HasExitTime";
        private const string PrefsKeyTransitionExitTime = PrefsKeyPrefix + "Transition.ExitTime";
        private const string PrefsKeyTransitionOffset = PrefsKeyPrefix + "Transition.Offset";
        private const string PrefsKeyTransitionCanTransitionToSelf = PrefsKeyPrefix + "Transition.CanTransitionToSelf";
        private const string PrefsKeyLayerDefaultWeight = PrefsKeyPrefix + "Layer.DefaultWeight";
        private const string PrefsKeyStateDefaultMotionGUID = PrefsKeyPrefix + "State.DefaultMotionGUID";
        // ステート名ナンバリング（重複時）
        private const string PrefsKeyStateNameNumberingEnabled = PrefsKeyPrefix + "StateNameNumbering.Enabled";
        private const string PrefsKeyStateNameNumberingSeparator = PrefsKeyPrefix + "StateNameNumbering.Separator";

        private const bool DefaultWriteDefaults = false; // VRChat等では false が推奨
        private const bool DefaultMirror = false;
        private const float DefaultSpeed = 1f;
        private const bool DefaultEnabled = true;
        private const float DefaultTransitionDuration = 0f;
        private const bool DefaultTransitionHasFixedDuration = true;
        private const bool DefaultTransitionHasExitTime = false;
        private const float DefaultTransitionExitTime = 1f;
        private const float DefaultTransitionOffset = 0f;
        private const bool DefaultTransitionCanTransitionToSelf = false; // AnyState遷移で自分自身への遷移を許可しない
        private const float DefaultLayerDefaultWeight = 1f; // 新規レイヤーのWeight（0だと重み0のまま）
        private const bool DefaultStateNameNumberingEnabled = true;
        private const string DefaultStateNameNumberingSeparator = " ";
        private const string ProxyEmptyAnimPath = "Packages/com.vrchat.avatars/Samples/AV3 Demo Assets/Animation/ProxyAnim/proxy_empty.anim";
        private static readonly HashSet<int> ScriptAppliedStateInstanceIds = new HashSet<int>();
        private static readonly HashSet<int> ScriptAppliedTransitionInstanceIds = new HashSet<int>();

        public static bool Enabled
        {
            get => EditorPrefs.GetBool(PrefsKeyEnabled, DefaultEnabled);
            set => EditorPrefs.SetBool(PrefsKeyEnabled, value);
        }

        public static bool WriteDefaults
        {
            get => EditorPrefs.GetBool(PrefsKeyWriteDefaults, DefaultWriteDefaults);
            set => EditorPrefs.SetBool(PrefsKeyWriteDefaults, value);
        }

        public static bool Mirror
        {
            get => EditorPrefs.GetBool(PrefsKeyMirror, DefaultMirror);
            set => EditorPrefs.SetBool(PrefsKeyMirror, value);
        }

        public static float Speed
        {
            get => EditorPrefs.GetFloat(PrefsKeySpeed, DefaultSpeed);
            set => EditorPrefs.SetFloat(PrefsKeySpeed, value);
        }

        public static float TransitionDuration
        {
            get => EditorPrefs.GetFloat(PrefsKeyTransitionDuration, DefaultTransitionDuration);
            set => EditorPrefs.SetFloat(PrefsKeyTransitionDuration, value);
        }

        public static bool TransitionHasFixedDuration
        {
            get => EditorPrefs.GetBool(PrefsKeyTransitionHasFixedDuration, DefaultTransitionHasFixedDuration);
            set => EditorPrefs.SetBool(PrefsKeyTransitionHasFixedDuration, value);
        }

        public static bool TransitionHasExitTime
        {
            get => EditorPrefs.GetBool(PrefsKeyTransitionHasExitTime, DefaultTransitionHasExitTime);
            set => EditorPrefs.SetBool(PrefsKeyTransitionHasExitTime, value);
        }

        public static float TransitionExitTime
        {
            get => EditorPrefs.GetFloat(PrefsKeyTransitionExitTime, DefaultTransitionExitTime);
            set => EditorPrefs.SetFloat(PrefsKeyTransitionExitTime, value);
        }

        public static float TransitionOffset
        {
            get => EditorPrefs.GetFloat(PrefsKeyTransitionOffset, DefaultTransitionOffset);
            set => EditorPrefs.SetFloat(PrefsKeyTransitionOffset, value);
        }

        /// <summary>AnyStateからの遷移の Can Transition To Self のデフォルト（オフ推奨）</summary>
        public static bool TransitionCanTransitionToSelf
        {
            get => EditorPrefs.GetBool(PrefsKeyTransitionCanTransitionToSelf, DefaultTransitionCanTransitionToSelf);
            set => EditorPrefs.SetBool(PrefsKeyTransitionCanTransitionToSelf, value);
        }

        /// <summary>新規作成したレイヤーの Weight のデフォルト（1番目のレイヤーには適用されない）</summary>
        public static float LayerDefaultWeight
        {
            get => EditorPrefs.GetFloat(PrefsKeyLayerDefaultWeight, DefaultLayerDefaultWeight);
            set => EditorPrefs.SetFloat(PrefsKeyLayerDefaultWeight, Mathf.Clamp01(value));
        }

        /// <summary>新規ステートにデフォルトでアタッチする AnimationClip（null なら未設定）</summary>
        public static AnimationClip StateDefaultMotion
        {
            get
            {
                var guid = EditorPrefs.GetString(PrefsKeyStateDefaultMotionGUID, "");
                if (string.IsNullOrEmpty(guid)) return null;
                var path = AssetDatabase.GUIDToAssetPath(guid);
                return string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            }
            set
            {
                if (value == null)
                    EditorPrefs.SetString(PrefsKeyStateDefaultMotionGUID, "");
                else
                    EditorPrefs.SetString(PrefsKeyStateDefaultMotionGUID, AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(value)));
            }
        }

        /// <summary>同レイヤー内でステート名が重複した場合にナンバリングするか</summary>
        public static bool StateNameNumberingEnabled
        {
            get => EditorPrefs.GetBool(PrefsKeyStateNameNumberingEnabled, DefaultStateNameNumberingEnabled);
            set => EditorPrefs.SetBool(PrefsKeyStateNameNumberingEnabled, value);
        }

        /// <summary>ステート名と番号の間の区切り文字（例: " ", "_", "."）</summary>
        public static string StateNameNumberingSeparator
        {
            get => EditorPrefs.GetString(PrefsKeyStateNameNumberingSeparator, DefaultStateNameNumberingSeparator);
            set => EditorPrefs.SetString(PrefsKeyStateNameNumberingSeparator, value ?? DefaultStateNameNumberingSeparator);
        }

        /// <summary>Default Motion に proxy_empty.anim を設定する</summary>
        public static void SetProxyEmptyAsDefaultMotion()
        {
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(ProxyEmptyAnimPath);
            if (clip != null)
            {
                StateDefaultMotion = clip;
            }
            else
            {
                Debug.LogWarning($"[AnimatorDefaultSetting] proxy_empty.anim が見つかりません: {ProxyEmptyAnimPath}");
            }
        }

        [InitializeOnLoadMethod]
        private static void Initialize()
        {
#if UNITY_2022_2_OR_NEWER
            ObjectChangeEvents.changesPublished += OnChangesPublished;
#endif
        }

#if UNITY_2022_2_OR_NEWER
        private static void OnChangesPublished(ref ObjectChangeEventStream stream)
        {
            if (!Enabled) return;

            for (int i = 0; i < stream.length; i++)
            {
                var kind = stream.GetEventType(i);

                if (kind == ObjectChangeKind.CreateAssetObject)
                {
                    stream.GetCreateAssetObjectEvent(i, out var args);
                    var obj = EditorUtility.InstanceIDToObject(args.instanceId);
                    var instanceId = args.instanceId;

                    if (obj is AnimatorState)
                    {
                        EditorApplication.delayCall += () =>
                        {
                            if (ConsumeSuppressedInstanceId(ScriptAppliedStateInstanceIds, instanceId)) return;
                            var state = EditorUtility.InstanceIDToObject(instanceId) as AnimatorState;
                            ApplyDefaultsToState(state);
                        };
                    }
                    else if (obj is AnimatorStateTransition)
                    {
                        // エディタの「Make Transition」等で付く Unity 初期値はバージョンにより異なり、
                        // ShouldApplyDefaultsToTransition だけでは検知できないことがある。新規サブアセット作成なので prefs をそのまま適用する。
                        EditorApplication.delayCall += () =>
                        {
                            if (ConsumeSuppressedInstanceId(ScriptAppliedTransitionInstanceIds, instanceId)) return;
                            var tr = EditorUtility.InstanceIDToObject(instanceId) as AnimatorStateTransition;
                            ApplyDefaultsToTransition(tr, requireFreshUnityDefaults: false);
                        };
                    }
                    else if (obj is AnimatorStateMachine)
                    {
                        EditorApplication.delayCall += () =>
                        {
                            var sm = EditorUtility.InstanceIDToObject(instanceId) as AnimatorStateMachine;
                            ApplyDefaultsToNewLayer(sm);
                        };
                    }

                    continue;
                }

                // 「Make Transition」等でサブアセットの Create が報告されず、プロパティ変更のみ流れる場合がある。
                if (kind == ObjectChangeKind.ChangeAssetObjectProperties)
                {
                    stream.GetChangeAssetObjectPropertiesEvent(i, out var changeArgs);
                    var instanceId = changeArgs.instanceId;
                    var obj = EditorUtility.InstanceIDToObject(instanceId);
                    if (obj is AnimatorStateTransition)
                    {
                        EditorApplication.delayCall += () =>
                        {
                            if (ConsumeSuppressedInstanceId(ScriptAppliedTransitionInstanceIds, instanceId)) return;
                            var tr = EditorUtility.InstanceIDToObject(instanceId) as AnimatorStateTransition;
                            ApplyDefaultsToTransition(tr, requireFreshUnityDefaults: true);
                        };
                    }
                }
            }
        }
#endif

        /// <summary>
        /// <see cref="AnimatorBinding"/> 等、API でステートを作成した直後に呼び出す。
        /// Unity エディタの「新規直後」判定をスキップし、<see cref="Enabled"/> が true なら prefs を適用する。
        /// </summary>
        public static void ApplyDefaultsToStateFromScript(AnimatorState state)
        {
            SuppressNextEventAutoApply(ScriptAppliedStateInstanceIds, state);
            ApplyDefaultsToState(state, requireFreshUnityDefaults: false);
        }

        /// <summary>
        /// <see cref="AnimatorBinding"/> 等、API で遷移を作成した直後に呼び出す。
        /// </summary>
        public static void ApplyDefaultsToTransitionFromScript(AnimatorStateTransition transition)
        {
            SuppressNextEventAutoApply(ScriptAppliedTransitionInstanceIds, transition);
            ApplyDefaultsToTransition(transition, requireFreshUnityDefaults: false);
        }

        private static void SuppressNextEventAutoApply<T>(HashSet<int> suppressedIds, T obj) where T : UnityEngine.Object
        {
            if (obj == null) return;
            suppressedIds.Add(obj.GetInstanceID());
        }

        private static bool ConsumeSuppressedInstanceId(HashSet<int> suppressedIds, int instanceId)
        {
            return suppressedIds.Remove(instanceId);
        }

        private static void ApplyDefaultsToState(AnimatorState state, bool requireFreshUnityDefaults = true)
        {
            if (!Enabled) return;
            if (state == null) return;
            if (requireFreshUnityDefaults && !ShouldApplyDefaultsToState(state)) return;

            Undo.RecordObject(state, "Animator Default Setting");
            var defaultMotion = StateDefaultMotion;
            if (defaultMotion != null && state.motion == null)
            {
                state.motion = defaultMotion;
                state.name = defaultMotion.name;
            }

            if (StateNameNumberingEnabled)
            {
                var stateMachine = GetParentStateMachine(state);
                if (stateMachine != null)
                    state.name = GetUniqueStateName(stateMachine, state, state.name);
            }

            state.writeDefaultValues = WriteDefaults;
            state.mirror = Mirror;
            state.speed = Speed;
            EditorUtility.SetDirty(state);
        }

        /// <summary>
        /// スクリプトで作成・設定済みのステートは上書きしない。
        /// Unity の新規作成直後に近い値の場合のみデフォルト設定を適用する。
        /// </summary>
        private static bool ShouldApplyDefaultsToState(AnimatorState state)
        {
            // D&D で AnimationClip から State を作ると、作成直後から motion が設定済みになる。
            // ここで motion の有無を条件にすると「新規 State」でも適用がスキップされるため判定に使わない。
            if (state.writeDefaultValues != true) return false;
            if (state.mirror != false) return false;
            if (!Mathf.Approximately(state.speed, 1f)) return false;
            return true;
        }

        /// <summary>ステートが属する直接の親ステートマシン（同レイヤー内のSubStateは別扱い）を取得</summary>
        private static AnimatorStateMachine GetParentStateMachine(AnimatorState state)
        {
            var path = AssetDatabase.GetAssetPath(state);
            if (string.IsNullOrEmpty(path)) return null;
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if (controller == null) return null;

            foreach (var layer in controller.layers)
            {
                var found = FindStateMachineContainingState(layer.stateMachine, state);
                if (found != null) return found;
            }
            return null;
        }

        private static AnimatorStateMachine FindStateMachineContainingState(AnimatorStateMachine sm, AnimatorState target)
        {
            foreach (var s in sm.states)
            {
                if (s.state == target) return sm;
            }
            foreach (var sub in sm.stateMachines)
            {
                var found = FindStateMachineContainingState(sub.stateMachine, target);
                if (found != null) return found;
            }
            return null;
        }

        /// <summary>同レイヤー内（同一ステートマシン内・SubStateの子は除く）で重複しなければそのまま、重複時はナンバリング。既存のナンバリング形式も検知する。対象ステート自身は重複カウントから除外する。</summary>
        private static string GetUniqueStateName(AnimatorStateMachine stateMachine, AnimatorState targetState, string desiredName)
        {
            if (string.IsNullOrEmpty(desiredName)) return desiredName;

            var names = new HashSet<string>();
            foreach (var s in stateMachine.states)
            {
                if (s.state == targetState) continue; // 自身は重複判定から除外
                names.Add(s.state.name);
            }

            if (!names.Contains(desiredName))
                return desiredName;

            var separator = string.IsNullOrEmpty(StateNameNumberingSeparator) ? DefaultStateNameNumberingSeparator : StateNameNumberingSeparator;
            var escapedSep = Regex.Escape(separator);

            // 既に "ベース名 + 区切り + 数字" の形式かどうかを検知
            var numberedPattern = new Regex(@"^(.+)" + escapedSep + @"(\d+)$");
            var baseName = desiredName;
            var match = numberedPattern.Match(desiredName);
            if (match.Success)
                baseName = match.Groups[1].Value;

            var usedNumbers = new HashSet<int>();
            foreach (var n in names)
            {
                if (n == baseName)
                    usedNumbers.Add(0);
                else if (n.StartsWith(baseName + separator))
                {
                    var suffix = n.Substring(baseName.Length + separator.Length);
                    if (int.TryParse(suffix, out var num))
                        usedNumbers.Add(num);
                }
            }

            var next = 1;
            while (usedNumbers.Contains(next)) next++;
            return baseName + separator + next;
        }

        private static void ApplyDefaultsToTransition(AnimatorStateTransition transition, bool requireFreshUnityDefaults = true)
        {
            if (!Enabled) return;
            if (transition == null) return;
            if (requireFreshUnityDefaults && !ShouldApplyDefaultsToTransition(transition)) return;

            Undo.RecordObject(transition, "Animator Default Setting (Transition)");
            transition.duration = TransitionDuration;
            transition.hasFixedDuration = TransitionHasFixedDuration;
            transition.hasExitTime = TransitionHasExitTime;
            transition.exitTime = TransitionExitTime;
            transition.offset = TransitionOffset;
            if (AnimatorTransitionEditOperations.IsAnyStateTransition(transition))
                transition.canTransitionToSelf = TransitionCanTransitionToSelf;
            EditorUtility.SetDirty(transition);
        }

        /// <summary>
        /// スクリプトで値を設定済みの遷移は上書きしない。
        /// Unity の新規作成直後に近い値の場合のみデフォルト設定を適用する。
        /// </summary>
        private static bool ShouldApplyDefaultsToTransition(AnimatorStateTransition transition)
        {
            if (transition == null) return false;
            if (transition.hasFixedDuration != true) return false;
            if (!Mathf.Approximately(transition.offset, 0f)) return false;

            // Unity の「新規トランジション」初期値はバージョン・作成経路（右クリック Make Transition 等）で異なる。
            // いずれかに一致するときだけ「まだユーザーが触っていない」とみなす。

            // 典型: Blend 0.25s, Exit Time 0.75, Has Exit Time
            if (Mathf.Approximately(transition.duration, 0.25f) &&
                transition.hasExitTime &&
                Mathf.Approximately(transition.exitTime, 0.75f))
                return true;

            // ブレンド無し・Exit Time オフ（Unity 6 系などで見られる）
            if (Mathf.Approximately(transition.duration, 0f) && !transition.hasExitTime)
                return true;

            // 短いクロスフェード例（環境により 0.1 / Exit Time 付き）
            if (Mathf.Approximately(transition.duration, 0.1f) &&
                transition.hasExitTime &&
                Mathf.Approximately(transition.exitTime, 0.75f))
                return true;

            return false;
        }

        private static void ApplyDefaultsToNewLayer(AnimatorStateMachine stateMachine)
        {
            if (stateMachine == null) return;

            var path = AssetDatabase.GetAssetPath(stateMachine);
            if (string.IsNullOrEmpty(path)) return;
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            if (controller == null) return;

            var layers = controller.layers;
            Undo.RecordObject(controller, "Animator Default Setting (Layer Weight)");
            var modified = false;

            // レイヤー0のWeightが0なら1に設定
            if (layers.Length > 0 && Mathf.Approximately(layers[0].defaultWeight, 0f))
            {
                layers[0].defaultWeight = 1f;
                modified = true;
            }

            for (int i = 0; i < layers.Length; i++)
            {
                if (layers[i].stateMachine != stateMachine) continue;
                if (i == 0)
                {
                    if (modified)
                    {
                        controller.layers = layers;
                        EditorUtility.SetDirty(controller);
                    }
                    return;
                }
                if (!Mathf.Approximately(layers[i].defaultWeight, 0f))
                    return;
                layers[i].defaultWeight = LayerDefaultWeight;
                modified = true;
                controller.layers = layers;
                EditorUtility.SetDirty(controller);
                return;
            }
        }

        [MenuItem("samirin33 Editor Tools/Animator Default Setting", false, 100)]
        private static void OpenSettings()
        {
#if UNITY_2022_2_OR_NEWER
            SettingsService.OpenUserPreferences("Samirin Editor Tools/Animator Default Setting");
#else
            AnimatorDefaultSettingWindow.ShowWindow();
#endif
        }
    }

#if !UNITY_2022_2_OR_NEWER
    internal sealed class AnimatorDefaultSettingWindow : EditorWindow
    {
        public static void ShowWindow()
        {
            var w = GetWindow<AnimatorDefaultSettingWindow>("Animator Default Setting");
            w.minSize = new Vector2(320, 320);
        }

        private void OnGUI()
        {
            SamirinEditorStyleHelper.DrawWithBlueBackground(() =>
            {
                EditorGUILayout.Space(8);
                EditorGUILayout.HelpBox("新規ステートへの自動適用には Unity 2022.2 以降が必要です。\n以下で初期値のみ設定できます。", MessageType.Info);
                EditorGUILayout.Space(4);

                EditorGUI.BeginChangeCheck();
                AnimatorDefaultSetting.Enabled = EditorGUILayout.Toggle("有効（2022.2以降で使用）", AnimatorDefaultSetting.Enabled);
                AnimatorDefaultSetting.WriteDefaults = EditorGUILayout.Toggle("Write Defaults", AnimatorDefaultSetting.WriteDefaults);
                AnimatorDefaultSetting.Mirror = EditorGUILayout.Toggle("Mirror", AnimatorDefaultSetting.Mirror);
                AnimatorDefaultSetting.Speed = EditorGUILayout.FloatField("Speed", AnimatorDefaultSetting.Speed);
                AnimatorDefaultSetting.StateDefaultMotion = (AnimationClip)EditorGUILayout.ObjectField("Default Motion (Clip)", AnimatorDefaultSetting.StateDefaultMotion, typeof(AnimationClip), false);
                if (GUILayout.Button("proxy_empty を選択"))
                    AnimatorDefaultSetting.SetProxyEmptyAsDefaultMotion();
                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField("遷移のデフォルト", EditorStyles.boldLabel);
                AnimatorDefaultSetting.TransitionDuration = EditorGUILayout.FloatField("Duration", AnimatorDefaultSetting.TransitionDuration);
                AnimatorDefaultSetting.TransitionHasFixedDuration = EditorGUILayout.Toggle("Has Fixed Duration", AnimatorDefaultSetting.TransitionHasFixedDuration);
                AnimatorDefaultSetting.TransitionHasExitTime = EditorGUILayout.Toggle("Has Exit Time", AnimatorDefaultSetting.TransitionHasExitTime);
                AnimatorDefaultSetting.TransitionExitTime = EditorGUILayout.FloatField("Exit Time", AnimatorDefaultSetting.TransitionExitTime);
                AnimatorDefaultSetting.TransitionOffset = EditorGUILayout.FloatField("Offset", AnimatorDefaultSetting.TransitionOffset);
                AnimatorDefaultSetting.TransitionCanTransitionToSelf = EditorGUILayout.Toggle("Can Transition To Self (AnyState)", AnimatorDefaultSetting.TransitionCanTransitionToSelf);
                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField("レイヤーのデフォルト", EditorStyles.boldLabel);
                AnimatorDefaultSetting.LayerDefaultWeight = EditorGUILayout.Slider("Weight", AnimatorDefaultSetting.LayerDefaultWeight, 0f, 1f);
                if (EditorGUI.EndChangeCheck()) { }
            }, new Rect(0, 0, position.width, position.height));
        }
    }
#endif

#if UNITY_2022_2_OR_NEWER
    internal sealed class AnimatorDefaultSettingProvider : SettingsProvider
    {
        public AnimatorDefaultSettingProvider(string path, SettingsScope scope)
            : base(path, scope) { }

        public override void OnGUI(string searchContext)
        {
            SamirinEditorStyleHelper.DrawWithBlueBackgroundForSettingsGui(() =>
            {
                EditorGUI.BeginChangeCheck();

                AnimatorDefaultSetting.Enabled = EditorGUILayout.Toggle("有効", AnimatorDefaultSetting.Enabled);
                EditorGUILayout.Space(4);

                EditorGUI.BeginDisabledGroup(!AnimatorDefaultSetting.Enabled);
                AnimatorDefaultSetting.WriteDefaults = EditorGUILayout.Toggle("Write Defaults", AnimatorDefaultSetting.WriteDefaults);
                AnimatorDefaultSetting.Mirror = EditorGUILayout.Toggle("Mirror", AnimatorDefaultSetting.Mirror);
                AnimatorDefaultSetting.Speed = EditorGUILayout.FloatField("Speed", AnimatorDefaultSetting.Speed);
                AnimatorDefaultSetting.StateDefaultMotion = (AnimationClip)EditorGUILayout.ObjectField("Default Motion (Clip)", AnimatorDefaultSetting.StateDefaultMotion, typeof(AnimationClip), false);
                if (GUILayout.Button("proxy_empty を選択"))
                    AnimatorDefaultSetting.SetProxyEmptyAsDefaultMotion();
                EditorGUI.EndDisabledGroup();

                EditorGUILayout.Space(8);
                EditorGUILayout.LabelField("遷移のデフォルト", EditorStyles.boldLabel);
                EditorGUI.BeginDisabledGroup(!AnimatorDefaultSetting.Enabled);
                AnimatorDefaultSetting.TransitionDuration = EditorGUILayout.FloatField("Duration", AnimatorDefaultSetting.TransitionDuration);
                AnimatorDefaultSetting.TransitionHasFixedDuration = EditorGUILayout.Toggle("Has Fixed Duration", AnimatorDefaultSetting.TransitionHasFixedDuration);
                AnimatorDefaultSetting.TransitionHasExitTime = EditorGUILayout.Toggle("Has Exit Time", AnimatorDefaultSetting.TransitionHasExitTime);
                AnimatorDefaultSetting.TransitionExitTime = EditorGUILayout.FloatField("Exit Time", AnimatorDefaultSetting.TransitionExitTime);
                AnimatorDefaultSetting.TransitionOffset = EditorGUILayout.FloatField("Offset", AnimatorDefaultSetting.TransitionOffset);
                EditorGUILayout.Space(2);
                AnimatorDefaultSetting.TransitionCanTransitionToSelf = EditorGUILayout.Toggle("Can Transition To Self (AnyState)", AnimatorDefaultSetting.TransitionCanTransitionToSelf);
                EditorGUI.EndDisabledGroup();

                EditorGUILayout.Space(8);
                EditorGUILayout.LabelField("レイヤーのデフォルト", EditorStyles.boldLabel);
                EditorGUI.BeginDisabledGroup(!AnimatorDefaultSetting.Enabled);
                AnimatorDefaultSetting.LayerDefaultWeight = EditorGUILayout.Slider("Weight", AnimatorDefaultSetting.LayerDefaultWeight, 0f, 1f);
                EditorGUI.EndDisabledGroup();

                EditorGUILayout.Space(8);
                EditorGUILayout.HelpBox(
                    "新規ステート・遷移を作成した際に、上記の値が初期設定として適用されます。\n" +
                    "Write Defaults を false にすると、VRChat 等で推奨される設定になります。",
                    MessageType.Info);

                if (EditorGUI.EndChangeCheck()) { }
            });
        }

        [SettingsProvider]
        public static SettingsProvider Create()
        {
            return new AnimatorDefaultSettingProvider(
                "Preferences/Samirin Editor Tools/Animator Default Setting",
                SettingsScope.User);
        }
    }
#endif

#if !UNITY_2022_2_OR_NEWER
    [CustomEditor(typeof(AnimatorDefaultSettingWindow))]
    internal sealed class AnimatorDefaultSettingWindowEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            EditorGUILayout.LabelField("Animator Default Setting", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);
            AnimatorDefaultSetting.Enabled = EditorGUILayout.Toggle("有効", AnimatorDefaultSetting.Enabled);
            EditorGUI.BeginDisabledGroup(!AnimatorDefaultSetting.Enabled);
            AnimatorDefaultSetting.WriteDefaults = EditorGUILayout.Toggle("Write Defaults", AnimatorDefaultSetting.WriteDefaults);
            AnimatorDefaultSetting.Mirror = EditorGUILayout.Toggle("Mirror", AnimatorDefaultSetting.Mirror);
            AnimatorDefaultSetting.Speed = EditorGUILayout.FloatField("Speed", AnimatorDefaultSetting.Speed);
            AnimatorDefaultSetting.StateDefaultMotion = (AnimationClip)EditorGUILayout.ObjectField("Default Motion (Clip)", AnimatorDefaultSetting.StateDefaultMotion, typeof(AnimationClip), false);
            if (GUILayout.Button("proxy_empty を選択"))
                AnimatorDefaultSetting.SetProxyEmptyAsDefaultMotion();
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("遷移のデフォルト", EditorStyles.miniBoldLabel);
            AnimatorDefaultSetting.TransitionDuration = EditorGUILayout.FloatField("Duration", AnimatorDefaultSetting.TransitionDuration);
            AnimatorDefaultSetting.TransitionHasFixedDuration = EditorGUILayout.Toggle("Has Fixed Duration", AnimatorDefaultSetting.TransitionHasFixedDuration);
            AnimatorDefaultSetting.TransitionHasExitTime = EditorGUILayout.Toggle("Has Exit Time", AnimatorDefaultSetting.TransitionHasExitTime);
            AnimatorDefaultSetting.TransitionExitTime = EditorGUILayout.FloatField("Exit Time", AnimatorDefaultSetting.TransitionExitTime);
            AnimatorDefaultSetting.TransitionOffset = EditorGUILayout.FloatField("Offset", AnimatorDefaultSetting.TransitionOffset);
            AnimatorDefaultSetting.TransitionCanTransitionToSelf = EditorGUILayout.Toggle("Can Transition To Self (AnyState)", AnimatorDefaultSetting.TransitionCanTransitionToSelf);
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("レイヤーのデフォルト", EditorStyles.miniBoldLabel);
            AnimatorDefaultSetting.LayerDefaultWeight = EditorGUILayout.Slider("Weight", AnimatorDefaultSetting.LayerDefaultWeight, 0f, 1f);
            EditorGUI.EndDisabledGroup();
        }
    }
#endif
}