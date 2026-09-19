using UnityEngine;

[RequireComponent(typeof(Animator))]
public class AvatarController : MonoBehaviour
{
    private Animator animator;
    private PoseData currentPose;

    [Header("IK Settings")]
    public float ikWeight = 1.0f;
    
    [Header("Motion Smoothing")]
    [Range(5f, 30f)]
    public float smoothingSpeed = 15f;

    [Header("Avatar Scale Calibration")]
    [Tooltip("If the avatar's arms don't reach far enough, increase this to scale up the MediaPipe skeleton to match the Unity character.")]
    public float avatarScale = 1.2f;

    // Smoothed Targets
    private Vector3 s_leftHandPos, s_leftElbowPos;
    private Vector3 s_rightHandPos, s_rightElbowPos;
    private Quaternion s_leftHandRot, s_rightHandRot;

    // Manual Punch State
    private float manualPunchTimer;
    private bool manualPunchIsLeft;

    public Vector3 LeftHandWorldPos => s_leftHandPos != Vector3.zero ? s_leftHandPos : (animator != null && animator.GetBoneTransform(HumanBodyBones.LeftHand) != null ? animator.GetBoneTransform(HumanBodyBones.LeftHand).position : transform.position + transform.forward * 0.5f);
    public Vector3 RightHandWorldPos => s_rightHandPos != Vector3.zero ? s_rightHandPos : (animator != null && animator.GetBoneTransform(HumanBodyBones.RightHand) != null ? animator.GetBoneTransform(HumanBodyBones.RightHand).position : transform.position + transform.forward * 0.5f);
    public Vector3 HeadWorldPos => animator != null && animator.GetBoneTransform(HumanBodyBones.Head) != null ? animator.GetBoneTransform(HumanBodyBones.Head).position : transform.position + Vector3.up * 1.6f;

    void Start()
    {
        animator = GetComponent<Animator>();
    }

    public void UpdatePose(PoseData pose)
    {
        currentPose = pose;
    }

    public void TriggerManualPunch(bool isLeft)
    {
        manualPunchTimer = 0.35f;
        manualPunchIsLeft = isLeft;
    }

    private Vector3 GetMpVector(int index)
    {
        if (currentPose == null || currentPose.landmarks == null || currentPose.landmarks.Count <= index) return Vector3.zero;
        Landmark lm = currentPose.landmarks[index];
        // Mirror X, Invert Y, Forward Z
        return new Vector3(-lm.x, -lm.y, -lm.z);
    }

