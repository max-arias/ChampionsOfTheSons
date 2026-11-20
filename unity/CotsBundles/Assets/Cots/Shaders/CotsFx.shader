// Unlit effect shader for the mod's particles and effect meshes (COS-102). HDRP's own Unlit ignores vertex colour, so
// particle colour-over-life and the mod's runtime tints would not show; this one multiplies texture x vertex colour x
// _TintColor x _Intensity. Drawn in HDRP's SRPDefaultUnlit pass (rendered in the transparent forward pass), written
// pre-exposed like HDRP/Unlit, so effects read the same by day and by night.
//   Blend      One/One = additive glow (alpha folded into colour), SrcAlpha/OneMinusSrcAlpha = alpha blend (smoke,
//              dark cores, telegraph fills)
//   _Fresnel   > 0 brightens edges (domes, shields, the black-hole rim); 0 = flat
//   _Scroll    UV scroll speed (xy) for the main texture: fire columns, portals, beams
//   _Pulse     brightness pulse per second (0 = steady)
//   _FadeEnds  1 = fade out towards both ends of the mesh's V (unscrolled), so columns have no hard rims
Shader "Cots/Fx"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _TintColor ("Tint", Color) = (1, 1, 1, 1)
        _Intensity ("Intensity", Float) = 1
        _Fresnel ("Fresnel power (0 = off)", Float) = 0
        _FresnelInvert ("Fresnel invert (dark core)", Float) = 0
        _Scroll ("UV scroll (xy)", Vector) = (0, 0, 0, 0)
        _Pulse ("Pulse per second", Float) = 0
        _FadeEnds ("Fade the mesh's V ends (columns, beams)", Float) = 0
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src blend", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst blend", Float) = 1
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 0
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("ZTest", Float) = 4
    }
    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" "RenderType" = "Transparent" "Queue" = "Transparent" "IgnoreProjector" = "True" }
        Pass
        {
            Name "CotsFx"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite Off
            ZTest [_ZTest]
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 4.5
            #pragma only_renderers d3d11 d3d12 vulkan
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _TintColor;
                float _Intensity;
                float _Fresnel;
                float _FresnelInvert;
                float4 _Scroll;
                float _Pulse;
                float _FadeEnds;
                float _SrcBlend;
                float _DstBlend;
                float _Cull;
                float _ZTest;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
                float rawV : TEXCOORD3;
            };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                float3 positionWS = TransformObjectToWorld(v.positionOS);   // camera-relative in HDRP
                o.positionCS = TransformWorldToHClip(positionWS);
                o.positionWS = positionWS;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.color = v.color;
                o.rawV = v.uv.y;
                o.uv = v.uv * _MainTex_ST.xy + _MainTex_ST.zw + _Scroll.xy * _Time.y;
                return o;
            }

            float4 Frag(Varyings i) : SV_Target
            {
                float4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                float4 c = tex * i.color * _TintColor;
                if (_Fresnel > 0)
                {
                    float3 v = normalize(-i.positionWS);   // camera at the origin (camera-relative rendering)
                    float f = pow(1 - saturate(abs(dot(normalize(i.normalWS), v))), _Fresnel);
                    f = lerp(f, 1 - f, _FresnelInvert);
                    c.a *= f;
                    c.rgb *= lerp(f, 1, _FresnelInvert);
                }
                if (_FadeEnds > 0) c.a *= smoothstep(0, 0.15, i.rawV) * smoothstep(1, 0.6, i.rawV);
                float pulse = _Pulse > 0 ? 0.75 + 0.25 * sin(_Time.y * _Pulse * 6.2831853) : 1;
                c.rgb *= _Intensity * pulse;
                // Additive (One One) has no alpha: fold it into colour so faded particles fade out.
                if (_DstBlend == 1) c.rgb *= c.a;
                return c;
            }
            ENDHLSL
        }
    }
    Fallback Off
}
