using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.Dynamics;
using VRC.SDK3.Dynamics.Contact.Components;
using VRC.SDK3.Dynamics.PhysBone.Components;
#if SAMIVRCBLOCKS_VRCSDK_CONTACT_ADVANCED_PARAMS
using AdvancedParameter = VRC.Dynamics.ContactReceiver.AdvancedParameter;
#endif

namespace Samirin33.AvatarEditor.Tools.Editor
{
    /// <summary>
    /// VRC PhysBone / Contact Receiver の Animation パラメーター名を
    /// 公式 Inspector 末尾に Harmony postfix で表示し、コピー／開いている Animator への追加を行う。
    /// </summary>
    /// <remarks>
    /// PhysBone: https://creators.vrchat.com/common-components/physbones/
    /// Contact: https://creators.vrchat.com/common-components/contacts/
    /// Contact Advanced Parameters は SDK 3.10.6+（versionDefine）のみ。
    /// </remarks>
    [InitializeOnLoad]
    internal static class VrcDynamicsParameterCopy
    {
        private const string HarmonyId = "com.github.samirin33.samivrcblocks-avatar-editor.vrc-dynamics-param-copy";
        private const int InstallRetryLimit = 8;

        // https://creators.vrchat.com/common-components/physbones/
        private static readonly ParamDef[] PhysBoneOutputs =
        {
            new ParamDef("_IsGrabbed", AnimatorControllerParameterType.Bool),
            new ParamDef("_IsPosed", AnimatorControllerParameterType.Bool),
            new ParamDef("_Angle", AnimatorControllerParameterType.Float),
            new ParamDef("_Stretch", AnimatorControllerParameterType.Float),
            new ParamDef("_Squish", AnimatorControllerParameterType.Float),
        };

#if SAMIVRCBLOCKS_VRCSDK_CONTACT_ADVANCED_PARAMS
        // https://creators.vrchat.com/common-components/contacts/#advanced-parameters
        private static readonly (AdvancedParameter flag, ParamDef def)[] ContactAdvancedOutputs =
        {
            (AdvancedParameter.SenderType, new ParamDef(ContactReceiver.PARAM_SENDER_TYPE, AnimatorControllerParameterType.Int)),
            (AdvancedParameter.SenderLocalPosition, new ParamDef(ContactReceiver.PARAM_SENDER_LOCAL_POSITION_X, AnimatorControllerParameterType.Float)),
            (AdvancedParameter.SenderLocalPosition, new ParamDef(ContactReceiver.PARAM_SENDER_LOCAL_POSITION_Y, AnimatorControllerParameterType.Float)),
            (AdvancedParameter.SenderLocalPosition, new ParamDef(ContactReceiver.PARAM_SENDER_LOCAL_POSITION_Z, AnimatorControllerParameterType.Float)),
            (AdvancedParameter.SenderLossyScale, new ParamDef(ContactReceiver.PARAM_SENDER_LOSSY_SCALE_X, AnimatorControllerParameterType.Float)),
            (AdvancedParameter.SenderLossyScale, new ParamDef(ContactReceiver.PARAM_SENDER_LOSSY_SCALE_Y, AnimatorControllerParameterType.Float)),
            (AdvancedParameter.SenderLossyScale, new ParamDef(ContactReceiver.PARAM_SENDER_LOSSY_SCALE_Z, AnimatorControllerParameterType.Float)),
            (AdvancedParameter.SenderVelocity, new ParamDef(ContactReceiver.PARAM_SENDER_VELOCITY_X, AnimatorControllerParameterType.Float)),
            (AdvancedParameter.SenderVelocity, new ParamDef(ContactReceiver.PARAM_SENDER_VELOCITY_Y, AnimatorControllerParameterType.Float)),
            (AdvancedParameter.SenderVelocity, new ParamDef(ContactReceiver.PARAM_SENDER_VELOCITY_Z, AnimatorControllerParameterType.Float)),
            (AdvancedParameter.SenderVelocity, new ParamDef(ContactReceiver.PARAM_SENDER_VELOCITY_MAGNITUDE, AnimatorControllerParameterType.Float)),
            (AdvancedParameter.PointSeparation, new ParamDef(ContactReceiver.PARAM_POINT_SEPARATION, AnimatorControllerParameterType.Float)),
            (AdvancedParameter.ProximitySpeed, new ParamDef(ContactReceiver.PARAM_PROXIMITY_SPEED, AnimatorControllerParameterType.Float)),
        };
#endif

