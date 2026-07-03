using UnityEngine;

/// <summary>
/// MeshCollider 生成ヘルパー。
/// 線/点トポロジ（MeshTopology.Lines / Points）のメッシュに MeshCollider を付けると、
/// PhysX が「Failed getting triangles. Submesh topology is lines or points.」を大量に出し、
/// convex cook がワーカースレッドで失敗する。VR実機ではこれが native abort(SIGABRT) の誘因にもなり得る。
/// 生成前にこのユーティリティで「全サブメッシュが Triangle か」を判定し、非対応メッシュはスキップする。
/// </summary>
public static class MeshColliderUtil
{
    /// <summary>全サブメッシュが Triangle トポロジで MeshCollider(cook) に使えるか。</summary>
    public static bool IsCookable(Mesh m)
    {
        if (m == null || m.vertexCount == 0)
        {
            return false;
        }
        for (int i = 0; i < m.subMeshCount; i++)
        {
            if (m.GetTopology(i) != MeshTopology.Triangles)
            {
                return false;
            }
        }
        return true;
    }
}
