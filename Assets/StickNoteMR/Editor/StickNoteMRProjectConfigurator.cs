using System.Linq;
using System.Reflection;
using Meta.XR.InputActions;
using UnityEditor;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.Management;

[InitializeOnLoad]
public static class StickNoteMRProjectConfigurator
{
    private const string OculusLoaderType = "Unity.XR.Oculus.OculusLoader";
    private const string OculusSettingsPath = "Assets/XR/Settings/OculusSettings.asset";
    private const string MxInkFolder = "Assets/StickNoteMR/InputActions";
    private const string MxInkActionSetPath = MxInkFolder + "/MxInkActions.asset";
    private const string MxInkInteractionProfile = "/interaction_profiles/logitech/mx_ink_stylus_logitech";
    private static readonly BuildTargetGroup[] SupportedBuildTargets =
    {
        BuildTargetGroup.Android,
        BuildTargetGroup.Standalone
    };

    static StickNoteMRProjectConfigurator()
    {
        EditorApplication.delayCall += ConfigureProject;
    }

    public static void ConfigureProjectNow()
    {
        ConfigureProject();
    }

    private static void ConfigureProject()
    {
        ConfigurePlayerSettings();
        ConfigureXRManagement();
        ConfigureOculusSettings();
        ConfigureOculusProjectConfig();
        ConfigureMetaInputActions();
        AssetDatabase.SaveAssets();
    }

