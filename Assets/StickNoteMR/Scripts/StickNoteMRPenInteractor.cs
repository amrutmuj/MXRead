using UnityEngine;

public class StickNoteMRPenInteractor : MonoBehaviour
{
    private const string MxInkProfile = "/interaction_profiles/logitech/mx_ink_stylus_logitech";
    private const string AimLeft = "aim_left";
    private const string AimRight = "aim_right";
    private const string Tip = "tip";
    private const string Middle = "middle";
    private const string Front = "front";
    private const string Back = "back";
    private const string Dock = "dock";
    private const string Haptics = "haptic_pulse";

    [SerializeField] private OVRCameraRig cameraRig;
    [SerializeField] private InkCanvas pageHighlightCanvas;
    [SerializeField] private Transform pageBoard;
    [SerializeField] private TextMesh statusText;
    [SerializeField] private StickyNoteTemplate[] stickyTemplates;
    [SerializeField] private Transform[] noteAnchors;
    [SerializeField] private LineRenderer controllerBeam;
    [SerializeField] private Transform controllerReticle;
    [SerializeField] private float beamLength = 0.24f;
    [SerializeField] private float tipDrawThreshold = 0.08f;
    [SerializeField] private float middleDragThreshold = 0.22f;
    [SerializeField] private float hoverRadius = 0.018f;

    private readonly Collider[] hoverBuffer = new Collider[24];
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
    private bool mxInkActive;
    private OVRPlugin.Hand activeStylusHand = OVRPlugin.Hand.None;
    private Pose activePointerPose;
    private float tipForce;
    private float middleForce;
    private bool frontPressed;
    private bool backPressed;
    private bool frontPressedLastFrame;
    private bool backPressedLastFrame;
    private bool isDraggingBoard;
    private Vector3 boardDragOffset;

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

