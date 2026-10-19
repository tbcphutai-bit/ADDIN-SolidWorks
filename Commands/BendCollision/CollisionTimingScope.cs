using System;

namespace ADDIN.Commands.BendCollision
{
    // Optional per-thread managed instrumentation. It does not enter query
    // policies, evidence or predicates and must not affect their result.
    internal sealed class CollisionTimingScope : IDisposable
    {
        [ThreadStatic] private static Action<double> observer;
        private readonly Action<double> previous;
        internal static Action<double> Observer => observer;
        internal CollisionTimingScope(Action<double> sink) {previous=observer;observer=sink;}
        public void Dispose(){observer=previous;}
    }
}
