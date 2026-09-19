using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace BoxingAI
{
    /// <summary>
    /// Detects player punches and guard state from MediaPipe landmark data
    /// and/or keyboard/mouse inputs via the new Input System.
    /// Drives visual punches on AvatarController and computes accurate 3D world hit coordinates.
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

        // Position tracking
        private Vector3 prevLeftWrist, prevRightWrist;
        private Vector3 currLeftWrist, currRightWrist;
        private Vector3 leftWristVelocity, rightWristVelocity;

        // Timers
        private float punchCooldownTimer;
        private float recoveryTimer;
        private float punchActiveTimer;
        private bool manualGuardActive;

        private bool hasPreviousFrame;
        private Vector3 prevCenter;

        // --- Public State ---

        public bool IsPunching { get; private set; }
        public BoxingAction LastPunchType { get; private set; }
        public float GuardLevel01 { get; private set; }
        public bool IsGuarding => GuardLevel01 > 0.6f;

        public Vector3 PlayerCenter => playerTransform != null ? playerTransform.position : (prevCenter != Vector3.zero ? prevCenter : transform.position);
        public Vector3 PlayerVelocity { get; private set; }
        public bool IsRecovering => recoveryTimer > 0f;

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

        private void Update()
        {
            // Cooldowns and active punch window
            if (punchCooldownTimer > 0f) punchCooldownTimer -= Time.deltaTime;
            if (recoveryTimer > 0f) recoveryTimer -= Time.deltaTime;

            if (punchActiveTimer > 0f)
            {
                punchActiveTimer -= Time.deltaTime;
                if (punchActiveTimer <= 0f)
                    IsPunching = false;
            }
            else
            {
                IsPunching = false;
            }

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
                    if (Keyboard.current.jKey.wasPressedThisFrame)
                        TriggerManualPunch(BoxingAction.Jab);
                    else if (Keyboard.current.kKey.wasPressedThisFrame)
                        TriggerManualPunch(BoxingAction.Cross);
                    else if (Keyboard.current.hKey.wasPressedThisFrame)
                        TriggerManualPunch(BoxingAction.HookLeft);
                    else if (Keyboard.current.lKey.wasPressedThisFrame)
                        TriggerManualPunch(BoxingAction.HookRight);

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
                }

                if (Mouse.current != null)
                {
                    if (Mouse.current.leftButton.wasPressedThisFrame)
                        TriggerManualPunch(BoxingAction.Jab);
                    else if (Mouse.current.rightButton.wasPressedThisFrame)
                        TriggerManualPunch(BoxingAction.Cross);
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
            if (punchCooldownTimer > 0f) return;

            LastPunchType = punchType;
            IsPunching = true;
            punchActiveTimer = 0.20f;
            punchCooldownTimer = settings != null ? settings.attackCooldown : 0.35f;
            recoveryTimer = settings != null ? settings.recoveryTime : 0.25f;

            // Trigger visual punch on the avatar
            if (avatarController != null)
            {
                bool isLeft = punchType == BoxingAction.Jab || punchType == BoxingAction.HookLeft;
                avatarController.TriggerManualPunch(isLeft);
            }

            Debug.Log($"[PunchDetector] Manual punch triggered: {punchType}");
        }

        /// <summary>
        /// Call each frame with the latest PoseData from PoseManager.
        /// Processes landmarks to detect punches and guard state.
        /// </summary>
        public void ProcessPose(PoseData pose)
        {
            if (pose == null || pose.landmarks == null || pose.landmarks.Count != 33)
                return;

            if (settings == null)
                settings = CombatSettings.CreateDefault();

            // Convert key landmarks to world-relative positions
            Vector3 origin = playerTransform != null ? playerTransform.position : transform.position;
            Quaternion rot = playerTransform != null ? playerTransform.rotation : transform.rotation;

            Vector3 nose = origin + rot * LandmarkToLocal(pose.landmarks[NOSE]);
            Vector3 leftShoulder = origin + rot * LandmarkToLocal(pose.landmarks[LEFT_SHOULDER]);
            Vector3 rightShoulder = origin + rot * LandmarkToLocal(pose.landmarks[RIGHT_SHOULDER]);
            Vector3 leftWrist = origin + rot * LandmarkToLocal(pose.landmarks[LEFT_WRIST]);
            Vector3 rightWrist = origin + rot * LandmarkToLocal(pose.landmarks[RIGHT_WRIST]);
            Vector3 leftHip = origin + rot * LandmarkToLocal(pose.landmarks[LEFT_HIP]);
            Vector3 rightHip = origin + rot * LandmarkToLocal(pose.landmarks[RIGHT_HIP]);

            // Update wrist history
            prevLeftWrist = currLeftWrist;
            prevRightWrist = currRightWrist;
            currLeftWrist = leftWrist;
            currRightWrist = rightWrist;

            // Player center from hip midpoint
            Vector3 center = (leftHip + rightHip) * 0.5f;
            PlayerVelocity = hasPreviousFrame
                ? (center - prevCenter) / Mathf.Max(Time.deltaTime, 0.001f)
                : Vector3.zero;
            prevCenter = center;

            if (!hasPreviousFrame)
            {
                hasPreviousFrame = true;
                return;
            }

            // Calculate wrist velocities
            float dt = Mathf.Max(Time.deltaTime, 0.001f);
            leftWristVelocity = (currLeftWrist - prevLeftWrist) / dt;
            rightWristVelocity = (currRightWrist - prevRightWrist) / dt;

            // Reset single-frame punch flag unless manual punch is triggering
            IsPunching = false;

            // Guard Detection (wrists near nose)
            float leftGuardDist = Vector3.Distance(leftWrist, nose);
            float rightGuardDist = Vector3.Distance(rightWrist, nose);
            float avgGuardDist = (leftGuardDist + rightGuardDist) * 0.5f;
            GuardLevel01 = Mathf.Clamp01(1f - (avgGuardDist / Mathf.Max(settings.guardDistanceThreshold * 4f, 0.1f)));

            if (punchCooldownTimer > 0f || IsGuarding)
                return;

            // Check left hand (Jab or HookLeft)
            if (TryDetectPunch(
                    leftWrist, leftShoulder, leftHip, leftWristVelocity,
                    BoxingAction.Jab, BoxingAction.HookLeft))
                return;

            // Check right hand (Cross or HookRight)
            TryDetectPunch(
                rightWrist, rightShoulder, rightHip, rightWristVelocity,
                BoxingAction.Cross, BoxingAction.HookRight);
        }

        private bool TryDetectPunch(
            Vector3 wrist, Vector3 shoulder, Vector3 hip,
            Vector3 wristVelocity,
            BoxingAction straightType, BoxingAction hookType)
        {
            float speed = wristVelocity.magnitude;
            float extension = Vector3.Distance(wrist, shoulder);
            float restDist = Vector3.Distance(shoulder, hip) * 0.5f;

            if (speed > settings.punchVelocityThreshold &&
                extension > restDist * settings.armExtensionMultiplier)
            {
                float lateralRatio = Mathf.Abs(wristVelocity.x) / Mathf.Max(speed, 0.01f);
                LastPunchType = lateralRatio > 0.6f ? hookType : straightType;
                IsPunching = true;
                punchActiveTimer = 0.20f;
                punchCooldownTimer = settings.attackCooldown;
                recoveryTimer = settings.recoveryTime;

                Debug.Log($"[PunchDetector] MediaPipe detected {LastPunchType} (Speed: {speed:F2}, Ext: {extension:F2})");
                return true;
            }

            return false;
        }

        private Vector3 LandmarkToLocal(Landmark lm)
        {
            // Mirror X, Invert Y, Forward Z
            return new Vector3(-lm.x, -lm.y, -lm.z) * 1.5f;
        }
    }
}
