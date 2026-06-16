using UnityEngine;

namespace GlobalIlluminationOverride
{
    [CreateAssetMenu(menuName = "GI Override/Preset", fileName = "GIOverridePreset")]
    public class GIOverridePreset : ScriptableObject
    {
        [ColorUsage(false, true)]
        [SerializeField] private Color _skyColor = new(0.212f, 0.227f, 0.259f);

        [ColorUsage(false, true)]
        [SerializeField] private Color _equatorColor = new(0.114f, 0.125f, 0.133f);

        [ColorUsage(false, true)]
        [SerializeField] private Color _groundColor = new(0.047f, 0.043f, 0.035f);

        public Color SkyColor => _skyColor;
        public Color EquatorColor => _equatorColor;
        public Color GroundColor => _groundColor;
    }
}
