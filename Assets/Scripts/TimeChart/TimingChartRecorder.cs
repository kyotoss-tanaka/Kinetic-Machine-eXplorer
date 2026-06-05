using UnityEngine;

namespace KyotoSS.TimingChart
{
    /// <summary>
    /// 制御スクリプトからタイミングチャートにリアルタイムでデータを送る。
    /// SetData() でコードから DataAsset を渡せる。
    /// Inspector の Data フィールドへの直接設定も引き続き可能。
    /// </summary>
    public class TimingChartRecorder : MonoBehaviour
    {
        [SerializeField] private TimingChartDataAsset m_Data;
        [SerializeField] private bool m_RecordOnStart = false; // コード制御時は false 推奨

        private bool  m_IsRecording = false;
        private float m_StartTime   = 0f;

        public bool  IsRecording => m_IsRecording;
        public float ElapsedMs   => (Time.realtimeSinceStartup - m_StartTime) * 1000f;

        // ---- DataAsset をコードから設定 ----
        /// <summary>
        /// DataAsset をコードから渡す。
        /// Awake() または AddComponent 直後に呼ぶこと。
        /// </summary>
        public void SetData(TimingChartDataAsset data)
        {
            m_Data = data;
        }

        private void Start()
        {
            if (m_Data == null)
            {
                Debug.LogWarning("[TimingChartRecorder] DataAsset 未設定。SetData() を呼ぶか Inspector で設定してください。");
                return;
            }
            if (m_RecordOnStart) StartRecording();
        }

        // ---- 記録制御 ----
        public void StartRecording()
        {
            if (m_Data == null) { Debug.LogWarning("[TimingChartRecorder] DataAsset 未設定"); return; }
            m_StartTime   = Time.realtimeSinceStartup;
            m_IsRecording = true;
            m_Data.ClearAllSamples();
        }

        public void StopRecording() => m_IsRecording = false;

        // ---- データ追記 API ----

        /// <summary>シリンダ・オートスイッチ・センサなどデジタル変化点を記録</summary>
        public void SetDigital(string name, DeviceCategory category, bool isOn)
        {
            if (!m_IsRecording || m_Data == null) return;
            m_Data.GetOrAddChannel(name, category, SignalType.Digital)
                  .AppendDigitalEdge(ElapsedMs, isOn);
        }

        /// <summary>モータ位置などアナログ値を記録（Update() から毎フレーム呼んでも可）</summary>
        public void SetAnalog(string name, float value, float minVal = 0f, float maxVal = 100f)
        {
            if (!m_IsRecording || m_Data == null) return;
            var ch = m_Data.GetOrAddChannel(name, DeviceCategory.Motor, SignalType.Analog);
            ch.AnalogMin = minVal;
            ch.AnalogMax = maxVal;
            ch.AppendSample(ElapsedMs, value);
        }

        /// <summary>外部タイムスタンプ（PLC 等）で直接記録</summary>
        public void AppendRaw(string name, DeviceCategory category,
                              SignalType type, float timeMs, float value)
        {
            if (m_Data == null) return;
            m_Data.GetOrAddChannel(name, category, type).AppendSample(timeMs, value);
        }
    }
}