        private static Harmony _harmony;
        private static int _retries;
        private static bool _installed;

        private readonly struct ParamDef
        {
            public readonly string SuffixOrName;
            public readonly AnimatorControllerParameterType Type;
            public readonly bool IsAbsoluteName;

            public ParamDef(string suffixOrName, AnimatorControllerParameterType type, bool isAbsoluteName = false)
            {
                SuffixOrName = suffixOrName;
                Type = type;
                IsAbsoluteName = isAbsoluteName;
            }

            public string Resolve(string prefix) =>
                IsAbsoluteName ? SuffixOrName : prefix + SuffixOrName;

            public string TypeLabel => Type.ToString();
        }

        static VrcDynamicsParameterCopy()
        {
            EditorApplication.delayCall += TryInstall;
        }

        private static void TryInstall()
        {
            if (_installed) return;

            try
            {
                Install();
                _installed = true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[SamiVRCBlocks] VrcDynamicsParameterCopy: Harmony patch 失敗 ({ex.GetType().Name}: {ex.Message})");
                if (++_retries <= InstallRetryLimit)
                    EditorApplication.delayCall += TryInstall;
            }
        }

        private static void Install()
        {
            _harmony?.UnpatchAll(HarmonyId);
            _harmony = new Harmony(HarmonyId);

            AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeReload;
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeReload;

            // 実際の型名は VRC.SDK3.Dynamics.PhysBone.VRCPhysBoneEditor（...Editor 名前空間ではない）
            var physBoneEditor = FindTypeByName("VRCPhysBoneEditor", "VRC.SDK3.Dynamics.PhysBone.Editor")
                                 ?? FindTypeByName("VRCPhysBoneEditor", null);
            // Contact Receiver は OnInspectorGUI を継承し、本体描画は DrawInspectorBody
            var contactEditor = FindTypeByName("VRCContactReceiverEditor", "VRC.SDK3.Dynamics.Contact.Editor")
                                ?? FindTypeByName("VRCContactReceiverEditor", null);

            PatchMethod(physBoneEditor, "OnInspectorGUI", nameof(PostfixPhysBoneInspector));
            // DrawInspectorBody が無ければ基底の OnInspectorGUI に postfix
            if (AccessTools.DeclaredMethod(contactEditor, "DrawInspectorBody") != null)
                PatchMethod(contactEditor, "DrawInspectorBody", nameof(PostfixContactInspector));
            else
                PatchMethod(contactEditor, "OnInspectorGUI", nameof(PostfixContactInspector));

            Debug.Log($"[SamiVRCBlocks] VrcDynamicsParameterCopy: Harmony patched ({physBoneEditor.FullName}, {contactEditor.FullName})");
        }

        private static void OnBeforeReload()
        {
            AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeReload;
            try
            {
                _harmony?.UnpatchAll(HarmonyId);
            }
            catch
            {
                // リロード直前の解除失敗で止めない
            }

            _harmony = null;
            _installed = false;
        }

        private static Type FindTypeByName(string typeName, string assemblyName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (!string.IsNullOrEmpty(assemblyName)
                    && !string.Equals(assembly.GetName().Name, assemblyName, StringComparison.Ordinal))
                    continue;

                Type[] types;
                try
                {
                    types = assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException ex)
                {
                    types = ex.Types.Where(t => t != null).ToArray();
                }

                for (var i = 0; i < types.Length; i++)
                {
                    if (types[i] != null && types[i].Name == typeName)
                        return types[i];
                }
            }

            return null;
        }

        private static void PatchMethod(Type editorType, string methodName, string postfixMethodName)
        {
            if (editorType == null)
                throw new TypeLoadException($"Editor type not found for patch target of {postfixMethodName}");

            var target = AccessTools.DeclaredMethod(editorType, methodName)
                         ?? AccessTools.Method(editorType, methodName, Type.EmptyTypes);
            if (target == null)
                throw new MissingMethodException(editorType.FullName, methodName);

            var postfix = AccessTools.Method(typeof(VrcDynamicsParameterCopy), postfixMethodName);
            _harmony.Patch(target, postfix: new HarmonyMethod(postfix));
        }

