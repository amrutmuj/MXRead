using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

public static class StickNoteMRSceneBootstrap
{
    private const string SceneFolder = "Assets/StickNoteMR/Scenes";
    private const string MaterialFolder = "Assets/StickNoteMR/Materials";
    private const string ScenePath = SceneFolder + "/StickNoteMRScene.unity";
    private const string PageMaterialPath = MaterialFolder + "/BookPageSurface.mat";
    private const string FrameMaterialPath = MaterialFolder + "/BookPageFrame.mat";
    private const string PointerMaterialPath = MaterialFolder + "/Pointer.mat";
    private const string CameraRigPrefabPath = "Packages/com.meta.xr.sdk.core/Prefabs/OVRCameraRig.prefab";
    private const float PageDepth = 0.0022f;
    private const string LegacyInteractorGuid = "8e83a4066dadacb4fa8f3fec2e32d3bf";
    private const string CurrentInteractorGuid = "02a52a354d4841d5ba9d547a1ce74f0a";
    private const string ActionButtonGuid = "0c9eb69604ac4fe0bc21298fbc19dca9";

    [InitializeOnLoadMethod]
    private static void Initialize()
    {
        EditorApplication.delayCall += EnsureScene;
    }

    [MenuItem("Tools/Stick Note MR/Rebuild Scene")]
    private static void RebuildSceneFromMenu()
    {
        CreateScene(forceRebuild: true);
    }

    public static void RebuildSceneFromCommandLine()
    {
        CreateScene(forceRebuild: true);
    }

    private static void EnsureScene()
    {
        SceneAsset existingScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
        if (existingScene == null)
        {
            CreateScene(forceRebuild: false);
            return;
        }

        string absoluteScenePath = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..", ScenePath));
        string sceneContents = System.IO.File.ReadAllText(absoluteScenePath);
        if (sceneContents.Contains(LegacyInteractorGuid) ||
            !sceneContents.Contains(CurrentInteractorGuid) ||
            !sceneContents.Contains(ActionButtonGuid) ||
            !sceneContents.Contains("PageWritingSurface") ||
            !sceneContents.Contains("PageToolPanel"))
        {
            CreateScene(forceRebuild: true);
        }
    }

