// 線画の線抽出の共通処理（LineArtEdge / LineArtOverlay で使う）
// 下描きは rg=面の向き(xy) b=奥行き a=部品番号（背景は -1）
#ifndef LINE_ART_COMMON_INCLUDED
#define LINE_ART_COMMON_INCLUDED

sampler2D _MainTex;
float4 _MainTex_TexelSize;
float _CreaseCos;
float _DepthTol;
float _ContactTol;
float _Thickness;

float4 LineArtSample(sampler2D tex, float2 uv, float2 offs)
{
    return tex2Dlod(tex, float4(uv + offs * _MainTex_TexelSize.xy * _Thickness, 0, 0));
}

float3 LineArtNormal(float4 g)
{
    return float3(g.xy, sqrt(saturate(1 - dot(g.xy, g.xy))));
}

// 中心 c と隣 s の間に線を引くか（中心側に描く）
float LineArtEdgeWith(float4 c, float4 s)
{
    bool bgC = c.a < 0;
    bool bgS = s.a < 0;
    if (bgC) return 0;              // 背景側には描かない
    if (bgS) return 1;              // 外形
    if (abs(s.a - c.a) > 0.5)
    {
        // 別の部品との境目：手前側（接している所は両側）に描く
        return c.b <= s.b + _ContactTol ? 1 : 0;
    }
    // 同じ部品：面の折れ目
    return dot(LineArtNormal(c), LineArtNormal(s)) < _CreaseCos ? 1 : 0;
}

// 同じ部品内の奥行きの段差（自分自身に隠れる所の輪郭）
float LineArtDepthStep(float4 c, float4 a, float4 b)
{
    if (c.a < 0 || abs(a.a - c.a) > 0.5 || abs(b.a - c.a) > 0.5) return 0;
    // 傾いた平面は差し引きで 0 になる。中心が両隣より手前に飛び出していれば輪郭
    return (a.b + b.b - 2 * c.b) > _DepthTol ? 1 : 0;
}

// uv の点が線か（1=線）
float LineArtEdgeAt(sampler2D tex, float2 uv)
{
    float4 c = LineArtSample(tex, uv, float2(0, 0));
    float4 l = LineArtSample(tex, uv, float2(-1, 0));
    float4 r = LineArtSample(tex, uv, float2(1, 0));
    float4 d = LineArtSample(tex, uv, float2(0, -1));
    float4 u = LineArtSample(tex, uv, float2(0, 1));
    float4 ld = LineArtSample(tex, uv, float2(-1, -1));
    float4 ru = LineArtSample(tex, uv, float2(1, 1));
    float4 lu = LineArtSample(tex, uv, float2(-1, 1));
    float4 rd = LineArtSample(tex, uv, float2(1, -1));

    float e = 0;
    e = max(e, LineArtEdgeWith(c, l));
    e = max(e, LineArtEdgeWith(c, r));
    e = max(e, LineArtEdgeWith(c, d));
    e = max(e, LineArtEdgeWith(c, u));
    e = max(e, LineArtEdgeWith(c, ld));
    e = max(e, LineArtEdgeWith(c, ru));
    e = max(e, LineArtEdgeWith(c, lu));
    e = max(e, LineArtEdgeWith(c, rd));
    e = max(e, LineArtDepthStep(c, l, r));
    e = max(e, LineArtDepthStep(c, d, u));
    e = max(e, LineArtDepthStep(c, ld, ru));
    e = max(e, LineArtDepthStep(c, lu, rd));
    return e;
}

#endif
