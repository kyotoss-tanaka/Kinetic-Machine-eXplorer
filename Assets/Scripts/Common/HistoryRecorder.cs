using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Jobs;

/// <summary>
/// 見るだけの履歴。直近 HistoryMs の見た目を記録し、TimeController の Prev/Next で過去の見た目を表示する。
/// 記録するもの：ユニットの下（と、ユニットが外で動かすスプロケット）で動いた Transform（UI・カメラ・ワークは除く）、
/// ユニットの表示、ワークの表示・親・位置（子の位置も）。動くのはユニットとして登録された物だけなので、シーン全体は見ない。
/// 表示している間は Time.timeScale を 0 にして FixedUpdate（各ユニット・内部処理・コンベア・物理）を止め、
/// シミュレーションの状態（機構が持つ一覧や所有権など）には触らない。
/// 「今」まで戻ったら記録の最後（＝見始めた時）の見た目に戻して、続きを動かす
/// </summary>
[DefaultExecutionOrder(32000)]
public class HistoryRecorder : MonoBehaviour
{
    /// <summary>
    /// 記録を残す長さ(ms)
    /// </summary>
    public const int HistoryMs = 10000;

    /// <summary>
    /// 動いた物を探す時に、1つの作業にまとめる Transform の数
    /// </summary>
    private const int ScanBatch = 256;

    /// <summary>
    /// 1つだけ（内部処理ごとではなくシーンに1つ）
    /// </summary>
    public static HistoryRecorder Instance { get; private set; }

    /// <summary>
    /// 過去を表示中か
    /// </summary>
    public bool IsViewing => viewing;

    /// <summary>
    /// 過去の表示を再生中か（今に追いついたら表示をやめて続きを動かす）
    /// </summary>
    public bool IsPlaying => viewing && playing;

    /// <summary>
    /// 表示中の時刻(ms)
    /// </summary>
    public int ViewTime => viewTime;

    /// <summary>
    /// 記録の最後の時刻(ms)＝「今」
    /// </summary>
    public int NowTime => lastTime;

    /// <summary>
    /// 戻れるか（記録がある）
    /// </summary>
    public bool CanView => built && hasRecord && (comInner != null);

    #region 記録の入れ物
    /// <summary>
    /// 位置・姿勢・大きさ（親から見た値）
    /// </summary>
    private struct Pose
    {
        public Vector3 p;
        public Quaternion r;
        public Vector3 s;

        public static Pose Of(Transform t)
        {
            return new Pose { p = t.localPosition, r = t.localRotation, s = t.localScale };
        }

        public void ApplyTo(Transform t)
        {
            t.localPosition = p;
            t.localRotation = r;
            t.localScale = s;
        }

        public bool Same(Pose o)
        {
            // Vector3/Quaternion の == は誤差を許す比較（浮動小数の揺れは変化とみなさない）
            return (p == o.p) && (r == o.r) && (s == o.s);
        }
    }

    /// <summary>
    /// ワークの状態
    /// </summary>
    private struct WorkState
    {
        public bool active;
        public Transform parent;
        public Pose pose;

        public bool Same(WorkState o)
        {
            return (active == o.active) && (parent == o.parent) && pose.Same(o.pose);
        }
    }

    /// <summary>
    /// 時刻つきの値の並び（変わった時だけ積む）。
    /// 窓より古い値は最後の1つだけ残す（窓の始まりの状態として使う）
    /// </summary>
    private class Track<T> where T : struct
    {
        private readonly List<int> times = new();
        private readonly List<T> values = new();
        private int head;

        public int Count => times.Count - head;
        public int LastTime => times[times.Count - 1];
        public T Last => values[values.Count - 1];
        public T First => values[head];

        public void Add(int time, T value)
        {
            if ((Count > 0) && (times[times.Count - 1] >= time))
            {
                // 同じ時刻（止めている間に物理で動いた等）は上書き
                values[values.Count - 1] = value;
                return;
            }
            times.Add(time);
            values.Add(value);
        }

