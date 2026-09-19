using UnityEngine;

public class PoseManager : MonoBehaviour
{
    /// <summary>
    /// The most recently parsed pose data. Read by combat systems (PunchDetector, etc.).
    /// </summary>
    public static PoseData LatestPose { get; private set; }

    private SkeletonVisualizer visualizer;
    private AvatarController avatarController;

    void Start()
    {
        visualizer = FindAnyObjectByType<SkeletonVisualizer>();
        avatarController = FindAnyObjectByType<AvatarController>();
    }

    void Update()
    {
        if (string.IsNullOrEmpty(UDPReceiver.LatestJson))
            return;

        try
        {
            PoseData pose = JsonUtility.FromJson<PoseData>(UDPReceiver.LatestJson);

            if (pose != null)
            {
                LatestPose = pose;
                if (visualizer != null) visualizer.UpdateLandmarks(pose);
                if (avatarController != null) avatarController.UpdatePose(pose);
            }
        }
        catch
        {
            // Ignore malformed packets
        }
    }
}
