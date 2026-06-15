using UnityEngine;

[RequireComponent(typeof(LineRenderer))]
public class PageOutline : MonoBehaviour
{
    [Header("Page Dimensions (in meters)")]
    // 19 cm = 0.19m, 27 cm = 0.27m
    public float pageWidth = 0.19f;
    public float pageHeight = 0.27f;

    [Header("QR Padding (Distance from Top-Right Edge)")]
    // How far inside the box the QR code should sit
    public float paddingRight = 0.02f; // 2 cm from the right edge
    public float paddingTop = 0.02f;   // 2 cm from the top edge

    private LineRenderer lineRenderer;

    void Start()
    {
        lineRenderer = GetComponent<LineRenderer>();

        // Force the settings via code
        lineRenderer.positionCount = 4;
        lineRenderer.loop = true;
        lineRenderer.useWorldSpace = true;
        lineRenderer.startWidth = 0.005f;
        lineRenderer.endWidth = 0.005f;
    }

    void Update()
    {
        DrawBoundary();
    }

    private void DrawBoundary()
    {
        // 1. Find the TRUE Top-Right corner of the page.
        // We know +transform.right goes LEFT, so -transform.right goes RIGHT (towards the edge).
        // We know -transform.up goes DOWN, so +transform.up goes UP (towards the edge).
        Vector3 trueTopRight = transform.position
                             - (transform.right * paddingRight)
                             + (transform.up * paddingTop);

        // 2. Calculate the other 3 corners relative to the TRUE Top-Right corner
        Vector3 topLeft = trueTopRight + (transform.right * pageWidth);
        Vector3 bottomRight = trueTopRight - (transform.up * pageHeight);
        Vector3 bottomLeft = trueTopRight + (transform.right * pageWidth) - (transform.up * pageHeight);

        // 3. Feed these 4 corners into the Line Renderer to draw the box
        lineRenderer.SetPosition(0, trueTopRight);
        lineRenderer.SetPosition(1, topLeft);
        lineRenderer.SetPosition(2, bottomLeft);
        lineRenderer.SetPosition(3, bottomRight);
    }
}