using System.Collections.Generic;
using UnityEngine.VFX;
using System;
using System.Diagnostics;
using System.Linq;
using UnityEngine.Experimental.GlobalIllumination;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Experimental.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RendererUtils;
using UnityEngine;
using UnityEngine.Rendering;

namespace UnityEngine.Rendering
{

// Then split full VB/IB in clusters and change accordingly.
public class GeometryPrimitivePool
{
#region Shader Parameter indices
    int _UVCompactionParams;
    int _UV1CompactionParams;
    int _NormalCompactionParams;
    int _PosCompactionParams;
    int _TangentCompactionParams;
    int _IBCompactionParams;
    int _InputUVVB;
    int _InputUV1VB;
    int _InputNormalVB;
    int _InputPosVB;
    int _InputTangentVB;
    int _InputIB;
    int _OutputVB;
    int _OutputIB;

    void InitParamIndices()
    {
        _UVCompactionParams = Shader.PropertyToID("_UVCompactionParams");
        _UV1CompactionParams = Shader.PropertyToID("_UV1CompactionParams");
        _NormalCompactionParams = Shader.PropertyToID("_NormalCompactionParams");
        _PosCompactionParams = Shader.PropertyToID("_PosCompactionParams");
        _TangentCompactionParams = Shader.PropertyToID("_TangentCompactionParams");
        _IBCompactionParams = Shader.PropertyToID("_IBCompactionParams");
        _InputUVVB = Shader.PropertyToID("_InputUVVB");
        _InputUV1VB = Shader.PropertyToID("_InputUV1VB");
        _InputNormalVB = Shader.PropertyToID("_InputNormalVB");
        _InputPosVB = Shader.PropertyToID("_InputPosVB");
        _InputTangentVB = Shader.PropertyToID("_InputTangentVB");
        _InputIB = Shader.PropertyToID("_InputIB");
        _OutputVB = Shader.PropertyToID("_OutputVB");
        _OutputIB = Shader.PropertyToID("_OutputIB");
    }
#endregion


    private static float Asfloat(uint val) { unsafe { return *((float*)&val); } }

    ComputeShader m_VertexBufferCompactionCS = null;

    public ComputeBuffer CompactedVB = null;
    public ComputeBuffer CompactedIB = null;
    public ComputeBuffer InstanceVDataB = null;



    private GraphicsBuffer m_tmpIndexBuffer = null;

    public uint clusterBackCount = 0;
    public uint clusterFrontCount = 0;
    public uint clusterDoubleCount = 0;

    struct MaterialData
    {
        public int numRenderers;
        public int globalMaterialID;
        public int bucketID;
    }

    Dictionary<Material, MaterialData> materials = new Dictionary<Material, MaterialData>();

    public GeometryPrimitivePool()
    {
        InitParamIndices();
        InitShaders();
        InitVBuffer();
    }

    public void Dispose()
    {
        DisposeVBufferStuff();

        if (m_tmpIndexBuffer != null)
            m_tmpIndexBuffer.Dispose();
    }

    void InitShaders()
    {
        m_VertexBufferCompactionCS = (ComputeShader)Resources.Load("VisibilityBufferCompactionCS");
    }

    void InitVBuffer()
    {
        CompactedVB = null;
        CompactedIB = null;
        InstanceVDataB = null;
    }

    void DisposeVBufferStuff()
    {
        CoreUtils.SafeRelease(CompactedIB);
        CoreUtils.SafeRelease(CompactedVB);
        CoreUtils.SafeRelease(InstanceVDataB);
    }

    int GetFormatByteCount(VertexAttributeFormat format)
    {
        switch (format)
        {
            case VertexAttributeFormat.Float32: return 4;
            case VertexAttributeFormat.Float16: return 2;
            case VertexAttributeFormat.UNorm8: return 1;
            case VertexAttributeFormat.SNorm8: return 1;
            case VertexAttributeFormat.UNorm16: return 2;
            case VertexAttributeFormat.SNorm16: return 2;
            case VertexAttributeFormat.UInt8: return 1;
            case VertexAttributeFormat.SInt8: return 1;
            case VertexAttributeFormat.UInt16: return 2;
            case VertexAttributeFormat.SInt16: return 2;
            case VertexAttributeFormat.UInt32: return 4;
            case VertexAttributeFormat.SInt32: return 4;
        }
        return 4;
    }

