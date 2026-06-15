using UnityEngine;

public class VRStylusControllerHandling : StylusHandler
{
    [SerializeField] private GameObject _mxInk_model;
    [SerializeField] private GameObject _tip;
    [SerializeField] private GameObject _cluster_front;
    [SerializeField] private GameObject _cluster_middle;
    [SerializeField] private GameObject _cluster_back;

    [SerializeField] private GameObject _left_touch_controller;
    [SerializeField] private GameObject _right_touch_controller;

    public Color active_color = Color.green;
    public Color double_tap_active_color = Color.cyan;
    public Color default_color = Color.white;

    private const string MX_Ink_Pose_Right = "aim_right";
    private const string MX_Ink_Pose_Left = "aim_left";
    private const string MX_Ink_TipForce = "tip";
    private const string MX_Ink_MiddleForce = "middle";
    private const string MX_Ink_ClusterFront = "front";
    private const string MX_Ink_ClusterBack = "back";
    private const string MX_Ink_ClusterBack_DoubleTap = "back_double_tap";
    private const string MX_Ink_Haptic_Pulse = "haptic_pulse";

    private bool usingControllerFallback = false;

    private void UpdatePose()
    {
        var leftDevice = OVRPlugin.GetCurrentInteractionProfileName(OVRPlugin.Hand.HandLeft);
        var rightDevice = OVRPlugin.GetCurrentInteractionProfileName(OVRPlugin.Hand.HandRight);

        bool stylusLeft = leftDevice.Contains("logitech");
        bool stylusRight = rightDevice.Contains("logitech");

        _stylus.isActive = stylusLeft || stylusRight;
        _stylus.isOnRightHand = stylusRight;

        usingControllerFallback = !_stylus.isActive;

        _mxInk_model.SetActive(_stylus.isActive);
        _right_touch_controller.SetActive(!_stylus.isOnRightHand || !_stylus.isActive);
        _left_touch_controller.SetActive(_stylus.isOnRightHand || !_stylus.isActive);

        if (_stylus.isActive)
        {
            string pose = _stylus.isOnRightHand ? MX_Ink_Pose_Right : MX_Ink_Pose_Left;

            if (OVRPlugin.GetActionStatePose(pose, out OVRPlugin.Posef handPose))
            {
                transform.localPosition = handPose.Position.FromFlippedZVector3f();
                transform.localRotation = handPose.Orientation.FromFlippedZQuatf();
                _stylus.inkingPose.position = transform.localPosition;
                _stylus.inkingPose.rotation = transform.localRotation;
            }
        }
    }

    void Update()
    {
        OVRInput.Update();
        UpdatePose();

        if (usingControllerFallback)
        {
            HandleControllerInput();
            Transform controllerTransform = _right_touch_controller.transform;
            _stylus.inkingPose.position = controllerTransform.position;
            _stylus.inkingPose.rotation = controllerTransform.rotation;
        }
        else
        {
            HandleStylusInput();
        }

        UpdateVisuals();
    }

    void HandleStylusInput()
    {
        OVRPlugin.GetActionStateFloat(MX_Ink_TipForce, out _stylus.tip_value);
        OVRPlugin.GetActionStateFloat(MX_Ink_MiddleForce, out _stylus.cluster_middle_value);
        OVRPlugin.GetActionStateBoolean(MX_Ink_ClusterFront, out _stylus.cluster_front_value);
        OVRPlugin.GetActionStateBoolean(MX_Ink_ClusterBack, out _stylus.cluster_back_value);
        OVRPlugin.GetActionStateBoolean(MX_Ink_ClusterBack_DoubleTap, out _stylus.cluster_back_double_tap_value);
    }

    void HandleControllerInput()
    {
        OVRInput.Controller controller = OVRInput.Controller.RTouch;

        _stylus.tip_value = OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger, controller);
        _stylus.cluster_middle_value = OVRInput.Get(OVRInput.Axis1D.PrimaryHandTrigger, controller);

        _stylus.cluster_front_value = OVRInput.Get(OVRInput.Button.One, controller);
        _stylus.cluster_back_value = OVRInput.Get(OVRInput.Button.Two, controller);

        _stylus.cluster_back_double_tap_value = OVRInput.GetDown(OVRInput.Button.PrimaryThumbstick, controller);
    }

    void UpdateVisuals()
    {
        _stylus.any = _stylus.tip_value > 0 ||
                      _stylus.cluster_front_value ||
                      _stylus.cluster_middle_value > 0 ||
                      _stylus.cluster_back_value ||
                      _stylus.cluster_back_double_tap_value;

        _tip.GetComponent<MeshRenderer>().material.color =
            _stylus.tip_value > 0 ? active_color : default_color;

        _cluster_front.GetComponent<MeshRenderer>().material.color =
            _stylus.cluster_front_value ? active_color : default_color;

        _cluster_middle.GetComponent<MeshRenderer>().material.color =
            _stylus.cluster_middle_value > 0 ? active_color : default_color;

        if (_stylus.cluster_back_value)
        {
            _cluster_back.GetComponent<MeshRenderer>().material.color = active_color;
        }
        else
        {
            _cluster_back.GetComponent<MeshRenderer>().material.color =
                _stylus.cluster_back_double_tap_value ? double_tap_active_color : default_color;
        }
    }

    public override void TriggerHapticPulse(float amplitude, float duration)
    {
        if (!_stylus.isActive)
        {
            return;
        }

        OVRPlugin.Hand holdingHand = _stylus.isOnRightHand ? OVRPlugin.Hand.HandRight : OVRPlugin.Hand.HandLeft;
        OVRPlugin.TriggerVibrationAction(MX_Ink_Haptic_Pulse, holdingHand, duration, amplitude);
    }
}