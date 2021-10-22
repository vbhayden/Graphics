Shader "Hidden/Universal Render Pipeline/FSR"
{
    HLSLINCLUDE
        #pragma exclude_renderers gles
        #pragma multi_compile_local_fragment _ _FXAA

        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Filtering.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/Shaders/PostProcessing/Common.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Debug/DebuggingFullscreen.hlsl"

        TEXTURE2D_X(_SourceTex);
        float4 _SourceSize;

        #define FSR_INPUT_TEXTURE _SourceTex
        #define FSR_INPUT_SAMPLER sampler_LinearClamp
        #include "Packages/com.unity.render-pipelines.core/Runtime/PostProcessing/Shaders/FSRCommon.hlsl"

        half4 FragSetup(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

            float2 uv = UnityStereoTransformScreenSpaceTex(input.uv);
            float2 positionNDC = uv;
            int2   positionSS = uv * _SourceSize.xy;

            half3 color = SAMPLE_TEXTURE2D_X(_SourceTex, sampler_PointClamp, uv).xyz;

            #if _FXAA
            {
                color = ApplyFXAA(color, positionNDC, positionSS, _SourceSize, _SourceTex);
            }
            #endif

            // EASU expects the input image to be in gamma 2.0 color space so perform color space conversion
            // while we store the pixel data from the setup pass.
            color = LinearToGamma20(color);

            half4 finalColor = half4(color, 1);

            #if defined(DEBUG_DISPLAY)
            half4 debugColor = 0;

            if(CanDebugOverrideOutputColor(finalColor, uv, debugColor))
            {
                return debugColor;
            }
            #endif

            return finalColor;
        }

        half4 FragEASU(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

            float2 uv = UnityStereoTransformScreenSpaceTex(input.uv);
            uint2 integerUv = uv * _ScreenParams.xy;

            half3 color = ApplyEASU(integerUv);

            // Convert back to linear color space before this data is sent into RCAS
            color = Gamma20ToLinear(color);

            return half4(color, 1.0);
        }

    ENDHLSL

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline"}
        LOD 100
        ZTest Always ZWrite Off Cull Off

        Pass
        {
            Name "Setup"

            HLSLPROGRAM
                #pragma vertex FullscreenVert
                #pragma fragment FragSetup
                #pragma target 4.5
            ENDHLSL
        }

        Pass
        {
            Name "EASU"

            HLSLPROGRAM
                #pragma vertex FullscreenVert
                #pragma fragment FragEASU
                #pragma target 4.5
            ENDHLSL
        }
    }
}
