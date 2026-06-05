using System.Collections.Generic;
using UnityEngine;

namespace KyotoSS.TimingChart
{
    /// <summary>
    /// タイミングチャートの全コンポーネントをコードで組み立てるコントローラ。
    ///
    /// ■ リアルタイムモード
    ///   RecordSignals() から SetCylinder / SetSensor / SetMechanism でデータを記録・表示。
    ///   デバイス更新は ResetAndRegister() で任意タイミングに行う。
    ///
    /// ■ 履歴モード
    ///   SetHistoryData() で渡した履歴データを表示。リアルタイム記録は停止。
    ///   デバイス更新は SetHistoryData() の再呼び出しで任意タイミングに行う。
    ///
    /// ■ モード切り替え
    ///   開始時: SetInitialMode() で指定
    ///   途中: ツールバーボタン または SwitchMode() / SetMode() で切り替え
    /// </summary>
    public class TimeChartController : MonoBehaviour
    {
        // ----------------------------------------------------------------
        // モード定義
        // ----------------------------------------------------------------
        public enum ChartMode
        {
            Realtime,  // リアルタイム記録・表示
            History,   // 履歴データ表示（記録停止）
        }

        // ----------------------------------------------------------------
        // 履歴データ入力用クラス
        // ----------------------------------------------------------------
        public class HistorySample
        {
            public float TimeMs;
            public float Value;
            public HistorySample(float timeMs, float value) { TimeMs = timeMs; Value = value; }
        }

        public class HistoryChannel
        {
            /// <summary>チャンネル名（登録済みチャンネル名と一致）</summary>
            public string Name;
            public List<HistorySample> Samples = new List<HistorySample>();
            /// <summary>アナログチャンネルの場合 true（位置チャンネル用）</summary>
            public bool IsAnalog = false;
            public float AnalogMin = 0f;
            public float AnalogMax = 100f;
            /// <summary>PLCデバイス名（X300, Y411 など）。ラベルのサブテキストに表示</summary>
            public string DeviceName = "";
            /// <summary>true の場合、0時点の初期値補完をスキップする（SysRecReader データ等）</summary>
            public bool HasInitialValue = false;
        }

        // ----------------------------------------------------------------
        // Inspector
        // ----------------------------------------------------------------
        [Header("表示設定")]
        [SerializeField] private float channelHeight = 36f;
        [SerializeField] private float analogHeight = 56f;
        [SerializeField] private float labelWidth = 160f;
        [SerializeField] private TMPro.TMP_FontAsset font;
        [Header("開始モード")]
        [SerializeField] private ChartMode initialMode = ChartMode.Realtime;

        // ----------------------------------------------------------------
        // デバイス定義クラス
        // ----------------------------------------------------------------
        /// <summary>シリンダの1停止位置の定義</summary>
        public class CylinderPositionDef
        {
            /// <summary>位置名称（例: "原点", "中間点", "前端"）</summary>
            public string PositionName;
            /// <summary>移動指令IOチャンネル名</summary>
            public string CommandChannelName;
            /// <summary>到達確認ASチャンネル名</summary>
            public string ASChannelName;
            /// <summary>正規化位置値（0.0〜1.0）。未設定(-1)の場合は登録順から自動計算</summary>
            public float NormalizedValue = -1f;
            /// <summary>実際の位置値（終了位置）。同じ値のpositionは同一IOを共有する</summary>
            public float PosValue = float.NaN;
        }

        /// <summary>
        /// シリンダ定義。停止位置を配列で指定する。
        /// 位置値は登録順に 0.0〜1.0 へ自動正規化される。
        /// 2位置の場合: 0.0 / 1.0
        /// 3位置の場合: 0.0 / 0.5 / 1.0
        /// </summary>
        public class CylinderDef
        {
            /// <summary>シリンダ名称（キー・位置チャンネル名として使用）</summary>
            public string Name;
            /// <summary>停止位置リスト（物理的に近い順で登録）</summary>
            public List<CylinderPositionDef> Positions = new List<CylinderPositionDef>();
            /// <summary>Color.clear（デフォルト）の場合は自動でパレットから割り当て</summary>
            public Color Color = Color.clear;
        }

        public class SensorDef
        {
            public string Name;
            public string IOName;
            public Color Color = new Color(1f, 0.4f, 0.8f);
        }

        public class MechanismPosition
        {
            public string PositionName;
            public float PositionValue;
        }

        public class MechanismDef
        {
            public string Name;
            public List<MechanismPosition> Positions = new List<MechanismPosition>();
            public float MinValue = 0f;
            public float MaxValue = 100f;
            /// <summary>Color.clear（デフォルト）の場合は自動でパレットから割り当て</summary>
            public Color Color = Color.clear;
        }

        // ----------------------------------------------------------------
        // 公開プロパティ
        // ----------------------------------------------------------------
        public TimingChartRecorder Recorder { get; private set; }
        public TimingChartView View { get; private set; }
        public PositionSignalGenerator PosGen { get; private set; }
        public TimingChartDataAsset Data { get; private set; }
        public ChartMode CurrentMode { get; private set; } = ChartMode.Realtime;
        public bool IsRecording => Recorder != null && Recorder.IsRecording;

        // ----------------------------------------------------------------
        // 内部管理
        // ----------------------------------------------------------------
        private class CylinderState
        {
            public string PosName;
            public List<string> CmdNames = new List<string>(); // 各位置の指令IOチャンネル名
            public List<string> ASNames = new List<string>(); // 各位置のASチャンネル名
            public int Count => CmdNames.Count;
        }
        private Dictionary<string, CylinderState> m_Cylinders = new();
        private Dictionary<string, string> m_Sensors = new();
        private Dictionary<string, MechanismDef> m_Mechanisms = new();
        /// <summary>Mechanism デバイスの tagIn 実IO名セット（IsMechanismIOChannel 判定用）</summary>
        private HashSet<string> m_MechanismTagIns = new();
        private List<(string, List<string>)> m_PendingCylIOGroups = new();

