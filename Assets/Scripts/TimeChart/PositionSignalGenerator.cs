using System.Collections.Generic;
using UnityEngine;

namespace KyotoSS.TimingChart
{
    /// <summary>
    /// 複数停止位置を持つシリンダの位置チャンネルを自動生成する。
    ///
    /// 各停止位置は PositionEntry として定義する。
    /// 指令IOのONエッジ → 完了ASのONで位置を確定。
    /// 位置値は 0.0〜1.0 に正規化して表示する（インデックス / (位置数-1)）。
    /// </summary>
    public class PositionSignalGenerator : MonoBehaviour
    {
        // ----------------------------------------------------------------
        // 停止位置定義
        // ----------------------------------------------------------------
        /// <summary>1つの停止位置の定義</summary>
        public class PositionEntry
        {
            /// <summary>位置名称（例: "原点", "中間点", "前端"）</summary>
            public string Name;
            /// <summary>移動指令IOチャンネル名</summary>
            public string CommandChannelName;
            /// <summary>到達確認ASチャンネル名</summary>
            public string ASChannelName;
            /// <summary>正規化位置値（0.0〜1.0）。-1 の場合は登録順から自動計算</summary>
            public float NormalizedValue = -1f;
            /// <summary>実際の位置値（mm等）。設定されていれば大小からNormalizedValueを計算</summary>
            public float PosValue = float.NaN;
        }

        /// <summary>シリンダ1軸分の定義</summary>
        public class CylinderMotionDef
        {
            /// <summary>位置チャンネル名</summary>
            public string PositionName;
            /// <summary>停止位置リスト（登録順が位置順）</summary>
            public List<PositionEntry> Positions = new List<PositionEntry>();
            public Color Color = new Color(1f, 0.8f, 0.3f);
        }

        // 後方互換用（前進/後退の2位置）
        [System.Serializable]
        public class MotionPair
        {
            public string ForwardCommandName;
            public string ForwardASName;
            public string BackwardCommandName;
            public string BackwardASName;
            public string PositionName;
            public Color Color = new Color(1f, 0.8f, 0.3f);
        }

        // ----------------------------------------------------------------
        // フィールド
        // ----------------------------------------------------------------
        [SerializeField] private TimingChartDataAsset m_Data;

        private List<CylinderMotionDef> m_Defs = new();
        private Dictionary<string, RealtimeState> m_States = new();

        private float m_StartTime = 0f;
        private float ElapsedMs => (Time.realtimeSinceStartup - m_StartTime) * 1000f;

        // ----------------------------------------------------------------
        // リアルタイム追跡状態
        // ----------------------------------------------------------------
        private class RealtimeState
        {
            public int CurrentPosIdx = 0;      // 現在の確定位置インデックス
            public int TargetPosIdx = -1;     // 動作中のターゲット位置インデックス（-1=停止中）
            public float MotionStartMs = 0f;
            public float MotionStartVal = 0f;
            public bool[] PrevCmdState;            // 前フレームの各指令IO状態
            public bool[] PrevASState;             // 前フレームの各AS状態

            public bool IsMoving => TargetPosIdx >= 0;

            public void Init(int posCount)
            {
                PrevCmdState = new bool[posCount];
                PrevASState = new bool[posCount];
            }
        }

        // ----------------------------------------------------------------
        // 初期化 API
        // ----------------------------------------------------------------
        public void SetData(TimingChartDataAsset data) => m_Data = data;

        /// <summary>多位置シリンダを登録する</summary>
        public void AddCylinderDef(CylinderMotionDef def)
        {
            // NormalizedValue が未設定の場合は PosValue の大小から計算
            // PosValue がなければ登録順から計算
            int n = def.Positions.Count;
            float minPv = float.MaxValue, maxPv = float.MinValue;
            foreach (var p in def.Positions)
                if (!float.IsNaN(p.PosValue))
                {
                    if (p.PosValue < minPv) minPv = p.PosValue;
                    if (p.PosValue > maxPv) maxPv = p.PosValue;
                }
            bool hasPvRange = minPv < float.MaxValue && !Mathf.Approximately(minPv, maxPv);

            // 毎回再計算（ResetAndRegister後も正しく動作させるため条件なしで設定）
            for (int i = 0; i < n; i++)
            {
                if (hasPvRange && !float.IsNaN(def.Positions[i].PosValue))
                    def.Positions[i].NormalizedValue = Mathf.InverseLerp(minPv, maxPv, def.Positions[i].PosValue);
                else if (def.Positions[i].NormalizedValue < 0f)
                    def.Positions[i].NormalizedValue = n <= 1 ? 0f : (float)i / (n - 1);
            }

            m_Defs.Add(def);
            var st = new RealtimeState();
            st.Init(n);
            m_States[def.PositionName] = st;
        }

