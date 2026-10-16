using UnityEngine;
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
    
    [Header("Motion Smoothing")]
    [Range(5f, 40f)]
    [Tooltip("Higher values respond faster to explosive punches.")]
    public float baseSmoothingSpeed = 22f;

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

    // Smoothed IK Targets
    private Vector3 s_leftHandPos, s_leftElbowPos;
    private Vector3 s_rightHandPos, s_rightElbowPos;
    private Quaternion s_leftHandRot, s_rightHandRot;

    // Torso lean rotation
    private Quaternion s_spineRot = Quaternion.identity;

    // Measured physical arm reach limits to prevent Two-Bone IK stretching
    private float leftArmMaxReach = 0.75f;
    private float rightArmMaxReach = 0.75f;

    // Telemetry getters for gameplay systems (punch damage, hit sounds, UI)
    public float LeftPunchSpeed => currentPose != null ? currentPose.left_punch_speed : 0f;
    public float RightPunchSpeed => currentPose != null ? currentPose.right_punch_speed : 0f;
    public string CurrentPunchType => currentPose != null ? currentPose.punch_type : "none";
    public PoseData CurrentPose => currentPose;

    void Start()
    {
        animator = GetComponent<Animator>();
        if (animator != null)
        {
            // Calculate character's exact physical arm lengths
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

    private Vector3 GetMpVector(int index, bool isRightArm = false)
    {
        if (currentPose == null) return Vector3.zero;

        List<Landmark> targetLandmarks = null;

        switch (trackingSource)
        {
            case TrackingSource.FusedDualCamera:
                // Primary 3D fused landmarks: Front X/Y + Side Z punch depth
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
        // Mirror X (lateral), Invert Y (vertical height), Invert Z (forward punch extension)
        return new Vector3(-lm.x, -lm.y, -lm.z);
    }

    void Update()
    {
        if (currentPose == null || animator == null) return;

        // --- LEFT ARM CALCULATIONS (Lead Hand / Jab / Lead Hook) ---
        CalculateArmTargets(
            11, 13, 15, 19, 17, 
            HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand,
            ref s_leftHandPos, ref s_leftElbowPos, ref s_leftHandRot,
            false
        );

        // --- RIGHT ARM CALCULATIONS (Rear Hand / Cross / Power Punch) ---
        CalculateArmTargets(
            12, 14, 16, 20, 18, 
            HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand,
            ref s_rightHandPos, ref s_rightElbowPos, ref s_rightHandRot,
            true
        );

        // --- TORSO DODGING & SLIPPING ---
        if (enableTorsoDodge)
        {
            CalculateTorsoDodge();
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

        // Apply forward punch extension multiplier
        localReachVector.z *= punchReachMultiplier;

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
        Quaternion targetRot = Quaternion.identity;
        if (forward != Vector3.zero && up != Vector3.zero)
        {
            targetRot = Quaternion.LookRotation(forward, up);

            // Pronate fist (knuckles horizontal, palm down) as arm extends forward
            float extension = Mathf.Clamp01(localReachVector.magnitude / 0.55f);
            float pronationAngle = Mathf.Lerp(0f, isRightArm ? -75f : 75f, extension);
            targetRot = Quaternion.AngleAxis(pronationAngle, forward) * targetRot;
        }

        // Dynamic smoothing: punch speed increases responsiveness
        float currentSpeed = isRightArm ? RightPunchSpeed : LeftPunchSpeed;
        float dynamicSmoothing = baseSmoothingSpeed + Mathf.Clamp(currentSpeed * 4f, 0f, 25f);

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
        // Calculate lateral slip and forward/backward lean from shoulders relative to hips
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

        // Slip angle (Z rotation in local space): lateral tilt
        float slipAngle = -torsoOffset.x * 35f * dodgeWeight;

        // Lean angle (X rotation in local space): forward/backward dodge (measured by side camera!)
        float leanAngle = torsoOffset.z * 30f * dodgeWeight;

        Quaternion targetSpineRot = Quaternion.Euler(leanAngle, 0f, slipAngle);
        s_spineRot = Quaternion.Slerp(s_spineRot, targetSpineRot, Time.deltaTime * baseSmoothingSpeed);
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
    }
}