        // 履歴モード中にリアルタイムデータを退避
        private TimingChartDataAsset m_RealtimeData = null;

        // グループ定義: key=グループ名、value=チャンネル名リスト
        private Dictionary<string, (List<string> channels, Color color)> m_Groups = new();

        // 自動カラー割り当て用（登録順にパレットから取得）
        private int m_ColorIndex = 0;  // デバイス用
        private int m_GroupColorIndex = 0;  // グループ専用
        private static readonly Color[] k_ColorPalette = new Color[]
        {
            new Color(0.20f, 0.80f, 1.00f),  // シアン
            new Color(0.20f, 1.00f, 0.40f),  // グリーン
            new Color(1.00f, 0.80f, 0.20f),  // イエロー
            new Color(1.00f, 0.40f, 0.40f),  // レッド
            new Color(0.80f, 0.40f, 1.00f),  // パープル
            new Color(1.00f, 0.60f, 0.20f),  // オレンジ
            new Color(0.40f, 0.80f, 0.80f),  // ティール
            new Color(1.00f, 0.40f, 0.80f),  // ピンク
            new Color(0.60f, 1.00f, 0.60f),  // ライトグリーン
            new Color(0.60f, 0.80f, 1.00f),  // ライトブルー
        };

        /// <summary>デバイス用：パレットから次の色を取得する</summary>
        private Color NextColor() => k_ColorPalette[m_ColorIndex++ % k_ColorPalette.Length];

        /// <summary>グループ用：パレットからオフセットして次の色を取得する（デバイス色と被りにくくする）</summary>
        private Color NextGroupColor()
        {
            // パレットの後半から割り当てることでデバイス色と差別化
            int offset = k_ColorPalette.Length / 2;
            return k_ColorPalette[(offset + m_GroupColorIndex++) % k_ColorPalette.Length];
        }

        // ----------------------------------------------------------------
        // Unity ライフサイクル
        // ----------------------------------------------------------------
        protected virtual void Awake()
        {
            // Data・各コンポーネントを生成
            Data = ScriptableObject.CreateInstance<TimingChartDataAsset>();
            Recorder = gameObject.AddComponent<TimingChartRecorder>();
            PosGen = gameObject.AddComponent<PositionSignalGenerator>();
            View = gameObject.AddComponent<TimingChartView>();

            Recorder.SetData(Data);
            PosGen.SetData(Data);

            // View の設定（AddComponent 直後・Initialize 前に全て設定）
            View.Data = Data;
            View.ChannelHeight = channelHeight;
            View.AnalogHeight = analogHeight;
            View.LabelWidth = labelWidth;
            View.Font = font;

            // ツールバーボタンのコールバック登録
            View.OnModeToggleRequested = () => SwitchMode();
            View.OnDataSwitchRequested = (isSysRec) => SwitchHistoryData(isSysRec);
            View.OnCompareRequested = (unitName) => CompareHistoryData(unitName);
            View.OnCompareUnitChanged = (unitName) => ApplyCompareOffset(unitName);
            View.OnCompareChangeIndexChanged = (idx) => SetCompareChangeIndex(idx);
        }

        protected virtual void Start()
        {
            // 開始モードを設定（Inspector の initialMode またはコードから SetInitialMode() で指定）
            CurrentMode = initialMode;

            // デバイス登録・View 初期化
            ApplyRegistration();

            // モードに応じて開始
            if (CurrentMode == ChartMode.Realtime)
            {
                Recorder.StartRecording();
            }
            // 履歴モードで開始する場合は記録しない
            // （データは SetHistoryData() で随時渡す）

            View.UpdateModeButton(CurrentMode);
        }

        protected virtual void Update()
        {
            if (CurrentMode == ChartMode.Realtime && IsRecording)
                RecordSignals();
        }

        // ----------------------------------------------------------------
        // オーバーライド用
        // ----------------------------------------------------------------
        protected virtual void RegisterDevices() { }
        protected virtual void RecordSignals() { }

        // ----------------------------------------------------------------
        // 開始モード指定 API
        // ----------------------------------------------------------------

        /// <summary>
        /// 開始モードをコードから指定する。Start() より前（Awake等）に呼ぶこと。
        /// </summary>
        public void SetInitialMode(ChartMode mode)
        {
            initialMode = mode;
        }

        /// <summary>
        /// フォントと開始モードをまとめて設定する。Start() より前に呼ぶこと。
        /// </summary>
        public void SetParameter(TMPro.TMP_FontAsset font, ChartMode mode)
        {
            this.font = font;
            initialMode = mode;
            View.Font = font;
        }

        // ----------------------------------------------------------------
        // モード切り替え API
        // ----------------------------------------------------------------

        /// <summary>現在のモードをトグルする（ツールバーボタンから呼ばれる）</summary>
        public void SwitchMode()
        {
            SetMode(CurrentMode == ChartMode.Realtime ? ChartMode.History : ChartMode.Realtime);
        }

        /// <summary>モードを指定して切り替える</summary>
        public void SetMode(ChartMode mode)
        {
            if (CurrentMode == mode) return;

            if (mode == ChartMode.History)
            {
                // リアルタイム → 履歴
                m_RealtimeData = Data;
                Recorder.StopRecording();

                // 履歴用 DataAsset を作成（チャンネル定義のみコピー・サンプルなし）
                var histData = ScriptableObject.CreateInstance<TimingChartDataAsset>();
                CopyChannelDefs(m_RealtimeData, histData);
                SwitchViewData(histData);
            }
            else
            {
                // 履歴 → リアルタイム
                SwitchViewData(m_RealtimeData);
                m_RealtimeData = null;
                Recorder.StartRecording();
            }

            CurrentMode = mode;
            View.UpdateModeButton(CurrentMode);
        }

