using Parameters;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using UnityEngine;

/// <summary>
/// コンベア（ロジック搬送方式）。
/// 物理エンジンを使わず、搬送領域内のワークをベルト速度で決定的に動かす。
/// ・速度テーブル（上から評価し最初にONのタグの速度。全OFFで停止）＋加速度ランプ
/// ・搬送面 = 物体形状設定（あれば優先）またはベルト面モデルの境界から自動算出
/// ・ストッパー = 進行方向の堰き止め＋前進時の押し戻し（上流ワークへ連鎖）
/// ・整列ガイド = 横押し（ワーク間の押し出し連鎖あり）
/// ・終端動作 = そのまま／物理落下（スケール非依存の自前重力）
/// Ctrl+Shift押下中は搬送領域(緑)・ストッパー(赤)・ガイド(黄)の矩形を表示する。
/// </summary>
public class ConveyorScript : KssBaseScript
{
    /// <summary>
    /// キャンバス表示
    /// </summary>
    protected override bool isCanvas { get { return true; } }

    /// <summary>
    /// コンベア面上の矩形（フレーム座標：f=流れ方向、l=横方向、u=上方向の各スカラー範囲）
    /// </summary>
    private struct FrameRect
    {
        public float fMin, fMax, lMin, lMax, uMin, uMax;

        public bool OverlapF(FrameRect o) { return (fMin < o.fMax) && (fMax > o.fMin); }
        public bool OverlapL(FrameRect o) { return (lMin < o.lMax) && (lMax > o.lMin); }
        public bool OverlapU(FrameRect o) { return (uMin < o.uMax) && (uMax > o.uMin); }
    }

    /// <summary>
    /// 搬送中ワーク
    /// </summary>
    private class WorkEntry
    {
        public GameObject obj;
        public FrameRect rect;
    }

    /// <summary>
    /// ストッパー/ガイドの実行時情報。
    /// 境界はメッシュ形状(MeshFilter)から計算する（モデルを非表示にしても機構として働き続けるため）
    /// </summary>
    private class BlockerEntry
    {
        public ConveyerSetting.BlockerData data;
        public MeshFilter[] filters;
        public FrameRect rect;
        /// <summary>各メッシュ箱の8隅（フレーム座標 x=f, y=l, z=u）。
        /// 外接箱でなく実形状でクリップ判定するため保持する（斜めのストッパーでも箱が膨らまない）</summary>
        public readonly List<Vector3[]> boxes = new List<Vector3[]>();
        /// <summary>前フレーム姿勢の8隅（コンベア基準の相対姿勢から復元。共回り中は現在と一致し、
        /// プッシャーの「相対的な」移動だけが掃引として検出される）</summary>
        public readonly List<Vector3[]> prevBoxes = new List<Vector3[]>();
        /// <summary>前フレームのコンベア基準相対姿勢</summary>
        public Vector3 prevRelPos;
        public Quaternion prevRelRot;
        public bool hasPrevPose;
        public bool valid;
        public LineRenderer line;
    }

    /// <summary>
    /// コンベア設定
    /// </summary>
    private ConveyerSetting cv;

    /// <summary>
    /// ユニット設定
    /// </summary>
    private UnitSetting unit;

    /// <summary>
    /// 速度テーブルのタグ
    /// </summary>
    private List<TagInfo> speedTags = new List<TagInfo>();

    /// <summary>
    /// 現在速度(m/sec)
    /// </summary>
    private float currentSpeed;

    /// <summary>
    /// 動作中（目標速度>0）
    /// </summary>
    private bool isMoving;

    /// <summary>
    /// ストッパー/ガイド
    /// </summary>
    private List<BlockerEntry> blockers = new List<BlockerEntry>();

    /// <summary>
    /// ベルト面モデルのメッシュ（非表示でも搬送面を維持するためレンダラでなくメッシュ形状を使う）
    /// </summary>
    private MeshFilter[] beltFilters;

    /// <summary>
    /// 搬送面ソースの警告を一度だけ出す
    /// </summary>
    private bool warnedNoSurface;

    /// <summary>
    /// 搬送面情報のログを一度だけ出す
    /// </summary>
    private bool loggedSurface;


    /// <summary>
    /// 前フレームで搬送管理下にあったワーク（新規捕捉の判定用）
    /// </summary>
    private readonly HashSet<GameObject> captured = new HashSet<GameObject>();

    /// <summary>
    /// 全コンベア（終端の受け渡し先判定用）
    /// </summary>
    private static readonly List<ConveyorScript> conveyors = new List<ConveyorScript>();

    /// <summary>
    /// 土台ローカルのワーク定位置（位置・向き）。
    /// ターンテーブル等が動いたら毎フレームここからワールド姿勢を復元する（＝土台に追従）。
    /// 差分加算でなく復元方式なので、ワークが親子関係でも動かされる場合に二重適用にならない
    /// </summary>
    private struct RideAnchor
    {
        public Vector3 pos;      // 土台ローカル位置（スケール非適用の実寸オフセット）
        public Quaternion rot;   // 土台ローカル回転
    }

    /// <summary>
    /// 捕捉中ワークの土台ローカル定位置
    /// </summary>
    private readonly Dictionary<GameObject, RideAnchor> rideAnchors = new Dictionary<GameObject, RideAnchor>();

    /// <summary>
    /// 今フレームの搬送対象ワーク（MyFixedUpdate内でのみ使う作業用。毎フレームnewしないため使い回す）
    /// </summary>
    private readonly List<WorkEntry> entries = new List<WorkEntry>();

    /// <summary>
    /// 上記を流れ方向順に並べ替えたもの（同上）
    /// </summary>
    private readonly List<WorkEntry> byF = new List<WorkEntry>();

