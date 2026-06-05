using System;
using System.Collections.Generic;
using UnityEngine;

namespace KyotoSS.TimingChart
{
    public enum SignalType { Digital, Analog }
    public enum DeviceCategory { Cylinder, AutoSwitch, Motor, Sensor, Other }

    [Serializable]
    public class SignalSample
    {
        public float TimeMs;
        public float Value;
        public SignalSample(float t, float v) { TimeMs = t; Value = v; }
    }

    [Serializable]
    public class SignalChannel
    {
        public string Name = "Signal";
        public DeviceCategory Category = DeviceCategory.Other;
        public SignalType Type = SignalType.Digital;
        public float AnalogMin = 0f;
        public float AnalogMax = 100f;
        public Color Color = Color.cyan;

        public List<SignalSample> Samples = new List<SignalSample>();

        // デジタル変化点のみ記録（同値は無視）
        public void AppendDigitalEdge(float timeMs, bool on)
        {
            float v = on ? 1f : 0f;
            if (Samples.Count > 0 && Samples[Samples.Count - 1].Value == v) return;
            Samples.Add(new SignalSample(timeMs, v));
        }

        // アナログ / 強制追記
        public void AppendSample(float timeMs, float value)
            => Samples.Add(new SignalSample(timeMs, value));

        /// <summary>ラベル下に表示するサブテキスト（PLCデバイス名など）</summary>
        public string SubLabel = "";
        /// <summary>アナログチャンネルの位置情報リスト（正規化値, 実位置値, 名称）</summary>
        public List<(float NormValue, float RealValue, string Name)> PositionLabels = new List<(float, float, string)>();

        public void Clear() => Samples.Clear();

        /// <summary>
        /// 同じ Samples リストを共有するシャローコピーを返す。
        /// 複数グループで同一チャンネルを表示する際に使用。
        /// Samples は共有するため波形データは常に同一になる。
        /// </summary>
        public SignalChannel ShallowCopy() => new SignalChannel
        {
            Name = this.Name,
            Category = this.Category,
            Type = this.Type,
            AnalogMin = this.AnalogMin,
            AnalogMax = this.AnalogMax,
            Color = this.Color,
            SubLabel = this.SubLabel,
            Samples = this.Samples,        // 同じリストを共有
            PositionLabels = this.PositionLabels,  // 同じリストを共有
        };
    }

    // ----------------------------------------------------------------
    // ScriptableObject ― データバス
    // ----------------------------------------------------------------
    [CreateAssetMenu(menuName = "KyotoSS/TimingChart Data", fileName = "TimingChartData")]
    public class TimingChartDataAsset : ScriptableObject
    {
        [SerializeField] private List<SignalChannel> m_Channels = new List<SignalChannel>();
        public IReadOnlyList<SignalChannel> Channels => m_Channels;

        public SignalChannel GetOrAddChannel(string name, DeviceCategory cat, SignalType type)
        {
            var ch = m_Channels.Find(c => c.Name == name);
            if (ch != null) return ch;
            ch = new SignalChannel
            {
                Name = name,
                Category = cat,
                Type = type,
                Color = AutoColor(m_Channels.Count)
            };
            m_Channels.Add(ch);
            return ch;
        }

        public void ClearAllSamples() { foreach (var c in m_Channels) c.Clear(); }
        public void ClearAll() => m_Channels.Clear();

        // ---- JSON ----
        public string ToJson() => JsonUtility.ToJson(new W { Channels = m_Channels }, true);
        public void FromJson(string j) { var w = JsonUtility.FromJson<W>(j); if (w?.Channels != null) m_Channels = w.Channels; }
        [Serializable] private class W { public List<SignalChannel> Channels; }

        static Color AutoColor(int i)
        {
            Color[] p = {
                new Color(.2f,.8f,1f), new Color(.2f,1f,.4f), new Color(1f,.8f,.2f),
                new Color(1f,.4f,.4f), new Color(.8f,.4f,1f), new Color(1f,.6f,.2f),
                new Color(.4f,.8f,.8f),new Color(1f,.4f,.8f),
            };
            return p[i % p.Length];
        }
    }
}