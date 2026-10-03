using VaatusRevenge.Core;

namespace VaatusRevenge.EditorTools
{
    // The name rules the animation-set builder uses to guess which pack clip is which move. Edit freely: this is
    // the one place to teach the builder a new clip name. (Or skip the guessing: set the clip by hand in the
    // FighterAnimationSet asset, and the builder will never touch that entry again.)
    //
    // How a clip is scored for a key: its name is lower-cased and split into words ("FrontKick_R" -> "front kick r").
    // Each phrase that starts a word in the name scores its weight; the best phrase counts. A phrase found only in
    // the FILE name (clips inside an FBX are often called "Take 001") scores 60%. Any "never" word in the clip's
    // name rules that key out. Looping clips get +2 for looping keys (idle, walk...), one-shot clips +1 for the rest,
    // and "combo" clips -4 (several strikes in one clip don't fit one move). Highest score wins, one clip per key,
    // one key per clip (except the fallbacks further down, which deliberately reuse a clip).
    public static partial class AnimationSetBuilder
    {
        const int Strong = 10, Medium = 6, Weak = 3;

        sealed class KeyRule
        {
            public readonly string Key;
            public readonly bool Loops;
            public readonly (string phrase, int weight)[] Phrases;
            public readonly string[] Never;

            public KeyRule(string key, bool loops, (string, int)[] phrases, string[] never)
            {
                Key = key;
                Loops = loops;
                Phrases = phrases;
                Never = never ?? new string[0];
            }
        }

        // Words that mark a clip as reaction/defeat, so strike keys can refuse them.
        static readonly string[] NotAStrike = { "hit", "damage", "hurt", "react", "death", "die", "dead", "knock", "fall", "idle" };

        // Clips never used for anything.
        static readonly string[] IgnoredWords = { "taunt", "victory", "win", "celebrat", "tpose", "t pose", "bind pose", "reference" };

