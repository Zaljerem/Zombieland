
using System;
using System.Collections.Generic;
using Verse;

namespace ZombieLand
{
    public static class CacheUtils
    {
        /// Removes invalid entries from a cached set of RimWorld Things.
        /// Invalid = null, destroyed, or not spawned on the map.
        public static void PurgeInvalid<T>(HashSet<T> set) where T : Thing
        {
            if (set == null) return;

            set.RemoveWhere(obj =>
                obj == null ||
                obj.Destroyed ||
                obj.Spawned == false
            );
        }

        /// Removes invalid entries plus any that match a custom condition.
        public static void PurgeInvalid<T>(HashSet<T> set, Predicate<T> extraCondition) where T : Thing
        {
            if (set == null) return;

            set.RemoveWhere(obj =>
                obj == null ||
                obj.Destroyed ||
                obj.Spawned == false ||
                extraCondition(obj)
            );
        }
    }
}
