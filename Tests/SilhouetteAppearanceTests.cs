using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Linq;
using ArchotechEye_InvisibilityDetection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

internal static partial class Program
{
    private static void TestSilhouetteSettings()
    {
        ArchotechEyeDetectionSettings settings = new ArchotechEyeDetectionSettings();
        Color incoming = new Color(0.2f, 0.4f, 0.6f, 0.9f);
        Color tint = settings.ApplySilhouetteTint(incoming);
        Check(Near(tint.a, 0.3f) && tint.r == incoming.r && tint.g == incoming.g && tint.b == incoming.b,
            "Default silhouette preserves the original tint and alpha 0.3");
        settings.DetectAlphaStalking = false;
        settings.UseReadableSilhouette();
        tint = settings.ApplySilhouetteTint(incoming);
        Check(Near(tint.a, 0.6f) && tint.g > tint.r && tint.b > tint.r,
            "Readability preset selects a stronger cyan silhouette");
        settings.ResetSilhouetteAppearance();
        Check(Near(settings.SilhouetteOpacity, 0.3f) && settings.SilhouetteColor == SilhouetteColorPreset.Original && !settings.DetectAlphaStalking,
            "Appearance reset restores only visual defaults, not compatibility settings");

        settings.SilhouetteOpacity = -10f;
        Check(Near(settings.SilhouetteOpacity, 0.15f), "Opacity cannot hide a detected target completely");
        settings.SilhouetteOpacity = 10f;
        Check(Near(settings.SilhouetteOpacity, 0.8f), "Opacity remains translucent at the maximum setting");
        foreach (float invalid in new[] { float.NaN, float.NegativeInfinity, float.PositiveInfinity })
        {
            settings.SilhouetteOpacity = invalid;
            Check(Near(settings.SilhouetteOpacity, 0.3f), "Non-finite opacity falls back safely: " + invalid);
        }
        foreach (int invalid in new[] { -1, 999 })
        {
            settings.SilhouetteColor = (SilhouetteColorPreset)invalid;
            Check(settings.SilhouetteColor == SilhouetteColorPreset.Original, "Unknown palette falls back to original colors: " + invalid);
        }

        string path = Path.Combine(Path.GetTempPath(), "archotech-eye-appearance-" + Guid.NewGuid().ToString("N") + ".xml");
        try
        {
            foreach (SilhouetteColorPreset preset in Enum.GetValues(typeof(SilhouetteColorPreset)))
            {
                settings.SilhouetteColor = preset;
                settings.SilhouetteOpacity = 0.15f + (int)preset * 0.2f;
                Scribe.saver.InitSaving(path, "settings");
                settings.ExposeData();
                Scribe.saver.FinalizeSaving();
                ArchotechEyeDetectionSettings loaded = LoadAppearanceSettings(path);
                Check(loaded.SilhouetteColor == preset && Near(loaded.SilhouetteOpacity, settings.SilhouetteOpacity) && !loaded.DetectAlphaStalking,
                    "Scribe round-trip preserves visual and compatibility settings: " + preset);
            }
            new XDocument(new XElement("settings", new XElement("detectAlphaStalking", false))).Save(path);
            ArchotechEyeDetectionSettings legacy = LoadAppearanceSettings(path);
            Check(!legacy.DetectAlphaStalking && Near(legacy.SilhouetteOpacity, 0.3f) && legacy.SilhouetteColor == SilhouetteColorPreset.Original,
                "Existing settings without appearance fields keep their previous behavior");
            foreach (string value in new[] { "NaN", "Infinity", "-5", "100" })
            {
                new XDocument(new XElement("settings", new XElement("silhouetteOpacity", value), new XElement("silhouetteColor", 999))).Save(path);
                ArchotechEyeDetectionSettings loaded = LoadAppearanceSettings(path);
                Check(!float.IsNaN(loaded.SilhouetteOpacity) && loaded.SilhouetteOpacity >= 0.15f && loaded.SilhouetteOpacity <= 0.8f &&
                    loaded.SilhouetteColor == SilhouetteColorPreset.Original, "Loaded invalid appearance values are normalized: " + value);
            }
            Check(Scribe.mode == LoadSaveMode.Inactive, "Appearance persistence leaves Scribe inactive");
        }
        finally
        {
            Scribe.ForceStop();
            if (File.Exists(path)) File.Delete(path);
        }

        TestAppearanceTranslations();
    }

