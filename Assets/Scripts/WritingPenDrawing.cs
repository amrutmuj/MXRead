using System;
using System.Collections.Generic;
using Meta.XR.MRUtilityKit;
using Meta.XR.MRUtilityKitSamples.QRCodeDetection;
using UnityEngine;
using UnityEngine.Rendering;

public class WritingPenDrawing : MonoBehaviour
{
    private struct DrawnLineRecord
    {
        public GameObject LineObject;
        public int PageId;
    }

    private CircleToGeminiController controller;
    private readonly List<DrawnLineRecord> _lineRecords = new List<DrawnLineRecord>();
    private readonly Dictionary<int, Transform> _pageContentRoots = new Dictionary<int, Transform>();
    private LineRenderer _currentLine;
    private readonly List<float> _currentLineWidths = new List<float>();

    [SerializeField] float _maxLineWidth = 0.01f;
    [SerializeField] float _minLineWidth = 0.005f;

    [SerializeField] Material _material;
    [SerializeField] private bool syncWithQRCodePages = true;
    [SerializeField] private bool hideInactiveQRCodePages = true;
    [SerializeField] private bool useLocalSpaceLines = true;

    [SerializeField] private Color _currentColor = new Color32(0, 127, 255, 255);
    [SerializeField] [Range(0f, 1f)] private float penAlpha = 1f;
    [SerializeField] [Range(0f, 1f)] private float highlightAlpha = 0.2f;
    [SerializeField] private bool highlightMode;

    public Color CurrentColor
    {
        get { return _currentColor; }
        set { SetCurrentColor(value); }
    }

    public float MaxLineWidth
    {
        get { return _maxLineWidth; }
        set { _maxLineWidth = value; }
    }

    private bool _lineWidthIsFixed = false;
    public bool LineWidthIsFixed
    {
        get { return _lineWidthIsFixed; }
        set { _lineWidthIsFixed = value; }
    }

    private bool _isDrawing = false;
    private bool _doubleTapDetected = false;

    [SerializeField]
    private float longPressDuration = 1.0f;
    private float buttonPressedTimestamp = 0;

    [SerializeField]
    private StylusHandler _stylusHandler;

    private int _activePageId = 1;
    private Transform _activePageRoot;

    private Vector3 _previousLinePoint;
    private const float _minDistanceBetweenLinePoints = 0.0005f;
    [SerializeField] [Range(0f, 1f)] private float drawInputThreshold = 0.01f;

    [SerializeField] private Color yellow = new Color32(242, 211, 79, 255);
    [SerializeField] private Color green = new Color32(126, 217, 87, 255);
    [SerializeField] private Color blue = new Color32(0, 127, 255, 255);
    [SerializeField] private Color pink = new Color32(228, 138, 179, 255);
    [SerializeField] private Color red = new Color32(231, 76, 76, 255);

    private void OnEnable()
    {
        QRCodeManager.ActivePageChanged += HandleActiveQRCodePageChanged;
    }

    private void OnDisable()
    {
        QRCodeManager.ActivePageChanged -= HandleActiveQRCodePageChanged;
    }

    void Start()
    {
        controller = FindObjectOfType<CircleToGeminiController>();
        SetCurrentColor(_currentColor);
        SyncFromActiveQRCode();
        SetPageLineVisibility();
    }

    private void HandleActiveQRCodePageChanged(int pageId, MRUKTrackable trackable, string payload)
    {
        if (!syncWithQRCodePages)
        {
            return;
        }

        ApplyActiveQRCodePage(pageId, trackable, payload);
    }

    private void SyncFromActiveQRCode()
    {
        if (!syncWithQRCodePages)
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

        int previousPageId = _activePageId;
        Transform previousPageRoot = _activePageRoot;

        _activePageId = pageId;
        _activePageRoot = GetOrCreatePageRoot(pageId, trackable.transform, payload);

        bool pageChanged = previousPageId != _activePageId;
        bool pageRootChanged = previousPageRoot != _activePageRoot;

        if (!pageChanged && !pageRootChanged)
        {
            return;
        }

        if (_isDrawing)
        {
            ResetStrokeState();
        }

        SetPageLineVisibility();
    }