    GraphicsBuffer ExtractMeshIndexBuffer(Mesh mesh, out IndexFormat indexFormat)
    {
        var idxBuffer = mesh.GetIndexBuffer();
        if ((idxBuffer.target & GraphicsBuffer.Target.Raw) != 0)
        {
            indexFormat = mesh.indexFormat;
            return idxBuffer;
        }
        else
        {
            idxBuffer.Dispose();
            idxBuffer = null;
        }

        //TODO: internal unity bug causes sometimes us to be missing indices. Thus this is a workaround to copy them temporarily.        
        //We construct a quick 32 bit index buffer.
        indexFormat = IndexFormat.UInt32;
        int indicesCount = 0;
        for (int submeshId = 0; submeshId < mesh.subMeshCount; ++submeshId)
            indicesCount += (int)mesh.GetIndexCount(submeshId);

        if (m_tmpIndexBuffer == null || m_tmpIndexBuffer.count < indicesCount)
        {
            if (m_tmpIndexBuffer != null)
            {
                m_tmpIndexBuffer.Release();
                m_tmpIndexBuffer = null;
            }

            m_tmpIndexBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Raw, indicesCount, 4);
        }

        int indexOffset = 0;
        for (int submeshId = 0; submeshId < mesh.subMeshCount; ++submeshId)
        {
            int submeshCount = (int)mesh.GetIndexCount(submeshId);
            m_tmpIndexBuffer.SetData(mesh.GetIndices(submeshId), 0, indexOffset, submeshCount);
            indexOffset += submeshCount;
        }

        return m_tmpIndexBuffer;
    }

    int DivideMeshInClusters(Mesh mesh, MeshRenderer renderer, ref Dictionary<Mesh, uint> meshes, ref List<VertexBufferCompaction.InstanceVData> instancesBack, ref List<VertexBufferCompaction.InstanceVData> instancesFront, ref List<VertexBufferCompaction.InstanceVData> instancesDouble)
    {
        int clusterCount = 0;
        for (int matIndex = 0; matIndex < renderer.sharedMaterials.Length; ++matIndex)
        {
            int subMeshIndex = (matIndex + renderer.subMeshStartIndex) % mesh.subMeshCount;
            uint subMeshIndexSize = mesh.GetIndexCount(subMeshIndex);
            //int clustersForSubmesh = HDUtils.DivRoundUp((int)subMeshIndexSize, VisibilityBufferConstants.s_ClusterSizeInIndices);
            int clustersForSubmesh = ((int)subMeshIndexSize + VertexBufferCompaction.VisibilityBufferConstants.s_ClusterSizeInIndices - 1)/VertexBufferCompaction.VisibilityBufferConstants.s_ClusterSizeInIndices;

            Material currentMat = renderer.sharedMaterials[matIndex];
            if (currentMat == null)
                continue;

            // TODO: transparent materials exclusion from HDRP
            //if (IsTransparentMaterial(currentMat) || IsAlphaTestedMaterial(currentMat) || currentMat.shader.name != "HDRP/Lit")
            //    clusterCount += clustersForSubmesh;
            //else
            {
                bool doubleSided = currentMat.doubleSidedGI || currentMat.IsKeywordEnabled("_DOUBLESIDED_ON");

                float cullMode = 2.0f;
                if (currentMat.HasProperty("_CullMode"))
                    cullMode = currentMat.GetFloat("_CullMode");

                MaterialData materialData = new MaterialData();
                materials.TryGetValue(currentMat, out materialData);
                uint materialID = ((uint)materialData.globalMaterialID) & 0xffff;
                uint bucketID = ((uint)materialData.bucketID) & 0xffff;

                // Instance data common to all clusters
                VertexBufferCompaction.InstanceVData data;
                data.materialData = materialID | (bucketID << 16);
                data.localToWorld = renderer.localToWorldMatrix;
                data.lightmapST = renderer.lightmapScaleOffset;

                for (int c = 0; c < clustersForSubmesh; ++c)
                {
                    // Adjust the chunk start index
                    data.chunkStartIndex = meshes[mesh] + (uint)clusterCount;

                    if (doubleSided)
                        instancesDouble.Add(data);
                    else if (cullMode == 2.0f)
                        instancesBack.Add(data);
                    else
                        instancesFront.Add(data);

                    clusterCount++;
                }
            }
        }

        return clusterCount;
    }

