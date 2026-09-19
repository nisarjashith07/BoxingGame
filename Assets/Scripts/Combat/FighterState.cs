using UnityEngine;

namespace BoxingAI
{
    /// <summary>
    /// Tracks the combat state of a single fighter (player or AI).
    /// Health, stamina, guard level, cooldowns, and spatial data.
    /// </summary>
    [System.Serializable]
    public class FighterState
    {
        public float health = 100f;
        public float stamina = 100f;

        [Range(0f, 1f)]
        public float guard;

        public bool isBlocking;
        public bool isAttacking;
        public bool isRecovering;

        public float attackCooldown;
        public float dodgeCooldown;

        public Vector3 position;
        public Vector3 velocity;

        private Vector3 previousPosition;

        // --- Derived Properties ---

        public bool CanAttack => attackCooldown <= 0f && stamina > 10f && !isRecovering;
        public bool CanDodge => dodgeCooldown <= 0f && stamina > 15f && !isAttacking;
        public bool CanBlock => stamina > 5f && !isAttacking;
        public bool IsAlive => health > 0f;

        public float Health01 => Mathf.Clamp01(health / 100f);
        public float Stamina01 => Mathf.Clamp01(stamina / 100f);

        // --- Methods ---

        public void TakeDamage(float amount, bool blocked, float blockReduction)
        {
            float finalDamage = blocked ? amount * (1f - blockReduction) : amount;
            health = Mathf.Max(0f, health - finalDamage);
        }

        public void DrainStamina(float amount)
        {
            stamina = Mathf.Max(0f, stamina - amount);
        }

        public void Tick(float deltaTime, float staminaRegenRate, float blockStaminaDrain)
        {
            // Cooldowns
            if (attackCooldown > 0f) attackCooldown -= deltaTime;
            if (dodgeCooldown > 0f) dodgeCooldown -= deltaTime;

            // Stamina: drain while blocking, regenerate otherwise
            if (isBlocking)
                stamina = Mathf.Max(0f, stamina - blockStaminaDrain * deltaTime);
            else
                stamina = Mathf.Min(100f, stamina + staminaRegenRate * deltaTime);

            // Force drop guard if stamina depleted
            if (stamina <= 0f && isBlocking)
                isBlocking = false;

            // Velocity from position delta
            velocity = (position - previousPosition) / Mathf.Max(deltaTime, 0.001f);
            previousPosition = position;
        }

        public void Reset()
        {
            health = 100f;
            stamina = 100f;
            guard = 0f;
            isBlocking = false;
            isAttacking = false;
            isRecovering = false;
            attackCooldown = 0f;
            dodgeCooldown = 0f;
            velocity = Vector3.zero;
        }
    }
}
