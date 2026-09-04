using Parameters;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using TMPro;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// 段ボール用スクリプト
/// </summary>
public class CardboardScript : KssBaseScript
{
    [Serializable]
    public class CardboardParts
    {
        [SerializeField]
        public string name;
        [SerializeField]
        public GameObject parts;
        [SerializeField]
        public CardboardPartsScript script;
        [SerializeField]
        public Vector3 anchor = new();
        [SerializeField]
        public Vector3 axis = new Vector3(1, 0, 0);
        [SerializeField]
        public ActionTableData actionTableData;
        [SerializeField]
        public bool isFlap = false;
        [SerializeField]
        public decimal value;
    }

    [Serializable]
    public class CardboardSize
    {

        [SerializeField]
        public int L_Width;
        [SerializeField]
        public int W_Width;
        [SerializeField]
        public int Body_Height;
        [SerializeField]
        public int Top_Height;
        [SerializeField]
        public int Bottom_Height;
    }


    [Serializable]
    public class SuckInfo
    {
        public SuctionScript suctionScript;
        public CardboardParts parts;
    }

    /// <summary>
    /// 段ボール設定
    /// </summary>
    [SerializeField]
    protected CardboardSetting cardboardSetting;

    /// <summary>
    /// モード 0:L1/W1 1:L2/W2 1:L1/L2 2:W1:W2
    /// </summary>
    [SerializeField]
    protected int mode;

    /// <summary>
    /// 現在時間
    /// </summary>
    [SerializeField]
    protected int time;

    /// <summary>
    /// 現在時間
    /// </summary>
    [SerializeField]
    protected int startTime;

    /// <summary>
    /// 現在サイクル
    /// </summary>
    [SerializeField]
    protected int cycle;

    /// <summary>
    /// Body間距離
    /// </summary>
    [SerializeField]
    protected float distance;

    /// <summary>
    /// サイズ
    /// </summary>
    [SerializeField]
    protected CardboardSize Size;

    /// <summary>
    /// 吸引中情報
    /// </summary>
    [SerializeField]
    protected List<SuckInfo> suckInfos = new();

    /// <summary>
    /// 全部品
    /// </summary>
    [SerializeField]
    protected List<CardboardParts> cardboardParts = new();

    [SerializeField]
    CardboardParts L1_Body;
    [SerializeField]
    CardboardParts L1_Top;
    [SerializeField]
    CardboardParts L1_Bottom;
    [SerializeField]
    CardboardParts L2_Body;
    [SerializeField]
    CardboardParts L2_Top;
    [SerializeField]
    CardboardParts L2_Bottom;
    [SerializeField]
    CardboardParts W1_Body;
    [SerializeField]
    CardboardParts W1_Top;
    [SerializeField]
    CardboardParts W1_Bottom;
    [SerializeField]
    CardboardParts W2_Body;
    [SerializeField]
    CardboardParts W2_Top;
    [SerializeField]
    CardboardParts W2_Bottom;

    /// <summary>
    /// サイクルタグ
    /// </summary>
    protected TagInfo cycleTag;

    /// <summary>
    /// チェックポイント（時刻順に解決済み）。null/空なら従来動作
    /// </summary>
    private List<(float time, TagInfo tag, string name)> checkPoints;

    /// <summary>
    /// 次に待つチェックポイントの位置。checkPoints.Count に達したら以降は待たない
    /// </summary>
    private int checkIndex;

    /// <summary>
    /// 再生ヘッド(ms)。チェックポイント方式ではこれで動作テーブルを引く
    /// </summary>
    private float playHead;

    /// <summary>
    /// 待機に入った時刻（無言で固まるのを防ぐ警告用。Time.time基準）
    /// </summary>
    private float waitBegan;

    /// <summary>
    /// 待機の警告を出したか（1回だけ出す）
    /// </summary>
    private bool waitWarned;

    /// <summary>
    /// チェックポイントを解決済みか（設定とタグが揃うまで毎フレーム再試行する）
    /// </summary>
    private bool checkResolved;

