using LudeonTK;
using Verse;

namespace ArchotechEye_InvisibilityDetection;

internal static class InvisibilityDiagnosticsDebugActions
{
    [DebugAction("Archotech Eye", "Inspect invisibility compatibility", actionType = DebugActionType.ToolMapForPawns,
        allowedGameStates = AllowedGameStates.PlayingOnMap)]
    private static void InspectPawn(Pawn pawn)
    {
        if (!Prefs.DevMode || pawn == null)
        {
            return;
        }

        string report = InvisibilityDiagnostics.Format(InvisibilityDiagnostics.Inspect(pawn));
        Find.WindowStack.Add(new Dialog_MessageBox(report,
            buttonAText: "Close".Translate(),
            buttonBText: InvisibilityDiagnostics.T("WriteLog"),
            buttonBAction: () => Log.Message("[" + ArchotechEyeInvisibilityDetectionMod.HarmonyId + "]\n" + report),
            title: InvisibilityDiagnostics.T("Title"))
        {
            closeOnCancel = true,
            closeOnAccept = true
        });
    }
}
