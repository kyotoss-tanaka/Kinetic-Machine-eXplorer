Shader "URP/TransparentLines"
{
    Properties
    {
        [Header(Main)]
        [HDR]_Color ("Color", Color) = (0,0,0,1)
        [Toggle]_UseVertexColor ("Use Vertex Colors", Float) = 0

        [Header(Render)]
        [Toggle]_Offset ("See Through", Float) = 0
        _Alpha ("Alpha", Range(0,1)) = 1

        [Header(Dashes)]
        [Toggle]_Dashes ("Enabled", Float) = 0
        _DashesScale ("Scale", Range(1,10)) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline"="UniversalRenderPipeline"
            "Queue"="Transparent"
            "RenderType"="Transparent"
        }

        Pass
        {
            Name "Lines"
            ZWrite Off
            ZTest Always
            Blend SrcAlpha OneMinusSrcAlpha
            Cull Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // ★ グローバルクリップ平面
            float4 _ClipPlane;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 worldPos   : TEXCOORD0;   // ★ worldPos を渡す
                float3 posOS      : TEXCOORD1;
                float4 color      : COLOR0;
            };

            float4 _Color;
            float _UseVertexColor;
            float _Offset;
            float _Alpha;

            float _Dashes;
            float _DashesScale;

            Varyings vert (Attributes v)
            {
                Varyings o;

                float3 worldPos = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(worldPos);

                // 疑似 See-Through（常に手前）
                if (_Offset > 0.5)
                {
                    o.positionCS.z = o.positionCS.w * 0.0001;
                }

                o.worldPos = worldPos;      // ★
                o.posOS = v.positionOS.xyz;
                o.color = v.color;
                return o;
            }

            bool Checker(float3 p)
            {
                int x = (int)floor(p.x);
                int y = (int)floor(p.y);
                int z = (int)floor(p.z);
                return ((x & 1) ^ (y & 1) ^ (z & 1)) == 1;
            }
            half4 frag (Varyings i) : SV_Target
            {
                // ★ 断面クリップ
                float d = dot(i.worldPos, _ClipPlane.xyz) + _ClipPlane.w;
                clip(-d);

                // ★ すべて無視して完全な黒
                return half4(0, 0, 0, 1);
            }
            ENDHLSL
        }
    }
}
