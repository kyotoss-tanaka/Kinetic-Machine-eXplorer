using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Text.Json;
using UnityEngine;
using UnityEngine.InputSystem.XR;
using Unity.VisualScripting;
using static UnityEngine.UI.CanvasScaler;
using System.Collections;
using NUnit.Framework;
using UnityEngine.UI;

namespace Parameters
{
    /// <summary>
    /// csvに記載されたパラメータテーブルを読込み、UnitSettingリストを作成する。
    /// csvのフォーマット変更に対する柔軟性はなく、項目変更などがある場合は修正を要する
    /// </summary>
    public class ParameterLoader : MonoBehaviour
    {
        [Serializable]
        public class ObjEntry
        {
            public string key;
            public GameObject obj;
        }

        /// <summary>
        /// 動作可能オブジェクト名
        /// </summary>
        private static string movableName = "MovableObject";
        private GameObject globalSetting;
        private GameObject prefabObj;
        private List<ObjEntry> movableObjs = new List<ObjEntry>();
        private List<ObjEntry> undefinedUnits = new List<ObjEntry>();
        private List<GameObject> prefabs = new List<GameObject>();
        private List<GameObject> switchPrefabs = new List<GameObject>();
        private List<GameObject> towerPrefabs = new List<GameObject>();
        private List<GameObject> switchModel = new List<GameObject>();
        private List<GameObject> towerModel = new List<GameObject>();
        private List<ObjEntry> works = new List<ObjEntry>();
        private List<PostgresSetting> postgresSettings;
        private List<DataExchangeSetting> dataExSettings;
        private List<UnitSetting> unitSettings;
        private List<UnitActionSetting> actionSettings;
        private List<InnerProcessSetting> innerSettings;
        private List<HiddenUnit> hiddenSettings;
        private List<ChuckUnitSetting> chuckUnitSettings;
        private List<RobotSetting> robotSettings;
        private List<PlanarMotorSetting> pmSettings;
        private List<ConveyerSetting> cvSettings;
        private List<WorkCreateSetting> wkSettings;
        private List<WorkDeleteSetting> wkDeleteSettings;
        private List<SensorSetting> sensorSettings;
        private List<SuctionSetting> suctionSettings;
        private List<ShapeSetting> shapeSettings;
        private List<SwitchSetting> switchSettings;
        private List<SignalTowerSetting> towerSettings;
        private List<DebugSetting> debugSettings;
        private List<ActionTableData> actionTableDatas;
        private BuildConfig buildConfig;
        private bool IsLoading = false;
        private bool IsPrmLoading = false;

        private GameObject canvaObj;
        private GameObject uiObj;
        private Toggle toggle;

        void Awake()
        {
            Debug.Log($"***** Start Load *****");

            StartCoroutine(LoadParameter());

            CreateCanvas();
        }

