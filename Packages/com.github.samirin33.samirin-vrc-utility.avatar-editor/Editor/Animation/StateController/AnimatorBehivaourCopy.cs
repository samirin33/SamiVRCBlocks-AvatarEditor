using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;

/// <summary>
/// Animatorのステートにアタッチされた StateMachineBehaviour を右クリックメニューから
/// コピー・新規ペースト・値のペーストできるようにするエディタ拡張。
/// ステート自体のまとめてコピー・ペーストにも対応。
/// </summary>
public static class AnimatorBehivaourCopy
{
    private const string EditorPrefsKeyJson = "SamirinEditorTools.AnimatorBehaviourCopy.Json";
    private const string EditorPrefsKeyType = "SamirinEditorTools.AnimatorBehaviourCopy.Type";

    // ステート全体コピー用
    private const string EditorPrefsKeyStateCopy = "SamirinEditorTools.AnimatorStateCopy.Data";

    /// <summary>コピーされたBehaviourのJSON（EditorPrefsに保存し、セッションをまたいで使用可能）</summary>
    private static string CopiedJson
    {
        get => EditorPrefs.GetString(EditorPrefsKeyJson, "");
        set => EditorPrefs.SetString(EditorPrefsKeyJson, value ?? "");
    }

    /// <summary>コピーされたBehaviourの型アセンブリ修飾名</summary>
    private static string CopiedTypeName
    {
        get => EditorPrefs.GetString(EditorPrefsKeyType, "");
        set => EditorPrefs.SetString(EditorPrefsKeyType, value ?? "");
    }

    /// <summary>コピー済みか（型が一致するペーストが可能か）</summary>
    public static bool HasCopiedBehaviour => !string.IsNullOrEmpty(CopiedTypeName) && !string.IsNullOrEmpty(CopiedJson);

    /// <summary>コピーされた型のアセンブリ修飾名を取得（ペースト用）</summary>
    public static string GetCopiedTypeName() => CopiedTypeName;

    /// <summary>コピーされた型と指定した型が一致するか</summary>
    public static bool IsCopiedTypeMatch(Type behaviourType)
    {
        if (behaviourType == null || string.IsNullOrEmpty(CopiedTypeName)) return false;
        return behaviourType.AssemblyQualifiedName == CopiedTypeName;
    }

    /// <summary>Behaviourのシリアライズ済みデータをコピー（クリップボード用の内部保存）</summary>
    public static void Copy(StateMachineBehaviour behaviour)
    {
        if (behaviour == null) return;
        try
        {
            CopiedTypeName = behaviour.GetType().AssemblyQualifiedName;
            CopiedJson = EditorJsonUtility.ToJson(behaviour);
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
        }
    }

    /// <summary>同じステートに同じ型のBehaviourを新規追加して値をペースト</summary>
    public static void PasteAsNew(AnimatorState state, Type behaviourType)
    {
        if (state == null || behaviourType == null || !IsCopiedTypeMatch(behaviourType)) return;
        if (!typeof(StateMachineBehaviour).IsAssignableFrom(behaviourType)) return;
        try
        {
            var newBehaviour = state.AddStateMachineBehaviour(behaviourType);
            EditorJsonUtility.FromJsonOverwrite(CopiedJson, newBehaviour);
            var ctx = AnimatorController.FindStateMachineBehaviourContext(newBehaviour);
            if (ctx != null && ctx.Length > 0 && ctx[0].animatorController != null)
                EditorUtility.SetDirty(ctx[0].animatorController);
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
        }
    }

    /// <summary>既存のBehaviourに値を上書きペースト</summary>
    public static void PasteValues(StateMachineBehaviour destination)
    {
        if (destination == null || !IsCopiedTypeMatch(destination.GetType())) return;
        try
        {
            Undo.RecordObject(destination, "Paste StateMachineBehaviour Values");
            EditorJsonUtility.FromJsonOverwrite(CopiedJson, destination);
            var context = AnimatorController.FindStateMachineBehaviourContext(destination);
            if (context != null && context.Length > 0 && context[0].animatorController != null)
                EditorUtility.SetDirty(context[0].animatorController);
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
        }
    }

    #region ステート全体のコピー・ペースト

    [Serializable]
    private class CopiedStateData
    {
        public string stateJson;
        public List<BehaviourEntry> behaviours = new List<BehaviourEntry>();
    }

    [Serializable]
    private class BehaviourEntry
    {
        public string typeName;
        public string json;
    }

    /// <summary>ステート全体をコピー済みか</summary>
    public static bool HasCopiedState
    {
        get
        {
            var s = EditorPrefs.GetString(EditorPrefsKeyStateCopy, "");
            return !string.IsNullOrEmpty(s);
        }
    }

