// Draws SkiaUnityRenderTarget.Texture, whose pixels Skia writes with premultiplied alpha. Unity's
// default blending (SrcAlpha, OneMinusSrcAlpha) would multiply color by alpha a second time and
// darken every edge and translucent area. Lives in Resources so builds always include it; see
// SkiaUnityRenderTarget.PremultipliedMaterial.
Shader "SkiaGameRendering/Premultiplied"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
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
            fixed4 _Color;

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

            fixed4 frag(v2f i) : SV_Target
            {
                // The tint's alpha has to scale color too, or the result stops being premultiplied.
                return tex2D(_MainTex, i.uv) * fixed4(_Color.rgb * _Color.a, _Color.a);
            }
            ENDCG
        }
    }
}
