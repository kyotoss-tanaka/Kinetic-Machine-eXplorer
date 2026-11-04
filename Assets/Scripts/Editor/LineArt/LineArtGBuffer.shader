// 線画用の下描き：画面の各点に「面の向き(xy)・奥行き・部品番号」を書き込む
// PrefabLineArtRenderer から CommandBuffer で直接描く（URP の描画処理は通さない）
Shader "Hidden/KMX/LineArtGBuffer"
{
    Properties
    {
        _ObjectId ("Object Id", Float) = 0
    }
    SubShader
    {
        Pass
        {
            Cull Off
            ZWrite On
            ZTest LEqual

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float4x4 _LineArtView;
            float4x4 _LineArtProj;
            float _ObjectId;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 vpos : TEXCOORD0;
                float3 vnormal : TEXCOORD1;
            };

            v2f vert(appdata v)
            {
                v2f o;
                float4 wpos = mul(unity_ObjectToWorld, v.vertex);
                float4 vp = mul(_LineArtView, wpos);
                o.pos = mul(_LineArtProj, vp);
                o.vpos = vp.xyz;
                o.vnormal = mul((float3x3)_LineArtView, UnityObjectToWorldNormal(v.normal));
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                // 法線が無いメッシュは面の向きを位置の変化から求める
                float3 n = i.vnormal;
                if (dot(n, n) < 1e-8)
                {
                    n = cross(ddx(i.vpos), ddy(i.vpos));
                }
                n = normalize(n);
                // 裏面が見えている場合も手前向きにそろえる（視線は -z 方向）
                if (n.z < 0) n = -n;
                return float4(n.x, n.y, -i.vpos.z, _ObjectId);
            }
            ENDCG
        }
    }
}
