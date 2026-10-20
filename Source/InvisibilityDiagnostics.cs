using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using HarmonyLib;
using RimWorld;
using Verse;

namespace ArchotechEye_InvisibilityDetection;

internal enum InvisibilityDiagnosticState
{
    ActiveGameInvisibility,
    SupportedCustomStealth,
    CustomStealthDisabled,
    InactiveVanillaComponent,
    NoVanillaEvidence,
    Unavailable
}

internal sealed class DiagnosticBool
{
    public bool? Value;
    public string Error;

    public static DiagnosticBool Read(Func<bool> read)
    {
        try
        {
            return new DiagnosticBool { Value = read() };
        }
        catch (Exception exception)
        {
            return new DiagnosticBool { Error = exception.GetType().Name };
        }
    }
}

internal sealed class InvisibilityComponentDiagnostic
{
    public string Type;
    public DiagnosticBool Active;
    public DiagnosticBool VisibleToPlayer;
}

internal sealed class HediffDiagnostic
{
    public string Identity;
    public readonly List<string> ComponentTypes = new List<string>();
    public readonly List<InvisibilityComponentDiagnostic> InvisibilityComponents = new List<InvisibilityComponentDiagnostic>();
}

internal sealed class ObserverDiagnostic
{
    public string Identity;
    public DiagnosticBool HasEye;
    public DiagnosticBool AliveAndSpawned;
    public DiagnosticBool PlayerControlled;
    public DiagnosticBool Cached;
    public DiagnosticBool LineOfSight = new DiagnosticBool();
    public float? Distance;
}

internal sealed class InvisibilityDiagnosticReport
{
    public string Identity;
    public bool Spawned;
    public DiagnosticBool Dead;
    public string Position;
    public bool ShowHiddenPawns;
    public DiagnosticBool GameInvisible = new DiagnosticBool();
    public DiagnosticBool HiddenFromPlayer = new DiagnosticBool();
    public DiagnosticBool CachedForPlayer = new DiagnosticBool();
    public bool AlphaAdapterInstalled;
    public bool AlphaAdapterEnabled;
    public DiagnosticBool AlphaStalking = new DiagnosticBool();
    public InvisibilityDiagnosticState State = InvisibilityDiagnosticState.Unavailable;
    public int LastRefreshTick = -1;
    public int? CurrentTick;
    public readonly List<HediffDiagnostic> Hediffs = new List<HediffDiagnostic>();
    public readonly List<string> PawnComponentTypes = new List<string>();
    public readonly List<ObserverDiagnostic> Observers = new List<ObserverDiagnostic>();
    public readonly List<string> PatchOwners = new List<string>();
}

internal static class InvisibilityDiagnostics
{
    public static InvisibilityDiagnosticReport Inspect(Pawn pawn)
    {
        InvisibilityDiagnosticReport report = new InvisibilityDiagnosticReport
        {
            Identity = Identify(pawn),
            Spawned = pawn?.Spawned == true,
            Dead = pawn?.health == null ? new DiagnosticBool() : DiagnosticBool.Read(() => pawn.Dead),
            Position = pawn?.Spawned == true ? pawn.Position.ToString() : "-"
        };
        if (pawn == null)
        {
            return report;
        }
        report.ShowHiddenPawns = DebugSettings.showHiddenPawns;

        // Suppress only our reveal override. Other mods' patches still participate in the game predicate.
        using (VisibilityContext.EnterOriginal())
        {
            if (pawn.health?.hediffSet?.hediffs != null)
            {
                report.GameInvisible = DiagnosticBool.Read(() => pawn.IsPsychologicallyInvisible());
                report.HiddenFromPlayer = DiagnosticBool.Read(() => pawn.IsHiddenFromPlayer());
                CaptureHediffs(pawn, report);
            }
        }

        int nativeComponents = 0;
        foreach (HediffDiagnostic hediff in report.Hediffs)
        {
            nativeComponents += hediff.InvisibilityComponents.Count;
        }
        report.State = Classify(report.GameInvisible, nativeComponents);
        report.AlphaAdapterInstalled = AlphaAnimalsCompatibility.Installed;
        report.AlphaAdapterEnabled = AlphaAnimalsCompatibility.Enabled;
        report.AlphaStalking = DiagnosticBool.Read(() => AlphaAnimalsCompatibility.HasStalkingHediff(pawn));
        if (report.GameInvisible.Value == false && report.AlphaStalking.Value == true)
        {
            report.State = report.AlphaAdapterEnabled ? InvisibilityDiagnosticState.SupportedCustomStealth :
                InvisibilityDiagnosticState.CustomStealthDisabled;
        }

        if (pawn.AllComps != null)
        {
            foreach (ThingComp component in pawn.AllComps)
            {
                if (component != null)
                {
                    report.PawnComponentTypes.Add(DescribeType(component.GetType()));
                }
            }
        }

        Map map = pawn.Map;
        InvisibilityDetectionMapComponent cache = map?.GetComponent<InvisibilityDetectionMapComponent>();
        report.CachedForPlayer = cache == null ? new DiagnosticBool() :
            DiagnosticBool.Read(() => cache.IsDetectedForPlayer(pawn));
        report.LastRefreshTick = cache?.LastRefreshTick ?? -1;
        if (Find.TickManager != null)
        {
            report.CurrentTick = Find.TickManager.TicksGame;
        }

        IReadOnlyList<Pawn> observers = map?.mapPawns?.AllPawnsSpawned;
        if (observers != null)
        {
            foreach (Pawn observer in observers)
            {
                if (observer == null || observer == pawn)
                {
                    continue;
                }
                DiagnosticBool hasEye = DiagnosticBool.Read(() => ArchotechEyeVisibilityUtility.HasArchotechEye(observer));
                if (hasEye.Value == true || hasEye.Error != null)
                {
                    report.Observers.Add(InspectObserver(observer, pawn));
                }
            }
        }

        CapturePatchOwners(report, nameof(InvisibilityUtility.IsPsychologicallyInvisible));
        CapturePatchOwners(report, nameof(InvisibilityUtility.IsHiddenFromPlayer));
        return report;
    }

