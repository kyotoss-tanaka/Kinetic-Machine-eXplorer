using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 機械全体の干渉チェック（MeshCollider を使わない軽量版）。
///
/// 方針（重い常時 MeshCollider 物理の代替）:
///  - チェック対象(a側)は「isCollision を付けたユニット」だけに絞る（相手 b 側は機械全体）。
///    ＝興味のあるユニット周辺だけ判定するので軽い＆赤くなる対象も限定される。
///  - ブロードフェーズ: Renderer.bounds(世界AABB) の重なりで「近いペア」だけ抽出（安い）。
///  - ナローフェーズ: 近いペアだけ、重なり領域に張った**一様グリッド(空間ハッシュ)**で
///    近い三角形どうしだけ SAT で交差判定（総当たり O(triA×triB) を回避）。
///  - 静止メッシュ(機械本体)のワールド座標は一度だけ計算してキャッシュ。
///  - 間引き(intervalFrames)＋1フレームの三角形テスト上限(budget・持ち越し)でフリーズ防止。
///  - ★設計上の常時接触(ホーム姿勢で既に接触しているペア)は baseline に記録して除外。
///    ＝ガイド/隣接カバー等に常に触れている部品が全部赤くなる誤検知を防ぐ。
///
/// 有効/無効は既存の実行時トグル GlobalScript.isCollision で切替（従来と同じ操作感）。
/// 干渉した Renderer は赤マテリアルにし、外れたら元に戻す。
/// 重い/取りこぼす場合は intervalFrames / maxTrianglesPerMesh / GridDim / triTestBudget で調整。
/// </summary>
public sealed class MachineInterferenceChecker : MonoBehaviour
{
    [Tooltip("判定の間引き(FixedUpdate 何回に1回)。大きいほど軽い・反応は鈍い")]
    [SerializeField] private int intervalFrames = 8;
    [Tooltip("この三角形数を超えるメッシュはナローフェーズをスキップ(重すぎ回避)。0=無制限")]
    [SerializeField] private int maxTrianglesPerMesh = 6000;
    [Tooltip("1回のチェックで実行する三角形-三角形テストの上限。超えたら中断し次回に持ち越し")]
    [SerializeField] private int triTestBudget = 30000;
    [Tooltip("1フレームで判定に使う最大時間(ms)。超えたら中断し次フレーム継続。処理落ちで固まるのを防ぐ本命ガード")]
    [SerializeField] private float maxMillisPerFrame = 4f;

    // 重なり領域に張るグリッドの分割数(軸あたり)。大きいほど枝刈りは効くがセル管理コスト増。
    private const int GridDim = 16;
    private const int GridCells = GridDim * GridDim * GridDim;
    // 三角形1つがこのセル数より広く跨る場合は「大きい三角形」として別扱い(全 a 三角形と線形比較)。
    private const int BigTriCellSpan = 40;

    private sealed class Part
    {
        public int id;              // parts 内の通し番号(チェック対象同士の二重判定回避用)
        public MeshFilter mf;
        public MeshRenderer rend;
        public bool moving;         // ワールド座標が動くか(=毎フレーム再計算)。静止本体は false
        public bool checkedMover;   // チェック対象(a側)。isCollision ユニットのみ true
        public Transform unitRoot;   // 動くユニットのルート(同一ユニット内除外用)。固定は null
        public int triCount;
        public Vector3[] localVerts; // ローカル頂点(不変・Setupで1回取得)
        public int[] tris;           // 三角形index(三角形サブメッシュのみ・Setupで1回取得。線/点は除外)
        public Vector3[] worldVerts; // ワールド頂点バッファ(再利用・毎フレーム確保しない)
        public bool worldBuilt;      // static のワールド変換済みフラグ
        public int worldPass;        // moving のワールド変換を行った pass 番号(同一 pass 内は再変換しない)
    }

    private readonly List<Part> parts = new();
    private readonly List<Part> movingParts = new();   // checkedMover のみ(a側)
    private int frameCtr;
    private bool ready;
    private int scanOffset;        // ラウンドロビン/持ち越しの開始 part
    private int resumeJ;           // 持ち越し時、開始 part の j 再開位置
    private bool warnedBudget;

