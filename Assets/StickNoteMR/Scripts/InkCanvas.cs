using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public class InkCanvas : MonoBehaviour
{
    private sealed class StrokeRecord
    {
        public GameObject StrokeObject;
        public int PageId;
    }

    [SerializeField] private Vector2 surfaceSizeMeters = new Vector2(0.12f, 0.12f);
    [SerializeField] private float touchDistance = 0.018f;
    [SerializeField] private Color strokeColor = Color.black;
    [SerializeField] private float strokeWidth = 0.003f;
    [SerializeField] private float minPointSpacing = 0.002f;
    [SerializeField] private string strokeMaterialName = "Ink Stroke Material";

    private readonly List<StrokeRecord> strokeRecords = new List<StrokeRecord>();
    private readonly List<Vector3> activePoints = new List<Vector3>();

    private LineRenderer activeStroke;
    private Material strokeMaterial;
    private int activeStrokePageId = 1;
    private int visiblePageId = 1;

    public float TouchDistance => touchDistance;

    private void Awake()
    {
        EnsureMaterial();
    }

    public void Configure(
        Vector2 surfaceSize,
        float maxTouchDistance,
        Color color,
        float width,
        float pointSpacing,
        string materialName)
    {
        surfaceSizeMeters = surfaceSize;
        touchDistance = maxTouchDistance;
        strokeColor = color;
        strokeWidth = width;
        minPointSpacing = pointSpacing;
        strokeMaterialName = materialName;

        EnsureMaterial(forceRecreate: true);
    }

    public bool TryProjectRay(Ray ray, out Vector3 localPoint, out Vector3 worldPoint)
    {
        Plane plane = new Plane(transform.forward, transform.position);

        if (!plane.Raycast(ray, out float enter))
        {
            localPoint = default;
            worldPoint = default;
            return false;
        }

        worldPoint = ray.GetPoint(enter);
        return TryProjectTouchPoint(worldPoint, touchDistance * 3f, out localPoint);
    }

    public bool TryProjectTouchPoint(Vector3 worldPoint, float maxDistance, out Vector3 localPoint)
    {
        Vector3 candidate = transform.InverseTransformPoint(worldPoint);

        if (Mathf.Abs(candidate.z) > maxDistance)
        {
            localPoint = default;
            return false;
        }

        float halfWidth = surfaceSizeMeters.x * 0.5f;
        float halfHeight = surfaceSizeMeters.y * 0.5f;

        if (candidate.x < -halfWidth || candidate.x > halfWidth || candidate.y < -halfHeight || candidate.y > halfHeight)
        {
            localPoint = default;
            return false;
        }

        localPoint = new Vector3(candidate.x, candidate.y, 0.0012f);
        return true;
    }

    public void BeginStroke(Vector3 localPoint, int pageId = 1)
    {
        if (activeStroke != null)
        {
            return;
        }

        activeStrokePageId = Mathf.Max(1, pageId);
        activeStroke = CreateStrokeRenderer();
        activePoints.Clear();
        AddPoint(localPoint, force: true);
    }

    public void AppendStroke(Vector3 localPoint)
    {
        if (activeStroke == null)
        {
            BeginStroke(localPoint);
            return;
        }

        AddPoint(localPoint, force: false);
    }

    public void EndStroke()
    {
        if (activeStroke == null)
        {
            return;
        }

        if (activePoints.Count == 1)
        {
            AddPoint(activePoints[0] + Vector3.right * 0.001f, force: true);
        }

        activeStroke = null;
        activePoints.Clear();
    }

    public void ClearAll()
    {
        EndStroke();

        for (int i = 0; i < strokeRecords.Count; i++)
        {
            if (strokeRecords[i].StrokeObject != null)
            {
                Destroy(strokeRecords[i].StrokeObject);
            }
        }

        strokeRecords.Clear();
    }

    public void ClearPage(int pageId)
    {
        pageId = Mathf.Max(1, pageId);

        if (activeStroke != null && activeStrokePageId == pageId)
        {
            EndStroke();
        }

        for (int i = strokeRecords.Count - 1; i >= 0; i--)
        {
            if (strokeRecords[i].PageId != pageId)
            {
                continue;
            }

            if (strokeRecords[i].StrokeObject != null)
            {
                Destroy(strokeRecords[i].StrokeObject);
            }

            strokeRecords.RemoveAt(i);
        }
    }

    public void SetVisiblePage(int pageId)
    {
        visiblePageId = Mathf.Max(1, pageId);

        for (int i = 0; i < strokeRecords.Count; i++)
        {
            GameObject strokeObject = strokeRecords[i].StrokeObject;
            if (strokeObject == null)
            {
                continue;
            }

            bool isVisible = strokeRecords[i].PageId == visiblePageId;
            if (strokeObject.activeSelf != isVisible)
            {
                strokeObject.SetActive(isVisible);
            }
        }
    }

    private void AddPoint(Vector3 localPoint, bool force)
    {
        if (!force && activePoints.Count > 0)
        {
            Vector3 previous = activePoints[activePoints.Count - 1];
            if (Vector3.Distance(previous, localPoint) < minPointSpacing)
            {
                return;
            }
        }

        activePoints.Add(localPoint);
        activeStroke.positionCount = activePoints.Count;
        activeStroke.SetPositions(activePoints.ToArray());
    }

    private LineRenderer CreateStrokeRenderer()
    {
        EnsureMaterial();

        GameObject strokeObject = new GameObject("Ink Stroke " + (strokeRecords.Count + 1));
        strokeObject.transform.SetParent(transform, false);

        LineRenderer renderer = strokeObject.AddComponent<LineRenderer>();
        renderer.useWorldSpace = false;
        renderer.alignment = LineAlignment.TransformZ;
        renderer.textureMode = LineTextureMode.Stretch;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.numCapVertices = 8;
        renderer.numCornerVertices = 4;
        renderer.startWidth = strokeWidth;
        renderer.endWidth = strokeWidth;
        renderer.material = strokeMaterial;
        renderer.startColor = strokeColor;
        renderer.endColor = strokeColor;

        strokeRecords.Add(new StrokeRecord
        {
            StrokeObject = strokeObject,
            PageId = activeStrokePageId
        });

        strokeObject.SetActive(activeStrokePageId == visiblePageId);
        return renderer;
    }

    private void EnsureMaterial(bool forceRecreate = false)
    {
        if (strokeMaterial != null && !forceRecreate)
        {
            return;
        }

        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null)
        {
            shader = Shader.Find("Unlit/Color");
        }

        strokeMaterial = new Material(shader)
        {
            name = strokeMaterialName,
            color = strokeColor
        };
    }
}