    GraphicsBuffer GetVertexAttribInfo(Mesh mesh, VertexAttribute attribute, out int streamStride, out int attributeOffset, out int attributeBytes)
    {
        if (mesh.HasVertexAttribute(attribute))
        {
            int stream = mesh.GetVertexAttributeStream(attribute);
            streamStride = mesh.GetVertexBufferStride(stream);
            attributeOffset = mesh.GetVertexAttributeOffset(attribute);
            attributeBytes = GetFormatByteCount(mesh.GetVertexAttributeFormat(attribute)) * mesh.GetVertexAttributeDimension(attribute);

            return mesh.GetVertexBuffer(stream);
        }
        else
        {
            streamStride = attributeOffset = attributeBytes = 0;
            return null;
        } 
    }

    void AddMeshToCompactedBuffer(ref uint clusterIndex, ref uint vbStart, Mesh mesh)
    {
        var ib = ExtractMeshIndexBuffer(mesh, out var indexFormat);

        //var cs = defaultResources.shaders.vbCompactionCS;
        var cs = m_VertexBufferCompactionCS;
        var kernel = cs.FindKernel("VBCompactionKernel");
        mesh.indexBufferTarget |= GraphicsBuffer.Target.Raw;
        mesh.vertexBufferTarget |= GraphicsBuffer.Target.Raw;

        int posStreamStride, posOffset, posBytes;
        var posVBStream = GetVertexAttribInfo(mesh, VertexAttribute.Position, out posStreamStride, out posOffset, out posBytes);

        int uvStreamStride, uvOffset, uvBytes;
        var uvVBStream = GetVertexAttribInfo(mesh, VertexAttribute.TexCoord0, out uvStreamStride, out uvOffset, out uvBytes);

        int normalStreamStride, normalOffset, normalBytes;
        var normalVBStream = GetVertexAttribInfo(mesh, VertexAttribute.Normal, out normalStreamStride, out normalOffset, out normalBytes);

        int tangentStreamStride, tangentOffset, tangentBytes;
        var tangentVBStream = GetVertexAttribInfo(mesh, VertexAttribute.Tangent, out tangentStreamStride, out tangentOffset, out tangentBytes);

        Vector4 uv1CompactionParam = Vector4.zero;
        bool hasTexCoord1 = mesh.HasVertexAttribute(VertexAttribute.TexCoord1);
        GraphicsBuffer uv1VBStream = null;

        if (hasTexCoord1)
        {
            int uv1StreamStride, uv1Offset, uv1Bytes;
            uv1VBStream = GetVertexAttribInfo(mesh, VertexAttribute.TexCoord1, out uv1StreamStride, out uv1Offset, out uv1Bytes);
            List<Vector2> blah = new List<Vector2>();
            mesh.GetUVs(1, blah);
            uv1CompactionParam = new Vector4(uv1Offset, mesh.vertexCount, uv1StreamStride, vbStart);
            cs.SetVector(_UV1CompactionParams, uv1CompactionParam);
        }

        Vector4 uvCompactionParam = new Vector4(uvOffset, mesh.vertexCount, uvStreamStride, vbStart);
        Vector4 normalCompactionParam = new Vector4(normalOffset, mesh.vertexCount, normalStreamStride, vbStart);
        Vector4 posCompactionParam = new Vector4(posOffset, mesh.vertexCount, posStreamStride, vbStart);
        Vector4 tangentCompactionParam = new Vector4(tangentOffset, mesh.vertexCount, tangentStreamStride, vbStart);


        cs.SetBuffer(kernel, _InputUVVB, uvVBStream);
        cs.SetBuffer(kernel, _InputNormalVB, normalVBStream);
        cs.SetBuffer(kernel, _InputPosVB, posVBStream);
        if (tangentVBStream != null)
        {
            cs.SetBuffer(kernel, _InputTangentVB, tangentVBStream);
        }
        else
        {
            cs.SetBuffer(kernel, _InputTangentVB, normalVBStream);
            tangentCompactionParam = Vector4.zero;
        }

        if (hasTexCoord1)
            cs.SetBuffer(kernel, _InputUV1VB, uv1VBStream);
        else
            cs.SetBuffer(kernel, _InputUV1VB, uvVBStream);

        cs.SetBuffer(kernel, _OutputVB, CompactedVB);


        cs.SetVector(_UVCompactionParams, uvCompactionParam);
        cs.SetVector(_NormalCompactionParams, normalCompactionParam);
        cs.SetVector(_PosCompactionParams, posCompactionParam);
        cs.SetVector(_TangentCompactionParams, tangentCompactionParam);

        int dispatchSize = (mesh.vertexCount + 63) / 64;

        cs.Dispatch(kernel, dispatchSize, 1, 1);


        if (indexFormat == IndexFormat.UInt16)
            kernel = cs.FindKernel("IBCompactionKernelUINT16");
        else
            kernel = cs.FindKernel("IBCompactionKernelUINT32");

        cs.SetBuffer(kernel, _InputIB, ib);
        cs.SetBuffer(kernel, _OutputIB, CompactedIB);

        for (int i = 0; i < mesh.subMeshCount; ++i)
        {
            uint indexCount = mesh.GetIndexCount(i);
            int clusterCount = ((int)indexCount + VertexBufferCompaction.VisibilityBufferConstants.s_ClusterSizeInIndices - 1) / VertexBufferCompaction.VisibilityBufferConstants.s_ClusterSizeInIndices;

            Vector4 ibCompactionParams = new Vector4(indexCount, Asfloat((uint)(clusterIndex * VertexBufferCompaction.VisibilityBufferConstants.s_ClusterSizeInIndices)), vbStart, mesh.GetIndexStart(i));
            dispatchSize = ((int)indexCount / 3 + VertexBufferCompaction.VisibilityBufferConstants.s_ClusterSizeInTriangles - 1) / VertexBufferCompaction.VisibilityBufferConstants.s_ClusterSizeInTriangles;
            cs.SetVector(_IBCompactionParams, ibCompactionParams);
            cs.Dispatch(kernel, dispatchSize, 1, 1);
            clusterIndex += (uint)clusterCount;
        }

        vbStart += (uint)mesh.vertexCount;
        posVBStream.Dispose();
        uvVBStream.Dispose();
        normalVBStream.Dispose();
        tangentVBStream?.Dispose();
        if (hasTexCoord1)
            uv1VBStream.Dispose();

        if (ib != m_tmpIndexBuffer)
            ib.Dispose();
    }