        // ----------------------------------------------------------------
        // デバイス更新 API（任意タイミング・両モードで有効）
        // ----------------------------------------------------------------

        /// <summary>
        /// デバイス登録を含む完全リセット＆再登録。
        /// リアルタイム・履歴どちらのモードでも呼び出し可能。
        /// リアルタイムモードでは記録を再スタート。
        /// 履歴モードでは次の SetHistoryData() を待つ状態になる。
        /// </summary>
        public void ResetAndRegister()
        {
            // 現在のモードを保持したままリセット
            bool wasHistory = (CurrentMode == ChartMode.History);

            // 記録停止
            Recorder.StopRecording();

            // 退避データも含めてクリア
            Data.ClearAll();
            if (m_RealtimeData != null) m_RealtimeData.ClearAll();

            m_Cylinders.Clear();
            m_Sensors.Clear();
            m_Mechanisms.Clear();
            m_MechanismTagIns.Clear();
            m_PendingCylIOGroups.Clear();
            m_Groups.Clear();
            m_ColorIndex = 0;
            m_GroupColorIndex = 0;
            PosGen.ClearPairs();

            // 新しい Data を生成してコンポーネントに接続
            Data = ScriptableObject.CreateInstance<TimingChartDataAsset>();
            Recorder.SetData(Data);
            PosGen.SetData(Data);

            if (wasHistory)
            {
                // 履歴モードのまま再登録
                m_RealtimeData = Data;

                // Data にチャンネル定義を登録
                ApplyRegistrationToData(Data);

                // 履歴用 DataAsset を新規作成してチャンネル定義をコピー
                var histData = ScriptableObject.CreateInstance<TimingChartDataAsset>();
                CopyChannelDefs(Data, histData);

                // View を完全にリセットしてから再構築
                View.ClearGroups();
                View.RegisterCylIOGroupsFromPending(m_PendingCylIOGroups);
                foreach (var kv in m_Groups)
                    View.RegisterGroup(kv.Key, kv.Value.channels, kv.Value.color);
                View.Data = histData;
                View.Reinitialize();
                // 次の SetHistoryData() 呼び出しを待つ
            }
            else
            {
                // リアルタイムモードのまま再登録
                m_RealtimeData = null;
                View.Data = Data;
                ApplyRegistration();
                Recorder.StartRecording();
            }
        }

        /// <summary>
        /// 記録データだけクリアして再スタート（デバイス登録は維持）。
        /// リアルタイムモードのみ有効。
        /// </summary>
        public void ResetRecording()
        {
            if (CurrentMode != ChartMode.Realtime) return;
            Recorder.StopRecording();
            Data.ClearAllSamples();
            Recorder.StartRecording();
        }

        // ----------------------------------------------------------------
        // 履歴データ入力 API（任意タイミング・履歴モードに自動切り替え）
        // ----------------------------------------------------------------

        /// <summary>
        /// 履歴データを渡して表示する。
        /// 現在リアルタイムモードの場合は自動で履歴モードに切り替える。
        /// 任意のタイミングで繰り返し呼び出すことでデータを更新できる。
        /// </summary>
        public void SetHistoryData(List<HistoryChannel> channels)
        {
            // 履歴モードへ切り替え（すでに履歴モードなら何もしない）
            if (CurrentMode != ChartMode.History)
                SetMode(ChartMode.History);

            var histData = View.Data;
            histData.ClearAllSamples();

            foreach (var hch in channels)
            {
                var ch = FindChannelInData(histData, hch.Name);
                if (ch == null)
                {
                    // RegisterMechanism で登録されたデバイスのIO系チャンネル
                    // （"デバイス名_PosN_終了" / "デバイス名_PosN_開始" など）はスキップ
                    // Mechanism はアナログ位置チャンネルのみ表示する
                    if (!hch.IsAnalog && IsMechanismIOChannel(hch.Name))
                        continue;

                    // 未登録チャンネルはデバイス種別を推測して自動追加
                    var sigType = hch.IsAnalog ? SignalType.Analog : SignalType.Digital;
                    ch = histData.GetOrAddChannel(hch.Name, DeviceCategory.Other, sigType);
                    ch.Color = new Color(0.5f, 0.5f, 0.5f);

                    // チャンネル名から対応するデバイスのグループを推測して登録
                    string parentGroup = FindGroupForChannel(hch.Name);
                    if (parentGroup != null)
                    {
                        if (!m_Groups[parentGroup].channels.Contains(hch.Name))
                            m_Groups[parentGroup].channels.Add(hch.Name);
                    }
                }
                // アナログチャンネルの場合は必ず HistoryChannel の Min/Max で上書き
                if (hch.IsAnalog)
                {
                    ch.Type = SignalType.Analog;
                    ch.AnalogMin = hch.AnalogMin;
                    ch.AnalogMax = hch.AnalogMax;
                }
                // PLCデバイス名をサブラベルに設定
                if (!string.IsNullOrEmpty(hch.DeviceName))
                    ch.SubLabel = hch.DeviceName;
                foreach (var s in hch.Samples)
                    ch.AppendSample(s.TimeMs, s.Value);
            }

            // 位置チャンネルが HistoryChannel に含まれている場合（IsAnalog=true）は
            // PosGen で上書きしない。IO チャンネルのみの場合は PosGen で再生成する。
            bool hasPosChannel = channels.Exists(c => c.IsAnalog);
            if (!hasPosChannel)
            {
                PosGen.SetData(histData);
                PosGen.GenerateFromRecordedData();
                PosGen.SetData(Data);
            }

            // histData のチャンネル定義を View のグループ情報に同期
            View.ClearGroups();
            View.RegisterCylIOGroupsFromPending(m_PendingCylIOGroups);
            foreach (var kv in m_Groups)
                View.RegisterGroup(kv.Key, kv.Value.channels, kv.Value.color);

            // チャンネルが追加された可能性があるため表示を再構築
            View.RebuildChannels();
            View.FitView();
        }