    // ★設計上の常時接触(ホーム姿勢で既に接触しているペア)を除外するための基準。
    private bool baselineReady;
    private readonly HashSet<long> baseline = new();

    private Material redMat;
    private readonly Dictionary<MeshRenderer, Material> origMat = new();
    private readonly HashSet<MeshRenderer> curRed = new();
    private readonly HashSet<MeshRenderer> prevRed = new();

    // ワールド頂点は各 Part の worldVerts バッファに再利用格納。moving は pass 毎に1回だけ再変換。
    private int passId;   // CheckCore 呼び出し毎にインクリメント（moving のワールド変換キャッシュ判定用）

    // ── ナローフェーズ用グリッドのスクラッチ(使い回し) ──
    private List<int>[] cells;             // セル -> b三角形の開始インデックス(ib)一覧
    private readonly List<int> usedCells = new();   // 今ペアで触れたセル(クリア対象)
    private readonly List<int> bigB = new();        // 広く跨る b三角形(線形比較)
    private int[] visited;                 // b三角形の重複テスト回避(トークン方式)
    private int visitTok;

    /// <summary>
    /// ロード後に ParameterLoader から呼ぶ。
    /// movingRoots=全可動ユニットのルート(b側の相手候補)、checkedRoots=チェック対象(a側・isCollision かつ動作するユニット)、
    /// staticRoot=固定本体(prefabObj)。checkedRoots が空なら何もチェックしない(全機械フォールバックはしない)。
    /// </summary>
    public void Setup(List<GameObject> movingRoots, HashSet<GameObject> checkedRoots, GameObject staticRoot)
    {
        parts.Clear();
        movingParts.Clear();
        passId = 0;
        baseline.Clear();
        baselineReady = false;
        scanOffset = 0;
        resumeJ = 0;
        warnedBudget = false;
        // ★グリッド/赤状態のスクラッチもクリア（再Setup=F5等で cells を作り直すため、usedCells に古い
        //   インデックスが残ると ClearGrid で null 参照する。visited/赤状態も前セッションを持ち越さない）。
        usedCells.Clear();
        bigB.Clear();
        visited = null;
        curRed.Clear();
        prevRed.Clear();
        origMat.Clear();
        var seen = new HashSet<MeshFilter>();

        if (movingRoots != null)
        {
            foreach (var root in movingRoots)
            {
                if (root == null) { continue; }
                // チェック対象(a側)は checkedRoots のユニットのみ。それ以外は相手(b側)としてのみ登録。
                bool chk = checkedRoots != null && checkedRoots.Contains(root);
                foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
                {
                    AddPart(mf, moving: true, checkedMover: chk, unitRoot: root.transform, seen);
                }
            }
        }
        if (staticRoot != null)
        {
            foreach (var mf in staticRoot.GetComponentsInChildren<MeshFilter>(true))
            {
                AddPart(mf, moving: false, checkedMover: false, unitRoot: null, seen);
            }
        }

        cells = new List<int>[GridCells];
        redMat = (Material)Resources.Load("Materials/RedMaterial");
        ready = parts.Count > 0 && movingParts.Count > 0;
        Debug.Log($"[Interference] 部品 {parts.Count}(チェック対象 {movingParts.Count}) 軽量干渉チェック準備 ready={ready}");
        if (movingParts.Count == 0)
        {
            Debug.LogWarning("[Interference] チェック対象が0。干渉を見たいユニットに isCollision を設定してください(かつ動作 actionSetting を持つこと)。");
        }
    }

    /// <summary>基準接触を採り直す(現在姿勢を新たな「設計上の接触」基準にする)。</summary>
    public void Recalibrate()
    {
        baseline.Clear();
        baselineReady = false;
        scanOffset = 0;
        resumeJ = 0;
    }