        static readonly KeyRule[] Rules =
        {
            //          key                        loops  phrases (weight)                                                                           never
            new KeyRule(AnimationKeys.Jab,          false, new[] { ("jab", Strong), ("lead punch", Strong), ("straight punch", Strong), ("front punch", Strong), ("punch", Weak), ("straight", Weak) },
                                                           Concat(NotAStrike, "kick", "knee", "cross", "hook", "uppercut", "rear", "reverse")),
            new KeyRule(AnimationKeys.Cross,        false, new[] { ("cross", Strong), ("rear punch", Strong), ("reverse punch", Strong), ("back punch", Strong), ("punch", Weak), ("hook", Weak) },
                                                           Concat(NotAStrike, "kick", "knee", "jab", "uppercut")),
            new KeyRule(AnimationKeys.SnapKick,     false, new[] { ("front kick", Strong), ("snap", Strong), ("push kick", Strong), ("teep", Strong), ("mae geri", Strong), ("kick", Weak) },
                                                           Concat(NotAStrike, "round", "spin", "back kick", "side kick", "high kick", "axe", "jump", "flying", "low")),
            new KeyRule(AnimationKeys.SpinKick,     false, new[] { ("roundhouse", Strong), ("round kick", Strong), ("spin kick", Strong), ("spinning", Strong), ("back kick", Strong), ("turning kick", Strong), ("mawashi", Strong), ("spin", Medium), ("side kick", Medium), ("kick", 2) },
                                                           Concat(NotAStrike, "jump", "flying", "sweep", "low")),
            new KeyRule(AnimationKeys.PhoenixPalm,  false, new[] { ("double palm", Strong), ("push", Strong), ("two hand", Strong), ("both hand", Strong), ("double", Medium), ("palm", Medium) },
                                                           Concat(NotAStrike, "kick", "push up", "pushup")),
            new KeyRule(AnimationKeys.FaJinPalm,    false, new[] { ("palm strike", Strong), ("palm", Strong), ("fa jin", Strong), ("power punch", Strong), ("heavy punch", Strong), ("strong punch", Strong), ("heavy", Weak), ("power", Weak) },
                                                           Concat(NotAStrike, "kick", "double")),
            new KeyRule(AnimationKeys.Launcher,     false, new[] { ("uppercut", Strong), ("upper cut", Strong), ("rising", Strong), ("knee", Strong), ("launch", Strong), ("rise", Weak) },
                                                           Concat(NotAStrike, "get up", "getup", "stand")),
            new KeyRule(AnimationKeys.AxeKick,      false, new[] { ("axe", Strong), ("high kick", Strong), ("heel drop", Strong), ("overhead kick", Strong), ("high", Weak) },
                                                           NotAStrike),
            new KeyRule(AnimationKeys.FireWhip,     false, new[] { ("whip", Strong), ("backhand", Strong), ("back hand", Strong), ("backfist", Strong), ("back fist", Strong), ("hook", Medium), ("swing", Weak) },
                                                           Concat(NotAStrike, "kick")),
            new KeyRule(AnimationKeys.FlameWheel,   false, new[] { ("sweep", Strong), ("leg sweep", Strong), ("low spin", Strong), ("low kick", Medium) },
                                                           NotAStrike),
            new KeyRule(AnimationKeys.FireBlast,    false, new[] { ("fireball", Strong), ("blast", Strong), ("hadouken", Strong), ("energy", Strong), ("projectile", Strong), ("shoot", Medium) },
                                                           NotAStrike),
            new KeyRule(AnimationKeys.AirJab,       false, new[] { ("air punch", Strong), ("jump punch", Strong), ("jumping punch", Strong), ("aerial punch", Strong) },
                                                           NotAStrike),
            new KeyRule(AnimationKeys.AirCrescent,  false, new[] { ("crescent", Strong), ("air kick", Strong), ("jump kick", Strong), ("jumping kick", Strong), ("aerial", Weak) },
                                                           Concat(NotAStrike, "spin", "tornado")),
            new KeyRule(AnimationKeys.AirTornado,   false, new[] { ("tornado", Strong), ("540", Strong), ("butterfly", Strong), ("jump spin", Strong), ("jumping spin", Strong) },
                                                           NotAStrike),
            new KeyRule(AnimationKeys.SprintKick,   false, new[] { ("flying kick", Strong), ("fly kick", Strong), ("running kick", Strong), ("sprint kick", Strong), ("superman", Strong) },
                                                           NotAStrike),
            new KeyRule(AnimationKeys.ZipKick,      false, new[] { ("dash kick", Strong), ("zip", Strong), ("lunge kick", Strong) },
                                                           NotAStrike),
            new KeyRule(AnimationKeys.Shove,        false, new[] { ("shove", Strong), ("push", Strong), ("two hand", Medium) },
                                                           Concat(NotAStrike, "kick", "push up", "pushup")),

            // ---- Build 05: the other elements' signature strikes (a pack clip only replaces the procedural one if the
            // name says so clearly: a stomp, a ground slam, a two-palm push). Everything else in Water, Earth and Air stays
            // procedural, and so do Air's flurries (one pack strike can't show several sub-hits).
            new KeyRule(AnimationKeys.StompLine,    false, new[] { ("stomp", Strong), ("stamp", Strong), ("foot stomp", 12) },
                                                           Concat(NotAStrike, "punch", "ground pound", "slam")),
            new KeyRule(AnimationKeys.QuakeSlam,    false, new[] { ("ground pound", Strong), ("ground slam", Strong), ("ground punch", Strong), ("earthquake", Strong), ("slam", Medium) },
                                                           Concat(NotAStrike, "kick", "body slam", "stomp")),
            new KeyRule(AnimationKeys.TwoPalmPush,  false, new[] { ("palm push", Strong), ("push palm", Strong), ("twin palm", Strong), ("palms", Medium) },
                                                           Concat(NotAStrike, "kick", "push up", "pushup")),

            // ---- States and reactions ----
            new KeyRule(AnimationKeys.Hurt,         false, new[] { ("hit", Strong), ("damage", Strong), ("hurt", Strong), ("react", Strong), ("flinch", Strong), ("pain", Medium) },
                                                           new[] { "knock", "down", "fall", "death", "die", "dead", "ko", "heavy", "big", "block", "guard" }),
            new KeyRule(AnimationKeys.Stagger,      false, new[] { ("stagger", Strong), ("stumble", Strong), ("heavy hit", Strong), ("big hit", Strong), ("heavy damage", Strong), ("hit", Weak), ("damage", Weak) },
                                                           new[] { "knock", "down", "fall", "death", "die", "dead", "ko", "block", "guard" }),
            new KeyRule(AnimationKeys.Knockdown,    false, new[] { ("knockdown", Strong), ("knock down", Strong), ("knocked", Strong), ("knock", Medium), ("fall down", Strong), ("down", Medium), ("fall", Medium) },
                                                           new[] { "get up", "getup", "stand", "rise", "death", "die", "dead", "knockout", "knock out", "ko" }),
            new KeyRule(AnimationKeys.Launched,     false, new[] { ("launched", Strong), ("air hit", Strong), ("juggle", Strong), ("blown", Strong), ("knock back", Medium), ("knockback", Medium) },
                                                           new[] { "death", "die", "dead", "get up", "getup" }),
            new KeyRule(AnimationKeys.GetUp,        false, new[] { ("get up", Strong), ("getup", Strong), ("stand up", Strong), ("standup", Strong), ("rise", Strong), ("recover", Strong), ("wake up", Strong), ("stand", Weak) },
                                                           new[] { "death", "die", "dead", "idle" }),
            new KeyRule(AnimationKeys.Death,        false, new[] { ("death", Strong), ("die", Strong), ("dead", Strong), ("dying", Strong), ("ko", Strong), ("knockout", 12), ("knock out", 12) },
                                                           new[] { "get up", "getup", "stand" }),
            new KeyRule(AnimationKeys.Parry,        false, new[] { ("parry", Strong), ("deflect", Strong), ("block", Medium), ("guard", Medium) },
                                                           new[] { "success", "counter", "idle", "walk" }),
            new KeyRule(AnimationKeys.ParrySuccess, false, new[] { ("parry success", Strong), ("counter", Strong) },
                                                           new[] { "hit", "damage" }),
            new KeyRule(AnimationKeys.Block,        true,  new[] { ("block", Strong), ("guard", Strong) },
                                                           new[] { "walk", "run" }),
            new KeyRule(AnimationKeys.Dodge,        false, new[] { ("dodge", Strong), ("evade", Strong), ("roll", Strong), ("dash", Medium), ("step", Weak) },
                                                           new[] { "hit", "damage", "kick", "punch", "back step", "backstep", "step back", "left", "right", "back", "duck", "weave", "slip" }),
            // The Build 05 dodge kinds (Spider-Man 2 style: the body keeps facing the foe). Slip in = ducking or weaving
            // under a strike; side-slips by side; evade out = a hop or dodge backwards that stays squared up.
            new KeyRule(AnimationKeys.DodgeSlip,    false, new[] { ("slip", Strong), ("weave", Strong), ("duck", Strong), ("bob", Medium), ("dodge forward", Strong) },
                                                           new[] { "hit", "damage", "kick", "punch", "back", "left", "right" }),
            new KeyRule(AnimationKeys.DodgeSideLeft, false, new[] { ("dodge left", Strong), ("sidestep left", Strong), ("side step left", Strong), ("step left", Strong), ("evade left", Strong) },
                                                           new[] { "hit", "damage", "kick", "punch", "right" }),
            new KeyRule(AnimationKeys.DodgeSideRight, false, new[] { ("dodge right", Strong), ("sidestep right", Strong), ("side step right", Strong), ("step right", Strong), ("evade right", Strong) },
                                                           new[] { "hit", "damage", "kick", "punch", "left" }),
            new KeyRule(AnimationKeys.DodgeEvade,   false, new[] { ("dodge back", Strong), ("evade back", Strong), ("jump back", Strong), ("hop back", Medium), ("back dodge", Strong) },
                                                           new[] { "hit", "damage", "kick", "punch", "left", "right" }),
            new KeyRule(AnimationKeys.Backstep,     false, new[] { ("backstep", Strong), ("back step", Strong), ("step back", Strong), ("hop back", Strong), ("retreat", Strong) },
                                                           new[] { "hit", "damage" }),
            new KeyRule(AnimationKeys.Charge,       false, new[] { ("charge", Strong), ("power up", Strong), ("focus", Strong), ("chamber", Strong) },
                                                           new[] { "run", "hit", "damage" }),
            new KeyRule(AnimationKeys.Heal,         false, new[] { ("drink", Strong), ("heal", Strong), ("potion", Strong) }, null),
            new KeyRule(AnimationKeys.Jump,         false, new[] { ("jump", Strong) },
                                                           new[] { "kick", "punch", "land", "fall", "spin", "attack" }),
            new KeyRule(AnimationKeys.Fall,         true,  new[] { ("falling", Strong), ("in air", Strong), ("air idle", Strong), ("fall loop", Strong) },
                                                           new[] { "down", "death", "die", "hit" }),
            new KeyRule(AnimationKeys.Land,         false, new[] { ("land", Strong) }, new[] { "hit", "death" }),

            // ---- Locomotion (looping) ----
            new KeyRule(AnimationKeys.Idle,         true,  new[] { ("idle", Strong), ("stance", Strong), ("ready", Strong), ("fight idle", 12), ("fighting idle", 12), ("breath", Medium), ("wait", Weak) },
                                                           new[] { "walk", "run", "kick", "punch", "hit", "damage", "death", "block", "guard", "jump", "fall", "air" }),
            new KeyRule(AnimationKeys.Walk,         true,  new[] { ("walk", Strong), ("walk forward", 12), ("walk fwd", 12), ("move", Weak) },
                                                           new[] { "back", "left", "right", "side", "strafe", "run" }),
            new KeyRule(AnimationKeys.Strafe,       true,  new[] { ("strafe", Strong), ("walk left", Strong), ("walk right", Strong), ("walk side", Strong), ("side walk", Strong), ("shuffle", Medium) },
                                                           new[] { "run", "kick", "punch" }),
            new KeyRule(AnimationKeys.Run,          true,  new[] { ("run", Strong), ("jog", Strong), ("run forward", 12), ("sprint", Weak) },
                                                           new[] { "back", "left", "right", "kick", "punch" }),
            new KeyRule(AnimationKeys.Sprint,       true,  new[] { ("sprint", Strong), ("fast run", Strong), ("run fast", Strong) },
                                                           new[] { "kick", "punch" }),
        };

