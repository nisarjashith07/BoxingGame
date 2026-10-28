using UnityEngine;

namespace BoxingAI
{
    public enum AIDifficulty
    {
        Easy,
        Medium,
        Hard
    }

    [CreateAssetMenu(fileName = "AIOpponentProfile", menuName = "Boxing AI/Opponent Profile")]
    public class AIOpponentProfile : ScriptableObject
    {
        [Header("Identity")]
        public AIDifficulty difficulty = AIDifficulty.Easy;

        [Header("Core Combat")]
        [Range(0f, 1f)] public float aggression = 0.5f;
        [Range(0f, 1f)] public float defense = 0.5f;
        [Range(0f, 1f)] public float mobility = 0.5f;
        [Range(0f, 1f)] public float comboSkill = 0.5f;
        [Range(0f, 1f)] public float counterSkill = 0.5f;
        [Range(0f, 1f)] public float adaptation = 0.25f;

        [Header("Decision Quality")]
        [Range(0f, 1f)] public float anticipation = 0.2f;
        [Range(0f, 1f)] public float randomness = 0.25f;
        [Range(0.05f, 1f)] public float decisionInterval = 0.25f;

        [Header("Resource Management")]
        [Range(0f, 1f)] public float staminaConservation = 0.5f;

        [Header("Preferred Range")]
        [Min(0.1f)] public float preferredDistance = 1.4f;
        [Min(0.1f)] public float attackDistance = 1.3f;
        [Min(0.1f)] public float retreatDistance = 0.65f;

        public static AIOpponentProfile CreateRuntime(AIDifficulty level)
        {
            var profile = CreateInstance<AIOpponentProfile>();
            profile.difficulty = level;

            switch (level)
            {
                case AIDifficulty.Easy:
                    profile.aggression = 0.65f;
                    profile.defense = 0.35f;
                    profile.mobility = 0.45f;
                    profile.comboSkill = 0.30f;
                    profile.counterSkill = 0.25f;
                    profile.adaptation = 0.10f;
                    profile.anticipation = 0.10f;
                    profile.randomness = 0.30f;
                    profile.decisionInterval = 0.22f;
                    profile.staminaConservation = 0.35f;
                    profile.preferredDistance = 1.45f;
                    profile.attackDistance = 1.30f;
                    profile.retreatDistance = 0.70f;
                    break;

                case AIDifficulty.Medium:
                    profile.aggression = 0.80f;
                    profile.defense = 0.55f;
                    profile.mobility = 0.60f;
                    profile.comboSkill = 0.55f;
                    profile.counterSkill = 0.50f;
                    profile.adaptation = 0.40f;
                    profile.anticipation = 0.35f;
                    profile.randomness = 0.20f;
                    profile.decisionInterval = 0.16f;
                    profile.staminaConservation = 0.55f;
                    profile.preferredDistance = 1.40f;
                    profile.attackDistance = 1.30f;
                    profile.retreatDistance = 0.70f;
                    break;

                case AIDifficulty.Hard:
                    profile.aggression = 0.90f;
                    profile.defense = 0.80f;
                    profile.mobility = 0.80f;
                    profile.comboSkill = 0.80f;
                    profile.counterSkill = 0.75f;
                    profile.adaptation = 0.75f;
                    profile.anticipation = 0.70f;
                    profile.randomness = 0.08f;
                    profile.decisionInterval = 0.10f;
                    profile.staminaConservation = 0.70f;
                    profile.preferredDistance = 1.35f;
                    profile.attackDistance = 1.25f;
                    profile.retreatDistance = 0.70f;
                    break;
            }

            return profile;
        }
    }
}
