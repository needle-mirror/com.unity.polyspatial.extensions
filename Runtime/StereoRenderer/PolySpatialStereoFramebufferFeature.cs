using System;
using Unity.PolySpatial.Internals;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.Serialization;
using UnityEngine.XR;

#if UNITY_EDITOR && ENABLE_CLOUD_SERVICES_ANALYTICS
using UnityEditor.PolySpatial.Analytics;
#endif

namespace Unity.PolySpatial.Extensions
{
    [DisallowMultipleRendererFeature("PolySpatial Stereo Framebuffer")]
    [Tooltip("Captures the Stereo Framebuffer from a camera with the PolySpatialStereoFramebufferCamera added to it then sends it to the PolySpatial host.")]
    internal class PolySpatialStereoFramebufferFeature : ScriptableRendererFeature
    {
        [Tooltip("GBuffer resolution will be divided by this value resulting in a re-projection mesh with fewer vertices. " +
                 "This can be better for performance but could also produce better results by not having sharp edges in the re-projection mesh.")]
        [SerializeField]
        int m_GBufferPixelToVertexRatio = 10;

        PolySpatialStereoFramebufferPass m_BlitPass;
        PolySpatialStereoFramebufferCamera m_PendingStereoFramebufferCamera;

#if UNITY_EDITOR && ENABLE_CLOUD_SERVICES_ANALYTICS
        [NonSerialized]
        bool m_AnalyticsSent;
#endif

        public override void Create()
        {
            m_BlitPass = new PolySpatialStereoFramebufferPass();
            m_BlitPass.renderPassEvent = RenderPassEvent.AfterRendering;
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (!Application.isPlaying || PolySpatialCore.LocalAssetManager == null)
                return;

            var camera = renderingData.cameraData.camera;
            if (!ShouldRenderCamera(camera))
                return;

            var component = camera.GetComponent<PolySpatialStereoFramebufferCamera>();
            if (component == null || !component.isActiveAndEnabled)
                return;

            // This seems to need to be called every frame otherwise I sometimes got the occlusionMesh in the depth blit
#if ENABLE_VR
            XRSettings.useOcclusionMesh = false;
#endif

            m_BlitPass.ConfigureInput(ScriptableRenderPassInput.Color | ScriptableRenderPassInput.Depth);
            m_BlitPass.ValidateFramebufferData(component, camera, m_GBufferPixelToVertexRatio, ref renderingData);
            renderer.EnqueuePass(m_BlitPass);

#if UNITY_EDITOR && ENABLE_CLOUD_SERVICES_ANALYTICS
            if (!m_AnalyticsSent)
            {
                m_AnalyticsSent = true;
                var deviceDisplayDimensions = PolySpatialSettings.Instance != null
                    ? PolySpatialSettings.Instance.DeviceDisplayProviderParameters.dimensions.ToString()
                    : "";
                PolySpatialAnalytics.Send(FeatureName.StereoRenderTarget, deviceDisplayDimensions);
            }
#endif
        }

        bool ShouldRenderCamera(Camera camera)
        {
            return camera.isActiveAndEnabled && camera.cameraType == CameraType.Game && camera.gameObject.layer != PolySpatialLayerUtils.BackingLayer;
        }

        protected override void Dispose(bool disposing)
        {
            m_BlitPass?.Dispose();
            m_BlitPass = null;
        }
    }

#if UNITY_EDITOR
    [CustomEditor(typeof(PolySpatialStereoFramebufferFeature))]
    internal class StereoRendererFeatureEditor : UnityEditor.Editor
    {
        SerializedProperty m_GBufferPixelToVertexRatioProperty;

        SerializedObject m_SerializedSettings;
        SerializedProperty m_StereoRendererResolutionProperty;
        SerializedProperty m_DeviceDisplayProviderParametersProperty;

        public void OnEnable()
        {
            m_GBufferPixelToVertexRatioProperty = serializedObject.FindProperty("m_GBufferPixelToVertexRatio");

            m_SerializedSettings = new SerializedObject(PolySpatialSettings.Instance);
            m_DeviceDisplayProviderParametersProperty = m_SerializedSettings.FindProperty("m_DeviceDisplayProviderParameters");
        }

        public override void OnInspectorGUI()
        {
            m_SerializedSettings.Update();
            EditorGUILayout.PropertyField(m_GBufferPixelToVertexRatioProperty);
            EditorGUILayout.PropertyField(m_DeviceDisplayProviderParametersProperty);
            m_SerializedSettings.ApplyModifiedProperties();

            var originalColor = GUI.contentColor;

            EditorGUILayout.Space();
            EditorGUILayout.Separator();
            var stereoRendererFramebuffers = FindObjectsByType<PolySpatialStereoFramebufferCamera>(FindObjectsInactive.Include, FindObjectsSortMode.InstanceID);
            var activeCount = 0;
            if (stereoRendererFramebuffers.Length > 0)
            {
                EditorGUILayout.LabelField("Stereo Renderer Cameras:");
                EditorGUI.indentLevel++;
                GUI.enabled = false;
                foreach (var stereoRendererCamera in stereoRendererFramebuffers)
                {
                    var active = stereoRendererCamera.IsCameraAndComponentActive;
                    activeCount += active ? 1 : 0;
                    EditorGUILayout.ObjectField(active ? "" : "Disabled: ", stereoRendererCamera, stereoRendererCamera.GetType(), true);
                }
                GUI.enabled = true;
            }
            else
            {
                GUI.contentColor = Color.yellow;
                EditorGUILayout.LabelField("No Stereo Renderer Cameras! Add a StereoRendererCamera component next to a camera!");
            }

            GUI.contentColor = originalColor;
        }
    }
#endif
}
