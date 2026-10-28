using UnityEngine;
using UnityEngine.Serialization;
using System.Collections.Generic;

[RequireComponent(typeof(Animator))]
public class AvatarController : MonoBehaviour
{
    private Animator animator;
    private PoseData currentPose;

    public enum TrackingSource
    {
        FusedDualCamera,       // Uses 3D fused Front+Side landmarks for maximum accuracy (Recommended)
        SideAssistedRightArm,  // Left arm from Front, Right arm from Side
        RawFrontCameraOnly     // Single camera fallback
    }

    [Header("Tracking Mode")]
    [Tooltip("Select how dual-camera landmarks are routed to the avatar.")]
    public TrackingSource trackingSource = TrackingSource.FusedDualCamera;

    [Header("IK Settings")]
    [Range(0f, 1f)]
    public float ikWeight = 1.0f;
    
    [Header("Motion Smoothing & Speed")]
    [Range(15f, 90f)]
    [Tooltip("Base tracking speed. 55f gives near-instantaneous 1:1 response.")]
    [FormerlySerializedAs("smoothingSpeed")]
    public float baseSmoothingSpeed = 55f;

    [Tooltip("Punch acceleration multiplier to eliminate IK drag during fast punches.")]
    [Range(5f, 40f)]
    public float punchAcceleration = 20f;

    [Header("Avatar Scale & Reach")]
    [Tooltip("Scales the MediaPipe skeleton to match the Unity character proportions.")]
    public float avatarScale = 1.25f;

    [Tooltip("Fine-tunes the forward reach extension of punches.")]
    [Range(0.8f, 1.8f)]
    public float punchReachMultiplier = 1.15f;

    [Header("Dodging & Torso Movement")]
    [Tooltip("Enables the avatar to duck, bob, weave, and slip punches based on player body motion.")]
    public bool enableTorsoDodge = true;
    [Range(0f, 1f)]
    public float dodgeWeight = 0.5f;

    [Header("Mirroring Options")]
    [Tooltip("Enables full-body mirroring so all movements (arms, torso, ducks, footwork) act like looking in a mirror reflection.")]
    public bool fullBodyMirror = true;
    [Tooltip("Inverts lateral X motion.")]
    public bool invertLateral = false;

    public bool mirrorLeftRight
    {
        get => fullBodyMirror;
        set => fullBodyMirror = value;
    }

    // Smoothed IK Targets
    private Vector3 s_leftHandPos, s_leftElbowPos;
    private Vector3 s_rightHandPos, s_rightElbowPos;
    private Quaternion s_leftHandRot, s_rightHandRot;

    // Torso lean rotation & ducking
    private Quaternion s_spineRot = Quaternion.identity;
    private float s_verticalDip = 0f;

    // Measured physical arm reach limits to prevent Two-Bone IK stretching
    private float leftArmMaxReach = 0.75f;
    private float rightArmMaxReach = 0.75f;

    // Manual Punch State (for WASD / controller support)
    private float manualPunchTimer;
    private bool manualPunchIsLeft;
    private bool manualPunchIsHook;

    // Combat System World Position Accessors
    public Vector3 LeftHandWorldPos => s_leftHandPos != Vector3.zero ? s_leftHandPos : (animator != null && animator.GetBoneTransform(HumanBodyBones.LeftHand) != null ? animator.GetBoneTransform(HumanBodyBones.LeftHand).position : transform.position + transform.forward * 0.5f);
    public Vector3 RightHandWorldPos => s_rightHandPos != Vector3.zero ? s_rightHandPos : (animator != null && animator.GetBoneTransform(HumanBodyBones.RightHand) != null ? animator.GetBoneTransform(HumanBodyBones.RightHand).position : transform.position + transform.forward * 0.5f);
    public Vector3 HeadWorldPos => animator != null && animator.GetBoneTransform(HumanBodyBones.Head) != null ? animator.GetBoneTransform(HumanBodyBones.Head).position : transform.position + Vector3.up * 1.6f;

    // Telemetry getters for combat and gameplay systems
    public float LeftPunchSpeed => currentPose != null ? currentPose.left_punch_speed : 0f;
    public float RightPunchSpeed => currentPose != null ? currentPose.right_punch_speed : 0f;
    public string CurrentPunchType => currentPose != null ? currentPose.punch_type : "none";
    public PoseData CurrentPose => currentPose;

