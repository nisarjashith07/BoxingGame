using System;
using UnityEngine;

namespace BoxingAI
{
    public class AIOpponentBrain
    {
        private readonly AIOpponentProfile profile;
        private readonly OpponentPatternMemory memory = new OpponentPatternMemory();

        private BoxingAction lastAction = BoxingAction.Idle;
        private BoxingAction previousAction = BoxingAction.Idle;
        private float decisionTimer;
        private int comboStep;
        private float comboTimer;

        public BoxingAction LastAction => lastAction;
        public OpponentPatternMemory Memory => memory;

        public AIOpponentBrain(AIOpponentProfile profile)
        {
            this.profile = profile ?? throw new ArgumentNullException(nameof(profile));
        }

        public BoxingAction Decide(BoxingObservation observation, float deltaTime)
        {
            memory.Observe(observation);
            decisionTimer -= deltaTime;
            comboTimer -= deltaTime;

            if (decisionTimer > 0f)
                return lastAction;

            decisionTimer = Mathf.Max(0.05f,
                profile.decisionInterval * UnityEngine.Random.Range(1f - profile.randomness, 1f + profile.randomness));

            if (comboTimer <= 0f && comboStep > 0)
                comboStep = 0;

            BoxingAction chosen = ChooseAction(observation);
            previousAction = lastAction;
            lastAction = chosen;
            return chosen;
        }

        private BoxingAction ChooseAction(BoxingObservation o)
        {
            // --- Stamina-based recovery retreat ---
            if (o.selfStamina01 < 0.20f && profile.staminaConservation > 0.45f)
            {
                if (ScoreRoll(profile.staminaConservation))
                    return ChooseRecoveryMovement(o);
            }

            // --- Anticipate incoming attack using pattern memory ---
            float anticipatedThreat = o.opponentAttackIntent01
                + memory.AggressionEstimate * profile.adaptation * profile.anticipation;

            bool threatened = o.incomingAttack
                || (profile.anticipation > 0.4f && anticipatedThreat > 0.55f);

            // --- Defensive reaction to incoming / anticipated player punches ---
            if (threatened && (o.incomingAttack || ScoreRoll(profile.anticipation * 0.6f)))
            {
                float defensivePressure = profile.defense * 1.3f;
                defensivePressure += profile.anticipation * 0.35f;
                // Hard opponents exploit low blocker tendency
                if (memory.BlockRate < 0.15f && profile.adaptation > 0.5f)
                    defensivePressure -= 0.12f;

                if (o.selfCanDodge && ScoreRoll(defensivePressure * 0.7f))
                    return ChooseDodge(o);

                if (o.selfCanBlock && ScoreRoll(profile.defense))
                    return BoxingAction.Block;
            }

            // --- In boxing range: stand ground, attack, or guard ---
            if (o.distance <= profile.attackDistance)
            {
                // Punish recovering opponent (counter window)
                if (o.opponentRecovering && o.selfCanAttack)
                {
                    float punishChance = profile.counterSkill + profile.aggression * 0.4f;
                    if (ScoreRoll(punishChance))
                        return ChooseAttack(o, punish: true);
                }

                // Normal attack decision influenced by opponent pattern memory
                if (o.selfCanAttack)
                {
                    float attackChance = profile.aggression;
                    // Exploit opponent who rarely guards
                    if (memory.BlockRate < 0.25f && profile.adaptation > 0.3f)
                        attackChance += 0.15f * profile.adaptation;
                    // Back off if opponent is very aggressive and our stamina is low
                    if (memory.AggressionEstimate > 0.7f && o.selfStamina01 < 0.4f)
                        attackChance -= 0.20f * profile.staminaConservation;

                    if (ScoreRoll(attackChance))
                        return ChooseAttack(o, punish: false);
                }

                // Retreat if pushed into retreat threshold
                if (o.distance < profile.retreatDistance && profile.mobility > 0.4f)
                    return ChooseRetreat(o);

                // Hold guard
                if (o.selfCanBlock && ScoreRoll(profile.defense * 0.6f))
                    return BoxingAction.Block;

                // Lateral footwork to open angles
                if (profile.mobility > 0.55f && ScoreRoll(profile.mobility * 0.35f))
                    return UnityEngine.Random.value < 0.5f ? BoxingAction.StrafeLeft : BoxingAction.StrafeRight;

                return BoxingAction.Idle;
            }

            // --- Outside range: close the gap intelligently ---
            if (o.distance > profile.preferredDistance + 0.5f)
                return ChooseApproach(o);

            return ChooseMaintain(o);
        }

