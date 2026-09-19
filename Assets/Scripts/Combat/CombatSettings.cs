using UnityEngine;

namespace BoxingAI
{
    /// <summary>
    /// ScriptableObject containing all tunable combat constants.
    /// Create via: Right-click → Create → Boxing → Combat Settings.
    /// </summary>
    [CreateAssetMenu(fileName = "CombatSettings", menuName = "Boxing/Combat Settings")]
    public class CombatSettings : ScriptableObject
    {
        [Header("Punch Damage")]
        public float jabDamage = 8f;
        public float crossDamage = 14f;
        public float hookDamage = 18f;

        [Header("Defense")]
        [Range(0f, 1f)]
        public float blockDamageReduction = 0.75f;

        [Header("Stamina Costs")]
        public float punchStaminaCost = 12f;
        public float blockStaminaDrain = 5f;
        public float dodgeStaminaCost = 20f;

        [Header("Stamina Regeneration")]
        public float staminaRegenRate = 8f;

        [Header("Ranges")]
        public float hitRange = 1.8f;

        [Header("Cooldowns")]
        public float attackCooldown = 0.4f;
        public float dodgeCooldown = 0.6f;
        public float recoveryTime = 0.3f;

        [Header("Player Punch Detection (MediaPipe)")]
        [Tooltip("Minimum wrist speed (units/sec) to register a punch.")]
        public float punchVelocityThreshold = 0.8f;
        [Tooltip("Arm must extend this multiple of resting length to count as a punch.")]
        public float armExtensionMultiplier = 1.15f;
        [Tooltip("Average wrist-to-nose distance below which guard is detected.")]
        public float guardDistanceThreshold = 0.20f;

        [Header("AI Movement")]
        public float aiMoveSpeed = 1.1f;
        public float aiStrafeSpeed = 0.8f;
        public float aiDodgeDistance = 0.7f;
        public float aiDodgeDuration = 0.25f;
        public float aiRotationSpeed = 8f;

        [Header("AI Punch Timing")]
        public float aiPunchWindup = 0.1f;
        public float aiPunchExtend = 0.15f;
        public float aiPunchHold = 0.05f;
        public float aiPunchRetract = 0.2f;

        /// <summary>Returns the damage value for a given punch action.</summary>
        public float GetDamage(BoxingAction action)
        {
            switch (action)
            {
                case BoxingAction.Jab: return jabDamage;
                case BoxingAction.Cross: return crossDamage;
                case BoxingAction.HookLeft:
                case BoxingAction.HookRight: return hookDamage;
                default: return 0f;
            }
        }

        /// <summary>Creates a CombatSettings instance at runtime with default values.</summary>
        public static CombatSettings CreateDefault()
        {
            return CreateInstance<CombatSettings>();
        }
    }
}
