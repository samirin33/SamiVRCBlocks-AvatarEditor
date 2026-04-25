#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Samirin33.Editor;

namespace Samirin33.SamirinVRCUtility.AvatarEditor
{
    /// <summary>競合先のレイヤー＋クリップ一覧</summary>
    public class ConflictTargetInfo
    {
        public string LayerName;
        public List<AnimationClip> Clips = new List<AnimationClip>();
    }

    /// <summary>選択Clip側の競合パス1件（パス＋属性＋競合先リスト）</summary>
    public class ConflictEntry
    {
        public string Path;
        public string LastObjectName;
        public string PropertyName;
        public string TypeName;
        public List<ConflictTargetInfo> Targets = new List<ConflictTargetInfo>();
    }

    /// <summary>
    /// 多レイヤー競合している Transform パス＋属性の詳細を表示する専用ウィンドウ。
    /// 選択Clipの競合パスごとにボックスで囲み、最後のオブジェクト名・コンフリクト先をクリックで選択可能にする。
    /// </summary>
    internal class ClipConflictDetailsWindow : EditorWindow
    {
        private AnimationClip _clip;
        private List<ConflictEntry> _entries = new List<ConflictEntry>();
        private GameObject _root;
        private Vector2 _scroll;

        private GUIStyle _clickableLabelStyle;

        private void EnsureStyles()
        {
            if (_clickableLabelStyle != null) return;
            var labelSource = EditorStyles.label;
            _clickableLabelStyle = labelSource != null ? new GUIStyle(labelSource) { richText = true } : null;
        }

        public static void Open(AnimationClip clip, IReadOnlyList<ConflictEntry> entries, GameObject root)
        {
            var window = GetWindow<ClipConflictDetailsWindow>("Clip Conflict Details");
            window._clip = clip;
            window._entries = entries != null ? new List<ConflictEntry>(entries) : new List<ConflictEntry>();
            window._root = root;
            window.minSize = new Vector2(460, 280);
            window.Show();
            window.Focus();
        }

        private void OnEnable()
        {
            Undo.undoRedoPerformed += RefreshConflictEntries;
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= RefreshConflictEntries;
        }

        private void RefreshConflictEntries()
        {
            if (_clip == null || _root == null) return;
            var entries = AnimationClipSelector.GetConflictEntriesForClip(_clip, _root);
            _entries = entries != null ? new List<ConflictEntry>(entries) : new List<ConflictEntry>();
            Repaint();
        }

        private void OnGUI()
        {
            EnsureStyles();
            SamirinEditorStyleHelper.DrawWithBlueBackground(() =>
            {
                EditorGUILayout.LabelField("多レイヤー競合の詳細", EditorStyles.boldLabel);

                if (_clip != null)
                {
                    var clipStyle = new GUIStyle(EditorStyles.label) { fontStyle = FontStyle.Italic };
                    EditorGUILayout.LabelField("Clip: " + _clip.name, clipStyle);
                }

                EditorGUILayout.Space();

                if (_entries == null || _entries.Count == 0)
                {
                    SamirinEditorStyleHelper.DrawHelpBoxWithDefaultFont("表示可能な競合情報がありません。", MessageType.Info);
                    return;
                }

                _scroll = EditorGUILayout.BeginScrollView(_scroll);
                var toRemoveEntries = new List<ConflictEntry>();
                var toRemoveClips = new List<(ConflictTargetInfo target, AnimationClip clip)>();
                foreach (var entry in _entries)
                {
                    if (entry == null) continue;
                    DrawConflictEntry(entry, toRemoveEntries, toRemoveClips);
                    EditorGUILayout.Space(6);
                }
                foreach (var e in toRemoveEntries)
                    _entries.Remove(e);
                foreach (var (target, clip) in toRemoveClips)
                {
                    if (target?.Clips != null) target.Clips.Remove(clip);
                }
                // 個別削除で全Clipが消えたエントリは一覧からも削除（「コンフリクトプロパティを削除」だけ残らないようにする）
                var emptyEntries = new List<ConflictEntry>();
                foreach (var entry in _entries)
                {
                    if (entry?.Targets == null) { emptyEntries.Add(entry); continue; }
                    var hasAnyClip = false;
                    foreach (var t in entry.Targets)
                    {
                        if (t?.Clips != null && t.Clips.Count > 0) { hasAnyClip = true; break; }
                    }
                    if (!hasAnyClip) emptyEntries.Add(entry);
                }
                foreach (var e in emptyEntries)
                    _entries.Remove(e);

                if (toRemoveEntries.Count > 0 || toRemoveClips.Count > 0 || emptyEntries.Count > 0)
                    Repaint();
                EditorGUILayout.EndScrollView();
            }, new Rect(0, 0, position.width, position.height));
        }