        public void Trim(int minTime)
        {
            while ((Count >= 2) && (times[head + 1] <= minTime))
            {
                head++;
            }
            if (head >= 256)
            {
                times.RemoveRange(0, head);
                values.RemoveRange(0, head);
                head = 0;
            }
        }

        /// <summary>
        /// time 以前で最後の値（無ければ一番古い値）
        /// </summary>
        public T At(int time)
        {
            int lo = head, hi = times.Count - 1, found = -1;
            while (lo <= hi)
            {
                var mid = (lo + hi) >> 1;
                if (times[mid] <= time)
                {
                    found = mid;
                    lo = mid + 1;
                }
                else
                {
                    hi = mid - 1;
                }
            }
            return values[found < 0 ? head : found];
        }
    }

    /// <summary>
    /// ワーク1つ分の記録
    /// </summary>
    private class WorkTrack
    {
        public GameObject go;
        public readonly Track<WorkState> root = new();
        /// <summary>子（段ボールのフラップ等）。見つけた時点の子で固定</summary>
        public Transform[] children;
        public Pose[] lastChild;
        public Track<Pose>[] childTracks;
        /// <summary>表示中に描画だけ切ったレンダラ（戻す時に元へ）</summary>
        public List<Renderer> hidden;
    }
    #endregion

    /// <summary>内部処理（時刻の出どころ）</summary>
    private ComInner comInner;

    /// <summary>記録の候補を作ったか</summary>
    private bool built;
    /// <summary>候補（ユニットの下の Transform）</summary>
    private Transform[] candidateList;
    private TransformAccessArray candidates;
    private NativeArray<Vector3> lastPos;
    private NativeArray<Quaternion> lastRot;
    private NativeArray<Vector3> lastScale;
    private NativeArray<Vector3> oldPos;
    private NativeArray<Quaternion> oldRot;
    private NativeArray<Vector3> oldScale;
    private NativeArray<byte> changed;

    /// <summary>動いた Transform の記録（候補の番号→記録）</summary>
    private readonly Dictionary<int, Track<Pose>> poseTracks = new();

    /// <summary>ユニットの表示（切り替え機構など）</summary>
    private GameObject[] activeTargets;
    private bool[] lastActive;
    private Track<bool>[] activeTracks;

    /// <summary>ワークの記録</summary>
    private readonly Dictionary<GameObject, WorkTrack> workTracks = new();

    /// <summary>記録があるか</summary>
    private bool hasRecord;
    /// <summary>記録の最後の時刻</summary>
    private int lastTime;
    /// <summary>記録を始めた時刻（これより前へは戻れない）</summary>
    private int firstTime;

    /// <summary>過去を表示中か</summary>
    private bool viewing;
    /// <summary>表示中の時刻</summary>
    private int viewTime;
    /// <summary>表示前の timeScale</summary>
    private float savedTimeScale = 1f;
    /// <summary>過去の表示を再生中か</summary>
    private bool playing;
    /// <summary>再生で進めた 1ms 未満の端数</summary>
    private float playFraction;

    /// <summary>後片付け用</summary>
    private readonly List<int> removeKeys = new();
    private readonly List<GameObject> removeWorks = new();

    #region 計測（10秒ごとにログ）
    private readonly System.Diagnostics.Stopwatch swScan = new();
    private double scanMsSum;
    private double scanMsMax;
    private int scanCount;
    private float lastDiagLog = -1f;
    #endregion

    /// <summary>
    /// 内部処理から作る
    /// </summary>
    public static void Ensure(ComInner inner)
    {
        if (Instance == null)
        {
            Instance = inner.gameObject.AddComponent<HistoryRecorder>();
        }
        Instance.comInner = inner;
    }

