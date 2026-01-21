using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

namespace com.dnw.standardpackage
{

    /// <summary>
    /// Detects when the VR headset is removed/put on and provides events for pausing/resuming.
    /// Uses OpenXR InputDevices (preferred) or OVRManager (fallback) to detect headset presence on Quest devices.
    /// </summary>
    public class HeadsetPresenceDetector : MonoBehaviour
    {
        [Header("Detection Settings")]
        [Tooltip("Check interval in seconds for headset presence")]
        [SerializeField] private float checkInterval = 0.5f;
        
        [Tooltip("Delay in seconds before considering headset 'removed' (prevents false positives)")]
        [SerializeField] private float removalConfirmationDelay = 1.0f;

        private bool _isHeadsetWorn = true;
        private bool _lastKnownState = true;
        private float _timeSinceLastRemoval = 0f;
        private float _checkTimer = 0f;
        private InputDevice _headDevice;
        private bool _useOpenXR = false;
        private bool _useOVRManager = false;

        /// <summary>
        /// Event fired when headset is detected as removed (after confirmation delay).
        /// </summary>
        public event Action OnHeadsetRemoved;
        
        /// <summary>
        /// Event fired when headset is detected as put back on.
        /// </summary>
        public event Action OnHeadsetPutOn;

        /// <summary>
        /// Returns true if headset is currently being worn.
        /// </summary>
        public bool IsHeadsetWorn => _isHeadsetWorn;

        private void Start()
        {
            // Try to initialize OpenXR detection first (preferred method)
            InitializeOpenXRDetection();
            
            // If OpenXR not available, try OVRManager (Oculus SDK fallback)
            if (!_useOpenXR)
            {
                InitializeOVRManagerDetection();
            }

            // Initialize with current state
            _isHeadsetWorn = IsUserPresent();
            _lastKnownState = _isHeadsetWorn;
            
            Debug.Log($"[HeadsetPresenceDetector]: Initialized - Method: {(_useOpenXR ? "OpenXR" : (_useOVRManager ? "OVRManager" : "None (fallback)"))}, Headset worn: {_isHeadsetWorn}");
        }

        /// <summary>
        /// Initializes OpenXR-based headset detection using InputDevices.
        /// </summary>
        private void InitializeOpenXRDetection()
        {
            try
            {
                List<InputDevice> devices = new List<InputDevice>();
                InputDevices.GetDevicesAtXRNode(XRNode.Head, devices);
                
                if (devices.Count > 0)
                {
                    _headDevice = devices[0];
                    if (_headDevice.isValid && _headDevice.TryGetFeatureValue(CommonUsages.userPresence, out bool _))
                    {
                        _useOpenXR = true;
                        Debug.Log("[HeadsetPresenceDetector]: Using OpenXR InputDevices for headset detection");
                    }
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[HeadsetPresenceDetector]: OpenXR detection not available: {ex.Message}");
            }
        }

        /// <summary>
        /// Initializes OVRManager-based headset detection (Oculus SDK fallback).
        /// </summary>
        private void InitializeOVRManagerDetection()
        {
            try
            {
                // Check if OVRManager is available
                var ovrManagerType = System.Type.GetType("OVRManager");
                if (ovrManagerType != null)
                {
                    var instanceProperty = ovrManagerType.GetProperty("instance");
                    if (instanceProperty != null)
                    {
                        var instance = instanceProperty.GetValue(null);
                        if (instance != null)
                        {
                            var isUserPresentProperty = ovrManagerType.GetProperty("isUserPresent");
                            if (isUserPresentProperty != null)
                            {
                                _useOVRManager = true;
                                Debug.Log("[HeadsetPresenceDetector]: Using OVRManager for headset detection");
                            }
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[HeadsetPresenceDetector]: OVRManager detection not available: {ex.Message}");
            }
        }

        private void Update()
        {
            _checkTimer += Time.unscaledDeltaTime;
            
            if (_checkTimer >= checkInterval)
            {
                _checkTimer = 0f;
                CheckHeadsetPresence();
            }
        }

        /// <summary>
        /// Checks if the user is currently wearing the headset.
        /// Uses OpenXR InputDevices (preferred) or OVRManager (fallback).
        /// </summary>
        private bool IsUserPresent()
        {
            #if UNITY_EDITOR
            // In Editor, we can't detect headset removal, so assume it's always worn
            return true;
            #else
            // Method 1: OpenXR InputDevices (preferred for modern Unity projects)
            if (_useOpenXR)
            {
                try
                {
                    // Refresh head device if needed
                    if (!_headDevice.isValid)
                    {
                        List<InputDevice> devices = new List<InputDevice>();
                        InputDevices.GetDevicesAtXRNode(XRNode.Head, devices);
                        if (devices.Count > 0)
                        {
                            _headDevice = devices[0];
                        }
                    }

                    if (_headDevice.isValid)
                    {
                        if (_headDevice.TryGetFeatureValue(CommonUsages.userPresence, out bool userPresent))
                        {
                            return userPresent;
                        }
                    }
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning($"[HeadsetPresenceDetector]: Error checking OpenXR user presence: {ex.Message}");
                }
            }

            // Method 2: OVRManager (Oculus SDK fallback)
            if (_useOVRManager)
            {
                try
                {
                    var ovrManagerType = System.Type.GetType("OVRManager");
                    if (ovrManagerType != null)
                    {
                        var instanceProperty = ovrManagerType.GetProperty("instance");
                        if (instanceProperty != null)
                        {
                            var instance = instanceProperty.GetValue(null);
                            if (instance != null)
                            {
                                var isUserPresentProperty = ovrManagerType.GetProperty("isUserPresent");
                                if (isUserPresentProperty != null)
                                {
                                    bool userPresent = (bool)isUserPresentProperty.GetValue(instance);
                                    return userPresent;
                                }
                            }
                        }
                    }
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning($"[HeadsetPresenceDetector]: Error checking OVRManager user presence: {ex.Message}");
                }
            }

            // Fallback: If neither method is available, assume headset is worn
            // This prevents false positives that would pause the game unnecessarily
            return true;
            #endif
        }

        /// <summary>
        /// Checks headset presence and fires events when state changes.
        /// Uses a confirmation delay to prevent false positives from brief removals.
        /// </summary>
        private void CheckHeadsetPresence()
        {
            bool currentState = IsUserPresent();
            
            if (currentState != _lastKnownState)
            {
                if (!currentState)
                {
                    // Headset was just removed - start confirmation timer
                    _timeSinceLastRemoval = 0f;
                }
                else
                {
                    // Headset was just put back on - fire event immediately
                    if (!_isHeadsetWorn)
                    {
                        _isHeadsetWorn = true;
                        _timeSinceLastRemoval = 0f;
                        OnHeadsetPutOn?.Invoke();
                        Debug.Log("[HeadsetPresenceDetector]: Headset put back on");
                    }
                }
                
                _lastKnownState = currentState;
            }
            
            // If headset appears removed, wait for confirmation delay before firing event
            if (!currentState && _isHeadsetWorn)
            {
                _timeSinceLastRemoval += Time.unscaledDeltaTime;
                
                if (_timeSinceLastRemoval >= removalConfirmationDelay)
                {
                    _isHeadsetWorn = false;
                    OnHeadsetRemoved?.Invoke();
                    Debug.Log("[HeadsetPresenceDetector]: Headset removed (confirmed)");
                }
            }
        }
    }
}
