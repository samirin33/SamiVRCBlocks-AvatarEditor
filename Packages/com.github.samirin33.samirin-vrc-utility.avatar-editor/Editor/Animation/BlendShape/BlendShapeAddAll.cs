#if UNITY_EDITOR
using System;
using Samirin33.AvatarEditor.Animation.Editor;
using Samirin33.Editor;
using UnityEditor;
using UnityEngine;

namespace Samirin33.SamirinVRCUtility.AvatarEditor
{
    /// <summary>
    /// 編集中（または手動で指定）の AnimationClip に、任意の <see cref="SkinnedMeshRenderer"/>
    /// のブレンドシェイプ用カーブを一括（または選択した分だけ）追加する。
    /// </summary>
    public class BlendShapeAddAll : EditorWindow
    {
        private const string MenuPath = "samirin33 Editor Tools/BlendShapeAddAll";

        [MenuItem(MenuPath, priority = 120)]
        public static void Open()
        {
            var w = GetWindow<BlendShapeAddAll>(false, "BlendShape キー追加", true);
            w.minSize = new Vector2(420, 420);
            w.AutoSyncFromAnimation();
        }

        [SerializeField] private AnimationClip _clip;
        [SerializeField] private GameObject _pathRoot;
        [SerializeField] private SkinnedMeshRenderer _skinned;
        [SerializeField] private float _keyTime;
        /// <summary>オンなら Animation ウィンドウの再生ヘッド時刻でキー、オフなら <see cref="_keyTime"/>。</summary>
        [SerializeField] private bool _usePlayheadTime = true;
        [SerializeField] private bool _allSelected = true;
        [SerializeField] private string _search = "";

        private bool[] _selection;
        private Vector2 _listScroll;

        private void AutoSyncFromAnimation()
        {
            if (AnimationWindowHelper.TryGetAnimationWindowStateUnfocused(out var root, out var clip, out var time))
            {
                _clip = clip;
                _pathRoot = root;
                _keyTime = Mathf.Max(0f, time);
            }
            else if (AnimationWindowHelper.TryGetAnimationWindowState(out var root2, out var clip2))
            {
                _clip = clip2;
                _pathRoot = root2;
            }

            if (_clip == null && Selection.activeObject is AnimationClip selClip) _clip = selClip;
            if (_pathRoot == null) _pathRoot = Selection.activeGameObject;
            if (_pathRoot == null) _pathRoot = FindSceneRootForSkinned(_skinned);
            if (_skinned == null) _skinned = TryGetSkinnedFromSelection();
            if (_skinned != null) EnsureSelectionArraySize();
        }

        private static SkinnedMeshRenderer TryGetSkinnedFromSelection()
        {
            if (Selection.activeObject is SkinnedMeshRenderer s) return s;
            if (Selection.activeObject is GameObject go && go.TryGetComponent<SkinnedMeshRenderer>(out var smr)) return smr;
            if (Selection.activeObject is Component c) return c.GetComponent<SkinnedMeshRenderer>();
            return null;
        }

        private static GameObject FindSceneRootForSkinned(SkinnedMeshRenderer smr)
        {
            if (smr == null) return null;
            return smr.transform.root.gameObject;
        }

        private void EnsureSelectionArraySize()
        {
            if (_skinned == null || _skinned.sharedMesh == null) { _selection = Array.Empty<bool>(); return; }
            int n = _skinned.sharedMesh.blendShapeCount;
            if (_selection == null || _selection.Length != n)
            {
                _selection = new bool[n];
                for (int i = 0; i < n; i++) _selection[i] = _allSelected;
            }
        }

        private void OnGUI()
        {
            SamirinEditorStyleHelper.DrawWithBlueBackground(DrawContent, new Rect(0, 0, position.width, position.height));
        }

        private void DrawContent()
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("AnimationClip へのブレンドシェイプキー一括追加", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);

