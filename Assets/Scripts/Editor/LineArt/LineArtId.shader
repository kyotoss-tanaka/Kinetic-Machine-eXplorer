// 線画の押せる範囲用：下描きから部品番号だけを取り出す（最も近い点を取る）
Shader "Hidden/KMX/LineArtId"
{
    Properties
    {
        _MainTex ("GBuffer", 2D) = "black" {}
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

            sampler2D _MainTex;

            float4 frag(v2f_img i) : SV_Target
            {
                float id = tex2Dlod(_MainTex, float4(i.uv, 0, 0)).a;
                return float4(id, 0, 0, 1);
            }
            ENDCG
        }
    }
}
