using System.Collections.Generic;
using Verse;

namespace ArchotechEye_InvisibilityDetection;

internal static class VisibilityContext
{
    [System.ThreadStatic]
    private static Stack<Pawn> currentSearchers;

    public static Pawn CurrentSearcher
    {
        get
        {
            if (currentSearchers == null || currentSearchers.Count == 0)
            {
                return null;
            }

            return currentSearchers.Peek();
        }
    }

    public static void Push(Pawn pawn)
    {
        if (currentSearchers == null)
        {
            currentSearchers = new Stack<Pawn>();
        }

        currentSearchers.Push(pawn);
    }

    public static void Pop()
    {
        if (currentSearchers == null || currentSearchers.Count == 0)
        {
            return;
        }

        currentSearchers.Pop();
    }
}
