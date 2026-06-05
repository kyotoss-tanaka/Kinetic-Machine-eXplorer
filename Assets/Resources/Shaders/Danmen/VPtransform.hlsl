void VPtransform_float
(float3 _position,
	float4x4 _ProjectorMatrixVP,
    out float4 _projectorSpacePos,
    out float2 UV)
{
        float4 vertex;
        float4 projectorSpacePos;
        float3 worldPos;
        float3 worldNormal;
#if defined(SHADERGRAPH_PREVIEW)
        _projectorSpacePos= float4 (1, 1, 1, 1);
        UV = float2(1, 1);
#else
    projectorSpacePos = mul(_ProjectorMatrixVP, float4(_position.xyz, 1.0));
    projectorSpacePos.xy = projectorSpacePos.xy * 0.5f + projectorSpacePos.w * 0.5f;
    //projectorSpacePos = _ProjectorMatrixVP;
    //projectorSpacePos = ComputeScreenPos(projectorSpacePos);
    //projectorSpacePos.xyz /= projectorSpacePos.w;
    //projectorSpacePos.xyz = float4(projectorSpacePos.x, projectorSpacePos.y, projectorSpacePos.w, projectorSpacePos.w);
    float2 uv = float2(projectorSpacePos.x, projectorSpacePos.y);
    _projectorSpacePos = projectorSpacePos;
    UV = uv;
#endif
}