    /// <summary>
    /// 再生の開始時刻(ms)。動作テーブルの最も早い時刻。
    /// テーブルが絶対時間で書かれているため0から始めると開始時刻までの区間が
    /// 見た目の変化なしに経過してしまう。生成時からその時刻で再生を始める
    /// </summary>
    private float playStart;

    /// <summary>
    /// 再生ヘッド(ms)。ActUnitInfoでの現在時間表示に使う
    /// </summary>
    public float PlayHead { get { return playHead; } }

    /// <summary>
    /// チェックポイント待機中か
    /// </summary>
    public bool IsWaiting { get; private set; }

    /// <summary>
    /// 待機中のチェックポイント時刻(ms)。待機していなければ -1
    /// </summary>
    public float WaitTime { get; private set; } = -1f;

    /// <summary>
    /// 待機中のタグ名。待機していなければ空
    /// </summary>
    public string WaitTag { get; private set; } = "";

    /// <summary>
    /// チェックポイントの総数（0なら従来動作）
    /// </summary>
    public int CheckPointCount { get { return checkPoints == null ? 0 : checkPoints.Count; } }

    /// <summary>
    /// 消化済みチェックポイント数
    /// </summary>
    public int CheckPointIndex { get { return checkIndex; } }

    /// <summary>
    /// 生成済みの段ボール（テンプレート含む）。ActUnitInfoから状態を引くために登録する
    /// </summary>
    private static readonly List<CardboardScript> instances = new List<CardboardScript>();

    /// <summary>
    /// 指定ユニットの段ボールが存在するか（表示行を作るかの判定用）
    /// </summary>
    /// <summary>
    /// 段ボール設定を持つユニット（機番+名前）。
    /// インスタンスの有無に依存せず判定するために、設定の読み込み時点で登録する。
    /// ※テンプレートの生成は親モデルの存在が条件で、無い場合は実行中にワークが
    ///   生成されるまで実体が現れない（＝画面の一覧作成に間に合わない）
    /// </summary>
    private static readonly HashSet<string> unitKeys = new HashSet<string>();

    /// <summary>
    /// 段ボールのユニット定義。
    /// 段ボールはユニットオブジェクトを作らないため ParameterLoader.CreateUnitObject で
    /// unitSettings から除去される。ActUnitInfo で製函状態を見るために退避しておく
    /// </summary>
    private static readonly List<UnitSetting> unitDefs = new List<UnitSetting>();

    /// <summary>
    /// 退避した段ボールのユニット定義（キャンバスの一覧作成で使う）
    /// </summary>
    public static List<UnitSetting> UnitDefs { get { return unitDefs; } }

    /// <summary>
    /// unitSettings から除去する段ボールのユニット定義を退避する
    /// </summary>
    public static void RegisterUnitDefs(List<UnitSetting> units)
    {
        if (units == null)
        {
            return;
        }
        foreach (var u in units)
        {
            if ((u != null) && !unitDefs.Contains(u))
            {
                unitDefs.Add(u);
            }
        }
    }

    /// <summary>
    /// 段ボール設定を持つユニットを登録する（設定読み込み時に呼ぶ）
    /// </summary>
    public static void RegisterUnits(List<CardboardSetting> settings)
    {
        unitKeys.Clear();
        // 設定の読み込みは CreateUnitObject より前なので、ここで退避も初期化する
        unitDefs.Clear();
        if (settings == null)
        {
            return;
        }
        foreach (var cb in settings)
        {
            unitKeys.Add(cb.mechId + "	" + cb.name);
        }
        CommonFunction.DebugLog($"[Cardboard] 段ボール設定 {unitKeys.Count}件");
    }

    /// <summary>
    /// 指定ユニットが段ボールか（表示行を作るかの判定用）
    /// </summary>
    public static bool HasUnit(UnitSetting unit)
    {
        if (unit == null)
        {
            return false;
        }
        if (unitKeys.Contains(unit.mechId + "	" + unit.name))
        {
            return true;
        }
        // 設定登録が無い経路（旧データ等）でも実体があれば拾う。
        // 参照一致ではなく機番＋ユニット名で照合する
        return instances.Exists(d => IsSameUnit(d, unit));
    }