    private void AddPart(MeshFilter mf, bool moving, bool checkedMover, Transform unitRoot, HashSet<MeshFilter> seen)
    {
        if (mf == null || mf.sharedMesh == null) { return; }
        var r = mf.GetComponent<MeshRenderer>();
        if (r == null) { return; }
        if (mf.GetComponent<LineRenderer>() != null) { return; }
        // DCS安全ゾーン等の可視化オブジェクト(巨大な半透明ボックス)は干渉対象外。
        // 巻き込むと誤検知・材質差し替え(半透明が消える)・重さの原因になる。
        for (var t = mf.transform; t != null; t = t.parent)
        {
            if (t.name == "SafetyZones") { return; }
            // ワーク操作の確認表示（Ctrl+Shift／F9調整パネル）は見せるだけの半透明表示なので干渉対象外。
            // 種類が増えても漏れないよう接頭辞でまとめて除外する
            if (t.name.StartsWith("WorkDeleteZone")) { return; }   // 削除範囲（球）
            if (t.name.StartsWith("WorkChange")) { return; }       // 変換範囲（球）／変換元・変換先の形状
            if (t.name.StartsWith("WorkCreate")) { return; }       // 生成位置の形状
            if (t.name.StartsWith("WorkAttach")) { return; }       // アタッチ範囲（球）
        }
        if (!seen.Add(mf)) { return; }
        var sm = mf.sharedMesh;
        // ★三角形サブメッシュのみから index を集める（線/点トポロジは mesh.triangles が失敗するため除外）。
        //   頂点・三角形は不変なので Setup で1回だけ取得してキャッシュ（毎フレームの mesh.vertices/triangles 確保も回避）。
        int[] tris = GatherTriangleIndices(sm);
        if (tris == null || tris.Length == 0) { return; }   // 三角形が無い(線/点/空) → 干渉対象外
        var localVerts = sm.vertices;
        var p = new Part
        {
            id = parts.Count, mf = mf, rend = r, moving = moving, checkedMover = checkedMover,
            unitRoot = unitRoot, triCount = tris.Length / 3,
            localVerts = localVerts, tris = tris, worldVerts = new Vector3[localVerts.Length],
        };
        parts.Add(p);
        if (checkedMover) { movingParts.Add(p); }
    }

    /// <summary>三角形トポロジのサブメッシュだけから index を集める（線/点は mesh.triangles が失敗するため除外）。Setup時のみ。</summary>
    private static int[] GatherTriangleIndices(Mesh mesh)
    {
        var all = new List<int>();
        var tmp = new List<int>();
        for (int s = 0; s < mesh.subMeshCount; s++)
        {
            if (mesh.GetTopology(s) != MeshTopology.Triangles) { continue; }
            mesh.GetTriangles(tmp, s);   // 該当サブメッシュの三角形index(共有頂点配列基準)
            all.AddRange(tmp);
        }
        return all.Count > 0 ? all.ToArray() : null;
    }

    // ★FixedUpdate ではなく Update で回す：重い判定が1物理ステップ(20ms)を超えると FixedUpdate は
    //   catch-up で連続実行され描画に回らず「完全フリーズ」する。Update は1描画フレーム1回なので、
    //   時間予算(maxMillisPerFrame)で打ち切れば重くても描画は回り、操作不能にならない。
    private void Update()
    {
        if (!ready) { return; }
        // ★トグルOFF中は「ベースラインも判定も」走らせない（起動時フリーズ回避）。
        //   isCollision を付けただけ(トグルOFF)で起動中に重いベースライン採取が走るのを防ぐ。
        //   仕様書 kmx_ros2/INTERFERENCE_STARTUP_FREEZE_FIX.md。
        if (!GlobalScript.isCollision)
        {
            if (curRed.Count > 0 || prevRed.Count > 0 || scanOffset != 0) { RevertAll(); }
            return;
        }
        // 初めてトグルON になった時に、現姿勢の常時接触ペアを基準採取（完了まで間引き無視で毎フレーム）。
        if (!baselineReady)
        {
            if (CheckCore(true))
            {
                baselineReady = true;
                Debug.Log($"[Interference] 基準接触 {baseline.Count} ペアを設計上の接触として除外登録。");
            }
            return;
        }
        if (intervalFrames > 1 && (frameCtr++ % intervalFrames) != 0) { return; }
        CheckCore(false);
    }

