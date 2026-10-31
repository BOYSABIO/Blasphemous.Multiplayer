using System;
using System.Collections.Generic;

namespace Blasphemous.Multiplayer.Common.Combat;

public static class EnemyAuthority
{
    /// <summary>All peers elect the same host regardless of roster or arrival order.</summary>
    public static string Resolve(IEnumerable<string> names, string preferred)
    {
        string first = string.Empty;
        foreach (string name in names)
        {
            if (string.IsNullOrEmpty(name)) continue;
            if (name == preferred) return name;
            if (first.Length == 0 || string.CompareOrdinal(name, first) < 0) first = name;
        }
        return first;
    }
}