        /// <summary>後方互換：前進/後退2位置ペアを登録する</summary>
        public void AddPair(MotionPair pair)
        {
            AddCylinderDef(new CylinderMotionDef
            {
                PositionName = pair.PositionName,
                Color = pair.Color,
                Positions = new List<PositionEntry>
                {
                    new PositionEntry { Name = "後退端", CommandChannelName = pair.BackwardCommandName, ASChannelName = pair.BackwardASName, NormalizedValue = 0f },
                    new PositionEntry { Name = "前進端", CommandChannelName = pair.ForwardCommandName,  ASChannelName = pair.ForwardASName,  NormalizedValue = 1f },
                }
            });
        }

        public void ClearPairs()
        {
            m_Defs.Clear();
            m_States.Clear();
            m_StartTime = Time.realtimeSinceStartup;
        }

        // ----------------------------------------------------------------
        // 初期化
        // ----------------------------------------------------------------
        private void Awake()
        {
            m_StartTime = Time.realtimeSinceStartup;
        }

        // ----------------------------------------------------------------
        // リアルタイム更新
        // ----------------------------------------------------------------

        /// <summary>
        /// 多位置シリンダのリアルタイム更新。
        /// cmdStates: 各位置への移動指令IO状態（Positions と同じ順序）
        /// asStates:  各位置の完了AS状態（Positions と同じ順序）
        /// </summary>
        public void UpdateSignalsMulti(string positionName, bool[] cmdStates, bool[] asStates)
        {
            if (m_Data == null) return;
            if (!m_States.TryGetValue(positionName, out var st)) return;
            var def = m_Defs.Find(d => d.PositionName == positionName);
            if (def == null) return;

            int n = def.Positions.Count;
            var posCh = GetOrInitAnalogChannel(def);
            float now = ElapsedMs;

            for (int i = 0; i < n; i++)
            {
                bool prevCmd = st.PrevCmdState[i];
                bool prevAS = st.PrevASState[i];
                bool curCmd = i < cmdStates.Length && cmdStates[i];
                bool curAS = i < asStates.Length && asStates[i];

                // 指令IOのONエッジ → 動作開始
                if (!prevCmd && curCmd && !st.IsMoving)
                {
                    float curVal = GetLastValue(posCh);
                    posCh.AppendSample(now, curVal);
                    st.TargetPosIdx = i;
                    st.MotionStartMs = now;
                    st.MotionStartVal = curVal;
                }

                // ASのONエッジ → 位置確定
                if (!prevAS && curAS && st.IsMoving && st.TargetPosIdx == i)
                {
                    float targetVal = def.Positions[i].NormalizedValue;
                    RemoveSamplesInRange(posCh, st.MotionStartMs, now);
                    posCh.AppendSample(st.MotionStartMs, st.MotionStartVal);
                    posCh.AppendSample(now, targetVal);
                    st.CurrentPosIdx = i;
                    st.TargetPosIdx = -1;
                }

                st.PrevCmdState[i] = curCmd;
                st.PrevASState[i] = curAS;
            }
        }

        /// <summary>後方互換：前進/後退2位置のリアルタイム更新</summary>
        public void UpdateSignals(string positionName,
                                  bool fwdCmd, bool fwdAS,
                                  bool bwdCmd, bool bwdAS)
        {
            // AddPair では [0]=後退, [1]=前進 の順で登録している
            UpdateSignalsMulti(positionName,
                cmdStates: new[] { bwdCmd, fwdCmd },
                asStates: new[] { bwdAS, fwdAS });
        }

        // ----------------------------------------------------------------
        // オフライン一括生成
        // ----------------------------------------------------------------
        public void GenerateFromRecordedData()
        {
            if (m_Data == null) return;
            foreach (var def in m_Defs)
                GenerateDef(def);
        }