    /// <summary>
    /// 同じユニットの段ボールか（機番＋ユニット名で照合）
    /// </summary>
    private static bool IsSameUnit(CardboardScript cbs, UnitSetting unit)
    {
        if (cbs == null)
        {
            return false;
        }
        var own = cbs.GetUnitSetting();
        return (own != null) && (own.mechId == unit.mechId) && (own.name == unit.name);
    }

    /// <summary>
    /// 指定ユニットの稼働中（アクティブな）段ボールを1つ返す。無ければ null
    /// </summary>
    public static CardboardScript FindActive(UnitSetting unit)
    {
        return (unit == null) ? null
            : instances.Find(d => (d != null) && d.gameObject.activeInHierarchy && IsSameUnit(d, unit));
    }

    /// <summary>
    /// 登録を破棄する（設定再読み込み時）
    /// </summary>
    public static void ClearInstances()
    {
        instances.Clear();
        unitKeys.Clear();
        unitDefs.Clear();
    }

    /// <summary>
    /// Rigidbody
    /// </summary>
    private Rigidbody rigi = null;

    /// <summary>
    /// 開始処理
    /// </summary>
    protected override void Start()
    {
        base.Start();

        // 初期化処理
        Initialize();
    }

    /// <summary>
    /// 周期処理
    /// </summary>
    protected override void FixedUpdate()
    {
        base.FixedUpdate();

        if (!checkResolved)
        {
            ResolveCheckPoints();
        }
        time = GlobalScript.GetTagData(cycleTag);
        if ((checkPoints != null) && (checkPoints.Count > 0))
        {
            // チェックポイント方式：生成時から自前で時間を進め、指定時刻でIOのONを待つ。
            // 装置が遅れても製函の絵だけが先に進まないようにするため、
            // 装置サイクルタグではなく実経過時間（待機分を除く）で再生ヘッドを進める
            AdvancePlayHead();
            cycle = (int)playHead;
            startTime = 0;   // テーブル評価を有効にする（下の startTime >= 0 のゲート）
        }
        else
        {
            // 従来動作：掴まれてから装置サイクルタグ基準で進む
            if (startTime < 0)
            {
                cycle = time % (cardboardSetting.cycle <= 0 ? 1000 : cardboardSetting.cycle);
            }
            else
            {
                cycle = time - startTime;
            }
            // Body
            if (suckInfos.Count > 0)
            {
                // 吸引されているときのみ段ボール動作
                if (startTime < 0)
                {
                    // サイクルはループしない
                    startTime = time - cycle;
                }
            }
        }
        if (startTime >= 0)
        {
            foreach (var parts in cardboardParts)
            {
                if ((parts.actionTableData != null) && (parts.actionTableData.datas.Count > 0))
                {
                    parts.value = 0;
                    var before = parts.actionTableData.datas.LastOrDefault(d => d.time <= cycle);
                    var after = parts.actionTableData.datas.FirstOrDefault(d => d.time >= cycle);
                    if (before != null && after != null && before.time != after.time)
                    {
                        parts.value = before.value + (after.value - before.value) * (cycle - before.time) / (after.time - before.time);
                    }
                    else
                    {
                        parts.value = before != null ? before.value : (after != null ? after.value : parts.value);
                    }
                    if (parts.isFlap)
                    {
                        // フラップなら
                        parts.parts.transform.localEulerAngles = (float)parts.value * parts.axis;
                    }
                }
            }
            if ((L1_Body.actionTableData != null) && (L1_Body.actionTableData.datas.Count > 0))
            {
                var value = (float)L1_Body.value;
                if (mode == 0)
                {
                    // L1基準
                    W1_Body.parts.transform.localEulerAngles = (180 - value) * W1_Body.axis;
                    W2_Body.parts.transform.localEulerAngles = (180 - value) * W2_Body.axis;
                    L2_Body.parts.transform.localEulerAngles = value * L2_Body.axis;
                }
                else
                {
                    // L2基準
                    W2_Body.parts.transform.localEulerAngles = (180 - value) * W2_Body.axis;
                    W1_Body.parts.transform.localEulerAngles = (180 - value) * W1_Body.axis;
                    L1_Body.parts.transform.localEulerAngles = value * L1_Body.axis;
                }
                /*
                value = 0;
                var before = actionTableData.datas.LastOrDefault(d => d.time <= cycle);
                var after = actionTableData.datas.FirstOrDefault(d => d.time >= cycle);
                if (before != null && after != null && before.time != after.time)
                {
                    value = before.value + (after.value - before.value) * (cycle - before.time) / (after.time - before.time);
                }
                else
                {
                    value = before != null ? before.value : (after != null ? after.value : value);
                }
                position = (float)(value + offset) / (rate == 0 ? 1000f : rate) * unitSetting.actionSetting.dir;
                if (isRotate)
                {
                    moveObject.transform.localEulerAngles = moveDir * position;
                }
                else
                {
                    moveObject.transform.localPosition = moveDir * position;
                }
                */
            }
        }
        /*
        var vctAngle = Vector3.zero;
        if (suckInfos.Count == 2)
        {
            // 並行
            var box1 = suckInfos[0].parts.script.boxCollider;
            var box2 = suckInfos[1].parts.script.boxCollider;
            // 引っ張っているときのみ処理
            if (mode >= 2)
            {
                if (LinePlaneIntersection(box1, box2))
                {
                    if (mode == 2)
                    {
                        if (distance > Size.W_Width)
                        {
                            angle = 90;
                        }
                        else
                        {
                            angle = (float)(Math.Asin(distance / Size.W_Width) * 180 / Math.PI);
                        }
                        vctAngle = angle * W1_Body.axis;
                        W1_Body.parts.transform.localEulerAngles = vctAngle;
                        vctAngle = angle * W2_Body.axis;
                        W2_Body.parts.transform.localEulerAngles = vctAngle;
                    }
                    else if (mode == 3)
                    {
                        if (distance > Size.L_Width)
                        {
                            angle = 90;
                        }
                        else
                        {
                            angle = (float)(Math.Asin(distance / Size.L_Width) * 180 / Math.PI);
                        }
                        vctAngle = (180 - angle) * L1_Body.axis;
                        L1_Body.parts.transform.localEulerAngles = vctAngle;
                        vctAngle = (180 - angle) * L2_Body.axis;
                        L2_Body.parts.transform.localEulerAngles = vctAngle;
                    }
                }
            }
            else
            {
                // 直角
                Vector3 normal1 = GetThinAxisNormal(box1);
                Vector3 normal2 = GetThinAxisNormal(box2);
                angle = Vector3.Angle(normal1, normal2);
                if (mode == 0)
                {
                    vctAngle = angle * W2_Body.axis;
                    W2_Body.parts.transform.localEulerAngles = vctAngle;
                    vctAngle = (180 - angle) * L2_Body.axis;
                    L2_Body.parts.transform.localEulerAngles = vctAngle;
                }
                else if (mode == 1)
                {
                    vctAngle = angle * W1_Body.axis;
                    W1_Body.parts.transform.localEulerAngles = vctAngle;
                    vctAngle = (180 - angle) * L1_Body.axis;
                    L1_Body.parts.transform.localEulerAngles = vctAngle;
                }
            }
        }
        */
    }
    /*
    /// <summary>
    /// 二つのコライダーの距離を図る
    /// </summary>
    /// <param name="boxA"></param>
    /// <param name="boxB"></param>
    /// <returns></returns>
    public bool LinePlaneIntersection(BoxCollider boxA, BoxCollider boxB)
    {
        Vector3 normal = GetThinAxisNormal(boxA);

        Vector3 p0 = boxA.ClosestPoint(boxB.transform.position);
        Vector3 dir = -1 * normal;
        Vector3 planePoint = boxB.ClosestPoint(boxA.transform.position);
        Vector3 planeNormal = normal;
        Vector3 intersection = Vector3.zero;
        distance = 0;

        float denom = Vector3.Dot(dir, planeNormal);
        if (Mathf.Abs(denom) < 1e-6f)
        {
            // dir と planeNormal が直交 → 平行なので交差しない
            return false;
        }

        float t = Vector3.Dot(planePoint - p0, planeNormal) / denom;
        if (t < 0)
        {
            // 交点は直線の逆方向（必要に応じて判定）
            // 直線ではなく「半直線」と考えたい場合は false
        }

        intersection = p0 + dir * t;
        distance = Vector3.Distance(p0, intersection) * 1000;
        return true;
    }

    public static Vector3 GetThinAxisNormal(BoxCollider box)
    {
        Vector3 size = box.size;
        Vector3 scale = box.transform.lossyScale;

        // 各軸のワールドスケールサイズ（= 見た目の厚み）
        float sx = Mathf.Abs(size.x * scale.x);
        float sy = Mathf.Abs(size.y * scale.y);
        float sz = Mathf.Abs(size.z * scale.z);

        // 最小軸を判定して、ワールド空間の方向ベクトルを返す
        if (sx <= sy && sx <= sz)
            return box.transform.right;      // 傾いたX軸
        else if (sy <= sx && sy <= sz)
            return box.transform.up;         // 傾いたY軸
        else
            return box.transform.forward;    // 傾いたZ軸
    }
    */
    /// <summary>
    /// チェックポイントを時刻順に解決する。
    /// タグ解決は毎フレームやらないよう、設定とタグが揃った時点で1回だけ行う
    /// </summary>
    private void ResolveCheckPoints()
    {
        if ((cardboardSetting == null) || !GlobalScript.isLoaded)
        {
            // まだ設定やタグが揃っていない。次フレームで再試行する
            return;
        }
        checkPoints = new List<(float, TagInfo, string)>();
        if (cardboardSetting.checkPoints != null)
        {
            foreach (var cp in cardboardSetting.checkPoints.OrderBy(d => d.time))
            {
                if ((cp.tag == null) || (cp.tag == ""))
                {
                    continue;
                }
                var tag = GlobalScript.GetTagInfo(unitSetting.Database, unitSetting.mechId, cp.tag);
                if (tag == null)
                {
                    Debug.Log($"[Cardboard] {unitSetting.name} チェックポイントのタグ '{cp.tag}' が解決できません（@{cp.time:F0}ms）");
                    continue;
                }
                // 表示用の名前は設定値をそのまま持つ。TagInfo.Tag は空のことがある
                checkPoints.Add((cp.time, tag, cp.tag));
            }
        }
        // 動作テーブルの最も早い時刻を再生開始位置にする（テーブルは絶対時間で書かれている）
        playStart = 0f;
        var hasTable = false;
        foreach (var parts in cardboardParts)
        {
            if ((parts.actionTableData == null) || (parts.actionTableData.datas.Count == 0))
            {
                continue;
            }
            // datas は時刻昇順に並べ替えてある
            var first = (float)parts.actionTableData.datas[0].time;
            if (!hasTable || (first < playStart))
            {
                playStart = first;
                hasTable = true;
            }
        }
        playHead = playStart;
        checkResolved = true;
        Debug.Log($"[Cardboard] {unitSetting.name} チェックポイント {checkPoints.Count}件"
            + $" 再生開始={playStart:F0}ms"
            + (checkPoints.Count == 0 ? "（未設定のため従来動作＝掴まれてから装置サイクル基準で進行）" : ""));
    }

