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
            // Defensive reaction to incoming player punches
            if (o.incomingAttack)
            {
                float defensivePressure = profile.defense * 1.3f;
                defensivePressure += profile.anticipation * 0.35f;

                if (o.selfCanDodge && ScoreRoll(defensivePressure * 0.7f))
                    return ChooseDodge(o);

                if (o.selfCanBlock && ScoreRoll(profile.defense))
                    return BoxingAction.Block;
            }

            // In boxing range (within ~1.65m): Stand ground, ATTACK, or guard!
            if (o.distance <= profile.attackDistance)
            {
                // Counter attack or normal attack
                if (o.selfCanAttack)
                {
                    float attackChance = profile.aggression;
                    if (o.opponentRecovering) attackChance += 0.25f;

                    if (ScoreRoll(attackChance))
                        return ChooseAttack(o, o.opponentRecovering);
                }

                // If on cooldown or low stamina: hold guard or stay in idle stance (never run!)
                return o.selfCanBlock && ScoreRoll(profile.defense * 0.6f)
                    ? BoxingAction.Block
                    : BoxingAction.Idle;
            }

            // Outside range (> 1.65m): Walk forward into the pocket
            return BoxingAction.MoveForward;
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
