using System;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

#if UNITY_EDITOR && ENABLE_CLOUD_SERVICES_ANALYTICS
using UnityEditor.PolySpatial.Analytics;
#endif

namespace Unity.PolySpatial.Extensions
{
    /// <summary>
    /// Utility to apply stereo render textures to a flat surface with the
    /// FlatStereoRendererTextures shader and properly configure it.
    /// </summary>
    [RequireComponent(typeof(MeshRenderer))]
    [RequireComponent(typeof(MeshFilter))]
    internal class PolySpatialStereoFramebufferRenderer : MonoBehaviour
    {
        [Tooltip("Camera this will display the output of.")]
        [SerializeField]
        internal PolySpatialStereoFramebufferCamera m_StereoFramebufferCamera;

        internal enum StereoFramebufferMode
        {
            FlatStereoscopicStatic,
            FlatStereoscopicProjected,
            DepthReprojection,
        }

        [Tooltip("FlatStereoscopicStatic statically places the stereo framebuffers on a flat surface with adjustable focus depth. " +
                 "FlatStereoscopicProjected dynamically places the stereo framebuffers on a flat surfaces using the camera projection " +
                 "DepthReprojection utilizes the depth buffer to reproject the stereo framebuffers in world space.")]
        [SerializeField]
        internal StereoFramebufferMode m_Mode;

        [Tooltip("Internally generate mesh to display reprojection.")]
        [SerializeField]
        bool m_GenerateReprojectionMesh = true;

        [Tooltip("Bounds of the reprojection mesh should. Needs to be big enough to trigger rendering from anywhere in world.")]
        [SerializeField]
        float m_ReprojectionMeshBounds = 1000f;

        [Tooltip("If enabled the offsets calculated via Focus Distance will be added to the left and right eye texture offsets " +
                 "to make the stereo framebuffer appear focused on a certain distance.")]
        [SerializeField]
        bool m_AddFocusDistanceToOffsets = true;

        [Tooltip("Distance used to calculate texture offsets applied to the left and right eye textures in order to make the " +
                 "stereo framebuffer appear focused on a certain distance.")]
        [SerializeField]
        [Min(0)]
        float m_FocusDistance = 2.0f;

        [Tooltip("Scales mesh to produce the correct aspect ratio for the stereo framebuffer.")] [SerializeField]
        bool m_ScaleToAspectRatio = true;

        const string k_DefaultFlatStereoscopicShader = "Shader Graphs/FlatStereoscropicStatic";
        const string k_DefaultFlatStereoscopicProjectedShader = "Shader Graphs/FlatStereoscropicProjected";
        const string k_DefaultDepthReprojectionShader = "Shader Graphs/DepthReprojection";
        static readonly int k_LeftColorFramebuffer = Shader.PropertyToID("_LeftEyeColor");
        static readonly int k_LeftDepthFramebuffer = Shader.PropertyToID("_LeftEyeGBuffer");
        static readonly int k_RightColorFramebuffer = Shader.PropertyToID("_RightEyeColor");
        static readonly int k_RightDepthFramebuffer = Shader.PropertyToID("_RightEyeGBuffer");
        static readonly int k_LeftViewProjection = Shader.PropertyToID("_LeftViewProjection");
        static readonly int k_RightViewProjection = Shader.PropertyToID("_RightViewProjection");
        static readonly int k_LefInvViewProjection = Shader.PropertyToID("_LefInvViewProjection");
        static readonly int k_RightInvViewProjection = Shader.PropertyToID("_RightInvViewProjection");

        Renderer m_Renderer;
        MeshFilter m_Filter;
        Vector2Int m_ReprojectionMeshSize;
        StereoFramebufferMode m_RuntimeMode;
        Vector2 m_InitialLeftOffset;
        Vector2 m_InitialRightOffset;


        void Awake()
        {
            m_Filter = GetComponent<MeshFilter>();
            m_Renderer = GetComponent<MeshRenderer>();
            if (m_StereoFramebufferCamera != null)
            {
                m_StereoFramebufferCamera.FramebufferUpdated.AddListener(FramebufferUpdated);
                FramebufferUpdated(m_StereoFramebufferCamera);
            }

#if UNITY_EDITOR && ENABLE_CLOUD_SERVICES_ANALYTICS
            if (Application.isPlaying && gameObject.scene.IsValid())
            {
                PolySpatialAnalytics.Send(FeatureName.StereoRenderTargetMode, m_Mode.ToString());
            }
#endif
        }