    void Start()
    {
        animator = GetComponent<Animator>();
        if (animator != null)
        {
            Transform ls = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            Transform le = animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
            Transform lh = animator.GetBoneTransform(HumanBodyBones.LeftHand);
            if (ls != null && le != null && lh != null)
            {
                leftArmMaxReach = (Vector3.Distance(ls.position, le.position) + Vector3.Distance(le.position, lh.position)) * 0.98f;
            }

            Transform rs = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            Transform re = animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
            Transform rh = animator.GetBoneTransform(HumanBodyBones.RightHand);
            if (rs != null && re != null && rh != null)
            {
                rightArmMaxReach = (Vector3.Distance(rs.position, re.position) + Vector3.Distance(re.position, rh.position)) * 0.98f;
            }
        }
    }

    public void UpdatePose(PoseData pose)
    {
        currentPose = pose;
    }

    public void TriggerManualPunch(bool isLeft, bool isHook = false)
    {
        manualPunchTimer = 0.35f;
        manualPunchIsLeft = isLeft;
        manualPunchIsHook = isHook;
    }

    private Vector3 GetMpVector(int index, bool isRightArm = false)
    {
        if (currentPose == null) return Vector3.zero;

        List<Landmark> targetLandmarks = null;

        switch (trackingSource)
        {
            case TrackingSource.FusedDualCamera:
                targetLandmarks = currentPose.landmarks;
                break;

            case TrackingSource.SideAssistedRightArm:
                if (isRightArm && currentPose.landmarks_right != null && currentPose.landmarks_right.Count > 0)
                    targetLandmarks = currentPose.landmarks_right;
                else
                    targetLandmarks = currentPose.landmarks;
                break;

            case TrackingSource.RawFrontCameraOnly:
                targetLandmarks = (currentPose.landmarks_front != null && currentPose.landmarks_front.Count > 0)
                                    ? currentPose.landmarks_front
                                    : currentPose.landmarks;
                break;
        }

        if (targetLandmarks == null || targetLandmarks.Count <= index) return Vector3.zero;

        Landmark lm = targetLandmarks[index];
        if (lm.visibility > 0f && lm.visibility < 0.25f) return Vector3.zero;
        if (lm.x == 0f && lm.y == 0f && lm.z == 0f) return Vector3.zero;
        // Lateral X (invertLateral flips X sign), Invert Y (vertical height), Invert Z (forward punch extension)
        float x = invertLateral ? lm.x : -lm.x;
        return new Vector3(x, -lm.y, -lm.z);
    }