    void Update()
    {
        if (animator == null) return;

        // Default boxing guard positions
        Transform head = animator.GetBoneTransform(HumanBodyBones.Head);
        Vector3 headPos = head != null ? head.position : transform.position + Vector3.up * 1.55f;
        Vector3 guardLeft = headPos + transform.forward * 0.35f - transform.right * 0.18f + Vector3.down * 0.12f;
        Vector3 guardRight = headPos + transform.forward * 0.35f + transform.right * 0.18f + Vector3.down * 0.12f;

        Transform leftShoulder = animator.GetBoneTransform(HumanBodyBones.LeftShoulder);
        Transform rightShoulder = animator.GetBoneTransform(HumanBodyBones.RightShoulder);
        Vector3 lShoulderPos = leftShoulder != null ? leftShoulder.position : transform.position - transform.right * 0.25f + Vector3.up * 1.4f;
        Vector3 rShoulderPos = rightShoulder != null ? rightShoulder.position : transform.position + transform.right * 0.25f + Vector3.up * 1.4f;

        if (currentPose != null && currentPose.landmarks != null && currentPose.landmarks.Count == 33)
        {
            // --- ARM CALCULATIONS FROM MEDIAPIPE ---
            CalculateArmTargets(
                11, 13, 15, 19, 17, 
                HumanBodyBones.LeftShoulder, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand,
                ref s_leftHandPos, ref s_leftElbowPos, ref s_leftHandRot
            );

            CalculateArmTargets(
                12, 14, 16, 20, 18, 
                HumanBodyBones.RightShoulder, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand,
                ref s_rightHandPos, ref s_rightElbowPos, ref s_rightHandRot
            );
        }
        else
        {
            // Stand in default boxing guard when no pose is received
            s_leftHandPos = Vector3.Lerp(s_leftHandPos == Vector3.zero ? guardLeft : s_leftHandPos, guardLeft, Time.deltaTime * smoothingSpeed);
            s_rightHandPos = Vector3.Lerp(s_rightHandPos == Vector3.zero ? guardRight : s_rightHandPos, guardRight, Time.deltaTime * smoothingSpeed);

            s_leftElbowPos = lShoulderPos - transform.right * 0.2f + Vector3.down * 0.25f;
            s_rightElbowPos = rShoulderPos + transform.right * 0.2f + Vector3.down * 0.25f;

            s_leftHandRot = Quaternion.LookRotation(transform.forward, Vector3.up);
            s_rightHandRot = Quaternion.LookRotation(transform.forward, Vector3.up);
        }

        // Manual Punch Overlay
        if (manualPunchTimer > 0f)
        {
            manualPunchTimer -= Time.deltaTime;
            float punchProgress = 1f - (manualPunchTimer / 0.35f);
            float ext = Mathf.Sin(punchProgress * Mathf.PI); // 0 -> 1 -> 0

            Vector3 punchTargetOffset = transform.forward * (ext * 0.95f);

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
        ref Vector3 smoothedHandPos, ref Vector3 smoothedElbowPos, ref Quaternion smoothedHandRot)
    {
        Transform shoulder = animator.GetBoneTransform(boneShoulder);
        Transform elbow = animator.GetBoneTransform(boneElbow);
        Transform hand = animator.GetBoneTransform(boneHand);

        if (shoulder == null || elbow == null || hand == null) return;

        Vector3 mps = GetMpVector(idxShoulder);
        Vector3 mpe = GetMpVector(idxElbow);
        Vector3 mpw = GetMpVector(idxWrist);
        Vector3 mpi = GetMpVector(idxIndex); 
        Vector3 mpp = GetMpVector(idxPinky); 

        if (mps == Vector3.zero || mpw == Vector3.zero) return;

        Vector3 localReachVector = mpw - mps;
        Vector3 worldReachVector = transform.TransformDirection(localReachVector) * avatarScale;
        Vector3 targetPos = shoulder.position + worldReachVector;

        Vector3 localElbowVector = mpe - mps;
        Vector3 worldElbowVector = transform.TransformDirection(localElbowVector) * avatarScale;
        Vector3 targetHint = shoulder.position + worldElbowVector;

        // Rotation
        Vector3 forward = transform.TransformDirection((mpi - mpw).normalized);
        Vector3 up = transform.TransformDirection((mpi - mpp).normalized);
        Quaternion targetRot = Quaternion.identity;
        if (forward != Vector3.zero && up != Vector3.zero) {
            targetRot = Quaternion.LookRotation(forward, up);
        }

        // Apply Smoothing
        smoothedHandPos = Vector3.Lerp(smoothedHandPos == Vector3.zero ? targetPos : smoothedHandPos, targetPos, Time.deltaTime * smoothingSpeed);
        smoothedElbowPos = Vector3.Lerp(smoothedElbowPos == Vector3.zero ? targetHint : smoothedElbowPos, targetHint, Time.deltaTime * smoothingSpeed);
        smoothedHandRot = Quaternion.Slerp(smoothedHandRot == Quaternion.identity ? targetRot : smoothedHandRot, targetRot, Time.deltaTime * smoothingSpeed);
    }

    void OnAnimatorIK(int layerIndex)
    {
        if (animator == null) return;

        animator.SetLookAtWeight(ikWeight);
        Transform head = animator.GetBoneTransform(HumanBodyBones.Head);
        if (head != null)
            animator.SetLookAtPosition(head.position + transform.forward * 5f);

        // Left
        animator.SetIKHintPositionWeight(AvatarIKHint.LeftElbow, ikWeight);
        animator.SetIKHintPosition(AvatarIKHint.LeftElbow, s_leftElbowPos);
        animator.SetIKPositionWeight(AvatarIKGoal.LeftHand, ikWeight);
        animator.SetIKPosition(AvatarIKGoal.LeftHand, s_leftHandPos);
        animator.SetIKRotationWeight(AvatarIKGoal.LeftHand, ikWeight);
        animator.SetIKRotation(AvatarIKGoal.LeftHand, s_leftHandRot);

        // Right
        animator.SetIKHintPositionWeight(AvatarIKHint.RightElbow, ikWeight);
        animator.SetIKHintPosition(AvatarIKHint.RightElbow, s_rightElbowPos);
        animator.SetIKPositionWeight(AvatarIKGoal.RightHand, ikWeight);
        animator.SetIKPosition(AvatarIKGoal.RightHand, s_rightHandPos);
        animator.SetIKRotationWeight(AvatarIKGoal.RightHand, ikWeight);
        animator.SetIKRotation(AvatarIKGoal.RightHand, s_rightHandRot);
    }
}
