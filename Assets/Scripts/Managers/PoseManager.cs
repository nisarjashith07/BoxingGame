using UnityEngine;
using BoxingAI;

public class PoseManager : MonoBehaviour
{
    [Header("Tracking Reliability")]
    [Tooltip("Seconds without incoming pose packets before tracking is marked lost and neutralized.")]
    [SerializeField] private float poseTimeoutSeconds = 0.5f;

    /// <summary>
    /// The most recently parsed pose data, or null when tracking is inactive/lost.
    /// </summary>
    public static PoseData LatestPose { get; private set; }

    /// <summary>
    /// True while fresh pose packets have arrived within poseTimeoutSeconds.
    /// </summary>
    public static bool IsTrackingActive { get; private set; }

    /// <summary>
    /// Unscaled Unity timestamp of the most recent valid pose packet.
    /// </summary>
    public static float LastPacketRealtime { get; private set; }

    private SkeletonVisualizer visualizer;
    private AvatarController avatarController;
    private PunchDetector punchDetector;

    void Start()
    {
        ResolveListeners();
    }

    private void ResolveListeners()
    {
        if (visualizer == null)
            visualizer = FindAnyObjectByType<SkeletonVisualizer>();
        if (avatarController == null)
            avatarController = FindAnyObjectByType<AvatarController>();
        if (punchDetector == null)
            punchDetector = FindAnyObjectByType<PunchDetector>();
    }

    void Update()
    {
        if (UDPReceiver.HasNewJson && !string.IsNullOrEmpty(UDPReceiver.LatestJson))
        {
            string json = UDPReceiver.LatestJson;
            UDPReceiver.HasNewJson = false;
            TryIngestJson(json, Time.unscaledTime);
            return;
        }

        CheckTrackingTimeout(Time.unscaledTime);
    }

    /// <summary>
    /// Parses a pose JSON packet, rejects malformed or duplicate frames, and dispatches to listeners.
    /// Exposed publicly for deterministic automated testing.
    /// </summary>
    public bool TryIngestJson(string json, float currentRealtime)
    {
        if (string.IsNullOrEmpty(json))
            return false;

        try
        {
            PoseData pose = JsonUtility.FromJson<PoseData>(json);
            if (pose == null || pose.landmarks == null || pose.landmarks.Count != 33)
                return false;

            // Ignore duplicate packets with the exact same positive frame counter
            if (LatestPose != null && pose.frame > 0 && pose.frame == LatestPose.frame)
                return false;

            LatestPose = pose;
            LastPacketRealtime = currentRealtime;
            IsTrackingActive = true;

            ResolveListeners();
            if (visualizer != null) visualizer.UpdateLandmarks(pose);
            if (avatarController != null) avatarController.UpdatePose(pose);
            if (punchDetector != null) punchDetector.ProcessPose(pose);
            return true;
        }
        catch
        {
            // Ignore malformed packets
            return false;
        }
    }

    public void CheckTrackingTimeout(float currentRealtime)
    {
        if (IsTrackingActive && (currentRealtime - LastPacketRealtime) > poseTimeoutSeconds)
        {
            ClearTrackingState();
        }
    }

    public void ClearTrackingState()
    {
        IsTrackingActive = false;
        LatestPose = null;

        ResolveListeners();
        if (avatarController != null)
            avatarController.UpdatePose(null);
        if (punchDetector != null)
            punchDetector.ClearPoseTracking();
    }

    void OnDisable()
    {
        LatestPose = null;
        IsTrackingActive = false;
    }

    void OnDestroy()
    {
        LatestPose = null;
        IsTrackingActive = false;
    }
}