    /// <summary>
    /// 1パス(全チェック対象×相手)を予算内で進める。予算切れなら false(次フレーム継続)、完了で true。
    /// calibrate=true: 交差ペアを baseline に記録(赤くしない)。false: baseline 以外の交差を赤に。
    /// </summary>
    private bool CheckCore(bool calibrate)
    {
        bool fresh = (scanOffset == 0 && resumeJ == 0);
        int firstResume = resumeJ;
        resumeJ = 0;
        passId++;   // このパスで moving のワールド変換を1回だけ行う判定用
        if (!calibrate && fresh) { curRed.Clear(); }   // 新パス開始でのみクリア(持ち越し中は蓄積)
        int budget = triTestBudget;
        double t0 = Time.realtimeSinceStartupAsDouble;   // 1フレーム時間予算の起点
        int n = movingParts.Count;
        int partsN = parts.Count;

        for (int k = 0; k < n; k++)
        {
            int i = (scanOffset + k) % n;
            var a = movingParts[i];
            if (a.rend == null || a.mf == null) { continue; }
            var ab = a.rend.bounds;
            int jStart = (k == 0) ? firstResume : 0;
            for (int j = jStart; j < partsN; j++)
            {
                var b = parts[j];
                if (b == a || b.rend == null || b.mf == null) { continue; }
                // 同一ユニット内の部品どうしは除外(自己形状の重なりで誤検知しないため)。
                if (a.unitRoot != null && a.unitRoot == b.unitRoot) { continue; }
                // チェック対象同士は id の小さい方を a 側とする一方向だけ判定(二重判定回避)。
                if (b.checkedMover && b.id <= a.id) { continue; }
                long key = PairKey(a.id, b.id);
                if (!calibrate && baseline.Contains(key)) { continue; }   // 設計上の接触は除外
                if (!ab.Intersects(b.rend.bounds)) { continue; }

                // ★時間予算: 1フレームで maxMillisPerFrame を超えたら、このペア未処理のまま打ち切り→次フレーム継続。
                //   1回の判定が長引いて Update/描画が固まるのを防ぐ本命ガード（triTestBudget では BuildWorld 等を拾えない）。
                if ((Time.realtimeSinceStartupAsDouble - t0) * 1000.0 > maxMillisPerFrame)
                {
                    scanOffset = i;
                    resumeJ = j;   // このペアは未処理なので j から再開
                    if (!calibrate) { ApplyRed(); }
                    return false;
                }

                bool hit = MeshesIntersect(a, b, ref budget);
                if (hit)
                {
                    if (calibrate) { baseline.Add(key); }
                    else { curRed.Add(a.rend); curRed.Add(b.rend); }
                }
                if (budget <= 0)
                {
                    // 保守的に、打ち切ったペアは基準扱い(スタール/取りこぼし防止)。
                    if (calibrate && !hit) { baseline.Add(key); }
                    scanOffset = i;
                    resumeJ = j + 1;
                    if (!calibrate && !warnedBudget)
                    {
                        warnedBudget = true;
                        Debug.LogWarning($"[Interference] 三角形テスト予算({triTestBudget})超過。次フレームに継続。重い場合は intervalFrames/maxTrianglesPerMesh を調整。");
                    }
                    return false;   // 未完(次フレーム継続)。赤は完了時のみ更新(ちらつき防止)。
                }
            }
        }
        scanOffset = 0;
        resumeJ = 0;
        if (!calibrate) { ApplyRed(); }
        return true;
    }

    private static long PairKey(int x, int y)
    {
        int lo = x < y ? x : y;
        int hi = x < y ? y : x;
        return ((long)lo << 32) | (uint)hi;
    }