    /// <summary>
    /// 再生ヘッドを進める。
    /// 次のチェックポイント時刻に達したら、そのタグがONになるまで止める（到達時に既にONなら通過）。
    /// IOがOFFに戻っても巻き戻さない（製函は一方通行）
    /// </summary>
    private void AdvancePlayHead()
    {
        if (checkIndex < checkPoints.Count)
        {
            var next = checkPoints[checkIndex];
            if (playHead >= next.time)
            {
                // ※GlobalScript.GetTagData(TagInfo) は tagDatas[..][TagInfo.Tag] で再検索するため使えない。
                //   デバイスアドレス形式（d_plc_y1[896]等）で解決されたタグは TagInfo.Tag が空で、
                //   再検索が必ず失敗して常に0になり、ONでも待機し続ける。TagInfo.Value を直接読む
                if ((next.tag != null) && (next.tag.Value < 1))
                {
                    // 待機中。時刻を止めたまま返す
                    IsWaiting = true;
                    WaitTime = next.time;
                    WaitTag = next.name;
                    if (waitBegan <= 0f)
                    {
                        waitBegan = Time.time;
                    }
                    else if (!waitWarned && (Time.time - waitBegan > 10f))
                    {
                        // 無言で固まると原因が分からないため1回だけ知らせる
                        waitWarned = true;
                        Debug.Log($"[Cardboard] {unitSetting.name} 製函が {next.time:F0}ms で待機中です"
                            + $"（タグ={next.name} が10秒以上ONになりません）");
                    }
                    // 待機位置に丸めておく（オーバーランした分を切り捨てて姿勢を固定する）
                    playHead = next.time;
                    return;
                }
                // 通過
                checkIndex++;
                waitBegan = 0f;
                waitWarned = false;
                IsWaiting = false;
                WaitTime = -1f;
                WaitTag = "";
            }
        }
        playHead += Time.fixedDeltaTime * 1000f;
    }

