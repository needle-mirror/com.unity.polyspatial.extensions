using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Rendering.Universal;

namespace Unity.PolySpatial.Extensions
{
    /// <summary>
    /// Add to a camera to have its StereoFramebuffer sent over PolySpatial.
    /// Also requires the PolySpatialStereoFramebufferFeature added to the URP Renderer.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    internal class PolySpatialStereoFramebufferCamera : MonoBehaviour
    {
        [Tooltip("Camera will generate and send a GBuffer with additional data. Currently only used with depth reprojection. " +
                 "If this StereoFramebuffer is not being used for depth reprojection this can be disabled as an optimization.")]
        [SerializeField]
        bool m_GenerateGBuffer = true;

        [Tooltip("Subscribe to this event to receive the stereo framebuffer sent back from the native display provider.")]
        [SerializeField]
        UnityEvent<PolySpatialStereoFramebufferCamera> m_FramebufferUpdated = new();

        /// <summary>
        /// Subscribe to this event to retrieve the stereo framebuffer.
        /// </summary>
        public UnityEvent<PolySpatialStereoFramebufferCamera> FramebufferUpdated => m_FramebufferUpdated;

        public bool GenerateGBuffer => m_GenerateGBuffer;

        public bool IsCameraAndComponentActive => isActiveAndEnabled && Camera.isActiveAndEnabled;

        public RenderTexture LeftColorTexture { get; private set; }
        public RenderTexture RightColorTexture { get; private set; }
        public RenderTexture LeftGBufferTexture { get; private set; }
        public RenderTexture RightGBufferTexture { get; private set; }

        public PolySpatialStereoFramebufferPass.FramebufferMode FramebufferMode { get; private set;  }

        public Camera Camera => m_Camera == null ? m_Camera = GetComponent<Camera>() : m_Camera;

        Camera m_Camera;

        void Start()
        {
            m_Camera = GetComponent<Camera>();
            var urpData = m_Camera.GetUniversalAdditionalCameraData();
            if (urpData == null)
            {
                Logging.LogWarning($"StereoRenderer only supported on URP. Disabling StereoFramebuffer on {m_Camera}.");
                enabled = false;
            }
        }

        void Update()
        {
            if (Application.isBatchMode && m_Camera.isActiveAndEnabled)
                m_Camera.Render();
        }

        /// <summary>
        /// When the XR texture is created and ready, it will set it here so other effects can retrieve
        /// it from this component and rely on the TextureUpdated event.
        /// </summary>
        public void SetTexture(int index, RenderTexture renderTexture, PolySpatialStereoFramebufferPass.FramebufferMode framebufferMode)
        {
            FramebufferMode = framebufferMode;
            switch (index)
            {
                case 0:
                    LeftColorTexture = renderTexture;
                    break;
                case 1:
                    LeftGBufferTexture = renderTexture;
                    break;
                case 2:
                    RightColorTexture = renderTexture;
                    break;
                case 3:
                    RightGBufferTexture = renderTexture;
                    break;
                default:
                    Logging.LogWarning($"PolySpatialStereoFramebuffer does not support index {index}");
                    return;
            }

            SetCameraFrustumFromDeviceSettings();
            m_FramebufferUpdated.Invoke(this);
        }

        /// <summary>
        /// This does not do anything on device, but in the editor
        /// it will force this cameras frustum to match what is entered
        /// for the stereo renderer device polyspatial settings in order
        /// to give the user a better preview in the editor.
        /// </summary>
        void SetCameraFrustumFromDeviceSettings()
        {
            // Don't execute on device! Values come from DisplayProvider.
            if (!Application.isEditor)
                return;

            var leftHalfAngles = PolySpatialSettings.Instance.DeviceDisplayProviderParameters.leftProjectionHalfAngles;
            var rightHalfAngles = PolySpatialSettings.Instance.DeviceDisplayProviderParameters.rightProjectionHalfAngles;

            var near = Camera.nearClipPlane;
            var far = Camera.farClipPlane;

            // Left and right halfway between combined frustras so that preview will be same ratio and dimension
            // of what it will be on device, but what it shows is in the center of both eyes.
            var left = (leftHalfAngles.left + rightHalfAngles.left) / 2.0f;
            var right = (leftHalfAngles.right + rightHalfAngles.right) / 2.0f;
            // Bottom and top must invert reasons I'm not completely sure.
            var top = -Mathf.Max(leftHalfAngles.top, rightHalfAngles.top);
            var bottom = -Mathf.Min(leftHalfAngles.bottom, rightHalfAngles.bottom);

            // Create a projection from tangents of the half angle from center.
            // This is the format the display provider internally uses.
            var deltaX = 1.0f / (right - left);
            var deltaY = 1.0f / (bottom - top);
            var deltaZ = 1.0f / (far - near);
            var centerX = right + left;
            var centerY = bottom + top;
            var proj = new Matrix4x4
            {
                [0, 0] = 2 * deltaX, [0, 1] = 0,          [0, 2] = centerX * deltaX, [0, 3] = 0,
                [1, 0] = 0,          [1, 1] = 2 * deltaY, [1, 2] = centerY * deltaY, [1, 3] = 0,
                [2, 0] = 0,          [2, 1] = 0,          [2, 2] = -far * deltaZ,    [2, 3] = -far * near * deltaZ,
                [3, 0] = 0,          [3, 1] = 0,          [3, 2] = -1.0f,            [3, 3] = 0
            };
            Camera.projectionMatrix = proj;

            // Setting this technically doesn't do anything, but it sets the FOV slider
            // in the camera to signify in the editor that value is being overriden
            Camera.fieldOfView = 2.0f * Mathf.Atan(1.0f / proj[1, 1]) * Mathf.Rad2Deg;

            // Fill in for other components to rely on. Not automatically calculated when setting projectionMatrix.
            Camera.aspect = proj[1, 1] / proj[0, 0];
        }

        void OnDrawGizmosSelected()
        {
            SetCameraFrustumFromDeviceSettings();
        }
    }
}