    /// <summary>
    /// WorkEntry の使い回しプール。WorkEntry はクラス（rectをその場で書き換えるためstructにできない）なので、
    /// 毎フレームnewすると「全コンベア×全搬送ワーク×サブステップ数」でゴミが出続ける
    /// </summary>
    private readonly List<WorkEntry> entryPool = new List<WorkEntry>();

    /// <summary>
    /// 今フレームでプールから貸し出した数
    /// </summary>
    private int entryPoolUsed;

    /// <summary>
    /// 作業用リストを空にしてプールを貸し出し前へ戻す
    /// </summary>
    private void ResetEntries()
    {
        entries.Clear();
        byF.Clear();
        entryPoolUsed = 0;
    }

    /// <summary>
    /// プールから WorkEntry を1つ借りる（足りなければ作って足す）
    /// </summary>
    private WorkEntry RentEntry()
    {
        if (entryPoolUsed >= entryPool.Count)
        {
            entryPool.Add(new WorkEntry());
        }
        return entryPool[entryPoolUsed++];
    }

    /// <summary>
    /// 直近フレームの搬送領域（他コンベアからの受け渡し判定用）
    /// </summary>
    private FrameRect lastRegion;

    /// <summary>
    /// 直近フレームの搬送領域が有効か
    /// </summary>
    private bool lastRegionValid;

    /// <summary>
    /// 確認表示（Ctrl+Shift）のルート
    /// </summary>
    private GameObject overlayRoot;

    /// <summary>
    /// 確認表示の搬送領域ライン
    /// </summary>
    private LineRenderer overlayRegion;

    // フレーム軸（毎フレーム再構成。親ユニットが動いても追従する）
    private Vector3 fDir;   // 流れ方向
    private Vector3 lDir;   // 横方向
    private Vector3 uDir;   // 上方向

    /// <summary>
    /// 現在速度(mm/sec)。ActUnitInfo表示用
    /// </summary>
    public float CurrentSpeedMmSec { get { return currentSpeed * 1000f; } }

    /// <summary>
    /// 動作中（目標速度あり）。ActUnitInfo表示用
    /// </summary>
    public bool IsMoving { get { return isMoving; } }

    /// <summary>
    /// コンベア設定（ActUnitInfo表示用）
    /// </summary>
    public ConveyerSetting Setting { get { return cv; } }

