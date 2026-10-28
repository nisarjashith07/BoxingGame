using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace BoxingAI
{
    /// <summary>
    /// Detects player punches and guard state from MediaPipe landmark data
    /// and/or keyboard/mouse inputs via the Input System.
    /// Computes velocities from packet capture timestamps (independent of Unity FPS),
    /// ignores duplicate/stale frames, and handles tracking loss cleanly.
    /// </summary>
    public class PunchDetector : MonoBehaviour
    {
        [Header("Settings")]
        [SerializeField] private CombatSettings settings;

        [Header("Player Tracking")]
        [SerializeField] private AvatarController avatarController;
        [SerializeField] private Transform playerTransform;

        // MediaPipe landmark indices
        private const int NOSE = 0;
        private const int LEFT_SHOULDER = 11;
        private const int RIGHT_SHOULDER = 12;
        private const int LEFT_WRIST = 15;
        private const int RIGHT_WRIST = 16;
        private const int LEFT_HIP = 23;
        private const int RIGHT_HIP = 24;

        private const float MIN_LANDMARK_VISIBILITY = 0.25f;
        private const double MAX_CAPTURE_GAP_SEC = 0.25;

        // Position tracking
        private Vector3 prevLeftWrist, prevRightWrist;
        private Vector3 currLeftWrist, currRightWrist;
        private Vector3 leftWristVelocity, rightWristVelocity;

        // Timers
        private float punchCooldownTimer;
        private float recoveryTimer;
        private float punchActiveTimer;
        private bool manualGuardActive;
        private FighterState playerFighterState;
        private BoxingEnvironment boxingEnv;

        // Packet tracking
        private bool hasPreviousFrame;
        private bool hasLeftWristHistory;
        private bool hasRightWristHistory;
        private int lastProcessedFrame = -1;
        private double lastProcessedTimestamp = -1.0;
        private Vector3 prevCenter;
        private Vector3 prevCenterLocal;

        // --- Public State ---

        public bool IsPunching { get; private set; }
        public BoxingAction LastPunchType { get; private set; }
        public float GuardLevel01 { get; private set; }
        public bool IsGuarding => GuardLevel01 > 0.6f;
        public bool HasActiveTracking => hasPreviousFrame;

        public Vector3 PlayerCenter => playerTransform != null ? playerTransform.position : (prevCenter != Vector3.zero ? prevCenter : transform.position);
        public Vector3 PlayerVelocity { get; private set; }
        public bool IsRecovering => recoveryTimer > 0f;
        public float LeftPunchSpeed => leftWristVelocity.magnitude;
        public float RightPunchSpeed => rightWristVelocity.magnitude;
        public float MaxPunchSpeed => Mathf.Max(leftWristVelocity.magnitude, rightWristVelocity.magnitude);

        public Vector3 LeftWristPosition => avatarController != null ? avatarController.LeftHandWorldPos : (currLeftWrist != Vector3.zero ? currLeftWrist : transform.position - transform.right * 0.3f + transform.forward * 0.5f);
        public Vector3 RightWristPosition => avatarController != null ? avatarController.RightHandWorldPos : (currRightWrist != Vector3.zero ? currRightWrist : transform.position + transform.right * 0.3f + transform.forward * 0.5f);

        private void Awake()
        {
            if (settings == null)
                settings = CombatSettings.CreateDefault();

            if (avatarController == null)
                avatarController = FindAnyObjectByType<AvatarController>();

            if (playerTransform == null && avatarController != null)
                playerTransform = avatarController.transform;
            else if (playerTransform == null)
                playerTransform = transform;
        }

        public void Initialize(CombatSettings combatSettings, AvatarController avatar, Transform player)
        {
            settings = combatSettings;
            avatarController = avatar;
            playerTransform = player;
        }

        public void Initialize(CombatSettings combatSettings)
        {
            settings = combatSettings;
        }

        public void SetFighterState(FighterState state)
        {
            playerFighterState = state;
        }

        /// <summary>
        /// Clears camera-derived velocity and guard state when pose tracking times out or drops out.
        /// Preserves manual keyboard guard if held.
        /// </summary>
        public void ClearPoseTracking()
        {
            hasPreviousFrame = false;
            hasLeftWristHistory = false;
            hasRightWristHistory = false;
            lastProcessedFrame = -1;
            lastProcessedTimestamp = -1.0;
            leftWristVelocity = Vector3.zero;
            rightWristVelocity = Vector3.zero;
            PlayerVelocity = Vector3.zero;
            prevCenter = Vector3.zero;
            prevCenterLocal = Vector3.zero;

            if (!manualGuardActive)
                GuardLevel01 = 0f;
        }

        public void ResetDetector()
        {
            punchCooldownTimer = 0f;
            recoveryTimer = 0f;
            punchActiveTimer = 0f;
            IsPunching = false;
            manualGuardActive = false;
            GuardLevel01 = 0f;
            ClearPoseTracking();
        }

        public void TickTimers(float deltaTime)
        {
            if (punchCooldownTimer > 0f) punchCooldownTimer -= deltaTime;
            if (recoveryTimer > 0f) recoveryTimer -= deltaTime;

            if (punchActiveTimer > 0f)
            {
                punchActiveTimer -= deltaTime;
                if (punchActiveTimer <= 0f)
                    IsPunching = false;
            }
            else
            {
                IsPunching = false;
            }
        }

        private void Update()
        {
            TickTimers(Time.deltaTime);

            if (boxingEnv == null)
                boxingEnv = FindAnyObjectByType<BoxingEnvironment>();

            if (boxingEnv != null && !boxingEnv.MatchActive)
                return;

            // Keyboard/Mouse check via Input System
            CheckManualInputs();
        }

        private void CheckManualInputs()
        {
            try
            {
#if ENABLE_INPUT_SYSTEM
                if (Keyboard.current != null)
                {
                    if (Keyboard.current.bKey.isPressed || Keyboard.current.spaceKey.isPressed)
                    {
                        GuardLevel01 = 1f;
                        manualGuardActive = true;
                    }
                    else if (manualGuardActive)
                    {
                        GuardLevel01 = 0f;
                        manualGuardActive = false;
                    }

                    if (!manualGuardActive)
                    {
                        if (Keyboard.current.jKey.wasPressedThisFrame)
                            TriggerManualPunch(BoxingAction.Jab);
                        else if (Keyboard.current.kKey.wasPressedThisFrame)
                            TriggerManualPunch(BoxingAction.Cross);
                        else if (Keyboard.current.hKey.wasPressedThisFrame)
                            TriggerManualPunch(BoxingAction.HookLeft);
                        else if (Keyboard.current.lKey.wasPressedThisFrame)
                            TriggerManualPunch(BoxingAction.HookRight);
                    }
                }

                if (Mouse.current != null && !manualGuardActive)
                {
                    if (Mouse.current.leftButton.wasPressedThisFrame)
                        TriggerManualPunch(BoxingAction.Jab);
                    else if (Mouse.current.rightButton.wasPressedThisFrame)
                        TriggerManualPunch(BoxingAction.Cross);
                }
#else
                if (Input.GetKey(KeyCode.Space) || Input.GetKey(KeyCode.B))
                {
                    GuardLevel01 = 1f;
                    manualGuardActive = true;
                }
                else if (manualGuardActive)
                {
                    GuardLevel01 = 0f;
                    manualGuardActive = false;
                }

                if (!manualGuardActive)
                {
                    if (Input.GetKeyDown(KeyCode.J) || Input.GetMouseButtonDown(0))
                        TriggerManualPunch(BoxingAction.Jab);
                    else if (Input.GetKeyDown(KeyCode.K) || Input.GetMouseButtonDown(1))
                        TriggerManualPunch(BoxingAction.Cross);
                    else if (Input.GetKeyDown(KeyCode.H))
                        TriggerManualPunch(BoxingAction.HookLeft);
                    else if (Input.GetKeyDown(KeyCode.L))
                        TriggerManualPunch(BoxingAction.HookRight);
                }
#endif
            }
            catch
            {
                // Fallback catch to prevent any input exception from halting execution
            }
        }

        public void TriggerManualPunch(BoxingAction punchType)
        {
            if (!BeginPunch(punchType))
                return;

            // Trigger visual punch on the avatar
            if (avatarController != null)
            {
                bool isLeft = punchType == BoxingAction.Jab || punchType == BoxingAction.HookLeft;
                bool isHook = punchType == BoxingAction.HookLeft || punchType == BoxingAction.HookRight;
                avatarController.TriggerManualPunch(isLeft, isHook);
            }

            Debug.Log($"[PunchDetector] Manual punch triggered: {punchType}");
        }

        private bool BeginPunch(BoxingAction punchType)
        {
            if (punchCooldownTimer > 0f || manualGuardActive || IsPunching)
                return false;

            if (playerFighterState != null && !playerFighterState.CanAttack)
                return false;

            LastPunchType = punchType;
            IsPunching = true;
            punchActiveTimer = 0.25f;
            punchCooldownTimer = settings != null ? settings.attackCooldown : 0.35f;
            recoveryTimer = settings != null ? settings.recoveryTime : 0.25f;
            GuardLevel01 = 0f;

            // Trigger visual punch extension on the avatar
            if (avatarController != null)
            {
                bool isLeft = punchType == BoxingAction.Jab || punchType == BoxingAction.HookLeft;
                bool isHook = punchType == BoxingAction.HookLeft || punchType == BoxingAction.HookRight;
                avatarController.TriggerManualPunch(isLeft, isHook);
            }

            if (playerFighterState != null && settings != null)
            {
                playerFighterState.DrainStamina(settings.punchStaminaCost);
                playerFighterState.attackCooldown = settings.attackCooldown;
                playerFighterState.isAttacking = true;
            }

            return true;
        }

        /// <summary>
        /// Processes a PoseData packet to detect punches and guard state.
        /// Deduplicates by frame ID and computes velocities from packet capture timestamps.
        /// </summary>
        public void ProcessPose(PoseData pose)
        {
            if (pose == null || pose.landmarks == null || pose.landmarks.Count != 33)
                return;

            // Ignore duplicate calls for the exact same camera frame
            if (pose.frame > 0 && pose.frame == lastProcessedFrame)
                return;

            // Detect tracker restart (frame counter reset)
            if (pose.frame > 0 && lastProcessedFrame > 0 && pose.frame < lastProcessedFrame)
            {
                hasPreviousFrame = false;
                hasLeftWristHistory = false;
                hasRightWristHistory = false;
                lastProcessedTimestamp = -1.0;
            }

            if (settings == null)
                settings = CombatSettings.CreateDefault();

            float dt;
            bool gapReset = false;
            if (pose.timestamp > 0.0 && lastProcessedTimestamp > 0.0)
            {
                double rawDt = pose.timestamp - lastProcessedTimestamp;
                if (rawDt <= 0.0001)
                    return; // Duplicate or out-of-order capture timestamp

                if (rawDt > MAX_CAPTURE_GAP_SEC)
                    gapReset = true; // Tracking dropout > 250ms: reset history to avoid teleport punch

                dt = Mathf.Clamp((float)rawDt, 0.004f, (float)MAX_CAPTURE_GAP_SEC);
            }
            else
            {
                dt = Mathf.Max(Time.deltaTime, 0.001f);
            }

            lastProcessedFrame = pose.frame;
            if (pose.timestamp > 0.0)
                lastProcessedTimestamp = pose.timestamp;

            if (gapReset)
            {
                hasPreviousFrame = false;
                hasLeftWristHistory = false;
                hasRightWristHistory = false;
                prevCenterLocal = Vector3.zero;
            }

            Vector3 origin = playerTransform != null ? playerTransform.position : transform.position;
            Quaternion rot = playerTransform != null ? playerTransform.rotation : transform.rotation;

            Landmark lmNose = pose.landmarks[NOSE];
            Landmark lmLS = pose.landmarks[LEFT_SHOULDER];
            Landmark lmRS = pose.landmarks[RIGHT_SHOULDER];
            Landmark lmLW = pose.landmarks[LEFT_WRIST];
            Landmark lmRW = pose.landmarks[RIGHT_WRIST];
            Landmark lmLH = pose.landmarks[LEFT_HIP];
            Landmark lmRH = pose.landmarks[RIGHT_HIP];

            bool noseOk = IsLandmarkUsable(lmNose);
            bool lsOk = IsLandmarkUsable(lmLS);
            bool rsOk = IsLandmarkUsable(lmRS);
            bool lwOk = IsLandmarkUsable(lmLW);
            bool rwOk = IsLandmarkUsable(lmRW);
            bool hipsOk = IsLandmarkUsable(lmLH) && IsLandmarkUsable(lmRH);

            Vector3 nose = noseOk ? origin + rot * LandmarkToLocal(lmNose) : Vector3.zero;
            Vector3 leftShoulder = lsOk ? origin + rot * LandmarkToLocal(lmLS) : Vector3.zero;
            Vector3 rightShoulder = rsOk ? origin + rot * LandmarkToLocal(lmRS) : Vector3.zero;
            Vector3 leftHip = hipsOk ? origin + rot * LandmarkToLocal(lmLH) : origin;
            Vector3 rightHip = hipsOk ? origin + rot * LandmarkToLocal(lmRH) : origin;

            if (Mathf.Abs(pose.body_vel_x) > 0.001f || Mathf.Abs(pose.body_vel_z) > 0.001f || Mathf.Abs(pose.body_vel_y) > 0.001f)
            {
                // True physical body velocity tracked by camera, rotated to avatar ring orientation
                Vector3 rawCamVel = new Vector3(pose.body_vel_x, pose.body_vel_y, pose.body_vel_z);
                PlayerVelocity = rot * rawCamVel;
                if (hipsOk)
                {
                    Vector3 centerLocal = (LandmarkToLocal(lmLH) + LandmarkToLocal(lmRH)) * 0.5f;
                    prevCenter = origin + rot * centerLocal;
                }
                hasPreviousFrame = true;
            }
            else if (hipsOk)
            {
                Vector3 centerLocal = (LandmarkToLocal(lmLH) + LandmarkToLocal(lmRH)) * 0.5f;
                Vector3 center = origin + rot * centerLocal;
                Vector3 localVel = hasPreviousFrame ? (centerLocal - prevCenterLocal) / dt : Vector3.zero;
                PlayerVelocity = rot * localVel;
                prevCenterLocal = centerLocal;
                prevCenter = center;
                hasPreviousFrame = true;
            }
            else
            {
                PlayerVelocity = Vector3.zero;
                prevCenterLocal = Vector3.zero;
            }

            if (lwOk)
            {
                Vector3 leftWrist = origin + rot * LandmarkToLocal(lmLW);
                prevLeftWrist = currLeftWrist;
                currLeftWrist = leftWrist;
                leftWristVelocity = hasLeftWristHistory ? (currLeftWrist - prevLeftWrist) / dt : Vector3.zero;
                hasLeftWristHistory = true;
            }
            else
            {
                hasLeftWristHistory = false;
                leftWristVelocity = Vector3.zero;
            }

            if (rwOk)
            {
                Vector3 rightWrist = origin + rot * LandmarkToLocal(lmRW);
                prevRightWrist = currRightWrist;
                currRightWrist = rightWrist;
                rightWristVelocity = hasRightWristHistory ? (currRightWrist - prevRightWrist) / dt : Vector3.zero;
                hasRightWristHistory = true;
            }
            else
            {
                hasRightWristHistory = false;
                rightWristVelocity = Vector3.zero;
            }

            // 1. Check punches if cooldown expired
            bool punchTriggered = false;
            if (punchCooldownTimer <= 0f)
            {
                // Check if Python tracker already flagged a high-confidence punch
                if (!string.IsNullOrEmpty(pose.punch_type) && pose.punch_type != "none" && pose.punch_type != "GUARD STANCE")
                {
                    if (pose.punch_type.Contains("LEFT") || pose.punch_type.Contains("JAB"))
                    {
                        punchTriggered = BeginPunch(BoxingAction.Jab);
                    }
                    else if (pose.punch_type.Contains("RIGHT") || pose.punch_type.Contains("CROSS"))
                    {
                        punchTriggered = BeginPunch(BoxingAction.Cross);
                    }
                }

                // Check left hand (Jab or HookLeft)
                if (!punchTriggered && lwOk && lsOk && hasLeftWristHistory)
                {
                    punchTriggered = TryDetectPunch(
                        currLeftWrist, leftShoulder, leftHip, leftWristVelocity,
                        BoxingAction.Jab, BoxingAction.HookLeft, pose.left_punch_speed);
                }

                // Check right hand (Cross or HookRight) if left did not punch
                if (!punchTriggered && rwOk && rsOk && hasRightWristHistory)
                {
                    punchTriggered = TryDetectPunch(
                        currRightWrist, rightShoulder, rightHip, rightWristVelocity,
                        BoxingAction.Cross, BoxingAction.HookRight, pose.right_punch_speed);
                }
            }

            // Reset punch flag only if punch timer is not keeping it active
            if (punchActiveTimer <= 0f && !punchTriggered)
                IsPunching = false;

            // 2. Guard Detection (wrists protecting chin/face), active only when not punching
            if (manualGuardActive)
            {
                GuardLevel01 = 1f;
            }
            else if (noseOk && !IsPunching && !punchTriggered)
            {
                float leftGuardDist = lwOk ? Vector3.Distance(currLeftWrist, nose) : 1f;
                float rightGuardDist = rwOk ? Vector3.Distance(currRightWrist, nose) : 1f;
                float guardRadius = Mathf.Max(settings.guardDistanceThreshold * 1.75f, 0.35f);
                bool leftGuarding = leftGuardDist < guardRadius;
                bool rightGuarding = rightGuardDist < guardRadius;

                if (leftGuarding && rightGuarding)
                    GuardLevel01 = 1f;
                else if (leftGuarding || rightGuarding)
                    GuardLevel01 = 0.5f;
                else
                    GuardLevel01 = 0f;
            }
            else
            {
                GuardLevel01 = 0f;
            }
        }

        private bool TryDetectPunch(
            Vector3 wrist, Vector3 shoulder, Vector3 hip,
            Vector3 wristVelocity,
            BoxingAction straightType, BoxingAction hookType,
            float pySpeed = 0f)
        {
            float speed = Mathf.Max(wristVelocity.magnitude, pySpeed);
            float extension = Vector3.Distance(wrist, shoulder);
            float restDist = Mathf.Max(Vector3.Distance(shoulder, hip) * 0.5f, 0.18f);

            float speedThresh = Mathf.Min(settings.punchVelocityThreshold, 0.70f);
            float extThresh = restDist * Mathf.Min(settings.armExtensionMultiplier, 1.10f);

            if (speed > speedThresh && extension > extThresh)
            {
                Vector3 playerRight = playerTransform != null ? playerTransform.right : transform.right;
                float lateralSpeed = Mathf.Abs(Vector3.Dot(wristVelocity, playerRight));
                float lateralRatio = lateralSpeed / Mathf.Max(speed, 0.01f);
                BoxingAction detectedAction = lateralRatio > 0.6f ? hookType : straightType;

                if (BeginPunch(detectedAction))
                {
                    Debug.Log($"[PunchDetector] MediaPipe detected {LastPunchType} (Speed: {speed:F2} m/s, Ext: {extension:F2}m)");
                    return true;
                }
            }

            return false;
        }

        private static bool IsLandmarkUsable(Landmark lm)
        {
            if (lm == null) return false;
            if (lm.x == 0f && lm.y == 0f && lm.z == 0f && lm.visibility <= 0f) return false;
            if (lm.visibility > 0f && lm.visibility < MIN_LANDMARK_VISIBILITY) return false;
            return true;
        }

        private Vector3 LandmarkToLocal(Landmark lm)
        {
            // Mirror X, Invert Y, Forward Z
            return new Vector3(-lm.x, -lm.y, -lm.z);
        }
    }
}