        void OnEnable()
        {
            ConfigureMaterialAndMesh();
            m_InitialLeftOffset = m_Renderer.sharedMaterial.GetTextureOffset(k_LeftColorFramebuffer);
            m_InitialRightOffset = m_Renderer.sharedMaterial.GetTextureOffset(k_RightColorFramebuffer);
            m_RuntimeMode = m_Mode;
        }

        void OnDestroy()
        {
            if (m_StereoFramebufferCamera != null)
            {
                m_StereoFramebufferCamera.FramebufferUpdated.RemoveListener(FramebufferUpdated);
            }
        }

        void OnValidate()
        {
            ConfigureMaterialAndMesh();
        }

        internal void ConfigureMaterialAndMesh()
        {
            m_Filter ??= GetComponent<MeshFilter>();
            m_Renderer ??= GetComponent<MeshRenderer>();

            switch (m_Mode)
            {
                case StereoFramebufferMode.FlatStereoscopicStatic:
                    // Let you update the focus while playing
                    if (Application.isPlaying && m_StereoFramebufferCamera != null && m_StereoFramebufferCamera.isActiveAndEnabled)
                        FramebufferUpdated(m_StereoFramebufferCamera);

                    if (m_Renderer.sharedMaterial == null ||
                        m_Renderer.sharedMaterial.shader == null ||
                        m_Renderer.sharedMaterial.shader.name == k_DefaultDepthReprojectionShader ||
                        m_Renderer.sharedMaterial.shader.name == k_DefaultFlatStereoscopicProjectedShader)
                    {
                        m_Renderer.sharedMaterial = new Material(Shader.Find(k_DefaultFlatStereoscopicShader));
                    }

                    if (m_Filter.sharedMesh == null)
                        m_Filter.sharedMesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx");

                    break;
                case StereoFramebufferMode.FlatStereoscopicProjected:
                    if (m_Renderer.sharedMaterial == null ||
                        m_Renderer.sharedMaterial.shader == null ||
                        m_Renderer.sharedMaterial.shader.name == k_DefaultDepthReprojectionShader ||
                        m_Renderer.sharedMaterial.shader.name == k_DefaultFlatStereoscopicShader)
                    {
                        m_Renderer.sharedMaterial = new Material(Shader.Find(k_DefaultFlatStereoscopicProjectedShader));
                    }

                    if (m_Filter.sharedMesh == null)
                        m_Filter.sharedMesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx");

                    break;
                case StereoFramebufferMode.DepthReprojection:

                    if (m_Renderer.sharedMaterial == null ||
                        m_Renderer.sharedMaterial.shader == null ||
                        m_Renderer.sharedMaterial.shader.name == k_DefaultFlatStereoscopicShader ||
                        m_Renderer.sharedMaterial.shader.name == k_DefaultFlatStereoscopicProjectedShader)
                    {
                        m_Renderer.sharedMaterial = new Material(Shader.Find(k_DefaultDepthReprojectionShader));
                    }

                    if (m_Filter.sharedMesh != null && m_Filter.sharedMesh.name == "Quad")
                        m_Filter.sharedMesh = null;

                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        /// <summary>
        /// Can be called from event on PolySpatialStereoFramebuffer to update MeshRender.
        /// </summary>
        public void FramebufferUpdated(PolySpatialStereoFramebufferCamera stereoFramebufferCamera)
        {
            m_Filter ??= GetComponent<MeshFilter>();
            m_Renderer ??= GetComponent<MeshRenderer>();

            if (m_Renderer.sharedMaterial == null)
                return;

            switch (m_RuntimeMode)
            {
                case StereoFramebufferMode.FlatStereoscopicStatic:
                    SetFlatStereoscopicTextureOffsets();
                    if (m_ScaleToAspectRatio)
                    {
                        var newScale = transform.localScale;
                        newScale.y = newScale.x / m_StereoFramebufferCamera.Camera.aspect;
                        transform.localScale = newScale;
                    }

                    break;
                case StereoFramebufferMode.FlatStereoscopicProjected:
                    break;
                case StereoFramebufferMode.DepthReprojection:
                    if (m_GenerateReprojectionMesh &&
                        stereoFramebufferCamera.LeftGBufferTexture != null &&
                        (m_ReprojectionMeshSize.x != stereoFramebufferCamera.LeftGBufferTexture.width ||
                         m_ReprojectionMeshSize.y != stereoFramebufferCamera.LeftGBufferTexture.height))
                    {
                        m_ReprojectionMeshSize = new Vector2Int(stereoFramebufferCamera.LeftGBufferTexture.width,
                            stereoFramebufferCamera.LeftGBufferTexture.height);
                        m_Filter.sharedMesh = GenerateReprojectionMesh(
                            new Vector2Int(stereoFramebufferCamera.LeftGBufferTexture.width, stereoFramebufferCamera.LeftGBufferTexture.height),
                            m_ReprojectionMeshBounds);
                    }

                    if (transform.position != Vector3.zero)
                    {
                        Logging.LogWarning($"Setting {name} position to world origin. This is necessary for reprojection to line up correctly with world.");
                        transform.position = Vector3.zero;
                    }

                    if (!m_StereoFramebufferCamera.GenerateGBuffer)
                        Logging.LogError($"{name} is trying to use depth reprojection on {m_StereoFramebufferCamera.name} which does not have GenerateGBuffer enabled.");

                    break;
            }

            m_Renderer.material.SetTexture(k_LeftColorFramebuffer, stereoFramebufferCamera.LeftColorTexture);
            m_Renderer.material.SetTexture(k_LeftDepthFramebuffer, stereoFramebufferCamera.LeftGBufferTexture);
            m_Renderer.material.SetTexture(k_RightColorFramebuffer, stereoFramebufferCamera.RightColorTexture);
            m_Renderer.material.SetTexture(k_RightDepthFramebuffer, stereoFramebufferCamera.RightGBufferTexture);
            m_StereoFramebufferCamera = stereoFramebufferCamera;
        }

        void LateUpdate()
        {
            switch (m_RuntimeMode)
            {
                case StereoFramebufferMode.FlatStereoscopicStatic:
                    break;
                case StereoFramebufferMode.FlatStereoscopicProjected:
                    var leftViewProj = ViewProjectionMatrix(Camera.StereoscopicEye.Left);
                    m_Renderer.material.SetMatrix(k_LeftViewProjection, leftViewProj);

                    if (m_StereoFramebufferCamera.FramebufferMode == PolySpatialStereoFramebufferPass.FramebufferMode.StereoSinglePass) {
                        var rightViewProj = ViewProjectionMatrix(Camera.StereoscopicEye.Right);
                        m_Renderer.material.SetMatrix(k_RightViewProjection, rightViewProj);
                    }

                    // This is a workaround to trigger the matrix update above
                    m_Renderer.material.SetTexture(k_LeftColorFramebuffer, null);
                    m_Renderer.material.SetTexture(k_LeftColorFramebuffer, m_StereoFramebufferCamera.LeftColorTexture);

                    break;
                case StereoFramebufferMode.DepthReprojection:
                    var leftInvViewProj = ViewProjectionMatrix(Camera.StereoscopicEye.Left).inverse;
                    m_Renderer.material.SetMatrix(k_LefInvViewProjection, leftInvViewProj);

                    if (m_StereoFramebufferCamera.FramebufferMode == PolySpatialStereoFramebufferPass.FramebufferMode.StereoSinglePass) {
                        var rightInvViewProj = ViewProjectionMatrix(Camera.StereoscopicEye.Right).inverse;
                        m_Renderer.material.SetMatrix(k_RightInvViewProjection, rightInvViewProj);
                    }

                    // This is a workaround to trigger the matrix update above
                    m_Renderer.material.SetTexture(k_LeftColorFramebuffer, null);
                    m_Renderer.material.SetTexture(k_LeftColorFramebuffer, m_StereoFramebufferCamera.LeftColorTexture);

                    break;
            }
        }

        void SetFlatStereoscopicTextureOffsets()
        {
            if (!m_AddFocusDistanceToOffsets)
                return;

            // Calculate the UV offset of a point in the center of the camera at specified m_FocusDistance
            // to determine how much to offset the textures to make it appear that is the focal point.
            var halfIpd = m_StereoFramebufferCamera.Camera.stereoSeparation * 0.5f;
            var rotation = Matrix4x4.Rotate(Quaternion.AngleAxis(m_StereoFramebufferCamera.transform.localEulerAngles.z, Vector3.forward));

            var leftProj = ProjMatrix(Camera.StereoscopicEye.Left);
            var leftView = (rotation * Matrix4x4.Translate(new Vector3(halfIpd, 0, 0))).inverse;

            var rightProj = ProjMatrix(Camera.StereoscopicEye.Right);
            var rightView = (rotation * Matrix4x4.Translate(new Vector3(-halfIpd, 0, 0))).inverse;

            var worldCenter = new Vector3(0, 0, m_FocusDistance);
            var leftUVOffset = WorldPositionToUVOffset(leftView, leftProj, worldCenter);
            var rightUVOffset = WorldPositionToUVOffset(rightView, rightProj, worldCenter);

            m_Renderer.sharedMaterial.SetTextureOffset(k_LeftColorFramebuffer, m_InitialLeftOffset + leftUVOffset);
            m_Renderer.sharedMaterial.SetTextureOffset(k_RightColorFramebuffer, m_InitialRightOffset + rightUVOffset);
        }

        Matrix4x4 ProjMatrix(Camera.StereoscopicEye eye)
        {
            return GL.GetGPUProjectionMatrix(m_StereoFramebufferCamera.FramebufferMode != PolySpatialStereoFramebufferPass.FramebufferMode.Mono
                ? m_StereoFramebufferCamera.Camera.GetStereoProjectionMatrix(eye)
                : m_StereoFramebufferCamera.Camera.projectionMatrix, true);
        }

        internal Matrix4x4 ViewProjectionMatrix(Camera.StereoscopicEye eye)
        {
            var view =  m_StereoFramebufferCamera.FramebufferMode != PolySpatialStereoFramebufferPass.FramebufferMode.Mono ?
                m_StereoFramebufferCamera.Camera.GetStereoViewMatrix(eye) :
                m_StereoFramebufferCamera.Camera.worldToCameraMatrix;
            return (ProjMatrix(eye) * view);
        }

        static Vector2 WorldPositionToUVOffset(Matrix4x4 view, Matrix4x4 proj, Vector3 worldPosition)
        {
            var viewProj = (proj * view);
            var clipPosition = viewProj * new Vector4(worldPosition.x, worldPosition.y, worldPosition.z, 1.0f);
            var ndc = new Vector3(clipPosition.x / clipPosition.w, clipPosition.y / clipPosition.w, clipPosition.z / clipPosition.w);
            var uvOffset = new Vector2(ndc.x * 0.5f, -ndc.y * 0.5f); // Don't add 0.5f because we don't want the actual UV, we want offset from center
            return uvOffset;
        }

        static Mesh GenerateReprojectionMesh(Vector2Int vertCount, float bounds)
        {
            var totalVertCount = vertCount.x * vertCount.y;
            var positions = new Vector3[totalVertCount];
            var uvs = new Vector2[totalVertCount];
            var totalSquareCount = (vertCount.x * vertCount.y) - (vertCount.x - 1) - (vertCount.y - 1) - 1;
            var totalTriCount = totalSquareCount * 2;
            var totalIndexCount = totalTriCount * 3;
            var indices = new int[totalIndexCount];

            var squareSize = new Vector2(1.0f / (vertCount.x - 1), 1.0f / (vertCount.y - 1));
            var vertex = 0;
            var index = 0;
            var halfBounds = bounds * 0.5f;
            for (var y = 0; y < vertCount.y; ++y)
            {
                for (var x = 0; x < vertCount.x; ++x)
                {
                    // We are generating bigger mesh so its bounds is always visible, but its actual vertices will be moved by shader
                    positions[vertex] = new Vector3(x * bounds - halfBounds, y * bounds - halfBounds, y * bounds - halfBounds);
                    uvs[vertex] = new Vector2((float)x * squareSize.x, (float)y * squareSize.y);

                    if (x < vertCount.x - 1 && y < vertCount.y - 1)
                    {
                        indices[index++] = vertex;
                        indices[index++] = vertex + vertCount.x;
                        indices[index++] = vertex + 1;

                        indices[index++] = vertex + 1;
                        indices[index++] = vertex + vertCount.x;
                        indices[index++] = vertex + vertCount.x + 1;
                    }

                    vertex++;
                }
            }

            var grid = new Mesh
            {
                vertices = positions,
                triangles = indices,
                uv = uvs,
            };
            grid.UploadMeshData(false);
            return grid;
        }


#if UNITY_EDITOR
        [CustomEditor(typeof(PolySpatialStereoFramebufferRenderer))]
        internal class PolySpatialUnityStereoRendererDisplayEditor : UnityEditor.Editor
        {
            PolySpatialStereoFramebufferRenderer m_StereoRendererDisplay;
            SerializedProperty m_ModeProperty;
            SerializedProperty m_StereoFramebufferCameraProperty;
            SerializedProperty m_GenerateReprojectionMeshProperty;
            SerializedProperty m_ReprojectionMeshBoundsProperty;
            SerializedProperty m_AddFocusDistanceToOffsetsProperty;
            SerializedProperty m_FocusDistanceProperty;
            SerializedProperty m_ScaleToAspectRatioProperty;

            void OnEnable()
            {
                m_StereoRendererDisplay = (PolySpatialStereoFramebufferRenderer)target;
                m_StereoFramebufferCameraProperty = serializedObject.FindProperty("m_StereoFramebufferCamera");
                m_ModeProperty = serializedObject.FindProperty("m_Mode");
                m_GenerateReprojectionMeshProperty = serializedObject.FindProperty("m_GenerateReprojectionMesh");
                m_ReprojectionMeshBoundsProperty = serializedObject.FindProperty("m_ReprojectionMeshBounds");
                m_AddFocusDistanceToOffsetsProperty = serializedObject.FindProperty("m_AddFocusDistanceToOffsets");
                m_FocusDistanceProperty = serializedObject.FindProperty("m_FocusDistance");
                m_ScaleToAspectRatioProperty = serializedObject.FindProperty("m_ScaleToAspectRatio");

                // EditorApplication.update += ForceRepaint;
            }

            void OnDisable()
            {
                // EditorApplication.update -= ForceRepaint;
            }

            void ForceRepaint()
            {
                Repaint();
            }

            void DrawTexture(Texture texture)
            {
                var aspect = (float)texture.height / texture.width;
                var previewRect = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none,
                    GUILayout.Height(EditorGUIUtility.currentViewWidth * aspect));
                GUI.DrawTexture(previewRect, texture, ScaleMode.ScaleToFit, true);
            }

            public override void OnInspectorGUI()
            {
                serializedObject.Update();

                // Only switch mode in edit mode
                if (Application.isPlaying) GUI.enabled = false;
                EditorGUILayout.PropertyField(m_StereoFramebufferCameraProperty);
                EditorGUILayout.PropertyField(m_ModeProperty);
                if (Application.isPlaying) GUI.enabled = true;

                EditorGUI.indentLevel++;
                switch ((StereoFramebufferMode)m_ModeProperty.enumValueFlag)
                {
                    case StereoFramebufferMode.FlatStereoscopicStatic:
                        EditorGUILayout.PropertyField(m_AddFocusDistanceToOffsetsProperty);
                        GUI.enabled = m_AddFocusDistanceToOffsetsProperty.boolValue;
                        EditorGUI.indentLevel++;
                        EditorGUILayout.PropertyField(m_FocusDistanceProperty);
                        EditorGUI.indentLevel--;
                        GUI.enabled = true;
                        EditorGUILayout.PropertyField(m_ScaleToAspectRatioProperty);
                        break;
                    case StereoFramebufferMode.DepthReprojection:
                        EditorGUILayout.PropertyField(m_GenerateReprojectionMeshProperty);
                        EditorGUILayout.PropertyField(m_ReprojectionMeshBoundsProperty);
                        break;
                    default:
                        break;
                }

                EditorGUI.indentLevel--;

                serializedObject.ApplyModifiedProperties();

                if (m_StereoRendererDisplay.m_StereoFramebufferCamera == null)
                    return;

                if (m_StereoRendererDisplay.m_StereoFramebufferCamera.LeftColorTexture != null)
                {
                    EditorGUILayout.LabelField("Left Color Texture");
                    DrawTexture(m_StereoRendererDisplay.m_StereoFramebufferCamera.LeftColorTexture);
                }

                if (m_StereoRendererDisplay.m_StereoFramebufferCamera.LeftGBufferTexture != null)
                {
                    EditorGUILayout.LabelField("Left GBuffer Texture");
                    DrawTexture(m_StereoRendererDisplay.m_StereoFramebufferCamera.LeftGBufferTexture);
                }

                if (m_StereoRendererDisplay.m_StereoFramebufferCamera.RightColorTexture != null)
                {
                    EditorGUILayout.LabelField("Right Color Texture");
                    DrawTexture(m_StereoRendererDisplay.m_StereoFramebufferCamera.RightColorTexture);
                }

                if (m_StereoRendererDisplay.m_StereoFramebufferCamera.RightGBufferTexture != null)
                {
                    EditorGUILayout.LabelField("Right GBuffer Texture");
                    DrawTexture(m_StereoRendererDisplay.m_StereoFramebufferCamera.RightGBufferTexture);
                }
            }
        }
#endif
    }
}