    void Update()
    {
        if (animator == null) return;

        // Default resting boxing guard positions
        Transform head = animator.GetBoneTransform(HumanBodyBones.Head);
        Vector3 headPos = head != null ? head.position : transform.position + Vector3.up * 1.55f;
        Vector3 guardLeft = headPos + transform.forward * 0.35f - transform.right * 0.18f + Vector3.down * 0.12f;
        Vector3 guardRight = headPos + transform.forward * 0.35f + transform.right * 0.18f + Vector3.down * 0.12f;

        Transform leftShoulder = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
        Transform rightShoulder = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
        Vector3 lShoulderPos = leftShoulder != null ? leftShoulder.position : transform.position - transform.right * 0.25f + Vector3.up * 1.4f;
        Vector3 rShoulderPos = rightShoulder != null ? rightShoulder.position : transform.position + transform.right * 0.25f + Vector3.up * 1.4f;

        // Hotkey [M] to toggle mirror mode
#if ENABLE_INPUT_SYSTEM
        if (UnityEngine.InputSystem.Keyboard.current != null && UnityEngine.InputSystem.Keyboard.current.mKey.wasPressedThisFrame)
        {
            fullBodyMirror = !fullBodyMirror;
            Debug.Log($"[AvatarController] Full Body Mirror: {(fullBodyMirror ? "ON (Mirror Reflection)" : "OFF (Anatomical)")}");
        }
#else
        if (Input.GetKeyDown(KeyCode.M))
        {
            fullBodyMirror = !fullBodyMirror;
            Debug.Log($"[AvatarController] Full Body Mirror: {(fullBodyMirror ? "ON (Mirror Reflection)" : "OFF (Anatomical)")}");
        }
#endif

        if (currentPose != null && currentPose.landmarks != null && currentPose.landmarks.Count == 33)
        {
            int lShoulder = fullBodyMirror ? 11 : 12;
            int lElbow = fullBodyMirror ? 13 : 14;
            int lWrist = fullBodyMirror ? 15 : 16;
            int lPinky = fullBodyMirror ? 19 : 20;
            int lIndex = fullBodyMirror ? 17 : 18;

            int rShoulder = fullBodyMirror ? 12 : 11;
            int rElbow = fullBodyMirror ? 14 : 13;
            int rWrist = fullBodyMirror ? 16 : 15;
            int rPinky = fullBodyMirror ? 20 : 19;
            int rIndex = fullBodyMirror ? 18 : 17;

            // --- ARM CALCULATIONS FROM DUAL-CAMERA MEDIAPIPE ---
            CalculateArmTargets(
                lShoulder, lElbow, lWrist, lIndex, lPinky, 
                HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand,
                ref s_leftHandPos, ref s_leftElbowPos, ref s_leftHandRot,
                false
            );

            CalculateArmTargets(
                rShoulder, rElbow, rWrist, rIndex, rPinky, 
                HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand,
                ref s_rightHandPos, ref s_rightElbowPos, ref s_rightHandRot,
                true
            );

            if (enableTorsoDodge)
            {
                CalculateTorsoDodge();
            }
        }
        else
        {
            // Stand in default boxing guard when no camera pose is available
            s_leftHandPos = Vector3.Lerp(s_leftHandPos == Vector3.zero ? guardLeft : s_leftHandPos, guardLeft, Time.deltaTime * baseSmoothingSpeed);
            s_rightHandPos = Vector3.Lerp(s_rightHandPos == Vector3.zero ? guardRight : s_rightHandPos, guardRight, Time.deltaTime * baseSmoothingSpeed);

            s_leftElbowPos = lShoulderPos - transform.right * 0.2f + Vector3.down * 0.25f;
            s_rightElbowPos = rShoulderPos + transform.right * 0.2f + Vector3.down * 0.25f;

            s_leftHandRot = Quaternion.LookRotation(transform.forward, Vector3.up);
            s_rightHandRot = Quaternion.LookRotation(transform.forward, Vector3.up);
        }

        // Manual Punch Overlay (if triggered via keyboard / controller)
        if (manualPunchTimer > 0f)
        {
            manualPunchTimer -= Time.deltaTime;
            float punchProgress = 1f - (manualPunchTimer / 0.35f);
            float ext = Mathf.Sin(punchProgress * Mathf.PI);

            Vector3 punchTargetOffset = transform.forward * (ext * 0.95f);
            if (manualPunchIsHook)
            {
                float lateralArc = Mathf.Sin(punchProgress * Mathf.PI) * 0.35f;
                punchTargetOffset += (manualPunchIsLeft ? transform.right : -transform.right) * lateralArc;
            }

            if (manualPunchIsLeft)
            {
                s_leftHandPos = guardLeft + punchTargetOffset;
                s_leftHandRot = Quaternion.LookRotation(transform.forward, Vector3.up);
            }
            else
            {
                s_rightHandPos = guardRight + punchTargetOffset;
                s_rightHandRot = Quaternion.LookRotation(transform.forward, Vector3.up);
            }
        }
    }