    /// <summary>
    /// 更新処理（決定的にするためFixedUpdateで搬送する）
    /// </summary>
    protected override void MyFixedUpdate()
    {
        if (cv == null)
        {
            return;
        }
        PurgeWorkFilters();
        var dt = Time.fixedDeltaTime;

        // 目標速度（上から評価し最初にONのタグの速度。タグ未入力の行は常時ON。全OFFで停止）
        var target = 0f;
        for (var i = 0; i < speedTags.Count; i++)
        {
            if ((speedTags[i] == null) || (GlobalScript.GetTagData(speedTags[i]) == 1))
            {
                target = cv.speeds[i].spd;
                break;
            }
        }
        isMoving = target != 0f;
        // 加速度ランプ（0=瞬時）
        currentSpeed = cv.acl <= 0f ? target : Mathf.MoveTowards(currentSpeed, target, cv.acl * dt);

        // フレーム・搬送領域・ストッパー/ガイド矩形を現在姿勢で再構成
        if (!BuildFrame(out var region))
        {
            lastRegionValid = false;
            return;
        }
        lastRegion = region;
        lastRegionValid = true;
        RenewBlockers();

        // 土台の動きへの追従: 前フレーム終了時に記録した「土台ローカルの定位置」からワールド姿勢を復元する。
        // 親子関係で既に動いたワークも同じ定位置へ戻るだけなので、二重適用は起きない
        foreach (var obj in captured)
        {
            if ((obj == null) || !obj.activeInHierarchy || !rideAnchors.TryGetValue(obj, out var anchor))
            {
                continue;
            }
            if (obj.TryGetComponent<ConveyorFallScript>(out _))
            {
                // 落下開始済みは物理に任せる
                continue;
            }
            obj.transform.position = transform.position + transform.rotation * anchor.pos;
            obj.transform.rotation = transform.rotation * anchor.rot;
        }

        // 搬送対象ワークの収集（全プールのアクティブワークから境界で判定）
        ResetEntries();
        foreach (var obj in MultiObjectFactoryScript.EnumerateActiveWorks())
        {
            var rect = GetWorldRect(obj);
            if (rect.fMax <= rect.fMin)
            {
                continue;
            }
            // 他機構（コンベア/バケット）が搬送中のワークは奪わない（上流が手放してから拾う）
            if (WorkOwnership.IsOwnedByOther(obj, this))
            {
                continue;
            }
            // 領域内（平面重なり）かつ底面が搬送面±許容帯
            var planOk = rect.fMin < region.fMax && rect.fMax > region.fMin && rect.lMin < region.lMax && rect.lMax > region.lMin;
            var heightOk = Mathf.Abs(rect.uMin - region.uMax) <= cv.margin;
            if (!planOk || !heightOk)
            {
                continue;
            }
            Capture(obj);
            // 新規捕捉時（生成直後・上から落下・置き直し）はベルト面へ着地させる
            if (!captured.Contains(obj))
            {
                var snap = region.uMax - rect.uMin;
                if (Mathf.Abs(snap) > 0.0002f)
                {
                    obj.transform.position += uDir * snap;
                    rect.uMin += snap;
                    rect.uMax += snap;
                }
            }
            var entry = RentEntry();
            entry.obj = obj;
            entry.rect = rect;
            entries.Add(entry);
        }
        // 所有権の更新: 今フレーム搬送するワークは自分の所有。
        // 前フレームまで搬送していて外れたワーク（領域外・削除・プール返却）は手放す→次のコンベアが拾える
        foreach (var w in entries)
        {
            WorkOwnership.Claim(w.obj, this);
        }
        foreach (var old in captured)
        {
            var still = false;
            foreach (var w in entries)
            {
                if (w.obj == old)
                {
                    still = true;
                    break;
                }
            }
            if (!still)
            {
                WorkOwnership.Release(old, this);
            }
        }
        // 今フレームの捕捉状態を記録（領域から出たワークは次に入り直したとき再着地する）
        captured.Clear();
        foreach (var w in entries)
        {
            captured.Add(w.obj);
        }

        // 下流から順に搬送（ストッパー/先行ワークでクランプ。前進ストッパーの押し戻しも同式で連鎖）
        entries.Sort((a, b) => b.rect.fMax.CompareTo(a.rect.fMax));
        for (var i = 0; i < entries.Count; i++)
        {
            var w = entries[i];
            // ストッパー（当たる面はワークがどちら側にいるかで自動判定）
            var stopClamp = float.PositiveInfinity;   // 上流面での堰き止め位置
            var pushTarget = float.NegativeInfinity;  // 下流面での前押し位置（プッシャー動作）
            foreach (var blk in blockers)
            {
                if (!blk.valid || (blk.data.role != 0))
                {
                    continue;
                }
                // 実形状をワークの横帯・高さ帯でクリップした支持面のf範囲で判定する
                // （外接箱だと斜めのストッパーが膨らんで、離れているワークに誤接触するため）
                var off = blk.data.offset;
                if (!ClipRangeF(blk.boxes, w.rect.lMin - off, w.rect.lMax + off, w.rect.uMin, w.rect.uMax, out var bfMin, out var bfMax))
                {
                    continue;
                }
                bfMin -= off;
                bfMax += off;
                // 前フレーム姿勢での支持面（コンベア基準相対姿勢から復元。共回り中は現在と一致）
                if (!ClipRangeF(blk.prevBoxes, w.rect.lMin - off, w.rect.lMax + off, w.rect.uMin, w.rect.uMax, out var pfMin, out var pfMax))
                {
                    pfMin = bfMin;
                    pfMax = bfMax;
                }
                else
                {
                    pfMin -= off;
                    pfMax += off;
                }
                var workCenter = (w.rect.fMin + w.rect.fMax) * 0.5f;
                // どちら側にいるかは前フレームのストッパー位置で判定する
                // （高速なストッパーが1フレームでワークを追い越しても、押すべき面を取り違えない）
                if (workCenter < (pfMin + pfMax) * 0.5f)
                {
                    // ワークが上流側: ストッパーの上流面で堰き止め。前進してきたら押し戻し
                    // （接触判定は前フレーム位置との掃引範囲。高速移動の通過フレームでも取りこぼさない）
                    if (w.rect.fMin < Mathf.Max(bfMax, pfMax))
                    {
                        stopClamp = Mathf.Min(stopClamp, bfMin);
                    }
                }
                else
                {
                    // ワークが下流側: ストッパーが「実際に前進した」フレームだけ下流面で前へ押す（プッシャー動作）
                    var advancing = (bfMax - pfMax) > 0.00001f;
                    if (advancing && (Mathf.Max(bfMax, pfMax) > w.rect.fMin))
                    {
                        pushTarget = Mathf.Max(pushTarget, bfMax + (w.rect.fMax - w.rect.fMin));
                    }
                }
            }
            var newFMax = Mathf.Min(w.rect.fMax + currentSpeed * dt, stopClamp);
            // 先行ワーク（処理済み＝より下流）
            for (var j = 0; j < i; j++)
            {
                var d = entries[j];
                if (d.rect.OverlapL(w.rect) && (w.rect.fMin < d.rect.fMax))
                {
                    newFMax = Mathf.Min(newFMax, d.rect.fMin - cv.gap);
                }
            }
            // 前押し（プッシャー）は先行ワークのクランプを超えて適用する
            // （食い込んだ分は後段の伝搬処理で先行ワークごと前へ送る）
            if (pushTarget > newFMax)
            {
                newFMax = Mathf.Min(pushTarget, stopClamp);
            }
            var delta = newFMax - w.rect.fMax;
            if (delta != 0f)
            {
                w.obj.transform.position += fDir * delta;
                w.rect.fMin += delta;
                w.rect.fMax += delta;
            }
        }

        // 前押しの下流伝搬: 押されたワークが先行ワークへ食い込んだら、先行ワークも前へ送る（渋滞列ごと押す）。
        // 行き先がストッパーで塞がれている場合はそこで止め、押し込んだ側を戻す（ジャム）
        byF.AddRange(entries);
        byF.Sort((a, b) => a.rect.fMin.CompareTo(b.rect.fMin));
        for (var i = 0; i < byF.Count; i++)
        {
            var up = byF[i];
            for (var j = i + 1; j < byF.Count; j++)
            {
                var dn = byF[j];
                if (!up.rect.OverlapL(dn.rect))
                {
                    continue;
                }
                var pen = up.rect.fMax + cv.gap - dn.rect.fMin;
                if (pen <= 0f)
                {
                    continue;
                }
                // 下流側ワークを前へ（自身のストッパー上流面まで）
                var advance = Mathf.Min(pen, StopperLimitF(dn) - dn.rect.fMax);
                if (advance > 0f)
                {
                    dn.obj.transform.position += fDir * advance;
                    dn.rect.fMin += advance;
                    dn.rect.fMax += advance;
                    pen -= advance;
                }
                if (pen > 0f)
                {
                    // 行き止まり: 押し込んだ側を戻す（ジャム）
                    up.obj.transform.position += fDir * (-pen);
                    up.rect.fMin -= pen;
                    up.rect.fMax -= pen;
                }
            }
        }

        // 横方向の押し出し（整列ガイド→ワーク、ワーク→ワークの連鎖。数回の反復で収束させる）
        for (var iter = 0; iter < 4; iter++)
        {
            var changed = false;
            foreach (var blk in blockers)
            {
                if (!blk.valid || (blk.data.role != 1))
                {
                    continue;
                }
                foreach (var w in entries)
                {
                    // 実形状をワークの進行帯・高さ帯でクリップした支持面のl範囲で判定する（斜めガイドの膨らみ防止）
                    var off = blk.data.offset;
                    if (!ClipRangeL(blk.boxes, w.rect.fMin - off, w.rect.fMax + off, w.rect.uMin, w.rect.uMax, out var blMin, out var blMax))
                    {
                        continue;
                    }
                    blMin -= off;
                    blMax += off;
                    if (!((blMin < w.rect.lMax) && (blMax > w.rect.lMin)))
                    {
                        continue;
                    }
                    // 食い込みが小さい側へ押し出す
                    var penPlus = blMax - w.rect.lMin;    // +l側へ出すための量
                    var penMinus = w.rect.lMax - blMin;   // -l側へ出すための量
                    var push = penPlus <= penMinus ? penPlus : -penMinus;
                    MoveLateral(w, push);
                    changed = true;
                }
            }
            // ワーク同士の重なり解消（食い込みが小さい軸方向へ押す）
            for (var i = 0; i < entries.Count; i++)
            {
                for (var j = i + 1; j < entries.Count; j++)
                {
                    var a = entries[i];
                    var b = entries[j];
                    if (!a.rect.OverlapF(b.rect) || !a.rect.OverlapL(b.rect))
                    {
                        continue;
                    }
                    var fPen = Mathf.Min(a.rect.fMax, b.rect.fMax) - Mathf.Max(a.rect.fMin, b.rect.fMin);
                    var lPen = Mathf.Min(a.rect.lMax, b.rect.lMax) - Mathf.Max(a.rect.lMin, b.rect.lMin);
                    if (lPen <= fPen)
                    {
                        // 横へ半分ずつ押し分ける（ガイドと再干渉したら次の反復で補正される）
                        var sign = (a.rect.lMin + a.rect.lMax) <= (b.rect.lMin + b.rect.lMax) ? 1f : -1f;
                        MoveLateral(a, -sign * lPen * 0.5f);
                        MoveLateral(b, sign * lPen * 0.5f);
                    }
                    else
                    {
                        // 進行方向で解消（上流側を押し戻す）
                        var up = a.rect.fMax <= b.rect.fMax ? a : b;
                        up.obj.transform.position += fDir * (-fPen);
                        up.rect.fMin -= fPen;
                        up.rect.fMax -= fPen;
                    }
                    changed = true;
                }
            }
            if (!changed)
            {
                break;
            }
        }

        // 終端動作（下流端を完全に通過したワーク）
        foreach (var w in entries)
        {
            if (w.rect.fMin > region.fMax)
            {
                // 所有権を手放す（次のコンベアが拾えるように）
                WorkOwnership.Release(w.obj, this);
                // 物理落下：他コンベアがその場で受け取れる（面高さが合う）なら落とさず受け渡す
                if ((cv.endMode == 1) && !CanHandOff(w.obj))
                {
                    StartFall(w.obj);
                }
            }
        }

        // 土台ローカルの定位置を記録（次フレームの追従復元用。落下開始済みは物理に任せるため除外）
        rideAnchors.Clear();
        var invRot = Quaternion.Inverse(transform.rotation);
        foreach (var w in entries)
        {
            if (w.obj.TryGetComponent<ConveyorFallScript>(out _))
            {
                continue;
            }
            rideAnchors[w.obj] = new RideAnchor
            {
                pos = invRot * (w.obj.transform.position - transform.position),
                rot = invRot * w.obj.transform.rotation,
            };
        }

        // 確認表示（Ctrl+Shift押下中のみBacketPathOverlayがアクティブ化する）
        RenewOverlay(region);
    }