        private void GenerateDef(CylinderMotionDef def)
        {
            var posCh = GetOrInitAnalogChannel(def);
            posCh.Clear();

            int n = def.Positions.Count;

            // 各位置の指令・ASチャンネルを取得
            var cmdChannels = new List<SignalChannel>();
            var asChannels = new List<SignalChannel>();
            for (int i = 0; i < n; i++)
            {
                var cmd = FindChannel(def.Positions[i].CommandChannelName);
                var asC = FindChannel(def.Positions[i].ASChannelName);
                if (cmd == null || asC == null)
                {
                    Debug.LogWarning($"[PositionSignalGenerator] チャンネルが見つかりません: {def.Positions[i].Name}");
                    return;
                }
                cmdChannels.Add(cmd);
                asChannels.Add(asC);
            }

            // 全エッジをタイムスタンプ順にマージ
            var events = new List<(float TimeMs, int PosIdx, bool IsCmd, bool IsRising)>();
            for (int i = 0; i < n; i++)
            {
                AddEdgesMulti(events, cmdChannels[i], i, isCmd: true);
                AddEdgesMulti(events, asChannels[i], i, isCmd: false);
            }
            // 同時刻は 指令 → AS の順
            events.Sort((a, b) =>
            {
                int c = a.TimeMs.CompareTo(b.TimeMs);
                if (c != 0) return c;
                if (a.IsCmd && !b.IsCmd) return -1;
                if (!a.IsCmd && b.IsCmd) return 1;
                return 0;
            });

            // ----------------------------------------------------------------
            // 初期位置を決定：記録開始時にONになっているASを確認する
            // AppendDigitalEdge は変化点のみ記録するため、最初のサンプルが
            // ON(1) なら記録開始時からその位置にいたことを意味する
            // ----------------------------------------------------------------
            float curVal = def.Positions[0].NormalizedValue;   // フォールバック
            for (int i = 0; i < n; i++)
            {
                if (asChannels[i].Samples.Count > 0 && asChannels[i].Samples[0].Value >= 0.5f)
                {
                    curVal = def.Positions[i].NormalizedValue;
                    break;
                }
            }

            // 記録全体の最大タイムスタンプ（波形を記録終了時刻まで延長するため）
            float maxT = 0f;
            foreach (var ch in m_Data.Channels)
                if (ch.Samples.Count > 0 && ch.Samples[ch.Samples.Count - 1].TimeMs > maxT)
                    maxT = ch.Samples[ch.Samples.Count - 1].TimeMs;

            // t=0 に初期位置のサンプルを追加
            posCh.AppendSample(0f, curVal);

            int targetIdx = -1;
            float startMs = 0f;
            float startVal = 0f;

            foreach (var ev in events)
            {
                if (!ev.IsRising) continue;

                if (ev.IsCmd && targetIdx < 0)
                {
                    // 指令ONエッジ → 動作開始
                    posCh.AppendSample(ev.TimeMs, curVal);
                    targetIdx = ev.PosIdx;
                    startMs = ev.TimeMs;
                    startVal = curVal;
                }
                else if (!ev.IsCmd && targetIdx == ev.PosIdx)
                {
                    // AS ONエッジ → 位置確定
                    float targetVal = def.Positions[ev.PosIdx].NormalizedValue;
                    RemoveSamplesInRange(posCh, startMs, ev.TimeMs);
                    posCh.AppendSample(startMs, startVal);
                    posCh.AppendSample(ev.TimeMs, targetVal);
                    curVal = targetVal;
                    targetIdx = -1;
                }
            }

            // 記録終了時刻まで最後の確定位置を延長
            if (maxT > 0f)
            {
                float lastT = posCh.Samples.Count > 0
                    ? posCh.Samples[posCh.Samples.Count - 1].TimeMs : 0f;
                if (maxT > lastT + 1f)
                    posCh.AppendSample(maxT, curVal);
            }
        }

        // ----------------------------------------------------------------
        // ヘルパー
        // ----------------------------------------------------------------
        private SignalChannel GetOrInitAnalogChannel(CylinderMotionDef def)
        {
            var ch = m_Data.GetOrAddChannel(def.PositionName, DeviceCategory.Other, SignalType.Analog);
            ch.Color = def.Color;
            ch.AnalogMin = 0f;
            ch.AnalogMax = 1f;
            return ch;
        }

        private SignalChannel FindChannel(string name)
            => System.Linq.Enumerable.FirstOrDefault(m_Data.Channels, c => c.Name == name);

        private static float GetLastValue(SignalChannel ch)
            => ch.Samples.Count > 0 ? ch.Samples[ch.Samples.Count - 1].Value : 0f;

        private static void RemoveSamplesInRange(SignalChannel ch, float fromMs, float toMs)
            => ch.Samples.RemoveAll(s => s.TimeMs >= fromMs && s.TimeMs <= toMs);

        private static void AddEdgesMulti(
            List<(float, int, bool, bool)> list,
            SignalChannel ch, int posIdx, bool isCmd)
        {
            for (int i = 0; i < ch.Samples.Count; i++)
            {
                float prev = i > 0 ? ch.Samples[i - 1].Value : 0f;
                float cur = ch.Samples[i].Value;
                if (prev < 0.5f && cur >= 0.5f)
                    list.Add((ch.Samples[i].TimeMs, posIdx, isCmd, true));
                else if (prev >= 0.5f && cur < 0.5f)
                    list.Add((ch.Samples[i].TimeMs, posIdx, isCmd, false));
            }
        }
    }
}