    private void CalculateArmTargets(
        int idxShoulder, int idxElbow, int idxWrist, int idxIndex, int idxPinky,
        HumanBodyBones boneShoulder, HumanBodyBones boneElbow, HumanBodyBones boneHand,
        ref Vector3 smoothedHandPos, ref Vector3 smoothedElbowPos, ref Quaternion smoothedHandRot,
        bool isRightArm)
    {
        Transform shoulder = animator.GetBoneTransform(boneShoulder);
        Transform elbow = animator.GetBoneTransform(boneElbow);
        Transform hand = animator.GetBoneTransform(boneHand);

        if (shoulder == null || elbow == null || hand == null) return;

        Vector3 mps = GetMpVector(idxShoulder, isRightArm);
        Vector3 mpe = GetMpVector(idxElbow, isRightArm);
        Vector3 mpw = GetMpVector(idxWrist, isRightArm);
        Vector3 mpi = GetMpVector(idxIndex, isRightArm); 
        Vector3 mpp = GetMpVector(idxPinky, isRightArm); 

        if (mps == Vector3.zero || mpw == Vector3.zero) return;

        // Vector offset from shoulder to wrist in MediaPipe space
        Vector3 localReachVector = mpw - mps;

        // Dynamic forward punch extension: amplify monocular Z depth to full character reach
        float rawZ = localReachVector.z;
        if (rawZ > 0.08f)
        {
            float norm = Mathf.Clamp01((rawZ - 0.08f) / 0.22f);
            localReachVector.z = Mathf.Lerp(rawZ, 0.65f * punchReachMultiplier, norm);
        }
        else
        {
            localReachVector.z *= punchReachMultiplier;
        }

        // Transform into Avatar's world orientation and scale to character size
        Vector3 worldReachVector = transform.TransformDirection(localReachVector) * avatarScale;

        // Clamp to physical arm reach limit to eliminate Two-Bone IK popping/tearing
        float maxReach = isRightArm ? rightArmMaxReach : leftArmMaxReach;
        if (maxReach > 0.1f && worldReachVector.magnitude > maxReach)
        {
            worldReachVector = worldReachVector.normalized * maxReach;
        }
        Vector3 targetPos = shoulder.position + worldReachVector;

        // Elbow hint (pole vector) with natural outward/downward bias to prevent 180° elbow flips
        Vector3 localElbowVector = mpe - mps;
        Vector3 worldElbowVector = transform.TransformDirection(localElbowVector) * avatarScale;
        Vector3 outwardDir = isRightArm ? transform.right : -transform.right;
        Vector3 targetHint = shoulder.position + worldElbowVector + (outwardDir * 0.14f - transform.up * 0.06f);

        // Fist rotation with authentic boxing glove pronation on punch extension
        Vector3 forward = transform.TransformDirection((mpi - mpw).normalized);
        Vector3 up = transform.TransformDirection((mpi - mpp).normalized);
        Quaternion defaultHandRot = Quaternion.LookRotation(transform.forward, Vector3.up);
        Quaternion targetRot = defaultHandRot;
        if (forward.sqrMagnitude > 0.01f && up.sqrMagnitude > 0.01f)
        {
            targetRot = Quaternion.LookRotation(forward, up);

            // Pronate fist (knuckles horizontal, palm down) as arm extends forward
            float extension = Mathf.Clamp01(localReachVector.magnitude / 0.55f);
            float pronationAngle = Mathf.Lerp(0f, isRightArm ? -75f : 75f, extension);
            targetRot = Quaternion.AngleAxis(pronationAngle, forward) * targetRot;
        }
        else if (localReachVector.sqrMagnitude > 0.01f)
        {
            Vector3 armDir = transform.TransformDirection(localReachVector.normalized);
            targetRot = Quaternion.LookRotation(armDir, Vector3.up);
        }

        // Dynamic smoothing: punch speed accelerates responsiveness for instant snap
        float currentSpeed = isRightArm ? RightPunchSpeed : LeftPunchSpeed;
        float dynamicSmoothing = baseSmoothingSpeed + Mathf.Clamp(currentSpeed * punchAcceleration, 0f, 65f);

        smoothedHandPos = Vector3.Lerp(
            smoothedHandPos == Vector3.zero ? targetPos : smoothedHandPos,
            targetPos,
            Time.deltaTime * dynamicSmoothing
        );

        smoothedElbowPos = Vector3.Lerp(
            smoothedElbowPos == Vector3.zero ? targetHint : smoothedElbowPos,
            targetHint,
            Time.deltaTime * dynamicSmoothing
        );

        smoothedHandRot = Quaternion.Slerp(
            smoothedHandRot == Quaternion.identity ? targetRot : smoothedHandRot,
            targetRot,
            Time.deltaTime * dynamicSmoothing
        );
    }

