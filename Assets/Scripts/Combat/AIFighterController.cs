using UnityEngine;

namespace BoxingAI
{
    /// <summary>
    /// Controls the AI opponent's humanoid character.
    /// Translates BoxingAction commands into movement (CharacterController + Animator locomotion),
    /// attacks (IK-driven punches with torso lunges), blocking (tight guard pose), and dodging.
    /// </summary>
    [RequireComponent(typeof(Animator))]
    [RequireComponent(typeof(CharacterController))]
    public class AIFighterController : MonoBehaviour
    {
        [Header("Settings")]
        [SerializeField] private CombatSettings settings;

        private Animator animator;
        private CharacterController characterController;

        // IK targets (smoothed each frame)
        private Vector3 leftHandTarget, rightHandTarget;
        private Quaternion leftHandRot, rightHandRot;
        private Vector3 leftElbowHint, rightElbowHint;
        private bool ikInitialized;

        // Locomotion animation smoothing
        private float currentMoveSpeed;
        private Vector3 desiredVelocity;

        // Punch state machine
        private enum PunchPhase { None, Windup, Extend, Hold, Retract }
        private PunchPhase punchPhase = PunchPhase.None;
        private float punchTimer;
        private bool punchIsLeftHand;
        private BoxingAction currentPunchType;

        // Dodge state
        private bool isDodging;
        private Vector3 dodgeDirection;
        private float dodgeTimer;

        // Block state
        private bool isBlockingState;

        // Target reference
        private Transform playerTarget;

        // Hit flash visual feedback
        private Renderer[] cachedRenderers;
        private float flashTimer;
        private static readonly Color AI_BASE_COLOR = new Color(0.9f, 0.25f, 0.2f);

        // --- Public API ---

        public bool IsAttacking => punchPhase == PunchPhase.Extend || punchPhase == PunchPhase.Hold;
        public bool IsRecovering => punchPhase == PunchPhase.Retract;
        public bool IsBlocking => isBlockingState;

        public Vector3 AttackHandPosition => punchIsLeftHand ? leftHandTarget : rightHandTarget;
        public BoxingAction CurrentPunchType => currentPunchType;
        public bool CanStartPunch => punchPhase == PunchPhase.None;

        public void SetPlayerTarget(Transform target)
        {
            playerTarget = target;
        }

        public void ResetForMatch(Vector3 position, Quaternion rotation)
        {
            if (characterController != null && characterController.enabled)
                characterController.enabled = false;
            transform.SetPositionAndRotation(position, rotation);
            if (characterController != null)
                characterController.enabled = true;
            desiredVelocity = Vector3.zero;
            currentMoveSpeed = 0f;
            punchPhase = PunchPhase.None;
            punchTimer = 0f;
            isDodging = false;
            dodgeTimer = 0f;
            isBlockingState = false;
            ikInitialized = false;
            if (animator != null)
            {
                animator.Rebind();
                animator.Update(0f);
            }
        }

        private void Awake()
        {
            if (settings == null)
                settings = CombatSettings.CreateDefault();

            animator = GetComponent<Animator>();
            characterController = GetComponent<CharacterController>();
            cachedRenderers = GetComponentsInChildren<Renderer>();
        }

        public void Initialize(CombatSettings combatSettings)
        {
            settings = combatSettings;
        }

        public void FlashHit()
        {
            flashTimer = 0.12f;
            if (cachedRenderers == null || cachedRenderers.Length == 0)
                cachedRenderers = GetComponentsInChildren<Renderer>();

            foreach (var r in cachedRenderers)
            {
                if (r != null && r.material != null)
                {
                    if (r.material.HasProperty("_BaseColor"))
                        r.material.SetColor("_BaseColor", Color.white);
                    else if (r.material.HasProperty("_Color"))
                        r.material.SetColor("_Color", Color.white);
                }
            }
        }

        // --- Action Execution ---

