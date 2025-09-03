Shader "Custom/ClipTransparent"
{
    Properties
    {
        _Color("Main Color", Color) = (1,1,1,1)
        _MainTex("Main Texture", 2D) = "white" {}
        // _ClipPlane �� Properties ����폜�i�O���[�o���ϐ��Ƃ��Đ錾�j
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        LOD 200
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Back

        CGPROGRAM
        #pragma surface surf Standard alpha:fade
        #pragma target 3.0

        sampler2D _MainTex;
        float4 _Color;

        // �O���[�o���ϐ��錾�iSurface Shader�ł͂�������g���j
        uniform float4 _ClipPlane;

        struct Input
        {
            float2 uv_MainTex;
            float3 worldPos;
        };

        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            float dist = dot(IN.worldPos, _ClipPlane.xyz) + _ClipPlane.w;

            // ���ʂ̖@�������̐����̓N���b�v�i�����j
            if (dist > 0.0)
            {
                clip(-1);
            }

            float4 tex = tex2D(_MainTex, IN.uv_MainTex) * _Color;
            o.Albedo = tex.rgb;
            o.Alpha = tex.a;
            o.Metallic = 0.0;
            o.Smoothness = 0.5;
        }
        ENDCG
    }

    FallBack "Transparent/Diffuse"
}