        private BoxingAction ChooseAttack(BoxingObservation o, bool punish)
        {
            if (comboStep > 0 && comboTimer > 0f && profile.comboSkill > 0.25f)
            {
                comboStep++;
                comboTimer = 0.55f;

                if (comboStep % 3 == 0)
                    return BoxingAction.HookLeft;

                return profile.comboSkill > 0.65f
                    ? BoxingAction.Cross
                    : BoxingAction.Jab;
            }

            comboStep = 1;
            comboTimer = Mathf.Lerp(0.20f, 0.75f, profile.comboSkill);

            if (punish && profile.counterSkill > 0.45f)
                return BoxingAction.Cross;

            float roll = UnityEngine.Random.value;
            if (roll < 0.42f)
                return BoxingAction.Jab;
            if (roll < 0.72f)
                return BoxingAction.Cross;
            if (roll < 0.86f)
                return BoxingAction.HookLeft;
            return BoxingAction.HookRight;
        }

        private BoxingAction ChooseApproach(BoxingObservation o)
        {
            // Occasionally strafe while approaching for unpredictability
            if (profile.mobility > 0.6f && UnityEngine.Random.value < profile.mobility * 0.2f)
                return UnityEngine.Random.value < 0.5f ? BoxingAction.StrafeLeft : BoxingAction.StrafeRight;

            return BoxingAction.MoveForward;
        }

        private BoxingAction ChooseMaintain(BoxingObservation o)
        {
            float offset = o.distance - profile.preferredDistance;

            // When in pocket range, stand ground and prepare to strike
            if (Mathf.Abs(offset) < 0.35f)
            {
                return UnityEngine.Random.value < profile.defense * 0.4f
                    ? BoxingAction.Block
                    : BoxingAction.Idle;
            }

            return offset > 0f
                ? BoxingAction.MoveForward
                : BoxingAction.MoveBackward;
        }

        private BoxingAction ChooseRetreat(BoxingObservation o)
        {
            if (profile.mobility > 0.5f && Mathf.Abs(o.relativeX) > 0.2f)
                return o.relativeX > 0f ? BoxingAction.StrafeLeft : BoxingAction.StrafeRight;

            return BoxingAction.MoveBackward;
        }

        private BoxingAction ChooseRecoveryMovement(BoxingObservation o)
        {
            if (profile.mobility > 0.55f && UnityEngine.Random.value < 0.55f)
                return UnityEngine.Random.value < 0.5f
                    ? BoxingAction.StrafeLeft
                    : BoxingAction.StrafeRight;

            return BoxingAction.MoveBackward;
        }

        private BoxingAction ChooseDodge(BoxingObservation o)
        {
            bool preferLeft = o.relativeX > 0f;
            if (UnityEngine.Random.value < 0.25f)
                preferLeft = !preferLeft;

            return preferLeft ? BoxingAction.DodgeLeft : BoxingAction.DodgeRight;
        }

        private bool ScoreRoll(float score)
        {
            score = Mathf.Clamp01(score);
            float noise = UnityEngine.Random.Range(-profile.randomness, profile.randomness);
            return UnityEngine.Random.value < Mathf.Clamp01(score + noise);
        }
    }
}