    private void CalculateTorsoDodge()
    {
        Vector3 leftShoulder = GetMpVector(11);
        Vector3 rightShoulder = GetMpVector(12);
        Vector3 leftHip = GetMpVector(23);
        Vector3 rightHip = GetMpVector(24);

        if (leftShoulder == Vector3.zero || rightShoulder == Vector3.zero ||
            leftHip == Vector3.zero || rightHip == Vector3.zero)
            return;

        Vector3 shoulderMid = (leftShoulder + rightShoulder) * 0.5f;
        Vector3 hipMid = (leftHip + rightHip) * 0.5f;

        Vector3 torsoOffset = shoulderMid - hipMid;

        float slipMultiplier = fullBodyMirror ? 35f : -35f;
        float slipAngle = torsoOffset.x * slipMultiplier * dodgeWeight;
        float leanAngle = torsoOffset.z * 30f * dodgeWeight;

        Quaternion targetSpineRot = Quaternion.Euler(leanAngle, 0f, slipAngle);
        s_spineRot = Quaternion.Slerp(s_spineRot, targetSpineRot, Time.deltaTime * baseSmoothingSpeed);

        // Pelvic ducking: check body_offset_y or vertical shoulder dip
        float targetDip = 0f;
        if (currentPose != null && currentPose.body_offset_y < -0.03f)
        {
            targetDip = Mathf.Clamp(-currentPose.body_offset_y * 1.2f, 0f, 0.40f) * dodgeWeight;
        }
        else
        {
            float verticalOffset = -torsoOffset.y;
            if (verticalOffset > 0.05f)
            {
                targetDip = Mathf.Clamp((verticalOffset - 0.05f) * 1.5f, 0f, 0.35f) * dodgeWeight;
            }
        }
        s_verticalDip = Mathf.Lerp(s_verticalDip, targetDip, Time.deltaTime * baseSmoothingSpeed);
    }

    void OnAnimatorIK(int layerIndex)
    {
        if (animator == null) return;

        // Head look-at forward
        Transform headBone = animator.GetBoneTransform(HumanBodyBones.Head);
        if (headBone != null)
        {
            animator.SetLookAtWeight(ikWeight);
            animator.SetLookAtPosition(headBone.position + transform.forward * 5f);
        }

        // Pelvis ducking / squatting under punches
        if (enableTorsoDodge && s_verticalDip > 0.015f)
        {
            animator.bodyPosition = animator.bodyPosition - Vector3.up * s_verticalDip;
        }

        // Torso Dodge Spine adjustment
        if (enableTorsoDodge)
        {
            Transform spine = animator.GetBoneTransform(HumanBodyBones.Spine);
            if (spine != null)
            {
                spine.localRotation = spine.localRotation * s_spineRot;
            }
        }

        // Left Arm IK (Jab / Hook)
        animator.SetIKPositionWeight(AvatarIKGoal.LeftHand, ikWeight);
        animator.SetIKPosition(AvatarIKGoal.LeftHand, s_leftHandPos);

        animator.SetIKRotationWeight(AvatarIKGoal.LeftHand, ikWeight);
        animator.SetIKRotation(AvatarIKGoal.LeftHand, s_leftHandRot);

        animator.SetIKHintPositionWeight(AvatarIKHint.LeftElbow, ikWeight);
        animator.SetIKHintPosition(AvatarIKHint.LeftElbow, s_leftElbowPos);

        // Right Arm IK (Cross / Uppercut)
        animator.SetIKPositionWeight(AvatarIKGoal.RightHand, ikWeight);
        animator.SetIKPosition(AvatarIKGoal.RightHand, s_rightHandPos);

        animator.SetIKRotationWeight(AvatarIKGoal.RightHand, ikWeight);
        animator.SetIKRotation(AvatarIKGoal.RightHand, s_rightHandRot);

        animator.SetIKHintPositionWeight(AvatarIKHint.RightElbow, ikWeight);
        animator.SetIKHintPosition(AvatarIKHint.RightElbow, s_rightElbowPos);

        // Full Body Leg / Knee hints (if visible in camera)
        Vector3 lKnee = GetMpVector(25);
        Vector3 rKnee = GetMpVector(26);
        Transform lHip = animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
        Transform rHip = animator.GetBoneTransform(HumanBodyBones.RightUpperLeg);

        if (lKnee != Vector3.zero && lHip != null)
        {
            Vector3 kneeHintL = lHip.position + transform.TransformDirection(lKnee - GetMpVector(23)) * avatarScale;
            animator.SetIKHintPositionWeight(AvatarIKHint.LeftKnee, ikWeight * 0.6f);
            animator.SetIKHintPosition(AvatarIKHint.LeftKnee, kneeHintL);
        }

        if (rKnee != Vector3.zero && rHip != null)
        {
            Vector3 kneeHintR = rHip.position + transform.TransformDirection(rKnee - GetMpVector(24)) * avatarScale;
            animator.SetIKHintPositionWeight(AvatarIKHint.RightKnee, ikWeight * 0.6f);
            animator.SetIKHintPosition(AvatarIKHint.RightKnee, kneeHintR);
        }
    }
}
