using UnityEngine;

public class StickNoteMRInteractor : MonoBehaviour
{
    [SerializeField] private OVRCameraRig cameraRig;
    [SerializeField] private InkCanvas pageHighlightCanvas;
    [SerializeField] private Transform pageBoard;
    [SerializeField] private TextMesh statusText;
    [SerializeField] private StickyNoteTemplate[] stickyTemplates;
    [SerializeField] private Transform[] noteAnchors;
    [SerializeField] private LineRenderer controllerBeam;
    [SerializeField] private Transform controllerReticle;
    [SerializeField] private float beamLength = 0.9f;
    [SerializeField] private float moveSpeed = 0.22f;
    [SerializeField] private float depthSpeed = 0.22f;
    [SerializeField] private float rotationSpeed = 70f;
    [SerializeField] private float scaleSpeed = 0.22f;

    private InkCanvas activeCanvas;
    private StickyNoteTemplate hoveredTemplate;
    private StickyNoteSurface hoveredNote;
    private InkCanvas hoveredCanvas;
    private Vector3 hoveredLocalPoint;
    private Vector3 hoveredWorldPoint;
    private bool hasProjectedHoverPoint;
    private int selectedTemplateIndex;
    private int nextAnchorIndex;
    private int noteSerialNumber = 1;
    private StickyNoteSurface[] placedNotes;
    private string transientStatus;
    private float transientStatusUntil;

    private void Start()
    {
        if (noteAnchors != null && noteAnchors.Length > 0)
        {
            placedNotes = new StickyNoteSurface[noteAnchors.Length];
        }

        ConfigureMixedRealityView();
        ResetBoardPose();
        RefreshTemplateSelection();
    }

    private void Update()
    {
        if (cameraRig == null || pageHighlightCanvas == null || pageBoard == null)
        {
            return;
        }

        UpdateHoverState();
        HandleTemplateInputs();
        HandleBoardCalibration();
        HandleDrawing();
        UpdateStatusText();
    }

    private void HandleTemplateInputs()
    {
        if (stickyTemplates == null || stickyTemplates.Length == 0)
        {
            return;
        }

        if (OVRInput.GetDown(OVRInput.Button.One))
        {
            CycleTemplate(-1);
        }

        if (OVRInput.GetDown(OVRInput.Button.Two))
        {
            CycleTemplate(1);
        }

        if (OVRInput.GetDown(OVRInput.Button.Three))
        {
            if (hoveredTemplate != null)
            {
                SelectTemplate(hoveredTemplate);
            }

            SpawnSelectedStickyNote();
        }

        if (OVRInput.GetDown(OVRInput.Button.Four))
        {
            if (hoveredNote != null)
            {
                RemoveNote(hoveredNote);
                return;
            }

            pageHighlightCanvas.ClearAll();
            ShowTransientStatus("Highlights cleared.");
        }
    }

    private void HandleBoardCalibration()
    {
        Transform head = cameraRig.centerEyeAnchor;
        Vector2 leftStick = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick);
        Vector2 rightStick = OVRInput.Get(OVRInput.Axis2D.SecondaryThumbstick);
        bool leftGrip = OVRInput.Get(OVRInput.Axis1D.PrimaryHandTrigger) > 0.55f;
        bool rightGrip = OVRInput.Get(OVRInput.Axis1D.SecondaryHandTrigger) > 0.55f;

        if (!leftGrip)
        {
            Vector3 lateral = head.right * leftStick.x + head.up * leftStick.y;
            pageBoard.position += lateral * moveSpeed * Time.deltaTime;
        }
        else
        {
            pageBoard.Rotate(head.up, leftStick.x * rotationSpeed * Time.deltaTime, Space.World);
            pageBoard.Rotate(head.right, -leftStick.y * rotationSpeed * Time.deltaTime, Space.World);
        }

        if (rightGrip)
        {
            float scaleAmount = 1f + rightStick.y * scaleSpeed * Time.deltaTime;
            pageBoard.localScale *= Mathf.Clamp(scaleAmount, 0.92f, 1.08f);
        }
        else
        {
            pageBoard.position += head.forward * rightStick.y * depthSpeed * Time.deltaTime;
        }

