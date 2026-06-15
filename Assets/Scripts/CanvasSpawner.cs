using UnityEngine;

public class CanvasSpawner : MonoBehaviour
{
    [Tooltip("Drag the Canvas Prefab here")]
    public GameObject canvasPrefab;

    [Tooltip("Drag the CanvasAudioRecorder script attached to this object here")]
    public CanvasAudioRecorder recorderScript;

    [SerializeField, Tooltip("Minimum move distance before this note is treated as grabbed")]
    private float grabMoveThreshold = 0.015f;

    [SerializeField, Tooltip("Minimum rotation change before this note is treated as grabbed")]
    private float grabRotateThreshold = 5f;

    [SerializeField, Tooltip("Small delay to ignore startup jitter")]
    private float grabDetectDelay = 0.15f;

    private bool hasSpawnedReplacement;
    private float spawnTime;
    private Vector3 originPosition;
    private Quaternion originRotation;
    private Vector3 originLocalPosition;
    private Quaternion originLocalRotation;
    private Vector3 originLocalScale;
    private Transform originParent;

    void Start()
    {
        originPosition = transform.position;
        originRotation = transform.rotation;
        originLocalPosition = transform.localPosition;
        originLocalRotation = transform.localRotation;
        originLocalScale = transform.localScale;
        originParent = transform.parent;
        spawnTime = Time.time;

        // Ensure the original canvas sitting on the desk is ALWAYS locked
        if (recorderScript != null)
        {
            recorderScript.canRecord = false;
            recorderScript.UpdateUIText();
        }
    }

    void Update()
    {
        if (hasSpawnedReplacement)
        {
            return;
        }

        if (Time.time - spawnTime < grabDetectDelay)
        {
            return;
        }

        if (HasBeenGrabbed())
        {
            LeaveCloneBehind();
        }
    }

    public void LeaveCloneBehind()
    {
        if (hasSpawnedReplacement)
        {
            return;
        }

        hasSpawnedReplacement = true;

        // Spawn a brand new (locked) clone at the original desk pose.
        if (canvasPrefab != null)
        {
            GameObject spawned;
            if (originParent != null)
            {
                spawned = Instantiate(canvasPrefab, originParent);
                spawned.transform.localPosition = originLocalPosition;
                spawned.transform.localRotation = originLocalRotation;
                spawned.transform.localScale = originLocalScale;
            }
            else
            {
                spawned = Instantiate(canvasPrefab, originPosition, originRotation);
                spawned.transform.localScale = originLocalScale;
            }
        }

        // Unlock the canvas that is currently being held.
        if (recorderScript != null)
        {
            recorderScript.canRecord = true;
            recorderScript.UpdateUIText();
        }

        // Destroy this spawner script so this instance cannot clone repeatedly.
        Destroy(this);
    }

    private bool HasBeenGrabbed()
    {
        if (transform.parent != originParent)
        {
            return true;
        }

        float movedDistance = Vector3.Distance(transform.localPosition, originLocalPosition);
        if (movedDistance >= grabMoveThreshold)
        {
            return true;
        }

        float rotatedDegrees = Quaternion.Angle(transform.localRotation, originLocalRotation);
        return rotatedDegrees >= grabRotateThreshold;
    }
}