            EditorGUILayout.BeginHorizontal();
            _clip = (AnimationClip)EditorGUILayout.ObjectField("アニメーションクリップ", _clip, typeof(AnimationClip), false);
            if (GUILayout.Button("Animation/選択から", GUILayout.MaxWidth(120)))
            {
                if (AnimationWindowHelper.TryGetAnimationWindowStateUnfocused(out var r, out var c, out var t))
                {
                    _clip = c; _pathRoot = r; _keyTime = Mathf.Max(0f, t);
                }
                else if (AnimationWindowHelper.TryGetAnimationWindowState(out r, out c))
                {
                    _clip = c; _pathRoot = r;
                }
                if (Selection.activeObject is AnimationClip sel) _clip = sel;
            }
            EditorGUILayout.EndHorizontal();

            _pathRoot = (GameObject)EditorGUILayout.ObjectField("パス用ルート (Avatar/Animator ルート)", _pathRoot, typeof(GameObject), true);
            var newSkinned = (SkinnedMeshRenderer)EditorGUILayout.ObjectField("SkinnedMeshRenderer", _skinned, typeof(SkinnedMeshRenderer), true);
            if (newSkinned != _skinned) { _skinned = newSkinned; if (_pathRoot == null) _pathRoot = FindSceneRootForSkinned(_skinned); }
            if (_skinned != null) EnsureSelectionArraySize();

            if (_pathRoot == null) _pathRoot = FindSceneRootForSkinned(_skinned);
            if (_pathRoot == null) EditorGUILayout.HelpBox("パス用ルートを指定するか、SkinnedMeshRenderer を先に割り当ててください（ルート推定に使用）。", MessageType.Info);

            EditorGUILayout.Space(4);
            _usePlayheadTime = EditorGUILayout.ToggleLeft("Animation ウィンドウの再生ヘッド時刻で打鍵", _usePlayheadTime);
            using (new EditorGUI.DisabledScope(_usePlayheadTime))
            {
                _keyTime = EditorGUILayout.FloatField("手動: キーを打つ時刻 (秒)", _keyTime);
            }
            if (_usePlayheadTime)
            {
                if (AnimationWindowHelper.TryGetAnimationWindowStateUnfocused(out _, out _, out var t))
                {
                    EditorGUILayout.HelpBox($"打鍵時刻: 約 {t:0.000} 秒（Animation ウィンドウの再生ヘッド）", MessageType.Info);
                }
            }

            EditorGUILayout.Space(6);
            if (_skinned == null || _skinned.sharedMesh == null)
            {
                EditorGUILayout.HelpBox("有効な SkinnedMeshRenderer（共有メッシュあり）を指定してください。", MessageType.Warning);
                return;
            }

            int blendCount = _skinned.sharedMesh.blendShapeCount;
            if (blendCount == 0) { EditorGUILayout.HelpBox("このメッシュにブレンドシェイプがありません。", MessageType.Info); return; }

            DrawSearchAndToggles(blendCount);

            EditorGUILayout.Space(6);
            if (GUILayout.Button("ブレンドシェイプをクリップにキー追加", GUILayout.Height(28)))
            {
                ApplyAddKeys();
            }
        }

        private void DrawSearchAndToggles(int blendCount)
        {
            _search = EditorGUILayout.TextField("絞り込み（名前）", _search);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("全選択")) { for (int i = 0; i < blendCount; i++) if (PassesFilter(i)) _selection[i] = true; _allSelected = true; }
            if (GUILayout.Button("全解除")) { for (int i = 0; i < blendCount; i++) if (PassesFilter(i)) _selection[i] = false; _allSelected = false; }
            if (GUILayout.Button("表示中のみ反転"))
            {
                for (int i = 0; i < blendCount; i++) if (PassesFilter(i)) _selection[i] = !_selection[i];
                _allSelected = false;
            }
            EditorGUILayout.EndHorizontal();

