using UnityEngine;
using UnityEngine.Rendering;

public static class StickyNoteFactory
{
    private const float NoteDepth = 0.0026f;

    public static StickyNoteTemplate CreateTemplateVisual(
        Transform parent,
        string objectName,
        Vector3 localPosition,
        Quaternion localRotation,
        string templateName,
        string shortLabel,
        Vector2 noteSizeMeters,
        Color noteColor,
        Color frameColor)
    {
        GameObject root = new GameObject(objectName);
        root.transform.SetParent(parent, false);
        root.transform.localPosition = localPosition;
        root.transform.localRotation = localRotation;

        StickyNoteTemplate template = root.AddComponent<StickyNoteTemplate>();
        template.Configure(templateName, shortLabel, noteSizeMeters, noteColor, frameColor);

        CreateNoteVisual(root.transform, noteSizeMeters, noteColor, frameColor, shortLabel, writable: false, out _, out _);
        return template;
    }

    public static StickyNoteSurface CreatePlacedNote(StickyNoteTemplate template, Transform parent, int noteNumber)
    {
        GameObject root = new GameObject(template.TemplateName + " Note " + noteNumber);
        root.transform.SetParent(parent, false);
        root.transform.localPosition = Vector3.zero;
        root.transform.localRotation = Quaternion.identity;

        CreateNoteVisual(
            root.transform,
            template.NoteSizeMeters,
            template.NoteColor,
            template.FrameColor,
            template.ShortLabel,
            writable: true,
            out InkCanvas inkCanvas,
            out TextMesh titleText);

        StickyNoteSurface surface = root.AddComponent<StickyNoteSurface>();
        surface.Configure(template, inkCanvas, titleText);
        return surface;
    }

    private static void CreateNoteVisual(
        Transform root,
        Vector2 noteSizeMeters,
        Color noteColor,
        Color frameColor,
        string label,
        bool writable,
        out InkCanvas inkCanvas,
        out TextMesh titleText)
    {
        Material noteMaterial = CreateFlatMaterial(label + " Face", noteColor);
        Material frameMaterial = CreateFlatMaterial(label + " Frame", frameColor);

        GameObject face = GameObject.CreatePrimitive(PrimitiveType.Cube);
        face.name = "NoteFace";
        face.transform.SetParent(root, false);
        face.transform.localPosition = Vector3.zero;
        face.transform.localRotation = Quaternion.identity;
        face.transform.localScale = new Vector3(noteSizeMeters.x, noteSizeMeters.y, NoteDepth);

        MeshRenderer faceRenderer = face.GetComponent<MeshRenderer>();
        faceRenderer.sharedMaterial = noteMaterial;
        faceRenderer.shadowCastingMode = ShadowCastingMode.Off;
        faceRenderer.receiveShadows = false;

        BoxCollider faceCollider = face.GetComponent<BoxCollider>();
        faceCollider.size = new Vector3(1f, 1f, 8f);

        float frontOffset = NoteDepth * 0.55f;

        CreateFrameSegment(root, "TopFrame", new Vector3(0f, noteSizeMeters.y * 0.5f, frontOffset), new Vector3(noteSizeMeters.x + 0.006f, 0.004f, NoteDepth), frameMaterial);
        CreateFrameSegment(root, "BottomFrame", new Vector3(0f, -noteSizeMeters.y * 0.5f, frontOffset), new Vector3(noteSizeMeters.x + 0.006f, 0.004f, NoteDepth), frameMaterial);
        CreateFrameSegment(root, "LeftFrame", new Vector3(-noteSizeMeters.x * 0.5f, 0f, frontOffset), new Vector3(0.004f, noteSizeMeters.y + 0.006f, NoteDepth), frameMaterial);
        CreateFrameSegment(root, "RightFrame", new Vector3(noteSizeMeters.x * 0.5f, 0f, frontOffset), new Vector3(0.004f, noteSizeMeters.y + 0.006f, NoteDepth), frameMaterial);

        titleText = CreateTitleText(root, label, noteSizeMeters);

        inkCanvas = null;
        if (writable)
        {
            inkCanvas = face.AddComponent<InkCanvas>();
            inkCanvas.Configure(
                noteSizeMeters * 0.92f,
                0.02f,
                new Color(0.15f, 0.12f, 0.11f, 1f),
                0.0024f,
                0.0014f,
                label + " Ink");
        }
    }

    private static TextMesh CreateTitleText(Transform root, string label, Vector2 noteSizeMeters)
    {
        GameObject textObject = new GameObject("Title");
        textObject.transform.SetParent(root, false);
        textObject.transform.localPosition = new Vector3(-noteSizeMeters.x * 0.38f, noteSizeMeters.y * 0.34f, NoteDepth * 0.9f);
        textObject.transform.localRotation = Quaternion.identity;

        TextMesh textMesh = textObject.AddComponent<TextMesh>();
        textMesh.fontSize = 28;
        textMesh.characterSize = Mathf.Min(noteSizeMeters.x, noteSizeMeters.y) * 0.021f;
        textMesh.anchor = TextAnchor.UpperLeft;
        textMesh.alignment = TextAlignment.Left;
        textMesh.color = new Color(0.21f, 0.18f, 0.14f, 0.95f);
        textMesh.text = label;
        return textMesh;
    }

    private static void CreateFrameSegment(Transform parent, string name, Vector3 localPosition, Vector3 localScale, Material material)
    {
        GameObject segment = GameObject.CreatePrimitive(PrimitiveType.Cube);
        segment.name = name;
        segment.transform.SetParent(parent, false);
        segment.transform.localPosition = localPosition;
        segment.transform.localScale = localScale;
        MeshRenderer renderer = segment.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        DestroyForCurrentMode(segment.GetComponent<BoxCollider>());
    }

    private static Material CreateFlatMaterial(string materialName, Color color)
    {
        Shader shader = Shader.Find("Unlit/Color");
        if (shader == null)
        {
            shader = Shader.Find("Standard");
        }

        Material material = new Material(shader)
        {
            name = materialName,
            color = color
        };

        if (material.HasProperty("_Cull"))
        {
            material.SetInt("_Cull", (int)CullMode.Off);
        }

        return material;
    }

    private static void DestroyForCurrentMode(Object target)
    {
        if (target == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            Object.Destroy(target);
            return;
        }

        Object.DestroyImmediate(target);
    }
}
