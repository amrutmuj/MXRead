// Copyright (c) Meta Platforms, Inc. and affiliates.

using Meta.XR.MRUtilityKit;
using Meta.XR.Samples;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Meta.XR.MRUtilityKitSamples.QRCodeDetection
{
    [MetaCodeSample("MRUKSample-QRCodeDetection")]
    public class QRCodeManager : MonoBehaviour
    {
        //
        // Static interface

        public const string ScenePermission = OVRPermissionsRequester.ScenePermission;

        public static bool IsSupported => MRUK.Instance.QRCodeTrackingSupported;

        public static bool HasPermissions
#if UNITY_EDITOR
            => true;
#else
            => UnityEngine.Android.Permission.HasUserAuthorizedPermission(ScenePermission);
#endif

        public static int ActiveTrackedCount
            => s_instance ? s_instance._activeCount : 0;

        public static MRUKTrackable ActiveTrackable
            => s_instance ? s_instance._activeTrackable : null;

        public static int ActivePageId
            => s_instance ? s_instance._activePageId : 0;

        public static string ActivePayload
            => s_instance ? s_instance._activePayload : string.Empty;

        public static event Action<int, MRUKTrackable, string> ActivePageChanged;

        public static bool TryGetActivePage(out int pageId, out MRUKTrackable trackable, out string payload)
        {
            pageId = ActivePageId;
            trackable = ActiveTrackable;
            payload = ActivePayload;
            return trackable != null && pageId > 0;
        }

        public static bool TrackingEnabled
        {
            get => s_instance && s_instance._mrukInstance && s_instance._mrukInstance.SceneSettings.TrackerConfiguration.QRCodeTrackingEnabled;
            set
            {
                if (!s_instance || !s_instance._mrukInstance)
                {
                    return;
                }
                var config = s_instance._mrukInstance.SceneSettings.TrackerConfiguration;
                config.QRCodeTrackingEnabled = value;
                s_instance._mrukInstance.SceneSettings.TrackerConfiguration = config;
            }
        }


        public static void RequestRequiredPermissions(Action<bool> onRequestComplete)
        {
            if (!s_instance)
            {
                Debug.LogError($"{nameof(RequestRequiredPermissions)} failed; no QRCodeManager instance.");
                return;
            }

#if UNITY_EDITOR
            const string kCantRequestMsg =
                "Cannot request Android permission when using Link or XR Sim. " +
                "For Link, enable the spatial data permission from the Link app under Settings > Beta > Spatial Data over Meta Quest Link. " +
                "For XR Sim, no permission is necessary.";

            Log(kCantRequestMsg, LogType.Warning);

            onRequestComplete?.Invoke(HasPermissions);
#else
            Log($"Requesting {ScenePermission} ... (currently: {HasPermissions})");

            var callbacks = new UnityEngine.Android.PermissionCallbacks();
            callbacks.PermissionGranted += perm => Log($"{perm} granted");

            var msgDenied = $"{ScenePermission} denied. Please press the 'Request Permission' button again.";
            var msgDeniedPermanently = $"{ScenePermission} permanently denied. To enable:\n" +
                                       $"    1. Uninstall and reinstall the app, OR\n" +
                                       $"    2. Manually grant permission in device Settings > Privacy & Safety > App Permissions.";

#if !UNITY_6000_0_OR_NEWER
            callbacks.PermissionDenied += _ => Log(msgDenied, LogType.Error);
            callbacks.PermissionDeniedAndDontAskAgain += _ => Log(msgDeniedPermanently, LogType.Error);
#else
            callbacks.PermissionDenied += perm =>
            {
                // ShouldShowRequestPermissionRationale returns false only if
                // the user selected 'Never ask again' or if the user has never
                // been asked for the permission (which can't be the case here).
                Log(
                    UnityEngine.Android.Permission.ShouldShowRequestPermissionRationale(perm)
                        ? msgDenied
                        : msgDeniedPermanently,
                    LogType.Error);
            };
#endif // UNITY_6000_0_OR_NEWER

            if (onRequestComplete is not null)
            {
                callbacks.PermissionGranted += _ => onRequestComplete(HasPermissions);
                callbacks.PermissionDenied += _ => onRequestComplete(HasPermissions);
#if !UNITY_6000_0_OR_NEWER
                callbacks.PermissionDeniedAndDontAskAgain += _ => onRequestComplete(HasPermissions);
#endif // UNITY_6000_0_OR_NEWER
            }

            UnityEngine.Android.Permission.RequestUserPermission(ScenePermission, callbacks);
#endif // UNITY_EDITOR
        }


        //
        // Serialized fields

        [SerializeField]
        QRCode _qrCodePrefab;

        [SerializeField]
        QRCodeSampleUI _uiInstance;

        [SerializeField]
        MRUK _mrukInstance;

        // non-serialized fields

        int _activeCount;
        int _activePageId;
        string _activePayload = string.Empty;

        static QRCodeManager s_instance;

        // NEW: Dictionaries to track all spawned UIs and their visibility states without destroying them
        private Dictionary<MRUKTrackable, GameObject> _spawnedUIs = new Dictionary<MRUKTrackable, GameObject>();
        private Dictionary<MRUKTrackable, bool> _previousTrackingStates = new Dictionary<MRUKTrackable, bool>();
        private MRUKTrackable _activeTrackable;
        private Dictionary<MRUKTrackable, int> _pageIdByTrackable = new Dictionary<MRUKTrackable, int>();
        private Dictionary<string, int> _pageIdByKey = new Dictionary<string, int>();
        private int _nextGeneratedPageId = 1;


        //
        // MonoBehaviour messages

        void OnValidate()
        {
            if (!_uiInstance && FindAnyObjectByType<QRCodeSampleUI>() is { } ui && ui.gameObject.scene == gameObject.scene)
            {
                _uiInstance = ui;
            }
            if (!_mrukInstance && FindAnyObjectByType<MRUK>() is { } mruk && mruk.gameObject.scene == gameObject.scene)
            {
                _mrukInstance = mruk;
            }
        }

        void OnEnable()
        {
            s_instance = this;

            if (!_mrukInstance)
            {
                Log($"{nameof(QRCodeManager)} requires an MRUK object in the scene!", LogType.Error);
                return;
            }

            _mrukInstance.SceneSettings.TrackableAdded.AddListener(OnTrackableAdded);
            _mrukInstance.SceneSettings.TrackableRemoved.AddListener(OnTrackableRemoved);
        }

        void OnDestroy()
            => s_instance = null;


        // NEW: Monitor tracking states every frame to switch active UI
        void Update()
        {
            if (_spawnedUIs.Count == 0) return;

            bool switchedActive = false;

            // 1. Check if any previously hidden QR code just became visible again
            foreach (var trackable in _spawnedUIs.Keys)
            {
                bool currentState = trackable.IsTracked;
                bool previousState = _previousTrackingStates[trackable];

                // If the user flipped back to this page and the OS re-acquired it
                if (currentState && !previousState)
                {
                    MakeActive(trackable);
                    switchedActive = true;
                }

                _previousTrackingStates[trackable] = currentState;
            }

            // 2. Safety fallback: If the current active one lost tracking, show another tracked one if available
            if (!switchedActive && _activeTrackable != null && !_activeTrackable.IsTracked)
            {
                foreach (var trackable in _spawnedUIs.Keys)
                {
                    if (trackable.IsTracked)
                    {
                        MakeActive(trackable);
                        break;
                    }
                }
            }
        }

        // Helper to turn on the correct UI and turn off the rest
        void MakeActive(MRUKTrackable newActive)
        {
            _activeTrackable = newActive;
            _activePageId = GetOrCreatePageId(newActive);
            _activePayload = GetPayloadString(newActive);

            foreach (var kvp in _spawnedUIs)
            {
                // Only enable the UI if it's the active one AND it's physically visible
                if (kvp.Key == _activeTrackable && kvp.Key.IsTracked)
                {
                    kvp.Value.SetActive(true);
                }
                else
                {
                    kvp.Value.SetActive(false);
                }
            }

            NotifyActivePageChanged();
        }


        //
        // UnityEvent listeners

        public void OnTrackableAdded(MRUKTrackable trackable)
        {
            if (trackable.TrackableType != OVRAnchor.TrackableType.QRCode)
            {
                return;
            }

            var log = $"{nameof(OnTrackableAdded)}: QRCode detected!\n";

            // Spawn the UI, but save it to our dictionary instead of destroying old ones
            var instance = Instantiate(_qrCodePrefab, trackable.transform);
            var qrCode = instance.GetComponent<QRCode>();
            qrCode.Initialize(trackable);

            var visualizer = instance.GetComponent<Bounded2DVisualizer>();
            if (visualizer != null)
            {
                visualizer.Initialize(trackable);
            }

            _spawnedUIs[trackable] = instance.gameObject;
            _previousTrackingStates[trackable] = trackable.IsTracked;
            GetOrCreatePageId(trackable);

            ++_activeCount;

            // Make this new page the active visual
            MakeActive(trackable);

            Log($"{log}\nPayload={qrCode.PayloadText}");
        }

        public void OnTrackableRemoved(MRUKTrackable trackable)
        {
            if (trackable.TrackableType != OVRAnchor.TrackableType.QRCode)
            {
                return;
            }

            Log($"QRCode removed");

            --_activeCount;

            // Clean up our dictionaries if the OS decides to permanently delete the anchor
            if (_spawnedUIs.TryGetValue(trackable, out GameObject uiInstance))
            {
                Destroy(uiInstance);
                _spawnedUIs.Remove(trackable);
            }

            _previousTrackingStates.Remove(trackable);
            _pageIdByTrackable.Remove(trackable);

            if (_activeTrackable == trackable)
            {
                _activeTrackable = null;
                _activePageId = 0;
                _activePayload = string.Empty;
                NotifyActivePageChanged();
            }

            Destroy(trackable.gameObject);
        }


        //
        // private impl.

        static void Log(object msg, LogType type = LogType.Log)
        {
            if (s_instance && s_instance._uiInstance)
            {
                s_instance._uiInstance.Log(msg, type);
            }
            else
            {
                Debug.LogFormat(
                    logType: type,
                    logOptions: LogOption.None,
                    context: s_instance,
                    format: "{0}(noinst): {1}", nameof(QRCodeManager), msg
                );
            }
        }

        void NotifyActivePageChanged()
        {
            ActivePageChanged?.Invoke(_activePageId, _activeTrackable, _activePayload);
        }

        int GetOrCreatePageId(MRUKTrackable trackable)
        {
            if (!trackable)
            {
                return 0;
            }

            if (_pageIdByTrackable.TryGetValue(trackable, out int existingId) && existingId > 0)
            {
                return existingId;
            }

            string payload = GetPayloadString(trackable);
            string key = string.IsNullOrEmpty(payload) ? trackable.name : payload;

            int pageId = 0;
            if (!string.IsNullOrEmpty(key) && _pageIdByKey.TryGetValue(key, out int mappedId) && mappedId > 0)
            {
                pageId = mappedId;
            }

            if (pageId <= 0)
            {
                if (!TryParsePositiveInt(payload, out pageId))
                {
                    TryParsePositiveInt(trackable.name, out pageId);
                }
            }

            if (pageId <= 0)
            {
                pageId = _nextGeneratedPageId;
            }

            _nextGeneratedPageId = Mathf.Max(_nextGeneratedPageId, pageId + 1);
            _pageIdByTrackable[trackable] = pageId;
            if (!string.IsNullOrEmpty(key))
            {
                _pageIdByKey[key] = pageId;
            }

            return pageId;
        }

        static string GetPayloadString(MRUKTrackable trackable)
        {
            if (!trackable)
            {
                return string.Empty;
            }

            if (!string.IsNullOrEmpty(trackable.MarkerPayloadString))
            {
                return trackable.MarkerPayloadString;
            }

            if (trackable.MarkerPayloadBytes != null && trackable.MarkerPayloadBytes.Length > 0)
            {
                return BitConverter.ToString(trackable.MarkerPayloadBytes);
            }

            return string.Empty;
        }

        static bool TryParsePositiveInt(string text, out int value)
        {
            value = 0;
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            int current = 0;
            bool foundDigit = false;

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c >= '0' && c <= '9')
                {
                    current = (current * 10) + (c - '0');
                    foundDigit = true;
                    continue;
                }

                if (foundDigit)
                {
                    if (current > 0)
                    {
                        value = current;
                        return true;
                    }

                    current = 0;
                    foundDigit = false;
                }
            }

            if (foundDigit && current > 0)
            {
                value = current;
                return true;
            }

            return false;
        }

    }
}