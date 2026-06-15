using System.Collections.Generic;
using Meta.XR.MRUtilityKit;
using Meta.XR.MRUtilityKitSamples.QRCodeDetection;
using UnityEngine;
using UnityEngine.Rendering;

public class StickNoteMRBookInteractor : MonoBehaviour
{
    private enum AnnotationTool
    {
        Highlight,
        Pen,
        AirInk
    }

    private const string MxInkProfile = "/interaction_profiles/logitech/mx_ink_stylus_logitech";
    private const string AimLeft = "aim_left";
    private const string AimRight = "aim_right";
    private const string TipPoseLeft = "tip_pose_left";
    private const string TipPoseRight = "tip_pose_right";
    private const string Tip = "tip";
    private const string Middle = "middle";
    private const string Front = "front";
    private const string Back = "back";
    private const string Dock = "dock";
    private const string Haptics = "haptic_pulse";

    [SerializeField] private OVRCameraRig cameraRig;
    [SerializeField] private InkCanvas pageHighlightCanvas;
    [SerializeField] private InkCanvas pageWritingCanvas;
    [SerializeField] private Collider pageSurfaceCollider;
    [SerializeField] private SpatialInkCanvas airInkCanvas;
    [SerializeField] private Transform pageBoard;
    [SerializeField] private Transform noteWorkspaceRoot;
    [SerializeField] private TextMesh statusText;
    [SerializeField] private Transform colorPaletteSelectionRoot;
    [SerializeField] private Transform toolSelectionRoot;
    [SerializeField] private WritingPenDrawing writingPenDrawing;
    [SerializeField] private GameObject penToolSelectedVisual;
    [SerializeField] private GameObject penToolNotSelectedVisual;
    [SerializeField] private GameObject highlightToolSelectedVisual;
    [SerializeField] private GameObject highlightToolNotSelectedVisual;
    [SerializeField] private GameObject penColorLabel;
    [SerializeField] private GameObject highlightLabel;
    [SerializeField] private StickyNoteTemplate[] stickyTemplates;
    [SerializeField] private Transform[] noteAnchors;
    [SerializeField] private StickNoteMRActionButton[] actionButtons;
    [SerializeField] private LineRenderer controllerBeam;
    [SerializeField] private Transform controllerReticle;
    [SerializeField] private bool syncWithQRCodeTrackables = true;
    [SerializeField] private bool hideInactiveQRCodeContent = true;
    [SerializeField] private float beamLength = 0.3f;
    [SerializeField] private float tipDrawThreshold = 0.04f;
    [SerializeField] private float middleDragThreshold = 0.22f;
    [SerializeField] private float hoverRadius = 0.02f;
    [SerializeField] private float maxAirInkDistance = 0.5f;
    [SerializeField] private float notePlacementDistance = 0.12f;
    [SerializeField] private float boardResetDistance = 0.54f;
    [SerializeField] private Vector3 boardResetLocalOffset = Vector3.zero;
    [SerializeField] private bool alignGrabbedNotesToStylus = true;
    [SerializeField] private bool keepGrabbedNotesUpright = false;
    [SerializeField] private Vector3 notePlacementRotationOffsetEuler = Vector3.zero;

    private readonly Collider[] hoverBuffer = new Collider[32];
    private readonly Dictionary<int, StickyNoteSurface[]> placedNotesByPage = new Dictionary<int, StickyNoteSurface[]>();
    private readonly List<StickyNoteSurface> floatingNotes = new List<StickyNoteSurface>();
    private readonly List<CanvasAudioRecorder> floatingVoiceNotes = new List<CanvasAudioRecorder>();
    private readonly Dictionary<int, Transform> qrPageRoots = new Dictionary<int, Transform>();
    private InkCanvas activeCanvas;
    private SpatialInkCanvas activeAirInkCanvas;
    private StickyNoteTemplate hoveredTemplate;
    private StickyNoteSurface hoveredNote;
    private InkCanvas hoveredCanvas;
    private CanvasAudioRecorder hoveredVoiceRecorder;
    private StickNoteMRActionButton hoveredButton;
    private Transform hoveredColorSwatch;
    private Transform hoveredToolSwatch;
    private Vector3 hoveredLocalPoint;
    private Vector3 hoveredWorldPoint;
    private Vector3 hoveredVoiceNormal;
    private bool hasProjectedHoverPoint;
    private int selectedTemplateIndex;
    private int nextAnchorIndex;
    private int noteSerialNumber = 1;
    private int currentPageIndex = 1;
    private int activeQrPageIndex;
    private AnnotationTool currentTool = AnnotationTool.Highlight;
    private string transientStatus;
    private float transientStatusUntil;
    private bool mxInkActive;
    private OVRPlugin.Hand activeStylusHand = OVRPlugin.Hand.None;
    private Pose activeAimPose;
    private Pose activeTipPose;
    private float tipForce;
    private float middleForce;
    private bool frontPressed;
    private bool backPressed;
    private bool frontPressedLastFrame;
    private bool backPressedLastFrame;
    private bool tipPressedLastFrame;
    private bool middlePressedLastFrame;
    private bool isDraggingBoard;
    private Vector3 boardDragOffset;
    private StickyNoteSurface grabbedNote;
    private CanvasAudioRecorder grabbedVoiceRecorder;

    private void OnEnable()
    {
        QRCodeManager.ActivePageChanged += HandleActiveQRCodePageChanged;
    }

    private void OnDisable()
    {
        QRCodeManager.ActivePageChanged -= HandleActiveQRCodePageChanged;
    }

    private void Start()
    {
        ResolveColorPaletteReferences();
        ResolveToolSelectionReferences();
        SyncToolModeFromToolSelectionVisuals();
        EnsurePaletteCanvasFacing();
        ConfigureMixedRealityView();
        ResetBoardPose();
        RefreshTemplateSelection();
        RefreshActionButtons();
        ApplyToolSelectionVisuals();
        ApplyWritingPenToolMode();
        SyncActiveQRCodePage();
        RegisterExistingVoiceRecorders();
        RefreshPageVisibility();
    }