    private void OnDestroy()
    {
        if (viewing)
        {
            ExitView();
        }
        ReleaseAll();
        if (Instance == this)
        {
            Instance = null;
        }
    }

    /// <summary>
    /// 記録（毎フレーム、全ての処理の後）
    /// </summary>
    private void LateUpdate()
    {
        if (!GlobalScript.isLoaded)
        {
            // 読み込み直し中：構成が変わるので作り直す
            if (viewing)
            {
                ExitView();
            }
            ReleaseAll();
            return;
        }
        if (!AppSettings.UseHistory)
        {
            // Application Settings で OFF：記録せず、記録の入れ物も手放す（ON にした時に作り直す）
            if (viewing)
            {
                ExitView();
            }
            if (built)
            {
                ReleaseAll();
            }
            return;
        }
        if (viewing || (comInner == null) || GlobalScript.isSystemRecorder)
        {
            return;
        }
        if (!built)
        {
            Build();
        }
        var now = comInner.time;
        if (hasRecord && (now < lastTime))
        {
            // 時刻が戻った（読み込み直し等）。記録は続かないので捨てる
            ClearRecords();
        }

        swScan.Restart();
        var prev = hasRecord ? lastTime : now;
        RecordTransforms(prev, now);
        RecordActives(prev, now);
        RecordWorks(prev, now);
        if (!hasRecord)
        {
            firstTime = now;
        }
        hasRecord = true;
        lastTime = now;
        Trim(now - HistoryMs);
        swScan.Stop();

        var ms = swScan.Elapsed.TotalMilliseconds;
        scanMsSum += ms;
        scanMsMax = System.Math.Max(scanMsMax, ms);
        scanCount++;
        LogDiag();
    }

    #region 候補の作成
    /// <summary>
    /// 記録の候補を集める（読み込み後に1回）
    /// </summary>
    private void Build()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        ReleaseAll();

        // カメラとその親（移動の台）は利用者の視点なので戻さない
        var camAncestors = new HashSet<Transform>();
        foreach (var cam in FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            for (var t = cam.transform.parent; t != null; t = t.parent)
            {
                camAncestors.Add(t);
            }
        }
        var list = new List<Transform>();
        var added = new HashSet<Transform>();
        var stack = new Stack<Transform>();
        var outside = new List<Transform>();
        var unitCount = 0;
        if (GlobalScript.unitSettings != null)
        {
            foreach (var unit in GlobalScript.unitSettings)
            {
                if ((unit == null) || (unit.unitObject == null))
                {
                    continue;
                }
                unitCount++;
                stack.Push(unit.unitObject.transform);
                if (unit.unitObject.TryGetComponent<AxisMotionBase>(out var motion))
                {
                    motion.CollectOutsideDriven(outside);
                }
            }
        }
        foreach (var t in outside)
        {
            stack.Push(t);
        }
        while (stack.Count > 0)
        {
            var t = stack.Pop();
            // ユニットが入れ子（チャック・取付先）の時に二重に数えない
            if (!added.Add(t))
            {
                continue;
            }
            // UI（RectTransform）・カメラ・ワーク（別に記録）はその下も含めて除く
            if ((t is RectTransform) || t.TryGetComponent<Camera>(out _) || t.TryGetComponent<ObjectScript>(out _))
            {
                continue;
            }
            if (!camAncestors.Contains(t))
            {
                list.Add(t);
            }
            for (var i = 0; i < t.childCount; i++)
            {
                stack.Push(t.GetChild(i));
            }
        }
        candidateList = list.ToArray();
        var n = candidateList.Length;
        candidates = new TransformAccessArray(candidateList);
        lastPos = new NativeArray<Vector3>(n, Allocator.Persistent);
        lastRot = new NativeArray<Quaternion>(n, Allocator.Persistent);
        lastScale = new NativeArray<Vector3>(n, Allocator.Persistent);
        oldPos = new NativeArray<Vector3>(n, Allocator.Persistent);
        oldRot = new NativeArray<Quaternion>(n, Allocator.Persistent);
        oldScale = new NativeArray<Vector3>(n, Allocator.Persistent);
        changed = new NativeArray<byte>(n, Allocator.Persistent);
        // 今の値を入れておく（1回目の比較で全部が変化扱いにならないように）
        ScanJobRun();

