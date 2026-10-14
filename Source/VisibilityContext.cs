using System;
using Verse;

namespace ArchotechEye_InvisibilityDetection;

internal static class VisibilityContext
{
    [ThreadStatic]
    private static Pawn currentSearcher;

    [ThreadStatic]
    private static bool readingOriginal;

    public static Pawn CurrentSearcher => currentSearcher;
    public static bool ReadingOriginal => readingOriginal;

    public static Scope Enter(Pawn observer)
    {
        Scope scope = new Scope(currentSearcher);
        currentSearcher = observer;
        return scope;
    }

    public static OriginalScope EnterOriginal()
    {
        OriginalScope scope = new OriginalScope(readingOriginal);
        readingOriginal = true;
        return scope;
    }

    internal readonly struct Scope : IDisposable
    {
        private readonly Pawn previous;
        private readonly bool entered;

        public Scope(Pawn previous)
        {
            this.previous = previous;
            entered = true;
        }

        public void Dispose()
        {
            if (entered)
            {
                currentSearcher = previous;
            }
        }
    }

    internal readonly struct OriginalScope : IDisposable
    {
        private readonly bool previous;

        public OriginalScope(bool previous) => this.previous = previous;

        public void Dispose() => readingOriginal = previous;
    }
}
