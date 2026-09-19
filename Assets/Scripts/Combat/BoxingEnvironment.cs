using UnityEngine;

namespace BoxingAI
{
    /// <summary>
    /// Main combat orchestrator. Each frame:
    /// 1. Reads player state from PunchDetector (MediaPipe landmarks + manual inputs)
    /// 2. Builds BoxingObservation from live game state
    /// 3. Feeds observation to AIOpponentController → gets BoxingAction
    /// 4. Executes action on AIFighterController
    /// 5. Performs hit detection (player→AI and AI→player)
    /// 6. Applies damage/stamina and updates HUD with audio/visual feedback
    /// 7. Checks win/loss conditions
    /// </summary>
    public class BoxingEnvironment : MonoBehaviour
    {
        [Header("Combat Components")]
        [SerializeField] private PunchDetector punchDetector;
        [SerializeField] private AIFighterController aiFighter;
        [SerializeField] private AIOpponentController aiOpponent;
        [SerializeField] private CombatSettings settings;

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
        private BoxingAction lastAIAction = BoxingAction.Idle;

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
            punchDetector = detector;
            aiFighter = fighter;
            aiOpponent = opponent;
            settings = combatSettings;
            combatHUD = hud;
            matchTimer = timer;
            playerTransform = player;
        }

        private void Start()
        {
            AutoFindReferences();

            // Configure AI difficulty
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
            playerState.Reset();
            aiState.Reset();

            if (aiOpponent != null)
                aiOpponent.ResetBrain();

            if (matchTimer != null)
                matchTimer.ResetTimer();

            playerPunchProcessed = false;
            aiPunchProcessed = false;
            matchActive = true;

            Debug.Log($"[Boxing] Match started — Difficulty: {difficulty}");
        }

        // --- Game Loop ---

        private void Update()
        {
            if (!matchActive) return;

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

            // --- 1. Process Player Pose ---
            if (punchDetector != null)
            {
                PoseData pose = PoseManager.LatestPose;
                if (pose != null)
                    punchDetector.ProcessPose(pose);
            }

            // --- 2. Update Fighter States ---
            UpdatePlayerState();
            UpdateAIState();

            // Tick cooldowns and stamina regen
            if (settings != null)
            {
                playerState.Tick(Time.deltaTime, settings.staminaRegenRate, settings.blockStaminaDrain);
                aiState.Tick(Time.deltaTime, settings.staminaRegenRate, settings.blockStaminaDrain);
            }

            // --- 3. AI Decision ---
            if (aiOpponent != null && aiFighter != null && punchDetector != null)
            {
                float roundTime01 = matchTimer != null ? matchTimer.NormalizedTimeRemaining : 1f;
                BoxingObservation observation = ObservationBuilder.Build(
                    aiState, playerState, punchDetector, aiFighter, roundTime01);

                BoxingAction action = aiOpponent.Evaluate(observation);

                // --- 4. Execute AI Action ---
                aiFighter.ExecuteAction(action);

                // Drain AI stamina for costly actions only on initiation
                if (settings != null && action != lastAIAction)
                {
                    if (action >= BoxingAction.Jab && action <= BoxingAction.HookRight)
                        aiState.DrainStamina(settings.punchStaminaCost);
                    else if (action == BoxingAction.DodgeLeft || action == BoxingAction.DodgeRight)
                        aiState.DrainStamina(settings.dodgeStaminaCost);
                }
                lastAIAction = action;
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
                playerState.velocity = punchDetector.PlayerVelocity;
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
                aiState.isAttacking = aiFighter.IsAttacking;
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
                Vector3 aiTorso = aiFighter.transform.position + Vector3.up * 1.1f;
                float leftDist = Vector3.Distance(punchDetector.LeftWristPosition, aiTorso);
                float rightDist = Vector3.Distance(punchDetector.RightWristPosition, aiTorso);
                float closestHand = Mathf.Min(leftDist, rightDist);

                // Hits connect if punching hand reaches AI or fighters are in close pocket range
                if (closestHand < settings.hitRange || fighterDistance < settings.hitRange)
                {
                    float damage = settings.GetDamage(punchDetector.LastPunchType);
                    bool blocked = aiFighter.IsBlocking;
                    aiState.TakeDamage(damage, blocked, settings.blockDamageReduction);
                    playerState.DrainStamina(settings.punchStaminaCost);
                    playerPunchProcessed = true;

                    aiFighter.FlashHit();

                    if (combatHUD != null)
                        combatHUD.OnHit("PLAYER", punchDetector.LastPunchType, damage, blocked, aiTorso);

                    Debug.Log($"[Boxing] Player {punchDetector.LastPunchType} → AI! " +
                              $"Damage: {damage:F0}{(blocked ? " (BLOCKED)" : "")} " +
                              $"| AI Health: {aiState.health:F0}");
                }
            }
            if (!punchDetector.IsPunching)
                playerPunchProcessed = false;

            // --- AI → Player ---
            if (aiFighter.IsAttacking && !aiPunchProcessed)
            {
                Vector3 playerTorso = playerState.position + Vector3.up * 1.1f;
                float hitDist = Vector3.Distance(aiFighter.AttackHandPosition, playerTorso);

                if (hitDist < settings.hitRange || fighterDistance < settings.hitRange)
                {
                    float damage = settings.GetDamage(aiFighter.CurrentPunchType);
                    bool blocked = punchDetector.IsGuarding;
                    playerState.TakeDamage(damage, blocked, settings.blockDamageReduction);
                    aiState.DrainStamina(settings.punchStaminaCost);
                    aiPunchProcessed = true;

                    if (combatHUD != null)
                        combatHUD.OnHit("AI", aiFighter.CurrentPunchType, damage, blocked, playerTorso);

                    Debug.Log($"[Boxing] AI {aiFighter.CurrentPunchType} → Player! " +
                              $"Damage: {damage:F0}{(blocked ? " (BLOCKED)" : "")} " +
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

            if (matchTimer != null)
                matchTimer.StopTimer();
            if (combatHUD != null)
                combatHUD.ShowResult(result);

            Debug.Log($"[Boxing] MATCH OVER — {result}");
        }
    }
}