        /// <summary>
        /// パラメータロード
        /// </summary>
        /// <returns></returns>
        private IEnumerator LoadParameter()
        {
            // データ削除
            yield return null; // 1フレーム待
            GlobalScript.ClearDictionary();
            yield return null; // 1フレーム待

            // 必要オブジェクト作成
            prefabObj = new GameObject("PrefabObjects");

            var globalSettings = GameObject.FindObjectsByType<GameObject>(FindObjectsSortMode.None).Where(d => d.name == "GlobalSetting").ToList();
            globalSetting = globalSettings.Count > 0 ? globalSettings[0] : new GameObject("GlobalSetting");
            try
            {
                Debug.Log($"***** Load Prefab Model *****");
                if (prefabs.Count == 0)
                {
                    prefabs = GlobalScript.CreateInitialModel();
                }
                // スイッチモデルロード
                if (switchPrefabs.Count == 0)
                {
                    switchPrefabs = GlobalScript.CreateSwitchModel();
                }
                // シグナルタワーモデルロード
                if (towerPrefabs.Count == 0)
                {
                    towerPrefabs = GlobalScript.CreateSignalTowerModel();
                }
                // 各種設定ファイルロード
                Debug.Log($"***** Parameter Load *****");
                IsPrmLoading = true;
                LoadParameterFiles();
            }
            catch (Exception ex)
            {
                Debug.Log($"***** " + ex.Message + " *****");
            }
            {
                // パラメータロード待ち
                while (IsPrmLoading)
                {
                    yield return null; // 1フレーム待
                }

                Debug.Log($"***** Set Debug Info *****");
                // 折り返し用データ
                GlobalScript.actionTableDatas = actionTableDatas;
                GlobalScript.callbackTags.Clear();
                foreach (var setting in debugSettings)
                {
                    var db = postgresSettings.Find(d => d.Name == setting.database);
                    if (db != null)
                    {
                        var tag = new GlobalScript.CallbackTag();
                        tag.database = setting.database;
                        tag.input = ScriptableObject.CreateInstance<TagInfo>();
                        tag.input.Database = setting.database;
                        tag.input.MechId = setting.mechId;
                        tag.input.Tag = setting.input;
                        tag.output = ScriptableObject.CreateInstance<TagInfo>();
                        tag.output.Database = setting.database;
                        tag.output.MechId = setting.mechId;
                        tag.output.Tag = setting.output;
                        tag.cntIn = ScriptableObject.CreateInstance<TagInfo>();
                        tag.cntIn.Database = setting.database;
                        tag.cntIn.MechId = setting.mechId;
                        tag.cntIn.Tag = setting.inputCnt;
                        tag.cntOut = ScriptableObject.CreateInstance<TagInfo>();
                        tag.cntOut.Database = setting.database;
                        tag.cntOut.MechId = setting.mechId;
                        tag.cntOut.Tag = setting.outputCnt;
                        tag.cycle = ScriptableObject.CreateInstance<TagInfo>();
                        tag.cycle.Database = setting.database;
                        tag.cycle.MechId = setting.mechId;
                        if (db.isInner)
                        {
                            tag.cycle.Tag = setting.cycle == "" ? "_innerCycle" : setting.cycle;
                        }
                        else
                        {
                            tag.cycle.Tag = setting.cycle == "" ? "" : setting.cycle;
                        }
                        tag.stopwatch = new System.Diagnostics.Stopwatch();
                        GlobalScript.callbackTags.Add(tag);
                    }
                }

                Debug.Log($"***** Set Database *****");
                foreach (var p in postgresSettings)
                {
                    var ex = dataExSettings.Find(d => d.dbNo == p.No);
                    if (p.isPostgres)
                    {
                        // Postgres
                        var db = (ComPostgres)globalSetting.AddComponent<ComPostgres>();
                        db.SetParameter(p.No, p.Cycle, p.Server, p.Port, p.Database, p.User, p.Password, p.isClientMode, ex);
                    }
                    else if (p.isMongo)
                    {
                        // MongoDB
                        var db = (ComMongo)globalSetting.AddComponent<ComMongo>();
                        db.SetParameter(p.No, p.Cycle, p.Server, p.Port, p.Database, p.User, p.Password, p.isClientMode, ex);
                    }
                    else if (p.isMqtt)
                    {
                        // MQTT
                        var db = (ComMqtt)globalSetting.AddComponent<ComMqtt>();
                        db.SetParameter(p.No, p.Cycle, p.Server, p.Port, p.Database, p.User, p.Password, p.isClientMode, ex);
                    }
                    else if (p.isInner)
                    {
                        // 内部通信
                        var db = (ComInner)globalSetting.AddComponent<ComInner>();
                        db.SetParameter(p.No, p.Cycle, p.Server, p.Port, p.Database, p.User, p.Password, p.isClientMode, ex, innerSettings, actionSettings);
                    }
                }

                // 無視オブジェクト無効化
                if (buildConfig.isRelease)
                {
                    // リリースモード時
                    Debug.Log($"***** Hidden Models *****");
                    foreach (var prefab in prefabs)
                    {
                        if (prefab.name[0] != '_')
                        {
                            foreach (var wk in wkSettings)
                            {
                                var work = prefab.GetComponentsInChildren<Transform>().ToList().FindAll(d => d.name == wk.work);
                                if (work.Count > 0)
                                {
                                    var w = works.Find(d => d.key == wk.work);
                                    if (w == null)
                                    {
                                        w = new ObjEntry { key = wk.work };
                                        works.Add(w);

                                    }
                                    w.obj = Instantiate(work[0].gameObject);
                                    w.obj.SetActive(false);
                                }
                            }

                            foreach (var m in hiddenSettings)
                            {
                                if (m.isEnable)
                                {
                                    if (m.mode == 0)
                                    {
                                        // 一致
                                        foreach (var o in prefab.GetComponentsInChildren<Transform>().ToList().FindAll(d => d.name == m.name))
                                        {
                                            if ((m.parent == null) || (m.parent == "") || (m.parent == o.parent.name))
                                            {
                                                o.gameObject.SetActive(false);
                                            }
                                        }
                                    }
                                    else if (m.mode == 1)
                                    {
                                        // 前方一致
                                        foreach (var o in prefab.GetComponentsInChildren<Transform>().ToList().FindAll(d => d.name.StartsWith(m.name)))
                                        {
                                            if ((m.parent == null) || (m.parent == "") || (m.parent == o.parent.name))
                                            {
                                                o.gameObject.SetActive(false);
                                            }
                                        }
                                    }
                                    else if (m.mode == 2)
                                    {
                                        // 後方一致
                                        foreach (var o in prefab.GetComponentsInChildren<Transform>().ToList().FindAll(d => d.name.EndsWith(m.name)))
                                        {
                                            if ((m.parent == null) || (m.parent == "") || (m.parent == o.parent.name))
                                            {
                                                o.gameObject.SetActive(false);
                                            }
                                        }
                                    }
                                    else if (m.mode == 3)
                                    {
                                        // 含まれている
                                        foreach (var o in prefab.GetComponentsInChildren<Transform>().ToList().FindAll(d => d.name.Contains(m.name)))
                                        {
                                            if ((m.parent == null) || (m.parent == "") || (m.parent == o.parent.name))
                                            {
                                                o.gameObject.SetActive(false);
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }

                Debug.Log($"***** Load Prefab Model *****");
                foreach (var prefab in prefabs)
                {
                    if (prefab.name[0] != '_')
                    {
                        var prefabDatas = GameObject.FindObjectsByType<GameObject>(FindObjectsSortMode.None).Where(d => d.name == prefab.name).ToList();
                        if (prefabDatas.Count == 0)
                        {
                            var obj = Instantiate(prefab);
                            obj.name = prefab.name;
                            obj.transform.parent = prefabObj.transform;
                        }
                        else
                        {
                            foreach (var prefabData in prefabDatas)
                            {
                                prefabData.transform.parent = prefabObj.transform;
                            }
                        }
                    }
                }

                // 無視オブジェクト無効化
                if (!buildConfig.isRelease)
                {
                    foreach (var wk in wkSettings)
                    {
                        var work = GameObject.FindObjectsByType<GameObject>(FindObjectsSortMode.None).ToList().FindAll(d => d.name == wk.work);
                        if (work.Count > 0)
                        {
                            var w = works.Find(d => d.key == wk.work);
                            if (w == null)
                            {
                                w = new ObjEntry { key = wk.work };
                                works.Add(w);
                                w.obj = Instantiate(work[0].gameObject);
                                w.obj.SetActive(false);
                            }
                        }
                    }
                    // デバッグモード時
                    Debug.Log($"***** Hidden Models *****");
                    // 無視オブジェクト無効化
                    foreach (var m in hiddenSettings)
                    {
                        if (m.isEnable)
                        {
                            if (m.mode == 0)
                            {
                                // 一致
                                foreach (var o in GameObject.FindObjectsByType<GameObject>(FindObjectsSortMode.None).ToList().FindAll(d => d.name == m.name))
                                {
                                    if ((m.parent == null) || (m.parent == "") || (m.parent == o.transform.parent.name))
                                    {
                                        o.SetActive(false);
                                    }
                                }
                            }
                            else if (m.mode == 1)
                            {
                                // 前方一致
                                foreach (var o in GameObject.FindObjectsByType<GameObject>(FindObjectsSortMode.None).ToList().FindAll(d => d.name.StartsWith(m.name)))
                                {
                                    if ((m.parent == null) || (m.parent == "") || (m.parent == o.transform.parent.name))
                                    {
                                        o.SetActive(false);
                                    }
                                }
                            }
                            else if (m.mode == 2)
                            {
                                // 後方一致
                                foreach (var o in GameObject.FindObjectsByType<GameObject>(FindObjectsSortMode.None).ToList().FindAll(d => d.name.EndsWith(m.name)))
                                {
                                    if ((m.parent == null) || (m.parent == "") || (m.parent == o.transform.parent.name))
                                    {
                                        o.SetActive(false);
                                    }
                                }
                            }
                            else if (m.mode == 3)
                            {
                                // 含まれている
                                foreach (var o in GameObject.FindObjectsByType<GameObject>(FindObjectsSortMode.None).ToList().FindAll(d => d.name.Contains(m.name)))
                                {
                                    if ((m.parent == null) || (m.parent == "") || (m.parent == o.transform.parent.name))
                                    {
                                        o.SetActive(false);
                                    }
                                }
                            }
                        }
                    }
                }

                // ワークセット
                foreach (var work in works)
                {
                    GlobalScript.works[work.key] = work.obj;
                }

                // メッシュのないオブジェクト無効化
                var names = new List<string>();
                foreach (var unitSetting in unitSettings)
                {
                    var db = postgresSettings.Find(d => d.No == unitSetting.dbNo);
                    if (db != null)
                    {
                        unitSetting.Database = db.Name;
                    }
                    if ((unitSetting.parent != null) || (unitSetting.parent != null))
                    {
                        if (!names.Contains(unitSetting.parent))
                        {
                            foreach (var o in GameObject.FindObjectsByType<GameObject>(FindObjectsSortMode.None).ToList().FindAll(d => d.name == unitSetting.parent))
                            {
                                var mesh = o.transform.GetComponentInChildren<MeshRenderer>();
                                if (mesh == null)
                                {
                                    o.SetActive(false);
                                }
                            }
                            names.Add(unitSetting.parent);
                        }
                    }
                }

                // ユニットオブジェクト先に生成しておく
                movableObjs.Clear();
                undefinedUnits.Clear();
                foreach (var unitSetting in unitSettings)
                {
                    unitSetting.unitObject = new GameObject(unitSetting.name);
                    var movable = new GameObject(movableName + "_" + unitSetting.mechId);
                    movable.transform.parent = unitSetting.unitObject.transform;
                    // 機番と紐づけ
                    if (movableObjs.Find(d => d.key == unitSetting.mechId) == null)
                    {
                        movableObjs.Add(new ObjEntry { key = unitSetting.mechId, obj = new GameObject("#" + unitSetting.mechId) });
                        undefinedUnits.Add(new ObjEntry { key = unitSetting.mechId, obj = new GameObject("UndefinedUnits") });
                        undefinedUnits[undefinedUnits.Count - 1].obj.name = "UndefinedUnits";
                        undefinedUnits[undefinedUnits.Count - 1].obj.transform.parent = movableObjs[movableObjs.Count - 1].obj.transform;
                    }
                }

                // ユニット生成順ソート
                var unitNames = unitSettings.Select(d => d.name).ToList();
                var tmpUnits = new List<UnitSetting>();
                // チャックユニットは先に生成しておく
                foreach (var chuck in chuckUnitSettings)
                {
                    foreach (var child in chuck.children)
                    {
                        var c = unitSettings.Find(d => (d.mechId == chuck.mechId) && (d.name == child.name));
                        if ((c != null) && !tmpUnits.Contains(c))
                        {
                            tmpUnits.Add(c);
                        }
                    }
                }
                SortUnitSettings(unitNames, unitSettings, ref tmpUnits);
                unitSettings = tmpUnits;

                // 全てのオブジェクト取得
                List<GameObject> allObjects = GameObject.FindObjectsByType<GameObject>(FindObjectsSortMode.None).ToList();

                // 存在しないスイッチモデル生成
                switchModel.Clear();
                if (switchPrefabs.Count > 0)
                {
                    foreach (var sw in switchSettings)
                    {
                        var unit = unitSettings.Find(d => (d.mechId == sw.mechId) && (d.name == sw.name));
                        if ((unit != null) && ((unit.group == null) || (unit.group == "")))
                        {
                            unit.parent = unit.parent == "" ? "_switch" + (switchSettings.IndexOf(sw) + 1) : unit.parent;
                            if (allObjects.Find(d => d.name == unit.parent) == null)
                            {
                                // モデルが存在しないので作成
                                var obj = Instantiate(switchPrefabs[0]);
                                obj.name = unit.parent;
                                obj.transform.localPosition = new Vector3(sw.pos[0], sw.pos[1], sw.pos[2]);
                                obj.transform.localEulerAngles = new Vector3(sw.rot[0], sw.rot[1], sw.rot[2]);
                                switchModel.Add(obj);
                            }
                        }
                    }
                }
                // 存在しないシグナルタワーモデル生成
                towerModel.Clear();
                if (towerPrefabs.Count > 0)
                {
                    foreach (var st in towerSettings)
                    {
                        var unit = unitSettings.Find(d => (d.mechId == st.mechId) && (d.name == st.name));
                        if ((unit != null) && ((unit.group == null) || (unit.group == "")))
                        {
                            if (allObjects.Find(d => d.name == unit.parent) == null)
                            {
                                // モデルが存在しないので作成
                                var obj = Instantiate(towerPrefabs[st.type]);
                                obj.name = unit.parent;
                                obj.transform.localPosition = new Vector3(st.pos[0], st.pos[1], st.pos[2]);
                                obj.transform.localEulerAngles = new Vector3(st.rot[0], st.rot[1], st.rot[2]);
                                towerModel.Add(obj);
                            }
                        }
                    }
                }

                // 親モデルに動作スクリプトを付与
                Debug.Log($"***** Load Units *****");
                foreach (var unitSetting in unitSettings)
                {
                    // 親モデル検索用
                    allObjects = GameObject.FindObjectsByType<GameObject>(FindObjectsSortMode.None).ToList();
                    unitSetting.childrenObject = new List<GameObject>();
                    var gameObjects = allObjects.FindAll(d => d.name == unitSetting.parent);
                    if(gameObjects.Count == 0)
                    {
                        // 空オブジェクト作成
                        var dummy = new GameObject(unitSetting.parent);
                        dummy.name = unitSetting.name;
                        dummy.isStatic = true;
                        gameObjects.Add(dummy);
                        Debug.Log($"エラー：ユニット名「{unitSetting.name}」の親モデル「{unitSetting.parent}」が存在しません。");
                    }
                    if (gameObjects.Count > 0)
                    {
                        if ((gameObjects.Count == 1) || (unitSetting.group == null) || (unitSetting.group == ""))
                        {
                            // 先頭を親にする
                            unitSetting.moveObject = gameObjects[0];
                            unitSetting.unitObject.isStatic = gameObjects[0].isStatic;
                            // 先頭以外は子供として紐づける
                            for (var i = 1; i < gameObjects.Count; i++)
                            {
                                unitSetting.childrenObject.Add(gameObjects[i]);
                            }
                            //　子モデルセット
                            foreach (var child in unitSetting.children)
                            {
                                var childObjects = allObjects.FindAll(d => d.name == child.name);
                                GameObject childObject = null;
                                if (FindInGroup(childObjects, child.group, ref childObject))
                                {
                                    unitSetting.childrenObject.Add(childObject);
                                }
                            }
                        }
                        else
                        {
                            if (FindInGroup(gameObjects, unitSetting.group, ref unitSetting.moveObject))
                            {
                                //　子モデルセット
                                foreach (var child in unitSetting.children)
                                {
                                    var childObjects = allObjects.FindAll(d => d.name == child.name);
                                    GameObject childObject = null;
                                    if (FindInGroup(childObjects, child.group, ref childObject))
                                    {
                                        unitSetting.childrenObject.Add(childObject);
                                    }
                                }
                            }
                            else
                            {
                                Debug.Log($"エラー：ユニット名「{unitSetting.name}」の親モデル「{unitSetting.parent}」がグループ[{unitSetting.group}]に存在しません。");
                            }
                        }
                        // ロボット紐づけ
                        unitSetting.robotSetting = robotSettings.Find(d => (d.mechId == unitSetting.mechId) && (d.name == unitSetting.name));
                        // ワーク生成設定紐づけ
                        unitSetting.workSetting = wkSettings.Find(d => (d.mechId == unitSetting.mechId) && (d.name == unitSetting.name));
                        // ワーク削除設定紐づけ
                        unitSetting.workDeleteSetting = wkDeleteSettings.Find(d => (d.mechId == unitSetting.mechId) && (d.name == unitSetting.name));
                        // センサ設定紐づけ
                        unitSetting.sensorSettings = sensorSettings.FindAll(d => (d.mechId == unitSetting.mechId) && (d.name == unitSetting.name));
                        // 吸引設定紐づけ
                        unitSetting.suctionSetting = suctionSettings.Find(d => (d.mechId == unitSetting.mechId) && (d.name == unitSetting.name));
                        // 物体形状設定紐づけ
                        unitSetting.shapeSetting = shapeSettings.Find(d => (d.mechId == unitSetting.mechId) && (d.name == unitSetting.name));
                        // スイッチ設定紐づけ
                        unitSetting.switchSetting = switchSettings.Find(d => (d.mechId == unitSetting.mechId) && (d.name == unitSetting.name));
                        // シグナルタワー設定紐づけ
                        unitSetting.towerSetting = towerSettings.Find(d => (d.mechId == unitSetting.mechId) && (d.name == unitSetting.name));
                        // チャック設定更新
                        var chuckSetting = chuckUnitSettings.Find(d => (d.mechId == unitSetting.mechId) && (d.name == unitSetting.name));
                        if (chuckSetting != null)
                        {
                            foreach (var chuck in chuckSetting.children)
                            {
                                chuck.setting = unitSettings.Find(d => d.name == chuck.name);
                            }
                        }
                        // 動作設定との紐づけ
                        unitSetting.actionSetting = actionSettings.Find(d => (d.mechId == unitSetting.mechId) && (d.name == unitSetting.name));
                        if (unitSetting.actionSetting != null)
                        {
                            // 動作設定
                            if (unitSetting.actionSetting.isInternal)
                            {
                                // 内部動作なら
                                var instance = unitSetting.unitObject.AddComponent<MotionInternal>();
                                instance.SetUnitSettings(unitSetting, chuckSetting);
                            }
                            else if (unitSetting.actionSetting.isExternal)
                            {
                                // 外部動作なら
                                var instance = unitSetting.unitObject.AddComponent<MotionExternal>();
                                instance.SetUnitSettings(unitSetting, chuckSetting);
                            }
                            else if (unitSetting.actionSetting.isActionTable)
                            {
                                // 動作テーブルなら
                                var instance = unitSetting.unitObject.AddComponent<MotionActionTable>();
                                instance.SetUnitSettings(unitSetting, chuckSetting);
                            }
                            else if (unitSetting.actionSetting.isRobo)
                            {
                                // 外部ロボットなら(再構築のみ)
                                var instance = unitSetting.unitObject.AddComponent<AxisMotionBase>();
                                instance.SetUnitSettings(unitSetting, chuckSetting);
                                // ロボットタイプ取得
                                var robo = robotSettings.Find(d => (d.mechId == unitSetting.mechId) && (d.name == unitSetting.name));
                                if (robo != null)
                                {
                                    robo.headUnit = unitSettings.Find(d => d.name == robo.head);
                                    if (robo.robo == RobotType.MPS2_3AS)
                                    {
                                    }
                                    else if (robo.robo == RobotType.MPS2_4AS)
                                    {
                                    }
                                    else if (robo.robo == RobotType.MPX_PI)
                                    {
                                        var rObj = unitSetting.moveObject.AddComponent<IrregularityParallel>();
                                        rObj.SetParameter(unitSetting, robo);
                                    }
                                    else if (robo.robo == RobotType.YF03N4)
                                    {
                                        var rObj = unitSetting.moveObject.AddComponent<YF03N4>();
                                        rObj.SetParameter(unitSetting, robo);
                                    }
                                    else if (robo.robo == RobotType.RS007L)
                                    {
                                    }
                                }
                                else
                                {
                                    Debug.Log($"エラー：ユニット名(ロボット名)「{unitSetting.name}」の動作設定が存在しません。");
                                }
                            }
                            else if (unitSetting.actionSetting.isPlanarMotor)
                            {
                                // 外部平面リニアなら(再構築のみ)
                                var instance = unitSetting.unitObject.AddComponent<AxisMotionBase>();
                                instance.SetUnitSettings(unitSetting, chuckSetting);
                                var pm = pmSettings.Find(d => (d.mechId == unitSetting.mechId) && (d.name == unitSetting.name));
                                if (pm != null)
                                {
                                    pm.headUnit = unitSettings.Find(d => d.name == pm.head);
                                    var pmObj = globalSetting.AddComponent<Br6DScript>();
                                    pmObj.SetParameter(unitSetting, pm);
                                }
                                else
                                {
                                    Debug.Log($"エラー：ユニット名(ロボット名)「{unitSetting.name}」の動作設定が存在しません。");
                                }
                            }
                            else if (unitSetting.actionSetting.isConveyer)
                            {
                                // コンベアなら(再構築のみ)
                                var instance = unitSetting.unitObject.AddComponent<AxisMotionBase>();
                                instance.SetUnitSettings(unitSetting, chuckSetting);
                                var cv = cvSettings.Find(d => (d.mechId == unitSetting.mechId) && (d.name == unitSetting.name));
                                if (cv != null)
                                {
                                    var cvObj = unitSetting.moveObject.AddComponent<ConveyorScript>();
                                    cvObj.SetParameter(unitSetting, cv);
                                }
                                else
                                {
                                    Debug.Log($"エラー：ユニット名(ロボット名)「{unitSetting.name}」の動作設定が存在しません。");
                                }
                            }
                        }
                        else
                        {
                            // 動作設定なし
                            var isFamiry = unitSettings.Find(d => d.children.Find(x => x.name == unitSetting.name) != null) != null;
                            var isChuck = chuckUnitSettings.Find(d => d.children.Find(x => x.name == unitSetting.name) != null) != null;
                            if (isFamiry ||                                     // 親子関係あり
                                (unitSetting.shapeSetting != null) ||           // 形状設定あり
                                (unitSetting.switchSetting != null) ||          // スイッチ設定あり
                                (unitSetting.towerSetting != null) ||           // シグナルタワー設定あり
                                (unitSetting.isCollision && !isChuck))          // チャック以外の衝突検知あり
                            {
                                // 構成のみセット
                                var instance = unitSetting.unitObject.AddComponent<AxisMotionBase>();
                                instance.SetUnitSettings(unitSetting, chuckSetting);
                            }
                        }
                    }
                    else
                    {
                        Debug.Log($"エラー：ユニット名「{unitSetting.name}」の親モデル「{unitSetting.parent}」が存在しません。");
                        Destroy(unitSetting.unitObject);
                        //                            EndApplication();
                    }
                    if ((Application.platform == RuntimePlatform.Android) || (Application.platform == RuntimePlatform.IPhonePlayer))
                    {
//                        yield return null; // 1フレーム待
                    }
                }

                // 使い勝手向上のため動作可能オブジェクトを移動
                var allMobableObjs = GameObject.FindObjectsByType<GameObject>(FindObjectsSortMode.None).ToList().FindAll(d => d.name.Contains(movableName + "_"));
                var moveObjs = new List<GameObject>();
                foreach (var obj in allMobableObjs)
                {
                    var parents = obj.transform.parent.GetComponentsInParent<Transform>().ToList().FindAll(d => d.parent != null).ToList();
                    parents.Remove(obj.transform.parent);
                    var isFind = false;
                    foreach (var p in parents)
                    {
                        var tmp = p.GetComponentsInChildren<Transform>().ToList().FindAll(d => d.parent.transform == p.transform).Find(d => d.name.Contains(movableName + "_"));
                        if (tmp != null) 
                        {
                            isFind = true;
                            break;
                        }
                    }
                    if (!isFind)
                    {
                        // 最上流の動作可能親オブジェクト
                        moveObjs.Add(obj);
                    }
                }
                foreach (var m in moveObjs)
                {
                    var mechId = m.name.Split('_')[1]!;
                    var uo = undefinedUnits.Find(d => d.key == mechId)!.obj;
                    var mo = movableObjs.Find(d => d.key == mechId)!.obj;
                    m.transform.parent.transform.parent = m.transform.parent.gameObject.isStatic ? uo.transform : mo.transform;
                }
                foreach (var m in allMobableObjs)
                {
                    Destroy(m);
                }

                // デバッグ情報
                if (buildConfig.isRelease)
                {
                    // 静的バッチングに変更
                    MeshRenderer[] renderers = prefabObj.GetComponentsInChildren<MeshRenderer>();
                    GameObject[] batchTargets = new GameObject[renderers.Length];
                    for (int i = 0; i < renderers.Length; i++)
                    {
                        batchTargets[i] = renderers[i].gameObject;
                        // VRは透明オブジェクトを削除
                        if ((Application.platform == RuntimePlatform.Android) || (Application.platform == RuntimePlatform.IPhonePlayer))
                        {
                            // 透明オブジェクトチェック
                            Material material = renderers[i].sharedMaterial;
                            if (material != null && material.HasProperty("_Mode"))
                            {
                                // _Modeプロパティの値を取得
                                if (material.GetFloat("_Mode") == 3f)
                                {
                                    // 透明は非表示
                                    renderers[i].gameObject.SetActive(false);
                                }
                            }
                        }
                    }
                    // 静的バッチングを実行（親にまとめてバッチング）
                    StaticBatchingUtility.Combine(batchTargets, prefabObj);
                }

                // VRならPrefab非表示にしておく
                if ((Application.platform == RuntimePlatform.Android) || (Application.platform == RuntimePlatform.IPhonePlayer))
                {
                    prefabObj.SetActive(false);
                }
            }
            // イベント登録
            toggle.onValueChanged.RemoveAllListeners();
            toggle.onValueChanged.AddListener(toggle_onValueChanged);
            IsLoading = false;
        }

        /// <summary>
        /// パラメータリロード
        /// </summary>
        public void ReloadParameter()
        {
            if (!IsLoading)
            {
                IsLoading = true;
                Debug.Log($"Start Reload");
                // 情報削除
                foreach (var obj in globalSetting.GetComponentsInChildren<ComPostgres>())
                {
                    Destroy(obj);
                }
                foreach (var obj in globalSetting.GetComponentsInChildren<ComMongo>())
                {
                    Destroy(obj);
                }
                foreach (var obj in globalSetting.GetComponentsInChildren<ComMqtt>())
                {
                    Destroy(obj);
                }
                foreach (var obj in globalSetting.GetComponentsInChildren<ComInner>())
                {
                    Destroy(obj);
                }
                foreach (var obj in globalSetting.GetComponentsInChildren<Br6DScript>())
                {
                    Destroy(obj);
                }
                foreach (var obj in switchModel)
                {
                    Destroy(obj);
                }
                foreach (var obj in towerModel)
                {
                    Destroy(obj);
                }
                Destroy(prefabObj);
                foreach (var obj in movableObjs)
                {
                    Destroy(obj.obj);
                }
                StartCoroutine(LoadParameter());
            }
        }

        /// <summary>
        /// パラメータロード
        /// </summary>
        private async void LoadParameterFiles()
        {
            Debug.Log($"***** Parameter Load : Postgres *****");
            postgresSettings = (List<PostgresSetting>)await GlobalScript.LoadListJson<List<PostgresSetting>>("Postgres");
            Debug.Log($"***** Parameter Load : DataExchangeInfo *****");
            dataExSettings = (List<DataExchangeSetting>)await GlobalScript.LoadListJson<List<DataExchangeSetting>>("DataExchangeInfo");
            Debug.Log($"***** Parameter Load : UnitInfo *****");
            unitSettings = (List<UnitSetting>)await GlobalScript.LoadListJson<List<UnitSetting>>("UnitInfo");
            Debug.Log($"***** Parameter Load : ActionInfo *****");
            actionSettings = (List<UnitActionSetting>)await GlobalScript.LoadListJson<List<UnitActionSetting>>("ActionInfo");
            Debug.Log($"***** Parameter Load : InnerProcessInfo *****");
            innerSettings = (List<InnerProcessSetting>)await GlobalScript.LoadListJson<List<InnerProcessSetting>>("InnerProcess");
            Debug.Log($"***** Parameter Load : HiddenUnitInfo *****");
            hiddenSettings = (List<HiddenUnit>)await GlobalScript.LoadListJson<List<HiddenUnit>>("HiddenUnitInfo");
            Debug.Log($"***** Parameter Load : ChuckUnitInfo *****");
            chuckUnitSettings = (List<ChuckUnitSetting>)await GlobalScript.LoadListJson<List<ChuckUnitSetting>>("ChuckUnitInfo");
            Debug.Log($"***** Parameter Load : RobotInfo *****");
            robotSettings = (List<RobotSetting>)await GlobalScript.LoadListJson<List<RobotSetting>>("RobotInfo");
            Debug.Log($"***** Parameter Load : PlanarMotorInfo *****");
            pmSettings = (List<PlanarMotorSetting>)await GlobalScript.LoadListJson<List<PlanarMotorSetting>>("PlanarMotorInfo");
            Debug.Log($"***** Parameter Load : ConveyerInfo *****");
            cvSettings = (List<ConveyerSetting>)await GlobalScript.LoadListJson<List<ConveyerSetting>>("ConveyerInfo");
            Debug.Log($"***** Parameter Load : WorkCreateInfo *****");
            wkSettings = (List<WorkCreateSetting>)await GlobalScript.LoadListJson<List<WorkCreateSetting>>("WorkCreateInfo");
            Debug.Log($"***** Parameter Load : WorkDeleteInfo *****");
            wkDeleteSettings = (List<WorkDeleteSetting>)await GlobalScript.LoadListJson<List<WorkDeleteSetting>>("WorkDeleteInfo");
            Debug.Log($"***** Parameter Load : SensorInfo *****");
            sensorSettings = (List<SensorSetting>)await GlobalScript.LoadListJson<List<SensorSetting>>("SensorInfo");
            Debug.Log($"***** Parameter Load : SuctionInfo *****");
            suctionSettings = (List<SuctionSetting>)await GlobalScript.LoadListJson<List<SuctionSetting>>("SuctionInfo");
            Debug.Log($"***** Parameter Load : ShapeInfo *****");
            shapeSettings = (List<ShapeSetting>)await GlobalScript.LoadListJson<List<ShapeSetting>>("ShapeInfo");
            Debug.Log($"***** Parameter Load : SwitchInfo *****");
            switchSettings = (List<SwitchSetting>)await GlobalScript.LoadListJson<List<SwitchSetting>>("SwitchInfo");
            Debug.Log($"***** Parameter Load : SignalTowerInfo *****");
            towerSettings = (List<SignalTowerSetting>)await GlobalScript.LoadListJson<List<SignalTowerSetting>>("SignalTowerInfo");
            Debug.Log($"***** Parameter Load : DebugInfo *****");
            debugSettings = (List<DebugSetting>)await GlobalScript.LoadListJson<List<DebugSetting>>("DebugInfo");
            Debug.Log($"***** Parameter Load : BuildConfig *****");
            buildConfig = (BuildConfig)await GlobalScript.LoadListJson<BuildConfig>("BuildConfig");
            Debug.Log($"***** Parameter Load : ActionTable *****");
            actionTableDatas = (List<ActionTableData>)await GlobalScript.LoadListJson<List<ActionTableData>>("ActionTableInfo");
            IsPrmLoading = false;
        }

        /// <summary>
        /// ユニット設定をソートする
        /// </summary>
        /// <param name="unitNames"></param>
        /// <param name="unitSettings"></param>
        /// <param name="tmpUnits"></param>
        /// <returns></returns>
        private bool SortUnitSettings(List<string> unitNames, List<UnitSetting> unitSettings, ref List<UnitSetting> tmpUnits)
        {
            foreach (var unitSetting in unitSettings)
            {
                var u = unitSetting.children.FindAll(d => unitNames.Contains(d.name));
                if (u.Count == 0)
                {
                    // 登録可能
                    if (!tmpUnits.Contains(unitSetting))
                    {
                        tmpUnits.Add(unitSetting);
                    }
                }
                else
                {
                    // 子供検索
                    var tmp = new List<UnitSetting>();
                    foreach (var c in u)
                    {
                        var t = this.unitSettings.Find(d => d.name == c.name);
                        if (t != null)
                        {
                            tmp.Add(t);
                        }
                    }
                    SortUnitSettings(unitNames, tmp, ref tmpUnits);
                    // ソートしてから登録可能
                    if (!tmpUnits.Contains(unitSetting))
                    {
                        tmpUnits.Add(unitSetting);
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// グループ内にいるか？
        /// </summary>
        /// <param name="g"></param>
        /// <param name="group"></param>
        /// <returns></returns>
        private bool FindInGroup(List<GameObject> gameObjects, string group, ref GameObject g)
        {
            if((group == null) || (group == ""))
            {
                if (gameObjects.Count > 0)
                {
                    g = gameObjects[0];
                    return true;
                }
            }
            foreach (var tmp in gameObjects)
            {
                var p = tmp.transform.GetComponentsInParent<Transform>().ToList();
                var t = p.Find(d => d.name == group);
                if (t != null)
                {
                    g = tmp;
                    return true;
                }
            }
            return false;
        }


        /// <summary>
        /// キャンバス追加
        /// </summary>
        private void CreateCanvas()
        {
            // キャンバス取得
            var canvasObjs = GameObject.FindObjectsByType<GameObject>(FindObjectsSortMode.None).Where(d => d.name == "Canvas").ToList();
            canvaObj = canvasObjs.Count == 0 ? new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster)) : canvasObjs[0];
            var prefabs = GlobalScript.LoadPrefabObject("Prefabs/Canvas", "ColliderSetting");
            if (prefabs.Count > 0)
            {
                uiObj = Instantiate(prefabs[0]);
                uiObj.transform.parent = canvaObj.transform;
                ((RectTransform)uiObj.transform).anchoredPosition = new Vector2(-((RectTransform)uiObj.transform).rect.width / 2, -((RectTransform)uiObj.transform).rect.height / 2);

                // コンポネント取得
                toggle = uiObj.GetComponentInChildren<Toggle>();
            }
        }

        /// <summary>
        /// トグル変更イベント
        /// </summary>
        /// <param name="value"></param>
        private void toggle_onValueChanged(bool value)
        {
            // 衝突
            GlobalScript.isCollision = value;
        }

        /// <summary>
        /// エラー時にUnityを終了させるラッパー
        /// </summary>
        void EndApplication()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;//ゲームプレイ終了
#else
            Application.Quit();//ゲームプレイ終了
#endif
        }
    }
}