        public void ExecuteAction(BoxingAction action)
        {
            isBlockingState = false;

            // Don't interrupt active punch extension
            if (punchPhase == PunchPhase.Windup || punchPhase == PunchPhase.Extend || punchPhase == PunchPhase.Hold)
                return;

            switch (action)
            {
                // Movement: controlled, measured boxing shuffle
                case BoxingAction.MoveForward:
                    desiredVelocity = transform.forward * settings.aiMoveSpeed;
                    break;
                case BoxingAction.MoveBackward:
                    desiredVelocity = -transform.forward * (settings.aiMoveSpeed * 0.75f);
                    break;
                case BoxingAction.StrafeLeft:
                    desiredVelocity = -transform.right * settings.aiStrafeSpeed;
                    break;
                case BoxingAction.StrafeRight:
                    desiredVelocity = transform.right * settings.aiStrafeSpeed;
                    break;

                // Attacks
                case BoxingAction.Jab:
                    StartPunch(action, true);
                    break;
                case BoxingAction.Cross:
                    StartPunch(action, false);
                    break;
                case BoxingAction.HookLeft:
                    StartPunch(action, true);
                    break;
                case BoxingAction.HookRight:
                    StartPunch(action, false);
                    break;

                // Defense
                case BoxingAction.Block:
                    isBlockingState = true;
                    desiredVelocity = Vector3.zero;
                    break;
                case BoxingAction.DodgeLeft:
                    StartDodge(-transform.right);
                    break;
                case BoxingAction.DodgeRight:
                    StartDodge(transform.right);
                    break;

                case BoxingAction.Idle:
                default:
                    desiredVelocity = Vector3.zero;
                    break;
            }
        }

        private void StartDodge(Vector3 direction)
        {
            if (isDodging) return;
            isDodging = true;
            dodgeDirection = direction.normalized;
            dodgeTimer = settings.aiDodgeDuration;
        }

        // --- Punch State Machine ---

        private void StartPunch(BoxingAction type, bool leftHand)
        {
            if (punchPhase != PunchPhase.None) return;
            punchPhase = PunchPhase.Windup;
            punchTimer = 0.08f;
            punchIsLeftHand = leftHand;
            currentPunchType = type;

            // Step slightly forward during punch to ensure reach
            if (playerTarget != null && Vector3.Distance(transform.position, playerTarget.position) > 1.25f)
            {
                desiredVelocity = transform.forward * 1.2f;
            }
        }

        private void UpdatePunchPhase()
        {
            if (punchPhase == PunchPhase.None) return;

            punchTimer -= Time.deltaTime;
            if (punchTimer <= 0f)
            {
                switch (punchPhase)
                {
                    case PunchPhase.Windup:
                        punchPhase = PunchPhase.Extend;
                        punchTimer = 0.12f;
                        break;
                    case PunchPhase.Extend:
                        punchPhase = PunchPhase.Hold;
                        punchTimer = 0.06f;
                        break;
                    case PunchPhase.Hold:
                        punchPhase = PunchPhase.Retract;
                        punchTimer = 0.12f;
                        break;
                    case PunchPhase.Retract:
                        punchPhase = PunchPhase.None;
                        break;
                }
            }
        }

        // --- Update ---

