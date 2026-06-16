using UnityEngine;
using UnityEngine.Rendering;

namespace GlobalIlluminationOverride
{
    public static class TrilightSHUtils
    {
        // Analytical trilight-to-L2 spherical harmonics.
        // Ported from https://discussions.unity.com/t/how-does-unity-calculate-spherical-harmonics-with-trilight/808741/3
        public static SphericalHarmonicsL2 CalculateTrilightAmbient(Color groundColor, Color equatorColor, Color skyColor)
        {
            groundColor = groundColor.linear;
            equatorColor = equatorColor.linear;
            skyColor = skyColor.linear;

            const float scale1 = 1f / 7f * 0.55f;
            const float scale2 = 2f / 7f * 0.55f;
            const float scale3 = 3f / 7f * 0.55f;

            SphericalHarmonicsL2 sh = new();
            sh.AddDirectionalLight(Vector3.up, skyColor, scale3);
            sh.AddDirectionalLight(Vector3.down, groundColor, scale3);

            Vector3 halfway = new(0.61237243569579f, 0.61f, 0.61237243569579f);

            Color upperColor = Color.Lerp(skyColor, equatorColor, 0.77f);
            sh.AddDirectionalLight(new Vector3(-halfway.x, +halfway.y, -halfway.z), upperColor, scale2);
            sh.AddDirectionalLight(new Vector3(+halfway.x, +halfway.y, -halfway.z), upperColor, scale2);
            sh.AddDirectionalLight(new Vector3(-halfway.x, +halfway.y, +halfway.z), upperColor, scale2);
            sh.AddDirectionalLight(new Vector3(+halfway.x, +halfway.y, +halfway.z), upperColor, scale2);

            Color centerColor = equatorColor;
            sh.AddDirectionalLight(Vector3.left, centerColor, scale1);
            sh.AddDirectionalLight(Vector3.right, centerColor, scale1);
            sh.AddDirectionalLight(Vector3.forward, centerColor, scale1);
            sh.AddDirectionalLight(Vector3.back, centerColor, scale1);

            Color lowerColor = Color.Lerp(groundColor, equatorColor, 0.77f);
            sh.AddDirectionalLight(new Vector3(-halfway.x, -halfway.y, -halfway.z), lowerColor, scale2);
            sh.AddDirectionalLight(new Vector3(+halfway.x, -halfway.y, -halfway.z), lowerColor, scale2);
            sh.AddDirectionalLight(new Vector3(-halfway.x, -halfway.y, +halfway.z), lowerColor, scale2);
            sh.AddDirectionalLight(new Vector3(+halfway.x, -halfway.y, +halfway.z), lowerColor, scale2);

            return sh;
        }

        public static void PackSH(in SphericalHarmonicsL2 sh,
            out Vector4 ar, out Vector4 ag, out Vector4 ab,
            out Vector4 br, out Vector4 bg, out Vector4 bb,
            out Vector4 c)
        {
            ar = new Vector4(sh[0, 3], sh[0, 1], sh[0, 2], sh[0, 0]);
            ag = new Vector4(sh[1, 3], sh[1, 1], sh[1, 2], sh[1, 0]);
            ab = new Vector4(sh[2, 3], sh[2, 1], sh[2, 2], sh[2, 0]);

            br = new Vector4(sh[0, 4], sh[0, 5], sh[0, 6], sh[0, 7]);
            bg = new Vector4(sh[1, 4], sh[1, 5], sh[1, 6], sh[1, 7]);
            bb = new Vector4(sh[2, 4], sh[2, 5], sh[2, 6], sh[2, 7]);

            c = new Vector4(sh[0, 8], sh[1, 8], sh[2, 8], 1f);
        }
    }
}