    internal static InvisibilityDiagnosticState Classify(DiagnosticBool invisible, int nativeComponents)
    {
        if (invisible?.Value == null)
        {
            return InvisibilityDiagnosticState.Unavailable;
        }
        if (invisible.Value == true)
        {
            return InvisibilityDiagnosticState.ActiveGameInvisibility;
        }
        return nativeComponents > 0 ? InvisibilityDiagnosticState.InactiveVanillaComponent :
            InvisibilityDiagnosticState.NoVanillaEvidence;
    }

    internal static ObserverDiagnostic InspectObserver(Pawn observer, Pawn target)
    {
        ObserverDiagnostic result = new ObserverDiagnostic
        {
            Identity = Identify(observer),
            HasEye = DiagnosticBool.Read(() => ArchotechEyeVisibilityUtility.HasArchotechEye(observer)),
            AliveAndSpawned = observer?.health == null ? new DiagnosticBool() :
                DiagnosticBool.Read(() => observer.Spawned && !observer.Dead),
            PlayerControlled = observer == null ? new DiagnosticBool() :
                DiagnosticBool.Read(() => observer.IsColonistPlayerControlled),
            Cached = DiagnosticBool.Read(() => ArchotechEyeVisibilityUtility.IsDetectedBy(observer, target))
        };
        if (observer?.Spawned != true || target?.Spawned != true || observer == target ||
            observer.Map == null || observer.Map != target.Map)
        {
            return result;
        }

        result.Distance = observer.Position.DistanceTo(target.Position);
        // This is an explicit debug command, never a render or tick callback.
        if (result.Distance <= ArchotechEyeVisibilityUtility.DetectionRadius)
        {
            result.LineOfSight = DiagnosticBool.Read(() =>
                GenSight.LineOfSight(observer.Position, target.Position, observer.Map, skipFirstCell: true));
        }
        return result;
    }

    private static void CaptureHediffs(Pawn pawn, InvisibilityDiagnosticReport report)
    {
        foreach (Hediff hediff in pawn.health.hediffSet.hediffs)
        {
            if (hediff == null)
            {
                continue;
            }
            HediffDiagnostic entry = new HediffDiagnostic { Identity = Identify(hediff.def, hediff.GetType()) };
            if (hediff is HediffWithComps withComps && withComps.comps != null)
            {
                foreach (HediffComp component in withComps.comps)
                {
                    if (component == null)
                    {
                        continue;
                    }
                    string type = DescribeType(component.GetType());
                    entry.ComponentTypes.Add(type);
                    if (component is HediffComp_Invisibility invisibility)
                    {
                        entry.InvisibilityComponents.Add(new InvisibilityComponentDiagnostic
                        {
                            Type = type,
                            Active = DiagnosticBool.Read(() => !invisibility.PsychologicallyVisible),
                            VisibleToPlayer = DiagnosticBool.Read(() => invisibility.Props.visibleToPlayer)
                        });
                    }
                }
            }
            report.Hediffs.Add(entry);
        }
    }

    private static void CapturePatchOwners(InvisibilityDiagnosticReport report, string methodName)
    {
        MethodInfo method = AccessTools.Method(typeof(InvisibilityUtility), methodName, new[] { typeof(Pawn) });
        Patches patches = method == null ? null : Harmony.GetPatchInfo(method);
        report.PatchOwners.Add(methodName + ": " + (patches == null ? "-" : string.Join(", ", patches.Owners)));
    }

