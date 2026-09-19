using UnityEngine;

public class Bone : MonoBehaviour
{
    public Transform startPoint;
    public Transform endPoint;

    void Update()
    {
        if (startPoint == null || endPoint == null)
            return;

        Vector3 direction = endPoint.position - startPoint.position;

        transform.position = (startPoint.position + endPoint.position) * 0.5f;

        transform.up = direction.normalized;

        transform.localScale = new Vector3(
            0.03f,
            direction.magnitude * 0.5f,
            0.03f
        );
    }
}