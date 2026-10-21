using System;
using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;

/// <summary>
/// Animatorのステートにアタッチされた StateMachineBehaviour を右クリックメニューから
/// コピー・新規ペースト・値のペーストできるようにするエディタ拡張。
/// </summary>
public static class AnimatorBehivaourCopy
{
    private const string EditorPrefsKeyJson = "SamirinEditorTools.AnimatorBehaviourCopy.Json";
    private const string EditorPrefsKeyType = "SamirinEditorTools.AnimatorBehaviourCopy.Type";

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

    /// <summary>同じステートに、コピーした型のBehaviourを新規追加して値をペースト（型一致不要）</summary>
    public static void PasteAsNew(AnimatorState state, Type behaviourType = null)
    {
        if (state == null || !HasCopiedBehaviour) return;

        var type = behaviourType;
        if (type == null || !IsCopiedTypeMatch(type))
            type = Type.GetType(CopiedTypeName ?? "", false);

        if (type == null || !typeof(StateMachineBehaviour).IsAssignableFrom(type)) return;
        try
        {
            var newBehaviour = AddStateMachineBehaviourWithUndo(state, type, "Paste StateMachineBehaviour As New");
            if (newBehaviour == null) return;
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

    /// <summary>ステートに Behaviour を追加し、追加したサブアセットと配列の変更を Undo に登録する。</summary>
    public static StateMachineBehaviour AddStateMachineBehaviourWithUndo(AnimatorState state, Type behaviourType, string undoLabel)
    {
        if (state == null || behaviourType == null) return null;
        Undo.RegisterCompleteObjectUndo(state, undoLabel);
        var behaviour = state.AddStateMachineBehaviour(behaviourType);
        if (behaviour != null)
            Undo.RegisterCreatedObjectUndo(behaviour, undoLabel);
        return behaviour;
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
        return command.context is StateMachineBehaviour && HasCopiedBehaviour;
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

        if (hasCopy)
            menu.AddItem(new GUIContent("Paste as New"), false, () => PasteAsNewAtState(behaviour));
        else
            menu.AddDisabledItem(new GUIContent("Paste as New"));

        if (hasCopy && typeMatch)
        {
            menu.AddItem(new GUIContent("Paste Values"), false, () =>
            {
                Undo.RecordObject(behaviour, "Paste StateMachineBehaviour Values");
                AnimatorBehivaourCopy.PasteValues(behaviour);
            });
        }
        else
        {
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
        AnimatorBehivaourCopy.PasteAsNew(state, type);
    }
}
