using System;
using UnityEngine;

namespace BoxingAI
{
    /// <summary>
    /// Drives the AI opponent character using the trained Deep RL policy network.
    ///
    /// Evaluates observations in real-time (~0.02ms) with zero garbage collection.
    /// Automatically loads the trained weights from 'Assets/Resources/boxing_ai_policy.bytes' (TextAsset).
    /// If weights are not found or disabled, smoothly falls back to the scripted AIOpponentBrain.
    /// </summary>
    public class RLOpponentController : MonoBehaviour
    {
        [Header("Model Configuration")]
        [Tooltip("The binary policy weights asset (boxing_ai_policy.bytes). If unassigned, automatically loaded from Resources or Assets/Models.")]
        [SerializeField] private TextAsset policyModelAsset;

        [Header("Fallback")]
        [Tooltip("If the RL model cannot be loaded, fall back to the scripted AI brain.")]
        [SerializeField] private bool fallbackToScripted = true;
        [SerializeField] private AIDifficulty fallbackDifficulty = AIDifficulty.Hard;

        [Header("Debug")]
        [SerializeField] private bool logActions = false;
        [SerializeField] private bool logLogits = false;

        private RLPolicyNetwork policyNetwork;
        private AIOpponentBrain fallbackBrain;
        private readonly float[] inputBuffer = new float[18];

        public bool IsUsingRL => policyNetwork != null && policyNetwork.IsLoaded;
        public BoxingAction CurrentAction { get; private set; } = BoxingAction.Idle;
        public BoxingObservation LatestObservation { get; private set; }

        private void Awake()
        {
            InitializeModel();
        }

        public void InitializeModel()
        {
            // 1. Try loading from assigned TextAsset
            if (policyModelAsset != null && policyModelAsset.bytes != null && policyModelAsset.bytes.Length > 0)
            {
                try
                {
                    policyNetwork = RLPolicyNetwork.LoadFromBytes(policyModelAsset.bytes);
                    Debug.Log("<color=green>[RLOpponent] Trained RL policy loaded successfully from assigned asset.</color>");
                    return;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[RLOpponent] Failed to load policy from asset: {ex.Message}");
                }
            }

            // 2. Try loading from Resources
            var resAsset = Resources.Load<TextAsset>("boxing_ai_policy");
            if (resAsset != null && resAsset.bytes != null && resAsset.bytes.Length > 0)
            {
                try
                {
                    policyNetwork = RLPolicyNetwork.LoadFromBytes(resAsset.bytes);
                    Debug.Log("<color=green>[RLOpponent] Trained RL policy loaded automatically from Resources.</color>");
                    return;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[RLOpponent] Failed to load policy from Resources: {ex.Message}");
                }
            }

            // 3. Fallback to scripted brain
            if (fallbackToScripted)
            {
                var profile = AIOpponentProfile.CreateRuntime(fallbackDifficulty);
                fallbackBrain = new AIOpponentBrain(profile);
                Debug.Log($"[RLOpponent] Using scripted fallback brain ({fallbackDifficulty}).");
            }
        }

        public BoxingAction Evaluate(BoxingObservation obs)
        {
            LatestObservation = obs;

            if (IsUsingRL)
            {
                BuildInputBuffer(obs, inputBuffer);
                BoxingAction action = policyNetwork.Predict(inputBuffer, out float[] logits);
                CurrentAction = action;

                if (logLogits)
                {
                    string lStr = "";
                    for (int i = 0; i < logits.Length; i++)
                        lStr += $"{(BoxingAction)i}:{logits[i]:F2} ";
                    Debug.Log($"[RLOpponent] Logits: {lStr}");
                }

                if (logActions)
                    Debug.Log($"[RLOpponent] Action: {action}");

                return action;
            }

            // Scripted fallback
            if (fallbackBrain != null)
            {
                CurrentAction = fallbackBrain.Decide(obs, Time.deltaTime);
                if (logActions)
                    Debug.Log($"[RLOpponent (Scripted)] Action: {CurrentAction}");
                return CurrentAction;
            }

            return BoxingAction.Idle;
        }

        public void ResetBrain()
        {
            CurrentAction = BoxingAction.Idle;
            LatestObservation = default;
            if (fallbackBrain != null)
            {
                var profile = AIOpponentProfile.CreateRuntime(fallbackDifficulty);
                fallbackBrain = new AIOpponentBrain(profile);
            }
        }

        public void SetPolicyAsset(TextAsset asset)
        {
            policyModelAsset = asset;
            InitializeModel();
        }

        private static void BuildInputBuffer(BoxingObservation o, float[] buffer)
        {
            // ARENA_HALF = 5.0f, MAX_HP = 100.0f (exact match with boxing_env.py)
            buffer[0]  = Mathf.Clamp01(o.distance / 10.0f);                      // distance normalised
            buffer[1]  = Mathf.Clamp01(o.selfHealth01);                          // selfHealth01
            buffer[2]  = Mathf.Clamp01(o.selfStamina01);                         // selfStamina01
            buffer[3]  = Mathf.Clamp01(o.opponentHealth01);                      // opponentHealth01
            buffer[4]  = Mathf.Clamp01(o.opponentStamina01);                     // opponentStamina01
            buffer[5]  = Mathf.Clamp01(o.selfGuard01);                           // selfGuard01
            buffer[6]  = Mathf.Clamp01(o.opponentGuard01);                       // opponentGuard01
            buffer[7]  = Mathf.Clamp(o.relativeX / 5.0f, -1.0f, 1.0f);            // relativeX
            buffer[8]  = Mathf.Clamp(o.relativeZ / 5.0f, -1.0f, 1.0f);            // relativeZ
            buffer[9]  = Mathf.Clamp(o.relativeVelocityX, -1.0f, 1.0f);          // relativeVelocityX
            buffer[10] = Mathf.Clamp(o.relativeVelocityZ, -1.0f, 1.0f);          // relativeVelocityZ
            buffer[11] = Mathf.Clamp01(o.opponentAttackIntent01);                // opponentAttackIntent01
            buffer[12] = o.incomingAttack ? 1.0f : 0.0f;                         // incomingAttack
            buffer[13] = o.opponentRecovering ? 1.0f : 0.0f;                     // opponentRecovering
            buffer[14] = o.selfCanAttack ? 1.0f : 0.0f;                          // selfCanAttack
            buffer[15] = o.selfCanDodge ? 1.0f : 0.0f;                           // selfCanDodge
            buffer[16] = o.selfCanBlock ? 1.0f : 0.0f;                           // selfCanBlock
            buffer[17] = Mathf.Clamp(o.selfHealth01 - o.opponentHealth01, -1.0f, 1.0f); // healthAdvantage
        }
    }
}
