Shader "URP/ClipTransparent"
{
    Properties
    {
        _Color ("Main Color", Color) = (1,1,1,0.5)
        _MainTex ("Main Texture", 2D) = "white" {}
        _CapColor ("Cap Color", Color) = (1,0,0,1)
        _CapThickness ("Cap Threshold", Float) = 0.001
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline"="UniversalRenderPipeline"
            "Queue"="Transparent"
            "RenderType"="Transparent"
        }

        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Back

        // =====================================================
        // Pass 1 : í èÌï`âÊ + ÉNÉäÉbÉv
        // =====================================================
        Pass
        {
            Name "ForwardClip"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            sampler2D _MainTex;
            float4 _Color;
            float4 _ClipPlane;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 worldPos : TEXCOORD1;
            };

            Varyings vert (Attributes v)
            {
                Varyings o;
                o.worldPos = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.worldPos);
                o.uv = v.uv;
                return o;
            }

            half4 frag (Varyings i) : SV_Target
            {
                float d = dot(i.worldPos, _ClipPlane.xyz) + _ClipPlane.w;
                clip(-d);   // ífñ ÉNÉäÉbÉv

                half4 col = tex2D(_MainTex, i.uv) * _Color;
                return col;
            }
            ENDHLSL
        }

        // =====================================================
        // Pass 2 : ífñ ÇÉXÉeÉìÉVÉãÇ…èëÇ´çûÇﬁ
        // =====================================================
        Pass
        {
            Name "StencilMark"
            ZWrite Off
            ColorMask 0

            Stencil
            {
                Ref 1
                Comp Always
                Pass Replace
            }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment fragStencil
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            float4 _ClipPlane;
            float _CapThickness;

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 worldPos : TEXCOORD0;
            };

            Varyings vert (Attributes v)
            {
                Varyings o;
                o.worldPos = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.worldPos);
                return o;
            }

            half4 fragStencil (Varyings i) : SV_Target
            {
                float d = dot(i.worldPos, _ClipPlane.xyz) + _ClipPlane.w;

                // ïΩñ ïtãﬂÇæÇØí Ç∑
                clip(_CapThickness - abs(d));
                return 0;
            }
            ENDHLSL
        }

        // =====================================================
        // Pass 3 : ífñ äWï`âÊ
        // =====================================================
        Pass
        {
            Name "CapDraw"
            ZWrite Off
            ZTest LEqual
            Cull Off

            Stencil
            {
                Ref 1
                Comp Equal
            }

            HLSLPROGRAM
            #pragma vertex vertCap
            #pragma fragment fragCap
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            float4 _CapColor;

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings vertCap (Attributes v)
            {
                Varyings o;
                float3 wp = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(wp);
                return o;
            }

            half4 fragCap (Varyings i) : SV_Target
            {
                return _CapColor;
            }
            ENDHLSL
        }
    }
}
