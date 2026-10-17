Shader "Menger/Surface"
{
    // Every value below is overwritten each frame from MengerSettings by WorldRenderer; the defaults only matter outside Play mode.
    Properties
    {
        _Ambient ("Ambient", Range(0, 1)) = 0.3
        _Key ("Sun light", Range(0, 2)) = 0.65
        _Fill ("Fill light", Range(0, 1)) = 0.25
        _HueBase ("Hue at level 0", Range(0, 1)) = 0.58
        _HueStep ("Hue step per level", Range(-0.3, 0.3)) = 0.045
        _Sat ("Saturation", Range(0, 1)) = 0.28
        _Val ("Brightness", Range(0, 1)) = 0.82
        _DetailTex ("Detail (R albedo, G roughness)", 2D) = "grey" {}
        _DetailTile ("Detail tile (logic units)", Float) = 33.3
        _DetailStrength ("Detail strength", Range(0, 1)) = 0.3
        _RoughBase ("Roughness", Range(0, 1)) = 0.6
        _RoughVar ("Roughness variation", Range(0, 1)) = 0.5
        _SpecStrength ("Highlight strength", Range(0, 2)) = 0.35
        _Reflect ("Sky reflection", Range(0, 2)) = 0.8
        _Phase ("Zoom phase (set per mesh)", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_DetailTex);
            SAMPLER(sampler_DetailTex);

            CBUFFER_START(UnityPerMaterial)
                half _Ambient, _Key, _Fill;
                half _HueBase, _HueStep, _Sat, _Val;
                float _DetailTile;
                half _DetailStrength, _RoughBase, _RoughVar, _SpecStrength, _Reflect;
                float _Phase;
            CBUFFER_END

            // vertex: position and colour only (16 bytes). colour.r = recursion level * 16, colour.g = face direction 0..5 (+x -x +y -y +z -z)
            struct Attributes { float4 positionOS : POSITION; half4 color : COLOR; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half3 normalWS : TEXCOORD0;
                half4 color : TEXCOORD1;
                half fog : TEXCOORD2;
                float3 positionWS : TEXCOORD3;
                float3 positionOS : TEXCOORD4;
                half3 normalOS : TEXCOORD5;
            };

            float3 FaceNormal(half g)
            {
                int d = (int)round(g * 255.0);
                float s = (d & 1) == 0 ? 1.0 : -1.0;
                int axis = d >> 1;
                return float3(axis == 0 ? s : 0.0, axis == 1 ? s : 0.0, axis == 2 ? s : 0.0);
            }

            Varyings vert(Attributes v)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                float3 normalOS = FaceNormal(v.color.g);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.positionOS = v.positionOS.xyz;   // the frame's logic coordinates: the detail pattern lives in them
                o.normalOS = normalOS;
                o.normalWS = TransformObjectToWorldNormal(normalOS);
                o.color = v.color;
                o.fog = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            half3 HsvToRgb(half3 c)
            {
                half4 K = half4(1.0, 2.0 / 3.0, 1.0 / 3.0, 3.0);
                half3 p = abs(frac(c.xxx + K.xyz) * 6.0 - K.www);
                return c.z * lerp(K.xxx, saturate(p - K.xxx), c.y);
            }

            half4 frag(Varyings i) : SV_Target
            {
                half3 n = normalize(i.normalWS);

                // base colour: the palette picks a hue by the cube's recursion level (stored in the vertex colour's red, 16 per level)
                half level = round(i.color.r * 255.0 / 16.0);
                half3 albedo = HsvToRgb(half3(frac(_HueBase + _HueStep * level), _Sat, _Val));

                // detail: faces are axis aligned, so a plain planar projection along the dominant axis is enough. Two octaves, x3 apart,
                // are cross-faded by the zoom phase (= continuous depth minus this mesh's depth): across a frame flip the logic
                // coordinates grow x3 and the phase drops by 1, which selects exactly the same texels again.
                float3 an = abs(i.normalOS);
                float2 uv = an.x > 0.5 ? i.positionOS.zy : (an.y > 0.5 ? i.positionOS.xz : i.positionOS.xy);
                uv /= _DetailTile;
                float k = floor(_Phase);
                float sa = exp2(k * 1.5849625);
                half2 d = lerp(SAMPLE_TEXTURE2D(_DetailTex, sampler_DetailTex, uv * sa).rg,
                               SAMPLE_TEXTURE2D(_DetailTex, sampler_DetailTex, uv * sa * 3.0).rg, _Phase - k);
                albedo *= 1.0 + _DetailStrength * (d.r * 2.0 - 1.0);
                half rough = saturate(_RoughBase + _RoughVar * (d.g * 2.0 - 1.0));

                // the scene's directional light, with its shadow; the fill light is a fixed, unshadowed bounce from the opposite side
                Light sun = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                half ndl = saturate(dot(n, sun.direction));
                half key = ndl * sun.shadowAttenuation;
                half fill = saturate(dot(n, -sun.direction * half3(0.7, 0.4, 1.0)));
                half3 col = albedo * (_Ambient + _Key * key + _Fill * fill);

                // highlight: smooth surfaces get a tight bright one, rough surfaces a wide faint one
                half3 viewDir = normalize(GetWorldSpaceViewDir(i.positionWS));
                half3 h = normalize(sun.direction + viewDir);
                half shininess = lerp(256.0, 6.0, rough);
                col += pow(saturate(dot(n, h)), shininess) * (1.0 - rough) * _SpecStrength * key;

                // mirror: the sky (the skybox reflection probe) seen in the face, stronger at grazing angles (Schlick), blurred by roughness
                half ndv = saturate(dot(n, viewDir));
                half fresnel = 0.06 + 0.94 * pow(1.0 - ndv, 5.0);
                half3 sky = GlossyEnvironmentReflection(reflect(-viewDir, n), rough, 1.0);
                col += sky * fresnel * _Reflect * (1.0 - rough * 0.6);

                col = MixFog(col, i.fog);
                return half4(col, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes { float4 positionOS : POSITION; half4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; };

            float3 FaceNormal(half g)
            {
                int d = (int)round(g * 255.0);
                float s = (d & 1) == 0 ? 1.0 : -1.0;
                int axis = d >> 1;
                return float3(axis == 0 ? s : 0.0, axis == 1 ? s : 0.0, axis == 2 ? s : 0.0);
            }

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 positionWS = TransformObjectToWorld(v.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(FaceNormal(v.color.g));
            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDirectionWS = normalize(_LightPosition - positionWS);
            #else
                float3 lightDirectionWS = _LightDirection;
            #endif
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
            #if UNITY_REVERSED_Z
                positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE * positionCS.w);
            #else
                positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE * positionCS.w);
            #endif
                o.positionCS = positionCS;
                return o;
            }

            half4 frag(Varyings i) : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; };

            Varyings vert(Attributes v) { Varyings o; o.positionCS = TransformObjectToHClip(v.positionOS.xyz); return o; }
            half frag(Varyings i) : SV_Target { return i.positionCS.z; }
            ENDHLSL
        }
    }
}