            _listScroll = EditorGUILayout.BeginScrollView(_listScroll, GUILayout.MinHeight(160), GUILayout.ExpandHeight(true));
            for (int i = 0; i < blendCount; i++)
            {
                if (!PassesFilter(i)) continue;
                string name = _skinned.sharedMesh.GetBlendShapeName(i);
                _selection[i] = EditorGUILayout.ToggleLeft($"[{i}]  {name}", _selection[i]);
            }
            EditorGUILayout.EndScrollView();
        }

        private bool PassesFilter(int index)
        {
            if (string.IsNullOrEmpty(_search)) return true;
            string n = _skinned.sharedMesh.GetBlendShapeName(index);
            return n != null && n.IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void ApplyAddKeys()
        {
            if (_clip == null) { EditorUtility.DisplayDialog("BlendShape キー追加", "AnimationClip を指定してください。", "OK"); return; }
            if (!AssetDatabase.IsOpenForEdit(_clip, StatusQueryOptions.UseCachedIfPossible))
            {
                EditorUtility.DisplayDialog("BlendShape キー追加", "このクリップは外部でロックされている、またはオープンできない場合があります。解除して再試行してください。", "OK");
                return;
            }

            var rootT = _pathRoot != null ? _pathRoot.transform : FindSceneRootForSkinned(_skinned)?.transform;
            if (rootT == null) { EditorUtility.DisplayDialog("BlendShape キー追加", "パス用ルート (Animator 等の基準) を解決できません。フィールドに指定してください。", "OK"); return; }
            if (_skinned.transform != rootT && !_skinned.transform.IsChildOf(rootT))
            {
                if (!EditorUtility.DisplayDialog("BlendShape キー追加",
                        "SkinnedMeshRenderer の Transform が、指定ルートの子階層内にありません。続行してよいですか？（想定外の相対パスになる場合があります。）", "続行", "中断"))
                    return;
            }

            string relativePath = AnimationUtility.CalculateTransformPath(_skinned.transform, rootT);
            if (string.IsNullOrEmpty(relativePath) && _skinned.transform == rootT) relativePath = "";

            float t;
            if (_usePlayheadTime && AnimationWindowHelper.TryGetAnimationWindowStateUnfocused(out _, out var stateClip, out var head) && stateClip == _clip) t = head;
            else t = _keyTime;
            t = SafeClipTime(_clip, t);

            Undo.RegisterCompleteObjectUndo(_clip, "Add BlendShape Keys");
            int added = 0;
            var mesh = _skinned.sharedMesh;
            for (int i = 0; i < mesh.blendShapeCount; i++)
            {
                if (_selection == null || i >= _selection.Length || !_selection[i]) continue;
                string shapeName = mesh.GetBlendShapeName(i);
                if (string.IsNullOrEmpty(shapeName)) continue;
                var binding = new EditorCurveBinding
                {
                    path = relativePath,
                    type = typeof(SkinnedMeshRenderer),
                    propertyName = "blendShape." + shapeName
                };
                float value = _skinned.GetBlendShapeWeight(i);
                AddOrMergeFloatKey(_clip, binding, t, value);
                added++;
            }

            EditorUtility.SetDirty(_clip);
            AssetDatabase.SaveAssets();
            EditorGUIUtility.PingObject(_clip);
            Debug.Log($"[BlendShapeAddAll] キー追加: {added} 本, clip={_clip.name}, 時刻={t:0.000}s, path=" + (string.IsNullOrEmpty(relativePath) ? "「root の直下」" : relativePath));
        }

        private static float SafeClipTime(AnimationClip clip, float t)
        {
            float len = clip.length;
            if (len <= 0.0001f) return 0f;
            return Mathf.Clamp(t, 0f, len);
        }

        private static void AddOrMergeFloatKey(AnimationClip clip, EditorCurveBinding binding, float time, float value)
        {
            var curve = AnimationUtility.GetEditorCurve(clip, binding);
            if (curve == null) curve = new AnimationCurve();
            int idx = -1;
            for (int k = 0; k < curve.length; k++) if (Mathf.Approximately(curve[k].time, time)) { idx = k; break; }
            if (idx >= 0)
            {
                var key = curve[idx];
                key.value = value;
                key.inTangent = 0f;
                key.outTangent = 0f;
                key.weightedMode = WeightedMode.None;
                curve.MoveKey(idx, key);
                AnimationUtility.SetKeyBroken(curve, idx, true);
            }
            else
            {
                int newIndex = curve.AddKey(new Keyframe(time, value, 0f, 0f));
                if (newIndex >= 0) AnimationUtility.SetKeyBroken(curve, newIndex, true);
            }
            AnimationUtility.SetEditorCurve(clip, binding, curve);
        }
    }
}
#endif
