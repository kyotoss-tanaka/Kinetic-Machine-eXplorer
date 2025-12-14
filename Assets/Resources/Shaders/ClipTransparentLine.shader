Shader "Custom/TransparentLines"
{
    Properties
    {
        [HDR]_Color ("Color", Color) = (0,0,0,0.5)
        [Toggle]_UseVertexColor ("Use Vertex Colors", Float) = 0
        [Toggle]_Offset ("See Through", Float) = 0
        _Alpha ("Alpha", Range(0,1)) = 1
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
            Tags { "LightMode"="UniversalForward" }   // š •K{
            ZWrite Off
            ZTest LEqual
            Blend SrcAlpha OneMinusSrcAlpha
            Cull Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 posOS : TEXCOORD0;
                float4 color : COLOR0;
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

                if (_Offset > 0.5)
                    o.positionCS.z = o.positionCS.w * 0.0001;

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
                if (_Dashes > 0.5)
                {
                    float scale = 1000.0 / (_DashesScale + 0.001);
                    if (Checker(floor(i.posOS * scale)))
                        discard;
                }

                half4 col = (_UseVertexColor > 0.5) ? i.color : _Color;
                col.a *= _Alpha;
                return col;
            }
            ENDHLSL
        }
    }
}
