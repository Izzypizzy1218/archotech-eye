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