    private static ArchotechEyeDetectionSettings LoadAppearanceSettings(string path)
    {
        bool profiling = DeepProfiler.enabled;
        try
        {
            // The headless harness has no preferences UI; keep native Scribe without its profiler.
            DeepProfiler.enabled = false;
            ArchotechEyeDetectionSettings loaded = new ArchotechEyeDetectionSettings();
            Scribe.loader.InitLoading(path);
            loaded.ExposeData();
            Scribe.loader.FinalizeLoading();
            return loaded;
        }
        finally
        {
            DeepProfiler.enabled = profiling;
        }
    }

    private static void TestSilhouetteRendering(InvisibilityDetectionMapComponent cache, Pawn observer, Pawn other, Pawn target)
    {
        ArchotechEyeDetectionSettings settings = ArchotechEyeInvisibilityDetectionMod.Settings;
        float originalOpacity = settings.SilhouetteOpacity;
        SilhouetteColorPreset originalColor = settings.SilhouetteColor;
        object snapshot = AccessTools.Field(typeof(InvisibilityDetectionMapComponent), "snapshot").GetValue(cache);
        int tick = cache.LastRefreshTick;
        Color incoming = new Color(0.2f, 0.4f, 0.6f, 0.9f);
        try
        {
            settings.SilhouetteOpacity = 0.65f;
            foreach (SilhouetteColorPreset preset in Enum.GetValues(typeof(SilhouetteColorPreset)))
            {
                settings.SilhouetteColor = preset;
                PawnDrawParms parms = new PawnDrawParms { tint = incoming, flags = PawnRenderFlags.Invisible };
                DetectedPawnRenderingPatch.ApplySilhouette(target, ref parms);
                Color expected = settings.ApplySilhouetteTint(incoming);
                Check(SameColor(parms.tint, expected) && Near(parms.tint.a, 0.65f) && parms.flags == PawnRenderFlags.Invisible,
                    "World rendering applies the selected appearance without replacing material flags: " + preset);
                if (preset == SilhouetteColorPreset.Cyan)
                    Check(Near(parms.tint.r, 0.3125f) && Near(parms.tint.g, 0.85f) && Near(parms.tint.b, 0.9f), "Cyan highlight blends with incoming tint");
                if (preset == SilhouetteColorPreset.Gold)
                    Check(Near(parms.tint.r, 0.8f) && Near(parms.tint.g, 0.7f) && Near(parms.tint.b, 0.375f), "Gold highlight blends with incoming tint");
                if (preset == SilhouetteColorPreset.White)
                    Check(Near(parms.tint.r, 0.8f) && Near(parms.tint.g, 0.85f) && Near(parms.tint.b, 0.9f), "White highlight brightens the incoming tint");
                foreach (PawnRenderFlags flag in new[] { PawnRenderFlags.Portrait, PawnRenderFlags.Cache })
                {
                    parms = new PawnDrawParms { tint = incoming, flags = flag };
                    DetectedPawnRenderingPatch.ApplySilhouette(target, ref parms);
                    Check(SameColor(parms.tint, incoming) && parms.flags == flag, "Non-world rendering stays unchanged: " + flag + ", " + preset);
                }
            }

            PawnDrawParms untouched = new PawnDrawParms { tint = incoming };
            DetectedPawnRenderingPatch.ApplySilhouette(other, ref untouched);
            Check(SameColor(untouched.tint, incoming) && untouched.flags == 0, "Undetected pawns do not receive a readability highlight");
            DetectedPawnRenderingPatch.ApplySilhouette(null, ref untouched);
            Check(SameColor(untouched.tint, incoming), "Null render targets keep their tint");

            Check(ReferenceEquals(snapshot, AccessTools.Field(typeof(InvisibilityDetectionMapComponent), "snapshot").GetValue(cache)) && tick == cache.LastRefreshTick,
                "Appearance changes do not replace or refresh the detection cache");
            Check(cache.IsDetectedBy(observer, target) && !cache.IsDetectedBy(other, target), "Readability does not share targeting between pawns");
            Check(ArchotechEyeVisibilityUtility.IsVanillaInvisible(target), "Readability does not remove the target's underlying invisibility");
            using (VisibilityContext.Enter(observer))
                Check(!target.IsPsychologicallyInvisible(), "The detecting observer can still target the highlighted pawn");
            using (VisibilityContext.Enter(other))
                Check(target.IsPsychologicallyInvisible(), "An undetecting observer does not gain targeting from the highlight");
        }
        finally
        {
            settings.SilhouetteOpacity = originalOpacity;
            settings.SilhouetteColor = originalColor;
        }
    }