    private Transform GetOrCreatePageRoot(int pageId, Transform trackableTransform, string payload)
    {
        if (trackableTransform == null)
        {
            return null;
        }

        if (!_pageContentRoots.TryGetValue(pageId, out Transform root) || root == null)
        {
            string rootName = string.IsNullOrEmpty(payload)
                ? "QRPageContent_" + pageId
                : "QRPageContent_" + pageId + "_" + payload;

            GameObject rootObject = new GameObject(rootName);
            root = rootObject.transform;
            _pageContentRoots[pageId] = root;
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

    private void StartNewLine(Vector3 startPosition, float width)
    {
        var gameObject = new GameObject("line");
        if (_activePageRoot != null)
        {
            gameObject.transform.SetParent(_activePageRoot, false);
        }
        else
        {
            gameObject.transform.SetParent(transform, false);
        }

        LineRenderer lineRenderer = gameObject.AddComponent<LineRenderer>();

        _currentLine = lineRenderer;
        _currentLine.positionCount = 0;

        _currentLine.material = _material;
        if (_currentLine.material == null)
        {
            _currentLine.material = new Material(Shader.Find("Sprites/Default"));
        }

        _currentLine.material.color = _currentColor;
        _currentLine.startColor = _currentColor;
        _currentLine.endColor = _currentColor;

        _currentLine.loop = false;
        _currentLine.startWidth = _minLineWidth;
        _currentLine.endWidth = _minLineWidth;

        _currentLine.useWorldSpace = !useLocalSpaceLines;
        _currentLine.alignment = LineAlignment.View;
        _currentLine.widthCurve = new AnimationCurve();
        _currentLineWidths.Clear();

        _currentLine.shadowCastingMode = ShadowCastingMode.Off;
        _currentLine.receiveShadows = false;

        _lineRecords.Add(new DrawnLineRecord
        {
            LineObject = gameObject,
            PageId = _activePageId
        });

        SetPageLineVisibility();
        _previousLinePoint = Vector3.positiveInfinity;
        AddPoint(startPosition, width, true);
    }

    private void TriggerHaptics()
    {
        const float dampingFactor = 0.6f;
        const float duration = 0.01f;
        float middleButtonPressure = _stylusHandler.CurrentState.cluster_middle_value * dampingFactor;
        _stylusHandler.TriggerHapticPulse(middleButtonPressure, duration);
    }

    private void AddPoint(Vector3 position, float width, bool forcePoint = false)
    {
        if (_currentLine == null)
        {
            return;
        }

        if (forcePoint || Vector3.Distance(position, _previousLinePoint) > _minDistanceBetweenLinePoints)
        {
            TriggerHaptics();
            _previousLinePoint = position;

            _currentLine.positionCount++;
            _currentLineWidths.Add(Math.Max(width * _maxLineWidth, _minLineWidth));

            Vector3 localPosition = position;
            Transform lineParent = _currentLine.transform.parent;
            if (!_currentLine.useWorldSpace && lineParent != null)
            {
                localPosition = lineParent.InverseTransformPoint(position);
            }

            _currentLine.SetPosition(_currentLine.positionCount - 1, localPosition);

            AnimationCurve curve = new AnimationCurve();

            if (_currentLineWidths.Count > 1)
            {
                for (int i = 0; i < _currentLineWidths.Count; i++)
                {
                    curve.AddKey(i / (float)(_currentLineWidths.Count - 1), _currentLineWidths[i]);
                }
            }
            else
            {
                curve.AddKey(0, _currentLineWidths[0]);
            }

            _currentLine.widthCurve = curve;
        }
    }

    private void ResetStrokeState()
    {
        _isDrawing = false;
        _currentLine = null;
        _currentLineWidths.Clear();
        _previousLinePoint = Vector3.positiveInfinity;
    }

    private void EndCurrentStroke()
    {
        if (_currentLine != null && _currentLine.positionCount > 20)
        {
            Vector3 first = GetLinePoint(_currentLine, 0);
            Vector3 last = GetLinePoint(_currentLine, _currentLine.positionCount - 1);

            if (Vector3.Distance(first, last) < 0.05f)
            {
                CloseCurrentLine();
                if (controller != null)
                {
                    controller.ProcessCircle();
                }
            }
        }

        ResetStrokeState();
    }

    private void RemoveLastLine()
    {
        for (int i = _lineRecords.Count - 1; i >= 0; i--)
        {
            DrawnLineRecord record = _lineRecords[i];
            if (record.PageId != _activePageId || record.LineObject == null)
            {
                continue;
            }

            _lineRecords.RemoveAt(i);
            Destroy(record.LineObject);
            break;
        }
    }

    private void ClearAllLines()
    {
        for (int i = _lineRecords.Count - 1; i >= 0; i--)
        {
            DrawnLineRecord record = _lineRecords[i];
            if (record.PageId != _activePageId)
            {
                continue;
            }

            if (record.LineObject != null)
            {
                Destroy(record.LineObject);
            }

            _lineRecords.RemoveAt(i);
        }

        SetPageLineVisibility();
    }

    private bool HasLinesOnActivePage()
    {
        for (int i = 0; i < _lineRecords.Count; i++)
        {
            if (_lineRecords[i].PageId == _activePageId && _lineRecords[i].LineObject != null)
            {
                return true;
            }
        }

        return false;
    }

    private Vector3 GetLinePoint(LineRenderer line, int index)
    {
        Vector3 point = line.GetPosition(index);
        if (line.useWorldSpace || line.transform.parent == null)
        {
            return point;
        }

        return line.transform.parent.TransformPoint(point);
    }

    private void SetPageLineVisibility()
    {
        if (!hideInactiveQRCodePages)
        {
            return;
        }

        for (int i = 0; i < _lineRecords.Count; i++)
        {
            DrawnLineRecord record = _lineRecords[i];
            if (record.LineObject == null)
            {
                continue;
            }

            bool shouldBeVisible = record.PageId == _activePageId;
            if (record.LineObject.activeSelf != shouldBeVisible)
            {
                record.LineObject.SetActive(shouldBeVisible);
            }
        }
    }

    private void HandleColorInput()
    {
        if (Input.GetKeyDown(KeyCode.Keypad1)) SetColorYellow();
        if (Input.GetKeyDown(KeyCode.Keypad2)) SetColorGreen();
        if (Input.GetKeyDown(KeyCode.Keypad3)) SetColorBlue();
        if (Input.GetKeyDown(KeyCode.Keypad4)) SetColorPink();
        if (Input.GetKeyDown(KeyCode.Keypad5)) SetColorRed();
    }

    public void SetHighlightMode(bool enabled)
    {
        highlightMode = enabled;
        SetCurrentColor(_currentColor);
    }

    public void SetHighlightMode()
    {
        SetHighlightMode(true);
    }

    public void SetPenMode()
    {
        SetHighlightMode(false);
    }

    public void SetColorYellow()
    {
        SetCurrentColor(yellow);
    }

    public void SetColorGreen()
    {
        SetCurrentColor(green);
    }

    public void SetColorBlue()
    {
        SetCurrentColor(blue);
    }

    public void SetColorPink()
    {
        SetCurrentColor(pink);
    }

    public void SetColorRed()
    {
        SetCurrentColor(red);
    }

    private Color ApplyToolAlpha(Color color)
    {
        color.a = highlightMode ? highlightAlpha : penAlpha;
        return color;
    }

    private void SetCurrentColor(Color color)
    {
        _currentColor = ApplyToolAlpha(color);
        RefreshCurrentLineColor();
    }

    private void RefreshCurrentLineColor()
    {
        if (_currentLine == null)
        {
            return;
        }

        if (_currentLine.material != null)
        {
            _currentLine.material.color = _currentColor;
        }

        _currentLine.startColor = _currentColor;
        _currentLine.endColor = _currentColor;
    }

    void Update()
    {
        SyncFromActiveQRCode();

        if (_stylusHandler == null)
        {
            return;
        }

        float analogInput = Mathf.Max(
            _stylusHandler.CurrentState.tip_value,
            _stylusHandler.CurrentState.cluster_middle_value
        );

        bool canDraw = _stylusHandler.CanDraw();
        bool hasDrawInput = analogInput > drawInputThreshold;

        if (hasDrawInput && canDraw)
        {
            float normalizedWidth = _lineWidthIsFixed ? 1.0f : analogInput;
            Vector3 tipPosition = _stylusHandler.CurrentState.inkingPose.position;

            if (!_isDrawing)
            {
                StartNewLine(tipPosition, normalizedWidth);
                _isDrawing = true;
            }
            else
            {
                AddPoint(tipPosition, normalizedWidth);
            }
        }
        else if (_isDrawing)
        {
            EndCurrentStroke();
        }

        if (_stylusHandler.CurrentState.cluster_back_double_tap_value ||
            _stylusHandler.CurrentState.cluster_back_value)
        {
            if (HasLinesOnActivePage() && !_doubleTapDetected)
            {
                buttonPressedTimestamp = Time.time;
                RemoveLastLine();
            }

            _doubleTapDetected = true;

            if (HasLinesOnActivePage() &&
                Time.time >= (buttonPressedTimestamp + longPressDuration))
            {
                ClearAllLines();
            }
        }
        else
        {
            _doubleTapDetected = false;
        }
    }

    public List<Vector3> GetDrawnPoints()
    {
        List<Vector3> pts = new List<Vector3>();

        if (_currentLine == null || _currentLine.positionCount == 0)
            return pts;

        for (int i = 0; i < _currentLine.positionCount; i++)
        {
            pts.Add(GetLinePoint(_currentLine, i));
        }

        return pts;
    }

    public void CloseCurrentLine()
    {
        if (_currentLine == null || _currentLine.positionCount < 3)
            return;

        Vector3 first = _currentLine.GetPosition(0);
        _currentLine.positionCount++;
        _currentLine.SetPosition(_currentLine.positionCount - 1, first);
    }
}
