using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace KyotoSS.TimingChart
{
    /// <summary>
    /// RenderTexture に GL で波形を描画し RawImage に貼る。
    /// ScrollRect の content 内に複数並べて使用。
    /// </summary>
    [RequireComponent(typeof(RawImage))]
    public class WaveformRenderer : MonoBehaviour
    {
        public SignalChannel Channel { get; private set; }

        // 描画パラメータ（TimingChartView から毎フレーム設定）
        public float ViewStartMs { get; set; }
        public float ViewEndMs { get; set; }

        private RawImage m_Image;
        private RenderTexture m_RT;
        private Material m_LineMat;
        private int m_LastWidth = -1;
        private int m_LastHeight = -1;

        // ---- 初期化 ----
        public void Init(SignalChannel ch, int width, int height)
        {
            Channel = ch;
            m_Image = GetComponent<RawImage>();
            // RT は初回 Redraw（＝可視行になった時）に生成する。画面外の行は確保しない（メモリ削減）。
            m_LastWidth = -1;
            m_LastHeight = -1;
            CreateMaterial();
        }

        private void CreateRT(int w, int h)
        {
            if (m_RT != null) m_RT.Release();
            m_RT = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32);
            m_RT.Create();
            if (m_Image)
            {
                m_Image.texture = m_RT;
            }
            m_LastWidth = w;
            m_LastHeight = h;
        }

        private void CreateMaterial()
        {
            if (m_LineMat == null)
            {
                var shader = Shader.Find("Hidden/Internal-Colored");
                m_LineMat = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                m_LineMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                m_LineMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                m_LineMat.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
                m_LineMat.SetInt("_ZWrite", 0);
            }
        }

        /// <summary>オーバーレイチャンネルリスト</summary>
        public readonly List<SignalChannel> Overlays = new List<SignalChannel>();

        // ---- 毎フレーム再描画 ----
        public void Redraw()
        {
            if (Channel == null) return;

            // RectTransform サイズ変化に追従。RT未確保（画面外で解放済み）なら生成。
            var rt = GetComponent<RectTransform>();
            int w = Mathf.Max(1, Mathf.RoundToInt(rt.rect.width));
            int h = Mathf.Max(1, Mathf.RoundToInt(rt.rect.height));
            if (m_RT == null || w != m_LastWidth || h != m_LastHeight) CreateRT(w, h);

            var prev = RenderTexture.active;
            RenderTexture.active = m_RT;

            GL.PushMatrix();
            GL.LoadPixelMatrix(0, w, h, 0);  // Y=0が上端・Y=hが下端

            // 背景クリア
            GL.Clear(true, true, new Color(0.11f, 0.11f, 0.11f, 1f));

            m_LineMat.SetPass(0);

            // グリッド
            DrawGrid(w, h);

            // 波形
            if (Channel.Samples != null && Channel.Samples.Count > 0)
            {
                if (Channel.Type == SignalType.Digital)
                    DrawDigital(w, h);
                else
                    DrawAnalog(w, h);
            }

            // オーバーレイチャンネルを重ねて描画
            foreach (var overlayCh in Overlays)
            {
                if (overlayCh == null || overlayCh.Samples == null || overlayCh.Samples.Count == 0) continue;
                m_LineMat.SetPass(0);
                var saved = Channel; Channel = overlayCh;
                if (overlayCh.Type == SignalType.Digital) DrawDigital(w, h);
                else DrawAnalog(w, h);
                Channel = saved;
            }

            // オーバーレイチャンネルを重ねて描画
            foreach (var overlayCh in Overlays)
            {
                if (overlayCh == null || overlayCh.Samples == null || overlayCh.Samples.Count == 0) continue;
                m_LineMat.SetPass(0);
                var saved = Channel; Channel = overlayCh;
                if (overlayCh.Type == SignalType.Digital) DrawDigital(w, h);
                else DrawAnalog(w, h);
                Channel = saved;
            }

            GL.PopMatrix();
            RenderTexture.active = prev;
        }

        // ---- グリッド ----
        private void DrawGrid(int w, int h)
        {
            GL.Begin(GL.LINES);
            GL.Color(new Color(0.22f, 0.22f, 0.22f, 1f));

            if (Channel.Type == SignalType.Digital)
            {
                // 中間線
                float mid = h * 0.5f;
                GL.Vertex3(0, mid, 0); GL.Vertex3(w, mid, 0);
            }
            else
            {
                float mid = h * 0.5f;
                GL.Vertex3(0, mid, 0); GL.Vertex3(w, mid, 0);
            }
            GL.End();
        }

        // ---- デジタル波形（矩形波）----
        private void DrawDigital(int w, int h)
        {
            float hiY = h * 0.1f;   // ON: 上端（アナログmax位置と同じ）
            float loY = h * 0.9f;   // OFF: 下端（アナログmin位置と同じ）

            var samples = Channel.Samples;

            int si = FindBefore(samples, ViewStartMs);
            int ei = FindAfter(samples, ViewEndMs);

            GL.Begin(GL.LINES);
            GL.Color(Channel.Color);

            float? prevX = null;
            float prevV = -1;

            for (int i = si; i <= ei && i < samples.Count; i++)
            {
                float x = TsToX(samples[i].TimeMs, w);
                float v = samples[i].Value;
                float y = v > 0.5f ? hiY : loY;

                if (prevX.HasValue)
                {
                    float py = prevV > 0.5f ? hiY : loY;
                    // 水平
                    GL.Vertex3(prevX.Value, py, 0); GL.Vertex3(x, py, 0);
                    // 垂直エッジ
                    GL.Vertex3(x, py, 0); GL.Vertex3(x, y, 0);
                }
                prevX = x; prevV = v;
            }
            // 最後から右端へ延長
            if (prevX.HasValue)
            {
                float py = prevV > 0.5f ? hiY : loY;
                GL.Vertex3(prevX.Value, py, 0); GL.Vertex3(w, py, 0);
            }
            GL.End();
        }

        // ---- アナログ波形（折れ線）----
        private void DrawAnalog(int w, int h)
        {
            var samples = Channel.Samples;


            int si = FindBefore(samples, ViewStartMs);
            int ei = FindAfter(samples, ViewEndMs);

            GL.Begin(GL.LINE_STRIP);
            GL.Color(Channel.Color);

            for (int i = si; i <= ei && i < samples.Count; i++)
            {
                float x = TsToX(samples[i].TimeMs, w);
                float norm = Mathf.InverseLerp(Channel.AnalogMin, Channel.AnalogMax, samples[i].Value);
                // Y=0が上端: norm=1（最大値）→ y=h*0.1（上端）、norm=0→ y=h*0.9（下端）
                float y = h * (0.1f + (1f - norm) * 0.8f);

                GL.Vertex3(x, y, 0);
            }
            GL.End();

            // 位置ガイドライン（設定されている各位置に水平破線）
            if (Channel.PositionLabels != null && Channel.PositionLabels.Count > 0)
            {
                GL.Begin(GL.LINES);
                GL.Color(new Color(0.55f, 0.55f, 0.55f, 0.45f));
                foreach (var pl in Channel.PositionLabels)
                {
                    // NormValue は既に0〜1に正規化済みなのでそのまま使う
                    float y = h * (0.1f + (1f - pl.NormValue) * 0.8f);
                    // 破線風に描画
                    float dashW = 6f, gapW = 4f;
                    for (float x = 0; x < w; x += dashW + gapW)
                    {
                        GL.Vertex3(x, y, 0);
                        GL.Vertex3(Mathf.Min(x + dashW, w), y, 0);
                    }
                }
                GL.End();
            }
        }

        // ---- ユーティリティ ----
        float TsToX(float ms, int w)
        {
            float span = Mathf.Max(ViewEndMs - ViewStartMs, 1f);
            return (ms - ViewStartMs) / span * w;
        }

        static int FindBefore(List<SignalSample> s, float t)
        {
            for (int i = s.Count - 1; i >= 0; i--)
                if (s[i].TimeMs <= t) return Mathf.Max(0, i);
            return 0;
        }
        static int FindAfter(List<SignalSample> s, float t)
        {
            for (int i = 0; i < s.Count; i++)
                if (s[i].TimeMs >= t) return Mathf.Min(s.Count - 1, i + 1);
            return s.Count - 1;
        }

        /// <summary>画面外になった行の RenderTexture を解放（メモリ削減）。再表示時に Redraw が再生成する。</summary>
        public void ReleaseRT()
        {
            if (m_RT != null)
            {
                m_RT.Release();
                Destroy(m_RT);
                m_RT = null;
            }
            if (m_Image != null) m_Image.texture = null;
            m_LastWidth = m_LastHeight = -1;
        }

        private void OnDestroy()
        {
            if (m_RT) { m_RT.Release(); Destroy(m_RT); }
            if (m_LineMat) Destroy(m_LineMat);
        }
    }
}