    /// <summary>2部品のメッシュ三角形が交差するか（重なり領域にグリッドを張って近傍だけ SAT）。</summary>
    private bool MeshesIntersect(Part a, Part b, ref int budget)
    {
        if (maxTrianglesPerMesh > 0 && (a.triCount > maxTrianglesPerMesh || b.triCount > maxTrianglesPerMesh))
        {
            return false;   // 巨大メッシュはスキップ(重すぎ回避)。必要なら分割/上限調整。
        }
        // 重なり領域(2つの bounds の交差)。この外の三角形は無視。
        Vector3 rmin = Vector3.Max(a.rend.bounds.min, b.rend.bounds.min);
        Vector3 rmax = Vector3.Min(a.rend.bounds.max, b.rend.bounds.max);

        var (va, ta) = GetWorld(a);
        var (vb, tb) = GetWorld(b);
        if (va == null || vb == null) { return false; }

        // グリッドのセルサイズ(領域の最大辺 / 分割数)。
        Vector3 rsize = rmax - rmin;
        float cs = Mathf.Max(rsize.x, Mathf.Max(rsize.y, rsize.z)) / GridDim;
        if (cs < 1e-6f) { cs = 1e-6f; }
        float inv = 1f / cs;

        // ── b の三角形(領域内)をグリッドに登録 ──
        ClearGrid();
        int bTriN = tb.Length / 3;
        EnsureVisited(bTriN);
        bool anyB = false;
        for (int ib = 0; ib < tb.Length; ib += 3)
        {
            Vector3 p0 = vb[tb[ib]], p1 = vb[tb[ib + 1]], p2 = vb[tb[ib + 2]];
            Vector3 tmin = Vector3.Min(p0, Vector3.Min(p1, p2));
            Vector3 tmax = Vector3.Max(p0, Vector3.Max(p1, p2));
            if (tmax.x < rmin.x || tmin.x > rmax.x ||
                tmax.y < rmin.y || tmin.y > rmax.y ||
                tmax.z < rmin.z || tmin.z > rmax.z) { continue; }
            anyB = true;

            int cx0 = CellClamp((tmin.x - rmin.x) * inv), cx1 = CellClamp((tmax.x - rmin.x) * inv);
            int cy0 = CellClamp((tmin.y - rmin.y) * inv), cy1 = CellClamp((tmax.y - rmin.y) * inv);
            int cz0 = CellClamp((tmin.z - rmin.z) * inv), cz1 = CellClamp((tmax.z - rmin.z) * inv);
            long span = (long)(cx1 - cx0 + 1) * (cy1 - cy0 + 1) * (cz1 - cz0 + 1);
            if (span > BigTriCellSpan)
            {
                bigB.Add(ib);   // 広く跨る三角形は別扱い(全 a と線形比較)
                continue;
            }
            for (int cx = cx0; cx <= cx1; cx++)
                for (int cy = cy0; cy <= cy1; cy++)
                    for (int cz = cz0; cz <= cz1; cz++)
                    {
                        int idx = (cx * GridDim + cy) * GridDim + cz;
                        var list = cells[idx];
                        if (list == null) { list = cells[idx] = new List<int>(); }
                        if (list.Count == 0) { usedCells.Add(idx); }
                        list.Add(ib);
                    }
        }
        if (!anyB && bigB.Count == 0) { return false; }

        // ── a の三角形(領域内)で近傍セルの b 三角形だけテスト ──
        for (int ia = 0; ia < ta.Length; ia += 3)
        {
            Vector3 a0 = va[ta[ia]], a1 = va[ta[ia + 1]], a2 = va[ta[ia + 2]];
            Vector3 tmin = Vector3.Min(a0, Vector3.Min(a1, a2));
            Vector3 tmax = Vector3.Max(a0, Vector3.Max(a1, a2));
            if (tmax.x < rmin.x || tmin.x > rmax.x ||
                tmax.y < rmin.y || tmin.y > rmax.y ||
                tmax.z < rmin.z || tmin.z > rmax.z) { continue; }

            int tok = NextToken();

            // 広い b 三角形(bigB)は毎回テスト。
            for (int t = 0; t < bigB.Count; t++)
            {
                int ib = bigB[t];
                visited[ib / 3] = tok;
                Vector3 b0 = vb[tb[ib]], b1 = vb[tb[ib + 1]], b2 = vb[tb[ib + 2]];
                if (AabbOverlap(tmin, tmax, b0, b1, b2))
                {
                    if (--budget <= 0) { return false; }
                    if (TriTri(a0, a1, a2, b0, b1, b2)) { return true; }
                }
            }

            int cx0 = CellClamp((tmin.x - rmin.x) * inv), cx1 = CellClamp((tmax.x - rmin.x) * inv);
            int cy0 = CellClamp((tmin.y - rmin.y) * inv), cy1 = CellClamp((tmax.y - rmin.y) * inv);
            int cz0 = CellClamp((tmin.z - rmin.z) * inv), cz1 = CellClamp((tmax.z - rmin.z) * inv);
            for (int cx = cx0; cx <= cx1; cx++)
                for (int cy = cy0; cy <= cy1; cy++)
                    for (int cz = cz0; cz <= cz1; cz++)
                    {
                        var list = cells[(cx * GridDim + cy) * GridDim + cz];
                        if (list == null) { continue; }
                        for (int t = 0; t < list.Count; t++)
                        {
                            int ib = list[t];
                            int bi = ib / 3;
                            if (visited[bi] == tok) { continue; }  // このa三角形で判定済み
                            visited[bi] = tok;
                            Vector3 b0 = vb[tb[ib]], b1 = vb[tb[ib + 1]], b2 = vb[tb[ib + 2]];
                            if (!AabbOverlap(tmin, tmax, b0, b1, b2)) { continue; }
                            if (--budget <= 0) { return false; }
                            if (TriTri(a0, a1, a2, b0, b1, b2)) { return true; }
                        }
                    }
        }
        return false;
    }