    private void Update()
    {
        if (cameraRig == null || pageHighlightCanvas == null || pageBoard == null)
        {
            return;
        }

        UpdateInputState();
        SyncActiveQRCodePage();
        UpdateHoverState();
        UpdateGrabbedNote();
        UpdateGrabbedVoiceRecorder();
        HandleBoardDrag();
        HandleActionInputs();
        HandleDrawing();
        UpdateStatusText();
    }

    private void UpdateInputState()
    {
        mxInkActive = TryGetStylusPoses(out activeAimPose, out activeTipPose, out activeStylusHand);
        if (!mxInkActive)
        {
            activeAimPose = GetFallbackPose();
            activeTipPose = activeAimPose;
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
            frontPressed = OVRInput.Get(OVRInput.Button.One) || OVRInput.Get(OVRInput.Button.Three);
            backPressed = OVRInput.Get(OVRInput.Button.Two) || OVRInput.Get(OVRInput.Button.Four);
        }
    }

    private void HandleActiveQRCodePageChanged(int pageId, MRUKTrackable trackable, string payload)
    {
        if (!syncWithQRCodeTrackables)
        {
            return;
        }

        ApplyActiveQRCodePage(pageId, trackable, payload);
    }

    private void SyncActiveQRCodePage()
    {
        if (!syncWithQRCodeTrackables)
        {
            return;
        }

        if (QRCodeManager.TryGetActivePage(out int pageId, out MRUKTrackable trackable, out string payload))
        {
            ApplyActiveQRCodePage(pageId, trackable, payload);
        }
    }

    private void ApplyActiveQRCodePage(int pageId, MRUKTrackable trackable, string payload)
    {
        if (!trackable || pageId <= 0)
        {
            return;
        }

        Transform pageRoot = GetOrCreateQRCodePageRoot(pageId, trackable.transform, payload);
        if (pageRoot == null)
        {
            return;
        }

        noteWorkspaceRoot = pageRoot;
        activeQrPageIndex = pageId;

        if (currentPageIndex != pageId)
        {
            SetCurrentPage(pageId);
            ShowTransientStatus("QR page " + pageId + " active.");
        }
        else
        {
            RefreshQRCodePageRootVisibility();
        }
    }

    private Transform GetOrCreateQRCodePageRoot(int pageId, Transform trackableTransform, string payload)
    {
        if (trackableTransform == null || pageId <= 0)
        {
            return null;
        }

        if (!qrPageRoots.TryGetValue(pageId, out Transform root) || root == null)
        {
            string rootName = string.IsNullOrEmpty(payload)
                ? "QRPageContent_" + pageId
                : "QRPageContent_" + pageId + "_" + payload;

            GameObject rootObject = new GameObject(rootName);
            root = rootObject.transform;
            qrPageRoots[pageId] = root;
        }

        if (root.parent != trackableTransform)
        {
            root.SetParent(trackableTransform, false);
        }

        root.localPosition = Vector3.zero;
        root.localRotation = Quaternion.identity;
        root.localScale = Vector3.one;

        return root;
    }

    private void RefreshQRCodePageRootVisibility()
    {
        if (!hideInactiveQRCodeContent)
        {
            return;
        }

        foreach (KeyValuePair<int, Transform> kvp in qrPageRoots)
        {
            Transform root = kvp.Value;
            if (root == null)
            {
                continue;
            }

            bool shouldBeVisible = kvp.Key == currentPageIndex;
            if (root.gameObject.activeSelf != shouldBeVisible)
            {
                root.gameObject.SetActive(shouldBeVisible);
            }
        }
    }

