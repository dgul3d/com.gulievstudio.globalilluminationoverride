using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace GlobalIlluminationOverride.Editor
{
    [CustomEditor(typeof(GIOverrideVolume))]
    [CanEditMultipleObjects]
    public class GIOverrideVolumeEditor : UnityEditor.Editor
    {
        private BoxBoundsHandle _handle;

        private SerializedProperty _size;
        private SerializedProperty _center;
        private SerializedProperty _blendSmoothness;
        private SerializedProperty _presetIndex;

        private static readonly Color HandleColor = new(0f, 1f, 1f, 1f);

        private void OnEnable()
        {
            _handle = new BoxBoundsHandle();
            _handle.axes = PrimitiveBoundsHandle.Axes.All;

            _size = serializedObject.FindProperty("_size");
            _center = serializedObject.FindProperty("_center");
            _blendSmoothness = serializedObject.FindProperty("_blendSmoothness");
            _presetIndex = serializedObject.FindProperty("_presetIndex");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.PropertyField(_size, new GUIContent("Size"));
            EditorGUILayout.PropertyField(_center, new GUIContent("Center Offset"));
            EditorGUILayout.PropertyField(_blendSmoothness, new GUIContent("Blend Smoothness"));

            DrawPresetIndexField();

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawPresetIndexField()
        {
            GIOverrideController controller = GIOverrideController.Instance;

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Preset", EditorStyles.boldLabel);

            if (controller == null || controller.Presets == null || controller.Presets.Length == 0)
            {
                EditorGUILayout.HelpBox("No GIOverrideController found in the scene or no presets defined.", MessageType.Warning);
                EditorGUILayout.PropertyField(_presetIndex, new GUIContent("Preset Index"));
                return;
            }

            GIOverridePreset[] presets = controller.Presets;
            string[] presetNames = new string[presets.Length];
            for (int i = 0; i < presets.Length; i++)
                presetNames[i] = presets[i] != null ? $"[{i}] {presets[i].name}" : $"[{i}] (None)";

            int current = Mathf.Clamp(_presetIndex.intValue, 0, presets.Length - 1);
            int selected = EditorGUILayout.Popup("Preset", current, presetNames);

            if (selected != _presetIndex.intValue)
            {
                _presetIndex.intValue = selected;
                serializedObject.ApplyModifiedProperties();
            }

            if (presets[current] != null)
            {
                EditorGUI.indentLevel++;
                using (new EditorGUI.DisabledScope(true))
                {
                    SerializedObject presetSO = new(presets[current]);
                    EditorGUILayout.PropertyField(presetSO.FindProperty("_skyColor"), new GUIContent("Sky"));
                    EditorGUILayout.PropertyField(presetSO.FindProperty("_equatorColor"), new GUIContent("Equator"));
                    EditorGUILayout.PropertyField(presetSO.FindProperty("_groundColor"), new GUIContent("Ground"));
                }
                EditorGUI.indentLevel--;
            }
        }

        private void OnSceneGUI()
        {
            GIOverrideVolume volume = (GIOverrideVolume)target;

            _handle.handleColor = HandleColor;
            _handle.wireframeColor = new Color(HandleColor.r, HandleColor.g, HandleColor.b, 0.5f);
            _handle.center = volume.Center;
            _handle.size = volume.Size;

            Transform t = volume.transform;
            Matrix4x4 localToWorld = Matrix4x4.TRS(t.position, t.rotation, t.lossyScale);

            using (new Handles.DrawingScope(HandleColor, localToWorld))
            {
                EditorGUI.BeginChangeCheck();
                _handle.DrawHandle();

                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(volume, "Edit GI Override Volume");
                    volume.Center = _handle.center;
                    volume.Size = _handle.size;
                    EditorUtility.SetDirty(volume);
                }
            }

            DrawSmoothnessLabel(volume);
        }

        private void DrawSmoothnessLabel(GIOverrideVolume volume)
        {
            if (volume.BlendSmoothness <= 0f)
                return;

            Transform t = volume.transform;
            Vector3 absScale = new(
                Mathf.Abs(t.lossyScale.x),
                Mathf.Abs(t.lossyScale.y),
                Mathf.Abs(t.lossyScale.z));

            // Convert world-space smoothness to local-space expansion per axis
            float sx = absScale.x > 0f ? volume.BlendSmoothness / absScale.x : 0f;
            float sy = absScale.y > 0f ? volume.BlendSmoothness / absScale.y : 0f;
            float sz = absScale.z > 0f ? volume.BlendSmoothness / absScale.z : 0f;
            Vector3 outerLocalSize = volume.Size + new Vector3(sx, sy, sz) * 2f;

            using (new Handles.DrawingScope(new Color(HandleColor.r, HandleColor.g, HandleColor.b, 0.15f),
                Matrix4x4.TRS(t.position, t.rotation, t.lossyScale)))
            {
                Handles.DrawWireCube(volume.Center, outerLocalSize);
            }
        }
    }
}