    /// <summary>Animatorウィンドウから選択中のステートを取得（reflection）</summary>
    public static AnimatorState GetSelectedAnimatorState(out AnimatorController controller, out int layerIndex)
    {
        controller = null;
        layerIndex = 0;
        // フォールバック: Inspector でステートが選択されている場合
        if (Selection.activeObject is AnimatorState fallbackState)
        {
            AnimatorController ctrl = null;
            if (fallbackState.behaviours != null && fallbackState.behaviours.Length > 0)
            {
                var ctx = AnimatorController.FindStateMachineBehaviourContext(fallbackState.behaviours[0]);
                if (ctx != null && ctx.Length > 0) ctrl = ctx[0].animatorController;
            }
            if (ctrl == null)
            {
                var path = AssetDatabase.GetAssetPath(fallbackState);
                if (!string.IsNullOrEmpty(path)) ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
            }
            if (ctrl != null)
            {
                controller = ctrl;
                for (int i = 0; i < controller.layers.Length; i++)
                {
                    if (Array.Exists(controller.layers[i].stateMachine.states, s => s.state == fallbackState))
                    {
                        layerIndex = i;
                        return fallbackState;
                    }
                }
            }
        }

        var window = EditorWindow.focusedWindow;
        if (window == null) return null;
        var type = window.GetType();
        if (type.Name != "AnimatorControllerWindow" && !type.FullName.Contains("AnimatorController"))
            return null;

        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        var controllerProp = type.GetProperty("animatorController", flags);
        var controllerField = type.GetField("animatorController", flags);
        if (controllerProp != null)
            controller = controllerProp.GetValue(window) as AnimatorController;
        else if (controllerField != null)
            controller = controllerField.GetValue(window) as AnimatorController;
        if (controller == null) return null;

        var layerProp = type.GetProperty("selectedLayerIndex", flags);
        var layerField = type.GetField("m_SelectedLayerIndex", flags) ?? type.GetField("selectedLayerIndex", flags);
        if (layerProp != null)
        {
            var l = layerProp.GetValue(window);
            if (l is int li) layerIndex = li;
        }
        else if (layerField != null)
        {
            var l = layerField.GetValue(window);
            if (l is int li) layerIndex = li;
        }
        if (layerIndex < 0 || layerIndex >= controller.layers.Length) return null;

        var stateProp = type.GetProperty("selectedState", flags);
        var stateField = type.GetField("m_SelectedState", flags) ?? type.GetField("selectedState", flags);
        if (stateProp != null)
        {
            var s = stateProp.GetValue(window);
            return s as AnimatorState;
        }
        if (stateField != null)
        {
            var s = stateField.GetValue(window);
            return s as AnimatorState;
        }
        return null;
    }

    /// <summary>選択中のコントローラーとレイヤーを取得（ステートがなくても）</summary>
    public static bool GetFocusedAnimatorContext(out AnimatorController controller, out int layerIndex)
    {
        controller = null;
        layerIndex = 0;
        var window = EditorWindow.focusedWindow;
        if (window != null)
        {
            var type = window.GetType();
            if (type.Name == "AnimatorControllerWindow" || type.FullName.Contains("AnimatorController"))
            {
                const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
                var controllerProp = type.GetProperty("animatorController", flags);
                var controllerField = type.GetField("animatorController", flags);
                if (controllerProp != null)
                    controller = controllerProp.GetValue(window) as AnimatorController;
                else if (controllerField != null)
                    controller = controllerField.GetValue(window) as AnimatorController;
                if (controller != null)
                {
                    var layerProp = type.GetProperty("selectedLayerIndex", flags);
                    var layerField = type.GetField("m_SelectedLayerIndex", flags) ?? type.GetField("selectedLayerIndex", flags);
                    if (layerProp != null)
                    {
                        var l = layerProp.GetValue(window);
                        if (l is int li) layerIndex = li;
                    }
                    else if (layerField != null)
                    {
                        var l = layerField.GetValue(window);
                        if (l is int li) layerIndex = li;
                    }
                    return true;
                }
            }
        }
        // フォールバック: Project で Controller が選択されていればレイヤー 0 でペースト可能
        if (Selection.activeObject is AnimatorController ctrl)
        {
            controller = ctrl;
            layerIndex = 0;
            return true;
        }
        return false;
    }

