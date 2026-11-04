// 線画用の線抽出：下描き（面の向き・奥行き・部品番号）の差から線を描く
// 白地に黒線を出す。部品の境目・面の折れ目・同じ部品内の奥行きの段差が線になる
Shader "Hidden/KMX/LineArtEdge"
{
    Properties
    {
        _MainTex ("GBuffer", 2D) = "black" {}
        _CreaseCos ("Crease Cos", Float) = 0.8
        _DepthTol ("Depth Tolerance", Float) = 0.001
        _ContactTol ("Contact Tolerance", Float) = 0.0005
        _Thickness ("Thickness(px)", Float) = 1
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

            fixed4 frag(v2f_img i) : SV_Target
            {
                float v = 1 - LineArtEdgeAt(_MainTex, i.uv);
                return fixed4(v, v, v, 1);
            }
            ENDCG
        }
    }
}
