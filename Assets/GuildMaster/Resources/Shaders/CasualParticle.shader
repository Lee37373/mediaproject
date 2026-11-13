// Particle shader for the Cartoon Casual VFX Pack under the project's URP 2D renderer (2026-10-30).
// The pack ships Built-in "Particles/Additive" and "Particles/Alpha Blended" materials, which URP cannot draw, and
// URP's own Particles shaders have no pass the 2D renderer draws. This is the legacy formula
// (2 x vertex colour x tint x texture) in a pass with no LightMode tag, so both renderers draw it.
// "_add" materials are soft glows made for a dark background; added onto the game's light stone and grass they vanish.
// So the added light is painted instead: its hue at full brightness, with opacity from its brightness (x _Boost),
// premultiplied (Blend One OneMinusSrcAlpha). Other materials are alpha blended as before.
Shader "GuildMaster/CasualParticle"
{
    Properties
    {
        _MainTex ("Particle Texture", 2D) = "white" {}
        _TintColor ("Tint Color", Color) = (0.5, 0.5, 0.5, 0.5)
        [HideInInspector] _SrcBlend ("Src Blend", Float) = 5
        [HideInInspector] _DstBlend ("Dst Blend", Float) = 10
        [HideInInspector] _Additive ("Additive", Float) = 0
        _Boost ("Glow Opacity Boost", Float) = 3.4
        _AlphaBoost ("Alpha Boost (blended materials)", Float) = 1.35
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "IgnoreProjector" = "True" "RenderType" = "Transparent" "PreviewType" = "Plane" "RenderPipeline" = "UniversalPipeline" }
        Blend [_SrcBlend] [_DstBlend]
        ColorMask RGB
        Cull Off
        ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _TintColor;
                half _Additive;
                half _Boost;
                half _AlphaBoost;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.color = v.color;
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half4 c = 2.0h * i.color * _TintColor * SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                if (_Additive > 0.5h)
                {
                    half3 light = saturate(c.rgb * c.a);
                    half peak = max(light.r, max(light.g, light.b));
                    half a = saturate(peak * _Boost);
                    return half4(light / max(peak, 0.001h) * a, a);
                }
                // 2026-11-13: blended materials a little more solid, so they read over grass and stone.
                c.a = saturate(c.a * _AlphaBoost);
                return c;
            }
            ENDHLSL
        }
    }
}