    /// <summary>ステート全体（設定＋全Behaviour）をコピー</summary>
    public static void CopyState(AnimatorState state)
    {
        if (state == null) return;
        try
        {
            var data = new CopiedStateData
            {
                stateJson = EditorJsonUtility.ToJson(state)
            };
            foreach (var b in state.behaviours)
            {
                if (b == null) continue;
                data.behaviours.Add(new BehaviourEntry
                {
                    typeName = b.GetType().AssemblyQualifiedName,
                    json = EditorJsonUtility.ToJson(b)
                });
            }
            EditorPrefs.SetString(EditorPrefsKeyStateCopy, JsonUtility.ToJson(data));
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
        }
    }

    /// <summary>コピーしたステートを同じレイヤーのステートマシンに新規ペースト</summary>
    public static void PasteState(AnimatorController controller, int layerIndex, string newStateName = null)
    {
        if (controller == null || !HasCopiedState) return;
        if (layerIndex < 0 || layerIndex >= controller.layers.Length) return;
        var stateMachine = controller.layers[layerIndex].stateMachine;
        try
        {
            var json = EditorPrefs.GetString(EditorPrefsKeyStateCopy, "");
            var data = JsonUtility.FromJson<CopiedStateData>(json);
            if (data == null || string.IsNullOrEmpty(data.stateJson)) return;

            var baseName = string.IsNullOrEmpty(newStateName) ? "New State" : newStateName;
            var name = baseName;
            var n = 0;
            while (Array.Exists(stateMachine.states, s => s.state.name == name))
                name = baseName + " (" + (++n) + ")";

            Undo.RegisterCompleteObjectUndo(controller, "Paste Animator State");
            var newState = stateMachine.AddState(name);
            EditorJsonUtility.FromJsonOverwrite(data.stateJson, newState);
            newState.name = name;

            foreach (var entry in data.behaviours ?? new List<BehaviourEntry>())
            {
                if (string.IsNullOrEmpty(entry.typeName) || string.IsNullOrEmpty(entry.json)) continue;
                var t = Type.GetType(entry.typeName, false);
                if (t == null || !typeof(StateMachineBehaviour).IsAssignableFrom(t)) continue;
                var b = newState.AddStateMachineBehaviour(t);
                EditorJsonUtility.FromJsonOverwrite(entry.json, b);
            }
            EditorUtility.SetDirty(controller);
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
        }
    }

    #endregion

    // #region ステート用 MenuItem（Tools/Samirin/... で右クリックメニュー風に利用）

    // [MenuItem("samirin33 Editor Tools/Animator State/Copy State", false, 102)]
    // private static void MenuCopyState()
    // {
    //     if (GetSelectedAnimatorState(out var controller, out _) is AnimatorState state)
    //         CopyState(state);
    // }

    // [MenuItem("samirin33 Editor Tools/Animator State/Copy State", true)]
    // private static bool MenuCopyStateValidate()
    // {
    //     return GetSelectedAnimatorState(out _, out _) != null;
    // }

    // [MenuItem("samirin33 Editor Tools/Animator State/Paste State", false, 103)]
    // private static void MenuPasteState()
    // {
    //     if (GetFocusedAnimatorContext(out var controller, out var layerIndex))
    //         PasteState(controller, layerIndex);
    // }

    // [MenuItem("samirin33 Editor Tools/Animator State/Paste State", true)]
    // private static bool MenuPasteStateValidate()
    // {
    //     return HasCopiedState && GetFocusedAnimatorContext(out _, out _);
    // }

    // [MenuItem("Assets/Samirin Editor Tools/Animator State/Paste State", false, 1201)]
    // private static void AssetsMenuPasteState()
    // {
    //     if (Selection.activeObject is AnimatorController ctrl)
    //         PasteState(ctrl, 0);
    // }

    // [MenuItem("Assets/Samirin Editor Tools/Animator State/Paste State", true)]
    // private static bool AssetsMenuPasteStateValidate()
    // {
    //     return HasCopiedState && Selection.activeObject is AnimatorController;
    // }

    // #endregion

    #region CONTEXT メニュー（コンポーネント右クリック時・VRChat 等の派生型でも表示）

    /// <summary>
    /// コンポーネント右クリック（⋮メニュー）に Copy/Paste を追加。
    /// CONTEXT/StateMachineBehaviour は派生型（VRCAnimatorLayerControl 等）でも表示される。
    /// </summary>
    [MenuItem("CONTEXT/StateMachineBehaviour/Copy", false, 300)]
    private static void ContextCopy(MenuCommand command)
    {
        if (command.context is StateMachineBehaviour b) Copy(b);
    }

    [MenuItem("CONTEXT/StateMachineBehaviour/Copy", true)]
    private static bool ContextCopyValidate(MenuCommand command) => command.context is StateMachineBehaviour;