        // ----------------------------------------------------------------
        // 内部処理
        // ----------------------------------------------------------------
        private void ApplyRegistration()
        {
            ApplyRegistrationToData(Data);
            View.RegisterCylIOGroupsFromPending(m_PendingCylIOGroups);
            // グループを View に登録
            View.ClearGroups();
            foreach (var kv in m_Groups)
            {
                View.RegisterGroup(kv.Key, kv.Value.channels, kv.Value.color);
            }
            View.Reinitialize();
        }

        private void ApplyRegistrationToData(TimingChartDataAsset target)
        {
            // 一時的に Data を target に向けて RegisterDevices() を実行
            var savedData = Data;
            Data = target;
            Recorder.SetData(target);
            PosGen.SetData(target);

            RegisterDevices();

            // 元に戻す（ResetAndRegister の場合は同じなので問題なし）
            Data = savedData;
            Recorder.SetData(savedData);
            PosGen.SetData(savedData);
        }

        private void SwitchViewData(TimingChartDataAsset newData)
        {
            View.Data = newData;
            View.RebuildChannels();
            View.FitView();
        }

        private static void CopyChannelDefs(TimingChartDataAsset src, TimingChartDataAsset dst)
        {
            foreach (var ch in src.Channels)
            {
                var newCh = dst.GetOrAddChannel(ch.Name, ch.Category, ch.Type);
                newCh.Color = ch.Color;
                newCh.AnalogMin = ch.AnalogMin;
                newCh.AnalogMax = ch.AnalogMax;
                newCh.SubLabel = ch.SubLabel;
                newCh.PositionLabels = ch.PositionLabels; // 参照をコピー（同一リストを共有）
            }
        }

        private static SignalChannel FindChannelInData(TimingChartDataAsset data, string name)
        {
            foreach (var ch in data.Channels)
                if (ch.Name == name) return ch;
            return null;
        }

        /// <summary>
        /// チャンネル名が Mechanism 関連のIOかどうかを判定する。
        /// ① "_PosN_終了" / "_PosN_開始" 形式 → デバイス名部分が Mechanism に登録済み
        /// ② Mechanism の tagIn として使われている実IO名（例: d_mech_pos[20].x）
        /// のいずれかであれば true を返す。
        /// </summary>
        private bool IsMechanismIOChannel(string channelName)
        {
            // ① "_Pos" を含む自動生成IO名パターン
            int posIdx = channelName.IndexOf("_Pos");
            if (posIdx >= 0)
            {
                string deviceName = channelName.Substring(0, posIdx);
                if (m_Mechanisms.ContainsKey(deviceName)) return true;
            }

            // ② Mechanism デバイスの tagIn（実IO名）として登録されているか確認
            // m_MechanismTagIns に Mechanism の全 tagIn 名を保持する
            if (m_MechanismTagIns.Contains(channelName)) return true;

            return false;
        }

        /// <summary>
        /// チャンネル名から対応するグループ名を推測する。
        /// "デバイス名_PosN_終了" や "デバイス名_PosN_開始" の形式から
        /// デバイス名を取り出し、そのデバイスが属するグループを返す。
        /// </summary>
        private string FindGroupForChannel(string channelName)
        {
            // まず既存グループのチャンネルリストに含まれているか確認
            foreach (var kv in m_Groups)
                if (kv.Value.channels.Contains(channelName)) return kv.Key;

            // "デバイス名/IO名" 形式の場合はデバイス名部分でグループを検索
            int slashIdx = channelName.IndexOf('/');
            if (slashIdx >= 0)
            {
                string deviceName = channelName.Substring(0, slashIdx);
                foreach (var kv in m_Groups)
                    if (kv.Value.channels.Contains(deviceName)) return kv.Key;
            }

            // "_Pos" で分割してデバイス名を取り出す（自動生成IO名パターン）
            int posIdx = channelName.IndexOf("_Pos");
            if (posIdx >= 0)
            {
                string deviceName = channelName.Substring(0, posIdx);
                foreach (var kv in m_Groups)
                    if (kv.Value.channels.Contains(deviceName)) return kv.Key;
            }

            return null;
        }

        // ----------------------------------------------------------------
        // デバイス登録 API（RegisterDevices() 内で呼ぶ）
        // ----------------------------------------------------------------
        /// <summary>float を辞書キーに使う際の比較クラス（誤差許容）</summary>
        private class FloatKeyComparer : IEqualityComparer<float>
        {
            public bool Equals(float x, float y) => Mathf.Approximately(x, y);
            public int GetHashCode(float obj) => Mathf.RoundToInt(obj * 1000).GetHashCode();
        }

