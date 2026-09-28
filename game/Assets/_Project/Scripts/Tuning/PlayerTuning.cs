using UnityEngine;

// All player numbers live here so balancing is editing values, not code.
// Create one via: right-click in Project > Create > Tuning > Player.
[CreateAssetMenu(fileName = "PlayerTuning", menuName = "Tuning/Player")]
public class PlayerTuning : ScriptableObject
{
    [Header("Health")]
    public float maxHealth = 100f;

    [Header("Movement")]
    public float walkSpeed = 4f;
    public float sprintSpeed = 7f;
    public float turnSpeed = 720f; // degrees per second

    [Header("Stamina")]
    public float maxStamina = 100f;
    public float staminaRegenPerSecond = 25f;
    public float staminaRegenDelay = 0.8f; // seconds after spending before regen starts
    public float sprintStaminaPerSecond = 15f;

    [Header("Dodge")]
    public float dodgeStaminaCost = 20f;
    public float dodgeDistance = 4f;
    public float dodgeDuration = 0.5f;
    public float dodgeIFrameStart = 0.05f; // invincibility window, seconds into the dodge
    public float dodgeIFrameEnd = 0.35f;

    [Header("Attacks")]
    public float lightAttackDamage = 10f;
    public float lightAttackStaminaCost = 12f;
    public float heavyAttackDamage = 25f;
    public float heavyAttackStaminaCost = 30f;
}