    /// <summary>
    /// フレーム軸と搬送領域を現在姿勢で構成する
    /// </summary>
    private bool BuildFrame(out FrameRect region)
    {
        region = new FrameRect();
        // 流れ方向（親空間の軸。動作設定=localPosition駆動・選択軸表示・X/Y/Z手動動作と同じ基準）
        var local = cv.axis == 0 ? Vector3.right : (cv.axis == 1 ? Vector3.up : Vector3.forward);
        var baseRot = transform.parent != null ? transform.parent.rotation : Quaternion.identity;
        var flow = (baseRot * local).normalized * cv.dir;
        // 上方向はワールド上。傾斜コンベアにも追従するよう直交化する
        uDir = Vector3.up;
        lDir = Vector3.Cross(uDir, flow);
        if (lDir.sqrMagnitude < 1e-6f)
        {
            // 流れ方向が鉛直（設定ミス）
            return false;
        }
        lDir = lDir.normalized;
        fDir = flow.normalized;
        uDir = Vector3.Cross(fDir, lDir).normalized;

        // 搬送領域：物体形状設定＞ベルト面モデル＞動作部モデル全体
        var hasRect = false;
        var source = "動作部モデル全体（境界上面。ガイド等を含むため実ベルト面より高くなりやすい）";
        if ((unit != null) && (unit.shapeSetting != null) && (unit.shapeSetting.datas != null) && (unit.shapeSetting.datas.Count > 0))
        {
            source = "物体形状設定";
            foreach (var s in unit.shapeSetting.datas)
            {
                var center = new Vector3(s.center[0], s.center[1], s.center[2]);
                var size = new Vector3(s.size[0], s.size[1], s.size[2]);
                for (var i = 0; i < 8; i++)
                {
                    var corner = center + Vector3.Scale(size * 0.5f, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    // 設定値は実寸(m・動作部モデルの姿勢基準)。親スケールを掛けない（ShapeScriptの表示と同じ規約）
                    Encapsulate(ref region, transform.position + transform.rotation * corner, ref hasRect);
                }
            }
        }
        else
        {
            if (cv.beltObject != null)
            {
                source = $"ベルト面モデル({cv.beltObject.name})";
            }
            if ((beltFilters == null) || (beltFilters.Length == 0) || (beltFilters[0] == null))
            {
                if (cv.beltObject != null)
                {
                    beltFilters = cv.beltObject.GetComponentsInChildren<MeshFilter>(true);
                }
                else
                {
                    beltFilters = GetComponentsInChildren<MeshFilter>(true);
                }
            }
            foreach (var mf in beltFilters)
            {
                if ((mf == null) || (mf.sharedMesh == null))
                {
                    continue;
                }
                var b = mf.sharedMesh.bounds;
                for (var i = 0; i < 8; i++)
                {
                    var corner = new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z);
                    Encapsulate(ref region, mf.transform.TransformPoint(corner), ref hasRect);
                }
            }
        }
        if (!hasRect && !warnedNoSurface)
        {
            warnedNoSurface = true;
            Debug.Log($"[Conveyor] {name} 搬送面が取得できません（物体形状設定またはベルト面モデルを設定してください）");
        }
        // 搬送面の高さ補正（複数コンベアの面高さ合わせ用）
        region.uMax += cv.surface;
        if (hasRect && !loggedSurface)
        {
            loggedSurface = true;
            Debug.Log($"[Conveyor] {name} 搬送面={source} 面高さ={region.uMax * 1000:F1}mm(補正{cv.surface * 1000:F1}mm込) 許容±{cv.margin * 1000:F1}mm 領域f={region.fMin * 1000:F0}..{region.fMax * 1000:F0}mm");
        }
        return hasRect;
    }

