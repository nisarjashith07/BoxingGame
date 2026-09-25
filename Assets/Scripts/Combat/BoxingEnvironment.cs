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
        [SerializeField] private bool enablePythonRLBridge = true;

        // Fighter states
        private FighterState playerState = new FighterState();
        private FighterState aiState = new FighterState();

        // Hit detection deduplication
        private bool playerPunchProcessed;
        private bool aiPunchProcessed;
        private BoxingAction lastAIAction = BoxingAction.Idle;

        private bool matchActive;
        private RLTrainingBridge trainingBridge;
        private Vector3 initialPlayerPosition;
        private Quaternion initialPlayerRotation;
        private Vector3 initialAIPosition;
        private Quaternion initialAIRotation;
        private bool initialTransformsCached;
        private bool pythonTrainingSession;

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

            if (enablePythonRLBridge && trainingBridge == null)
                trainingBridge = GetComponent<RLTrainingBridge>() ?? gameObject.AddComponent<RLTrainingBridge>();
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

            if (playerTransform != null)
            {
                initialPlayerPosition = playerTransform.position;
                initialPlayerRotation = playerTransform.rotation;
            }
            if (aiFighter != null)
            {
                initialAIPosition = aiFighter.transform.position;
                initialAIRotation = aiFighter.transform.rotation;
            }
            initialTransformsCached = true;

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
            if (initialTransformsCached)
            {
                if (playerTransform != null)
                {
                    CharacterController playerController = playerTransform.GetComponent<CharacterController>();
                    if (playerController != null && playerController.enabled) playerController.enabled = false;
                    playerTransform.SetPositionAndRotation(initialPlayerPosition, initialPlayerRotation);
                    if (playerController != null) playerController.enabled = true;
                }
                if (aiFighter != null)
                    aiFighter.ResetForMatch(initialAIPosition, initialAIRotation);
            }

            playerState.Reset();
            aiState.Reset();
            if (playerTransform != null) playerState.ResetPosition(playerTransform.position);
            if (aiFighter != null) aiState.ResetPosition(aiFighter.transform.position);
            if (punchDetector != null) punchDetector.ResetForMatch();

            if (aiOpponent != null)
                aiOpponent.ResetBrain();

            if (matchTimer != null)
                matchTimer.ResetTimer();

            playerPunchProcessed = false;
            aiPunchProcessed = false;
            matchActive = true;
            lastAIAction = BoxingAction.Idle;

            Debug.Log($"[Boxing] Match started — Difficulty: {difficulty}");
        }

        // --- Game Loop ---

        private void Update()
        {
            bool isTrainingStep = false;
            BoxingAction externalAction = BoxingAction.Idle;
            float playerHealthBeforeStep = playerState.health;
            float aiHealthBeforeStep = aiState.health;

            if (trainingBridge != null && trainingBridge.TryReceive(out RLTrainingBridge.TrainingCommand command))
            {
                if (command.command == "reset")
                {
                    pythonTrainingSession = true;
                    StartMatch();
                    UpdatePlayerState();
                    UpdateAIState();
                    trainingBridge.ReplyReset(BuildTrainingObservation());
                    return;
                }

                if (command.command == "step")
                {
                    if (command.action < 0 || command.action > (int)BoxingAction.DodgeRight)
                    {
                        trainingBridge.ReplyError("Action must be an integer from 0 through 11.");
                        return;
                    }
                    isTrainingStep = true;
                    externalAction = (BoxingAction)command.action;
                    playerHealthBeforeStep = playerState.health;
                    aiHealthBeforeStep = aiState.health;
                }
            }

            if (!matchActive)
            {
                if (isTrainingStep)
                    ReplyTerminalTrainingStep(true, playerHealthBeforeStep, aiHealthBeforeStep,
                        playerState.health >= aiState.health ? "player" : "opponent");
                return;
            }

            // --- Win/Loss Check ---
            if (!playerState.IsAlive)
            {
                EndMatch("AI WINS — KO!");
                ReplyTerminalTrainingStep(isTrainingStep, playerHealthBeforeStep, aiHealthBeforeStep, "opponent");
                return;
            }
            if (!aiState.IsAlive)
            {
                EndMatch("PLAYER WINS — KO!");
                ReplyTerminalTrainingStep(isTrainingStep, playerHealthBeforeStep, aiHealthBeforeStep, "player");
                return;
            }
            if (matchTimer != null && matchTimer.TimeRemaining <= 0f)
            {
                string winner = playerState.health >= aiState.health
                    ? "PLAYER WINS" : "AI WINS";
                EndMatch($"{winner} — Decision!");
                ReplyTerminalTrainingStep(isTrainingStep, playerHealthBeforeStep, aiHealthBeforeStep,
                    playerState.health >= aiState.health ? "player" : "opponent");
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
            if (aiOpponent != null && aiFighter != null && punchDetector != null
                && (!pythonTrainingSession || isTrainingStep))
            {
                float roundTime01 = matchTimer != null ? matchTimer.NormalizedTimeRemaining : 1f;
                BoxingObservation observation = ObservationBuilder.Build(
                    aiState, playerState, punchDetector, aiFighter, roundTime01);

                BoxingAction action;
                if (isTrainingStep)
                {
                    aiOpponent.SetExternalAction(observation, externalAction);
                    action = externalAction;
                }
                else
                {
                    action = aiOpponent.Evaluate(observation);
                }

                bool punchAction = action >= BoxingAction.Jab && action <= BoxingAction.HookRight;
                bool dodgeAction = action == BoxingAction.DodgeLeft || action == BoxingAction.DodgeRight;
                bool canStartPunch = aiFighter.CanStartPunch;
                if (punchAction && (!aiState.CanAttack || !canStartPunch)) action = BoxingAction.Idle;
                if (dodgeAction && !aiState.CanDodge) action = BoxingAction.Idle;
                if (action == BoxingAction.Block && !aiState.CanBlock) action = BoxingAction.Idle;

                // --- 4. Execute AI Action ---
                aiFighter.ExecuteAction(action);

                // Charge the same stamina/cooldown cost whenever a punch or dodge begins.
                if (settings != null && punchAction && action != BoxingAction.Idle && canStartPunch)
                {
                    aiState.DrainStamina(settings.punchStaminaCost);
                    aiState.attackCooldown = settings.attackCooldown;
                }
                else if (settings != null && dodgeAction && action != BoxingAction.Idle)
                {
                    aiState.DrainStamina(settings.dodgeStaminaCost);
                    aiState.dodgeCooldown = settings.dodgeCooldown;
                }
                lastAIAction = action;
            }

            // --- 5. Hit Detection ---
            ProcessHits();
            UpdatePlayerState();
            UpdateAIState();

            if (!playerState.IsAlive)
            {
                EndMatch("AI WINS — KO!");
                ReplyTerminalTrainingStep(isTrainingStep, playerHealthBeforeStep, aiHealthBeforeStep, "opponent");
                return;
            }
            if (!aiState.IsAlive)
            {
                EndMatch("PLAYER WINS — KO!");
                ReplyTerminalTrainingStep(isTrainingStep, playerHealthBeforeStep, aiHealthBeforeStep, "player");
                return;
            }

            if (isTrainingStep)
            {
                float reward = (playerHealthBeforeStep - playerState.health) / 100f
                    - (aiHealthBeforeStep - aiState.health) / 100f;
                trainingBridge.ReplyStep(BuildTrainingObservation(), reward, false, false, string.Empty);
            }

            // --- 6. Update HUD ---
            if (combatHUD != null)
                combatHUD.UpdateHUD(playerState, aiState);
        }

        private BoxingObservation BuildTrainingObservation()
        {
            float roundTime01 = matchTimer != null ? matchTimer.NormalizedTimeRemaining : 1f;
            return ObservationBuilder.Build(aiState, playerState, punchDetector, aiFighter, roundTime01);
        }

        private void ReplyTerminalTrainingStep(bool isTrainingStep, float playerHealthBeforeStep,
            float aiHealthBeforeStep, string winner)
        {
            if (!isTrainingStep || trainingBridge == null) return;
            float reward = (playerHealthBeforeStep - playerState.health) / 100f
                - (aiHealthBeforeStep - aiState.health) / 100f;
            reward += winner == "opponent" ? 1f : winner == "player" ? -1f : 0f;
            trainingBridge.ReplyStep(BuildTrainingObservation(), reward, true, false, winner);
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
