using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using UnityEngine;

public class MPX_R6 : MPX_RX
{
    #region 変数
    protected GameObject arm1_1;
    protected GameObject arm1_2;
    protected GameObject arm2_1;
    protected GameObject arm2_2;
    protected GameObject arm3;
    protected GameObject arm4;
    protected GameObject plate;

    private Vector3 ang1_1;
    private Vector3 ang1_2;
    private Vector3 ang2_1;
    private Vector3 ang2_2;
    private Vector3 ang3;
    private Vector3 ang4;
    private Vector3 angP;

    private float offset = 45;

    /// <summary>
    /// アームの部品が揃って組み立てられたか（揃わない時は動かさない）
    /// </summary>
    private bool isAssembled = false;
    #endregion 変数

    /// <summary>
    /// 目標位置セット
    /// </summary>
    /// <param name="x"></param>
    /// <param name="y"></param>
    /// <param name="z"></param>
    protected override bool TryModelPoseTarget(out Vector3 raw)
    {
        // SetTarget の角度の当て方を逆にたどる（arm1_1=180+a0、arm2_1=-a1、plate=(±90)-head_offset-a2）
        raw = Vector3.zero;
        if ((arm1_1 == null) || (arm1_2 == null) || (arm2_1 == null) || (arm2_2 == null) || (arm3 == null) || (arm4 == null) || (plate == null))
        {
            return false;
        }
        var a0 = Mathf.DeltaAngle(0f, ang1_1.z - 180f);
        var a1 = -Mathf.DeltaAngle(0f, ang2_1.z);
        var a2 = Mathf.DeltaAngle(0f, (isRvs ? -90f : 90f) - head_offset - angP.z);
        raw = ForwardMPX(a0, a1, a2);
        return true;
    }

    public override void SetTarget(float x, float y, float z)
    {
        base.SetTarget(x, y, z);
        if (!isAssembled)
        {
            return;
        }
        arm1_1.transform.localEulerAngles = new Vector3(ang1_1.x, ang1_1.y, 180 + angle[0]);
        arm1_2.transform.localEulerAngles = new Vector3(ang1_2.x, ang1_2.y, (angle[0] + angle[1]) - 180);
        arm2_1.transform.localEulerAngles = new Vector3(ang2_1.x, ang2_1.y, -angle[1]);
        arm2_2.transform.localEulerAngles = new Vector3(ang2_2.x, ang2_2.y, 180 - (angle[0] + angle[1]));
        arm3.transform.localEulerAngles = new Vector3(ang3.x, ang3.y, offset - angle[1]);
        arm4.transform.localEulerAngles = new Vector3(ang4.x, ang4.y, (isRvs ? 90 : -90) - arm3.transform.localEulerAngles.z);
        if (plate != null)
        {
            plate.transform.localEulerAngles = new Vector3(angP.x, angP.y, (isRvs ? -90 : 90) - head_offset - angle[2]);
        }
    }



