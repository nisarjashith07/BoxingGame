using UnityEngine;

public class SkeletonVisualizer : MonoBehaviour
{
    [Header("Prefabs")]
    public GameObject landmarkPrefab;
    public GameObject bonePrefab;

    private GameObject[] points;
    private GameObject[] bones;

    // Bone connections (MediaPipe indices)
    private readonly int[,] bonePairs =
    {
        {11,13}, // Left Upper Arm
        {13,15}, // Left Lower Arm

        {12,14}, // Right Upper Arm
        {14,16}, // Right Lower Arm

        {11,12}, // Shoulders

        {11,23}, // Left Torso
        {12,24}, // Right Torso

        {23,24}, // Hips

        {23,25}, // Left Thigh
        {25,27}, // Left Shin

        {24,26}, // Right Thigh
        {26,28}  // Right Shin
    };

    void Start()
    {
        //-----------------------------
        // Create Parent Objects
        //-----------------------------

        GameObject landmarkParent = new GameObject("Landmarks");
        landmarkParent.transform.SetParent(transform);

        GameObject boneParent = new GameObject("Bones");
        boneParent.transform.SetParent(transform);

        //-----------------------------
        // Create Landmarks
        //-----------------------------

        points = new GameObject[33];

        for (int i = 0; i < 33; i++)
        {
            points[i] = Instantiate(
                landmarkPrefab,
                Vector3.zero,
                Quaternion.identity,
                landmarkParent.transform
            );

            points[i].name = "Landmark_" + i;
        }

        //-----------------------------
        // Create Bones
        //-----------------------------

        bones = new GameObject[bonePairs.GetLength(0)];

        for (int i = 0; i < bonePairs.GetLength(0); i++)
        {
            bones[i] = Instantiate(
                bonePrefab,
                Vector3.zero,
                Quaternion.identity,
                boneParent.transform
            );

            bones[i].name = "Bone_" + i;

            Bone bone = bones[i].GetComponent<Bone>();

            if (bone == null)
                bone = bones[i].AddComponent<Bone>();

            bone.startPoint = points[bonePairs[i, 0]].transform;
            bone.endPoint = points[bonePairs[i, 1]].transform;
        }
    }

    public void UpdateLandmarks(PoseData pose)
    {
        if (pose == null)
            return;

        if (pose.landmarks == null)
            return;

        if (pose.landmarks.Count != 33)
            return;

        for (int i = 0; i < 33; i++)
        {
            Landmark lm = pose.landmarks[i];

            Vector3 position = new Vector3(
                (lm.x - 0.5f) * 8f,
                -(lm.y - 0.5f) * 5f,
                -lm.z * 2f
            );

            points[i].transform.position = position;
        }
    }
}