using System.Collections.Generic;

public class UseTagBaseScript : KssBaseScript
{
    /// <summary>
    /// 使用しているタグを取得する
    /// </summary>
    /// <returns></returns>
    public virtual List<TagInfo> GetUseTags()
    {
        return new List<TagInfo>();
    }
}
