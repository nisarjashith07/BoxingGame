using UnityEngine;

public class PoseManager : MonoBehaviour
{
    private SkeletonVisualizer visualizer;
    private AvatarController avatarController;

    void Start()
    {
        visualizer = FindObjectOfType<SkeletonVisualizer>();
        avatarController = FindObjectOfType<AvatarController>();
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
