using UnityEngine;

// All boss numbers live here. Each attack has a wind-up (the "tell")
// so the fight is hard but fair: the player can always see it coming.
// Create one via: right-click in Project > Create > Tuning > Boss.
[CreateAssetMenu(fileName = "BossTuning", menuName = "Tuning/Boss")]
public class BossTuning : ScriptableObject
{
    [System.Serializable]
    public class Attack
    {
        public string name = "Swipe";
        public float windUp = 0.8f;   // the tell: time before the hit lands
        public float activeTime = 0.2f; // how long the hit can connect
        public float recovery = 1.0f; // punish window for the player
        public float damage = 20f;
        public float range = 3f;
    }

    [Header("Health")]
    public float maxHealth = 1000f;
    [Range(0f, 1f)] public float phaseTwoAtHealthPercent = 0.5f;
    public float phaseTwoSpeedMultiplier = 1.2f;

    [Header("Movement")]
    public float moveSpeed = 3f;
    public float turnSpeed = 180f;

    [Header("Attacks")]
    public Attack[] attacks = new Attack[3];
}
