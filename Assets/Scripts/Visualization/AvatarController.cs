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

    void Start()
    {
        animator = GetComponent<Animator>();
    }

    public void UpdatePose(PoseData pose)
    {
        currentPose = pose;
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
        if (currentPose == null || animator == null) return;

        // --- ARM CALCULATIONS ---
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

        // SIMPLIFIED, DIRECT VECTOR OFFSET:
        // We take the exact 3D displacement vector from the shoulder to the wrist in MediaPipe
        // Then we rotate it to match the character's facing direction, and scale it up to the Avatar's physical size.
        
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
        animator.SetLookAtPosition(animator.GetBoneTransform(HumanBodyBones.Head).position + transform.forward * 5f);

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
