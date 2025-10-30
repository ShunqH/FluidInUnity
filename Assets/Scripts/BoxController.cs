using UnityEngine;

public class BoxController : MonoBehaviour
{
    public Vector3 minBound = new Vector3 (-1f, 0f, -1f);
    public Vector3 maxBound = new Vector3 (1f, 2f, 1f);

    public void UpdateBounds()
    {
        Vector3 center = transform.position;
        Vector3 size = transform.localScale;

        minBound = center - size * 0.5f;
        maxBound = center + size * 0.5f;
    }

    // Update is called once per frame
    void Update()
    {
        UpdateBounds();
    }
}