        // ユニットの表示（切り替え機構は動作オブジェクトの表示を切り替える）
        var targets = new List<GameObject>();
        if (GlobalScript.unitSettings != null)
        {
            foreach (var unit in GlobalScript.unitSettings)
            {
                if ((unit != null) && (unit.moveObject != null) && !targets.Contains(unit.moveObject))
                {
                    targets.Add(unit.moveObject);
                }
            }
        }
        activeTargets = targets.ToArray();
        lastActive = new bool[activeTargets.Length];
        activeTracks = new Track<bool>[activeTargets.Length];
        for (var i = 0; i < activeTargets.Length; i++)
        {
            lastActive[i] = activeTargets[i].activeSelf;
        }

        built = true;
        UnityEngine.Debug.Log($"[History] 記録の候補 {n}個（Transform。ユニット {unitCount}個の下＋外で動かす物 {outside.Count}個）/ 表示を見るユニット {activeTargets.Length}個 / 作成 {sw.ElapsedMilliseconds}ms / 残す長さ {HistoryMs}ms");
    }

    /// <summary>
    /// 候補の今の値を読み、前回から変わった物に印を付ける（ワーカースレッドで読み取りのみ）
    /// </summary>
    private void ScanJobRun()
    {
        var job = new ScanJob
        {
            lastPos = lastPos,
            lastRot = lastRot,
            lastScale = lastScale,
            oldPos = oldPos,
            oldRot = oldRot,
            oldScale = oldScale,
            changed = changed
        };
        job.ScheduleReadOnly(candidates, ScanBatch).Complete();
    }

    /// <summary>
    /// 候補（数万個）を毎フレーム読むので Burst でコンパイルする（Burst なしでは 1フレーム 15〜18ms かかった）
    /// </summary>
    [BurstCompile]
    private struct ScanJob : IJobParallelForTransform
    {
        public NativeArray<Vector3> lastPos;
        public NativeArray<Quaternion> lastRot;
        public NativeArray<Vector3> lastScale;
        public NativeArray<Vector3> oldPos;
        public NativeArray<Quaternion> oldRot;
        public NativeArray<Vector3> oldScale;
        public NativeArray<byte> changed;

        public void Execute(int index, TransformAccess transform)
        {
            if (!transform.isValid)
            {
                changed[index] = 0;
                return;
            }
            var p = transform.localPosition;
            var r = transform.localRotation;
            var s = transform.localScale;
            if ((p != lastPos[index]) || (r != lastRot[index]) || (s != lastScale[index]))
            {
                oldPos[index] = lastPos[index];
                oldRot[index] = lastRot[index];
                oldScale[index] = lastScale[index];
                lastPos[index] = p;
                lastRot[index] = r;
                lastScale[index] = s;
                changed[index] = 1;
            }
            else
            {
                changed[index] = 0;
            }
        }
    }
    #endregion

    #region 記録
    private void RecordTransforms(int prev, int now)
    {
        ScanJobRun();
        for (var i = 0; i < changed.Length; i++)
        {
            if (changed[i] == 0)
            {
                continue;
            }
            if (!poseTracks.TryGetValue(i, out var track))
            {
                // 初めて動いた：動く前の値を前回の時刻で入れておく
                track = new Track<Pose>();
                track.Add(prev, new Pose { p = oldPos[i], r = oldRot[i], s = oldScale[i] });
                poseTracks.Add(i, track);
            }
            track.Add(now, new Pose { p = lastPos[i], r = lastRot[i], s = lastScale[i] });
        }
    }

    private void RecordActives(int prev, int now)
    {
        for (var i = 0; i < activeTargets.Length; i++)
        {
            var go = activeTargets[i];
            if (go == null)
            {
                continue;
            }
            var active = go.activeSelf;
            if (active == lastActive[i])
            {
                continue;
            }
            if (activeTracks[i] == null)
            {
                activeTracks[i] = new Track<bool>();
                activeTracks[i].Add(prev, lastActive[i]);
            }
            activeTracks[i].Add(now, active);
            lastActive[i] = active;
        }
    }

    private void RecordWorks(int prev, int now)
    {
        // 新しく出てきたワーク
        foreach (var go in MultiObjectFactoryScript.EnumerateActiveWorks())
        {
            if (workTracks.ContainsKey(go))
            {
                continue;
            }
            var wt = new WorkTrack { go = go };
            var all = go.GetComponentsInChildren<Transform>(true);
            wt.children = new Transform[all.Length - 1];
            System.Array.Copy(all, 1, wt.children, 0, all.Length - 1);
            wt.lastChild = new Pose[wt.children.Length];
            wt.childTracks = new Track<Pose>[wt.children.Length];
            for (var c = 0; c < wt.children.Length; c++)
            {
                wt.lastChild[c] = Pose.Of(wt.children[c]);
            }
            if (prev < now)
            {
                // それまでは無かった（プールの中）
                wt.root.Add(prev, new WorkState { active = false, parent = go.transform.parent, pose = Pose.Of(go.transform) });
            }
            workTracks.Add(go, wt);
        }
        foreach (var wt in workTracks.Values)
        {
            if (wt.go == null)
            {
                continue;
            }
            var t = wt.go.transform;
            var state = new WorkState { active = wt.go.activeSelf, parent = t.parent, pose = Pose.Of(t) };
            if ((wt.root.Count == 0) || !wt.root.Last.Same(state))
            {
                wt.root.Add(now, state);
            }
            if (!state.active)
            {
                continue;
            }
            for (var c = 0; c < wt.children.Length; c++)
            {
                var child = wt.children[c];
                if (child == null)
                {
                    continue;
                }
                var pose = Pose.Of(child);
                if (pose.Same(wt.lastChild[c]))
                {
                    continue;
                }
                if (wt.childTracks[c] == null)
                {
                    wt.childTracks[c] = new Track<Pose>();
                    wt.childTracks[c].Add(prev, wt.lastChild[c]);
                }
                wt.childTracks[c].Add(now, pose);
                wt.lastChild[c] = pose;
            }
        }
    }

    /// <summary>
    /// 窓より古い記録を捨てる
    /// </summary>
    private void Trim(int minTime)
    {
        removeKeys.Clear();
        foreach (var kv in poseTracks)
        {
            kv.Value.Trim(minTime);
            if ((kv.Value.Count == 1) && (kv.Value.LastTime <= minTime))
            {
                // 窓の中で動いていない（今の値のまま）
                removeKeys.Add(kv.Key);
            }
        }
        foreach (var key in removeKeys)
        {
            poseTracks.Remove(key);
        }
        for (var i = 0; i < activeTracks.Length; i++)
        {
            activeTracks[i]?.Trim(minTime);
        }
        removeWorks.Clear();
        foreach (var kv in workTracks)
        {
            var wt = kv.Value;
            if (wt.go == null)
            {
                removeWorks.Add(kv.Key);
                continue;
            }
            wt.root.Trim(minTime);
            foreach (var ct in wt.childTracks)
            {
                ct?.Trim(minTime);
            }
            if ((wt.root.Count == 1) && !wt.root.Last.active && (wt.root.LastTime <= minTime))
            {
                // 窓の間ずっとプールの中
                removeWorks.Add(kv.Key);
            }
        }
        foreach (var key in removeWorks)
        {
            workTracks.Remove(key);
        }
    }
    #endregion

    #region 表示
    /// <summary>
    /// 過去の表示の再生（timeScale=0 でも回るよう Update と実時間で進める）
    /// </summary>
    private void Update()
    {
        if (!viewing || !playing)
        {
            return;
        }
        // 再生速度は TimeController のスライダー（内部処理の時間比率）に合わせる
        var rate = (comInner != null) ? comInner.timeRate : 1f;
        playFraction += Time.unscaledDeltaTime * 1000f * rate;
        var advance = (int)playFraction;
        playFraction -= advance;
        if (advance <= 0)
        {
            return;
        }
        viewTime += advance;
        if (viewTime >= lastTime)
        {
            // 今に追いついた：表示をやめて、そのまま続きを動かす
            ExitView();
            return;
        }
        Apply(viewTime, false);
    }

    /// <summary>
    /// 表示している時刻から再生する（一時停止を外した時）
    /// </summary>
    public void StartPlayback()
    {
        if (!viewing)
        {
            return;
        }
        playing = true;
        playFraction = 0f;
    }

    /// <summary>
    /// 再生を止める（その時刻を表示したまま）
    /// </summary>
    public void StopPlayback()
    {
        playing = false;
    }

    /// <summary>
    /// 表示する時刻を動かす（Prev は負、Next は正）。「今」まで進んだら表示をやめて続きへ戻る
    /// </summary>
    public void StepView(int deltaMs)
    {
        if (!CanView)
        {
            return;
        }
        playing = false;
        if (!viewing)
        {
            if (deltaMs >= 0)
            {
                return;
            }
            viewing = true;
            viewTime = lastTime;
            // FixedUpdate（各ユニット・内部処理・コンベア・物理）を止める
            savedTimeScale = Time.timeScale;
            Time.timeScale = 0f;
        }
        var oldest = System.Math.Max(firstTime, lastTime - HistoryMs);
        viewTime = System.Math.Clamp(viewTime + deltaMs, oldest, lastTime);
        if ((deltaMs > 0) && (viewTime >= lastTime))
        {
            ExitView();
            return;
        }
        Apply(viewTime, false);
    }

    /// <summary>
    /// 表示をやめて「今」の見た目に戻し、続きを動かせるようにする
    /// </summary>
    public void ExitView()
    {
        if (!viewing)
        {
            return;
        }
        Apply(lastTime, true);
        viewing = false;
        playing = false;
        Time.timeScale = savedTimeScale;
        // 止めている間の経過時間を内部処理の時刻に入れない
        comInner?.ResyncClock();
    }

    /// <summary>
    /// time の見た目にする（latest は「今」に戻す時。記録の最後の値を使う）
    /// </summary>
    private void Apply(int time, bool latest)
    {
        if (candidateList != null)
        {
            foreach (var kv in poseTracks)
            {
                var t = candidateList[kv.Key];
                if (t != null)
                {
                    (latest ? kv.Value.Last : kv.Value.At(time)).ApplyTo(t);
                }
            }
        }
        if (activeTracks != null)
        {
            for (var i = 0; i < activeTracks.Length; i++)
            {
                if ((activeTracks[i] == null) || (activeTargets[i] == null))
                {
                    continue;
                }
                var active = latest ? activeTracks[i].Last : activeTracks[i].At(time);
                if (activeTargets[i].activeSelf != active)
                {
                    activeTargets[i].SetActive(active);
                }
            }
        }
        foreach (var wt in workTracks.Values)
        {
            if ((wt.go == null) || (wt.root.Count == 0))
            {
                continue;
            }
            ApplyWork(wt, latest ? wt.root.Last : wt.root.At(time), time, latest);
        }
        // センサや干渉チェック（Update で動く）が表示した位置で判定できるように
        Physics.SyncTransforms();
    }

    private void ApplyWork(WorkTrack wt, WorkState state, int time, bool latest)
    {
        var live = wt.root.Last;
        var t = wt.go.transform;
        if (state.active)
        {
            SetHidden(wt, false);
            if (t.parent != state.parent)
            {
                t.SetParent(state.parent, false);
            }
            state.pose.ApplyTo(t);
            if (!wt.go.activeSelf)
            {
                // 今はプールの中だが、その時刻には有った
                wt.go.SetActive(true);
            }
            for (var c = 0; c < wt.children.Length; c++)
            {
                if ((wt.childTracks[c] != null) && (wt.children[c] != null))
                {
                    (latest ? wt.childTracks[c].Last : wt.childTracks[c].At(time)).ApplyTo(wt.children[c]);
                }
            }
        }
        else if (live.active)
        {
            // 今は有るが、その時刻には無かった。SetActive(false) は使わない
            // （落下中のワークの ConveyorFallScript が OnDisable で自分を消し、戻った後に落ちなくなるため）。描画だけ切る
            SetHidden(wt, true);
        }
        else if (wt.go.activeSelf)
        {
            // 今もその時刻も無い（表示のために出していたのを戻す）
            wt.go.SetActive(false);
            if (t.parent != state.parent)
            {
                t.SetParent(state.parent, false);
            }
            state.pose.ApplyTo(t);
        }
    }

    /// <summary>
    /// ワークの描画だけ切る／戻す
    /// </summary>
    private static void SetHidden(WorkTrack wt, bool hide)
    {
        if (hide)
        {
            if (wt.hidden != null)
            {
                return;
            }
            wt.hidden = new List<Renderer>();
            foreach (var r in wt.go.GetComponentsInChildren<Renderer>())
            {
                if (r.enabled)
                {
                    r.enabled = false;
                    wt.hidden.Add(r);
                }
            }
        }
        else if (wt.hidden != null)
        {
            foreach (var r in wt.hidden)
            {
                if (r != null)
                {
                    r.enabled = true;
                }
            }
            wt.hidden = null;
        }
    }
    #endregion

    #region 後片付け
    private void ClearRecords()
    {
        poseTracks.Clear();
        workTracks.Clear();
        if (activeTracks != null)
        {
            for (var i = 0; i < activeTracks.Length; i++)
            {
                activeTracks[i] = null;
                lastActive[i] = (activeTargets[i] != null) && activeTargets[i].activeSelf;
            }
        }
        hasRecord = false;
    }

    private void ReleaseAll()
    {
        ClearRecords();
        if (candidates.isCreated)
        {
            candidates.Dispose();
        }
        if (lastPos.IsCreated)
        {
            lastPos.Dispose();
            lastRot.Dispose();
            lastScale.Dispose();
            oldPos.Dispose();
            oldRot.Dispose();
            oldScale.Dispose();
            changed.Dispose();
        }
        candidateList = null;
        activeTargets = null;
        activeTracks = null;
        lastActive = null;
        built = false;
    }
    #endregion

    /// <summary>
    /// 10秒ごとに記録の量と時間をログへ出す
    /// </summary>
    private void LogDiag()
    {
        var t = Time.unscaledTime;
        if (lastDiagLog < 0f)
        {
            lastDiagLog = t;
            return;
        }
        if (t - lastDiagLog < 10f)
        {
            return;
        }
        lastDiagLog = t;
        long entries = 0;
        foreach (var tr in poseTracks.Values)
        {
            entries += tr.Count;
        }
        UnityEngine.Debug.Log($"[History] 記録 平均 {(scanCount > 0 ? scanMsSum / scanCount : 0):0.00}ms 最大 {scanMsMax:0.00}ms（{scanCount}フレーム）/ 動いた Transform {poseTracks.Count}個（{entries}件）/ ワーク {workTracks.Count}個 / 戻れる範囲 {lastTime - System.Math.Max(firstTime, lastTime - HistoryMs)}ms");
        scanMsSum = 0;
        scanMsMax = 0;
        scanCount = 0;
    }
}