        /// <summary>
        /// シリンダを登録する。Positions に停止位置を追加する。
        /// チャンネル登録順：位置(アナログ) → 位置0指令 → 位置0AS → 位置1指令 → 位置1AS → ...
        /// 全チャンネルに同じ色が使われる。
        /// </summary>
        protected void RegisterCylinder(CylinderDef def)
        {
            if (string.IsNullOrEmpty(def.Name))
            {
                Debug.LogWarning("[TimeChartController] RegisterCylinder: Name が空です。スキップします。RegisterDevices() でデータが設定される前に呼ばれていないか確認してください。");
                return;
            }
            if (def.Positions == null || def.Positions.Count == 0)
            {
                Debug.LogWarning($"[TimeChartController] シリンダ {def.Name} に位置が登録されていません。");
                return;
            }

            // Color が未設定（Color.clear）の場合はパレットから自動割り当て
            Color c = def.Color.a < 0.01f ? NextColor() : def.Color;

            // 位置チャンネルを先に登録
            var posCh = Data.GetOrAddChannel(def.Name, DeviceCategory.Other, SignalType.Analog);
            posCh.Color = c; posCh.AnalogMin = 0f; posCh.AnalogMax = 1f;

            var st = new CylinderState { PosName = def.Name };
            var ioNames = new List<string>();
            var posDefs = new List<PositionSignalGenerator.PositionEntry>();

            // 各停止位置のチャンネルを登録
            for (int posIdx = 0; posIdx < def.Positions.Count; posIdx++)
            {
                var pos = def.Positions[posIdx];

                // CommandChannelName が空の場合は自動生成
                string cmdName = string.IsNullOrEmpty(pos.CommandChannelName)
                    ? $"{def.Name}_Pos{posIdx + 1}_Command"
                    : pos.CommandChannelName;

                // ASChannelName が空の場合は自動生成
                string asName = string.IsNullOrEmpty(pos.ASChannelName)
                    ? $"{def.Name}_Pos{posIdx + 1}_AS"
                    : pos.ASChannelName;

                // "デバイス名/IO名" 形式でデバイスごとに独立したチャンネルを登録
                string scopedCmdName = $"{def.Name}/{cmdName}";
                string scopedAsName = $"{def.Name}/{asName}";

                pos.CommandChannelName = scopedCmdName;
                pos.ASChannelName = scopedAsName;

                var cmdCh = Data.GetOrAddChannel(scopedCmdName, DeviceCategory.Cylinder, SignalType.Digital);
                var asCh = Data.GetOrAddChannel(scopedAsName, DeviceCategory.AutoSwitch, SignalType.Digital);
                cmdCh.Color = asCh.Color = c;

                if (!st.CmdNames.Contains(scopedCmdName)) { st.CmdNames.Add(scopedCmdName); ioNames.Add(scopedCmdName); }
                if (!st.ASNames.Contains(scopedAsName)) { st.ASNames.Add(scopedAsName); ioNames.Add(scopedAsName); }

                posDefs.Add(new PositionSignalGenerator.PositionEntry
                {
                    Name = pos.PositionName,
                    CommandChannelName = scopedCmdName,
                    ASChannelName = scopedAsName,
                    NormalizedValue = pos.NormalizedValue,
                    PosValue = pos.PosValue,
                });
            }

            // PositionSignalGenerator に多位置ペアを登録（NormalizedValueがここで確定）
            PosGen.AddCylinderDef(new PositionSignalGenerator.CylinderMotionDef
            {
                PositionName = def.Name,
                Positions = posDefs,
                Color = c,
            });

            // アナログ位置チャンネルに位置名称を設定
            // RealValue（PosValue）の大小でNormValueを決定する
            {
                // まず全 pd の RealValue を収集
                var pdInfos = new List<(float normVal, float realVal, string name)>();
                foreach (var pd in posDefs)
                {
                    float realVal = float.NaN;
                    foreach (var pos in def.Positions)
                        if (pos.CommandChannelName == pd.CommandChannelName && !float.IsNaN(pos.PosValue))
                        { realVal = pos.PosValue; break; }
                    if (float.IsNaN(realVal)) continue;

                    // 同じ RealValue が既にあればスキップ
                    bool exists = false;
                    foreach (var info in pdInfos)
                        if (Mathf.Approximately(info.realVal, realVal)) { exists = true; break; }
                    if (!exists) pdInfos.Add((pd.NormalizedValue, realVal, pd.Name));
                }

                // RealValue の最小・最大から NormValue を正しく再計算
                float minR = float.MaxValue, maxR = float.MinValue;
                foreach (var info in pdInfos)
                {
                    if (info.realVal < minR) minR = info.realVal;
                    if (info.realVal > maxR) maxR = info.realVal;
                }
                bool hasRange = !Mathf.Approximately(minR, maxR);

                posCh.PositionLabels.Clear();
                foreach (var info in pdInfos)
                {
                    // NormValue: RealValue の大小を正規化（大=1=上端、小=0=下端）
                    float normVal = hasRange
                        ? Mathf.InverseLerp(minR, maxR, info.realVal)
                        : info.normVal;
                    posCh.PositionLabels.Add((normVal, info.realVal, info.name));
                }
            }

            m_Cylinders[def.Name] = st;
            m_PendingCylIOGroups.Add((def.Name, ioNames));
        }

        /// <summary>
        /// データ切り替えボタンから呼ばれる。ResetAndRegister後にSetHistoryDataを実行する。
        /// isSysRec=false: 設計値データ（HistoryDataBuilder）
        /// isSysRec=true:  レコードデータ（SysRecHistoryBuilder）
        /// </summary>
        public virtual void SwitchHistoryData(bool isSysRec)
        {
            // 比較モードのオーバーレイをクリア
            View.ClearOverlays();
            // チャンネル色を元に戻す
            if (View.Data != null && m_OriginalColors.Count > 0)
            {
                foreach (var ch in View.Data.Channels)
                    if (m_OriginalColors.TryGetValue(ch.Name, out var c)) ch.Color = c;
                m_OriginalColors.Clear();
            }
            ResetAndRegister();
            if (!isSysRec)
            {
                var channels = HistoryDataBuilder.Build(GlobalScript.timeChartDatas);
                SetHistoryData(channels);
            }
            else
            {
                var channels = SysRecHistoryBuilder.BuildFromTimeChartDatas(GlobalScript.timeChartDatas);
                SetHistoryData(channels);
            }
        }

