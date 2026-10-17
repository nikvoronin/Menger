Shader "Menger/Glow"
{
    Properties
    {
        _Glow ("Glow", Float) = 2.2
        _MinPixels ("Minimum line width (pixels)", Float) = 2.5
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" }
        Pass
        {
            Name "Glow"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half _Glow;
                half _MinPixels;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 thickness : TEXCOORD0; half4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; half4 color : TEXCOORD0; half fog : TEXCOORD1; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                // keep the outline at least _MinPixels wide on screen, so it stays visible from far away (long draw distance)
                float objScale = length(unity_ObjectToWorld._m00_m10_m20);
                float depth = TransformObjectToHClip(v.positionOS.xyz).w;
                float worldPerPixel = 2.0 * depth / (_ScreenParams.y * unity_CameraProjection._m11);
                float extra = max(0.0, _MinPixels * worldPerPixel - v.thickness.x * objScale) * 0.5 / objScale;
                float3 posOS = v.positionOS.xyz + v.normalOS * extra;
                VertexPositionInputs p = GetVertexPositionInputs(posOS);
                o.positionCS = p.positionCS;
                o.color = v.color;
                o.fog = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                // vertex alpha carries the per-entrance intensity (selection / fade); additive blend fades to black with fog
                half3 col = i.color.rgb * (i.color.a * _Glow);
                col = MixFogColor(col, half3(0, 0, 0), i.fog);
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
}
