using UnityEngine;

namespace BoxingAI
{
    public class OpponentPatternMemory
    {
        private float attackRate;
        private float closeRangeRate;
        private float blockRate;
        private float dodgeRate;
        private float aggressionEstimate;

        private int observations;

        public float AggressionEstimate => aggressionEstimate;
        public float AttackRate => attackRate;
        public float CloseRangeRate => closeRangeRate;
        public float BlockRate => blockRate;
        public float DodgeRate => dodgeRate;

        public void Observe(BoxingObservation observation)
        {
            observations++;

            float attack = observation.opponentAttackIntent01;
            float close = observation.distance < 1.35f ? 1f : 0f;
            float block = observation.opponentGuard01;
            float dodge = Mathf.Abs(observation.relativeVelocityX) > 0.6f ? 1f : 0f;

            float alpha = observations < 20 ? 0.10f : 0.05f;
            attackRate = Mathf.Lerp(attackRate, attack, alpha);
            closeRangeRate = Mathf.Lerp(closeRangeRate, close, alpha);
            blockRate = Mathf.Lerp(blockRate, block, alpha);
            dodgeRate = Mathf.Lerp(dodgeRate, dodge, alpha);

            aggressionEstimate = Mathf.Clamp01(
                attackRate * 0.65f + closeRangeRate * 0.20f + (1f - blockRate) * 0.15f);
        }
    }
}