        private List<HistoryChannel> m_CompareDesign;
        private Dictionary<string, Color> m_OriginalColors = new(); // 比較前の元の色を保存
        private List<HistoryChannel> m_CompareRecord;
        private float m_CompareDesignOffset = 0f;
        private int m_CompareChangeIndex = 1;
        private string m_CompareAlignUnit = "";

        public virtual void CompareHistoryData(string alignUnitName = "")
        {
            ResetAndRegister();
            float recordTotalMs = 0f;
            if (SysRecReader.recordDatas != null && SysRecReader.recordDatas.Count > 0)
                recordTotalMs = (float)(SysRecReader.dtEnd.Timestamp
                    - SysRecReader.dtStart.Timestamp).TotalMilliseconds;
            int cycles = 2;
            if (recordTotalMs > 0f && GlobalScript.timeChartDatas != null
                && GlobalScript.timeChartDatas.Count > 0)
            {
                float cycleMs = GlobalScript.timeChartDatas[0].cycle;
                if (cycleMs > 0f)
                    cycles = Mathf.Max(2, Mathf.CeilToInt(recordTotalMs / cycleMs));
            }
            m_CompareDesign = HistoryDataBuilder.Build(GlobalScript.timeChartDatas, cycles);
            m_CompareRecord = SysRecHistoryBuilder.BuildFromTimeChartDatas(GlobalScript.timeChartDatas);
            m_CompareDesignOffset = 0f;
            m_CompareAlignUnit = alignUnitName;
            var unitNames = new List<string>();
            foreach (var ch in m_CompareDesign)
                if (ch.IsAnalog && !unitNames.Contains(ch.Name))
                    unitNames.Add(ch.Name);
            View.UpdateCompareUnitList(unitNames);
            if (!string.IsNullOrEmpty(alignUnitName))
                m_CompareDesignOffset = CalcAlignOffset(alignUnitName, m_CompareChangeIndex);
            SetCompareData(m_CompareDesign, m_CompareRecord, m_CompareDesignOffset);
        }

        public void ApplyCompareOffset(string unitName)
        {
            if (m_CompareDesign == null || m_CompareRecord == null) return;
            m_CompareAlignUnit = unitName;
            m_CompareDesignOffset = string.IsNullOrEmpty(unitName)
                ? 0f : CalcAlignOffset(unitName, m_CompareChangeIndex);
            // オフセット後にサイクル数を再計算して設計データを更新
            RebuildDesignWithOffset();
            SetCompareData(m_CompareDesign, m_CompareRecord, m_CompareDesignOffset);
        }

        public void SetCompareChangeIndex(int index)
        {
            m_CompareChangeIndex = Mathf.Max(1, index);
            ApplyCompareOffset(m_CompareAlignUnit);
        }

        /// <summary>
        /// オフセット適用後に必要なサイクル数を再計算して設計データを再生成する。
        /// オフセットがある場合、設計データが右にずれる分だけ追加サイクルが必要。
        /// </summary>
        private void RebuildDesignWithOffset()
        {
            if (GlobalScript.timeChartDatas == null || GlobalScript.timeChartDatas.Count == 0) return;
            float cycleMs = GlobalScript.timeChartDatas[0].cycle;
            if (cycleMs <= 0f) return;

            float recordTotalMs = 0f;
            if (SysRecReader.recordDatas != null && SysRecReader.recordDatas.Count > 0)
                recordTotalMs = (float)(SysRecReader.dtEnd.Timestamp
                    - SysRecReader.dtStart.Timestamp).TotalMilliseconds;

            float neededMs = recordTotalMs + Mathf.Max(0f, m_CompareDesignOffset);
            int cycles = Mathf.Max(2, Mathf.CeilToInt(neededMs / cycleMs) + 1);


            m_CompareDesign = HistoryDataBuilder.Build(GlobalScript.timeChartDatas, cycles);

            // 再生成後に変化点数を確認
            if (!string.IsNullOrEmpty(m_CompareAlignUnit))
            {
                for (int n = 1; n <= 12; n++)
                {
                    float des = GetNthChangeMs(m_CompareDesign, m_CompareAlignUnit, n);
                    float rec = GetNthChangeMs(m_CompareRecord, m_CompareAlignUnit, n);
                }
            }
        }

        private float CalcAlignOffset(string unitName, int nth = 1)
        {
            if (m_CompareRecord == null || m_CompareDesign == null) return 0f;
            float recNth = GetNthChangeMs(m_CompareRecord, unitName, nth);
            float desNth = GetNthChangeMs(m_CompareDesign, unitName, nth);
            // recがNaN（レコードにデータなし）の場合はオフセット0
            if (float.IsNaN(desNth)) return 0f;
            if (float.IsNaN(recNth)) return 0f;
            return recNth - desNth;
        }

        private static float GetNthChangeMs(List<HistoryChannel> channels, string name, int nth)
        {
            foreach (var ch in channels)
            {
                if (ch.Name != name || ch.Samples.Count < 2) continue;
                float prev = ch.Samples[0].Value;
                int count = 0;
                for (int i = 1; i < ch.Samples.Count; i++)
                {
                    if (!Mathf.Approximately(ch.Samples[i].Value, prev))
                    {
                        count++;
                        if (count >= nth) return ch.Samples[i - 1].TimeMs; // 変化直前の時刻
                        prev = ch.Samples[i].Value;
                    }
                }
            }
            return float.NaN;
        }

