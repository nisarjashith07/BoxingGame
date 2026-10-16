using System;
using System.Collections.Generic;

[Serializable]
public class PoseData
{
    public int frame;

    // Primary 3D fused landmarks (Front X/Y + Side Z Depth)
    public List<Landmark> landmarks;

    // Side camera landmarks aligned to player space
    public List<Landmark> landmarks_right;

    // Raw front camera landmarks
    public List<Landmark> landmarks_front;

    // Real-time boxing punch telemetry
    public float left_punch_speed;
    public float right_punch_speed;
    public string left_punch_state;
    public string right_punch_state;
    public string punch_type;
}