        private void Update()
        {
            // Reset flash color after hit impact
            if (flashTimer > 0f)
            {
                flashTimer -= Time.deltaTime;
                if (flashTimer <= 0f && cachedRenderers != null)
                {
                    foreach (var r in cachedRenderers)
                    {
                        if (r != null && r.material != null)
                        {
                            if (r.material.HasProperty("_BaseColor"))
                                r.material.SetColor("_BaseColor", AI_BASE_COLOR);
                            else if (r.material.HasProperty("_Color"))
                                r.material.SetColor("_Color", AI_BASE_COLOR);
                        }
                    }
                }
            }

            // Face the player smoothly
            if (playerTarget != null)
            {
                Vector3 lookDir = playerTarget.position - transform.position;
                lookDir.y = 0f;
                if (lookDir.sqrMagnitude > 0.01f)
                {
                    Quaternion targetRot = Quaternion.LookRotation(lookDir);
                    transform.rotation = Quaternion.Slerp(
                        transform.rotation, targetRot, Time.deltaTime * settings.aiRotationSpeed);
                }
            }

            // Prevent pushing forward when already in punching pocket range
            if (playerTarget != null && Vector3.Distance(transform.position, playerTarget.position) < 1.35f)
            {
                if (Vector3.Dot(desiredVelocity, transform.forward) > 0f)
                    desiredVelocity = Vector3.zero;
            }

            // Movement handling (slow controlled boxing shuffle, no running)
            Vector3 moveVec = desiredVelocity;
            if (isDodging)
            {
                dodgeTimer -= Time.deltaTime;
                float dodgeSpeed = settings.aiDodgeDistance / Mathf.Max(settings.aiDodgeDuration, 0.01f);
                moveVec = dodgeDirection * dodgeSpeed;
                if (dodgeTimer <= 0f)
                    isDodging = false;
            }

            // Move character controller
            if (characterController != null && characterController.enabled)
            {
                if (!characterController.isGrounded)
                    moveVec.y -= 9.81f;

                characterController.Move(moveVec * Time.deltaTime);
            }

            // Drive walking locomotion in Animator (clamped so it NEVER triggers running animation)
            if (animator != null)
            {
                float targetSpeed = Mathf.Clamp(moveVec.magnitude, 0f, 1.2f);
                currentMoveSpeed = Mathf.Lerp(currentMoveSpeed, targetSpeed, Time.deltaTime * 8f);
                animator.SetFloat("Speed", currentMoveSpeed);
                animator.SetFloat("MotionSpeed", 1f);
            }

            // Progress punch phases
            UpdatePunchPhase();

            // Calculate IK arm targets
            UpdateIKTargets();

            // Decay desired velocity
            desiredVelocity = Vector3.Lerp(desiredVelocity, Vector3.zero, Time.deltaTime * 6f);
        }

        // --- IK ---

        private void UpdateIKTargets()
        {
            if (animator == null) return;

            Transform head = animator.GetBoneTransform(HumanBodyBones.Head);
            Transform lShoulder = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm) ?? animator.GetBoneTransform(HumanBodyBones.LeftShoulder);
            Transform rShoulder = animator.GetBoneTransform(HumanBodyBones.RightUpperArm) ?? animator.GetBoneTransform(HumanBodyBones.RightShoulder);

            Vector3 headPos = head != null ? head.position : transform.position + Vector3.up * 1.55f;
            Vector3 lShoulderPos = lShoulder != null ? lShoulder.position : transform.position - transform.right * 0.25f + Vector3.up * 1.4f;
            Vector3 rShoulderPos = rShoulder != null ? rShoulder.position : transform.position + transform.right * 0.25f + Vector3.up * 1.4f;

            // Default boxing guard: fists held high guarding chin and face
            Vector3 guardLeft = headPos + transform.forward * 0.32f - transform.right * 0.16f + Vector3.down * 0.10f;
            Vector3 guardRight = headPos + transform.forward * 0.32f + transform.right * 0.16f + Vector3.down * 0.10f;

            Vector3 targetLeft = guardLeft;
            Vector3 targetRight = guardRight;
            Quaternion targetLeftRot = Quaternion.LookRotation(transform.forward, Vector3.up);
            Quaternion targetRightRot = Quaternion.LookRotation(transform.forward, Vector3.up);

            // Tight defensive block
            if (isBlockingState)
            {
                targetLeft = headPos + transform.forward * 0.22f - transform.right * 0.08f;
                targetRight = headPos + transform.forward * 0.22f + transform.right * 0.08f;
            }