        public void SetCompareData(List<HistoryChannel> designChannels,
            List<HistoryChannel> recordChannels, float designOffsetMs = 0f)
        {
            // 設計データ：オフセット適用
            var designOnly = new List<HistoryChannel>();
            foreach (var ch in designChannels)
            {
                if (Mathf.Approximately(designOffsetMs, 0f))
                { designOnly.Add(ch); }
                else
                {
                    var shifted = new HistoryChannel
                    {
                        Name = ch.Name,
                        IsAnalog = ch.IsAnalog,
                        AnalogMin = ch.AnalogMin,
                        AnalogMax = ch.AnalogMax,
                        DeviceName = ch.DeviceName,
                        HasInitialValue = ch.HasInitialValue,
                    };
                    foreach (var s in ch.Samples)
                        shifted.Samples.Add(new HistorySample(s.TimeMs + designOffsetMs, s.Value));
                    designOnly.Add(shifted);
                }
            }

            SetHistoryData(designOnly, fitView: false);

            var histData = View.Data;
            var designNames = new HashSet<string>();
            foreach (var ch in designChannels) designNames.Add(ch.Name);

            View.ClearOverlays();
            foreach (var ch in recordChannels)
            {
                if (!designNames.Contains(ch.Name)) continue;
                var baseCh = FindChannelInData(histData, ch.Name);
                if (baseCh == null) continue;

                // 元の色を保存（初回のみ）
                if (!m_OriginalColors.ContainsKey(baseCh.Name))
                    m_OriginalColors[baseCh.Name] = baseCh.Color;

                // レコードデータ（オーバーレイ）：元の色相を少しずらして明るく
                Color origColor = m_OriginalColors.TryGetValue(baseCh.Name, out var saved)
                    ? saved : baseCh.Color;
                Color.RGBToHSV(origColor, out float h, out float s, out float v);
                float newH = (h + 0.055f) % 1f;
                Color overlayColor = Color.HSVToRGB(newH, Mathf.Min(s * 1.2f, 1f), Mathf.Min(v * 1.5f, 1f));
                overlayColor = new Color(overlayColor.r, overlayColor.g, overlayColor.b, 0.85f);

                // 設計データ（ベース）：baseCh.Colorは変更せず
                // WaveformRenderer にグレイ色で描画させるためにオーバーレイとして登録
                var designOverlay = new SignalChannel
                {
                    Name = ch.Name + "_D",
                    Type = baseCh.Type,
                    Color = new Color(0.35f, 0.35f, 0.35f, 1f),
                    AnalogMin = baseCh.AnalogMin,
                    AnalogMax = baseCh.AnalogMax,
                    SubLabel = baseCh.SubLabel,
                };
                foreach (var s2 in baseCh.Samples)
                    designOverlay.AppendSample(s2.TimeMs, s2.Value);

                // ベースチャンネルのサンプルをレコードデータで置き換え、色もレコード色に
                baseCh.Color = overlayColor;
                baseCh.Samples.Clear();
                foreach (var s2 in ch.Samples)
                    baseCh.AppendSample(s2.TimeMs, s2.Value);

                // 設計データをオーバーレイ（グレイ）として登録
                View.AddOverlay(ch.Name, designOverlay);
            }

            View.ClearGroups();
            View.RegisterCylIOGroupsFromPending(m_PendingCylIOGroups);
            foreach (var kv in m_Groups)
                View.RegisterGroup(kv.Key, kv.Value.channels, kv.Value.color);
            View.RebuildChannels();
        }

        public void SetHistoryData(List<HistoryChannel> channels, bool fitView = true)
        {
            if (CurrentMode != ChartMode.History) SetMode(ChartMode.History);
            var histData = View.Data;
            histData.ClearAllSamples();
            foreach (var hch in channels)
            {
                var ch = FindChannelInData(histData, hch.Name);
                if (ch == null)
                {
                    if (!hch.IsAnalog && IsMechanismIOChannel(hch.Name)) continue;
                    var sigType = hch.IsAnalog ? SignalType.Analog : SignalType.Digital;
                    ch = histData.GetOrAddChannel(hch.Name, DeviceCategory.Other, sigType);
                    ch.Color = new Color(0.5f, 0.5f, 0.5f);
                    string parentGroup = FindGroupForChannel(hch.Name);
                    if (parentGroup != null && !m_Groups[parentGroup].channels.Contains(hch.Name))
                        m_Groups[parentGroup].channels.Add(hch.Name);
                }
                if (hch.IsAnalog) { ch.Type = SignalType.Analog; ch.AnalogMin = hch.AnalogMin; ch.AnalogMax = hch.AnalogMax; }
                if (!string.IsNullOrEmpty(hch.DeviceName)) ch.SubLabel = hch.DeviceName;
                foreach (var s in hch.Samples) ch.AppendSample(s.TimeMs, s.Value);
            }
            bool hasPosChannel = channels.Exists(c => c.IsAnalog);
            if (!hasPosChannel) { PosGen.SetData(histData); PosGen.GenerateFromRecordedData(); PosGen.SetData(Data); }
            View.ClearGroups();
            View.RegisterCylIOGroupsFromPending(m_PendingCylIOGroups);
            foreach (var kv in m_Groups) View.RegisterGroup(kv.Key, kv.Value.channels, kv.Value.color);
            View.RebuildChannels();
            if (fitView) View.FitView();
        }

        protected void RegisterSensor(SensorDef def)
        {
            Color c = def.Color.a < 0.01f ? NextColor() : def.Color;
            var ch = Data.GetOrAddChannel(def.IOName, DeviceCategory.Sensor, SignalType.Digital);
            ch.Color = c;
            m_Sensors[def.Name] = def.IOName;
        }

        protected void RegisterMechanism(MechanismDef def)
        {
            Color c = def.Color.a < 0.01f ? NextColor() : def.Color;
            var ch = Data.GetOrAddChannel(def.Name, DeviceCategory.Motor, SignalType.Analog);
            ch.Color = c; ch.AnalogMin = def.MinValue; ch.AnalogMax = def.MaxValue;
            def.Color = c;
            m_Mechanisms[def.Name] = def;

            // Mechanism の tagIn（実IO名: d_mech_pos[x].y など）を収集して非表示対象に追加
            // GlobalScript 経由でデータを参照するのはコントローラの責務ではないため
            // 呼び出し側 (MachineTimeChart) で RegisterMechanismTagIn を呼ぶ方式にする
        }

