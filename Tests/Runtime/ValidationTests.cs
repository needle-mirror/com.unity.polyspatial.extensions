using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Unity.PolySpatial.Extensions.RuntimeTests
{
    [TestFixture]
    class ValidationTests
    {
        [TestCase("Shader Graphs/FlatStereoscropicProjected")]
        [TestCase("Shader Graphs/DepthReprojection")]
        [TestCase("Shader Graphs/FlatStereoscropicStatic")]
        [Test]
        public void PolySpatialStereoFramebufferCamera_Shaders(string shaderName)
        {
            var shader = Shader.Find(shaderName);
            Assert.IsNotNull(shader);
        }

        // These tests aren't able to run in isolation because the stereo camera render requires additional SRP setup which is configured in the test proj.
#if POLYSPATIAL_INTERNAL

        GameObject m_StereoCameraGO;
        Camera m_MainCamera;
        PolySpatialStereoFramebufferCamera m_StereoCamera;

        // This value is defined in the scriptable renderer feature that we can't access in isolation.
        const int k_DefaultGBufferPixelToVertexRatio = 10;

        [SetUp]
        public void Setup()
        {
            CreateStereoCamera();
        }

        [TearDown]
        public void TearDown()
        {
            if (m_StereoCameraGO != null)
                GameObject.DestroyImmediate(m_StereoCameraGO);
        }

        // Creates an inactive GameObject with a camera and PolySpatialStereoFramebufferCamera component.
        void CreateStereoCamera()
        {
            m_StereoCameraGO = new GameObject();
            m_StereoCameraGO.SetActive(false);
            m_MainCamera = m_StereoCameraGO.AddComponent<Camera>();
            m_StereoCamera = m_StereoCameraGO.AddComponent<PolySpatialStereoFramebufferCamera>();
        }

        // Tests that the FramebufferUpdate callback of the StereoFrameBufferCamera is called.
        [UnityTest]
        public IEnumerator PolySpatialStereoFramebufferCamera_FramebufferUpdated()
        {
// TODO LXR-4146: Windows StereoFramebufferCamera logs "[Error] Unsupported D3D format 0x9"
#if UNITY_STANDALONE_WIN || UNITY_STANDALONE_LINUX
            yield break;
#else
            var framebufferUpdatedCallback = false;
            m_StereoCamera.FramebufferUpdated.AddListener((camera) =>
            {
                framebufferUpdatedCallback = true;
            });

            m_StereoCameraGO.SetActive(true);
            yield return null;
            Assert.IsTrue(framebufferUpdatedCallback);
#endif
        }

        // Tests that the default values of the StereoFramebufferCamera are as expected.
        [Test]
        public void PolySpatialStereoFramebufferCamera_Setup()
        {
#if UNITY_STANDALONE_WIN || UNITY_STANDALONE_LINUX
            return;
#else
            m_StereoCameraGO.SetActive(true);
            Assert.IsNotNull(m_StereoCamera);
            Assert.IsNotNull(m_StereoCamera.Camera);
            Assert.AreEqual(m_MainCamera, m_StereoCamera.Camera);
            Assert.IsTrue(m_StereoCamera.GenerateGBuffer, "Default value of GenerateGBuffer should be true.");
            Assert.IsTrue(m_StereoCamera.IsCameraAndComponentActive, "Stereo camera and component should be active.");
            Assert.AreEqual(PolySpatialStereoFramebufferPass.FramebufferMode.Mono, m_StereoCamera.FramebufferMode, "Stereo camera framebuffer mode should be mono by default.");
#endif
        }

        // Tests that the left eye color and gbuffer textures of the StereoFramebufferCamera are the correct resolution.
        [UnityTest]
        public IEnumerator PolySpatialStereoFramebufferCamera_Texture_Resolution()
        {
#if UNITY_STANDALONE_WIN || UNITY_STANDALONE_LINUX
            yield break;
#else
            var expectedGBufferDimensions = new Vector2Int(m_MainCamera.pixelWidth / k_DefaultGBufferPixelToVertexRatio,
                m_MainCamera.pixelHeight / k_DefaultGBufferPixelToVertexRatio);

            m_StereoCameraGO.SetActive(true);
            yield return null;

            Assert.IsNotNull(m_StereoCamera.LeftColorTexture);
            Assert.IsNotNull(m_StereoCamera.LeftGBufferTexture);

#if (UNITY_VISIONOS && !UNITY_EDITOR)
            // Since we aren't able to infer what the expected dimensions should be without using reflection to inspect the stereo camera renderer feature,
            // just confirm that the textures have greater than 0 resolution.
            Assert.Greater(m_StereoCamera.LeftColorTexture.width, 0);
            Assert.Greater(m_StereoCamera.LeftColorTexture.height, 0);
            Assert.Greater(m_StereoCamera.LeftGBufferTexture.width, 0);
            Assert.Greater(m_StereoCamera.LeftGBufferTexture.height, 0);
#else
            Assert.AreEqual(m_MainCamera.pixelWidth, m_StereoCamera.LeftColorTexture.width);
            Assert.AreEqual(m_MainCamera.pixelHeight, m_StereoCamera.LeftColorTexture.height);
            Assert.AreEqual(expectedGBufferDimensions.x, m_StereoCamera.LeftGBufferTexture.width);
            Assert.AreEqual(expectedGBufferDimensions.y, m_StereoCamera.LeftGBufferTexture.height);
#endif
#endif
        }

        // Tests that the material properties of the renderer associated with the StereoFrameBufferRenderer are as expected.
        [UnityTest]
        public IEnumerator PolySpatialStereoFramebufferRenderer_Material_Properties()
        {
#if UNITY_STANDALONE_WIN || UNITY_STANDALONE_LINUX
            yield break;
#else
            m_StereoCameraGO.SetActive(true);

            var frameBufferRendererGO = GameObject.CreatePrimitive(PrimitiveType.Quad);
            var frameBufferMeshRenderer = frameBufferRendererGO.GetComponent<MeshRenderer>();
            frameBufferMeshRenderer.sharedMaterial = null;
            frameBufferRendererGO.SetActive(false);

            var frameBufferRenderer = frameBufferRendererGO.AddComponent<PolySpatialStereoFramebufferRenderer>();
            frameBufferRenderer.m_StereoFramebufferCamera = m_StereoCamera;
            frameBufferRendererGO.SetActive(true);
            yield return null;

            var leftEyeColorPropertyID = Shader.PropertyToID("_LeftEyeColor");
            var fbMaterialTextureLeftColor = frameBufferMeshRenderer.material.GetTexture(leftEyeColorPropertyID);
#if (UNITY_VISIONOS && !UNITY_EDITOR)
            Assert.Greater(fbMaterialTextureLeftColor.width, 0);
            Assert.Greater(fbMaterialTextureLeftColor.height, 0);
#else
            Assert.AreEqual(m_MainCamera.pixelWidth, fbMaterialTextureLeftColor.width);
            Assert.AreEqual(m_MainCamera.pixelHeight, fbMaterialTextureLeftColor.height);
#endif
            Object.Destroy(frameBufferRenderer);
            GameObject.Destroy(frameBufferRendererGO);
#endif
        }

        // Tests that the scale of the StereoFramebufferRenderer's transform is adjusted to the correct aspect ratio.
        [UnityTest]
        public IEnumerator PolySpatialStereoFramebufferRenderer_Scale()
        {
#if UNITY_STANDALONE_WIN || UNITY_STANDALONE_LINUX
            yield break;
#else
            m_StereoCameraGO.SetActive(true);

            var frameBufferRendererGO = GameObject.CreatePrimitive(PrimitiveType.Quad);
            var frameBufferMeshRenderer = frameBufferRendererGO.GetComponent<MeshRenderer>();
            frameBufferMeshRenderer.sharedMaterial = null;
            frameBufferRendererGO.SetActive(false);
            var frameBufferRenderer = frameBufferRendererGO.AddComponent<PolySpatialStereoFramebufferRenderer>();
            frameBufferRenderer.m_StereoFramebufferCamera = m_StereoCamera;
            frameBufferRendererGO.SetActive(true);
            yield return null;

            // Verify mesh is scaled to produce the correct aspect ratio, which will occur if PolySpatialStereoFramebufferRenderer's m_ScaleToAspectRatio
            // is true (which it is by default).
            var expectedYScale = frameBufferRenderer.transform.localScale.x / m_StereoCamera.Camera.aspect;
            Assert.AreEqual(expectedYScale, frameBufferRenderer.transform.localScale.y);
            Object.Destroy(frameBufferRenderer);
            GameObject.Destroy(frameBufferRendererGO);
#endif
        }

        // Tests that the depth projection mode assigns the correct shader and verifies the mesh vertex count.
        [UnityTest]
        public IEnumerator PolySpatialStereoFramebufferRenderer_DepthReprojection()
        {
#if UNITY_STANDALONE_WIN || UNITY_STANDALONE_LINUX
            yield break;
#else
            var frameBufferRendererGO = GameObject.CreatePrimitive(PrimitiveType.Quad);
            frameBufferRendererGO.SetActive(false);

            var expectedGBufferDimensions = new Vector2Int(m_MainCamera.pixelWidth / k_DefaultGBufferPixelToVertexRatio,
                m_MainCamera.pixelHeight / k_DefaultGBufferPixelToVertexRatio);

            var frameBufferMeshRenderer = frameBufferRendererGO.GetComponent<MeshRenderer>();
            var frameBufferMeshFilter = frameBufferRendererGO.GetComponent<MeshFilter>();

            yield return null;

            var frameBufferRenderer = frameBufferRendererGO.AddComponent<PolySpatialStereoFramebufferRenderer>();
            frameBufferRenderer.m_StereoFramebufferCamera = m_StereoCamera;
            frameBufferRenderer.m_Mode = PolySpatialStereoFramebufferRenderer.StereoFramebufferMode.DepthReprojection;

            // Set the shared material to null so it gets re-initialized with the depth reprojection shader.
            frameBufferMeshRenderer.sharedMaterial = null;
            frameBufferMeshFilter.sharedMesh = null;

            frameBufferRenderer.ConfigureMaterialAndMesh();

            m_StereoCameraGO.SetActive(true);
            frameBufferRendererGO.SetActive(true);

            yield return null;

            Assert.AreEqual("Shader Graphs/DepthReprojection", frameBufferMeshRenderer.sharedMaterial.shader.name);

            // Check to see if the reprojection mesh was created with the correct amount of verts.
            var expectedVertexCount = expectedGBufferDimensions.x * expectedGBufferDimensions.y;

#if (UNITY_VISIONOS && !UNITY_EDITOR)
            Assert.Greater(frameBufferMeshFilter.sharedMesh.vertexCount, 0);
#else
            Assert.AreEqual(expectedVertexCount, frameBufferMeshFilter.sharedMesh.vertexCount);
#endif
            Object.Destroy(frameBufferRenderer);
            GameObject.Destroy(frameBufferRendererGO);
#endif
        }

        // Tests whether the StereoFramebufferRenderer's FlatStereoscopicProjected mode assigns the correct VP matrix to the material.
        [UnityTest]
        public IEnumerator PolySpatialStereoFramebufferRenderer_FlatStereoscopicProjected()
        {
#if UNITY_STANDALONE_WIN || UNITY_STANDALONE_LINUX
            yield break;
#else
            var frameBufferRendererGO = GameObject.CreatePrimitive(PrimitiveType.Quad);
            frameBufferRendererGO.SetActive(false);
            var frameBufferMeshRenderer = frameBufferRendererGO.GetComponent<MeshRenderer>();
            var frameBufferMeshFilter = frameBufferRendererGO.GetComponent<MeshFilter>();

            yield return null;

            var frameBufferRenderer = frameBufferRendererGO.AddComponent<PolySpatialStereoFramebufferRenderer>();
            frameBufferRenderer.m_StereoFramebufferCamera = m_StereoCamera;
            frameBufferRenderer.m_Mode = PolySpatialStereoFramebufferRenderer.StereoFramebufferMode.FlatStereoscopicProjected;

            // Set the shared material to null so it gets re-initialized with the depth reprojection shader.
            frameBufferMeshRenderer.sharedMaterial = null;
            frameBufferMeshFilter.sharedMesh = null;

            frameBufferRenderer.ConfigureMaterialAndMesh();

            m_StereoCameraGO.SetActive(true);
            frameBufferRendererGO.SetActive(true);

            yield return null;

            Assert.AreEqual("Shader Graphs/FlatStereoscropicProjected", frameBufferMeshRenderer.sharedMaterial.shader.name);

            var expectedMatrix = frameBufferRenderer.ViewProjectionMatrix(Camera.StereoscopicEye.Left);
            // Wait a frame until the matrix was updated.
            yield return null;
            var matrix = frameBufferMeshRenderer.sharedMaterial.GetMatrix("_LeftViewProjection");

            Assert.AreEqual(expectedMatrix, matrix);
            Object.Destroy(frameBufferRenderer);
            GameObject.Destroy(frameBufferRendererGO);
#endif
        }
#endif
    }
}
