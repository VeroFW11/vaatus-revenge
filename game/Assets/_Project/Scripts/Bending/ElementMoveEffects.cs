using UnityEngine;
using VaatusRevenge.Core;

namespace VaatusRevenge
{
    // A strike's effect at the moment it can hit, chosen by the move's EffectKey (data) and drawn in the move's element:
    // a burst from the fist, a cone, a lash, a ring all round, a slam, a wave, a line of spikes, a dome, a vortex,
    // shards, a stomp. Every effect is sized from the move's Range and ArcDegrees, so what you see is what can hit.
    // (This was PlayerFeedback.ActiveStarted before Build 05; Fire's moves draw exactly what they drew then.)
    //
    // Fire has no wave, line, dome, vortex, shards or stomp of its own: a Fire move using one of those keys bursts.
    // A multi-hit move (MoveData.HitCount > 1) draws its big effect on the first sub-hit and a small burst from the limb
    // on each later one, so a four-hit swirl isn't four swirls on top of each other.
    // Plain C# owned by PlayerFeedback.
    public sealed class ElementMoveEffects
    {
        FireVfxHandle whip;

        // The strike went active. limb: the striking fist or foot (the body when there's no rig). burstMultiplier: extra
        // size for the burst-like parts (the dodge strike's burst is a little bigger), 1 = as data says.
        // Returns true when the strike hit the ground hard (a stomp), for the caller's screen shake.
        public bool ActiveStarted(in PlayerEvent e, ElementId element, Transform body, Transform limb, HumanoidBody rig,
            PlayerFeedbackSettings s, float burstMultiplier)
        {
            MoveData move = e.Move;
            if (move == null) return false;
            bool faJin = e.ChargeTier == ChargeTier.FaJin;
            Vector3 origin = e.Origin.ToUnity();
            Vector3 direction = e.Direction.ToUnity();
            float half = move.Range * s.BurstScalePerMetre * 0.5f * burstMultiplier;
            if (!IsFirstSubHit(in e))
            {
                // A later sub-hit of a flurry: a small burst from the limb, one per hit.
                ElementVfx.Burst(element, limb.position, direction, move.Range * s.BurstScalePerMetre * s.SubHitBurstShare * burstMultiplier);
                return false;
            }
            float scale = move.Range * s.BurstScalePerMetre * (faJin ? s.FaJinBurstMultiplier : 1f) * burstMultiplier;
            PlayerStrikePoses p = s.Poses;
            string key = move.EffectKey;
            bool fire = element == ElementId.Fire || element == ElementId.None;
            if (fire && IsOtherElementsShape(key)) key = EffectKeys.Burst;   // Fire bursts from the strike instead
            switch (key)
            {
                case EffectKeys.Cone:
                    // Drawn exactly as long as it hits: a fa jin cone is stronger, not longer.
                    ElementVfx.Cone(element, origin, direction, move.Range, move.ArcDegrees);
                    return false;
                case EffectKeys.Pillar:
                    // The rising strike throws a burst up along it; the column itself rises under the enemy when it's
                    // actually launched (EnemyRigPresenter.OnLaunched), so there's only ever one.
                    ElementVfx.Burst(element, limb.position, direction, half);
                    return false;
                case EffectKeys.Whip:
                    whip.Stop();
                    whip = FireVfxHandle.None;
                    if (rig != null)
                        whip = ElementVfx.Whip(element, rig.GetAnchor(Limb.RightFist), origin, direction, move.Range, move.ArcDegrees,
                            move.Active + (p != null ? p.WhipLinger : 0.1f));
                    return false;
                case EffectKeys.Wheel:
                    ElementVfx.Wheel(element, body.position, move.Range);
                    return false;
                case EffectKeys.Slam:
                    // A downward burst from the striking limb. The ring on the floor appears where and when the enemy actually
                    // lands (EnemyRigPresenter.OnKnockedDown), not under the player.
                    ElementVfx.Burst(element, limb.position, Vector3.down, half);
                    return false;
                case EffectKeys.Trail:
                    ElementVfx.Burst(element, limb.position, direction, half);
                    // A wide spinning strike also throws a low ring.
                    if (move.ArcDegrees >= 180f && s.WideArcRingShare > 0f) ElementVfx.Ring(element, body.position, move.Range * s.WideArcRingShare);
                    return false;
                case EffectKeys.Wave:
                    ElementVfx.Wave(element, origin, direction, move.Range, move.ArcDegrees, scale);
                    return false;
                case EffectKeys.Line:
                    ElementVfx.Line(element, origin, direction, move.Range, scale);
                    return false;
                case EffectKeys.Dome:
                    ElementVfx.Dome(element, body.position, move.Range, direction, scale);
                    return false;
                case EffectKeys.Vortex:
                    ElementVfx.Vortex(element, body.position, move.Range, direction, scale);
                    return false;
                case EffectKeys.Shards:
                    ElementVfx.Shards(element, origin, direction, move.Range, scale);
                    return false;
                case EffectKeys.Stomp:
                    ElementVfx.Stomp(element, body.position, move.Range, direction, scale);
                    return true;
                default:
                    ElementVfx.Burst(element, origin, direction, scale);
                    return false;
            }
        }

        // The shapes Build 05 added for Water, Earth and Air; Fire has none of its own.
        static bool IsOtherElementsShape(string key)
        {
            return key == EffectKeys.Wave || key == EffectKeys.Line || key == EffectKeys.Dome || key == EffectKeys.Vortex
                   || key == EffectKeys.Shards || key == EffectKeys.Stomp;
        }

        // The first sub-hit of a move (every single-hit move's only one) carries the move's big effect.
        public static bool IsFirstSubHit(in PlayerEvent e)
        {
            return e.MoveInstanceId == 0 || e.AttackId == e.MoveInstanceId;
        }

        // Moves that throw a big effect of their own keep the limb's element shorter, so the big effect reads.
        public static bool HasBigEffect(MoveData move)
        {
            if (move == null) return false;
            string key = move.EffectKey;
            return key == EffectKeys.Cone || key == EffectKeys.Whip || key == EffectKeys.Wheel
                   || key == EffectKeys.Wave || key == EffectKeys.Dome || key == EffectKeys.Vortex || key == EffectKeys.Stomp;
        }

        public void Stop()
        {
            whip.Stop();
            whip = FireVfxHandle.None;
        }
    }
}
