using System.Collections.Generic;

/// <summary>
/// 通信コンポーネントの共通インターフェース。
/// GlobalScript(Common層) が Com 具象型に依存しないための依存性逆転用。
/// Com 各クラス（ComProtocolBase / ComInner / ComPostgres / ComMongo /
/// ComOpcUaApi / ComMqtt / ComRedis 等）が実装する。
/// </summary>
public interface ITagCom
{
    /// <summary>接続先名（Server:Port 等のキー）</summary>
    string Name { get; }

    /// <summary>タグ値を通信側へ反映する</summary>
    void SetDatas(List<TagInfo> tags);

    /// <summary>サイクル/統計などの更新処理</summary>
    void RenewData();
}
