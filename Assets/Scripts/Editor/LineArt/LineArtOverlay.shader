// 線画の強調表示：選んだ部品だけの下描き(_MainTex)を全体の下描き(_SceneTex)と比べて、
// 見えている所は赤線＋薄い塗り、他の部品に隠れている所は薄い赤で透かして描く（透明 PNG 用）
Shader "Hidden/KMX/LineArtOverlay"
{
    Properties
    {
        _MainTex ("Highlight GBuffer", 2D) = "black" {}
        _SceneTex ("Scene GBuffer", 2D) = "black" {}
        _Color ("Color", Color) = (0.85, 0.1, 0.1, 1)
        _CreaseCos ("Crease Cos", Float) = 0.8
        _DepthTol ("Depth Tolerance", Float) = 0.001
        _ContactTol ("Contact Tolerance", Float) = 0.0005
        _Thickness ("Thickness(px)", Float) = 1
        _VisibleTol ("Visible Tolerance", Float) = 0.0005
    }
    SubShader
    {
        Pass
        {
            Cull Off
            ZWrite Off
            ZTest Always

            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "LineArtCommon.cginc"

            sampler2D _SceneTex;
            float4 _Color;
            float _VisibleTol;

            fixed4 frag(v2f_img i) : SV_Target
            {
                float4 h = tex2Dlod(_MainTex, float4(i.uv, 0, 0));
                if (h.a < 0) return fixed4(_Color.rgb, 0);
                float4 s = tex2Dlod(_SceneTex, float4(i.uv, 0, 0));
                bool visible = h.b <= s.b + _VisibleTol;
                float e = LineArtEdgeAt(_MainTex, i.uv);
                float a = e > 0 ? (visible ? 1.0 : 0.5) : (visible ? 0.22 : 0.08);
                return fixed4(_Color.rgb, a);
            }
            ENDCG
        }
    }
}