    /// <summary>
    /// 製函の再生状態を初期化する。
    /// プール再利用ではStartが再実行されないため、返却時にここを呼ぶ必要がある
    /// </summary>
    public void ResetPlayback()
    {
        startTime = -1;
        cycle = 0;
        playHead = playStart;
        checkIndex = 0;
        waitBegan = 0f;
        waitWarned = false;
        checkResolved = false;
        IsWaiting = false;
        WaitTime = -1f;
        WaitTag = "";
        suckInfos.Clear();
    }

    /// <summary>
    /// 初期化処理
    /// </summary>
    private void Initialize()
    {
        // サイクルタグ設定
        var tag = GlobalScript.callbackTags.Find(d => d.database == unitSetting.Database);
        cycleTag = tag == null ? null : tag.cycle;
        startTime = -1;
        // チェックポイントの解決は FixedUpdate で遅延実行する。
        // SetParameter は Initialize を呼ばないため、Start が先に走ると
        // cardboardSetting が未設定でここでは解決できない
        checkPoints = null;
        checkResolved = false;
        playHead = 0f;
        checkIndex = 0;
        waitBegan = 0f;
        waitWarned = false;

        // Rigidbody追加
        rigi = GetComponent<Rigidbody>();
        if (rigi == null)
        {
            rigi = gameObject.AddComponent<Rigidbody>();
        }
        rigi.isKinematic = false;
        rigi.useGravity = true;

        // フラップの親子関係設定
        L1_Top.parts.transform.parent = L1_Body.parts.transform;
        L1_Bottom.parts.transform.parent = L1_Body.parts.transform;
        L2_Top.parts.transform.parent = L2_Body.parts.transform;
        L2_Bottom.parts.transform.parent = L2_Body.parts.transform;
        W1_Top.parts.transform.parent = W1_Body.parts.transform;
        W1_Bottom.parts.transform.parent = W1_Body.parts.transform;
        W2_Top.parts.transform.parent = W2_Body.parts.transform;
        W2_Bottom.parts.transform.parent = W2_Body.parts.transform;

        // ボディの親子関係
        if (mode == 0)
        {
            // L1基準で開く
            W2_Body.parts.transform.parent = L1_Body.parts.transform;
            L2_Body.parts.transform.parent = W2_Body.parts.transform;
            W1_Body.parts.transform.parent = L2_Body.parts.transform;
        }
        else
        {
            // L2基準で開く
            W1_Body.parts.transform.parent = L2_Body.parts.transform;
            L1_Body.parts.transform.parent = W1_Body.parts.transform;
            W2_Body.parts.transform.parent = L1_Body.parts.transform;
        }
        /*
        // モード別親子関係設定
        if (mode == 1)
        {
            L1_Body.parts.transform.parent = W2_Body.parts.transform;
            W1_Body.parts.transform.parent = L1_Body.parts.transform;
        }
        else if (mode == 2)
        {
            W1_Body.parts.transform.parent = L1_Body.parts.transform;
            W2_Body.parts.transform.parent = L2_Body.parts.transform;
        }
        else if (mode == 3)
        {
            L1_Body.parts.transform.parent = W2_Body.parts.transform;
            L2_Body.parts.transform.parent = W1_Body.parts.transform;
        }
        else
        {
            L2_Body.parts.transform.parent = W1_Body.parts.transform;
            W2_Body.parts.transform.parent = L2_Body.parts.transform;
        }
        */

        // 各設定
        L1_Top.isFlap = true;
        L1_Bottom.isFlap = true;
        L2_Top.isFlap = true;
        L2_Bottom.isFlap = true;
        W1_Top.isFlap = true;
        W1_Bottom.isFlap = true;
        W2_Top.isFlap = true;
        W2_Bottom.isFlap = true;

        cardboardParts.Add(L1_Body);
        cardboardParts.Add(L1_Top);
        cardboardParts.Add(L1_Bottom);
        cardboardParts.Add(L2_Body);
        cardboardParts.Add(L2_Top);
        cardboardParts.Add(L2_Bottom);
        cardboardParts.Add(W1_Body);
        cardboardParts.Add(W1_Top);
        cardboardParts.Add(W1_Bottom);
        cardboardParts.Add(W2_Body);
        cardboardParts.Add(W2_Top);
        cardboardParts.Add(W2_Bottom);
        foreach (var parts in cardboardParts)
        {
            SetComponent(parts);
        }
    }

