using System;
using System.Collections.Generic;

[Serializable]
public class PoseData
{
    public int frame;

    public List<Landmark> landmarks;
}