    private void UpdateHoverState()
    {
        hoveredTemplate = null;
        hoveredNote = null;
        hoveredCanvas = null;
        hoveredVoiceRecorder = null;
        hoveredButton = null;
        hoveredColorSwatch = null;
        hoveredToolSwatch = null;
        hoveredLocalPoint = default;
        hoveredWorldPoint = default;
        hoveredVoiceNormal = default;
        hasProjectedHoverPoint = false;

        Vector3 tipPosition = activeTipPose.position;
        Vector3 tipForward = activeAimPose.rotation * Vector3.forward;
        Vector3 beamEnd = activeAimPose.position + tipForward * beamLength;
        Collider collider = FindHoveredCollider(tipPosition, tipForward, ref beamEnd);

        if (collider == null)
        {
            UpdateBeam(activeAimPose.position, beamEnd, false, beamEnd);
            return;
        }

        hoveredButton = collider.GetComponentInParent<StickNoteMRActionButton>();
        hoveredTemplate = collider.GetComponentInParent<StickyNoteTemplate>();
        hoveredNote = collider.GetComponentInParent<StickyNoteSurface>();
        hoveredVoiceRecorder = collider.GetComponentInParent<CanvasAudioRecorder>();
        hoveredColorSwatch = GetPaletteSwatch(collider);
        hoveredToolSwatch = GetToolSelectionSwatch(collider);

        if (hoveredButton != null)
        {
            hoveredWorldPoint = collider.ClosestPoint(tipPosition);
            hasProjectedHoverPoint = true;
            UpdateBeam(activeAimPose.position, hoveredWorldPoint, true, hoveredWorldPoint);
            return;
        }

        if (hoveredColorSwatch != null)
        {
            hoveredWorldPoint = collider.ClosestPoint(tipPosition);
            hasProjectedHoverPoint = true;
            UpdateBeam(activeAimPose.position, hoveredWorldPoint, true, hoveredWorldPoint);
            return;
        }

        if (hoveredToolSwatch != null)
        {
            hoveredWorldPoint = collider.ClosestPoint(tipPosition);
            hasProjectedHoverPoint = true;
            UpdateBeam(activeAimPose.position, hoveredWorldPoint, true, hoveredWorldPoint);
            return;
        }

        if (hoveredVoiceRecorder != null && hoveredVoiceRecorder.TryGetInteractorHover(collider, tipPosition, out hoveredWorldPoint, out hoveredVoiceNormal))
        {
            hasProjectedHoverPoint = true;
            UpdateBeam(activeAimPose.position, hoveredWorldPoint, true, hoveredWorldPoint);
            return;
        }

        if (hoveredNote != null)
        {
            hoveredCanvas = hoveredNote.InkCanvas;
        }
        else if (IsPageSurface(collider))
        {
            hoveredCanvas = GetSelectedPageCanvas();
        }
        else
        {
            hoveredCanvas = collider.GetComponentInParent<InkCanvas>();
        }

        if (hoveredCanvas != null && hoveredTemplate == null)
        {
            if (hoveredCanvas.TryProjectTouchPoint(tipPosition, hoveredCanvas.TouchDistance * 1.8f, out hoveredLocalPoint))
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

        UpdateBeam(activeAimPose.position, beamEnd, hasProjectedHoverPoint, hoveredWorldPoint);
    }

    private void HandleBoardDrag()
    {
        bool canDrag = middleForce > middleDragThreshold &&
                       tipForce < tipDrawThreshold * 0.75f &&
                       grabbedVoiceRecorder == null &&
                       hoveredCanvas == null &&
                       hoveredVoiceRecorder == null &&
                       hoveredTemplate == null &&
                       hoveredNote == null &&
                       hoveredButton == null &&
                       hoveredColorSwatch == null &&
                       hoveredToolSwatch == null;

        if (!canDrag)
        {
            isDraggingBoard = false;
            return;
        }

        if (!isDraggingBoard)
        {
            boardDragOffset = pageBoard.position - activeTipPose.position;
            isDraggingBoard = true;
        }

        pageBoard.position = activeTipPose.position + boardDragOffset;
    }

    private void HandleActionInputs()
    {
        bool tipPressed = tipForce > tipDrawThreshold;
        bool tipDown = tipPressed && !tipPressedLastFrame;
        bool middlePressed = middleForce > middleDragThreshold;
        bool middleDown = middlePressed && !middlePressedLastFrame;
        bool middleUp = !middlePressed && middlePressedLastFrame;
        bool frontDown = frontPressed && !frontPressedLastFrame;
        bool frontUp = !frontPressed && frontPressedLastFrame;
        bool backDown = backPressed && !backPressedLastFrame;

        if (middleDown && hoveredVoiceRecorder != null)
        {
            BeginGrabVoiceRecorder(hoveredVoiceRecorder);
            CommitFrameInputState(tipPressed, middlePressed);
            return;
        }

        if (middleUp && grabbedVoiceRecorder != null)
        {
            ReleaseGrabbedVoiceRecorder();
        }

        if (tipDown && hoveredVoiceRecorder != null)
        {
            hoveredVoiceRecorder.TriggerFromInteractor();
            PromoteVoiceRecorderToCurrentPage(hoveredVoiceRecorder);
            ShowTransientStatus("Voice note action: Spawn/Record/Stop/Play/Pause");
            PulseStylus(0.1f, 0.01f);
            CommitFrameInputState(tipPressed, middlePressed);
            return;
        }

        if (tipDown && hoveredToolSwatch != null)
        {
            ApplyHoveredToolSwatch();
            CommitFrameInputState(tipPressed, middlePressed);
            return;
        }

        if (frontDown)
        {
            if (hoveredToolSwatch != null)
            {
                ApplyHoveredToolSwatch();
                CommitFrameInputState(tipPressed, middlePressed);
                return;
            }

            if (hoveredColorSwatch != null)
            {
                ApplyHoveredColorSwatch();
                CommitFrameInputState(tipPressed, middlePressed);
                return;
            }

            if (hoveredButton != null)
            {
                ActivateAction(hoveredButton.Action);
                CommitFrameInputState(tipPressed, middlePressed);
                return;
            }

            if (hoveredVoiceRecorder != null)
            {
                hoveredVoiceRecorder.TriggerFromInteractor();
                PromoteVoiceRecorderToCurrentPage(hoveredVoiceRecorder);
                ShowTransientStatus("Voice note action: Spawn/Record/Stop/Play/Pause");
                PulseStylus(0.1f, 0.01f);
                CommitFrameInputState(tipPressed, middlePressed);
                return;
            }

            if (hoveredTemplate != null)
            {
                SelectTemplate(hoveredTemplate);
                StickyNoteSurface createdNote = SpawnSelectedStickyNote();
                if (createdNote != null)
                {
                    BeginGrabNote(createdNote);
                }
                CommitFrameInputState(tipPressed, middlePressed);
                return;
            }

            if (hoveredNote != null)
            {
                BeginGrabNote(hoveredNote);
                CommitFrameInputState(tipPressed, middlePressed);
                return;
            }
        }

        if (frontUp && grabbedNote != null)
        {
            ReleaseGrabbedNote();
        }

        if (backDown)
        {
            if (hoveredNote != null)
            {
                RemoveNote(hoveredNote);
            }
            else if (hoveredVoiceRecorder != null && hoveredVoiceRecorder.canRecord)
            {
                RemoveVoiceNote(hoveredVoiceRecorder);
            }
            else
            {
                ClearCurrentPageInk();
            }
        }

        CommitFrameInputState(tipPressed, middlePressed);
    }

    private void CommitFrameInputState(bool tipPressed, bool middlePressed)
    {
        frontPressedLastFrame = frontPressed;
        backPressedLastFrame = backPressed;
        tipPressedLastFrame = tipPressed;
        middlePressedLastFrame = middlePressed;
    }

    private void HandleDrawing()
    {
        bool tipPressed = tipForce > tipDrawThreshold;

        if (grabbedNote != null || grabbedVoiceRecorder != null)
        {
            EndStroke();
            return;
        }

        if (hoveredVoiceRecorder != null)
        {
            EndStroke();
            return;
        }

        if (hoveredNote != null)
        {
            HandleNoteDrawing(tipPressed, hoveredNote.InkCanvas);
            return;
        }

        if (currentTool == AnnotationTool.AirInk)
        {
            HandleAirInkDrawing(tipPressed);
            return;
        }

        HandleCanvasDrawing(tipPressed, hoveredCanvas);
    }

    private void HandleCanvasDrawing(bool tipPressed, InkCanvas targetCanvas)
    {
        if (!tipPressed || targetCanvas == null || !hasProjectedHoverPoint || hoveredTemplate != null || hoveredButton != null || hoveredColorSwatch != null || hoveredToolSwatch != null)
        {
            EndStroke();
            return;
        }

        if (activeCanvas != targetCanvas)
        {
            EndStroke();
            activeCanvas = targetCanvas;
            activeCanvas.BeginStroke(hoveredLocalPoint, currentPageIndex);
            PulseStylus(0.1f, 0.012f);
            return;
        }

        activeCanvas.AppendStroke(hoveredLocalPoint);
    }

    private void HandleNoteDrawing(bool tipPressed, InkCanvas targetCanvas)
    {
        if (!tipPressed || targetCanvas == null || !hasProjectedHoverPoint || hoveredTemplate != null || hoveredButton != null || hoveredColorSwatch != null || hoveredToolSwatch != null)
        {
            EndStroke();
            return;
        }

        targetCanvas.SetVisiblePage(1);

        if (activeCanvas != targetCanvas)
        {
            EndStroke();
            activeCanvas = targetCanvas;
            activeCanvas.BeginStroke(hoveredLocalPoint, 1);
            PulseStylus(0.1f, 0.012f);
            return;
        }

        activeCanvas.AppendStroke(hoveredLocalPoint);
    }

    private void HandleAirInkDrawing(bool tipPressed)
    {
        if (!tipPressed || airInkCanvas == null)
        {
            EndStroke();
            return;
        }

        Vector3 localTipPoint = pageBoard.InverseTransformPoint(activeTipPose.position);
        if (localTipPoint.magnitude > maxAirInkDistance)
        {
            EndStroke();
            return;
        }

        if (activeAirInkCanvas != airInkCanvas)
        {
            EndStroke();
            activeAirInkCanvas = airInkCanvas;
            activeAirInkCanvas.BeginStroke(activeTipPose.position, currentPageIndex);
            PulseStylus(0.14f, 0.014f);
            return;
        }

        activeAirInkCanvas.AppendStroke(activeTipPose.position);
    }

    private void ActivateAction(StickNoteMRActionButton.ActionKind action)
    {
        switch (action)
        {
            case StickNoteMRActionButton.ActionKind.PreviousPage:
                SetCurrentPage(Mathf.Max(1, currentPageIndex - 1));
                break;
            case StickNoteMRActionButton.ActionKind.NextPage:
                SetCurrentPage(currentPageIndex + 1);
                break;
            case StickNoteMRActionButton.ActionKind.UseHighlight:
                SetTool(AnnotationTool.Highlight);
                break;
            case StickNoteMRActionButton.ActionKind.UsePen:
                SetTool(AnnotationTool.Pen);
                break;
            case StickNoteMRActionButton.ActionKind.UseAirInk:
                SetTool(AnnotationTool.AirInk);
                break;
            case StickNoteMRActionButton.ActionKind.ClearCurrentPage:
                ClearCurrentPageInk();
                break;
            case StickNoteMRActionButton.ActionKind.ResetBoard:
                ResetBoardPose();
                ShowTransientStatus("Book frame reset.");
                break;
        }
    }

    private void SetTool(AnnotationTool tool)
    {
        bool changed = currentTool != tool;
        if (changed)
        {
            EndStroke();
            currentTool = tool;
        }

        RefreshActionButtons();
        ApplyToolSelectionVisuals();
        ApplyWritingPenToolMode();

        if (!changed)
        {
            return;
        }

        string toolMessage;
        switch (tool)
        {
            case AnnotationTool.Highlight:
                toolMessage = "Highlight tool ready.";
                break;
            case AnnotationTool.Pen:
                toolMessage = "Page pen ready.";
                break;
            default:
                toolMessage = "Air ink ready.";
                break;
        }
        ShowTransientStatus(toolMessage);
    }

    private void SetCurrentPage(int pageIndex)
    {
        currentPageIndex = Mathf.Max(1, pageIndex);
        EndStroke();
        RefreshPageVisibility();
        ShowTransientStatus("Page " + currentPageIndex + " active.");
    }

    private void RefreshPageVisibility()
    {
        pageHighlightCanvas?.SetVisiblePage(currentPageIndex);
        pageWritingCanvas?.SetVisiblePage(currentPageIndex);
        airInkCanvas?.SetVisiblePage(currentPageIndex);

        for (int i = floatingVoiceNotes.Count - 1; i >= 0; i--)
        {
            CanvasAudioRecorder voiceRecorder = floatingVoiceNotes[i];
            if (voiceRecorder == null)
            {
                floatingVoiceNotes.RemoveAt(i);
                continue;
            }

            bool shouldBeVisible = !hideInactiveQRCodeContent || voiceRecorder.PageIndex == currentPageIndex;
            voiceRecorder.SetVisible(shouldBeVisible);
        }

        for (int i = 0; i < floatingNotes.Count; i++)
        {
            if (floatingNotes[i] != null)
            {
                floatingNotes[i].SetVisible(floatingNotes[i].PageIndex == currentPageIndex);
            }
        }

        foreach (KeyValuePair<int, StickyNoteSurface[]> entry in placedNotesByPage)
        {
            StickyNoteSurface[] noteArray = entry.Value;
            if (noteArray == null)
            {
                continue;
            }

            bool isVisible = entry.Key == currentPageIndex;
            for (int i = 0; i < noteArray.Length; i++)
            {
                if (noteArray[i] != null)
                {
                    noteArray[i].SetVisible(isVisible);
                }
            }
        }

        RefreshQRCodePageRootVisibility();
    }

    private void UpdateGrabbedNote()
    {
        if (grabbedNote == null || !frontPressed)
        {
            return;
        }

        PositionNoteForPlacement(grabbedNote.transform);
    }

    private void UpdateGrabbedVoiceRecorder()
    {
        if (grabbedVoiceRecorder == null || middleForce <= middleDragThreshold)
        {
            return;
        }

        PositionNoteForPlacement(grabbedVoiceRecorder.transform);
    }

    private void BeginGrabVoiceRecorder(CanvasAudioRecorder recorder)
    {
        if (recorder == null)
        {
            return;
        }

        recorder.TryUnlockFromSpawner();
        PromoteVoiceRecorderToCurrentPage(recorder);
        grabbedVoiceRecorder = recorder;
        PositionNoteForPlacement(grabbedVoiceRecorder.transform);
        ShowTransientStatus("Move the voice note and release MIDDLE to place it.");
        PulseStylus(0.18f, 0.018f);
    }

    private void ReleaseGrabbedVoiceRecorder()
    {
        if (grabbedVoiceRecorder == null)
        {
            return;
        }

        PositionNoteForPlacement(grabbedVoiceRecorder.transform);
        ShowTransientStatus("Voice note placed.");
        grabbedVoiceRecorder = null;
        PulseStylus(0.12f, 0.012f);
    }

    private void BeginGrabNote(StickyNoteSurface noteSurface)
    {
        if (noteSurface == null)
        {
            return;
        }

        grabbedNote = noteSurface;
        PositionNoteForPlacement(grabbedNote.transform);
        ShowTransientStatus("Move the note with the MX Ink and release FRONT to place it.");
        PulseStylus(0.18f, 0.018f);
    }

    private void ReleaseGrabbedNote()
    {
        if (grabbedNote == null)
        {
            return;
        }

        PositionNoteForPlacement(grabbedNote.transform);
        ShowTransientStatus("Sticky note placed.");
        grabbedNote = null;
        PulseStylus(0.12f, 0.012f);
    }

    private StickyNoteSurface SpawnSelectedStickyNote()
    {
        if (stickyTemplates == null || stickyTemplates.Length == 0)
        {
            ShowTransientStatus("Sticky note templates are not ready yet.");
            return null;
        }

        Transform workspaceRoot = EnsureNoteWorkspaceRoot();
        if (workspaceRoot == null)
        {
            ShowTransientStatus("Sticky note workspace is not ready yet.");
            return null;
        }

        StickyNoteTemplate selectedTemplate = stickyTemplates[selectedTemplateIndex];
        StickyNoteSurface noteSurface = StickyNoteFactory.CreatePlacedNote(selectedTemplate, workspaceRoot, noteSerialNumber++);
        noteSurface.SetPageIndex(currentPageIndex);
        noteSurface.SetVisible(noteSurface.PageIndex == currentPageIndex);
        floatingNotes.Add(noteSurface);
        PositionNoteForPlacement(noteSurface.transform);

        ShowTransientStatus(selectedTemplate.TemplateName + " created.");
        PulseStylus(0.18f, 0.02f);
        return noteSurface;
    }

    private void RegisterExistingVoiceRecorders()
    {
        CanvasAudioRecorder[] recorders = FindObjectsByType<CanvasAudioRecorder>(FindObjectsSortMode.None);
        for (int i = 0; i < recorders.Length; i++)
        {
            CanvasAudioRecorder recorder = recorders[i];
            if (recorder == null || !recorder.canRecord)
            {
                continue;
            }

            if (!floatingVoiceNotes.Contains(recorder))
            {
                floatingVoiceNotes.Add(recorder);
            }
        }
    }

    private void PromoteVoiceRecorderToCurrentPage(CanvasAudioRecorder recorder)
    {
        if (recorder == null || !recorder.canRecord)
        {
            return;
        }

        Transform workspaceRoot = EnsureNoteWorkspaceRoot();
        if (workspaceRoot != null && recorder.transform.parent != workspaceRoot)
        {
            recorder.transform.SetParent(workspaceRoot, true);
        }

        recorder.SetPageIndex(currentPageIndex);

        if (!floatingVoiceNotes.Contains(recorder))
        {
            floatingVoiceNotes.Add(recorder);
        }

        recorder.SetVisible(!hideInactiveQRCodeContent || recorder.PageIndex == currentPageIndex);
    }

    private void RemoveNote(StickyNoteSurface noteSurface)
    {
        if (noteSurface == null)
        {
            return;
        }

        foreach (KeyValuePair<int, StickyNoteSurface[]> entry in placedNotesByPage)
        {
            StickyNoteSurface[] pageNotes = entry.Value;
            if (pageNotes == null)
            {
                continue;
            }

            for (int i = 0; i < pageNotes.Length; i++)
            {
                if (pageNotes[i] == noteSurface)
                {
                    pageNotes[i] = null;
                }
            }
        }

        floatingNotes.Remove(noteSurface);
        if (grabbedNote == noteSurface)
        {
            grabbedNote = null;
        }

        Destroy(noteSurface.gameObject);
        ShowTransientStatus("Sticky note removed.");
    }

    private void RemoveVoiceNote(CanvasAudioRecorder recorder)
    {
        if (recorder == null)
        {
            return;
        }

        floatingVoiceNotes.Remove(recorder);
        if (grabbedVoiceRecorder == recorder)
        {
            grabbedVoiceRecorder = null;
        }

        recorder.SetVisible(false);
        Destroy(recorder.gameObject);
        ShowTransientStatus("Voice note removed.");
    }

    private StickyNoteSurface[] GetNotesForPage(int pageIndex)
    {
        pageIndex = Mathf.Max(1, pageIndex);
        if (!placedNotesByPage.TryGetValue(pageIndex, out StickyNoteSurface[] pageNotes) || pageNotes == null)
        {
            pageNotes = new StickyNoteSurface[noteAnchors != null ? noteAnchors.Length : 0];
            placedNotesByPage[pageIndex] = pageNotes;
        }

        return pageNotes;
    }

    private int FindOpenAnchorIndex(StickyNoteSurface[] pageNotes)
    {
        if (pageNotes == null || pageNotes.Length == 0)
        {
            return -1;
        }

        for (int offset = 0; offset < pageNotes.Length; offset++)
        {
            int index = (nextAnchorIndex + offset) % pageNotes.Length;
            if (pageNotes[index] == null)
            {
                return index;
            }
        }

        return -1;
    }

    private Transform EnsureNoteWorkspaceRoot()
    {
        if (noteWorkspaceRoot != null)
        {
            return noteWorkspaceRoot;
        }

        GameObject existingRoot = GameObject.Find("StickyNoteWorldRoot");
        if (existingRoot != null)
        {
            noteWorkspaceRoot = existingRoot.transform;
            return noteWorkspaceRoot;
        }

        GameObject rootObject = new GameObject("StickyNoteWorldRoot");
        if (cameraRig != null && cameraRig.trackingSpace != null)
        {
            rootObject.transform.SetParent(cameraRig.trackingSpace, false);
        }

        noteWorkspaceRoot = rootObject.transform;
        return noteWorkspaceRoot;
    }

    private void PositionNoteForPlacement(Transform noteTransform)
    {
        if (noteTransform == null)
        {
            return;
        }

        Vector3 placementPosition = activeTipPose.position + activeAimPose.rotation * new Vector3(0f, 0f, notePlacementDistance);
        noteTransform.position = placementPosition;
        noteTransform.rotation = GetPlacementRotation(placementPosition);
    }

    private Quaternion GetPlacementRotation(Vector3 placementPosition)
    {
        Quaternion stylusRotation = activeAimPose.rotation * Quaternion.Euler(notePlacementRotationOffsetEuler);

        if (alignGrabbedNotesToStylus)
        {
            if (!keepGrabbedNotesUpright)
            {
                return stylusRotation;
            }

            Vector3 stylusForwardFlat = Vector3.ProjectOnPlane(stylusRotation * Vector3.forward, Vector3.up);
            if (stylusForwardFlat.sqrMagnitude >= 0.0001f)
            {
                return Quaternion.LookRotation(stylusForwardFlat.normalized, Vector3.up);
            }

            return stylusRotation;
        }

        if (cameraRig == null || cameraRig.centerEyeAnchor == null)
        {
            return stylusRotation;
        }

        Vector3 towardHead = cameraRig.centerEyeAnchor.position - placementPosition;
        Vector3 flatTowardHead = Vector3.ProjectOnPlane(towardHead, Vector3.up);
        if (flatTowardHead.sqrMagnitude < 0.0001f)
        {
            flatTowardHead = towardHead;
        }

        return Quaternion.LookRotation(flatTowardHead.normalized, Vector3.up);
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
                ShowTransientStatus(template.TemplateName + " selected.");
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

    private void RefreshActionButtons()
    {
        if (actionButtons == null)
        {
            return;
        }

        for (int i = 0; i < actionButtons.Length; i++)
        {
            if (actionButtons[i] == null)
            {
                continue;
            }

            bool selected = false;
            switch (actionButtons[i].Action)
            {
                case StickNoteMRActionButton.ActionKind.UseHighlight:
                    selected = currentTool == AnnotationTool.Highlight;
                    break;
                case StickNoteMRActionButton.ActionKind.UsePen:
                    selected = currentTool == AnnotationTool.Pen;
                    break;
                case StickNoteMRActionButton.ActionKind.UseAirInk:
                    selected = currentTool == AnnotationTool.AirInk;
                    break;
            }

            actionButtons[i].SetSelected(selected);
        }
    }

    private void ClearCurrentPageInk()
    {
        EndStroke();
        pageHighlightCanvas?.ClearPage(currentPageIndex);
        pageWritingCanvas?.ClearPage(currentPageIndex);
        airInkCanvas?.ClearPage(currentPageIndex);
        ShowTransientStatus("Page " + currentPageIndex + " ink cleared.");
        PulseStylus(0.12f, 0.015f);
    }

    private void EndStroke()
    {
        if (activeCanvas != null)
        {
            activeCanvas.EndStroke();
            activeCanvas = null;
        }

        if (activeAirInkCanvas != null)
        {
            activeAirInkCanvas.EndStroke();
            activeAirInkCanvas = null;
        }
    }

    private void ResetBoardPose()
    {
        if (cameraRig == null || cameraRig.centerEyeAnchor == null)
        {
            pageBoard.position = Vector3.zero;
            pageBoard.rotation = Quaternion.identity;
            return;
        }

        Transform head = cameraRig.centerEyeAnchor;
        Vector3 forwardFlat = Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized;
        if (forwardFlat.sqrMagnitude < 0.001f)
        {
            forwardFlat = Vector3.forward;
        }

        Vector3 worldOffset = head.TransformVector(boardResetLocalOffset);
        pageBoard.position = head.position + forwardFlat * boardResetDistance + worldOffset;
        pageBoard.rotation = Quaternion.LookRotation(forwardFlat, Vector3.up);
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
        }

        OVRManager.eyeFovPremultipliedAlphaModeEnabled = false;

        OVRPassthroughLayer passthroughLayer = cameraRig != null ? cameraRig.GetComponent<OVRPassthroughLayer>() : null;
        if (passthroughLayer == null && cameraRig != null)
        {
            passthroughLayer = cameraRig.gameObject.AddComponent<OVRPassthroughLayer>();
        }

        if (cameraRig != null)
        {
            OVRPassthroughLayer[] childLayers = cameraRig.GetComponentsInChildren<OVRPassthroughLayer>(true);
            for (int i = 0; i < childLayers.Length; i++)
            {
                if (childLayers[i] == null || childLayers[i] == passthroughLayer)
                {
                    continue;
                }

                childLayers[i].enabled = false;
            }
        }

        if (passthroughLayer != null)
        {
            passthroughLayer.enabled = true;
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
                rigCameras[i].stereoTargetEye = StereoTargetEyeMask.Both;
                rigCameras[i].allowHDR = false;
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

        string selectedTemplate = stickyTemplates != null && stickyTemplates.Length > 0
            ? stickyTemplates[selectedTemplateIndex].TemplateName
            : "None";

        string toolLabel;
        switch (currentTool)
        {
            case AnnotationTool.Highlight:
                toolLabel = "Highlight";
                break;
            case AnnotationTool.Pen:
                toolLabel = "Page Pen";
                break;
            default:
                toolLabel = "Air Ink";
                break;
        }

        string passthrough = OVRManager.IsInsightPassthroughInitialized()
            ? "MR passthrough active"
            : "Editor passthrough fallback";
        string input = mxInkActive ? "MX Ink connected" : "Quest controller fallback";

        statusText.text =
            "Stick Note MR\n" +
            passthrough + "\n" +
            input + "\n" +
            "Page: " + currentPageIndex + "\n" +
            "Tool: " + toolLabel + "\n" +
            "Selected note: " + selectedTemplate + "\n" +
            "Front on tool swatch: switch pen/highlighter\n" +
            "Front on color swatch: set current tool color\n" +
            "Front or Trigger on voice note: Spawn/Record/Stop/Play/Pause\n" +
            "Front on a sticky note: pick/place it\n" +
            "Front on a template: create a new sticky note\n" +
            "Tip writes on the placed sticky note\n" +
            "Middle on voice note moves it; empty-space middle moves frame\n" +
            "Rear click removes a note or clears page ink";

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

    private bool TryGetStylusPoses(out Pose aimPose, out Pose tipPose, out OVRPlugin.Hand hand)
    {
        if (TryGetStylusPoseForHand(OVRPlugin.Hand.HandRight, AimRight, TipPoseRight, out aimPose, out tipPose))
        {
            hand = OVRPlugin.Hand.HandRight;
            return true;
        }

        if (TryGetStylusPoseForHand(OVRPlugin.Hand.HandLeft, AimLeft, TipPoseLeft, out aimPose, out tipPose))
        {
            hand = OVRPlugin.Hand.HandLeft;
            return true;
        }

        aimPose = default;
        tipPose = default;
        hand = OVRPlugin.Hand.None;
        return false;
    }

    private static bool TryGetStylusPoseForHand(OVRPlugin.Hand hand, string aimAction, string tipAction, out Pose aimPose, out Pose tipPose)
    {
        aimPose = default;
        tipPose = default;

        if (!string.Equals(GetInteractionProfile(hand), MxInkProfile, System.StringComparison.Ordinal))
        {
            return false;
        }

        bool hasAim = OVRPlugin.GetActionStatePose(aimAction, hand, out OVRPlugin.Posef rawAimPose);
        bool hasTip = OVRPlugin.GetActionStatePose(tipAction, hand, out OVRPlugin.Posef rawTipPose);
        if (!hasAim && !hasTip)
        {
            return false;
        }

        if (hasAim)
        {
            aimPose = new Pose(
                rawAimPose.Position.FromFlippedZVector3f(),
                rawAimPose.Orientation.FromFlippedZQuatf());
        }

        if (hasTip)
        {
            tipPose = new Pose(
                rawTipPose.Position.FromFlippedZVector3f(),
                rawTipPose.Orientation.FromFlippedZQuatf());
        }

        if (!hasAim)
        {
            aimPose = tipPose;
        }

        if (!hasTip)
        {
            tipPose = aimPose;
        }

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

        RaycastHit[] forwardHits = Physics.RaycastAll(new Ray(activeAimPose.position - tipForward * 0.04f, tipForward), beamLength + 0.1f, ~0, QueryTriggerInteraction.Collide);
        Collider rayCollider = FindBestRaycastHit(forwardHits, out Vector3 hitPoint);
        if (rayCollider != null)
        {
            beamEnd = hitPoint;
            return rayCollider;
        }

        RaycastHit[] reverseHits = Physics.RaycastAll(new Ray(activeAimPose.position + tipForward * 0.04f, -tipForward), beamLength + 0.1f, ~0, QueryTriggerInteraction.Collide);
        rayCollider = FindBestRaycastHit(reverseHits, out hitPoint);
        if (rayCollider != null)
        {
            beamEnd = hitPoint;
        }

        return rayCollider;
    }

    private Collider FindBestRaycastHit(RaycastHit[] hits, out Vector3 point)
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

    private bool IsPageSurface(Collider candidate)
    {
        return pageSurfaceCollider != null &&
               candidate != null &&
               (candidate == pageSurfaceCollider || candidate.transform.IsChildOf(pageSurfaceCollider.transform));
    }

    private InkCanvas GetSelectedPageCanvas()
    {
        return currentTool == AnnotationTool.Pen ? pageWritingCanvas : pageHighlightCanvas;
    }

    private bool IsInteractive(Collider candidate)
    {
        return candidate != null &&
               (candidate.GetComponentInParent<InkCanvas>() != null ||
                candidate.GetComponentInParent<CanvasAudioRecorder>() != null ||
                candidate.GetComponentInParent<StickyNoteTemplate>() != null ||
                candidate.GetComponentInParent<StickyNoteSurface>() != null ||
                candidate.GetComponentInParent<StickNoteMRActionButton>() != null ||
                GetPaletteSwatch(candidate) != null ||
                GetToolSelectionSwatch(candidate) != null);
    }

    private void ResolveColorPaletteReferences()
    {
        if (colorPaletteSelectionRoot == null && pageBoard != null)
        {
            colorPaletteSelectionRoot = pageBoard.Find("ColorPaletteSelection");
            if (colorPaletteSelectionRoot == null)
            {
                colorPaletteSelectionRoot = pageBoard.Find("ColorPalatteSelection");
            }
        }

        if (writingPenDrawing == null)
        {
            writingPenDrawing = Object.FindAnyObjectByType<WritingPenDrawing>();
        }
    }

    private void ResolveToolSelectionReferences()
    {
        if (pageBoard == null)
        {
            return;
        }

        if (toolSelectionRoot == null)
        {
            toolSelectionRoot = pageBoard.Find("ToolSelection");
        }

        if (toolSelectionRoot == null)
        {
            toolSelectionRoot = FindChildRecursive(pageBoard, "ToolSelection");
        }

        if (toolSelectionRoot != null)
        {
            if (penToolSelectedVisual == null)
            {
                Transform target = toolSelectionRoot.Find("PenTool_Selected");
                if (target != null)
                {
                    penToolSelectedVisual = target.gameObject;
                }
            }

            if (penToolNotSelectedVisual == null)
            {
                Transform target = toolSelectionRoot.Find("PenTool_NotSelected");
                if (target != null)
                {
                    penToolNotSelectedVisual = target.gameObject;
                }
            }

            if (highlightToolSelectedVisual == null)
            {
                Transform target = toolSelectionRoot.Find("HighlightTool_Selected");
                if (target != null)
                {
                    highlightToolSelectedVisual = target.gameObject;
                }
            }

            if (highlightToolNotSelectedVisual == null)
            {
                Transform target = toolSelectionRoot.Find("HighlightTool_NotSelected");
                if (target != null)
                {
                    highlightToolNotSelectedVisual = target.gameObject;
                }
            }
        }

        if (penColorLabel == null)
        {
            Transform target = pageBoard.Find("Panel/PenColor");
            if (target == null)
            {
                target = FindChildRecursive(pageBoard, "PenColor");
            }

            if (target != null)
            {
                penColorLabel = target.gameObject;
            }
        }

        if (highlightLabel == null)
        {
            Transform target = pageBoard.Find("Panel/Highlight");
            if (target == null)
            {
                target = FindChildRecursive(pageBoard, "Highlight");
            }

            if (target != null)
            {
                highlightLabel = target.gameObject;
            }
        }
    }

    private static Transform FindChildRecursive(Transform root, string childName)
    {
        if (root == null || string.IsNullOrEmpty(childName))
        {
            return null;
        }

        for (int i = 0; i < root.childCount; i++)
        {
            Transform child = root.GetChild(i);
            if (child.name == childName)
            {
                return child;
            }

            Transform nested = FindChildRecursive(child, childName);
            if (nested != null)
            {
                return nested;
            }
        }

        return null;
    }

    private void SyncToolModeFromToolSelectionVisuals()
    {
        bool penSelected = penToolSelectedVisual != null && penToolSelectedVisual.activeSelf;
        bool highlightSelected = highlightToolSelectedVisual != null && highlightToolSelectedVisual.activeSelf;

        if (penSelected == highlightSelected)
        {
            return;
        }

        currentTool = penSelected ? AnnotationTool.Pen : AnnotationTool.Highlight;
    }

    private void ApplyToolSelectionVisuals()
    {
        if (currentTool != AnnotationTool.Pen && currentTool != AnnotationTool.Highlight)
        {
            return;
        }

        bool penMode = currentTool == AnnotationTool.Pen;
        SetActiveState(penToolSelectedVisual, penMode);
        SetActiveState(penToolNotSelectedVisual, !penMode);
        SetActiveState(highlightToolSelectedVisual, !penMode);
        SetActiveState(highlightToolNotSelectedVisual, penMode);
        SetActiveState(penColorLabel, penMode);
        SetActiveState(highlightLabel, !penMode);
    }

    private void ApplyWritingPenToolMode()
    {
        if (writingPenDrawing == null)
        {
            writingPenDrawing = Object.FindAnyObjectByType<WritingPenDrawing>();
        }

        if (writingPenDrawing == null)
        {
            return;
        }

        if (currentTool == AnnotationTool.Highlight)
        {
            writingPenDrawing.SetHighlightMode(true);
        }
        else if (currentTool == AnnotationTool.Pen)
        {
            writingPenDrawing.SetHighlightMode(false);
        }
    }

    private static void SetActiveState(GameObject target, bool shouldBeActive)
    {
        if (target == null || target.activeSelf == shouldBeActive)
        {
            return;
        }

        target.SetActive(shouldBeActive);
    }

    private void EnsurePaletteCanvasFacing()
    {
        if (colorPaletteSelectionRoot == null)
        {
            return;
        }

        Canvas paletteCanvas = colorPaletteSelectionRoot.GetComponent<Canvas>();
        if (paletteCanvas == null || paletteCanvas.renderMode != RenderMode.WorldSpace)
        {
            return;
        }

        Vector3 localEuler = colorPaletteSelectionRoot.localEulerAngles;
        colorPaletteSelectionRoot.localRotation = Quaternion.Euler(localEuler.x, 180f, localEuler.z);
    }

    private Transform GetPaletteSwatch(Collider candidate)
    {
        if (candidate == null || colorPaletteSelectionRoot == null)
        {
            return null;
        }

        Transform current = candidate.transform;
        while (current != null && current != colorPaletteSelectionRoot)
        {
            if (current.parent == colorPaletteSelectionRoot)
            {
                return current;
            }

            current = current.parent;
        }

        return null;
    }

    private Transform GetToolSelectionSwatch(Collider candidate)
    {
        if (candidate == null || toolSelectionRoot == null)
        {
            return null;
        }

        Transform current = candidate.transform;
        while (current != null && current != toolSelectionRoot)
        {
            if (current.parent == toolSelectionRoot)
            {
                string toolName = current.name.ToLowerInvariant();
                if (toolName.Contains("pentool") || toolName.Contains("highlighttool"))
                {
                    return current;
                }

                return null;
            }

            current = current.parent;
        }

        return null;
    }

    private void ApplyHoveredToolSwatch()
    {
        if (hoveredToolSwatch == null)
        {
            return;
        }

        string swatchName = hoveredToolSwatch.name.ToLowerInvariant();
        if (swatchName.Contains("highlight"))
        {
            SetTool(AnnotationTool.Highlight);
        }
        else if (swatchName.Contains("pen"))
        {
            SetTool(AnnotationTool.Pen);
        }
        else
        {
            ShowTransientStatus("Unknown tool selection.");
            return;
        }

        PulseStylus(0.09f, 0.012f);
    }

    private void ApplyHoveredColorSwatch()
    {
        if (hoveredColorSwatch == null)
        {
            return;
        }

        if (writingPenDrawing == null)
        {
            writingPenDrawing = Object.FindAnyObjectByType<WritingPenDrawing>();
        }

        if (writingPenDrawing == null)
        {
            ShowTransientStatus("Drawing target not found.");
            return;
        }

        string colorTarget = currentTool == AnnotationTool.Highlight ? "Highlight color: " : "Pen color: ";
        string swatchName = hoveredColorSwatch.name.ToLowerInvariant();
        if (swatchName.Contains("yellow"))
        {
            writingPenDrawing.SetColorYellow();
            ShowTransientStatus(colorTarget + "Yellow");
        }
        else if (swatchName.Contains("green"))
        {
            writingPenDrawing.SetColorGreen();
            ShowTransientStatus(colorTarget + "Green");
        }
        else if (swatchName.Contains("blue"))
        {
            writingPenDrawing.SetColorBlue();
            ShowTransientStatus(colorTarget + "Blue");
        }
        else if (swatchName.Contains("pink"))
        {
            writingPenDrawing.SetColorPink();
            ShowTransientStatus(colorTarget + "Pink");
        }
        else if (swatchName.Contains("red"))
        {
            writingPenDrawing.SetColorRed();
            ShowTransientStatus(colorTarget + "Red");
        }
        else
        {
            ShowTransientStatus("Unknown color swatch.");
            return;
        }

        PulseStylus(0.09f, 0.012f);
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