    /// <summary>
    /// コンポーネントセット
    /// </summary>
    /// <param name="parts"></param>
    private void SetComponent(CardboardParts parts)
    {
        parts.script = parts.parts.AddComponent<CardboardPartsScript>();
        parts.script.isFlap = parts.isFlap;

        // テーブルデータ取得
        var unit = unitSetting.name + ":";
        parts.actionTableData = GlobalScript.actionTableDatas.Find(d => (d.mechId == unitSetting.mechId) && (d.name == unit + parts.name));
        if (parts.actionTableData == null)
        {
            parts.actionTableData = new ActionTableData();
        }
        else
        {
            // 時間ごとにソート
            parts.actionTableData.datas = parts.actionTableData.datas.OrderBy(d => d.time).ToList();
        }
    }

    /// <summary>
    /// 吸引セット
    /// </summary>
    public bool SetSuction(SuctionScript suction, GameObject parts)
    {
        if (parts != null)
        {
            var p = cardboardParts.Find(d => d.parts == parts);
            if (!p.isFlap)
            {
                var info = new CardboardScript.SuckInfo
                {
                    suctionScript = suction,
                    parts = p
                };
                /*
                if (suckInfos.Count == 0)
                {
                    transform.parent = suction.transform;
                }
                else
                {
                    parts.transform.parent = suction.transform;
                }
                */
                suckInfos.Add(info);
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// 吸引セット
    /// </summary>
    public void ResetSuction(SuctionScript suction)
    {
        var info = suckInfos.Find(d => d.suctionScript == suction);
        if (info != null)
        {
            suckInfos.Remove(info);
            /*
            if (suckInfos.Count > 0)
            {
                transform.parent = suckInfos[0].suctionScript.transform;
                suckInfos[0].parts.parts.transform.parent = transform;
            }
            else
            {
                transform.parent = null;
                rigi.useGravity = true;
                rigi.isKinematic = false;
            }
            */
        }
    }

    /// <summary>
    /// パラメータセット
    /// </summary>
    /// <param name="unitSetting"></param>
    /// <param name="obj"></param>
    public void SetParameter(CardboardScript org)
    {
        SetParameter(org.GetUnitSetting(), org.GetSetting());
    }

    /// <summary>
    /// パラメータセット
    /// </summary>
    /// <param name="unitSetting"></param>
    /// <param name="obj"></param>
    public override void SetParameter(UnitSetting unitSetting, object obj)
    {
        base.SetParameter(unitSetting, obj);

        cardboardSetting = (CardboardSetting)obj;
        mode = cardboardSetting.mode;
        // 設定が入れ替わったのでチェックポイントを再解決させる
        // （F5の再読み込みでタグが更新されないため）
        checkResolved = false;
        checkIndex = 0;
        IsWaiting = false;
        WaitTime = -1f;
        WaitTag = "";
        // ActUnitInfo から現在時間を引けるように登録する（破棄済みは参照時に除外）
        instances.RemoveAll(d => d == null);
        if (!instances.Contains(this))
        {
            instances.Add(this);
        }
        var children = GetComponentsInChildren<Transform>().Select(d => d.gameObject).ToList();
        L1_Body = new CardboardParts
        {
            name = "Body",
            parts = children.Find(d => d.name == cardboardSetting.l1_Body),
            axis = new Vector3(0, 1, 0)
        };
        L1_Top = new CardboardParts
        {
            name = "L1_Top",
            parts = children.Find(d => d.name == cardboardSetting.l1_Top),
            axis = new Vector3(-1, 0, 0)
        };
        L1_Bottom = new CardboardParts
        {
            name = "L1_Bottom",
            parts = children.Find(d => d.name == cardboardSetting.l1_Bottom),
            axis = new Vector3(1, 0, 0)
        };
        L2_Body = new CardboardParts
        {
            parts = children.Find(d => d.name == cardboardSetting.l2_Body),
            axis = new Vector3(0, 1, 0)
        };
        L2_Top = new CardboardParts
        {
            name = "L2_Top",
            parts = children.Find(d => d.name == cardboardSetting.l2_Top),
            axis = new Vector3(-1, 0, 0)
        };
        L2_Bottom = new CardboardParts
        {
            name = "L2_Bottom",
            parts = children.Find(d => d.name == cardboardSetting.l2_Bottom),
            axis = new Vector3(1, 0, 0)
        };
        W1_Body = new CardboardParts
        {
            parts = children.Find(d => d.name == cardboardSetting.w1_Body),
            axis = new Vector3(0, 1, 0)
        };
        W1_Top = new CardboardParts
        {
            name = "W1_Top",
            parts = children.Find(d => d.name == cardboardSetting.w1_Top),
            axis = new Vector3(-1, 0, 0)
        };
        W1_Bottom = new CardboardParts
        {
            name = "W1_Bottom",
            parts = children.Find(d => d.name == cardboardSetting.w1_Bottom),
            axis = new Vector3(1, 0, 0)
        };
        W2_Body = new CardboardParts
        {
            parts = children.Find(d => d.name == cardboardSetting.w2_Body),
            axis = new Vector3(0, 1, 0)
        };
        W2_Top = new CardboardParts
        {
            name = "W2_Top",
            parts = children.Find(d => d.name == cardboardSetting.w2_Top),
            axis = new Vector3(-1, 0, 0)
        };
        W2_Bottom = new CardboardParts
        {
            name = "W2_Bottom",
            parts = children.Find(d => d.name == cardboardSetting.w2_Bottom),
            axis = new Vector3(1, 0, 0)
        };
    }

    /// <summary>
    /// 設定取得
    /// </summary>
    /// <returns></returns>
    public UnitSetting GetUnitSetting()
    {
        return unitSetting;
    }

    /// <summary>
    /// 設定取得
    /// </summary>
    /// <returns></returns>
    public CardboardSetting GetSetting()
    {
        return cardboardSetting;
    }
}