        // Keys with no clip of their own borrow another key's clip. Order matters: a fallback may use one made above it.
        static readonly (string key, string from)[] Fallbacks =
        {
            (AnimationKeys.AirJab, AnimationKeys.Jab),
            (AnimationKeys.FireBlast, AnimationKeys.Cross),          // a thrusting rear punch throws the fireball
            (AnimationKeys.FaJinPalm, AnimationKeys.PhoenixPalm),
            (AnimationKeys.PhoenixPalm, AnimationKeys.FaJinPalm),
            (AnimationKeys.SprintKick, AnimationKeys.SnapKick),
            (AnimationKeys.ZipKick, AnimationKeys.SprintKick),
            (AnimationKeys.Stagger, AnimationKeys.Hurt),
            (AnimationKeys.Backstep, AnimationKeys.Dodge),
            (AnimationKeys.Parry, AnimationKeys.Block),
            (AnimationKeys.Block, AnimationKeys.Parry),
            (AnimationKeys.ParrySuccess, AnimationKeys.Parry),
            (AnimationKeys.Sprint, AnimationKeys.Run),
            (AnimationKeys.Strafe, AnimationKeys.Walk),
        };

        // Which keys each set is built for. Enemies (sword soldiers) only take body states and reactions from the
        // unarmed packs: an unarmed punch clip would look wrong on a sword swing, so their attacks stay procedural.
        static readonly string[] FireKeys =
        {
            AnimationKeys.Idle, AnimationKeys.Walk, AnimationKeys.Run, AnimationKeys.Sprint, AnimationKeys.Strafe,
            AnimationKeys.Jump, AnimationKeys.Fall, AnimationKeys.Land, AnimationKeys.Dodge, AnimationKeys.Backstep,
            AnimationKeys.Parry, AnimationKeys.ParrySuccess, AnimationKeys.Block, AnimationKeys.Hurt, AnimationKeys.Stagger,
            AnimationKeys.Launched, AnimationKeys.Knockdown, AnimationKeys.GetUp, AnimationKeys.Death, AnimationKeys.Heal,
            AnimationKeys.Charge,
            AnimationKeys.Jab, AnimationKeys.Cross, AnimationKeys.SnapKick, AnimationKeys.SpinKick, AnimationKeys.PhoenixPalm,
            AnimationKeys.Launcher, AnimationKeys.AirJab, AnimationKeys.AirCrescent, AnimationKeys.AirTornado,
            AnimationKeys.AxeKick, AnimationKeys.FaJinPalm, AnimationKeys.FireBlast, AnimationKeys.FireWhip,
            AnimationKeys.FlameWheel, AnimationKeys.ZipKick, AnimationKeys.SprintKick,
            // Build 05: the dodge kinds, and the few element strikes a pack clip can stand in for by name
            AnimationKeys.DodgeSlip, AnimationKeys.DodgeSideLeft, AnimationKeys.DodgeSideRight, AnimationKeys.DodgeEvade,
            AnimationKeys.StompLine, AnimationKeys.QuakeSlam, AnimationKeys.TwoPalmPush,
        };

        static readonly string[] EnemyKeys =
        {
            AnimationKeys.Idle, AnimationKeys.Walk, AnimationKeys.Run, AnimationKeys.Strafe, AnimationKeys.Block,
            AnimationKeys.Hurt, AnimationKeys.Stagger, AnimationKeys.Launched, AnimationKeys.Knockdown, AnimationKeys.GetUp,
            AnimationKeys.Death, AnimationKeys.Backstep, AnimationKeys.Shove,
        };

        static string[] Concat(string[] a, params string[] b)
        {
            var all = new string[a.Length + b.Length];
            a.CopyTo(all, 0);
            b.CopyTo(all, a.Length);
            return all;
        }
    }
}
