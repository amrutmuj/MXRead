using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.Android;

[RequireComponent(typeof(AudioSource))]
public class CanvasAudioRecorder : MonoBehaviour
{
    public Button interactionButton;
    public TextMeshProUGUI buttonText;

    // NEW: This locks the original canvas so it can't record
    public bool canRecord = false;
    [SerializeField] private int pageIndex = 1;

    [Header("Optional Shared Custom Reticle")]
    public bool useSharedCustomReticle = true;
    public bool onlyWhenUnlocked = true;
    public bool useTriggerInteraction = true;
    public Transform rayOrigin;
    public LineRenderer sharedBeam;
    public Transform sharedReticle;
    public float maxReticleDistance = 1.5f;
    public LayerMask reticleLayerMask = ~0;

    [Header("Interactor Integration")]
    public bool useExternalInteractor = true;

    private AudioSource audioSource;
    private Collider[] ownColliders;
    private bool isAimingRecorder;
    private string activeMicrophoneDevice;
    private float recordingStartTimestamp;
    private const int RecordingSampleRate = 16000;
    private const float MinimumRecordingDurationSeconds = 0.12f;
    private enum MemoState { ReadyToRecord, Recording, Paused, Playing }
    private MemoState currentState = MemoState.ReadyToRecord;

    public int PageIndex => Mathf.Max(1, pageIndex);

    public void SetPageIndex(int index)
    {
        pageIndex = Mathf.Max(1, index);
    }

    public void SetVisible(bool isVisible)
    {
        if (gameObject.activeSelf == isVisible)
        {
            return;
        }

        if (!isVisible)
        {
            if (currentState == MemoState.Recording)
            {
                if (Microphone.IsRecording(activeMicrophoneDevice))
                {
                    Microphone.End(activeMicrophoneDevice);
                }

                currentState = MemoState.ReadyToRecord;
            }
            else if (currentState == MemoState.Playing)
            {
                audioSource.Stop();
                currentState = audioSource.clip != null ? MemoState.Paused : MemoState.ReadyToRecord;
            }
        }

        gameObject.SetActive(isVisible);

        if (isVisible)
        {
            UpdateUIText();
        }
    }

    void Start()
    {
        audioSource = GetComponent<AudioSource>();
        ownColliders = GetComponentsInChildren<Collider>(true);

        // Voice notes should be clearly audible regardless of world position.
        audioSource.playOnAwake = false;
        audioSource.loop = false;
        audioSource.spatialBlend = 0f;
        audioSource.volume = 1f;
        audioSource.ignoreListenerPause = true;
        audioSource.ignoreListenerVolume = true;

        ResolveMicrophoneDevice();

        AutoResolveReticleReferences();

        if (!Permission.HasUserAuthorizedPermission(Permission.Microphone))
        {
            Permission.RequestUserPermission(Permission.Microphone);
        }

        if (interactionButton != null)
        {
            interactionButton.onClick.AddListener(OnCanvasTapped);
        }
        UpdateUIText();
    }

    void Update()
    {
        if (currentState == MemoState.Playing && !audioSource.isPlaying)
        {
            audioSource.time = 0f;
            currentState = MemoState.Paused;
            UpdateUIText();
        }

        if (!useExternalInteractor)
        {
            UpdateSharedReticle();
            HandleTriggerInteraction();
        }
    }

    public bool TryGetInteractorHover(Collider candidate, Vector3 probePosition, out Vector3 hitPoint, out Vector3 hitNormal)
    {
        hitPoint = default;
        hitNormal = Vector3.up;

        if (!canRecord || !IsOwnedCollider(candidate))
        {
            return false;
        }

        hitPoint = candidate.ClosestPoint(probePosition);
        Vector3 surfaceNormal = probePosition - hitPoint;
        if (surfaceNormal.sqrMagnitude < 0.0001f)
        {
            surfaceNormal = transform.forward;
        }

        hitNormal = surfaceNormal.normalized;
        return true;
    }

    public void TriggerFromInteractor()
    {
        if (!canRecord)
        {
            if (TryUnlockFromSpawner())
            {
                // First trigger on a locked desk note now unlocks and starts recording.
                OnCanvasTapped();
            }
            return;
        }

        OnCanvasTapped();
    }

    public bool TryUnlockFromSpawner()
    {
        if (canRecord)
        {
            return true;
        }

        CanvasSpawner spawner = GetComponent<CanvasSpawner>();
        if (spawner == null)
        {
            return false;
        }

        spawner.LeaveCloneBehind();
        return canRecord;
    }

