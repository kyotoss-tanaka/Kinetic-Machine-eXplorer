using UnityEngine;

namespace KyotoSS.TimingChart.Example
{
    /// <summary>
    /// TimingChartRecorder + PositionSignalGenerator の使用例。
    /// 実際の制御スクリプトでの呼び出し方を示す。
    /// </summary>
    public class MachineControllerExample : MonoBehaviour
    {
        [SerializeField] private TimingChartRecorder     recorder;
        [SerializeField] private PositionSignalGenerator posGen;

        // 制御状態（実際は PLC・センサから取得）
        private bool cyl1FwdCmd = false;
        private bool cyl1FwdAS  = false;
        private bool cyl1BwdCmd = false;
        private bool cyl1BwdAS  = false;

        private void Update()
        {
            // ---- 各 IO を Recorder で記録 ----
            recorder.SetDigital("CYL1_前進指令", DeviceCategory.Cylinder,   cyl1FwdCmd);
            recorder.SetDigital("AS1_前端",      DeviceCategory.AutoSwitch, cyl1FwdAS);
            recorder.SetDigital("CYL1_後退指令", DeviceCategory.Cylinder,   cyl1BwdCmd);
            recorder.SetDigital("AS1_後端",      DeviceCategory.AutoSwitch, cyl1BwdAS);

            // ---- 位置チャンネルをリアルタイム更新 ----
            posGen.UpdateSignals("POS1_位置",
                fwdCmd: cyl1FwdCmd, fwdAS: cyl1FwdAS,
                bwdCmd: cyl1BwdCmd, bwdAS: cyl1BwdAS);
        }

        /// <summary>JSON ロード後に呼ぶ（オフライン一括生成）</summary>
        public void OnJsonLoaded()
        {
            posGen.GenerateFromRecordedData();
        }

        /// <summary>Inspector の ContextMenu からテスト動作を実行できる</summary>
        [ContextMenu("Simulate 1 Cycle")]
        private void SimulateCycle()
        {
            StartCoroutine(SimulateCycleCoroutine());
        }

        private System.Collections.IEnumerator SimulateCycleCoroutine()
        {
            // 前進指令 ON
            cyl1FwdCmd = true;
            yield return new WaitForSeconds(0.5f);

            // 前端 AS ON（前進完了）
            cyl1FwdAS = true;
            yield return new WaitForSeconds(1.0f);

            // 前進指令 OFF
            cyl1FwdCmd = false;
            yield return new WaitForSeconds(0.3f);

            // 後退指令 ON
            cyl1BwdCmd = true;
            yield return new WaitForSeconds(0.5f);

            // 後端 AS ON（後退完了）
            cyl1BwdAS = true;
            yield return new WaitForSeconds(0.5f);

            // 全 OFF（リセット）
            cyl1BwdCmd = false;
            yield return new WaitForSeconds(0.1f);
            cyl1FwdAS  = false;
            cyl1BwdAS  = false;
        }
    }
}