        // Harmony postfix 用（Editor インスタンスを __instance で受け取る）
        public static void PostfixPhysBoneInspector(UnityEditor.Editor __instance)
        {
            if (__instance == null || __instance.targets == null) return;
            var physBones = __instance.targets.OfType<VRCPhysBone>().ToArray();
            if (physBones.Length == 0) return;
            DrawPanel(CollectPhysBoneEntries(physBones), "PhysBone");
        }

        public static void PostfixContactInspector(UnityEditor.Editor __instance)
        {
            if (__instance == null || __instance.targets == null) return;
            var receivers = __instance.targets.OfType<VRCContactReceiver>().ToArray();
            if (receivers.Length == 0) return;

            if (receivers.Length == 1)
                EditorGUILayout.LabelField("Receiver Type", receivers[0].receiverType.ToString(), EditorStyles.miniLabel);

            DrawPanel(CollectContactEntries(receivers), "Contact Receiver");
        }

        private static List<(string name, AnimatorControllerParameterType type)> CollectPhysBoneEntries(VRCPhysBone[] physBones)
        {
            var list = new List<(string, AnimatorControllerParameterType)>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var pb in physBones)
            {
                var prefix = pb.parameter ?? string.Empty;
                if (string.IsNullOrEmpty(prefix)) continue;
                foreach (var def in PhysBoneOutputs)
                {
                    var name = def.Resolve(prefix);
                    if (!seen.Add(name)) continue;
                    list.Add((name, def.Type));
                }
            }