    /// <summary>
    /// ワールド点をフレーム矩形へ取り込む
    /// </summary>
    /// <summary>
    /// 削除位置等の基準フレームを返す。原点＝搬送面の「最上流(fMin)×天面(uMax)×幅中央(l中点)」。
    /// 姿勢はUnity標準の軸対応（X=横 / Y=上 / Z=流れ）で、fDir/lDir/uDir がそのまま Z/X/Y に対応する。
    /// 搬送面は物体形状設定やベルトモデルから毎フレーム算出されるため、面高さや領域を変えても追従する。
    /// 領域未算出（ロード直後・設定不備）では false を返す。
    /// </summary>
    /// <param name="pos">基準原点（ワールド）</param>
    /// <param name="rot">基準姿勢（ワールド）</param>
    /// <returns>取得できたか</returns>
    public bool TryGetSurfaceOrigin(out Vector3 pos, out Quaternion rot)
    {
        pos = Vector3.zero;
        rot = Quaternion.identity;
        if (!lastRegionValid)
        {
            return false;
        }
        // regionは fDir/lDir/uDir への絶対射影なので、正規直交基底で線形結合すれば元のワールド点に戻る
        pos = (fDir * lastRegion.fMin)
            + (uDir * lastRegion.uMax)
            + (lDir * ((lastRegion.lMin + lastRegion.lMax) * 0.5f));
        rot = Quaternion.LookRotation(fDir, uDir);
        return true;
    }

    private void Encapsulate(ref FrameRect rect, Vector3 p, ref bool has)
    {
        var f = Vector3.Dot(p, fDir);
        var l = Vector3.Dot(p, lDir);
        var u = Vector3.Dot(p, uDir);
        if (!has)
        {
            rect.fMin = rect.fMax = f;
            rect.lMin = rect.lMax = l;
            rect.uMin = rect.uMax = u;
            has = true;
            return;
        }
        rect.fMin = Mathf.Min(rect.fMin, f); rect.fMax = Mathf.Max(rect.fMax, f);
        rect.lMin = Mathf.Min(rect.lMin, l); rect.lMax = Mathf.Max(rect.lMax, l);
        rect.uMin = Mathf.Min(rect.uMin, u); rect.uMax = Mathf.Max(rect.uMax, u);
    }

    /// <summary>
    /// ワークのMeshFilterキャッシュ。
    /// GetComponentsInChildren は階層全走査＋配列アロケートを伴い、これを
    /// 「全コンベア×全アクティブワーク×物理サブステップ数」で毎フレーム呼ぶと支配的なコストになる。
    /// ワークはプール再利用で階層が固定、実行中のメッシュ差し替えも無いため使い回せる。
    /// ※複数コンベアで共有するため static
    /// </summary>
    private static readonly Dictionary<GameObject, MeshFilter[]> workFilters = new Dictionary<GameObject, MeshFilter[]>();

    /// <summary>
    /// 次にキャッシュを掃除する時刻
    /// </summary>
    private static float nextFilterPurgeTime;

    /// <summary>
    /// ワークのMeshFilterを取得する（初回のみ階層走査）
    /// </summary>
    private static MeshFilter[] GetWorkFilters(GameObject obj)
    {
        if (workFilters.TryGetValue(obj, out var filters))
        {
            return filters;
        }
        filters = obj.GetComponentsInChildren<MeshFilter>();
        workFilters[obj] = filters;
        return filters;
    }

    /// <summary>
    /// 破棄済みワークのキャッシュを定期的に捨てる（放置すると辞書が際限なく増える）
    /// </summary>
    private static void PurgeWorkFilters()
    {
        if (Time.time < nextFilterPurgeTime)
        {
            return;
        }
        nextFilterPurgeTime = Time.time + 5f;
        var stale = new List<GameObject>();
        foreach (var pair in workFilters)
        {
            if (pair.Key == null)
            {
                stale.Add(pair.Key);
            }
        }
        foreach (var key in stale)
        {
            workFilters.Remove(key);
        }
    }

    /// <summary>
    /// キャッシュを破棄する（設定再読み込み時。ワークが作り直されるため）
    /// </summary>
    public static void ClearWorkFilterCache()
    {
        workFilters.Clear();
        nextFilterPurgeTime = 0f;
    }