    private static int CellClamp(float f)
    {
        int c = (int)f;
        if (c < 0) { return 0; }
        if (c >= GridDim) { return GridDim - 1; }
        return c;
    }

    private void ClearGrid()
    {
        for (int i = 0; i < usedCells.Count; i++) { cells[usedCells[i]].Clear(); }
        usedCells.Clear();
        bigB.Clear();
    }

    private void EnsureVisited(int triN)
    {
        if (visited == null || visited.Length < triN)
        {
            visited = new int[Mathf.Max(triN, 256)];
            visitTok = 0;
        }
    }

    private int NextToken()
    {
        visitTok++;
        if (visitTok == int.MaxValue)
        {
            System.Array.Clear(visited, 0, visited.Length);
            visitTok = 1;
        }
        return visitTok;
    }

    /// <summary>
    /// キャッシュ済みローカル頂点をワールドへ変換して返す(＋三角形)。バッファ(worldVerts)再利用で毎フレーム確保しない。
    /// static は一度だけ変換して保持、moving は pass 毎に1回だけ再変換。mesh.vertices/triangles は呼ばない(線/点で失敗しない・GCなし)。
    /// </summary>
    private (Vector3[] verts, int[] tris) GetWorld(Part p)
    {
        if (p.localVerts == null || p.tris == null || p.rend == null) { return (null, null); }
        // static: 動かない前提で1回のみ変換。moving: このパスでまだなら再変換。
        if (p.moving ? (p.worldPass != passId) : !p.worldBuilt)
        {
            var m = p.rend.localToWorldMatrix;
            var lv = p.localVerts;
            var wv = p.worldVerts;
            for (int i = 0; i < lv.Length; i++) { wv[i] = m.MultiplyPoint3x4(lv[i]); }
            if (p.moving) { p.worldPass = passId; } else { p.worldBuilt = true; }
        }
        return (p.worldVerts, p.tris);
    }

    private static bool AabbOverlap(Vector3 amin, Vector3 amax, Vector3 b0, Vector3 b1, Vector3 b2)
    {
        Vector3 bmin = Vector3.Min(b0, Vector3.Min(b1, b2));
        Vector3 bmax = Vector3.Max(b0, Vector3.Max(b1, b2));
        return amax.x >= bmin.x && bmax.x >= amin.x
            && amax.y >= bmin.y && bmax.y >= amin.y
            && amax.z >= bmin.z && bmax.z >= amin.z;
    }

