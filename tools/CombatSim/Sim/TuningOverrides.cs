using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using VaatusRevenge.Core;

namespace VaatusRevenge.CombatSim
{
    // --set target.Field=value (repeatable): what-if experiments on the default tuning without editing game code.
    // Targets: player (PlayerTuning), dodge (the move set's DodgeProfile), charge (ChargeSettings),
    // soldier / crossbow (EnemyTuning of melee / ranged enemies), camera (CameraTuning, both framings).
    // Public float, int and bool fields only (nested class fields with dots). Setting a field on a nested reference
    // type (e.g. an EnemyAttackData) changes the object held by that fresh tuning copy only.
    public static class TuningOverrides
    {
        static readonly List<(string target, string field, string value)> sets = new List<(string, string, string)>();

        public static bool Any => sets.Count > 0;
        public static string Describe() => string.Join(", ", sets.ConvertAll(s => s.target + "." + s.field + "=" + s.value));

        public static void Add(string spec)
        {
            int dot = spec.IndexOf('.'), eq = spec.IndexOf('=');
            if (dot <= 0 || eq <= dot + 1) throw new ArgumentException("--set expects target.Field=value, got " + spec);
            sets.Add((spec.Substring(0, dot).ToLowerInvariant(), spec.Substring(dot + 1, eq - dot - 1), spec.Substring(eq + 1)));
        }

        public static void ApplyPlayer(PlayerTuning tuning, ElementMoveSet moves)
        {
            Apply("player", tuning);
            Apply("dodge", moves.Dodge);
            Apply("charge", moves.Charge);
        }

        public static void ApplyCamera(CameraTuning tuning)
        {
            Apply("camera", tuning);
        }

        public static void ApplyEnemy(EnemyTuning tuning)
        {
            if (tuning.Archetype == EnemyArchetype.Melee) Apply("soldier", tuning);
            else if (tuning.Archetype == EnemyArchetype.Ranged) Apply("crossbow", tuning);
        }

        static void Apply(string target, object obj)
        {
            if (obj == null) return;
            foreach (var s in sets)
            {
                if (s.target != target) continue;
                // Nested fields use dots, e.g. soldier.BreakOut.HitsToTrigger=4 or soldier.BreakOut.Attack.Move.Startup=0.5.
                string[] path = s.field.Split('.');
                object owner = obj;
                FieldInfo f = null;
                for (int i = 0; i < path.Length; i++)
                {
                    if (owner == null) throw new ArgumentException("--set: " + s.field + " passes through a null field");
                    f = owner.GetType().GetField(path[i], BindingFlags.Public | BindingFlags.Instance);
                    if (f == null) throw new ArgumentException("--set: " + owner.GetType().Name + " has no public field " + path[i]);
                    if (i < path.Length - 1) owner = f.GetValue(owner);
                }
                object v = f.FieldType == typeof(bool) ? bool.Parse(s.value)
                    : f.FieldType == typeof(int) ? int.Parse(s.value, CultureInfo.InvariantCulture)
                    : (object)float.Parse(s.value, CultureInfo.InvariantCulture);
                f.SetValue(owner, v);
            }
        }
    }
}
