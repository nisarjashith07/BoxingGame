using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace BoxingAI
{
    /// <summary>
    /// Dedicated Boxing Player Controller that replaces the free-roam ThirdPersonController.
    /// - Always locks facing onto the opponent (no turning your back in a boxing match).
    /// - WASD performs tight boxing footwork: W=step in, S=step back, A/D=circle/strafe.
    /// - Left/Right Click or J/K throws punches.
    /// - Space/B holds guard.
    /// - Distance clamping prevents clipping through the opponent.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class BoxingPlayerController : MonoBehaviour
    {
        [Header("Movement Speeds")]
        public float forwardSpeed = 1.8f;
        public float backwardSpeed = 1.4f;
        public float strafeSpeed = 1.5f;
        public float rotationSpeed = 14f;

        [Header("Opponent Target")]
        [SerializeField] private Transform opponentTransform;

        private CharacterController controller;
        private Animator animator;
        private PunchDetector punchDetector;
        private float currentAnimSpeed;

        private void Awake()
        {
            controller = GetComponent<CharacterController>();
            animator = GetComponent<Animator>();
            punchDetector = FindAnyObjectByType<PunchDetector>();

            // Disable the free-roam ThirdPersonController if present
            var tpc = GetComponent<StarterAssets.ThirdPersonController>();
            if (tpc != null)
                tpc.enabled = false;
        }

        private void Start()
        {
            FindOpponent();

            // Lock cursor for focused boxing controls
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        public void SetOpponent(Transform opponent)
        {
            opponentTransform = opponent;
        }

        private void FindOpponent()
        {
            if (opponentTransform != null) return;

            AIFighterController ai = FindAnyObjectByType<AIFighterController>();
            if (ai != null)
            {
                opponentTransform = ai.transform;
                return;
            }

            GameObject aiGo = GameObject.Find("AIFighter");
            if (aiGo != null)
                opponentTransform = aiGo.transform;
        }

        private void Update()
        {
            // Ensure ThirdPersonController remains disabled
            var tpc = GetComponent<StarterAssets.ThirdPersonController>();
            if (tpc != null && tpc.enabled)
                tpc.enabled = false;

            if (opponentTransform == null)
                FindOpponent();

            // 1. ALWAYS FACE OPPONENT (Lock-on boxing stance)
            FaceOpponent();

            // 2. READ WASD MOVEMENT
            Vector2 moveInput = ReadMoveInput();

            // Distance clamp: don't push into or clip through opponent when in close pocket
            if (opponentTransform != null)
            {
                float dist = Vector3.Distance(transform.position, opponentTransform.position);
                if (dist < 1.15f && moveInput.y > 0f)
                {
                    moveInput.y = 0f;
                }
            }

            // 3. APPLY BOXING FOOTWORK
            Vector3 moveDirection = Vector3.zero;
            if (moveInput.sqrMagnitude > 0.01f)
            {
                // W/S = forward/backward relative to facing direction
                Vector3 forward = transform.forward * moveInput.y;
                float forwardMoveSpeed = moveInput.y > 0 ? forwardSpeed : backwardSpeed;

                // A/D = strafe left/right relative to facing direction
                Vector3 right = transform.right * moveInput.x * strafeSpeed;

                moveDirection = (forward * forwardMoveSpeed + right).normalized * Mathf.Max(forwardMoveSpeed, strafeSpeed);
            }

            // Gravity
            if (controller != null && controller.enabled)
            {
                if (!controller.isGrounded)
                    moveDirection.y -= 9.81f;

                controller.Move(moveDirection * Time.deltaTime);
            }

            // 4. ANIMATE LOCOMOTION (Walking footwork, never running)
            if (animator != null)
            {
                float targetSpeed = Mathf.Clamp(new Vector3(moveDirection.x, 0, moveDirection.z).magnitude, 0f, 1.3f);
                currentAnimSpeed = Mathf.Lerp(currentAnimSpeed, targetSpeed, Time.deltaTime * 10f);
                animator.SetFloat("Speed", currentAnimSpeed);
                animator.SetFloat("MotionSpeed", 1f);
            }

            // 5. READ PUNCH / GUARD INPUTS
            CheckCombatInputs();

            // Cursor lock toggle on Escape
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                Cursor.lockState = Cursor.lockState == CursorLockMode.Locked ? CursorLockMode.None : CursorLockMode.Locked;
                Cursor.visible = Cursor.lockState != CursorLockMode.Locked;
            }
#endif
        }

        private void FaceOpponent()
        {
            if (opponentTransform == null) return;

            Vector3 lookDir = opponentTransform.position - transform.position;
            lookDir.y = 0f;

            if (lookDir.sqrMagnitude > 0.01f)
            {
                Quaternion targetRot = Quaternion.LookRotation(lookDir, Vector3.up);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, Time.deltaTime * rotationSpeed);
            }
        }

        private Vector2 ReadMoveInput()
        {
            Vector2 input = Vector2.zero;

#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null)
            {
                if (Keyboard.current.wKey.isPressed || Keyboard.current.upArrowKey.isPressed) input.y += 1f;
                if (Keyboard.current.sKey.isPressed || Keyboard.current.downArrowKey.isPressed) input.y -= 1f;
                if (Keyboard.current.aKey.isPressed || Keyboard.current.leftArrowKey.isPressed) input.x -= 1f;
                if (Keyboard.current.dKey.isPressed || Keyboard.current.rightArrowKey.isPressed) input.x += 1f;
            }
#endif

            return input.normalized;
        }

        private void CheckCombatInputs()
        {
            if (punchDetector == null)
                punchDetector = FindAnyObjectByType<PunchDetector>();

            if (punchDetector == null) return;

#if ENABLE_INPUT_SYSTEM
            // Mouse Clicks
            if (Mouse.current != null)
            {
                if (Mouse.current.leftButton.wasPressedThisFrame)
                    punchDetector.TriggerManualPunch(BoxingAction.Jab);
                else if (Mouse.current.rightButton.wasPressedThisFrame)
                    punchDetector.TriggerManualPunch(BoxingAction.Cross);
            }

            // Keys
            if (Keyboard.current != null)
            {
                if (Keyboard.current.jKey.wasPressedThisFrame)
                    punchDetector.TriggerManualPunch(BoxingAction.Jab);
                else if (Keyboard.current.kKey.wasPressedThisFrame)
                    punchDetector.TriggerManualPunch(BoxingAction.Cross);
                else if (Keyboard.current.hKey.wasPressedThisFrame)
                    punchDetector.TriggerManualPunch(BoxingAction.HookLeft);
                else if (Keyboard.current.lKey.wasPressedThisFrame)
                    punchDetector.TriggerManualPunch(BoxingAction.HookRight);
            }
#endif
        }
    }
}
