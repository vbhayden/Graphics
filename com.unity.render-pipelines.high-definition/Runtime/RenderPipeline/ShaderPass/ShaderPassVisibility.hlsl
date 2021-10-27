#ifndef VISIBILITY_PASS_HLSL
#define VISIBILITY_PASS_HLSL


#include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/ShaderPass/VaryingMesh.hlsl"

void ApplyVertexModification(AttributesMesh input, float3 normalWS, inout float3 positionRWS, float3 timeParameters)
{
}

#include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/ShaderPass/VertMesh.hlsl"

struct GeoPoolInput
{
    uint vertId : SV_VertexID;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct VisibilityVtoP
{
    float4 pos : SV_Position;
    UNITY_VERTEX_OUTPUT_STEREO
};

VisibilityVtoP Vert(AttributesMesh inputMesh)
{
    VisibilityVtoP v2p;

    #ifdef VISIBILITY_USE_ORIGINAL_MESH

    VaryingsMeshToPS vmesh = VertMesh(inputMesh);
    v2p.pos = vmesh.positionCS;

    #else

    UNITY_SETUP_INSTANCE_ID(input);

    float2 coord = float2((input.vertId & 1) ? -0.2 : 0.2, (input.vertId & 2) ? 0.2 : -0.2);
    v2p.pos = float4(coord.x, coord.y, 0, 1);

    #endif

    return v2p;
}

void Frag(VisibilityVtoP packedInput, out float4 outVisibility : SV_Target0)
{
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(packedInput);
    #ifdef DOTS_INSTANCING_ON
        #ifdef SOMETHING
        outVisibility = float4(1, 1, 1, 0);
        #else
        outVisibility = float4(0, 0, 1, 0);
        #endif
    #else
        outVisibility = float4(1, 0, 0, 0);
    #endif
}

#endif
