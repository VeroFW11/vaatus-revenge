using System.Collections.Generic;
using UnityEngine;
using VaatusRevenge.Core;

namespace VaatusRevenge
{
    // Marks a GameObject as a fighter: the player, an enemy or a training dummy.
    // Every fighter registers itself here while active, so other systems can find fighters without
    // searching the scene: lock-on scans Combatant.All, enemies read Combatant.Player, and the hit
    // system tests attacks against each fighter's capsule (feet position + Height + Radius).
    // The actual health/guard/dodge logic lives in the IDamageReceiver component on the same object.
    [DisallowMultipleComponent]
    public class Combatant : MonoBehaviour
    {
        public static readonly List<Combatant> All = new List<Combatant>();
        public static Combatant Player { get; private set; }

        [SerializeField] Team team = Team.Enemy;
        [Tooltip("Where lock-on points and projectiles aim. Usually a child at chest height.")]
        [SerializeField] Transform aimPoint;
        [Tooltip("Body radius in metres, used by hit detection and lock-on.")]
        [SerializeField] float radius = 0.4f;
        [Tooltip("Body height in metres, measured up from this object's position (its feet).")]
        [SerializeField] float height = 1.8f;
        [Tooltip("Name shown on screen. Lore names live here in data, never in code.")]
        [SerializeField] string displayName = "";

        IDamageReceiver receiver;
        int id;

        public Team Team => team;
        public float Radius => radius;
        public float Height => height;
        public string DisplayName => string.IsNullOrEmpty(displayName) ? name : displayName;
        public Transform AimPoint => aimPoint != null ? aimPoint : transform;

        // Feet position; the capsule runs from here up to Height.
        public Vector3 Feet => transform.position;

        // Unique per fighter for this play session (used to stop one swing hitting a fighter twice).
        public int Id
        {
            get
            {
                if (id == 0) id = CombatIds.Next();
                return id;
            }
        }

        public IDamageReceiver Receiver
        {
            get
            {
                // Looked up lazily so the order components were added in doesn't matter.
                if (receiver == null) receiver = GetComponent<IDamageReceiver>();
                return receiver;
            }
        }

        public bool IsAlive
        {
            get
            {
                IDamageReceiver r = Receiver;
                return r != null && r.IsAlive;
            }
        }

        // For code that builds fighters at runtime or in the sandbox builder.
        public void Configure(Team newTeam, Transform newAimPoint, float newRadius, float newHeight, string newDisplayName)
        {
            bool wasActive = isActiveAndEnabled;
            if (wasActive) OnDisable();
            team = newTeam;
            aimPoint = newAimPoint;
            radius = newRadius;
            height = newHeight;
            displayName = newDisplayName;
            if (wasActive) OnEnable();
        }

        void OnEnable()
        {
            if (!All.Contains(this)) All.Add(this);
            if (team == Team.Player) Player = this;
        }

        void OnDisable()
        {
            All.Remove(this);
            if (Player == this) Player = null;
        }
    }
}