    /// <summary>
    /// モデル再構築
    /// </summary>
    /// <param name="instance"></param>
    protected override void ModelRestructProcess()
    {
        var baseObj = base.ModelRestructProcess("MPX-R6");

        r1 = 200;
        r2 = 200;

        var children = unitSetting.moveObject.GetComponentsInChildren<Transform>().ToList();

        if (children.Find(d => d.name.Contains("W0578936-")) != null)
        {
            axisType = 3;
            isRvs = false;
        }
        else if (children.Find(d => d.name.Contains("W0652706-")) != null)
        {
            axisType = 3;
            offset = -offset; 
            isRvs = true;
        }

        // 各アームはロボットの定義（Datas/Robots/RobotModels.json）の名前で探す。定義に無いアームはここに書いた既定の名前で探す
        var finder = RobotDefinitions.Finder(unitSetting, Parameters.RobotType.MPX_R6, children, HeadObject);
        arm1_1 = finder.Find("アーム1-1", "parent:W0578802-", "parent:W0652681-");
        arm1_2 = finder.Find("アーム1-2", "parent:W0578972-");
        arm2_1 = finder.Find("アーム2-1", "parent:W0579111-", "parent:W0652733-");   // 三角プレート
        arm2_2 = finder.Find("アーム2-2", "parent:W0578963-", "parent:W0652724-", "parent:W0971610-");   // W0971610 は R8790（減速機・モーターと同じ入れ物）
        arm3 = finder.Find("アーム3", "parent:W0578936-", "parent:W0652706-");
        arm4 = finder.Find("アーム4", "parent:W0578981-");
        // プレート：減速機の回転部
        var plateTmp = finder.Find("プレート", "VRGF-45B60P-8AG8_2^88P3_VRGF-45B60P-8AG8");
        finder.Log();

        // 見つからない部品があれば組み立てない（途中で null に触って例外になり、毎フレーム例外を出していた）
        var missing = new List<string>();
        if (arm1_1 == null) missing.Add("アーム1-1");
        if (arm1_2 == null) missing.Add("アーム1-2");
        if (arm2_1 == null) missing.Add("アーム2-1");
        if (arm2_2 == null) missing.Add("アーム2-2");
        if (arm3 == null) missing.Add("アーム3");
        if (arm4 == null) missing.Add("アーム4");
        if (missing.Count > 0)
        {
            Debug.LogWarning($"[Robot] {unitSetting.name}: MPX-R6 の部品 {string.Join("・", missing)} が見つからないため、組み立てずに動かしません（Datas/Robots/RobotModels.json の parts で指定できます）");
            return;
        }

        // プレート 減速機の回転部
        if (plateTmp == null)
        {
            if (HeadObject != null)
            {
                // プレートの部品が無い時はヘッドをプレートとして回す。
                // ヘッドの Z 軸が回転軸と同じ向きとは限らない（R8790 の ROBO3 は直交している）ので、
                // ヘッドの位置にアーム2-2 と同じ向き（Z＝回転軸）の入れ物を作り、そちらを回す
                var pivot = new GameObject("PlatePivot");
                pivot.transform.SetParent(arm2_2.transform, false);
                pivot.transform.position = HeadObject.transform.position;
                pivot.transform.localRotation = Quaternion.identity;
                HeadObject.transform.parent = pivot.transform;
                plate = pivot;
                angP = plate.transform.localEulerAngles;
                // ヘッドの取付角（回転軸まわりの角度。プレートの部品がある時の head_offset と同じ役割で、ヘッドの向き＝90°−Z になる）。
                // ヘッドの Z 軸が回転軸と違う向きでも求まるよう、回転軸まわりのねじれ成分で求める（Z 軸が揃っていれば Euler の z と同じ）
                var q = HeadObject.transform.localRotation;
                head_offset = Mathf.DeltaAngle(0f, 2f * Mathf.Atan2(q.z, q.w) * Mathf.Rad2Deg);
                Debug.Log($"[Robot] {unitSetting.name}: プレートの部品が無いため、ヘッド {HeadObject.name} をアーム2-2 の回転軸の向きで回します（取付角 {head_offset:0.00}°）");
            }
            else
            {
                Debug.LogWarning($"[Robot] {unitSetting.name}: プレート（減速機の回転部）が無く、ヘッドも指定されていないため、Z（ヘッドの回転）は動きません");
            }
        }
        else
        {
            plate = plateTmp;
            plate.transform.parent = arm2_2.transform;
            angP = plate.transform.localEulerAngles;

            // ヘッドセット
            if (HeadObject != null)
            {
                HeadObject.transform.parent = plate.transform;
                head_offset = HeadObject.transform.localEulerAngles.z;
            }
        }

        // 親子関係構築
        arm1_1.transform.parent = mpx.transform;
        arm2_1.transform.parent = mpx.transform;
        arm3.transform.parent = mpx.transform;
        arm1_2.transform.parent = arm2_1.transform;
        arm2_2.transform.parent = arm1_1.transform;
        arm4.transform.parent = arm3.transform;

        // 初期角度セット
        ang1_1 = arm1_1.transform.localEulerAngles;
        ang1_2 = arm1_2.transform.localEulerAngles;
        ang2_1 = arm2_1.transform.localEulerAngles;
        ang2_2 = arm2_2.transform.localEulerAngles;
        ang3 = arm3.transform.localEulerAngles;
        ang4 = arm4.transform.localEulerAngles;
        isAssembled = true;
    }
}
