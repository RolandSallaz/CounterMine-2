using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Stable sky and ground bounce for dynamic characters and unbaked arena geometry.</summary>
public sealed class ArenaDaylight : MonoBehaviour
{
    private void OnEnable() => ApplyAmbient();
    public static void ApplyAmbient()
    {
        const int samples = 64;
        var probe = new SphericalHarmonicsL2();
        var white = new SphericalHarmonicsL2();
        Color sky = RenderSettings.ambientSkyColor.linear;
        Color horizon = RenderSettings.ambientEquatorColor.linear;
        Color ground = RenderSettings.ambientGroundColor.linear;
        for (int i = 0; i < samples; i++)
        {
            float y = 1f - 2f * (i + .5f) / samples;
            float radius = Mathf.Sqrt(1f - y * y), angle = i * 2.39996323f;
            var direction = new Vector3(Mathf.Cos(angle) * radius, y, Mathf.Sin(angle) * radius);
            Color radiance = Color.Lerp(horizon, y > 0 ? sky : ground, Mathf.Abs(y));
            probe.AddDirectionalLight(direction, radiance, 1);
            white.AddDirectionalLight(direction, Color.white, 1);
        }
        var directions = new[] { Vector3.up };
        var evaluated = new Color[1];
        white.Evaluate(directions, evaluated);
        probe *= RenderSettings.ambientIntensity / Mathf.Max(.001f, evaluated[0].r);
        // Set explicitly: unbaked scenes and WebGL cannot depend on an editor GI update.
        RenderSettings.ambientProbe = probe;
    }
}