        UpdateInputState();
        UpdateHoverState();
        HandleBoardDrag();
        HandleTemplateInputs();
        HandleDrawing();
        UpdateStatusText();
    }

    private void UpdateInputState()
    {
        mxInkActive = TryGetStylusPose(out activePointerPose, out activeStylusHand);
        if (!mxInkActive)
        {
            activePointerPose = GetFallbackPose();
            activeStylusHand = OVRPlugin.Hand.None;
        }

        tipForce = 0f;
        middleForce = 0f;
        frontPressed = false;
        backPressed = false;

        if (mxInkActive)
        {
            TryReadFloat(Tip, out tipForce);
            TryReadFloat(Middle, out middleForce);
            TryReadBool(Front, out frontPressed);
            TryReadBool(Back, out backPressed);
            if (TryReadBool(Dock, out bool docked) && docked)
            {
                tipForce = 0f;
                middleForce = 0f;
                frontPressed = false;
                backPressed = false;
            }
        }
        else
        {
            tipForce = OVRInput.Get(OVRInput.Axis1D.SecondaryIndexTrigger);
            middleForce = Mathf.Max(
                OVRInput.Get(OVRInput.Axis1D.PrimaryHandTrigger),
                OVRInput.Get(OVRInput.Axis1D.SecondaryHandTrigger));
            frontPressed = OVRInput.Get(OVRInput.Button.One);
            backPressed = OVRInput.Get(OVRInput.Button.Two);
        }
    }

    private void UpdateHoverState()
    {
        hoveredTemplate = null;
        hoveredNote = null;
        hoveredCanvas = null;
        hoveredLocalPoint = default;
        hoveredWorldPoint = default;
        hasProjectedHoverPoint = false;

        Vector3 tipPosition = activePointerPose.position;
        Vector3 tipForward = activePointerPose.rotation * Vector3.forward;
        Vector3 beamEnd = tipPosition + tipForward * beamLength;
        Collider collider = FindHoveredCollider(tipPosition, tipForward, ref beamEnd);

        if (collider == null)
        {
            UpdateBeam(tipPosition, beamEnd, false, beamEnd);
            return;
        }

        hoveredTemplate = collider.GetComponentInParent<StickyNoteTemplate>();
        hoveredNote = collider.GetComponentInParent<StickyNoteSurface>();
        hoveredCanvas = hoveredNote != null
            ? hoveredNote.InkCanvas
            : collider.GetComponentInParent<InkCanvas>();

        if (hoveredCanvas != null && hoveredTemplate == null)
        {
            if (hoveredCanvas.TryProjectTouchPoint(tipPosition, hoveredCanvas.TouchDistance * 1.75f, out hoveredLocalPoint))
            {
                hoveredWorldPoint = hoveredCanvas.transform.TransformPoint(hoveredLocalPoint);
                hasProjectedHoverPoint = true;
            }
            else
            {
                hasProjectedHoverPoint = TryProjectCanvas(hoveredCanvas, tipPosition, tipForward, out hoveredLocalPoint, out hoveredWorldPoint);
            }
        }
        else
        {
            hoveredWorldPoint = collider.ClosestPoint(tipPosition);
            hasProjectedHoverPoint = true;
        }

        if (hasProjectedHoverPoint)
        {
            beamEnd = hoveredWorldPoint;
        }

        UpdateBeam(tipPosition, beamEnd, hasProjectedHoverPoint, hoveredWorldPoint);
    }

    private void HandleBoardDrag()
    {
        bool canDrag = middleForce > middleDragThreshold &&
                       tipForce < tipDrawThreshold * 0.55f &&
                       hoveredCanvas == null &&
                       hoveredTemplate == null &&
                       hoveredNote == null;

        if (!canDrag)
        {
            isDraggingBoard = false;
            return;
        }

        if (!isDraggingBoard)
        {
            boardDragOffset = pageBoard.position - activePointerPose.position;
            isDraggingBoard = true;
        }

        pageBoard.position = activePointerPose.position + boardDragOffset;

        Transform head = cameraRig.centerEyeAnchor;
        Vector3 towardHead = head.position - pageBoard.position;
        Vector3 forwardFlat = Vector3.ProjectOnPlane(towardHead, Vector3.up).normalized;
        if (forwardFlat.sqrMagnitude > 0.001f)
        {
            pageBoard.rotation = Quaternion.LookRotation(forwardFlat, Vector3.up);
            pageBoard.Rotate(Vector3.right, 8f, Space.Self);
        }
    }

    private void HandleTemplateInputs()
    {
        if (stickyTemplates == null || stickyTemplates.Length == 0)
        {
            return;
        }

        bool frontDown = frontPressed && !frontPressedLastFrame;
        bool backDown = backPressed && !backPressedLastFrame;
        frontPressedLastFrame = frontPressed;
        backPressedLastFrame = backPressed;

        if (frontDown)
        {
            if (hoveredTemplate != null)
            {
                SelectTemplate(hoveredTemplate);
                ShowTransientStatus(hoveredTemplate.TemplateName + " selected.");
            }

            SpawnSelectedStickyNote();
        }

        if (backDown)
        {
            if (hoveredNote != null)
            {
                RemoveNote(hoveredNote);
            }
            else
            {
                pageHighlightCanvas.ClearAll();
                ShowTransientStatus("Highlights cleared.");
                PulseStylus(0.18f, 0.02f);
            }
        }
    }

    private void HandleDrawing()
    {
        bool tipPressed = tipForce > tipDrawThreshold;
        if (!tipPressed || hoveredCanvas == null || !hasProjectedHoverPoint || hoveredTemplate != null)
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

        StickyNoteTemplate template = stickyTemplates[selectedTemplateIndex];
        StickyNoteSurface note = StickyNoteFactory.CreatePlacedNote(template, noteAnchors[anchorIndex], noteSerialNumber++);
        note.transform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(noteSerialNumber * 0.67f) * 4.5f);
        placedNotes[anchorIndex] = note;
        nextAnchorIndex = (anchorIndex + 1) % noteAnchors.Length;

        ShowTransientStatus(template.TemplateName + " placed.");
        PulseStylus(0.28f, 0.03f);
    }

    private void RemoveNote(StickyNoteSurface note)
    {
        if (note == null)
        {
            return;
        }

        for (int i = 0; i < placedNotes.Length; i++)
        {
            if (placedNotes[i] == note)
            {
                placedNotes[i] = null;
            }
        }

        Destroy(note.gameObject);
        ShowTransientStatus("Sticky note removed.");
        PulseStylus(0.22f, 0.02f);
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

    private void ResetBoardPose()
    {
        Transform head = cameraRig.centerEyeAnchor;
        Vector3 forwardFlat = Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized;
        if (forwardFlat.sqrMagnitude < 0.001f)
        {
            forwardFlat = head.forward;
        }

        pageBoard.position = head.position + forwardFlat * 0.54f + Vector3.down * 0.04f;
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

        OVRManager.eyeFovPremultipliedAlphaModeEnabled = false;

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
                rigCameras[i].clearFlags = CameraClearFlags.SolidColor;
                rigCameras[i].backgroundColor = Color.clear;
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

        string selected = stickyTemplates != null && stickyTemplates.Length > 0
            ? stickyTemplates[selectedTemplateIndex].TemplateName
            : "None";

        string passthrough = OVRManager.IsInsightPassthroughInitialized()
            ? "MR passthrough active"
            : "Editor passthrough fallback";
        string input = mxInkActive ? "MX Ink connected" : "Quest controller fallback";

        statusText.text =
            "Stick Note MR\n" +
            passthrough + "\n" +
            input + "\n" +
            "Selected note: " + selected + "\n" +
            "Hover template + FRONT: choose & place note\n" +
            "Touch page or note with TIP: write / highlight\n" +
            "BACK: remove hovered note or clear highlights\n" +
            "Hold MIDDLE and move pen: reposition book frame";

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

    private Pose GetFallbackPose()
    {
        Transform anchor = cameraRig.rightControllerAnchor != null
            ? cameraRig.rightControllerAnchor
            : cameraRig.centerEyeAnchor;
        Quaternion rotation = anchor.rotation;
        return new Pose(anchor.position + rotation * new Vector3(0.012f, -0.008f, 0.05f), rotation);
    }

    private bool TryGetStylusPose(out Pose pose, out OVRPlugin.Hand hand)
    {
        if (TryGetStylusPoseForHand(OVRPlugin.Hand.HandRight, AimRight, out pose))
        {
            hand = OVRPlugin.Hand.HandRight;
            return true;
        }

        if (TryGetStylusPoseForHand(OVRPlugin.Hand.HandLeft, AimLeft, out pose))
        {
            hand = OVRPlugin.Hand.HandLeft;
            return true;
        }

        pose = default;
        hand = OVRPlugin.Hand.None;
        return false;
    }

    private static bool TryGetStylusPoseForHand(OVRPlugin.Hand hand, string action, out Pose pose)
    {
        pose = default;
        if (!string.Equals(GetInteractionProfile(hand), MxInkProfile, System.StringComparison.Ordinal))
        {
            return false;
        }

        if (!OVRPlugin.GetActionStatePose(action, out OVRPlugin.Posef rawPose))
        {
            return false;
        }

        pose = new Pose(
            rawPose.Position.FromFlippedZVector3f(),
            rawPose.Orientation.FromFlippedZQuatf());
        return true;
    }

    private static string GetInteractionProfile(OVRPlugin.Hand hand)
    {
        try
        {
            return OVRPlugin.GetCurrentInteractionProfileName(hand) ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static bool TryReadFloat(string action, out float value)
    {
        value = 0f;
        return OVRPlugin.GetActionStateFloat(action, out value);
    }

    private static bool TryReadBool(string action, out bool value)
    {
        value = false;
        return OVRPlugin.GetActionStateBoolean(action, out value);
    }

    private Collider FindHoveredCollider(Vector3 tipPosition, Vector3 tipForward, ref Vector3 beamEnd)
    {
        int hitCount = Physics.OverlapSphereNonAlloc(tipPosition, hoverRadius, hoverBuffer, ~0, QueryTriggerInteraction.Collide);
        Collider bestCollider = null;
        float bestDistance = float.PositiveInfinity;

        for (int i = 0; i < hitCount; i++)
        {
            Collider candidate = hoverBuffer[i];
            if (!IsInteractive(candidate))
            {
                continue;
            }

            float distance = (candidate.ClosestPoint(tipPosition) - tipPosition).sqrMagnitude;
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestCollider = candidate;
            }
        }

        if (bestCollider != null)
        {
            beamEnd = bestCollider.ClosestPoint(tipPosition);
            return bestCollider;
        }

        RaycastHit[] forwardHits = Physics.RaycastAll(new Ray(tipPosition - tipForward * 0.04f, tipForward), beamLength + 0.08f, ~0, QueryTriggerInteraction.Collide);
        Collider rayCollider = FindBestRaycastHit(forwardHits, out Vector3 hitPoint);
        if (rayCollider != null)
        {
            beamEnd = hitPoint;
            return rayCollider;
        }

        RaycastHit[] reverseHits = Physics.RaycastAll(new Ray(tipPosition + tipForward * 0.04f, -tipForward), beamLength + 0.08f, ~0, QueryTriggerInteraction.Collide);
        rayCollider = FindBestRaycastHit(reverseHits, out hitPoint);
        if (rayCollider != null)
        {
            beamEnd = hitPoint;
        }

        return rayCollider;
    }

    private static Collider FindBestRaycastHit(RaycastHit[] hits, out Vector3 point)
    {
        point = default;
        if (hits == null || hits.Length == 0)
        {
            return null;
        }

        System.Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));
        for (int i = 0; i < hits.Length; i++)
        {
            if (!IsInteractive(hits[i].collider))
            {
                continue;
            }

            point = hits[i].point;
            return hits[i].collider;
        }

        return null;
    }

    private static bool IsInteractive(Collider candidate)
    {
        return candidate != null &&
               (candidate.GetComponentInParent<InkCanvas>() != null ||
                candidate.GetComponentInParent<StickyNoteTemplate>() != null ||
                candidate.GetComponentInParent<StickyNoteSurface>() != null);
    }

    private static bool TryProjectCanvas(InkCanvas canvas, Vector3 tipPosition, Vector3 tipForward, out Vector3 localPoint, out Vector3 worldPoint)
    {
        Ray forwardRay = new Ray(tipPosition - tipForward * 0.04f, tipForward);
        if (canvas.TryProjectRay(forwardRay, out localPoint, out worldPoint))
        {
            return true;
        }

        Ray reverseRay = new Ray(tipPosition + tipForward * 0.04f, -tipForward);
        return canvas.TryProjectRay(reverseRay, out localPoint, out worldPoint);
    }

    private void UpdateBeam(Vector3 startPoint, Vector3 endPoint, bool showReticle, Vector3 reticlePoint)
    {
        if (controllerBeam != null)
        {
            controllerBeam.positionCount = 2;
            controllerBeam.SetPosition(0, startPoint);
            controllerBeam.SetPosition(1, endPoint);
        }

        if (controllerReticle != null)
        {
            controllerReticle.gameObject.SetActive(showReticle);
            if (showReticle)
            {
                controllerReticle.position = reticlePoint;
                controllerReticle.rotation = Quaternion.LookRotation(pageBoard.forward, pageBoard.up);
            }
        }
    }

    private void PulseStylus(float amplitude, float duration)
    {
        if (!mxInkActive || activeStylusHand == OVRPlugin.Hand.None)
        {
            return;
        }

        OVRPlugin.TriggerVibrationAction(Haptics, activeStylusHand, duration, amplitude);
    }
}
