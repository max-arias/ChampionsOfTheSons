// Gravitational lens for the black hole (COS-106). Drawn on a sphere around the core in HDRP's transparent forward pass, it
// shows the scene behind it (HDRP's colour pyramid, the same copy refraction reads) pulled in towards the centre and swirled,
// so the background bends around the hole. Strongest near the core, nothing at the sphere's rim, so it has no visible edge.
// It does not rely on HDRP's distortion pass, which the game's camera does not draw.
//   _Strength  how far the view is pulled in, as a share of the distance to the centre (0..1)
//   _Swirl     how far it turns around the centre, in radians at the core
//   _Blur      colour-pyramid mip near the core (a slight smear of the bent light)
//   _Spin      turns of the swirl per second
Shader "Cots/Lens"
{
    Properties
    {
        _Strength ("Pull towards the centre", Range(0, 1)) = 0.55
        _Swirl ("Swirl at the core (radians)", Float) = 1.6
        _Blur ("Blur near the core (pyramid mip)", Float) = 1.5
        _Spin ("Swirl turns per second", Float) = 0.05
        _Darken ("Darken near the core", Range(0, 1)) = 0.35
    }
    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" "RenderType" = "Transparent" "Queue" = "Transparent" "IgnoreProjector" = "True" }
        Pass
        {
            Name "CotsLens"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma target 4.5
            #pragma only_renderers d3d11 d3d12 vulkan
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Strength;
                float _Swirl;
                float _Blur;
                float _Spin;
                float _Darken;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 centerWS : TEXCOORD2;
            };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                float3 positionWS = TransformObjectToWorld(v.positionOS);   // camera-relative in HDRP
                o.positionCS = TransformWorldToHClip(positionWS);
                o.positionWS = positionWS;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.centerWS = TransformObjectToWorld(float3(0, 0, 0));
                return o;
            }

            float4 Frag(Varyings i) : SV_Target
            {
                // How central this pixel is on the sphere: 1 facing the camera straight on (the core), 0 at the rim.
                float3 view = normalize(-i.positionWS);   // camera at the origin (camera-relative rendering)
                float core = saturate(dot(normalize(i.normalWS), view));
                float pull = core * core;

                float2 uv = i.positionCS.xy * _ScreenSize.zw;
                float2 center = ComputeNormalizedDeviceCoordinates(i.centerWS, UNITY_MATRIX_VP);
                float2 d = uv - center;

                // Pull towards the centre, then turn around it: light bent around the hole.
                d *= 1 - _Strength * pull;
                float a = _Swirl * pull + _Time.y * _Spin * 6.2831853 * pull;
                float s = sin(a), c = cos(a);
                d = float2(d.x * c - d.y * s, d.x * s + d.y * c);
                float2 bent = saturate(center + d);

                float3 col = SampleCameraColor(bent, _Blur * pull);
                col *= 1 - _Darken * pull * pull;
                // Fade to nothing at the rim so the sphere never shows an edge.
                float alpha = smoothstep(0.0, 0.45, core);
                return float4(col, alpha);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
