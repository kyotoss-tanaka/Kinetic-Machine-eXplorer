using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ワークの所有権レジストリ（機構間で共有）。
/// ワークは常に最大1つの機構（コンベア/バケット等）だけが搬送し、
/// 所有者が手放すまで他の機構は手を出さない。
/// 「上流が領域/搬送区間を抜けて手放す→下流が拾う」の受け渡しを機構横断で成立させる。
/// </summary>
public static class WorkOwnership
{
    /// <summary>ワーク→所有機構</summary>
    private static readonly Dictionary<GameObject, object> owners = new Dictionary<GameObject, object>();

    /// <summary>他の機構が所有しているか</summary>
    public static bool IsOwnedByOther(GameObject work, object me)
    {
        return owners.TryGetValue(work, out var o) && (o != null) && !ReferenceEquals(o, me);
    }

    /// <summary>自分が所有しているか</summary>
    public static bool IsOwner(GameObject work, object me)
    {
        return owners.TryGetValue(work, out var o) && ReferenceEquals(o, me);
    }

    /// <summary>所有権を取得する</summary>
    public static void Claim(GameObject work, object owner)
    {
        owners[work] = owner;
    }

    /// <summary>所有権を手放す（自分が所有者の場合のみ）</summary>
    public static void Release(GameObject work, object owner)
    {
        if (IsOwner(work, owner))
        {
            owners.Remove(work);
        }
    }

    /// <summary>全所有権を消去する（リロード時）</summary>
    public static void Clear()
    {
        owners.Clear();
    }

    /// <summary>指定機構の所有権を全て手放す（リロード・破棄時）</summary>
    public static void ReleaseAll(object owner)
    {
        var stale = new List<GameObject>();
        foreach (var kv in owners)
        {
            if (ReferenceEquals(kv.Value, owner) || (kv.Key == null))
            {
                stale.Add(kv.Key);
            }
        }
        foreach (var key in stale)
        {
            owners.Remove(key);
        }
    }
}