    int ComputeNumberOfClusters(Mesh currentMesh)
    {
        int numberClusters = 0;
        for (int subMeshIdx = 0; subMeshIdx < currentMesh.subMeshCount; ++subMeshIdx)
        {
            numberClusters += ((int)currentMesh.GetIndexCount(subMeshIdx) + VertexBufferCompaction.VisibilityBufferConstants.s_ClusterSizeInIndices - 1) / VertexBufferCompaction.VisibilityBufferConstants.s_ClusterSizeInIndices;
        }
        return numberClusters;
    }

    public void Build(List<MeshRenderer> renderers, bool ignoreRendererEnableFlag = false)
    {
        int vertexCount = 0;
        int clusterCount = 0;

        var meshes = new Dictionary<Mesh, uint>();
        var meshToMaterial = new Dictionary<Mesh, Material[]>();
        var instanceDataBack = new List<VertexBufferCompaction.InstanceVData>();
        var instanceDataFront = new List<VertexBufferCompaction.InstanceVData>();
        var instanceDataDouble = new List<VertexBufferCompaction.InstanceVData>();
        materials.Clear();
        MaterialPropertyBlock propBlock = new MaterialPropertyBlock();
        int materialIdx = 1;

        int validRenderers = 0;
        // Grab all the renderers from the scene
        var rendererArray = renderers;

        for (var i = 0; i < rendererArray.Count; i++)
        {
            // Fetch the current renderer
            MeshRenderer currentRenderer = rendererArray[i];

            // If it is not active skip it
            if (currentRenderer.enabled == false && !ignoreRendererEnableFlag) continue;

            // Grab the current game object
            GameObject gameObject = currentRenderer.gameObject;

            if (gameObject.TryGetComponent<ReflectionProbe>(out var _)) continue;

            currentRenderer.TryGetComponent(out MeshFilter meshFilter);
            if (meshFilter == null || meshFilter.sharedMesh == null) continue;

            if (!meshFilter.sharedMesh.isReadable)
            {
                UnityEngine.Debug.LogWarning("Cannot import mesh \"" + meshFilter.mesh.name + "\" Because its readOnly. Mesh must be reimported with Read/Write at import settings.");
                continue;
            }

            uint ibStartQ = 0;
            if (!meshes.TryGetValue(meshFilter.sharedMesh, out ibStartQ))
            {
                meshes.Add(meshFilter.sharedMesh, 0);
                vertexCount += meshFilter.sharedMesh.vertexCount;
                clusterCount += ComputeNumberOfClusters(meshFilter.sharedMesh);
            }

            MaterialData materialData = new MaterialData();
            foreach (var mat in currentRenderer.sharedMaterials)
            {
                if (mat == null) continue;
                if (!materials.TryGetValue(mat, out materialData))
                {
                    mat.enableInstancing = true;
                    materialData.numRenderers = 1;
                    materialData.globalMaterialID = materialIdx & 0xffff;
                    materials.Add(mat, materialData);
                    materialIdx++;
                }
                else
                {
                    materialData.numRenderers += 1;
                    materials[mat] = materialData;
                }
            }
            validRenderers++;
        }

        // If we don't have any valid renderer
        if (validRenderers == 0)
            return;

        // TODO: Worked on the sorted set of materials to optimize the space
        // We need to assign every material to a bucket
        int renderersPerBucket = (validRenderers + 7) / 8;
        int currentBucket = 1;
        int currentBucketRenderers = 0;
        var materialCouple = materials.ToArray();
        foreach (var mat in materialCouple)
        {
            // This goes into the current bucket
            MaterialData newData = mat.Value;
            currentBucketRenderers += newData.numRenderers;
            newData.bucketID = currentBucket;
            materials[mat.Key] = newData;

            if (currentBucketRenderers >= renderersPerBucket)
            {
                currentBucket++;
                currentBucketRenderers = 0;
            }
        }

        int currVBCount = CompactedVB == null ? 0 : CompactedVB.count;
        if (vertexCount != currVBCount)
        {
            if (CompactedVB != null && CompactedIB != null)
            {
                CoreUtils.SafeRelease(CompactedIB);
                CoreUtils.SafeRelease(CompactedVB);
                CompactedVB = null;
                CompactedIB = null;
            }

            var stride = System.Runtime.InteropServices.Marshal.SizeOf<VertexBufferCompaction.CompactVertex>();
            CompactedVB = new ComputeBuffer(vertexCount, stride);
            CompactedIB = new ComputeBuffer(clusterCount * VertexBufferCompaction.VisibilityBufferConstants.s_ClusterSizeInIndices, sizeof(int));
        }

        uint vbStart = 0;
        uint clusterIndex = 0;
        var keyArrays = meshes.Keys.ToArray();
        foreach (var mesh in keyArrays)
        {
            meshes[mesh] = clusterIndex;
            AddMeshToCompactedBuffer(ref clusterIndex, ref vbStart, mesh);
        }

        for (var i = 0; i < rendererArray.Count; i++)
        {
            // Fetch the current renderer
            MeshRenderer currentRenderer = rendererArray[i];

            // If it is not active skip it
            if (currentRenderer.enabled == false && !ignoreRendererEnableFlag) continue;

            // Grab the current game object
            GameObject gameObject = currentRenderer.gameObject;

            if (gameObject.TryGetComponent<ReflectionProbe>(out var _)) continue;

            currentRenderer.TryGetComponent(out MeshFilter meshFilter);
            if (meshFilter == null || meshFilter.sharedMesh == null || !meshFilter.sharedMesh.isReadable) continue;

            DivideMeshInClusters(meshFilter.sharedMesh, currentRenderer, ref meshes, ref instanceDataBack, ref instanceDataFront, ref instanceDataDouble);
        }

        clusterBackCount = (uint)instanceDataBack.Count;
        clusterFrontCount = (uint)instanceDataFront.Count;
        clusterDoubleCount = (uint)instanceDataDouble.Count;

        uint totalInstanceCount = clusterBackCount + clusterFrontCount + clusterDoubleCount;
        if (totalInstanceCount == 0)
            return;

        if (InstanceVDataB == null || InstanceVDataB.count != totalInstanceCount)
        {
            if (InstanceVDataB != null)
            {
                CoreUtils.SafeRelease(InstanceVDataB);
            }
            InstanceVDataB = new ComputeBuffer((int)totalInstanceCount, System.Runtime.InteropServices.Marshal.SizeOf<VertexBufferCompaction.InstanceVData>());
        }

        InstanceVDataB.SetData(instanceDataBack.ToArray(), 0, 0, instanceDataBack.Count);
        InstanceVDataB.SetData(instanceDataFront.ToArray(), 0, instanceDataBack.Count, instanceDataFront.Count);
        InstanceVDataB.SetData(instanceDataDouble.ToArray(), 0, instanceDataBack.Count + instanceDataFront.Count, instanceDataDouble.Count);
    }

}

}

