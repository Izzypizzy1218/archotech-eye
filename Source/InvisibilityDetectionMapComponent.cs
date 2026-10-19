using System.Collections.Generic;
using System.Threading;
using RimWorld;
using Verse;

namespace ArchotechEye_InvisibilityDetection;

public sealed class InvisibilityDetectionMapComponent : MapComponent
{
    private const int RefreshIntervalTicks = 60;

    // Published collections are never modified, so parallel rendering can safely read them.
    private sealed class DetectionSnapshot
    {
        public readonly Dictionary<Pawn, HashSet<Pawn>> ByWatcher = new Dictionary<Pawn, HashSet<Pawn>>();
        public readonly HashSet<Pawn> ForPlayer = new HashSet<Pawn>();
    }

    private DetectionSnapshot snapshot = new DetectionSnapshot();
    private readonly List<Pawn> invisibleTargets = new List<Pawn>();
    private readonly List<Pawn> watchers = new List<Pawn>();
    private readonly HashSet<Pawn> changedTargets = new HashSet<Pawn>();
    private int lastRefreshTick = -1;

    internal int LastRefreshTick => lastRefreshTick;

    public InvisibilityDetectionMapComponent(Map map) : base(map)
    {
    }

    public override void MapComponentTick()
    {
        if (Find.TickManager == null)
        {
            return;
        }

        int ticksGame = Find.TickManager.TicksGame;
        if (lastRefreshTick < 0 || ticksGame - lastRefreshTick >= RefreshIntervalTicks)
        {
            RefreshDetectionCache(ticksGame);
        }
    }

    public bool HasDetectedTargets(Pawn observer)
    {
        return observer != null && Volatile.Read(ref snapshot).ByWatcher.ContainsKey(observer);
    }

    public bool IsDetectedBy(Pawn observer, Pawn target)
    {
        DetectionSnapshot current = Volatile.Read(ref snapshot);
        return observer != null && target != null &&
            current.ByWatcher.TryGetValue(observer, out HashSet<Pawn> targets) && targets.Contains(target);
    }

    public bool IsDetectedForPlayer(Pawn target)
    {
        return target != null && Volatile.Read(ref snapshot).ForPlayer.Contains(target);
    }

    public override void MapRemoved()
    {
        Volatile.Write(ref snapshot, new DetectionSnapshot());
        invisibleTargets.Clear();
        watchers.Clear();
        changedTargets.Clear();
    }

    private void RefreshDetectionCache(int ticksGame)
    {
        DetectionSnapshot previous = Volatile.Read(ref snapshot);
        invisibleTargets.Clear();
        watchers.Clear();

        IReadOnlyList<Pawn> spawnedPawns = map?.mapPawns?.AllPawnsSpawned;
        if (spawnedPawns != null)
        {
            for (int i = 0; i < spawnedPawns.Count; i++)
            {
                Pawn pawn = spawnedPawns[i];
                if (pawn?.Spawned == true && !pawn.Dead && ArchotechEyeVisibilityUtility.HasArchotechEye(pawn))
                {
                    watchers.Add(pawn);
                }
            }

            // There is no reason to scan every hediff for invisibility if no observer can detect it.
            if (watchers.Count > 0)
            {
                for (int i = 0; i < spawnedPawns.Count; i++)
                {
                    Pawn pawn = spawnedPawns[i];
                    if (pawn?.Spawned == true && !pawn.Dead && ArchotechEyeVisibilityUtility.IsVanillaInvisible(pawn))
                    {
                        invisibleTargets.Add(pawn);
                    }
                }
            }
        }

        if (invisibleTargets.Count == 0 && previous.ByWatcher.Count == 0)
        {
            lastRefreshTick = ticksGame;
            return;
        }

        DetectionSnapshot next = new DetectionSnapshot();
        for (int i = 0; i < watchers.Count; i++)
        {
            Pawn watcher = watchers[i];
            bool revealForPlayer = watcher.IsColonistPlayerControlled;
            HashSet<Pawn> targets = null;
            for (int j = 0; j < invisibleTargets.Count; j++)
            {
                Pawn target = invisibleTargets[j];
                if (!CanWatcherDetectTarget(watcher, target))
                {
                    continue;
                }

                if (targets == null)
                {
                    targets = new HashSet<Pawn>();
                }

                targets.Add(target);
                if (revealForPlayer)
                {
                    next.ForPlayer.Add(target);
                }
            }

            if (targets != null)
            {
                next.ByWatcher.Add(watcher, targets);
            }
        }

        Volatile.Write(ref snapshot, next);
        lastRefreshTick = ticksGame;
        NotifyChangedTargets(previous.ForPlayer, next.ForPlayer);
    }

    private bool CanWatcherDetectTarget(Pawn watcher, Pawn target)
    {
        return watcher != target && watcher.Map == map && target.Map == map &&
            watcher.Position.DistanceTo(target.Position) <= ArchotechEyeVisibilityUtility.DetectionRadius &&
            GenSight.LineOfSight(watcher.Position, target.Position, map, skipFirstCell: true);
    }

    private void NotifyChangedTargets(HashSet<Pawn> previous, HashSet<Pawn> current)
    {
        changedTargets.Clear();
        changedTargets.UnionWith(previous);
        changedTargets.SymmetricExceptWith(current);

        Pawn newlyDetectedRevenant = null;
        foreach (Pawn target in changedTargets)
        {
            if (target?.Spawned != true || target.Map != map)
            {
                continue;
            }

            target.Drawer?.renderer?.SetAllGraphicsDirty();
            if (newlyDetectedRevenant == null && current.Contains(target) &&
                ArchotechEyeVisibilityUtility.IsRevenant(target))
            {
                newlyDetectedRevenant = target;
            }
        }

        // Vanilla attackTargetsCache indexes hostility, not visibility; no re-registration is needed.
        if (newlyDetectedRevenant != null)
        {
            Messages.Message("ArchotechEyeInvisibilityDetection_RevenantRevealed".Translate(),
                newlyDetectedRevenant, MessageTypeDefOf.CautionInput, historical: false);
        }
    }
}