        private void DrawConflictEntry(ConflictEntry entry, List<ConflictEntry> toRemoveEntries, List<(ConflictTargetInfo target, AnimationClip clip)> toRemoveClips)
        {
            var outerStyle = GUI.skin != null && GUI.skin.box != null ? GUI.skin.box : EditorStyles.helpBox;
            EditorGUILayout.BeginVertical(outerStyle);

            SamirinEditorStyleHelper.DrawWithDefaultFont(() =>
            {
                // 1行目: 選択Clipの競合パスの最後のオブジェクト名（クリックでヒエラルキーで選択）
                var lastName = string.IsNullOrEmpty(entry.LastObjectName) ? entry.Path : entry.LastObjectName;
                var pathLabel = string.IsNullOrEmpty(entry.PropertyName)
                    ? lastName
                    : $"{lastName}.{entry.PropertyName}";
                if (!string.IsNullOrEmpty(entry.TypeName))
                    pathLabel += $" [{entry.TypeName}]";

                var pathRect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
                var pathBtnStyle = _clickableLabelStyle ?? EditorStyles.label;
                if (GUI.Button(pathRect, pathLabel, pathBtnStyle))
                {
                    SelectTransformAtPath(entry.Path);
                    AnimationClipSelector.SetAnimationWindowToClip(_clip);
                }
                if (pathRect.Contains(Event.current.mousePosition))
                    EditorGUIUtility.AddCursorRect(pathRect, MouseCursor.Link);

                // コンフリクト解消: 他レイヤー側のClipから該当キーを削除するボタン（現在のClipは残す）
                EditorGUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("コンフリクトプロパティを削除", GUILayout.MinWidth(120)))
                {
                    var anyRemoved = false;
                    if (entry.Targets != null)
                    {
                        foreach (var target in entry.Targets)
                        {
                            if (target?.Clips == null) continue;
                            foreach (var clip in target.Clips)
                            {
                                if (clip == null) continue;
                                if (RemoveBindingFromClip(clip, entry.Path, entry.PropertyName))
                                {
                                    anyRemoved = true;
                                    toRemoveClips.Add((target, clip));
                                    EditorUtility.SetDirty(clip);
                                }
                            }
                        }
                    }
                    if (anyRemoved)
                    {
                        toRemoveEntries.Add(entry);
                        SelectTransformAtPath(entry.Path);
                        AnimationClipSelector.InvalidatePathConflictCache();
                    }
                }
                EditorGUILayout.EndHorizontal();

                // コンフリクト先: レイヤーごとにインデント＋Boxで囲み、Clipはリスト表示（各クリックで選択）
                if (entry.Targets != null)
                {
                    foreach (var target in entry.Targets)
                    {
                        if (target?.Clips == null || target.Clips.Count == 0) continue;

                        EditorGUILayout.Space(4);
                        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                        EditorGUILayout.LabelField(target.LayerName, EditorStyles.boldLabel);

                        foreach (var clip in target.Clips)
                        {
                            if (clip == null) continue;
                            EditorGUILayout.BeginHorizontal();
                            var lineBtnStyle = _clickableLabelStyle ?? EditorStyles.label;
                            if (GUILayout.Button("  • " + clip.name, lineBtnStyle))
                            {
                                SelectTransformAtPath(entry.Path);
                                AnimationClipSelector.SetAnimationWindowToClip(clip);
                            }
                            if (GUILayout.Button("削除", GUILayout.Width(40)))
                            {
                                if (RemoveBindingFromClip(clip, entry.Path, entry.PropertyName))
                                {
                                    toRemoveClips.Add((target, clip));
                                    EditorUtility.SetDirty(clip);
                                    SelectTransformAtPath(entry.Path);
                                    AnimationClipSelector.InvalidatePathConflictCache();
                                }
                            }
                            EditorGUILayout.EndHorizontal();
                        }
                        EditorGUILayout.EndVertical();
                    }
                }
            });
            EditorGUILayout.EndVertical();
        }

        /// <summary>指定パス・プロパティ名に一致するバインディングをクリップから削除する。Undo対応。</summary>
        private static bool RemoveBindingFromClip(AnimationClip clip, string path, string propertyName)
        {
            if (clip == null) return false;

            Undo.RecordObject(clip, "コンフリクトプロパティを削除");
            var removed = false;

            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
            {
                if (binding.path != path || binding.propertyName != propertyName) continue;
                AnimationUtility.SetEditorCurve(clip, binding, null);
                removed = true;
            }
            foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
            {
                if (binding.path != path || binding.propertyName != propertyName) continue;
                AnimationUtility.SetObjectReferenceCurve(clip, binding, null);
                removed = true;
            }

            return removed;
        }

        private void SelectTransformAtPath(string path)
        {
            if (_root == null || string.IsNullOrEmpty(path))
            {
                Selection.activeGameObject = _root;
                return;
            }
            var t = _root.transform.Find(path);
            if (t != null)
            {
                Selection.activeGameObject = t.gameObject;
                EditorGUIUtility.PingObject(t.gameObject);
            }
            else
            {
                Selection.activeGameObject = _root;
            }
        }
    }
}
#endif
