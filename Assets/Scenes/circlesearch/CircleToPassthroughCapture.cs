using System.Collections.Generic;
using UnityEngine;
using Meta.XR;

public class CircleToPassthroughCapture : MonoBehaviour
{
    public WritingPenDrawing pen;
    public Camera mainCamera;
    public PassthroughCameraAccess passthrough;
    public int padding = 20;

    public Texture2D CaptureSquare()
    {
        List<Vector3> worldPoints = pen.GetDrawnPoints();

        if (worldPoints == null || worldPoints.Count < 10)
            return null;

        Texture source = passthrough.GetTexture();
        if (source == null)
            return null;

        List<Vector2> screenPoints = new List<Vector2>();

        foreach (var p in worldPoints)
        {
            Vector3 sp = mainCamera.WorldToScreenPoint(p);
            if (sp.z > 0)
                screenPoints.Add(new Vector2(sp.x, sp.y));
        }

        if (screenPoints.Count == 0)
            return null;

        float minX = float.MaxValue;
        float minY = float.MaxValue;
        float maxX = float.MinValue;
        float maxY = float.MinValue;

        foreach (var p in screenPoints)
        {
            minX = Mathf.Min(minX, p.x);
            minY = Mathf.Min(minY, p.y);
            maxX = Mathf.Max(maxX, p.x);
            maxY = Mathf.Max(maxY, p.y);
        }

        float width = maxX - minX;
        float height = maxY - minY;
        float size = Mathf.Max(width, height);

        float centerX = (minX + maxX) * 0.5f;
        float centerY = (minY + maxY) * 0.5f;

        float x = centerX - size * 0.5f - padding;
        float y = centerY - size * 0.5f - padding;
        float w = size + padding * 2;
        float h = size + padding * 2;

        x = Mathf.Clamp(x, 0, source.width);
        y = Mathf.Clamp(y, 0, source.height);
        w = Mathf.Clamp(w, 0, source.width - x);
        h = Mathf.Clamp(h, 0, source.height - y);

        Rect rect = new Rect(x, y, w, h);
        rect.y = source.height - rect.y - rect.height;

        RenderTexture rt = RenderTexture.GetTemporary(source.width, source.height, 0);
        Graphics.Blit(source, rt);

        RenderTexture.active = rt;

        Texture2D tex = new Texture2D((int)rect.width, (int)rect.height, TextureFormat.RGB24, false);
        tex.ReadPixels(rect, 0, 0);
        tex.Apply();

        RenderTexture.active = null;
        RenderTexture.ReleaseTemporary(rt);

        return tex;
    }
}