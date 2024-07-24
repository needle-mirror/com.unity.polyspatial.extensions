using System;
using System.Collections.Generic;
using Unity.PolySpatial.Internals;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace Unity.PolySpatial.Extensions
{
    /// <summary>
    /// Captures the Stereo Framebuffer from a camera with the PolySpatialStereoFramebuffer
    /// added to it, then sends it to the PolySpatial host.
    /// </summary>
    internal class PolySpatialStereoFramebufferPass : ScriptableRenderPass, IDisposable
    {
        const string k_BlitShaderName = "Hidden/PolySpatial/StereoRendererBlit";
        const string k_DepthBlitShaderName = "Hidden/PolySpatial/StereoRendererGbufferBlit";

        static readonly int k_GBufferDimensions = Shader.PropertyToID("_GBufferDimensions");
        static readonly int k_BlitTexArraySlice = Shader.PropertyToID("_BlitTexArraySlice");

        readonly Dictionary<Camera, FramebufferData> m_CameraToFramebuffer = new();

        internal enum FramebufferMode
        {
            Mono,
            StereoMultiPass,
            StereoSinglePass,
        }

        class FramebufferData : IDisposable
        {
            internal const int FramebufferCapacity = 4;

            internal readonly PolySpatialAssetID[] TextureIds = new PolySpatialAssetID[FramebufferCapacity];
            internal readonly RTHandle[] Handles = new RTHandle[FramebufferCapacity];

            // Unfortunately since current Blitter API does not allow you to send in a MaterialPropertyBlock
            // you need to keep a separate Material instance per texture blitted otherwise changing properties
            // mid pass would override those material properties for the entire pass
            internal readonly Material[] BlitMaterials = new Material[FramebufferCapacity];

            // GBuffer currently only carries positional data to offset vertices in a grid mesh, since you only need
            // one pixel of positional data per vertex, gbuffer can be divided so the pixels count matches the vertex
            // count of the re-projection mesh
            internal readonly int GBufferPixelToVertexRatio;

            internal readonly Vector2Int GBufferDimensions;
            internal readonly Vector2Int Dimensions;
            internal readonly PolySpatialInstanceID InstanceID;
            internal readonly FramebufferMode FramebufferMode;
            internal readonly PolySpatialStereoFramebufferCamera StereoFramebufferCamera;

            internal FramebufferData(
                FramebufferMode framebufferMode,
                Vector2Int dimensions,
                int gBufferPixelToVertexRatio,
                PolySpatialStereoFramebufferCamera stereoFramebufferCamera)
            {

                FramebufferMode = framebufferMode;
                Dimensions = dimensions;
                GBufferPixelToVertexRatio = gBufferPixelToVertexRatio;
                StereoFramebufferCamera = stereoFramebufferCamera;
                GBufferDimensions = new Vector2Int(Dimensions.x / GBufferPixelToVertexRatio, Dimensions.y / GBufferPixelToVertexRatio);

                CreateMaterials();
                CreateRenderTextures();
                SetStereoFramebufferCameraTextures();
            }

            internal bool usesRightEye => FramebufferMode != FramebufferMode.Mono;

            internal bool isDepthIndex(int index) => index % 2 > 0;
            internal bool isRightIndex(int index) => index >= 2;

            internal int count => usesRightEye ? 4 : 2;

            internal PolySpatialAssetID LeftColorID => TextureIds[0];
            internal RTHandle LeftColorHandle => Handles[0];
            internal Material LeftColorBlitMaterial => BlitMaterials[0];

            internal PolySpatialAssetID LeftGBufferID => TextureIds[1];
            internal RTHandle LeftGBufferHandle => Handles[1];
            internal Material LeftGBufferBlitMaterial => BlitMaterials[1];

            internal PolySpatialAssetID RightColorID => TextureIds[2];
            internal RTHandle RightColorHandle => Handles[2];
            internal Material RightColorBlitMaterial => BlitMaterials[2];

            internal PolySpatialAssetID RightGBufferID => TextureIds[3];
            internal RTHandle RightGBufferHandle => Handles[3];
            internal Material RightGBufferBlitMaterial => BlitMaterials[3];

            public void Dispose()
            {
                for (var i = 0; i < FramebufferCapacity; ++i)
                {
                    PolySpatialCore.UnitySimulation?.AssetManager.Unregister(TextureIds[i]);
                    Handles[i]?.Release();
                    Handles[i] = null;
                    TextureIds[i] = PolySpatialAssetID.InvalidAssetID;
                    if (BlitMaterials[i] != null)
                    {
                        BlitMaterials[i].DestroyAppropriately();
                        BlitMaterials[i] = null;
                    }
                }
            }

            void CreateMaterials()
            {
                var blitPropertyDimensions = new Vector4(GBufferDimensions.x, GBufferDimensions.y, 1, 1);
                var colorBlitShader = Shader.Find(k_BlitShaderName);
                var depthBlitShader = Shader.Find(k_DepthBlitShaderName);
                for (var i = 0; i < count; ++i)
                {
                    BlitMaterials[i] = isDepthIndex(i) ? CoreUtils.CreateEngineMaterial(depthBlitShader) : CoreUtils.CreateEngineMaterial(colorBlitShader);
                    BlitMaterials[i].SetInt(k_BlitTexArraySlice, isRightIndex(i) ? 1 : 0);
                    if (isDepthIndex(i))
                        BlitMaterials[i].SetVector(k_GBufferDimensions, blitPropertyDimensions);
                    else
                        SetMaterialKeywords(BlitMaterials[i]);
                }
            }

            void CreateRenderTextures()
            {
                Logging.Log($"Creating StereoRenderFramebuffer: {FramebufferMode} \n" +
                            $"Color: {Dimensions.x} {Dimensions.y} \n" +
                            $"GBuffer: {GBufferDimensions.x} {GBufferDimensions.y} \n");

                for (var i = 0; i < count; ++i)
                {
                    var renderTexture = isDepthIndex(i)
                        ? new RenderTexture(
                            GBufferDimensions.x,
                            GBufferDimensions.y,
                            GraphicsFormat.R16G16B16A16_SFloat,
                            GraphicsFormat.None)
                        : new RenderTexture(
                            Dimensions.x,
                            Dimensions.y,
                            GraphicsFormat.B8G8R8A8_UNorm,
                            GraphicsFormat.None);
                    renderTexture.Create();

                    Handles[i] = RTHandles.Alloc(renderTexture);
                    TextureIds[i] = PolySpatialAssetID.CreateUnique();

                    PolySpatialCore.UnitySimulation.AssetManager.Register(Handles[i].rt, TextureIds[i]);
                }
            }

            void SetStereoFramebufferCameraTextures()
            {
                for (var i = 0; i < count; ++i)
                    StereoFramebufferCamera.SetTexture(i, Handles[i].rt, FramebufferMode);
            }

            void SetMaterialKeywords(Material material)
            {
                // We are overriding values in the built-in blit shader so that we
                // can selectively blit one eye texture out of the single pass texture array.
                // This is because RealityKit does not support texture arrays with the drawable queue.
                switch (FramebufferMode)
                {
                    case FramebufferMode.Mono:
                    case FramebufferMode.StereoMultiPass:
                        material.DisableKeyword("USE_TEXTURE2D_X_AS_ARRAY");
                        material.DisableKeyword("BLIT_SINGLE_SLICE");
                        break;
                    case FramebufferMode.StereoSinglePass:
                        material.EnableKeyword("USE_TEXTURE2D_X_AS_ARRAY");
                        material.EnableKeyword("BLIT_SINGLE_SLICE");
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
        }

        public void ValidateFramebufferData(
            PolySpatialStereoFramebufferCamera stereoFramebufferCamera,
            Camera renderingCamera,
            int gBufferDivide,
            ref RenderingData renderingData)
        {
            var cameraTargetDescriptor = renderingData.cameraData.cameraTargetDescriptor;
            var dimensions = new Vector2Int(cameraTargetDescriptor.width, cameraTargetDescriptor.height);
            var framebufferMode = renderingData.cameraData.xrRendering switch
            {
                true when renderingData.cameraData.xr.singlePassEnabled => FramebufferMode.StereoSinglePass,
                true => FramebufferMode.StereoMultiPass,
                _ => FramebufferMode.Mono
            };

            // Check and create framebuffer data if needed
            if (m_CameraToFramebuffer.TryGetValue(renderingCamera, out var framebufferData))
            {
                if (framebufferData.Dimensions == dimensions &&
                    framebufferData.GBufferPixelToVertexRatio == gBufferDivide &&
                    // Unfortunately the news might arrive late that the device is running in single pass, so you must re-init.
                    framebufferData.FramebufferMode == framebufferMode)
                    return;

                framebufferData.Dispose();
                m_CameraToFramebuffer.Remove(renderingCamera);
            }

            var newFramebufferData = new FramebufferData(
                framebufferMode,
                dimensions,
                gBufferDivide,
                stereoFramebufferCamera);
            m_CameraToFramebuffer.Add(renderingCamera, newFramebufferData);
        }

        class PassData
        {
            internal TextureHandle srcTexture;
            internal Material blitMaterial;
            internal Matrix4x4 invViewProj;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameContext)
        {
            var resourceData = frameContext.Get<UniversalResourceData>();
            var cameraData = frameContext.Get<UniversalCameraData>();

            // Even though scene camera is never enqueued, this still can get fired with the scene camera
            if (!m_CameraToFramebuffer.TryGetValue(cameraData.camera, out var framebufferData))
                return;

            using (var builder = renderGraph.AddRasterRenderPass<PassData>("BlitLeftColorStereoRenderTarget", out var passData))
            {
                passData.srcTexture = resourceData.cameraColor;
                passData.blitMaterial = framebufferData.LeftColorBlitMaterial;

                builder.UseTexture(passData.srcTexture);
                builder.SetRenderAttachment(renderGraph.ImportTexture(framebufferData.LeftColorHandle), 0);
                builder.SetRenderFunc<PassData>(ExecuteBlitPass);

                PolySpatialObjectUtils.MarkDirty(framebufferData.LeftColorHandle);
            }

            if (framebufferData.StereoFramebufferCamera.GenerateGBuffer)
            {
                using (var builder = renderGraph.AddRasterRenderPass<PassData>("BlitLeftDepthStereoRenderTarget", out var passData))
                {
                    passData.srcTexture = resourceData.cameraDepthTexture;
                    passData.blitMaterial = framebufferData.LeftGBufferBlitMaterial;

                    builder.UseTexture(passData.srcTexture);
                    builder.SetRenderAttachment(renderGraph.ImportTexture(framebufferData.LeftGBufferHandle), 0);
                    builder.SetRenderFunc<PassData>(ExecuteBlitPass);

                    PolySpatialObjectUtils.MarkDirty(framebufferData.LeftGBufferHandle);
                }
            }

            if (framebufferData.FramebufferMode != FramebufferMode.StereoSinglePass)
                return;

            using (var builder = renderGraph.AddRasterRenderPass<PassData>("BlitRightColorStereoRenderTarget", out var passData))
            {
                passData.srcTexture = resourceData.cameraColor;
                passData.blitMaterial = framebufferData.RightColorBlitMaterial;

                builder.UseTexture(passData.srcTexture);
                builder.SetRenderAttachment(renderGraph.ImportTexture(framebufferData.RightColorHandle), 0);
                builder.SetRenderFunc<PassData>(ExecuteBlitPass);

                PolySpatialObjectUtils.MarkDirty(framebufferData.RightColorHandle);
            }


            if (framebufferData.StereoFramebufferCamera.GenerateGBuffer)
            {
                using (var builder = renderGraph.AddRasterRenderPass<PassData>("BlitRightDepthStereoRenderTarget", out var passData))
                {
                    passData.srcTexture = resourceData.cameraDepthTexture;
                    passData.blitMaterial = framebufferData.RightGBufferBlitMaterial;

                    builder.UseTexture(passData.srcTexture);
                    builder.SetRenderAttachment(renderGraph.ImportTexture(framebufferData.RightGBufferHandle), 0);
                    builder.SetRenderFunc<PassData>(ExecuteBlitPass);

                    PolySpatialObjectUtils.MarkDirty(framebufferData.RightGBufferHandle);
                }
            }
        }

        void ExecuteBlitPass(PassData data, RasterGraphContext context)
        {
            Blitter.BlitTexture(context.cmd, data.srcTexture, new Vector4(1, 1, 0, 0), data.blitMaterial, 0);
        }

        public void Dispose()
        {
            foreach (var frameBuffer in m_CameraToFramebuffer)
            {
                frameBuffer.Value.Dispose();
            }

            m_CameraToFramebuffer.Clear();
        }
    }
}
