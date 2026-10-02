// Draws SkiaUnityRenderTarget.Texture, whose pixels Skia writes with premultiplied alpha. Unity's
// default blending (SrcAlpha, OneMinusSrcAlpha) would multiply color by alpha a second time and
// darken every edge and translucent area. Lives in Resources so builds always include it; see
// SkiaUnityRenderTarget.PremultipliedMaterial.
//
// Skia's bytes are sRGB-encoded, but the texture is UNorm (ANGLE renders into it), so in a Linear
// project nothing decodes them on sampling. This shader does, the way an sRGB texture holding the
// straight colors would: unpremultiply, decode, premultiply again. Decoding the premultiplied values
// directly, as an sRGB texture format would, darkens everything translucent. IMGUI is the exception:
// it writes gamma values even in a Linear project, so _GammaOutput (PremultipliedGuiMaterial) skips
// the decode there.
Shader "SkiaGameRendering/Premultiplied"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
        [HideInInspector] _GammaOutput ("Output is gamma (IMGUI)", Float) = 0
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" "PreviewType" = "Plane" }
        Blend One OneMinusSrcAlpha
        ZWrite Off
        Cull Off
        Lighting Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _Color;
            float _GammaOutput;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float4 color = tex2D(_MainTex, i.uv);
                float4 tint = _Color;
            #ifndef UNITY_COLORSPACE_GAMMA
                if (_GammaOutput > 0.5)
                {
                    // Unity hands material colors over in linear; this output is gamma.
                    tint.rgb = float3(LinearToGammaSpaceExact(tint.r), LinearToGammaSpaceExact(tint.g), LinearToGammaSpaceExact(tint.b));
                }
                else if (color.a > 0)
                {
                    float3 straight = saturate(color.rgb / color.a);
                    color.rgb = float3(GammaToLinearSpaceExact(straight.r), GammaToLinearSpaceExact(straight.g), GammaToLinearSpaceExact(straight.b)) * color.a;
                }
            #endif
                // The tint's alpha has to scale color too, or the result stops being premultiplied.
                return color * float4(tint.rgb * tint.a, tint.a);
            }
            ENDCG
        }
    }
}