    private static void CreateScene(bool forceRebuild)
    {
        try
        {
            Debug.Log($"Stick Note MR: creating scene at {ScenePath}.");

            EnsureFolder("Assets", "StickNoteMR");
            EnsureFolder("Assets/StickNoteMR", "Scenes");
            EnsureFolder("Assets/StickNoteMR", "Materials");

            if (forceRebuild && AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null)
            {
                AssetDatabase.DeleteAsset(ScenePath);
            }

            GameObject rigPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(CameraRigPrefabPath);
            if (rigPrefab == null)
            {
                Debug.LogWarning("Stick Note MR: Meta XR rig prefab is not ready yet. The scene will be created after package import finishes.");
                return;
            }

            Material pageMaterial = LoadOrCreateMaterial(PageMaterialPath, new Color(1f, 1f, 1f, 0f), true);
            Material frameMaterial = LoadOrCreateMaterial(FrameMaterialPath, new Color(0.75f, 0.44f, 0.12f, 0.96f), false);
            Material pointerMaterial = LoadOrCreateMaterial(PointerMaterialPath, new Color(0.24f, 0.91f, 1f, 0.92f), true);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            GameObject rigObject = (GameObject)PrefabUtility.InstantiatePrefab(rigPrefab);
            if (rigObject == null)
            {
                Debug.LogError("Stick Note MR: failed to instantiate OVRCameraRig prefab.");
                return;
            }

            rigObject.name = "OVRCameraRig";

            OVRManager manager = rigObject.GetComponent<OVRManager>();
            if (manager == null)
            {
                manager = rigObject.AddComponent<OVRManager>();
            }

            manager.isInsightPassthroughEnabled = true;
            manager.shouldBoundaryVisibilityBeSuppressed = true;
            manager.launchSimultaneousHandsControllersOnStartup = false;
            manager.SimultaneousHandsAndControllersEnabled = false;
            manager.controllerDrivenHandPosesType = OVRManager.ControllerDrivenHandPosesType.None;
            manager.trackingOriginType = OVRManager.TrackingOrigin.FloorLevel;

            Transform trackingSpace = EnsureChild(rigObject.transform, "TrackingSpace");

            OVRPassthroughLayer passthroughLayer = rigObject.GetComponent<OVRPassthroughLayer>();
            if (passthroughLayer == null)
            {
                passthroughLayer = rigObject.AddComponent<OVRPassthroughLayer>();
            }

            passthroughLayer.overlayType = OVROverlay.OverlayType.Underlay;
            passthroughLayer.projectionSurfaceType = OVRPassthroughLayer.ProjectionSurfaceType.Reconstructed;
            passthroughLayer.hidden = false;
            passthroughLayer.textureOpacity = 1f;

            Camera[] rigCameras = rigObject.GetComponentsInChildren<Camera>(true);
            for (int i = 0; i < rigCameras.Length; i++)
            {
                Camera rigCamera = rigCameras[i];
                rigCamera.clearFlags = CameraClearFlags.SolidColor;
                rigCamera.stereoTargetEye = StereoTargetEyeMask.Both;
                rigCamera.allowHDR = false;

                Color background = rigCamera.backgroundColor;
                background.a = 0f;
                rigCamera.backgroundColor = background;
            }

            RenderSettings.skybox = null;

            GameObject pageBoard = new GameObject("BookPageBoard");
            pageBoard.transform.position = new Vector3(0f, 1.24f, 0.55f);
            pageBoard.transform.rotation = Quaternion.Euler(8f, 180f, 0f);
            pageBoard.transform.localScale = Vector3.one * 1.2f;

            GameObject pageSurface = GameObject.CreatePrimitive(PrimitiveType.Cube);
            pageSurface.name = "BookPageSurface";
            pageSurface.transform.SetParent(pageBoard.transform, false);
            pageSurface.transform.localScale = new Vector3(0.24f, 0.34f, PageDepth);

            MeshRenderer pageRenderer = pageSurface.GetComponent<MeshRenderer>();
            pageRenderer.sharedMaterial = pageMaterial;
            pageRenderer.shadowCastingMode = ShadowCastingMode.Off;
            pageRenderer.receiveShadows = false;
            pageRenderer.enabled = false;

            BoxCollider pageCollider = pageSurface.GetComponent<BoxCollider>();
            pageCollider.size = new Vector3(1f, 1f, 10f);

            InkCanvas pageInkCanvas = pageSurface.AddComponent<InkCanvas>();
            pageInkCanvas.Configure(
                new Vector2(0.23f, 0.33f),
                0.02f,
                new Color(1f, 0.91f, 0.15f, 0.38f),
                0.014f,
                0.004f,
                "Book Highlight");

            GameObject pageWritingSurface = new GameObject("PageWritingSurface");
            pageWritingSurface.transform.SetParent(pageSurface.transform, false);
            pageWritingSurface.transform.localPosition = new Vector3(0f, 0f, PageDepth * 0.65f);
            InkCanvas pageWritingCanvas = pageWritingSurface.AddComponent<InkCanvas>();
            pageWritingCanvas.Configure(
                new Vector2(0.23f, 0.33f),
                0.018f,
                new Color(0.11f, 0.14f, 0.17f, 0.98f),
                0.0034f,
                0.0018f,
                "Book Writing");

            GameObject airInkRoot = new GameObject("AirInkRoot");
            airInkRoot.transform.SetParent(pageBoard.transform, false);
            airInkRoot.transform.localPosition = Vector3.zero;
            SpatialInkCanvas airInkCanvas = airInkRoot.AddComponent<SpatialInkCanvas>();
            airInkCanvas.Configure(
                new Color(0.10f, 0.16f, 0.23f, 0.98f),
                0.0045f,
                0.005f,
                "Air Ink");

            float frameOffset = PageDepth * 0.55f;
            CreateFrameSegment(pageBoard.transform, "TopFrame", new Vector3(0f, 0.172f, frameOffset), new Vector3(0.25f, 0.004f, 0.004f), frameMaterial);
            CreateFrameSegment(pageBoard.transform, "BottomFrame", new Vector3(0f, -0.172f, frameOffset), new Vector3(0.25f, 0.004f, 0.004f), frameMaterial);
            CreateFrameSegment(pageBoard.transform, "LeftFrame", new Vector3(-0.122f, 0f, frameOffset), new Vector3(0.004f, 0.348f, 0.004f), frameMaterial);
            CreateFrameSegment(pageBoard.transform, "RightFrame", new Vector3(0.122f, 0f, frameOffset), new Vector3(0.004f, 0.348f, 0.004f), frameMaterial);

            GameObject prototypeRoot = new GameObject("StickyNotePalette");
            prototypeRoot.transform.SetParent(pageBoard.transform, false);
            prototypeRoot.transform.localPosition = new Vector3(-0.27f, 0.02f, 0.028f);
            prototypeRoot.transform.localRotation = Quaternion.identity;

            StickyNoteTemplate[] templates =
            {
                StickyNoteFactory.CreateTemplateVisual(prototypeRoot.transform, "YellowSquareTemplate", new Vector3(0f, 0.13f, 0f), Quaternion.Euler(0f, 0f, 8f), "Yellow Square", "Idea", new Vector2(0.12f, 0.12f), new Color(0.96f, 0.89f, 0.44f, 1f), new Color(0.72f, 0.51f, 0.14f, 1f)),
                StickyNoteFactory.CreateTemplateVisual(prototypeRoot.transform, "PinkSquareTemplate", new Vector3(0.01f, 0.04f, 0f), Quaternion.Euler(0f, 0f, -6f), "Pink Square", "Quote", new Vector2(0.12f, 0.12f), new Color(0.97f, 0.70f, 0.78f, 1f), new Color(0.70f, 0.34f, 0.42f, 1f)),
                StickyNoteFactory.CreateTemplateVisual(prototypeRoot.transform, "BlueWideTemplate", new Vector3(-0.005f, -0.08f, 0f), Quaternion.Euler(0f, 0f, 4f), "Blue Wide", "Key Point", new Vector2(0.16f, 0.10f), new Color(0.68f, 0.86f, 0.97f, 1f), new Color(0.22f, 0.48f, 0.66f, 1f)),
                StickyNoteFactory.CreateTemplateVisual(prototypeRoot.transform, "GreenTabTemplate", new Vector3(0.015f, -0.19f, 0f), Quaternion.Euler(0f, 0f, -8f), "Green Tab", "Question", new Vector2(0.10f, 0.16f), new Color(0.74f, 0.90f, 0.61f, 1f), new Color(0.35f, 0.57f, 0.20f, 1f))
            };

            Transform[] noteAnchors =
            {
                CreateAnchor(pageBoard.transform, "AnchorTopLeft", new Vector3(-0.18f, 0.18f, 0.014f)),
                CreateAnchor(pageBoard.transform, "AnchorTopRight", new Vector3(0.18f, 0.18f, 0.014f)),
                CreateAnchor(pageBoard.transform, "AnchorRightMid", new Vector3(0.24f, 0.01f, 0.014f)),
                CreateAnchor(pageBoard.transform, "AnchorBottomRight", new Vector3(0.18f, -0.18f, 0.014f)),
                CreateAnchor(pageBoard.transform, "AnchorBottomLeft", new Vector3(-0.18f, -0.18f, 0.014f)),
                CreateAnchor(pageBoard.transform, "AnchorLeftMid", new Vector3(-0.24f, -0.02f, 0.014f))
            };

            StickNoteMRActionButton[] actionButtons =
            {
                CreateActionButton(pageBoard.transform, "PrevPageButton", new Vector3(0.26f, 0.11f, 0.018f), "Prev Page", StickNoteMRActionButton.ActionKind.PreviousPage),
                CreateActionButton(pageBoard.transform, "NextPageButton", new Vector3(0.26f, 0.06f, 0.018f), "Next Page", StickNoteMRActionButton.ActionKind.NextPage),
                CreateActionButton(pageBoard.transform, "HighlightToolButton", new Vector3(0.26f, 0.0f, 0.018f), "Highlight", StickNoteMRActionButton.ActionKind.UseHighlight),
                CreateActionButton(pageBoard.transform, "PenToolButton", new Vector3(0.26f, -0.05f, 0.018f), "Page Pen", StickNoteMRActionButton.ActionKind.UsePen),
                CreateActionButton(pageBoard.transform, "AirInkToolButton", new Vector3(0.26f, -0.10f, 0.018f), "Air Ink", StickNoteMRActionButton.ActionKind.UseAirInk),
                CreateActionButton(pageBoard.transform, "ClearPageButton", new Vector3(0.26f, -0.16f, 0.018f), "Clear Page", StickNoteMRActionButton.ActionKind.ClearCurrentPage),
                CreateActionButton(pageBoard.transform, "ResetBoardButton", new Vector3(0.26f, -0.21f, 0.018f), "Reset Frame", StickNoteMRActionButton.ActionKind.ResetBoard)
            };

            GameObject toolPanel = new GameObject("PageToolPanel");
            toolPanel.transform.SetParent(pageBoard.transform, false);
            toolPanel.transform.localPosition = new Vector3(0.26f, -0.05f, 0.014f);

            GameObject stickyNoteWorldRoot = new GameObject("StickyNoteWorldRoot");
            stickyNoteWorldRoot.transform.SetParent(trackingSpace, false);
            stickyNoteWorldRoot.transform.localPosition = Vector3.zero;

            GameObject controllerBeamObject = new GameObject("ControllerBeam");
            LineRenderer beam = controllerBeamObject.AddComponent<LineRenderer>();
            beam.useWorldSpace = true;
            beam.positionCount = 2;
            beam.startWidth = 0.003f;
            beam.endWidth = 0.001f;
            beam.numCapVertices = 6;
            beam.material = pointerMaterial;
            beam.startColor = new Color(0.2f, 0.93f, 1f, 0.92f);
            beam.endColor = new Color(0.2f, 0.93f, 1f, 0.08f);

            GameObject reticle = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            reticle.name = "ControllerReticle";
            reticle.transform.localScale = Vector3.one * 0.012f;
            reticle.GetComponent<MeshRenderer>().sharedMaterial = pointerMaterial;
            Object.DestroyImmediate(reticle.GetComponent<SphereCollider>());

            GameObject instructions = new GameObject("Instructions");
            instructions.transform.SetParent(pageBoard.transform, false);
            instructions.transform.localPosition = new Vector3(0.25f, 0.16f, 0.006f);
            instructions.transform.localRotation = Quaternion.identity;
            TextMesh textMesh = instructions.AddComponent<TextMesh>();
            textMesh.fontSize = 40;
            textMesh.characterSize = 0.0032f;
            textMesh.anchor = TextAnchor.UpperLeft;
            textMesh.alignment = TextAlignment.Left;
            textMesh.color = new Color(1f, 0.98f, 0.95f, 0.95f);
            textMesh.text = "Stick Note MR";

            GameObject interactionRoot = new GameObject("StickNoteInteractor");
            StickNoteMRBookInteractor interactor = interactionRoot.AddComponent<StickNoteMRBookInteractor>();

            Assign(interactor, "cameraRig", rigObject.GetComponent<OVRCameraRig>());
            Assign(interactor, "pageHighlightCanvas", pageInkCanvas);
            Assign(interactor, "pageWritingCanvas", pageWritingCanvas);
            Assign(interactor, "pageSurfaceCollider", pageCollider);
            Assign(interactor, "airInkCanvas", airInkCanvas);
            Assign(interactor, "pageBoard", pageBoard.transform);
            Assign(interactor, "noteWorkspaceRoot", stickyNoteWorldRoot.transform);
            Assign(interactor, "statusText", textMesh);
            AssignArray(interactor, "stickyTemplates", templates);
            AssignArray(interactor, "noteAnchors", noteAnchors);
            AssignArray(interactor, "actionButtons", actionButtons);
            Assign(interactor, "controllerBeam", beam);
            Assign(interactor, "controllerReticle", reticle.transform);

            if (!EditorSceneManager.SaveScene(scene, ScenePath))
            {
                Debug.LogError($"Stick Note MR: failed to save scene at {ScenePath}.");
                return;
            }

            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(ScenePath, true)
            };

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            EditorSceneManager.OpenScene(ScenePath);
            Debug.Log($"Stick Note MR: scene created successfully at {ScenePath}.");
        }
        catch (System.Exception exception)
        {
            Debug.LogException(exception);
        }
    }

    private static Transform CreateAnchor(Transform parent, string name, Vector3 localPosition)
    {
        GameObject anchor = new GameObject(name);
        anchor.transform.SetParent(parent, false);
        anchor.transform.localPosition = localPosition;
        anchor.transform.localRotation = Quaternion.identity;
        return anchor.transform;
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
        Object.DestroyImmediate(segment.GetComponent<BoxCollider>());
    }

    private static StickNoteMRActionButton CreateActionButton(
        Transform parent,
        string name,
        Vector3 localPosition,
        string label,
        StickNoteMRActionButton.ActionKind action)
    {
        GameObject buttonObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
        buttonObject.name = name;
        buttonObject.transform.SetParent(parent, false);
        buttonObject.transform.localPosition = localPosition;
        buttonObject.transform.localRotation = Quaternion.identity;
        buttonObject.transform.localScale = new Vector3(0.095f, 0.034f, 0.008f);

        MeshRenderer renderer = buttonObject.GetComponent<MeshRenderer>();
        Shader shader = Shader.Find("Unlit/Color");
        if (shader == null)
        {
            shader = Shader.Find("Standard");
        }

        renderer.sharedMaterial = new Material(shader)
        {
            color = new Color(0.30f, 0.35f, 0.43f, 0.92f)
        };
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;

        GameObject labelObject = new GameObject("Label");
        labelObject.transform.SetParent(buttonObject.transform, false);
        labelObject.transform.localPosition = new Vector3(-0.04f, 0.008f, 0.005f);
        labelObject.transform.localRotation = Quaternion.identity;

        TextMesh textMesh = labelObject.AddComponent<TextMesh>();
        textMesh.fontSize = 34;
        textMesh.characterSize = 0.003f;
        textMesh.anchor = TextAnchor.MiddleLeft;
        textMesh.alignment = TextAlignment.Left;
        textMesh.color = new Color(0.98f, 0.98f, 0.96f, 0.98f);
        textMesh.text = label;

        StickNoteMRActionButton button = buttonObject.AddComponent<StickNoteMRActionButton>();
        button.Configure(action, label, renderer, textMesh);
        return button;
    }

    private static Material LoadOrCreateMaterial(string path, Color color, bool transparent)
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);

        Shader shader = Shader.Find("Standard");
        if (shader == null)
        {
            shader = Shader.Find("Unlit/Color");
        }

        if (material == null)
        {
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, path);
        }

        if (material.shader != shader)
        {
            material.shader = shader;
        }

        material.color = color;

        if (transparent && shader.name == "Standard")
        {
            material.SetFloat("_Mode", 3f);
            material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetInt("_ZWrite", 0);
            material.DisableKeyword("_ALPHATEST_ON");
            material.EnableKeyword("_ALPHABLEND_ON");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.renderQueue = 3000;
        }

        if (material.HasProperty("_Cull"))
        {
            material.SetInt("_Cull", (int)CullMode.Off);
        }

        EditorUtility.SetDirty(material);
        return material;
    }

    private static void EnsureFolder(string parent, string child)
    {
        string path = parent + "/" + child;

        if (!AssetDatabase.IsValidFolder(path))
        {
            AssetDatabase.CreateFolder(parent, child);
        }
    }

    private static Transform EnsureChild(Transform parent, string childName)
    {
        Transform child = parent.Find(childName);
        if (child != null)
        {
            return child;
        }

        GameObject childObject = new GameObject(childName);
        childObject.transform.SetParent(parent, false);
        return childObject.transform;
    }

    private static void Assign(Object target, string fieldName, Object value)
    {
        SerializedObject serializedObject = new SerializedObject(target);
        SerializedProperty property = serializedObject.FindProperty(fieldName);
        if (property == null)
        {
            Debug.LogError($"Stick Note MR: could not find serialized field '{fieldName}' on {target.name}.");
            return;
        }

        property.objectReferenceValue = value;
        serializedObject.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(target);
    }

    private static void AssignArray(Object target, string fieldName, Object[] values)
    {
        SerializedObject serializedObject = new SerializedObject(target);
        SerializedProperty property = serializedObject.FindProperty(fieldName);
        if (property == null)
        {
            Debug.LogError($"Stick Note MR: could not find serialized array field '{fieldName}' on {target.name}.");
            return;
        }

        property.arraySize = values.Length;

        for (int i = 0; i < values.Length; i++)
        {
            property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        }

        serializedObject.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(target);
    }
}