    /// <summary>
    /// オブジェクトのメッシュ形状からフレーム矩形を得る。
    /// レンダラのワールドAABBはワークが回転すると膨らみ（斜め45°で最大√2倍）、
    /// ターンテーブル上で判定位置がずれてワークが勝手に動く原因になるため、メッシュ実寸の8隅を使う
    /// </summary>
    private FrameRect GetWorldRect(GameObject obj)
    {
        var rect = new FrameRect();
        var has = false;
        foreach (var mf in GetWorkFilters(obj))
        {
            if ((mf == null) || (mf.sharedMesh == null))
            {
                continue;
            }
            var b = mf.sharedMesh.bounds;
            for (var i = 0; i < 8; i++)
            {
                var corner = new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z);
                Encapsulate(ref rect, mf.transform.TransformPoint(corner), ref has);
            }
        }
        return rect;
    }

    /// <summary>
    /// ストッパー/ガイドの矩形を現在姿勢で更新する。
    /// メッシュ形状から計算するため、表示/非表示の切り替えに関係なく機構として働く
    /// </summary>
    private void RenewBlockers()
    {
        foreach (var blk in blockers)
        {
            blk.valid = false;
            var go = blk.data.gameObject;
            if (go == null)
            {
                continue;
            }
            if ((blk.filters == null) || (blk.filters.Length == 0) || (blk.filters[0] == null))
            {
                blk.filters = go.GetComponentsInChildren<MeshFilter>(true);
            }
            var bt = go.transform;
            // 前フレーム姿勢を「コンベア基準の相対姿勢」から復元する。
            // ターンテーブル等でコンベアごと共回りしている間は復元姿勢=現在姿勢となり、
            // フレーム軸の回転による見かけの移動（幽霊押し）が発生しない
            var hasPrev = blk.hasPrevPose;
            var dq = Quaternion.identity;
            var recPrevPos = Vector3.zero;
            if (hasPrev)
            {
                recPrevPos = transform.position + transform.rotation * blk.prevRelPos;
                var recPrevRot = transform.rotation * blk.prevRelRot;
                dq = recPrevRot * Quaternion.Inverse(bt.rotation);
            }
            var rect = new FrameRect();
            var has = false;
            blk.boxes.Clear();
            blk.prevBoxes.Clear();
            foreach (var mf in blk.filters)
            {
                if ((mf == null) || (mf.sharedMesh == null))
                {
                    continue;
                }
                var b = mf.sharedMesh.bounds;
                var cur = new Vector3[8];
                var prv = hasPrev ? new Vector3[8] : null;
                for (var i = 0; i < 8; i++)
                {
                    var corner = new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z);
                    var world = mf.transform.TransformPoint(corner);
                    cur[i] = new Vector3(Vector3.Dot(world, fDir), Vector3.Dot(world, lDir), Vector3.Dot(world, uDir));
                    Encapsulate(ref rect, world, ref has);
                    if (hasPrev)
                    {
                        var pw = recPrevPos + dq * (world - bt.position);
                        prv[i] = new Vector3(Vector3.Dot(pw, fDir), Vector3.Dot(pw, lDir), Vector3.Dot(pw, uDir));
                    }
                }
                blk.boxes.Add(cur);
                if (hasPrev)
                {
                    blk.prevBoxes.Add(prv);
                }
            }
            if (!has)
            {
                continue;
            }
            // 接触面オフセット（+でワーク側へ広げる）※外接箱は確認表示用
            rect.fMin -= blk.data.offset; rect.fMax += blk.data.offset;
            rect.lMin -= blk.data.offset; rect.lMax += blk.data.offset;
            blk.rect = rect;
            blk.valid = true;
            // コンベア基準の相対姿勢を記録
            blk.prevRelPos = Quaternion.Inverse(transform.rotation) * (bt.position - transform.position);
            blk.prevRelRot = Quaternion.Inverse(transform.rotation) * bt.rotation;
            blk.hasPrevPose = true;
        }
    }

    /// <summary>
    /// メッシュ箱の12辺（8隅のビット順に対応）
    /// </summary>
    private static readonly int[,] boxEdges =
    {
        {0,1},{2,3},{4,5},{6,7},   // x方向
        {0,2},{1,3},{4,6},{5,7},   // y方向
        {0,4},{1,5},{2,6},{3,7},   // z方向
    };

    /// <summary>
    /// ストッパー実形状のうち、ワークの横帯(l)・高さ帯(u)に重なる部分のf範囲（支持面）を求める。
    /// 外接箱でなく形状をクリップするため、コンベアに対して斜めのストッパーでも実際の面位置でしか当たらない
    /// </summary>
    private static bool ClipRangeF(List<Vector3[]> boxes, float lMin, float lMax, float uMin, float uMax, out float fMin, out float fMax)
    {
        fMin = float.PositiveInfinity;
        fMax = float.NegativeInfinity;
        foreach (var c in boxes)
        {
            // 箱のu範囲が高さ帯と重ならなければ除外（板が上へ退避したら効かない）
            var u0 = float.PositiveInfinity;
            var u1 = float.NegativeInfinity;
            for (var i = 0; i < 8; i++)
            {
                u0 = Mathf.Min(u0, c[i].z);
                u1 = Mathf.Max(u1, c[i].z);
            }
            if ((u1 < uMin) || (u0 > uMax))
            {
                continue;
            }
            // 帯内にある頂点
            for (var i = 0; i < 8; i++)
            {
                if ((c[i].y >= lMin) && (c[i].y <= lMax))
                {
                    fMin = Mathf.Min(fMin, c[i].x);
                    fMax = Mathf.Max(fMax, c[i].x);
                }
            }
            // 帯の境界を横切る辺との交点
            for (var e = 0; e < 12; e++)
            {
                var a = c[boxEdges[e, 0]];
                var b = c[boxEdges[e, 1]];
                for (var s = 0; s < 2; s++)
                {
                    var bound = s == 0 ? lMin : lMax;
                    if ((a.y - bound) * (b.y - bound) < 0f)
                    {
                        var t = (bound - a.y) / (b.y - a.y);
                        var f = a.x + (b.x - a.x) * t;
                        fMin = Mathf.Min(fMin, f);
                        fMax = Mathf.Max(fMax, f);
                    }
                }
            }
        }
        return fMax >= fMin;
    }

    /// <summary>
    /// ガイド実形状のうち、ワークの進行帯(f)・高さ帯(u)に重なる部分のl範囲を求める（ClipRangeFの横方向版）
    /// </summary>
    private static bool ClipRangeL(List<Vector3[]> boxes, float fMin, float fMax, float uMin, float uMax, out float lMin, out float lMax)
    {
        lMin = float.PositiveInfinity;
        lMax = float.NegativeInfinity;
        foreach (var c in boxes)
        {
            var u0 = float.PositiveInfinity;
            var u1 = float.NegativeInfinity;
            for (var i = 0; i < 8; i++)
            {
                u0 = Mathf.Min(u0, c[i].z);
                u1 = Mathf.Max(u1, c[i].z);
            }
            if ((u1 < uMin) || (u0 > uMax))
            {
                continue;
            }
            for (var i = 0; i < 8; i++)
            {
                if ((c[i].x >= fMin) && (c[i].x <= fMax))
                {
                    lMin = Mathf.Min(lMin, c[i].y);
                    lMax = Mathf.Max(lMax, c[i].y);
                }
            }
            for (var e = 0; e < 12; e++)
            {
                var a = c[boxEdges[e, 0]];
                var b = c[boxEdges[e, 1]];
                for (var s = 0; s < 2; s++)
                {
                    var bound = s == 0 ? fMin : fMax;
                    if ((a.x - bound) * (b.x - bound) < 0f)
                    {
                        var t = (bound - a.x) / (b.x - a.x);
                        var l = a.y + (b.y - a.y) * t;
                        lMin = Mathf.Min(lMin, l);
                        lMax = Mathf.Max(lMax, l);
                    }
                }
            }
        }
        return lMax >= lMin;
    }

    /// <summary>
    /// ワークが堰き止められる位置（ワークの帯にかかる上流面ストッパー支持面のfMinの最小値。なければ∞）
    /// </summary>
    private float StopperLimitF(WorkEntry w)
    {
        var limit = float.PositiveInfinity;
        foreach (var blk in blockers)
        {
            if (!blk.valid || (blk.data.role != 0))
            {
                continue;
            }
            var off = blk.data.offset;
            if (!ClipRangeF(blk.boxes, w.rect.lMin - off, w.rect.lMax + off, w.rect.uMin, w.rect.uMax, out var bfMin, out var bfMax))
            {
                continue;
            }
            bfMin -= off;
            bfMax += off;
            var workCenter = (w.rect.fMin + w.rect.fMax) * 0.5f;
            if (workCenter < (bfMin + bfMax) * 0.5f)
            {
                limit = Mathf.Min(limit, bfMin);
            }
        }
        return limit;
    }

    /// <summary>
    /// 別のコンベアがその場でワークを受け取れるか（面高さ・領域が合うか）
    /// </summary>
    private bool CanHandOff(GameObject obj)
    {
        foreach (var other in conveyors)
        {
            if ((other == null) || (other == this) || !other.lastRegionValid)
            {
                continue;
            }
            if (other.TestCapture(obj))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// このコンベアの直近の搬送領域でワークを捕捉できるか（受け渡し判定用）
    /// </summary>
    private bool TestCapture(GameObject obj)
    {
        if ((cv == null) || !lastRegionValid)
        {
            return false;
        }
        var rect = GetWorldRect(obj);
        if (rect.fMax <= rect.fMin)
        {
            return false;
        }
        var planOk = rect.fMin < lastRegion.fMax && rect.fMax > lastRegion.fMin && rect.lMin < lastRegion.lMax && rect.lMax > lastRegion.lMin;
        var heightOk = Mathf.Abs(rect.uMin - lastRegion.uMax) <= cv.margin;
        return planOk && heightOk;
    }

    /// <summary>
    /// ワークを搬送管理下に置く（物理停止）
    /// </summary>
    private void Capture(GameObject obj)
    {
        if (obj.TryGetComponent<ConveyorFallScript>(out var fall))
        {
            Destroy(fall);
        }
        var rigi = obj.GetComponentInChildren<Rigidbody>();
        if ((rigi != null) && !rigi.isKinematic)
        {
            rigi.isKinematic = true;
        }
    }

    /// <summary>
    /// ワークを横方向へ動かす
    /// </summary>
    private void MoveLateral(WorkEntry w, float delta)
    {
        if (delta == 0f)
        {
            return;
        }
        w.obj.transform.position += lDir * delta;
        w.rect.lMin += delta;
        w.rect.lMax += delta;
    }

    /// <summary>
    /// 終端の物理落下を開始する（自前重力：グローバル設定に依存しない）
    /// </summary>
    private void StartFall(GameObject obj)
    {
        if (obj.TryGetComponent<ConveyorFallScript>(out _))
        {
            return;
        }
        var rigi = obj.GetComponentInChildren<Rigidbody>();
        if (rigi == null)
        {
            return;
        }
        rigi.isKinematic = false;
        rigi.useGravity = false;
        rigi.linearVelocity = fDir * currentSpeed;
        obj.AddComponent<ConveyorFallScript>();
    }

    /// <summary>
    /// 確認表示（搬送領域=緑、ストッパー=赤、ガイド=黄）。Ctrl+Shift押下中のみ表示される
    /// </summary>
    private void RenewOverlay(FrameRect region)
    {
        if (overlayRoot == null)
        {
            overlayRoot = new GameObject($"ConveyorOverlay_{name}");
            overlayRoot.SetActive(false);
            overlayRegion = CreateOverlayLine(overlayRoot.transform, new Color(0.2f, 1f, 0.3f, 1f));
            BacketPathOverlay.RegisterLine($"{name}_conveyor", overlayRoot);
        }
        if (!overlayRoot.activeSelf)
        {
            return;
        }
        // 搬送領域（搬送面の高さで描く）
        SetOverlayRect(overlayRegion, region, region.uMax);
        // ストッパー/ガイド
        foreach (var blk in blockers)
        {
            if (blk.line == null)
            {
                blk.line = CreateOverlayLine(overlayRoot.transform, blk.data.role == 0 ? new Color(1f, 0.25f, 0.2f, 1f) : new Color(1f, 0.9f, 0.2f, 1f));
            }
            blk.line.enabled = blk.valid;
            if (blk.valid)
            {
                SetOverlayRect(blk.line, blk.rect, region.uMax + 0.001f);
            }
        }
    }

    /// <summary>
    /// 確認表示ラインを生成する
    /// </summary>
    private LineRenderer CreateOverlayLine(Transform parent, Color color)
    {
        var go = new GameObject("Rect");
        go.transform.SetParent(parent, false);
        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.loop = true;
        lr.positionCount = 4;
        lr.widthMultiplier = 0.002f;
        lr.numCornerVertices = 0;
        lr.numCapVertices = 0;
        var sh = Shader.Find("Universal Render Pipeline/Unlit");
        if (sh == null)
        {
            sh = Shader.Find("Sprites/Default");
        }
        if (sh != null)
        {
            var mat = new Material(sh);
            if (mat.HasProperty("_BaseColor")) { mat.SetColor("_BaseColor", color); }
            if (mat.HasProperty("_Color")) { mat.SetColor("_Color", color); }
            lr.sharedMaterial = mat;
        }
        lr.startColor = color;
        lr.endColor = color;
        return lr;
    }

    /// <summary>
    /// フレーム矩形をラインへ反映する
    /// </summary>
    private void SetOverlayRect(LineRenderer lr, FrameRect rect, float height)
    {
        lr.SetPosition(0, fDir * rect.fMin + lDir * rect.lMin + uDir * height);
        lr.SetPosition(1, fDir * rect.fMax + lDir * rect.lMin + uDir * height);
        lr.SetPosition(2, fDir * rect.fMax + lDir * rect.lMax + uDir * height);
        lr.SetPosition(3, fDir * rect.fMin + lDir * rect.lMax + uDir * height);
    }

    /// <summary>
    /// パラメータをセットする
    /// </summary>
    /// <param name="unitSetting"></param>
    /// <param name="obj"></param>
    public override void SetParameter(UnitSetting unitSetting, object obj)
    {
        base.SetParameter(unitSetting, obj);

        unit = unitSetting;
        cv = (ConveyerSetting)obj;
        currentSpeed = 0f;
        warnedNoSurface = false;
        loggedSurface = false;
        beltFilters = null;
        captured.Clear();
        rideAnchors.Clear();
        lastRegionValid = false;

        // コンベア一覧に登録（受け渡し判定用）し、リロードで残った自分の所有権を破棄
        if (!conveyors.Contains(this))
        {
            conveyors.Add(this);
        }
        ReleaseAllOwned();

        // 速度テーブルのタグ生成
        speedTags.Clear();
        if (cv.speeds != null)
        {
            foreach (var spd in cv.speeds)
            {
                TagInfo tag = null;
                if (!string.IsNullOrEmpty(spd.tag))
                {
                    tag = ScriptableObject.CreateInstance<TagInfo>();
                    tag.Database = unitSetting.Database;
                    tag.MechId = unitSetting.mechId;
                    tag.Tag = spd.tag;
                }
                speedTags.Add(tag);
            }
        }

        // ストッパー/ガイド
        blockers.Clear();
        if (cv.blockers != null)
        {
            foreach (var blk in cv.blockers)
            {
                if (blk.gameObject == null)
                {
                    Debug.Log($"[Conveyor] {name} {ConveyorRoleName(blk.role)}「{blk.model}」のモデルが見つかりません（path={blk.path}）");
                }
                blockers.Add(new BlockerEntry { data = blk });
            }
        }

        // リロード対策：前回の確認表示を破棄
        if (overlayRoot != null)
        {
            Destroy(overlayRoot);
            overlayRoot = null;
            overlayRegion = null;
        }
    }

    /// <summary>
    /// 役割名
    /// </summary>
    private static string ConveyorRoleName(int role)
    {
        return role == 0 ? "ストッパー" : "整列ガイド";
    }

    /// <summary>
    /// キャンバス表示用データ作成
    /// </summary>
    public override void RenewCanvasValues()
    {
        base.RenewCanvasValues();
        dctDispValue["Status"] = new CanvasValue
        {
            value = isMoving ? "Run" : "Stop"
        };
        dctDispValue["Speed"] = new CanvasValue
        {
            value = currentSpeed * 1000,
            unit = "mm/sec",
            format = "0.0"
        };
    }

    /// <summary>
    /// 自分が所有しているワークの所有権を全て手放す
    /// </summary>
    private void ReleaseAllOwned()
    {
        WorkOwnership.ReleaseAll(this);
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        conveyors.Remove(this);
        ReleaseAllOwned();
        if (overlayRoot != null)
        {
            Destroy(overlayRoot);
        }
    }
}

/// <summary>
/// コンベア終端の落下ワーク用の自前重力。
/// Physics.gravityに依存せず実重力相当(9.81m/s²)を与える。
/// プール返却（非アクティブ化）で自動的に外れる。
/// </summary>
public class ConveyorFallScript : MonoBehaviour
{
    private Rigidbody rigi;

    private void Start()
    {
        rigi = GetComponentInChildren<Rigidbody>();
    }

    private void FixedUpdate()
    {
        if ((rigi != null) && !rigi.isKinematic)
        {
            rigi.AddForce(Vector3.down * 9.81f, ForceMode.Acceleration);
        }
    }

    private void OnDisable()
    {
        // プール返却時に状態を持ち越さない
        Destroy(this);
    }
}
