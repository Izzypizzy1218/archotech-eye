using UnityEngine;
using Verse;

namespace ArchotechEye_InvisibilityDetection;

public enum SilhouetteColorPreset
{
    Original,
    Cyan,
    Gold,
    White
}

public sealed class ArchotechEyeDetectionSettings : ModSettings
{
    public const float DefaultSilhouetteOpacity = 0.3f;
    public const float MinimumSilhouetteOpacity = 0.15f;
    public const float MaximumSilhouetteOpacity = 0.8f;

    public bool DetectAlphaStalking = true;
    private float silhouetteOpacity = DefaultSilhouetteOpacity;
    private SilhouetteColorPreset silhouetteColor;

    public float SilhouetteOpacity
    {
        get => silhouetteOpacity;
        set => silhouetteOpacity = float.IsNaN(value) || float.IsInfinity(value)
            ? DefaultSilhouetteOpacity : Mathf.Clamp(value, MinimumSilhouetteOpacity, MaximumSilhouetteOpacity);
    }

    public SilhouetteColorPreset SilhouetteColor
    {
        get => silhouetteColor;
        set => silhouetteColor = value >= SilhouetteColorPreset.Original && value <= SilhouetteColorPreset.White
            ? value : SilhouetteColorPreset.Original;
    }

    public void UseReadableSilhouette()
    {
        SilhouetteOpacity = 0.6f;
        SilhouetteColor = SilhouetteColorPreset.Cyan;
    }

    public void ResetSilhouetteAppearance()
    {
        SilhouetteOpacity = DefaultSilhouetteOpacity;
        SilhouetteColor = SilhouetteColorPreset.Original;
    }

    internal Color ApplySilhouetteTint(Color tint)
    {
        if (silhouetteColor != SilhouetteColorPreset.Original)
        {
            Color highlight = silhouetteColor switch
            {
                SilhouetteColorPreset.Cyan => new Color(0.35f, 1f, 1f),
                SilhouetteColorPreset.Gold => new Color(1f, 0.8f, 0.3f),
                _ => Color.white
            };
            // Keep some of the incoming tint while making the chosen highlight readable.
            tint.r = tint.r * 0.25f + highlight.r * 0.75f;
            tint.g = tint.g * 0.25f + highlight.g * 0.75f;
            tint.b = tint.b * 0.25f + highlight.b * 0.75f;
        }
        tint.a = silhouetteOpacity;
        return tint;
    }

    public override void ExposeData()
    {
        Scribe_Values.Look(ref DetectAlphaStalking, "detectAlphaStalking", defaultValue: true);
        Scribe_Values.Look(ref silhouetteOpacity, "silhouetteOpacity", DefaultSilhouetteOpacity);
        Scribe_Values.Look(ref silhouetteColor, "silhouetteColor", SilhouetteColorPreset.Original);
        SilhouetteOpacity = silhouetteOpacity;
        SilhouetteColor = silhouetteColor;
    }
}
