using System;
using System.Collections.Generic;

namespace InterchangeMod.Generation.Geometry
{
    /// <summary>Follow matched degree-two road chains inside a finite footprint.</summary>
    public static class RoadChainTraversal
    {
        public static HashSet<T> Collect<T>(IEnumerable<T> seeds, Func<T, IEnumerable<T>> neighbors, Func<T, bool> inside)
        {
            var result = new HashSet<T>();
            var queue = new Queue<T>();
            foreach (var seed in seeds)
                if (result.Add(seed)) queue.Enqueue(seed);
            // The caller supplies finite in-footprint road entities. The visited set
            // terminates cycles; a global count cap would silently lose outer arms.
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                foreach (var next in neighbors(current))
                    if (!result.Contains(next) && inside(next) && result.Add(next)) queue.Enqueue(next);
            }
            return result;
        }
    }
}
