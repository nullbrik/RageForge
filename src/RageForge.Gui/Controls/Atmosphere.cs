/* RAGEFORGE - Atmosphere */

using System.Numerics;

namespace RageForge.Gui.Controls;

public readonly record struct Atmosphere(
    Vector3 Sun,
    Vector3 SunColor,
    Vector3 Light,
    Vector3 LightColor,
    Vector3 Zenith,
    Vector3 Horizon,
    Vector3 SkyAmbient,
    Vector3 GroundAmbient,
    Vector3 WaterDeep,
    float FogDensity,
    float Night)
{
    public static Atmosphere At(double hours)
    {
        float a = (float)((hours - 6.0) / 12.0 * Math.PI);
        var sun = Vector3.Normalize(new Vector3(MathF.Cos(a), MathF.Sin(a) * 0.9f, 0.35f));
        float e = sun.Y;
        float day = Smooth(-0.1f, 0.2f, e);
        float night = 1f - Smooth(-0.25f, 0.02f, e);
        float dusk = Math.Clamp(1f - MathF.Abs(e) / 0.28f, 0f, 1f) * Smooth(-0.2f, 0f, e);

        var zenith = Vector3.Lerp(new Vector3(0.010f, 0.014f, 0.040f), new Vector3(0.15f, 0.33f, 0.74f), day);
        var horizon = Vector3.Lerp(new Vector3(0.030f, 0.038f, 0.070f), new Vector3(0.60f, 0.71f, 0.86f), day);
        horizon = Vector3.Lerp(horizon, new Vector3(0.92f, 0.50f, 0.26f), dusk * 0.65f);

        var sunColor = Vector3.Lerp(new Vector3(1.0f, 0.50f, 0.24f), new Vector3(1.0f, 0.94f, 0.85f), Smooth(0.02f, 0.35f, e))
            * 1.7f * Smooth(-0.06f, 0.08f, e);
        var moon = Vector3.Normalize(new Vector3(-sun.X, MathF.Max(-sun.Y, 0.25f), -sun.Z));
        var moonColor = new Vector3(0.20f, 0.25f, 0.38f) * night;

        bool sunUp = e > -0.02f;
        var light = sunUp ? Vector3.Normalize(new Vector3(sun.X, MathF.Max(sun.Y, 0.04f), sun.Z)) : moon;
        var lightColor = sunUp ? sunColor + moonColor * 0.3f : moonColor;

        var skyAmbient = Vector3.Max(zenith * 0.85f + horizon * 0.25f, new Vector3(0.05f, 0.06f, 0.10f));
        var groundAmbient = Vector3.Max(horizon * 0.30f + new Vector3(0.05f, 0.045f, 0.035f) * day, new Vector3(0.03f, 0.03f, 0.05f));
        var deep = Vector3.Lerp(new Vector3(0.004f, 0.012f, 0.020f), new Vector3(0.02f, 0.10f, 0.13f), day);

        return new Atmosphere(sun, sunColor, light, lightColor, zenith, horizon, skyAmbient, groundAmbient, deep,
            1f / 3600f, night);
    }

    private static float Smooth(float edge0, float edge1, float x)
    {
        float t = Math.Clamp((x - edge0) / (edge1 - edge0), 0f, 1f);
        return t * t * (3f - 2f * t);
    }
}