        pageBoard.Rotate(pageBoard.forward, -rightStick.x * rotationSpeed * Time.deltaTime, Space.World);
    }

    private void HandleDrawing()
    {
        bool triggerPressed = OVRInput.Get(OVRInput.Axis1D.SecondaryIndexTrigger) > 0.18f;

        if (!triggerPressed || hoveredCanvas == null || !hasProjectedHoverPoint || hoveredTemplate != null)
        {
            EndStroke();
            return;
        }

        if (activeCanvas != hoveredCanvas)
        {
            EndStroke();
            activeCanvas = hoveredCanvas;
            activeCanvas.BeginStroke(hoveredLocalPoint);
            return;
        }

        activeCanvas.AppendStroke(hoveredLocalPoint);
    }

    private void UpdateHoverState()
    {
        hoveredTemplate = null;
        hoveredNote = null;
        hoveredCanvas = null;
        hoveredLocalPoint = default;
        hoveredWorldPoint = default;
        hasProjectedHoverPoint = false;

        Transform controller = cameraRig.rightControllerAnchor != null
            ? cameraRig.rightControllerAnchor
            : cameraRig.centerEyeAnchor;

        Ray ray = new Ray(controller.position, controller.forward);
        bool hitTarget = Physics.Raycast(ray, out RaycastHit hit, beamLength * 2f);

        if (!hitTarget)
        {
            UpdateControllerBeam(ray, false, ray.origin + ray.direction * beamLength);
            return;
        }

        hoveredTemplate = hit.collider.GetComponentInParent<StickyNoteTemplate>();
        hoveredNote = hit.collider.GetComponentInParent<StickyNoteSurface>();
        hoveredCanvas = hoveredNote != null
            ? hoveredNote.InkCanvas
            : hit.collider.GetComponentInParent<InkCanvas>();

        if (hoveredCanvas != null && hoveredTemplate == null)
        {
            hasProjectedHoverPoint = hoveredCanvas.TryProjectRay(ray, out hoveredLocalPoint, out hoveredWorldPoint);
        }
        else
        {
            hoveredWorldPoint = hit.point;
            hasProjectedHoverPoint = true;
        }

        UpdateControllerBeam(ray, hasProjectedHoverPoint, hoveredWorldPoint);
    }

    private void SpawnSelectedStickyNote()
    {
        if (stickyTemplates == null || stickyTemplates.Length == 0 || noteAnchors == null || noteAnchors.Length == 0)
        {
            ShowTransientStatus("Sticky note anchors are not ready yet.");
            return;
        }

        int anchorIndex = FindOpenAnchorIndex();
        if (anchorIndex < 0)
        {
            ShowTransientStatus("All sticky note slots are full.");
            return;
        }

        StickyNoteTemplate selectedTemplate = stickyTemplates[selectedTemplateIndex];
        StickyNoteSurface noteSurface = StickyNoteFactory.CreatePlacedNote(selectedTemplate, noteAnchors[anchorIndex], noteSerialNumber++);
        placedNotes[anchorIndex] = noteSurface;
        nextAnchorIndex = (anchorIndex + 1) % noteAnchors.Length;

        ShowTransientStatus(selectedTemplate.TemplateName + " placed.");
    }

    private void RemoveNote(StickyNoteSurface noteSurface)
    {
        if (noteSurface == null)
        {
            return;
        }

        for (int i = 0; i < placedNotes.Length; i++)
        {
            if (placedNotes[i] == noteSurface)
            {
                placedNotes[i] = null;
            }
        }

        Destroy(noteSurface.gameObject);
        ShowTransientStatus("Sticky note removed.");
    }

    private int FindOpenAnchorIndex()
    {
        if (placedNotes == null || placedNotes.Length == 0)
        {
            return -1;
        }

        for (int offset = 0; offset < placedNotes.Length; offset++)
        {
            int index = (nextAnchorIndex + offset) % placedNotes.Length;
            if (placedNotes[index] == null)
            {
                return index;
            }
        }

        return -1;
    }

    private void CycleTemplate(int direction)
    {
        if (stickyTemplates == null || stickyTemplates.Length == 0)
        {
            return;
        }

        selectedTemplateIndex = (selectedTemplateIndex + direction + stickyTemplates.Length) % stickyTemplates.Length;
        RefreshTemplateSelection();
    }

    private void SelectTemplate(StickyNoteTemplate template)
    {
        if (template == null || stickyTemplates == null)
        {
            return;
        }

        for (int i = 0; i < stickyTemplates.Length; i++)
        {
            if (stickyTemplates[i] == template)
            {
                selectedTemplateIndex = i;
                RefreshTemplateSelection();
                return;
            }
        }
    }

    private void RefreshTemplateSelection()
    {
        if (stickyTemplates == null)
        {
            return;
        }

        for (int i = 0; i < stickyTemplates.Length; i++)
        {
            if (stickyTemplates[i] == null)
            {
                continue;
            }

            stickyTemplates[i].transform.localScale = i == selectedTemplateIndex
                ? Vector3.one * 1.08f
                : Vector3.one;
        }
    }

    private void EndStroke()
    {
        if (activeCanvas == null)
        {
            return;
        }

        activeCanvas.EndStroke();
        activeCanvas = null;
    }

    private void UpdateControllerBeam(Ray ray, bool showReticle, Vector3 endPoint)
    {
        if (controllerBeam != null)
        {
            controllerBeam.positionCount = 2;
            controllerBeam.SetPosition(0, ray.origin);
            controllerBeam.SetPosition(1, endPoint);
        }

        if (controllerReticle != null)
        {
            controllerReticle.gameObject.SetActive(showReticle);

            if (showReticle)
            {
                controllerReticle.position = endPoint;
                controllerReticle.rotation = Quaternion.LookRotation(pageBoard.forward, pageBoard.up);
            }
        }
    }

    private void ResetBoardPose()
    {
        Transform head = cameraRig.centerEyeAnchor;
        Vector3 forwardFlat = Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized;
        if (forwardFlat.sqrMagnitude < 0.001f)
        {
            forwardFlat = head.forward;
        }

        pageBoard.position = head.position + forwardFlat * 0.56f + Vector3.down * 0.03f;
        pageBoard.rotation = Quaternion.LookRotation(-forwardFlat, Vector3.up);
        pageBoard.Rotate(Vector3.right, 8f, Space.Self);
        pageBoard.localScale = Vector3.one * 1.2f;
    }

    private void ConfigureMixedRealityView()
    {
        OVRManager manager = cameraRig != null ? cameraRig.GetComponent<OVRManager>() : null;
        if (manager != null)
        {
            manager.isInsightPassthroughEnabled = true;
            manager.shouldBoundaryVisibilityBeSuppressed = true;
            manager.launchSimultaneousHandsControllersOnStartup = false;
            manager.SimultaneousHandsAndControllersEnabled = false;
            manager.controllerDrivenHandPosesType = OVRManager.ControllerDrivenHandPosesType.None;
            manager.trackingOriginType = OVRManager.TrackingOrigin.FloorLevel;
        }

        OVRPassthroughLayer passthroughLayer = cameraRig != null
            ? cameraRig.GetComponentInChildren<OVRPassthroughLayer>(true)
            : null;

        if (passthroughLayer == null && cameraRig != null && cameraRig.centerEyeAnchor != null)
        {
            passthroughLayer = cameraRig.centerEyeAnchor.gameObject.AddComponent<OVRPassthroughLayer>();
        }

        if (passthroughLayer != null)
        {
            passthroughLayer.hidden = false;
            passthroughLayer.overlayType = OVROverlay.OverlayType.Underlay;
            passthroughLayer.projectionSurfaceType = OVRPassthroughLayer.ProjectionSurfaceType.Reconstructed;
            passthroughLayer.textureOpacity = 1f;
            passthroughLayer.edgeRenderingEnabled = false;
        }

        if (cameraRig != null)
        {
            Camera[] rigCameras = cameraRig.GetComponentsInChildren<Camera>(true);
            for (int i = 0; i < rigCameras.Length; i++)
            {
                Camera rigCamera = rigCameras[i];
                rigCamera.clearFlags = CameraClearFlags.SolidColor;

                Color background = rigCamera.backgroundColor;
                background.a = 0f;
                rigCamera.backgroundColor = background;
            }
        }

        RenderSettings.skybox = null;
    }

    private void UpdateStatusText()
    {
        if (statusText == null)
        {
            return;
        }

        string selectedTemplateName = stickyTemplates != null && stickyTemplates.Length > 0
            ? stickyTemplates[selectedTemplateIndex].TemplateName
            : "None";

        string passthroughState = OVRManager.IsInsightPassthroughInitialized()
            ? "MR passthrough active"
            : "Editor fallback view";

        statusText.text =
            "Stick Note MR\n" +
            passthroughState + "\n" +
            "Selected note: " + selectedTemplateName + "\n" +
            "Aim page + R trigger: highlight book\n" +
            "Aim note + R trigger: write note\n" +
            "A/B: change note template\n" +
            "X: place selected note near book\n" +
            "Y: remove aimed note / clear page\n" +
            "L stick move page  |  R stick depth / roll\n" +
            "Hold L grip tilt page  |  Hold R grip scale page";

        if (Time.time < transientStatusUntil)
        {
            statusText.text += "\n" + transientStatus;
        }
    }

    private void ShowTransientStatus(string message)
    {
        transientStatus = message;
        transientStatusUntil = Time.time + 2.2f;
    }
}
