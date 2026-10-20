using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace ArchotechEye_InvisibilityDetection;

public sealed class ArchotechEyeInvisibilityDetectionMod : Mod
{
    public const string HarmonyId = "codex.archotecheye.invisibilitydetection";
    internal static ArchotechEyeDetectionSettings Settings { get; private set; } = new ArchotechEyeDetectionSettings();

    public ArchotechEyeInvisibilityDetectionMod(ModContentPack content) : base(content)
    {
        Settings = content == null ? new ArchotechEyeDetectionSettings() : GetSettings<ArchotechEyeDetectionSettings>();
        Harmony harmony = new Harmony(HarmonyId);
        harmony.PatchAll();
        AlphaAnimalsCompatibility.TryInstall(harmony);
    }

    public override string SettingsCategory() => "ArchotechEyeInvisibilityDetection_SettingsTitle".Translate();

    public override void DoSettingsWindowContents(Rect inRect)
    {
        Listing_Standard listing = new Listing_Standard();
        listing.Begin(inRect);
        listing.Label("ArchotechEyeInvisibilityDetection_SilhouetteHeading".Translate());
        listing.Label("ArchotechEyeInvisibilityDetection_SilhouetteOpacity".Translate(
            Mathf.RoundToInt(Settings.SilhouetteOpacity * 100f)), tooltip:
            "ArchotechEyeInvisibilityDetection_SilhouetteOpacityTip".Translate());
        Settings.SilhouetteOpacity = listing.Slider(Settings.SilhouetteOpacity,
            ArchotechEyeDetectionSettings.MinimumSilhouetteOpacity, ArchotechEyeDetectionSettings.MaximumSilhouetteOpacity);
        if (listing.ButtonText("ArchotechEyeInvisibilityDetection_SilhouetteColor".Translate(
            ("ArchotechEyeInvisibilityDetection_SilhouetteColor_" + Settings.SilhouetteColor).Translate())))
        {
            List<FloatMenuOption> options = new List<FloatMenuOption>();
            foreach (SilhouetteColorPreset preset in Enum.GetValues(typeof(SilhouetteColorPreset)))
            {
                SilhouetteColorPreset selected = preset;
                options.Add(new FloatMenuOption(("ArchotechEyeInvisibilityDetection_SilhouetteColor_" + preset).Translate(),
                    () => Settings.SilhouetteColor = selected));
            }
            Find.WindowStack.Add(new FloatMenu(options));
        }
        if (listing.ButtonText("ArchotechEyeInvisibilityDetection_SilhouetteReadable".Translate()))
        {
            Settings.UseReadableSilhouette();
        }
        if (listing.ButtonText("ArchotechEyeInvisibilityDetection_SilhouetteReset".Translate()))
        {
            Settings.ResetSilhouetteAppearance();
        }
        listing.Label("ArchotechEyeInvisibilityDetection_SilhouetteNote".Translate());
        listing.GapLine();
        if (AlphaAnimalsCompatibility.Installed)
        {
            listing.CheckboxLabeled("ArchotechEyeInvisibilityDetection_DetectAlphaStalking".Translate(),
                ref Settings.DetectAlphaStalking, "ArchotechEyeInvisibilityDetection_DetectAlphaStalkingTip".Translate());
        }
        else
        {
            listing.Label("ArchotechEyeInvisibilityDetection_AlphaUnavailable".Translate());
        }
        listing.Gap();
        listing.Label("ArchotechEyeInvisibilityDetection_SettingsNote".Translate());
        listing.End();
    }
}