    private static void ConfigurePlayerSettings()
    {
        PlayerSettings.productName = "Stick Note MR";
        PlayerSettings.companyName = "Codex";
        PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
        PlayerSettings.colorSpace = ColorSpace.Linear;
        PlayerSettings.preserveFramebufferAlpha = true;
        PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android, "com.codex.sticknotemr");
        PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android, ScriptingImplementation.IL2CPP);
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
        PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel32;
        PlayerSettings.SplashScreen.show = false;
        PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
        PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.Vulkan });
        ConfigureInputHandling();
    }

    private static void ConfigureXRManagement()
    {
        MethodInfo getOrCreateMethod = typeof(XRGeneralSettingsPerBuildTarget).GetMethod(
            "GetOrCreate",
            BindingFlags.Static | BindingFlags.NonPublic);

        XRGeneralSettingsPerBuildTarget settingsPerBuildTarget =
            getOrCreateMethod?.Invoke(null, null) as XRGeneralSettingsPerBuildTarget;

        if (settingsPerBuildTarget == null)
        {
            Debug.LogWarning("XR settings asset could not be created automatically.");
            return;
        }

        foreach (BuildTargetGroup buildTargetGroup in SupportedBuildTargets)
        {
            ConfigureLoaderForBuildTarget(settingsPerBuildTarget, buildTargetGroup);
        }

        EditorUtility.SetDirty(settingsPerBuildTarget);
    }

    private static void ConfigureLoaderForBuildTarget(
        XRGeneralSettingsPerBuildTarget settingsPerBuildTarget,
        BuildTargetGroup buildTargetGroup)
    {
        if (!settingsPerBuildTarget.HasSettingsForBuildTarget(buildTargetGroup))
        {
            settingsPerBuildTarget.CreateDefaultSettingsForBuildTarget(buildTargetGroup);
        }

        if (!settingsPerBuildTarget.HasManagerSettingsForBuildTarget(buildTargetGroup))
        {
            settingsPerBuildTarget.CreateDefaultManagerSettingsForBuildTarget(buildTargetGroup);
        }

        XRGeneralSettings generalSettings = settingsPerBuildTarget.SettingsForBuildTarget(buildTargetGroup);
        XRManagerSettings managerSettings = settingsPerBuildTarget.ManagerSettingsForBuildTarget(buildTargetGroup);

        if (generalSettings == null || managerSettings == null)
        {
            Debug.LogWarning($"XR settings for {buildTargetGroup} could not be configured automatically.");
            return;
        }

        generalSettings.InitManagerOnStart = true;

        if (!managerSettings.activeLoaders.Any(loader => loader != null && loader.GetType().FullName == OculusLoaderType))
        {
            XRPackageMetadataStore.AssignLoader(managerSettings, OculusLoaderType, buildTargetGroup);
        }

        EditorUtility.SetDirty(generalSettings);
        EditorUtility.SetDirty(managerSettings);
    }

    private static void ConfigureOculusProjectConfig()
    {
        OVRProjectConfig projectConfig = OVRProjectConfig.CachedProjectConfig;

        if (projectConfig == null)
        {
            return;
        }

        projectConfig.targetDeviceTypes = projectConfig.targetDeviceTypes
            .Distinct()
            .ToList();

        if (!projectConfig.targetDeviceTypes.Contains(OVRProjectConfig.DeviceType.Quest3))
        {
            projectConfig.targetDeviceTypes.Add(OVRProjectConfig.DeviceType.Quest3);
        }

        projectConfig.handTrackingSupport = OVRProjectConfig.HandTrackingSupport.ControllersAndHands;
        projectConfig.handTrackingFrequency = OVRProjectConfig.HandTrackingFrequency.HIGH;
        projectConfig.insightPassthroughSupport = OVRProjectConfig.FeatureSupport.Required;
        projectConfig.systemLoadingScreenBackground = OVRProjectConfig.SystemLoadingScreenBackground.ContextualPassthrough;
        projectConfig.anchorSupport = OVRProjectConfig.AnchorSupport.Disabled;
        projectConfig.sceneSupport = OVRProjectConfig.FeatureSupport.Supported;
        projectConfig.sharedAnchorSupport = OVRProjectConfig.FeatureSupport.None;
        projectConfig.boundaryVisibilitySupport = OVRProjectConfig.FeatureSupport.Supported;
        projectConfig.processorFavor = OVRProjectConfig.ProcessorFavor.FavorGPU;
        projectConfig.experimentalFeaturesEnabled = false;

        OVRProjectConfig.CommitProjectConfig(projectConfig);
    }

    private static void ConfigureOculusSettings()
    {
        Object oculusSettings = AssetDatabase.LoadAssetAtPath<Object>(OculusSettingsPath);
        if (oculusSettings == null)
        {
            return;
        }

        SerializedObject serializedObject = new SerializedObject(oculusSettings);
        SetSerializedEnumOrIntIfExists(serializedObject, "m_StereoRenderingModeAndroid", 0);
        SetSerializedEnumOrIntIfExists(serializedObject, "m_StereoRenderingModeDesktop", 1);
        SetSerializedBoolIfExists(serializedObject, "TargetQuest2", false);
        SetSerializedBoolIfExists(serializedObject, "TargetQuestPro", false);
        SetSerializedBoolIfExists(serializedObject, "TargetQuest3", true);
        SetSerializedBoolIfExists(serializedObject, "TargetQuest3S", false);
        SetSerializedBoolIfExists(serializedObject, "SharedDepthBuffer", true);
        SetSerializedBoolIfExists(serializedObject, "SymmetricProjection", true);
        serializedObject.ApplyModifiedPropertiesWithoutUndo();

        EditorUtility.SetDirty(oculusSettings);
    }

    private static void ConfigureMetaInputActions()
    {
        EnsureFolder("Assets", "StickNoteMR");
        EnsureFolder("Assets/StickNoteMR", "InputActions");

        InputActionSet mxInkActionSet = AssetDatabase.LoadAssetAtPath<InputActionSet>(MxInkActionSetPath);
        if (mxInkActionSet == null)
        {
            mxInkActionSet = ScriptableObject.CreateInstance<InputActionSet>();
            AssetDatabase.CreateAsset(mxInkActionSet, MxInkActionSetPath);
        }

        mxInkActionSet.InteractionProfile = MxInkInteractionProfile;
        mxInkActionSet.InputActionDefinitions = new System.Collections.Generic.List<InputActionDefinition>
        {
            CreateAction("aim_left", OVRPlugin.ActionTypes.Pose, "/user/hand/left/input/aim/pose"),
            CreateAction("aim_right", OVRPlugin.ActionTypes.Pose, "/user/hand/right/input/aim/pose"),
            CreateAction("grip_left", OVRPlugin.ActionTypes.Pose, "/user/hand/left/input/grip/pose"),
            CreateAction("grip_right", OVRPlugin.ActionTypes.Pose, "/user/hand/right/input/grip/pose"),
            CreateAction("tip_pose_left", OVRPlugin.ActionTypes.Pose, "/user/hand/left/input/tip_logitech/pose"),
            CreateAction("tip_pose_right", OVRPlugin.ActionTypes.Pose, "/user/hand/right/input/tip_logitech/pose"),
            CreateAction("tip", OVRPlugin.ActionTypes.Float,
                "/user/hand/left/input/tip_logitech/force",
                "/user/hand/right/input/tip_logitech/force"),
            CreateAction("middle", OVRPlugin.ActionTypes.Float,
                "/user/hand/left/input/cluster_middle_logitech/force",
                "/user/hand/right/input/cluster_middle_logitech/force"),
            CreateAction("front", OVRPlugin.ActionTypes.Boolean,
                "/user/hand/left/input/cluster_front_logitech/click",
                "/user/hand/right/input/cluster_front_logitech/click"),
            CreateAction("back", OVRPlugin.ActionTypes.Boolean,
                "/user/hand/left/input/cluster_back_logitech/click",
                "/user/hand/right/input/cluster_back_logitech/click"),
            CreateAction("dock", OVRPlugin.ActionTypes.Boolean,
                "/user/hand/left/input/dock_logitech/docked_logitech",
                "/user/hand/right/input/dock_logitech/docked_logitech"),
            CreateAction("haptic_pulse", OVRPlugin.ActionTypes.Vibration,
                "/user/hand/left/output/haptic",
                "/user/hand/right/output/haptic")
        };

        RuntimeSettings runtimeSettings = RuntimeSettings.Instance;
        runtimeSettings.AddToPreloadedAssets();
        if (!runtimeSettings.InputActionSets.Contains(mxInkActionSet))
        {
            runtimeSettings.InputActionSets.Add(mxInkActionSet);
        }

        EditorUtility.SetDirty(mxInkActionSet);
        EditorUtility.SetDirty(runtimeSettings);
        RuntimeSettings.UpdateBindingsOnDisk();
    }

    private static void ConfigureInputHandling()
    {
        Object[] assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset");
        if (assets == null || assets.Length == 0)
        {
            return;
        }

        SerializedObject serializedObject = new SerializedObject(assets[0]);
        SerializedProperty inputHandler = serializedObject.FindProperty("activeInputHandler");
        if (inputHandler == null)
        {
            return;
        }

        // 1 = Input System Package only. "Both" is unsupported for Android.
        inputHandler.intValue = 1;
        serializedObject.ApplyModifiedPropertiesWithoutUndo();
    }

    private static InputActionDefinition CreateAction(string actionName, OVRPlugin.ActionTypes actionType, params string[] paths)
    {
        return new InputActionDefinition
        {
            ActionName = actionName,
            Type = actionType,
            Paths = paths
        };
    }

    private static void EnsureFolder(string parent, string child)
    {
        string path = parent + "/" + child;
        if (!AssetDatabase.IsValidFolder(path))
        {
            AssetDatabase.CreateFolder(parent, child);
        }
    }

    private static void SetSerializedBoolIfExists(SerializedObject serializedObject, string propertyPath, bool value)
    {
        SerializedProperty property = serializedObject.FindProperty(propertyPath);
        if (property != null && property.propertyType == SerializedPropertyType.Boolean)
        {
            property.boolValue = value;
        }
    }

    private static void SetSerializedEnumOrIntIfExists(SerializedObject serializedObject, string propertyPath, int value)
    {
        SerializedProperty property = serializedObject.FindProperty(propertyPath);
        if (property != null &&
            (property.propertyType == SerializedPropertyType.Enum || property.propertyType == SerializedPropertyType.Integer))
        {
            property.intValue = value;
        }
    }
}