    // ── 三角形-三角形 交差(SAT・11軸) ──────────────────────
    //   面法線2軸＋エッジ外積9軸。配列を確保しないようインライン展開(ホットループのGC回避)。
    private static bool TriTri(Vector3 a0, Vector3 a1, Vector3 a2, Vector3 b0, Vector3 b1, Vector3 b2)
    {
        Vector3 ea0 = a1 - a0, ea1 = a2 - a1, ea2 = a0 - a2;
        Vector3 eb0 = b1 - b0, eb1 = b2 - b1, eb2 = b0 - b2;

        if (Separated(Vector3.Cross(ea0, ea1), a0, a1, a2, b0, b1, b2)) { return false; }   // 面A
        if (Separated(Vector3.Cross(eb0, eb1), a0, a1, a2, b0, b1, b2)) { return false; }   // 面B

        if (Separated(Vector3.Cross(ea0, eb0), a0, a1, a2, b0, b1, b2)) { return false; }
        if (Separated(Vector3.Cross(ea0, eb1), a0, a1, a2, b0, b1, b2)) { return false; }
        if (Separated(Vector3.Cross(ea0, eb2), a0, a1, a2, b0, b1, b2)) { return false; }
        if (Separated(Vector3.Cross(ea1, eb0), a0, a1, a2, b0, b1, b2)) { return false; }
        if (Separated(Vector3.Cross(ea1, eb1), a0, a1, a2, b0, b1, b2)) { return false; }
        if (Separated(Vector3.Cross(ea1, eb2), a0, a1, a2, b0, b1, b2)) { return false; }
        if (Separated(Vector3.Cross(ea2, eb0), a0, a1, a2, b0, b1, b2)) { return false; }
        if (Separated(Vector3.Cross(ea2, eb1), a0, a1, a2, b0, b1, b2)) { return false; }
        if (Separated(Vector3.Cross(ea2, eb2), a0, a1, a2, b0, b1, b2)) { return false; }
        return true;   // どの軸でも分離できない＝交差
    }

    /// <summary>軸 axis 上で2三角形が分離しているか。分離していれば true(＝交差しない)。</summary>
    private static bool Separated(Vector3 axis, Vector3 a0, Vector3 a1, Vector3 a2, Vector3 b0, Vector3 b1, Vector3 b2)
    {
        float len2 = axis.sqrMagnitude;
        if (len2 < 1e-12f) { return false; }   // 退化軸は判定不能＝分離とみなさない
        float pa0 = Vector3.Dot(axis, a0), pa1 = Vector3.Dot(axis, a1), pa2 = Vector3.Dot(axis, a2);
        float pb0 = Vector3.Dot(axis, b0), pb1 = Vector3.Dot(axis, b1), pb2 = Vector3.Dot(axis, b2);
        float aMin = Mathf.Min(pa0, Mathf.Min(pa1, pa2));
        float aMax = Mathf.Max(pa0, Mathf.Max(pa1, pa2));
        float bMin = Mathf.Min(pb0, Mathf.Min(pb1, pb2));
        float bMax = Mathf.Max(pb0, Mathf.Max(pb1, pb2));
        return aMax < bMin || bMax < aMin;   // 重ならなければ分離
    }

    // ── 赤/復帰 ──────────────────────
    private void ApplyRed()
    {
        // [診断・一時] 干渉検出数の変化と redMat の有無をログ（確認後に削除）。
        //   検出0のまま→未検出(ベースライン除外/対象漏れ)。数>0だが赤くならない→redMat未ロード等。
        if (curRed.Count != prevRed.Count)
        {
            Debug.Log($"[Interference] 干渉 renderer 数 {prevRed.Count}→{curRed.Count} (redMat={(redMat != null)}, baseline={baseline.Count})");
        }
        // 今回赤 → 赤マテリアルに(元を保存)。
        foreach (var r in curRed)
        {
            if (r == null) { continue; }
            if (!origMat.ContainsKey(r)) { origMat[r] = r.sharedMaterial; }
            if (redMat != null) { r.sharedMaterial = redMat; }
        }
        // 前回赤で今回外れた → 元に戻す。
        foreach (var r in prevRed)
        {
            if (r == null || curRed.Contains(r)) { continue; }
            if (origMat.TryGetValue(r, out var m)) { r.sharedMaterial = m; origMat.Remove(r); }
        }
        prevRed.Clear();
        foreach (var r in curRed) { prevRed.Add(r); }
    }

    private void RevertAll()
    {
        foreach (var kv in origMat)
        {
            if (kv.Key != null) { kv.Key.sharedMaterial = kv.Value; }
        }
        origMat.Clear();
        curRed.Clear();
        prevRed.Clear();
        scanOffset = 0;
        resumeJ = 0;
    }
}