    [MenuItem("CONTEXT/StateMachineBehaviour/Paste as New", false, 301)]
    private static void ContextPasteAsNew(MenuCommand command)
    {
        if (command.context is StateMachineBehaviour b) ContextPasteAsNewImpl(b);
    }

    [MenuItem("CONTEXT/StateMachineBehaviour/Paste as New", true)]
    private static bool ContextPasteAsNewValidate(MenuCommand command)
    {
        if (command.context is not StateMachineBehaviour b) return false;
        return HasCopiedBehaviour && IsCopiedTypeMatch(b.GetType());
    }

    [MenuItem("CONTEXT/StateMachineBehaviour/Paste Values", false, 302)]
    private static void ContextPasteValues(MenuCommand command)
    {
        if (command.context is StateMachineBehaviour b)
        {
            Undo.RecordObject(b, "Paste StateMachineBehaviour Values");
            PasteValues(b);
        }
    }

    [MenuItem("CONTEXT/StateMachineBehaviour/Paste Values", true)]
    private static bool ContextPasteValuesValidate(MenuCommand command)
    {
        if (command.context is not StateMachineBehaviour b) return false;
        return HasCopiedBehaviour && IsCopiedTypeMatch(b.GetType());
    }

    private static void ContextPasteAsNewImpl(StateMachineBehaviour behaviour)
    {
        var context = AnimatorController.FindStateMachineBehaviourContext(behaviour);
        if (context == null || context.Length == 0) return;
        if (context[0].animatorObject is not AnimatorState state) return;
        var type = Type.GetType(GetCopiedTypeName() ?? "", false);
        if (type == null) return;
        var controller = context[0].animatorController;
        if (controller != null)
            Undo.RegisterCompleteObjectUndo(controller, "Paste StateMachineBehaviour As New");
        PasteAsNew(state, type);
    }

    #endregion
}

[CustomEditor(typeof(StateMachineBehaviour), true)]
internal sealed class StateMachineBehaviourCopyPasteEditor : Editor
{
    public override void OnInspectorGUI()
    {
        // 描画開始位置を記録（右クリック判定用）
        var startRect = GUILayoutUtility.GetRect(0, 0);

        DrawDefaultInspector();

        // このBehaviourのインスペクタ領域内での右クリックでコンテキストメニューを表示
        if (Event.current.type == EventType.ContextClick)
        {
            var endRect = GUILayoutUtility.GetLastRect();
            var fullRect = new Rect(startRect.x, startRect.y, Mathf.Max(startRect.width, endRect.width), Mathf.Max(0, endRect.yMax - startRect.y));
            if (fullRect.height < 1f)
                fullRect = endRect;
            if (fullRect.Contains(Event.current.mousePosition))
            {
                Event.current.Use();
                ShowContextMenu(target as StateMachineBehaviour);
            }
        }
    }

    private static void ShowContextMenu(StateMachineBehaviour behaviour)
    {
        if (behaviour == null) return;

        var menu = new GenericMenu();
        menu.AddItem(new GUIContent("Copy"), false, () =>
        {
            AnimatorBehivaourCopy.Copy(behaviour);
        });

        var hasCopy = AnimatorBehivaourCopy.HasCopiedBehaviour;
        var typeMatch = AnimatorBehivaourCopy.IsCopiedTypeMatch(behaviour.GetType());
        var canPaste = hasCopy && typeMatch;

        if (canPaste)
        {
            menu.AddItem(new GUIContent("Paste as New"), false, () => PasteAsNewAtState(behaviour));
            menu.AddItem(new GUIContent("Paste Values"), false, () =>
            {
                Undo.RecordObject(behaviour, "Paste StateMachineBehaviour Values");
                AnimatorBehivaourCopy.PasteValues(behaviour);
            });
        }
        else
        {
            menu.AddDisabledItem(new GUIContent("Paste as New"));
            menu.AddDisabledItem(new GUIContent("Paste Values"));
        }

        menu.ShowAsContext();
    }

    private static void PasteAsNewAtState(StateMachineBehaviour behaviour)
    {
        var context = AnimatorController.FindStateMachineBehaviourContext(behaviour);
        if (context == null || context.Length == 0) return;
        var animatorObject = context[0].animatorObject;
        if (!(animatorObject is AnimatorState state))
            return;
        var type = Type.GetType(AnimatorBehivaourCopy.GetCopiedTypeName() ?? "", false);
        if (type == null) return;
        var controller = context[0].animatorController;
        if (controller != null)
            Undo.RegisterCompleteObjectUndo(controller, "Paste StateMachineBehaviour As New");
        AnimatorBehivaourCopy.PasteAsNew(state, type);
    }
}
