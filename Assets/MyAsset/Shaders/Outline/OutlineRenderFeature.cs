using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RendererUtils;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;

public class OutlineRenderFeature : ScriptableRendererFeature
{
    [SerializeField] private Settings _settings;
    [SerializeField] private RenderPassEvent passEvent;
    
    [Serializable]
    public class Settings
    {
        public LayerMask layerMask;
        [Min(1)] public int passCount;
    }
    private BlurRenderPass _blurRenderPass;
    private CopyToTempPass _copyToTempPass;
    private OutlineRenderPass _outlineRenderPass;
    private OutlineViewRenderPass _outlineViewRenderPass;
    
    private Material _objectMaskMaterial;
    private Material _blurMaterial;
    private Material _subtractMaterial;

    public class CopyToTempPass : ScriptableRenderPass
    {
        private LayerMask _layerMask;
        private Material _objectMask;
        
        readonly ShaderTagId[] _shaderTags = {
            new ShaderTagId("UniversalForward"),
            new ShaderTagId("UniversalForwardOnly"),
            new ShaderTagId("SRPDefaultUnlit")
        };
        public class PassData
        {
            public TextureHandle textureHandle;
            public RendererListHandle rendererListHandle;
        }

        public class TransferTextureData : ContextItem
        {
            public TextureHandle textureHandle;
            public TextureHandle originalTextureHandle;
            public TextureHandle result;
            public override void Reset()
            {
                textureHandle = TextureHandle.nullHandle;
                originalTextureHandle = TextureHandle.nullHandle;
                result  = TextureHandle.nullHandle;
            }
        }

        public CopyToTempPass(LayerMask layerMask, Material maskMaterial)
        {
            _layerMask = layerMask;
            _objectMask = maskMaterial;
        }
        
        private PassData _passData = new PassData();

        public void ExecutePass(PassData passData, RasterGraphContext ctx)
        {
            ctx.cmd.DrawRendererList(passData.rendererListHandle);
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            using (var builder = renderGraph.AddRasterRenderPass<PassData>("Fill Render Objects", out var passData))
            {
                UniversalResourceData data = frameData.Get<UniversalResourceData>();
                UniversalCameraData camSrc = frameData.Get<UniversalCameraData>();
                UniversalRenderingData renderData = frameData.Get<UniversalRenderingData>();
                
                var rlDesc = new RendererListDesc(_shaderTags, renderData.cullResults, camSrc.camera)
                {
                    sortingCriteria = SortingCriteria.CommonOpaque,
                    renderQueueRange = RenderQueueRange.all,
                    layerMask = _layerMask,
                    overrideMaterial = _objectMask,
                    overrideMaterialPassIndex = 0
                };
                
                passData.rendererListHandle = renderGraph.CreateRendererList(rlDesc);
                
                var desc = camSrc.cameraTargetDescriptor;
                desc.msaaSamples = 1;
                desc.depthBufferBits = 0;
                passData.textureHandle = data.activeColorTexture;
                TextureHandle destination = UniversalRenderer.CreateRenderGraphTexture(renderGraph, desc, "CopyTexture", false);
                
                TransferTextureData transferData = frameData.Create<TransferTextureData>();
                transferData.textureHandle = destination;
                transferData.originalTextureHandle = destination;
                
                builder.UseTexture(passData.textureHandle, AccessFlags.Read);
                builder.SetRenderAttachment(destination, 0);
                builder.AllowPassCulling(false);
                builder.UseRendererList(passData.rendererListHandle);
                builder.SetRenderFunc((PassData pData, RasterGraphContext ctx) =>
                {
                    ExecutePass(pData, ctx);
                });
            }
        }
    }
    
    public class BlurRenderPass : ScriptableRenderPass
    {
        public class PassData
        {
            public TextureHandle textureToUse;
        }
        
        private Material _blurMaterial;
        private TextureDesc _descriptor;
        private int _passCount;

        public BlurRenderPass(Material blurMaterial, Settings settings)
        {
            _blurMaterial = blurMaterial;
            _passCount = settings.passCount;
        }
        
        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var source = frameData.Get<CopyToTempPass.TransferTextureData>().textureHandle;
            UniversalCameraData camSrc = frameData.Get<UniversalCameraData>();
            var desc = camSrc.cameraTargetDescriptor;
            desc.msaaSamples = 1;
            desc.depthBufferBits = 0;

            int passes = _passCount > 0 ? _passCount : 1;

            TextureHandle tmp1 = UniversalRenderer.CreateRenderGraphTexture(renderGraph, desc, "BlurTmp1", false);
            TextureHandle tmp2 = UniversalRenderer.CreateRenderGraphTexture(renderGraph, desc, "BlurTmp2", false);

            TextureHandle input = source;
            TextureHandle output;

            for (int i = 0; i < passes; i++)
            {
                output = (i % 2 == 0) ? tmp1 : tmp2;

                using (var builder = renderGraph.AddRasterRenderPass<PassData>("Blur Pass " + i, out var passData))
                {
                    passData.textureToUse = input;

                    builder.UseTexture(passData.textureToUse, AccessFlags.Read);
                    builder.SetRenderAttachment(output, 0);
                    builder.AllowPassCulling(false);

                    builder.SetRenderFunc((PassData pData, RasterGraphContext ctx) =>
                    {
                        Blitter.BlitTexture(ctx.cmd, pData.textureToUse, new Vector4(1, 1, 0, 0), _blurMaterial, 0);
                    });
                }

                input = output; // на следующем проходе берем выход предыдущего
            }

