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

VisibilityVtoP Vert(GeoPoolInput input)
{
    UNITY_SETUP_INSTANCE_ID(input);

    VisibilityVtoP v2p;
    float2 coord = float2((input.vertId & 1) ? -0.2 : 0.2, (input.vertId & 2) ? 0.2 : -0.2);
    v2p.pos = float4(coord.x, coord.y, 0, 1);
    return v2p;
}

void Frag(VisibilityVtoP packedInput, out float4 outVisibility : SV_Target0)
{
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(packedInput);
    #ifdef DOTS_INSTANCING_ON
        outVisibility = float4(0, 0, 1, 0);
    #else
        outVisibility = float4(1, 0, 0, 0);
    #endif
}
