using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace BoxingAI
{
    /// <summary>
    /// Main combat orchestrator. Each frame:
    /// 1. Reads player state from PunchDetector (timestamped MediaPipe landmarks + manual inputs)
    /// 2. Builds BoxingObservation from live game state
    /// 3. Feeds observation to RLOpponentController / AIOpponentController → gets BoxingAction
    /// 4. Executes action on AIFighterController with event-based stamina & cooldown enforcement
    /// 5. Performs hit detection (player→AI and AI→player)
    /// 6. Applies damage and updates HUD with audio/visual feedback
    /// 7. Checks win/loss conditions
    /// </summary>
    public class BoxingEnvironment : MonoBehaviour
    {
        [Header("Combat Components")]
        [SerializeField] private PunchDetector punchDetector;
        [SerializeField] private AIFighterController aiFighter;
        [SerializeField] private AIOpponentController aiOpponent;
        [SerializeField] private RLOpponentController rlOpponent;
        [SerializeField] private CombatSettings settings;

        [Header("AI Brain Selection")]
        [Tooltip("If true, drives the AI opponent using the trained Deep RL policy network. If false, uses the scripted FSM brain.")]
        [SerializeField] private bool useRLBrain = false;

        [Header("UI")]
        [SerializeField] private CombatHUD combatHUD;
        [SerializeField] private MatchTimer matchTimer;

        [Header("Player Reference")]
        [Tooltip("The player's avatar transform (PlayerArmature). Used as the AI's look-at target.")]
        [SerializeField] private Transform playerTransform;

        [Header("Match Configuration")]
        [SerializeField] private AIDifficulty difficulty = AIDifficulty.Easy;

        // Fighter states
        private FighterState playerState = new FighterState();
        private FighterState aiState = new FighterState();

        // Hit detection deduplication
        private bool playerPunchProcessed;
        private bool aiPunchProcessed;

        private bool matchActive;

        // --- Public API ---

        public FighterState PlayerState => playerState;
        public FighterState AIState => aiState;
        public bool MatchActive => matchActive;

        // --- Lifecycle ---

        private void Awake()
        {
            AutoFindReferences();
        }

        public void AutoFindReferences()
        {
            if (settings == null)
                settings = CombatSettings.CreateDefault();

            if (punchDetector == null)
                punchDetector = FindAnyObjectByType<PunchDetector>();

            if (aiFighter == null)
                aiFighter = FindAnyObjectByType<AIFighterController>();

            if (aiOpponent == null && aiFighter != null)
                aiOpponent = aiFighter.GetComponent<AIOpponentController>();
            if (aiOpponent == null)
                aiOpponent = FindAnyObjectByType<AIOpponentController>();

            if (rlOpponent == null && aiFighter != null)
                rlOpponent = aiFighter.GetComponent<RLOpponentController>();
            if (rlOpponent == null)
                rlOpponent = FindAnyObjectByType<RLOpponentController>();

            if (combatHUD == null)
                combatHUD = FindAnyObjectByType<CombatHUD>();

            if (matchTimer == null)
                matchTimer = FindAnyObjectByType<MatchTimer>();

            if (playerTransform == null)
            {
                var avatar = FindAnyObjectByType<AvatarController>();
                if (avatar != null)
                    playerTransform = avatar.transform;
            }
        }

        public void Initialize(
            PunchDetector detector,
            AIFighterController fighter,
            AIOpponentController opponent,
            CombatSettings combatSettings,
            CombatHUD hud,
            MatchTimer timer,
            Transform player)
        {
            Initialize(detector, fighter, opponent, null, combatSettings, hud, timer, player);
        }

        public void Initialize(
            PunchDetector detector,
            AIFighterController fighter,
            AIOpponentController opponent,
            RLOpponentController rlController,
            CombatSettings combatSettings,
            CombatHUD hud,
            MatchTimer timer,
            Transform player)
        {
            punchDetector = detector;
            aiFighter = fighter;
            aiOpponent = opponent;
            rlOpponent = rlController;
            settings = combatSettings;
            combatHUD = hud;
            matchTimer = timer;
            playerTransform = player;
        }

        private void Start()
        {
            AutoFindReferences();

            // Configure AI difficulty for scripted brain
            if (aiOpponent != null)
                aiOpponent.SetDifficulty(difficulty);

            // Tell AI fighter to face the player
            if (aiFighter != null && playerTransform != null)
                aiFighter.SetPlayerTarget(playerTransform);

            StartMatch();
        }

        /// <summary>Starts or restarts the match.</summary>
        public void StartMatch()
        {
            if (playerTransform != null)
                playerState.position = playerTransform.position;
            if (aiFighter != null)
                aiState.position = aiFighter.transform.position;

            playerState.Reset();
            aiState.Reset();

            if (punchDetector != null)
            {
                punchDetector.ResetDetector();
                punchDetector.SetFighterState(playerState);
            }

            if (aiFighter != null)
                aiFighter.ResetFighter();

            if (aiOpponent != null)
                aiOpponent.ResetBrain();

            if (rlOpponent != null)
                rlOpponent.ResetBrain();

            if (matchTimer != null)
                matchTimer.ResetTimer();

            playerPunchProcessed = false;
            aiPunchProcessed = false;
            matchActive = true;

            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;

            string brainType = useRLBrain && rlOpponent != null ? "Deep RL (Trained Policy)" : $"Scripted ({difficulty})";
            Debug.Log($"[Boxing] Match started — AI Brain: {brainType}");
        }

        // --- Game Loop ---

        private void Update()
        {
            if (!matchActive)
            {
#if ENABLE_INPUT_SYSTEM
                if (Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame)
                    StartMatch();
#else
                if (Input.GetKeyDown(KeyCode.R))
                    StartMatch();
#endif
                return;
            }

            // --- Win/Loss Check ---
            if (!playerState.IsAlive)
            {
                EndMatch("AI WINS — KO!");
                return;
            }
            if (!aiState.IsAlive)
            {
                EndMatch("PLAYER WINS — KO!");
                return;
            }
            if (matchTimer != null && matchTimer.TimeRemaining <= 0f)
            {
                string winner = playerState.health >= aiState.health
                    ? "PLAYER WINS" : "AI WINS";
                EndMatch($"{winner} — Decision!");
                return;
            }

            // --- 1. Process Player Pose (idempotent per camera frame ID) ---
            if (punchDetector != null)
            {
                PoseData pose = PoseManager.LatestPose;
                if (pose != null)
                    punchDetector.ProcessPose(pose);
            }

            // --- 2. Update Fighter States ---
            UpdatePlayerState();
            UpdateAIState();

            // Tick cooldowns, stamina regen, and root-position velocity
            if (settings != null)
            {
                playerState.Tick(Time.deltaTime, settings.staminaRegenRate, settings.blockStaminaDrain);
                aiState.Tick(Time.deltaTime, settings.staminaRegenRate, settings.blockStaminaDrain);
            }

            // Blend upper-body camera slip velocity if player is standing stationary in camera mode
            if (punchDetector != null && punchDetector.HasActiveTracking && playerState.velocity.sqrMagnitude < 0.001f)
            {
                playerState.velocity = punchDetector.PlayerVelocity;
            }

            // --- 3. AI Decision ---
            if (aiFighter != null && punchDetector != null)
            {
                float roundTime01 = matchTimer != null ? matchTimer.NormalizedTimeRemaining : 1f;
                BoxingObservation observation = ObservationBuilder.Build(
                    aiState, playerState, punchDetector, aiFighter, roundTime01);

                BoxingAction action;
                if (useRLBrain && rlOpponent != null)
                {
                    action = rlOpponent.Evaluate(observation);
                }
                else if (aiOpponent != null)
                {
                    action = aiOpponent.Evaluate(observation);
                }
                else
                {
                    action = BoxingAction.Idle;
                }

                // --- 4. Execute AI Action (event-based stamina & cooldowns inside AIFighterController) ---
                aiFighter.ExecuteAction(action, aiState);
            }

            // --- 5. Hit Detection ---
            ProcessHits();

            // --- 6. Update HUD ---
            if (combatHUD != null)
                combatHUD.UpdateHUD(playerState, aiState);
        }

        // --- State Sync ---

        private void UpdatePlayerState()
        {
            if (playerTransform != null)
                playerState.position = playerTransform.position;
            else if (punchDetector != null)
                playerState.position = punchDetector.PlayerCenter;

            if (punchDetector != null)
            {
                playerState.guard = punchDetector.GuardLevel01;
                playerState.isBlocking = punchDetector.IsGuarding;
                playerState.isAttacking = punchDetector.IsPunching;
                playerState.isRecovering = punchDetector.IsRecovering;
            }
        }

        private void UpdateAIState()
        {
            if (aiFighter != null)
            {
                aiState.position = aiFighter.transform.position;
                aiState.isBlocking = aiFighter.IsBlocking;
                aiState.isAttacking = aiFighter.IsCommittedToAttack;
                aiState.isRecovering = aiFighter.IsRecovering;
                aiState.guard = aiFighter.IsBlocking ? 1f : 0f;
            }
        }

        // --- Hit Detection ---

        private void ProcessHits()
        {
            if (aiFighter == null || punchDetector == null || settings == null) return;

            float fighterDistance = Vector3.Distance(playerState.position, aiState.position);

            // --- Player → AI ---
            if (punchDetector.IsPunching && !playerPunchProcessed)
            {
                if (aiFighter.IsDodging)
                {
                    playerPunchProcessed = true;
                    Debug.Log("[Boxing] AI dodged Player's punch!");
                }
                else
                {
                    Vector3 aiTorso = aiFighter.transform.position + Vector3.up * 1.1f;
                    Vector3 handPos = (punchDetector.LastPunchType == BoxingAction.Jab || punchDetector.LastPunchType == BoxingAction.HookLeft)
                        ? punchDetector.LeftWristPosition
                        : punchDetector.RightWristPosition;

                    float handDist = Vector3.Distance(handPos, aiTorso);
                    float maxHitDist = settings != null ? settings.hitRange : 1.6f;
                    bool hitConnected = handDist < 1.25f || fighterDistance < 1.25f || (handDist < 1.5f && fighterDistance < maxHitDist);

                    if (hitConnected)
                    {
                        float damage = settings.GetDamage(punchDetector.LastPunchType);
                        bool blocked = aiFighter.IsBlocking;
                        float damageDealt = blocked ? damage * (1f - settings.blockDamageReduction) : damage;

                        aiState.TakeDamage(damage, blocked, settings.blockDamageReduction);
                        playerPunchProcessed = true;

                        aiFighter.FlashHit();

                        if (combatHUD != null)
                            combatHUD.OnHit("PLAYER", punchDetector.LastPunchType, damageDealt, blocked, aiTorso);

                        Debug.Log($"[Boxing] Player {punchDetector.LastPunchType} → AI! " +
                                  $"Damage: {damageDealt:F1}{(blocked ? " (BLOCKED)" : "")} " +
                                  $"| AI Health: {aiState.health:F0}");
                    }
                }
            }
            if (!punchDetector.IsPunching)
                playerPunchProcessed = false;

            // --- AI → Player ---
            if (aiFighter.IsAttacking && !aiPunchProcessed)
            {
                Vector3 playerTorso = playerState.position + Vector3.up * 1.1f;
                float hitDist = Vector3.Distance(aiFighter.AttackHandPosition, playerTorso);
                bool hitConnected = hitDist < 0.75f || (hitDist < 1.0f && fighterDistance < 1.35f);

                if (hitConnected)
                {
                    float damage = settings.GetDamage(aiFighter.CurrentPunchType);
                    bool blocked = punchDetector.IsGuarding;
                    float damageDealt = blocked ? damage * (1f - settings.blockDamageReduction) : damage;

                    playerState.TakeDamage(damage, blocked, settings.blockDamageReduction);
                    aiPunchProcessed = true;

                    if (combatHUD != null)
                        combatHUD.OnHit("AI", aiFighter.CurrentPunchType, damageDealt, blocked, playerTorso);

                    Debug.Log($"[Boxing] AI {aiFighter.CurrentPunchType} → Player! " +
                              $"Damage: {damageDealt:F1}{(blocked ? " (BLOCKED)" : "")} " +
                              $"| Player Health: {playerState.health:F0}");
                }
            }
            if (!aiFighter.IsAttacking)
                aiPunchProcessed = false;
        }

        // --- Match Flow ---

        private void EndMatch(string result)
        {
            matchActive = false;

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            if (matchTimer != null)
                matchTimer.StopTimer();
            if (combatHUD != null)
                combatHUD.ShowResult(result);

            Debug.Log($"[Boxing] MATCH OVER — {result}");
        }
    }
}
