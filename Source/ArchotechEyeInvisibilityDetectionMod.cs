using HarmonyLib;
using Verse;

namespace ArchotechEye_InvisibilityDetection;

public sealed class ArchotechEyeInvisibilityDetectionMod : Mod
{
    public const string HarmonyId = "codex.archotecheye.invisibilitydetection";

    public ArchotechEyeInvisibilityDetectionMod(ModContentPack content) : base(content)
    {
        new Harmony(HarmonyId).PatchAll();
    }
}
