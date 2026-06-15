using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public class SpatialInkCanvas : MonoBehaviour
{
    private sealed class StrokeRecord
    {
        public GameObject StrokeObject;
        public int PageId;
    }

    [SerializeField] private Color strokeColor = new Color(0.11f, 0.14f, 0.17f, 0.98f);
    [SerializeField] private float strokeWidth = 0.004f;
    [SerializeField] private float minPointSpacing = 0.006f;
    [SerializeField] private string strokeMaterialName = "Air Ink";

    private readonly List<StrokeRecord> strokeRecords = new List<StrokeRecord>();
    private readonly List<Vector3> activePoints = new List<Vector3>();

    private LineRenderer activeStroke;
    private Material strokeMaterial;
    private int activeStrokePageId = 1;
    private int visiblePageId = 1;

    private void Awake()
    {
        EnsureMaterial();
    }

    public void Configure(Color color, float width, float pointSpacing, string materialName)
    {
        strokeColor = color;
        strokeWidth = width;
        minPointSpacing = pointSpacing;
        strokeMaterialName = materialName;
        EnsureMaterial(forceRecreate: true);
    }

    public void BeginStroke(Vector3 worldPoint, int pageId = 1)
    {
        if (activeStroke != null)
        {
            return;
        }

        activeStrokePageId = Mathf.Max(1, pageId);
        activeStroke = CreateStrokeRenderer();
        activePoints.Clear();
        AddPoint(worldPoint, force: true);
    }

    public void AppendStroke(Vector3 worldPoint)
    {
        if (activeStroke == null)
        {
            BeginStroke(worldPoint, activeStrokePageId);
            return;
        }

        AddPoint(worldPoint, force: false);
    }

    public void EndStroke()
    {
        if (activeStroke == null)
        {
            return;
        }

        if (activePoints.Count == 1)
        {
            AddPoint(transform.TransformPoint(activePoints[0] + Vector3.right * 0.0025f), force: true);
        }

        activeStroke = null;
        activePoints.Clear();
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

    private void AddPoint(Vector3 worldPoint, bool force)
    {
        Vector3 localPoint = transform.InverseTransformPoint(worldPoint);
        if (!force && activePoints.Count > 0)
        {
            if (Vector3.Distance(activePoints[activePoints.Count - 1], localPoint) < minPointSpacing)
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

        GameObject strokeObject = new GameObject("Air Ink Stroke " + (strokeRecords.Count + 1));
        strokeObject.transform.SetParent(transform, false);

        LineRenderer renderer = strokeObject.AddComponent<LineRenderer>();
        renderer.useWorldSpace = false;
        renderer.alignment = LineAlignment.TransformZ;
        renderer.textureMode = LineTextureMode.Stretch;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.numCapVertices = 8;
        renderer.numCornerVertices = 6;
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
