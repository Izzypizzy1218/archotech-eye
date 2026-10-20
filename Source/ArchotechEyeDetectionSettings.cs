using Verse;

namespace ArchotechEye_InvisibilityDetection;

public sealed class ArchotechEyeDetectionSettings : ModSettings
{
    public bool DetectAlphaStalking = true;

    public override void ExposeData()
    {
        Scribe_Values.Look(ref DetectAlphaStalking, "detectAlphaStalking", defaultValue: true);
    }
}
