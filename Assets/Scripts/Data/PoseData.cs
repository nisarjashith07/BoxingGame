using System;
using System.Collections.Generic;

[Serializable]
public class PoseData
{
    public int frame;

    // Monotonic capture timestamp in seconds from the Python tracker
    public double timestamp;

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

    // Real-world physical body displacement (meters) & footwork velocity (m/s)
    public float body_offset_x;
    public float body_offset_y;
    public float body_offset_z;
    public float body_vel_x;
    public float body_vel_y;
    public float body_vel_z;
}