            // Punch extension
            if (punchPhase != PunchPhase.None)
            {
                float t = 0f;
                switch (punchPhase)
                {
                    case PunchPhase.Windup:
                        t = 0f;
                        break;
                    case PunchPhase.Extend:
                        t = 1f - (punchTimer / 0.12f);
                        break;
                    case PunchPhase.Hold:
                        t = 1f;
                        break;
                    case PunchPhase.Retract:
                        t = punchTimer / 0.12f;
                        break;
                }

                Vector3 targetHead = playerTarget != null
                    ? playerTarget.position + Vector3.up * 1.35f
                    : transform.position + transform.forward * 1.8f + Vector3.up * 1.35f;

                Transform activeShoulder = punchIsLeftHand ? lShoulder : rShoulder;
                Vector3 shoulderOrigin = activeShoulder != null ? activeShoulder.position : (punchIsLeftHand ? lShoulderPos : rShoulderPos);
                Vector3 punchDir = (targetHead - shoulderOrigin).normalized;

                float reach = 1.05f;
                Vector3 punchTarget = shoulderOrigin + punchDir * reach;
                Quaternion punchRot = Quaternion.LookRotation(punchDir, Vector3.up);

                if (punchIsLeftHand)
                {
                    targetLeft = Vector3.Lerp(guardLeft, punchTarget, t);
                    targetLeftRot = Quaternion.Slerp(targetLeftRot, punchRot, t);
                }
                else
                {
                    targetRight = Vector3.Lerp(guardRight, punchTarget, t);
                    targetRightRot = Quaternion.Slerp(targetRightRot, punchRot, t);
                }
            }

            // Smooth IK targets
            float smoothSpeed = 25f;
            if (!ikInitialized)
            {
                leftHandTarget = targetLeft;
                rightHandTarget = targetRight;
                leftHandRot = targetLeftRot;
                rightHandRot = targetRightRot;
                ikInitialized = true;
            }
            else
            {
                leftHandTarget = Vector3.Lerp(leftHandTarget, targetLeft, Time.deltaTime * smoothSpeed);
                rightHandTarget = Vector3.Lerp(rightHandTarget, targetRight, Time.deltaTime * smoothSpeed);
                leftHandRot = Quaternion.Slerp(leftHandRot, targetLeftRot, Time.deltaTime * smoothSpeed);
                rightHandRot = Quaternion.Slerp(rightHandRot, targetRightRot, Time.deltaTime * smoothSpeed);
            }

            // Elbow hints
            leftElbowHint = lShoulderPos - transform.right * 0.25f + Vector3.down * 0.25f;
            rightElbowHint = rShoulderPos + transform.right * 0.25f + Vector3.down * 0.25f;
        }

        private void OnAnimatorIK(int layerIndex)
        {
            if (animator == null) return;

            float weight = 1.0f;

            // Head looks directly at player
            if (playerTarget != null)
            {
                animator.SetLookAtWeight(weight);
                animator.SetLookAtPosition(playerTarget.position + Vector3.up * 1.5f);
            }

            // Left hand & elbow
            animator.SetIKPositionWeight(AvatarIKGoal.LeftHand, weight);
            animator.SetIKPosition(AvatarIKGoal.LeftHand, leftHandTarget);
            animator.SetIKRotationWeight(AvatarIKGoal.LeftHand, weight);
            animator.SetIKRotation(AvatarIKGoal.LeftHand, leftHandRot);
            animator.SetIKHintPositionWeight(AvatarIKHint.LeftElbow, weight);
            animator.SetIKHintPosition(AvatarIKHint.LeftElbow, leftElbowHint);

            // Right hand & elbow
            animator.SetIKPositionWeight(AvatarIKGoal.RightHand, weight);
            animator.SetIKPosition(AvatarIKGoal.RightHand, rightHandTarget);
            animator.SetIKRotationWeight(AvatarIKGoal.RightHand, weight);
            animator.SetIKRotation(AvatarIKGoal.RightHand, rightHandRot);
            animator.SetIKHintPositionWeight(AvatarIKHint.RightElbow, weight);
            animator.SetIKHintPosition(AvatarIKHint.RightElbow, rightElbowHint);
        }
    }
}