            return list;
        }

        private static List<(string name, AnimatorControllerParameterType type)> CollectContactEntries(VRCContactReceiver[] receivers)
        {
            var list = new List<(string, AnimatorControllerParameterType)>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var receiver in receivers)
            {
                var prefix = receiver.parameter ?? string.Empty;
                if (string.IsNullOrEmpty(prefix)) continue;

                var baseType = receiver.receiverType == ContactReceiver.ReceiverType.Proximity
                    ? AnimatorControllerParameterType.Float
                    : AnimatorControllerParameterType.Bool;
                if (seen.Add(prefix))
                    list.Add((prefix, baseType));

                foreach (var entry in CollectContactAdvancedEntries(receiver))
                {
                    if (!seen.Add(entry.name)) continue;
                    list.Add(entry);
                }
            }

            return list;
        }

        private static IEnumerable<(string name, AnimatorControllerParameterType type)> CollectContactAdvancedEntries(ContactReceiver receiver)
        {
#if SAMIVRCBLOCKS_VRCSDK_CONTACT_ADVANCED_PARAMS
            if (string.IsNullOrEmpty(receiver.parameter))
                yield break;

            var flags = receiver.advancedParameters;
            foreach (var (flag, def) in ContactAdvancedOutputs)
            {
                if ((flags & flag) == 0) continue;
                yield return (def.Resolve(receiver.parameter), def.Type);
            }

            if ((flags & AdvancedParameter.CollisionTags) == 0 || receiver.collisionTags == null)
                yield break;

            foreach (var tag in receiver.collisionTags)
            {
                if (string.IsNullOrWhiteSpace(tag)) continue;
                yield return (
                    receiver.parameter + ContactReceiver.PARAM_COLLISION_TAG_PREFIX + tag,
                    AnimatorControllerParameterType.Bool);
            }
#else
            yield break;
#endif
        }

        private static void DrawPanel(List<(string name, AnimatorControllerParameterType type)> entries, string kind)
        {
            EditorGUILayout.Space(6f);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Animation Parameter", EditorStyles.boldLabel);

            var controller = GetOpenAnimatorController();
            if (controller != null)
                EditorGUILayout.LabelField("Animator", controller.name, EditorStyles.miniLabel);
            else
                EditorGUILayout.HelpBox("Animator ウィンドウで Controller を開くと「追加」が使えます。", MessageType.Info);

            if (entries == null || entries.Count == 0)
            {
                EditorGUILayout.HelpBox($"{kind} の Parameter が未設定です。", MessageType.Info);
                EditorGUILayout.EndVertical();
                return;
            }

            var existing = controller != null
                ? new HashSet<string>(controller.parameters.Select(p => p.name), StringComparer.Ordinal)
                : null;

            foreach (var (name, type) in entries)
            {
                var already = existing != null && existing.Contains(name);
                DrawParamRow(name, type, controller, already);
            }

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("すべてコピー", EditorStyles.miniButton))
                CopyToClipboard(string.Join("\n", entries.Select(e => e.name)));

            using (new EditorGUI.DisabledScope(controller == null))
            {
                if (GUILayout.Button("すべて追加", EditorStyles.miniButton))
                    AddParameters(controller, entries);
            }

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        private static void DrawParamRow(string name, AnimatorControllerParameterType type, AnimatorController controller, bool alreadyExists)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(type.ToString(), GUILayout.Width(48f));
            EditorGUILayout.SelectableLabel(name, EditorStyles.textField, GUILayout.Height(EditorGUIUtility.singleLineHeight));

            if (GUILayout.Button("Copy", EditorStyles.miniButton, GUILayout.Width(44f)))
                CopyToClipboard(name);

            using (new EditorGUI.DisabledScope(controller == null || alreadyExists))
            {
                var label = alreadyExists ? "済" : "追加";
                if (GUILayout.Button(label, EditorStyles.miniButton, GUILayout.Width(44f)) && !alreadyExists)
                    AddParameters(controller, new[] { (name, type) });
            }

            EditorGUILayout.EndHorizontal();
        }

        private static void AddParameters(
            AnimatorController controller,
            IEnumerable<(string name, AnimatorControllerParameterType type)> entries)
        {
            if (controller == null) return;

            Undo.RecordObject(controller, "Add Animator Parameters");
            var existing = new HashSet<string>(controller.parameters.Select(p => p.name), StringComparer.Ordinal);
            var added = 0;
            foreach (var (name, type) in entries)
            {
                if (string.IsNullOrEmpty(name) || !existing.Add(name))
                    continue;
                controller.AddParameter(name, type);
                added++;
            }

            if (added > 0)
            {
                EditorUtility.SetDirty(controller);
                AssetDatabase.SaveAssetIfDirty(controller);
                Debug.Log($"[SamiVRCBlocks] Animator「{controller.name}」にパラメーターを {added} 個追加しました。");
            }
            else
            {
                Debug.Log($"[SamiVRCBlocks] Animator「{controller.name}」に追加できる未登録パラメーターはありませんでした。");
            }
        }

        private static AnimatorController GetOpenAnimatorController()
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var typeNames = new[]
            {
                "UnityEditor.Graphs.AnimatorControllerTool, UnityEditor.Graphs",
                "UnityEditor.AnimatorControllerWindow, UnityEditor",
                "UnityEditor.AnimatorWindow, UnityEditor",
            };

            foreach (var typeName in typeNames)
            {
                var type = Type.GetType(typeName);
                if (type == null) continue;

                var windows = Resources.FindObjectsOfTypeAll(type);
                for (var i = 0; i < windows.Length; i++)
                {
                    var window = windows[i];
                    if (window == null) continue;
                    var wt = window.GetType();

                    var prop = wt.GetProperty("animatorController", flags) ?? wt.GetProperty("m_AnimatorController", flags);
                    if (prop != null)
                    {
                        var value = prop.GetValue(window) as AnimatorController;
                        if (value != null) return value;
                    }

                    var field = wt.GetField("animatorController", flags) ?? wt.GetField("m_AnimatorController", flags);
                    if (field != null)
                    {
                        var value = field.GetValue(window) as AnimatorController;
                        if (value != null) return value;
                    }
                }
            }

            if (Selection.activeObject is AnimatorController selected)
                return selected;

            return null;
        }

        private static void CopyToClipboard(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            EditorGUIUtility.systemCopyBuffer = text;
            Debug.Log($"[SamiVRCBlocks] Copied parameter: {text.Replace("\n", " / ")}");
        }
    }
}
