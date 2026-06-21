using UnityEngine;

namespace GlobalIlluminationOverride
{
    [ExecuteAlways]
    [AddComponentMenu("GI Override/GI Override Volume")]
    public class GIOverrideVolume : MonoBehaviour
    {
        [SerializeField] private Vector3 _size = new(1f, 1f, 1f);
        [SerializeField] private Vector3 _center = Vector3.zero;
        [SerializeField] private float _blendSmoothness = 0.1f;
        [SerializeField] private int _presetIndex;

        public Vector3 Size
        {
            get => _size;
            set => _size = value;
        }

        public Vector3 Center
        {
            get => _center;
            set => _center = value;
        }

        public float BlendSmoothness => _blendSmoothness;
        public int PresetIndex => _presetIndex;

        public Vector3 WorldCenter => transform.TransformPoint(_center);

        public Vector3 WorldSize => new(
            _size.x * Mathf.Abs(transform.lossyScale.x),
            _size.y * Mathf.Abs(transform.lossyScale.y),
            _size.z * Mathf.Abs(transform.lossyScale.z));

        public Quaternion WorldRotation => transform.rotation;

        private void OnEnable()
        {
            GIOverrideController.Register(this);
        }

        private void OnDisable()
        {
            GIOverrideController.Unregister(this);
        }

        private void OnValidate()
        {
            GIOverrideController.MarkDirty();
        }

        private void OnDrawGizmos()
        {
            DrawGizmoBox(new Color(0f, 1f, 1f, 0.08f), new Color(0f, 1f, 1f, 0.35f));
        }

        private void OnDrawGizmosSelected()
        {
            DrawGizmoBox(new Color(0f, 1f, 1f, 0.15f), new Color(0f, 1f, 1f, 1f));
        }

        private void DrawGizmoBox(Color fill, Color wire)
        {
            Matrix4x4 prevMatrix = Gizmos.matrix;
            Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, transform.lossyScale);

            Gizmos.color = fill;
            Gizmos.DrawCube(_center, _size);

            Gizmos.color = wire;
            Gizmos.DrawWireCube(_center, _size);

            Gizmos.matrix = prevMatrix;
        }
    }
}