    private static void CheckCompatibilityAppearance(Pawn target, bool alphaMaterial)
    {
        ArchotechEyeDetectionSettings settings = ArchotechEyeInvisibilityDetectionMod.Settings;
        float opacity = settings.SilhouetteOpacity;
        SilhouetteColorPreset color = settings.SilhouetteColor;
        try
        {
            settings.UseReadableSilhouette();
            PawnDrawParms parms = new PawnDrawParms { tint = Color.white, flags = alphaMaterial ? 0 : PawnRenderFlags.Invisible };
            DetectedPawnRenderingPatch.ApplySilhouette(target, ref parms);
            Check(Near(parms.tint.a, 0.6f) && parms.tint.g > parms.tint.r && parms.flags.FlagSet(PawnRenderFlags.Invisible),
                "Readability also applies to the supported compatibility silhouette: " + (alphaMaterial ? "Alpha Animals" : target.GetType().Name));
        }
        finally
        {
            settings.SilhouetteOpacity = opacity;
            settings.SilhouetteColor = color;
        }
    }

    private static void TestAppearanceTranslations()
    {
        string root = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(typeof(Program).Assembly.Location), "..", "..", "..", ".."));
        foreach (string language in new[] { "English", "Korean" })
        {
            XElement xml = XDocument.Load(Path.Combine(root, "Common", "Languages", language, "Keyed", "Strings.xml")).Root;
            HashSet<string> keys = new HashSet<string>();
            foreach (XElement element in xml.Elements())
                if (!keys.Add(element.Name.LocalName)) throw new InvalidOperationException("Duplicate translation: " + element.Name);
            foreach (SilhouetteColorPreset preset in Enum.GetValues(typeof(SilhouetteColorPreset)))
                Check(!string.IsNullOrWhiteSpace(xml.Element("ArchotechEyeInvisibilityDetection_SilhouetteColor_" + preset)?.Value),
                    "Palette has a translated label: " + language + ", " + preset);
            foreach (string key in new[] { "Heading", "Opacity", "OpacityTip", "Color", "Readable", "Reset", "Note" })
                Check(!string.IsNullOrWhiteSpace(xml.Element("ArchotechEyeInvisibilityDetection_Silhouette" + key)?.Value),
                    "Readability UI has a translated label: " + language + ", " + key);
            Check(string.Format(xml.Element("ArchotechEyeInvisibilityDetection_SilhouetteOpacity").Value, 60).Contains("60"),
                "Opacity translation formats its percentage: " + language);
        }
    }

    private static bool Near(float left, float right) => Math.Abs(left - right) < 0.001f;
    private static bool SameColor(Color left, Color right) => Near(left.r, right.r) && Near(left.g, right.g) && Near(left.b, right.b) && Near(left.a, right.a);
}