            frameData.Get<CopyToTempPass.TransferTextureData>().textureHandle = input;
        }
    }

    public class OutlineRenderPass : ScriptableRenderPass
    {
        public class PassData
        {
            public TextureHandle textureHandle;
            public TextureHandle activeColor;
        }

        private Material _substractMaterial;

        public OutlineRenderPass(Material substractMaterial)
        {
            _substractMaterial = substractMaterial;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            using (var builder = renderGraph.AddRasterRenderPass<PassData>("Outline Pass", out var passData))
            {
                UniversalResourceData data = frameData.Get<UniversalResourceData>();
                UniversalCameraData camSrc = frameData.Get<UniversalCameraData>();
                var desc = camSrc.cameraTargetDescriptor;
                desc.msaaSamples = 1;
                desc.depthBufferBits = 0;
                
                var blurTexture = frameData.Get<CopyToTempPass.TransferTextureData>().textureHandle;
                var originTexture = frameData.Get<CopyToTempPass.TransferTextureData>().originalTextureHandle;
                
                var destinationTexture = UniversalRenderer.CreateRenderGraphTexture(renderGraph, desc, "Outline2", false);
                
                passData.activeColor = data.activeColorTexture;
                
                builder.UseTexture(blurTexture, AccessFlags.Read);
                builder.UseTexture(originTexture, AccessFlags.Read);
                builder.UseTexture(passData.activeColor, AccessFlags.Read);
                builder.SetRenderAttachment(destinationTexture, 0);
                builder.AllowPassCulling(false);
                
                builder.SetRenderFunc((OutlineRenderPass.PassData pData, RasterGraphContext ctx) =>
                {
                    var mpb = new MaterialPropertyBlock();
                    mpb.SetTexture("_BluredTexture", blurTexture);
                    mpb.SetTexture("_OriginTexture", originTexture);
                    mpb.SetTexture("_ActiveColor", pData.activeColor);

                    CoreUtils.DrawFullScreen(ctx.cmd, _substractMaterial, mpb, 0);
                });
                
                frameData.Get<CopyToTempPass.TransferTextureData>().result = destinationTexture;
            }
        }
    }
    
    public class OutlineViewRenderPass : ScriptableRenderPass
    {
        public class PassData
        {
            public TextureHandle textureHandle;
        }
            
        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            using (var builder = renderGraph.AddRasterRenderPass<PassData>("View Outline Pass", out var passData))
            {
                UniversalResourceData data = frameData.Get<UniversalResourceData>();
                passData.textureHandle = frameData.Get<CopyToTempPass.TransferTextureData>().result;
                
                builder.UseTexture(passData.textureHandle, AccessFlags.Read);
                builder.SetRenderAttachment(data.activeColorTexture, 0);
                builder.AllowPassCulling(false);
                builder.SetRenderFunc((PassData pData, RasterGraphContext ctx) =>
                {
                    Blitter.BlitTexture(ctx.cmd, pData.textureHandle, new Vector4(1, 1, 0, 0), 0, false);
                });
            }
        }
    }
    
    public override void Create()
    {
        var blurShader = Shader.Find("Shader Graphs/BlurShaderGraph");
        var objectMask = Shader.Find("Shader Graphs/ObjectMask");
        var subtractShader = Shader.Find("Shader Graphs/SubtractTextures");
        
        _objectMaskMaterial = new Material(objectMask);
        _blurMaterial =  new Material(blurShader);
        _subtractMaterial = new Material(subtractShader);
        
        _copyToTempPass = new(_settings.layerMask, _objectMaskMaterial);
        _blurRenderPass = new BlurRenderPass(_blurMaterial,  _settings);
        _outlineRenderPass = new(_subtractMaterial);
        _outlineViewRenderPass = new();

        _copyToTempPass.renderPassEvent = passEvent;
        _blurRenderPass.renderPassEvent = passEvent + 1;
        _outlineRenderPass.renderPassEvent = passEvent + 2 + _settings.passCount;
        _outlineViewRenderPass.renderPassEvent = passEvent + 3 + _settings.passCount;
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (_settings.passCount >= 1)
        {
            renderer.EnqueuePass(_copyToTempPass);
            renderer.EnqueuePass(_blurRenderPass);
            renderer.EnqueuePass(_outlineRenderPass);
            renderer.EnqueuePass(_outlineViewRenderPass);
        }
    }
    protected override void Dispose(bool disposing)
    {
        if (Application.isPlaying)
        {
            Destroy(_objectMaskMaterial);
            Destroy(_blurMaterial);
            Destroy(_subtractMaterial);
        }
        else
        {
            DestroyImmediate(_objectMaskMaterial);
            DestroyImmediate(_blurMaterial);
            DestroyImmediate(_subtractMaterial);
        }
    }
}