        /// <summary>
        /// Mechanism の tagIn として使われる実IO名を非表示対象に登録する。
        /// RegisterMechanism の後に呼ぶ。
        /// </summary>
        protected void RegisterMechanismTagIn(string tagInName)
        {
            if (!string.IsNullOrEmpty(tagInName))
                m_MechanismTagIns.Add(tagInName);
        }

        /// <summary>
        /// デバイスをグループ化する。RegisterDevices() 内でデバイス登録後に呼ぶ。
        /// channelNames: グループに含めるチャンネル名（シリンダ名・センサIOName・機構名など）
        /// color: Color.clear の場合はパレットから自動割り当て
        /// </summary>
        protected void RegisterGroup(string groupName, List<string> channelNames, Color color = default)
        {
            // 空文字チャンネル名を除外
            var filtered = channelNames.FindAll(ch => !string.IsNullOrEmpty(ch));

            // すでに存在する場合はチャンネルをマージ（色は変えない）
            if (m_Groups.ContainsKey(groupName))
            {
                var existing = m_Groups[groupName].channels;
                foreach (var ch in filtered)
                    if (!existing.Contains(ch)) existing.Add(ch);
            }
            else
            {
                // 新規グループのみ色を割り当て（color 未指定なら専用インデックスから取得）
                Color c = color.a < 0.01f ? NextGroupColor() : color;
                m_Groups[groupName] = (filtered, c);
            }
        }

        /// <summary>
        /// シリンダのすべてのチャンネル（位置・指令・AS）をグループに自動追加するヘルパー。
        /// </summary>
        /// <summary>
        /// デバイス名でグループに登録する。
        /// シリンダ・センサ・機構のいずれかを自動判定して全チャンネルを追加する。
        /// RegisterCylinder / RegisterSensor / RegisterMechanism の後に呼ぶこと。
        /// </summary>
        protected void RegisterGroup(string groupName, string deviceName, Color color = default)
        {
            // シリンダ
            if (m_Cylinders.TryGetValue(deviceName, out var st))
            {
                var names = new List<string> { st.PosName };
                names.AddRange(st.CmdNames);
                names.AddRange(st.ASNames);
                RegisterGroup(groupName, names, color);
                return;
            }
            // センサ
            if (m_Sensors.TryGetValue(deviceName, out var ioName))
            {
                RegisterGroup(groupName, new List<string> { ioName }, color);
                return;
            }
            // 機構
            if (m_Mechanisms.ContainsKey(deviceName))
            {
                RegisterGroup(groupName, new List<string> { deviceName }, color);
                return;
            }
            Debug.LogWarning($"[TimeChartController] RegisterGroup: '{deviceName}' 未登録（RegisterCylinder/Sensor/Mechanismより後に呼ぶこと）");
        }

        /// <summary>後方互換。RegisterGroup(groupName, deviceName) を使用してください。</summary>
        [System.Obsolete("RegisterGroup(groupName, deviceName) を使用してください。")]
        protected void RegisterCylinderGroup(string groupName, string cylinderName, Color color = default)
            => RegisterGroup(groupName, cylinderName, color);

        // ----------------------------------------------------------------
        // データ入力 API（リアルタイムモードのみ有効）
        // ----------------------------------------------------------------
        /// <summary>
        /// シリンダのデータを渡す（毎フレーム呼ぶ）。
        /// cmdStates: 各停止位置への移動指令IO状態（RegisterCylinder の Positions と同じ順序）
        /// asStates:  各停止位置の完了AS状態（同上）
        /// </summary>
        public void SetCylinder(string name, bool[] cmdStates, bool[] asStates)
        {
            if (CurrentMode != ChartMode.Realtime) return;
            if (!m_Cylinders.TryGetValue(name, out var st))
            { Debug.LogWarning($"[TimeChartController] シリンダ未登録: {name}"); return; }

            for (int i = 0; i < st.Count; i++)
            {
                bool cmd = i < cmdStates.Length && cmdStates[i];
                bool asV = i < asStates.Length && asStates[i];
                Recorder.SetDigital(st.CmdNames[i], DeviceCategory.Cylinder, cmd);
                Recorder.SetDigital(st.ASNames[i], DeviceCategory.AutoSwitch, asV);
            }
            PosGen.UpdateSignalsMulti(st.PosName, cmdStates, asStates);
        }

        /// <summary>後方互換：2位置（前進/後退）シリンダ用</summary>
        public void SetCylinder(string name, bool fwdCmd, bool fwdAS, bool bwdCmd, bool bwdAS)
        {
            SetCylinder(name,
                cmdStates: new[] { bwdCmd, fwdCmd },
                asStates: new[] { bwdAS, fwdAS });
        }

        public void SetSensor(string name, bool isOn)
        {
            if (CurrentMode != ChartMode.Realtime) return;
            if (!m_Sensors.TryGetValue(name, out var ioName))
            { Debug.LogWarning($"[TimeChartController] センサ未登録: {name}"); return; }
            Recorder.SetDigital(ioName, DeviceCategory.Sensor, isOn);
        }

        public void SetMechanism(string name, float value)
        {
            if (CurrentMode != ChartMode.Realtime) return;
            if (!m_Mechanisms.TryGetValue(name, out var def))
            { Debug.LogWarning($"[TimeChartController] 機構未登録: {name}"); return; }
            Recorder.SetAnalog(def.Name, value, def.MinValue, def.MaxValue);
        }
    }
}