    private void OnCanvasTapped()
    {
        // NEW: If this is the original canvas on the desk, do nothing when poked!
        if (!canRecord) return;

        switch (currentState)
        {
            case MemoState.ReadyToRecord:
                if (!Permission.HasUserAuthorizedPermission(Permission.Microphone))
                {
                    Permission.RequestUserPermission(Permission.Microphone);
                    return;
                }

                ResolveMicrophoneDevice();
                audioSource.Stop();
                audioSource.time = 0f;
                audioSource.clip = Microphone.Start(activeMicrophoneDevice, false, 60, RecordingSampleRate);
                if (audioSource.clip == null)
                {
                    currentState = MemoState.ReadyToRecord;
                    break;
                }

                recordingStartTimestamp = Time.unscaledTime;
                currentState = MemoState.Recording;
                break;

            case MemoState.Recording:
                int recordedSamples = Microphone.GetPosition(activeMicrophoneDevice);
                Microphone.End(activeMicrophoneDevice);

                float recordedDuration = Time.unscaledTime - recordingStartTimestamp;

                if (recordedSamples <= 0 || recordedDuration < MinimumRecordingDurationSeconds)
                {
                    audioSource.clip = null;
                    currentState = MemoState.ReadyToRecord;
                    break;
                }

                if (audioSource.clip != null && recordedSamples > 0 && recordedSamples < audioSource.clip.samples)
                {
                    audioSource.clip = TrimClip(audioSource.clip, recordedSamples);
                }

                audioSource.time = 0f;
                currentState = MemoState.Paused;
                break;

            case MemoState.Paused:
                if (audioSource.clip == null || audioSource.clip.samples <= 0)
                {
                    currentState = MemoState.ReadyToRecord;
                    break;
                }

                if (audioSource.time >= audioSource.clip.length - 0.01f)
                {
                    audioSource.time = 0f;
                }

                audioSource.Play();
                if (!audioSource.isPlaying)
                {
                    currentState = MemoState.Paused;
                    break;
                }

                currentState = MemoState.Playing;
                break;

            case MemoState.Playing:
                audioSource.Pause();
                currentState = MemoState.Paused;
                break;
        }
        UpdateUIText();
    }

    // Made public so the spawner script can trigger UI updates
    public void UpdateUIText()
    {
        // NEW: Visual state for the original spawner canvas
        if (!canRecord)
        {
            buttonText.text = "Grab to Use";
            buttonText.color = Color.gray;
            return;
        }

        switch (currentState)
        {
            case MemoState.ReadyToRecord:
                buttonText.text = "Trigger: Record";
                buttonText.color = Color.red;
                break;
            case MemoState.Recording:
                buttonText.text = "Trigger: Stop";
                buttonText.color = Color.white;
                break;
            case MemoState.Paused:
                buttonText.text = "Trigger: Play";
                buttonText.color = Color.green;
                break;
            case MemoState.Playing:
                buttonText.text = "Trigger: Pause";
                buttonText.color = Color.yellow;
                break;
        }
    }

    private void HandleTriggerInteraction()
    {
        if (!useTriggerInteraction || !canRecord || !isAimingRecorder)
        {
            return;
        }

        if (OVRInput.GetDown(OVRInput.Button.SecondaryIndexTrigger) || OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger))
        {
            OnCanvasTapped();
        }
    }

    private AudioClip TrimClip(AudioClip sourceClip, int sampleCount)
    {
        sampleCount = Mathf.Clamp(sampleCount, 1, sourceClip.samples);
        int totalValues = sampleCount * sourceClip.channels;
        float[] data = new float[totalValues];
        sourceClip.GetData(data, 0);

        AudioClip trimmed = AudioClip.Create(sourceClip.name + "_Trimmed", sampleCount, sourceClip.channels, sourceClip.frequency, false);
        trimmed.SetData(data, 0);
        return trimmed;
    }

    private void ResolveMicrophoneDevice()
    {
        activeMicrophoneDevice = null;
        string[] devices = Microphone.devices;
        if (devices != null && devices.Length > 0)
        {
            activeMicrophoneDevice = devices[0];
        }
    }

    private void AutoResolveReticleReferences()
    {
        if (!useSharedCustomReticle)
        {
            return;
        }

        if (sharedReticle == null)
        {
            GameObject reticleObject = GameObject.Find("ControllerReticle");
            if (reticleObject != null)
            {
                sharedReticle = reticleObject.transform;
            }
        }

        if (sharedBeam == null)
        {
            GameObject beamObject = GameObject.Find("ControllerBeam");
            if (beamObject != null)
            {
                sharedBeam = beamObject.GetComponent<LineRenderer>();
            }
        }

        if (rayOrigin == null)
        {
            OVRCameraRig rig = FindAnyObjectByType<OVRCameraRig>();
            if (rig != null)
            {
                rayOrigin = rig.rightControllerAnchor != null ? rig.rightControllerAnchor : rig.centerEyeAnchor;
            }
        }
    }

    private void UpdateSharedReticle()
    {
        isAimingRecorder = false;

        if (!useSharedCustomReticle)
        {
            return;
        }

        if (onlyWhenUnlocked && !canRecord)
        {
            return;
        }

        if (rayOrigin == null || sharedReticle == null)
        {
            return;
        }

        Ray ray = new Ray(rayOrigin.position, rayOrigin.forward);
        if (!Physics.Raycast(ray, out RaycastHit hit, maxReticleDistance, reticleLayerMask, QueryTriggerInteraction.Collide))
        {
            return;
        }

        if (!IsOwnedCollider(hit.collider))
        {
            return;
        }

        isAimingRecorder = true;

        sharedReticle.gameObject.SetActive(true);
        sharedReticle.position = hit.point;
        sharedReticle.rotation = Quaternion.LookRotation(-hit.normal, Vector3.up);

        if (sharedBeam != null)
        {
            sharedBeam.positionCount = 2;
            sharedBeam.SetPosition(0, ray.origin);
            sharedBeam.SetPosition(1, hit.point);
        }
    }

    private bool IsOwnedCollider(Collider candidate)
    {
        if (candidate == null || ownColliders == null)
        {
            return false;
        }

        for (int i = 0; i < ownColliders.Length; i++)
        {
            if (ownColliders[i] == candidate)
            {
                return true;
            }
        }

        return false;
    }
}