    private static string DescribeType(Type type)
    {
        return type.FullName + " [" + type.Assembly.GetName().Name + "]";
    }

    private static string Identify(Pawn pawn)
    {
        return pawn == null ? "-" : "#" + pawn.thingIDNumber + " " + Identify(pawn.def, pawn.GetType());
    }

    private static string Identify(Def def, Type type)
    {
        ModContentPack mod = def?.modContentPack;
        return (def?.defName ?? "-") + " | " + DescribeType(type) + " | " +
            (mod == null ? "-" : mod.Name + " [" + mod.PackageIdPlayerFacing + "]");
    }

    public static string Format(InvisibilityDiagnosticReport report)
    {
        StringBuilder text = new StringBuilder();
        text.AppendLine(T("ReadOnly")).AppendLine();
        Field(text, "Target", report.Identity);
        Field(text, "Spawned", Bool(report.Spawned));
        Field(text, "Dead", FormatBool(report.Dead));
        Field(text, "Position", report.Position);
        text.AppendLine();
        Field(text, "GamePredicate", FormatBool(report.GameInvisible));
        Field(text, "HiddenPredicate", FormatBool(report.HiddenFromPlayer));
        Field(text, "ShowHiddenPawns", Bool(report.ShowHiddenPawns));
        Field(text, "State", T("State_" + report.State));
        text.AppendLine(T("PredicateNote")).AppendLine();
        Field(text, "AlphaInstalled", Bool(report.AlphaAdapterInstalled));
        Field(text, "AlphaEnabled", Bool(report.AlphaAdapterEnabled));
        Field(text, "AlphaStalking", FormatBool(report.AlphaStalking));
        text.AppendLine(T("AlphaNote")).AppendLine();
        Field(text, "CachedForPlayer", FormatBool(report.CachedForPlayer));
        Field(text, "CacheTick", report.LastRefreshTick.ToString(CultureInfo.InvariantCulture));
        Field(text, "CurrentTick", report.CurrentTick?.ToString(CultureInfo.InvariantCulture) ?? "-");
        text.AppendLine(T("CacheNote")).AppendLine();

        text.AppendLine(T("Hediffs"));
        if (report.Hediffs.Count == 0)
        {
            text.AppendLine(T("None"));
        }
        foreach (HediffDiagnostic hediff in report.Hediffs)
        {
            text.AppendLine(hediff.Identity);
            foreach (string component in hediff.ComponentTypes)
            {
                text.Append("  ").AppendLine(component);
            }
            foreach (InvisibilityComponentDiagnostic component in hediff.InvisibilityComponents)
            {
                text.Append("  ").Append(component.Type).Append(": ").Append(T("Active")).Append('=')
                    .Append(FormatBool(component.Active)).Append(", ").Append(T("VisibleToPlayer")).Append('=')
                    .AppendLine(FormatBool(component.VisibleToPlayer));
            }
        }

        text.AppendLine().AppendLine(T("PawnComps"));
        if (report.PawnComponentTypes.Count == 0)
        {
            text.AppendLine(T("None"));
        }
        foreach (string component in report.PawnComponentTypes)
        {
            text.AppendLine(component);
        }

        text.AppendLine().AppendLine(T("Observers"));
        if (report.Observers.Count == 0)
        {
            text.AppendLine(T("None"));
        }
        foreach (ObserverDiagnostic observer in report.Observers)
        {
            text.AppendLine(observer.Identity);
            Field(text, "HasEye", FormatBool(observer.HasEye));
            Field(text, "AliveAndSpawned", FormatBool(observer.AliveAndSpawned));
            Field(text, "PlayerControlled", FormatBool(observer.PlayerControlled));
            Field(text, "Distance", observer.Distance?.ToString("F1", CultureInfo.InvariantCulture) ?? "-");
            Field(text, "LineOfSight", FormatBool(observer.LineOfSight));
            Field(text, "Cached", FormatBool(observer.Cached));
        }

        text.AppendLine().AppendLine(T("Patches"));
        foreach (string patches in report.PatchOwners)
        {
            text.AppendLine(patches);
        }
        return text.ToString();
    }

    internal static string T(string suffix)
    {
        string key = "ArchotechEyeInvisibilityDetection_Diagnostics_" + suffix;
        return LanguageDatabase.activeLanguage == null ? key : key.Translate().ToString();
    }

    private static void Field(StringBuilder text, string key, string value)
    {
        text.Append(T(key)).Append(": ").AppendLine(value);
    }

    private static string Bool(bool value)
    {
        return T(value ? "Yes" : "No");
    }

    private static string FormatBool(DiagnosticBool value)
    {
        if (value?.Error != null)
        {
            return T("Error") + " (" + value.Error + ")";
        }
        return value?.Value is bool result ? Bool(result) : T("NotChecked");
    }
}
