using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace GlobalIlluminationOverride
{
    public enum GIOverrideUpdateMode
    {
        // Checks world-space state every frame; uploads only when something changed.
        DetectChanges,
        // Uploads unconditionally every LateUpdate. Use when volumes are driven by animation or script.
        EveryFrame,
        // Never uploads automatically. Call UpdateNow() or MarkDirty() from external code.
        OnDemand
    }

    [ExecuteAlways]
    [AddComponentMenu("GI Override/GI Override Controller")]
    public class GIOverrideController : MonoBehaviour
    {
        public const int MAX_VOLUMES = 8; //Change this in GIOverride.hlsl as well

        private static GIOverrideController _instance;
        public static GIOverrideController Instance => _instance;

        [SerializeField] private GIOverrideUpdateMode _updateMode = GIOverrideUpdateMode.DetectChanges;
        [SerializeField] private GIOverridePreset[] _presets = Array.Empty<GIOverridePreset>();

        private readonly HashSet<GIOverrideVolume> _volumes = new();

        // Volumes that called Register() before the controller instance was ready.
        private static readonly List<GIOverrideVolume> _pendingRegistrations = new();

        // ─── GPU upload buffers ────────────────────────────────────────────────────

        private readonly Vector4[] _centers = new Vector4[MAX_VOLUMES];
        private readonly Vector4[] _sizes = new Vector4[MAX_VOLUMES];
        private readonly Vector4[] _rotations = new Vector4[MAX_VOLUMES];
        private readonly Vector4[] _shAr = new Vector4[MAX_VOLUMES];
        private readonly Vector4[] _shAg = new Vector4[MAX_VOLUMES];
        private readonly Vector4[] _shAb = new Vector4[MAX_VOLUMES];
        private readonly Vector4[] _shBr = new Vector4[MAX_VOLUMES];
        private readonly Vector4[] _shBg = new Vector4[MAX_VOLUMES];
        private readonly Vector4[] _shBb = new Vector4[MAX_VOLUMES];
        private readonly Vector4[] _shC = new Vector4[MAX_VOLUMES];

        // ─── Change-detection snapshots ────────────────────────────────────────────

        // World-space volume state captured after each upload.
        // Compared every frame to detect any change, including parent-transform propagation.
        private int _snapshotCount;
        private readonly Vector3[] _snapshotCenters = new Vector3[MAX_VOLUMES];
        private readonly Vector3[] _snapshotSizes = new Vector3[MAX_VOLUMES];
        private readonly Quaternion[] _snapshotRotations = new Quaternion[MAX_VOLUMES];
        private readonly float[] _snapshotSmoothness = new float[MAX_VOLUMES];
        private readonly int[] _snapshotPresetIndices = new int[MAX_VOLUMES];

        // Flattened preset colors [sky, equator, ground] × preset count.
        // Detects runtime color edits to GIOverridePreset assets.
        private Color[] _presetColorSnapshot = Array.Empty<Color>();

        private bool _isDirty = true;

        // ─── Shader property IDs ───────────────────────────────────────────────────

        private static readonly int GIO_VOLUMES_COUNT = Shader.PropertyToID("_GIOVolumes_Count");
        private static readonly int GIO_VOLUMES_CENTER = Shader.PropertyToID("_GIOVolumes_Center");
        private static readonly int GIO_VOLUMES_SIZE = Shader.PropertyToID("_GIOVolumes_Size");
        private static readonly int GIO_VOLUMES_ROTATION = Shader.PropertyToID("_GIOVolumes_Rotation");
        private static readonly int GIO_VOLUMES_SH_AR = Shader.PropertyToID("_GIOVolumes_SH_Ar");
        private static readonly int GIO_VOLUMES_SH_AG = Shader.PropertyToID("_GIOVolumes_SH_Ag");
        private static readonly int GIO_VOLUMES_SH_AB = Shader.PropertyToID("_GIOVolumes_SH_Ab");
        private static readonly int GIO_VOLUMES_SH_BR = Shader.PropertyToID("_GIOVolumes_SH_Br");
        private static readonly int GIO_VOLUMES_SH_BG = Shader.PropertyToID("_GIOVolumes_SH_Bg");
        private static readonly int GIO_VOLUMES_SH_BB = Shader.PropertyToID("_GIOVolumes_SH_Bb");
        private static readonly int GIO_VOLUMES_SH_C = Shader.PropertyToID("_GIOVolumes_SH_C");

        public GIOverridePreset[] Presets => _presets;

        // Call after modifying a GIOverridePreset's colors at runtime to trigger upload next frame.
        public static void MarkDirty()
        {
            if (_instance != null)
                _instance._isDirty = true;
        }

        public static void Register(GIOverrideVolume volume)
        {
            if (_instance != null)
                _instance.RegisterVolume(volume);
            else
                _pendingRegistrations.Add(volume);
        }

        public static void Unregister(GIOverrideVolume volume)
        {
            if (_instance != null)
                _instance.UnregisterVolume(volume);
            else
                _pendingRegistrations.Remove(volume);
        }

        private void Awake()
        {
            if (_instance == null || _instance == this)
                _instance = this;
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
                Shader.SetGlobalInt(GIO_VOLUMES_COUNT, 0);
            }
        }

        private void OnEnable()
        {
            if (_instance == null)
                _instance = this;

            foreach (GIOverrideVolume volume in _pendingRegistrations)
            {
                if (volume && volume.isActiveAndEnabled)
                    RegisterVolume(volume);
            }

            _pendingRegistrations.Clear();

            UpdateNow();
        }

        private void OnDisable()
        {
            Shader.SetGlobalInt(GIO_VOLUMES_COUNT, 0);
        }

        private void LateUpdate()
        {
            switch (_updateMode)
            {
                case GIOverrideUpdateMode.EveryFrame:
                    UpdateNow();
                    break;

                case GIOverrideUpdateMode.DetectChanges:
                    if (!_isDirty)
                        _isDirty = HasVolumeStateChanged() || HasPresetColorChanged();

                    if (_isDirty)
                        UpdateNow();
                    break;

                case GIOverrideUpdateMode.OnDemand:
                    break;
            }
        }

        private void OnValidate()
        {
            _isDirty = true;
            UpdateNow();
        }

        // Uploads immediately regardless of dirty state and resets all snapshot state.
        public void UpdateNow()
        {
            UploadShaderData();
            CaptureVolumeSnapshot();
            CapturePresetSnapshot();
            _isDirty = false;
        }

        private void RegisterVolume(GIOverrideVolume volume)
        {
            _volumes.Add(volume);
            _isDirty = true;
        }

        private void UnregisterVolume(GIOverrideVolume volume)
        {
            _volumes.Remove(volume);
            _isDirty = true;
        }

        // ─── Change detection ──────────────────────────────────────────────────────

        private bool HasVolumeStateChanged()
        {
            int count = 0;

            foreach (GIOverrideVolume volume in _volumes)
            {
                if (!volume || !volume.isActiveAndEnabled)
                    continue;

                GIOverridePreset preset = ResolvePreset(volume.PresetIndex);
                if (preset == null)
                    continue;

                if (count >= MAX_VOLUMES)
                    break;

                if (count >= _snapshotCount)
                    return true;

                if (volume.WorldCenter != _snapshotCenters[count] ||
                    volume.WorldSize != _snapshotSizes[count] ||
                    volume.WorldRotation != _snapshotRotations[count] ||
                    volume.BlendSmoothness != _snapshotSmoothness[count] ||
                    volume.PresetIndex != _snapshotPresetIndices[count])
                    return true;

                count++;
            }

            return count != _snapshotCount;
        }

        private void CaptureVolumeSnapshot()
        {
            _snapshotCount = 0;

            foreach (GIOverrideVolume volume in _volumes)
            {
                if (!volume || !volume.isActiveAndEnabled)
                    continue;

                GIOverridePreset preset = ResolvePreset(volume.PresetIndex);
                if (preset == null)
                    continue;

                if (_snapshotCount >= MAX_VOLUMES)
                    break;

                _snapshotCenters[_snapshotCount] = volume.WorldCenter;
                _snapshotSizes[_snapshotCount] = volume.WorldSize;
                _snapshotRotations[_snapshotCount] = volume.WorldRotation;
                _snapshotSmoothness[_snapshotCount] = volume.BlendSmoothness;
                _snapshotPresetIndices[_snapshotCount] = volume.PresetIndex;
                _snapshotCount++;
            }
        }

        private bool HasPresetColorChanged()
        {
            if (_presets == null)
                return false;

            int needed = _presets.Length * 3;

            if (_presetColorSnapshot.Length != needed)
                return true;

            for (int i = 0; i < _presets.Length; i++)
            {
                GIOverridePreset p = _presets[i];
                int b = i * 3;

                Color sky     = p != null ? p.SkyColor     : Color.black;
                Color equator = p != null ? p.EquatorColor : Color.black;
                Color ground  = p != null ? p.GroundColor  : Color.black;

                if (_presetColorSnapshot[b] != sky ||
                    _presetColorSnapshot[b + 1] != equator ||
                    _presetColorSnapshot[b + 2] != ground)
                    return true;
            }

            return false;
        }

        private void CapturePresetSnapshot()
        {
            if (_presets == null)
            {
                _presetColorSnapshot = Array.Empty<Color>();
                return;
            }

            int needed = _presets.Length * 3;

            if (_presetColorSnapshot.Length != needed)
                _presetColorSnapshot = new Color[needed];

            for (int i = 0; i < _presets.Length; i++)
            {
                GIOverridePreset p = _presets[i];
                int b = i * 3;
                _presetColorSnapshot[b]     = p != null ? p.SkyColor     : Color.black;
                _presetColorSnapshot[b + 1] = p != null ? p.EquatorColor : Color.black;
                _presetColorSnapshot[b + 2] = p != null ? p.GroundColor  : Color.black;
            }
        }

        // ─── Shader upload ─────────────────────────────────────────────────────────

        private void UploadShaderData()
        {
            int count = 0;

            foreach (GIOverrideVolume volume in _volumes)
            {
                if (!volume || !volume.isActiveAndEnabled)
                    continue;

                if (count >= MAX_VOLUMES)
                    break;

                GIOverridePreset preset = ResolvePreset(volume.PresetIndex);
                if (preset == null)
                    continue;

                Vector3 worldCenter = volume.WorldCenter;
                Vector3 worldSize = volume.WorldSize;

                _centers[count] = new Vector4(worldCenter.x, worldCenter.y, worldCenter.z, 0f);
                _sizes[count] = new Vector4(worldSize.x, worldSize.y, worldSize.z, volume.BlendSmoothness);

                Quaternion rot = volume.WorldRotation;
                _rotations[count] = new Vector4(rot.x, rot.y, rot.z, rot.w);

                SphericalHarmonicsL2 sh = TrilightSHUtils.CalculateTrilightAmbient(
                    preset.GroundColor, preset.EquatorColor, preset.SkyColor);

                TrilightSHUtils.PackSH(in sh,
                    out _shAr[count], out _shAg[count], out _shAb[count],
                    out _shBr[count], out _shBg[count], out _shBb[count],
                    out _shC[count]);

                count++;
            }

            Shader.SetGlobalInt(GIO_VOLUMES_COUNT, count);
            Shader.SetGlobalVectorArray(GIO_VOLUMES_CENTER, _centers);
            Shader.SetGlobalVectorArray(GIO_VOLUMES_SIZE, _sizes);
            Shader.SetGlobalVectorArray(GIO_VOLUMES_ROTATION, _rotations);
            Shader.SetGlobalVectorArray(GIO_VOLUMES_SH_AR, _shAr);
            Shader.SetGlobalVectorArray(GIO_VOLUMES_SH_AG, _shAg);
            Shader.SetGlobalVectorArray(GIO_VOLUMES_SH_AB, _shAb);
            Shader.SetGlobalVectorArray(GIO_VOLUMES_SH_BR, _shBr);
            Shader.SetGlobalVectorArray(GIO_VOLUMES_SH_BG, _shBg);
            Shader.SetGlobalVectorArray(GIO_VOLUMES_SH_BB, _shBb);
            Shader.SetGlobalVectorArray(GIO_VOLUMES_SH_C, _shC);
        }

        private GIOverridePreset ResolvePreset(int index)
        {
            if (_presets == null || _presets.Length == 0)
                return null;

            if (index < 0 || index >= _presets.Length)
                return null;

            return _presets[index];
        }
    }
}
