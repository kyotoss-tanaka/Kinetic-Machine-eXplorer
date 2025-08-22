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
using TMPro;

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

        private bool isDebug = false;
        private System.Diagnostics.Stopwatch swDebug = new System.Diagnostics.Stopwatch();

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
        private List<ExMechSetting> exMechSettings;
        private List<SwitchSetting> switchSettings;
        private List<SignalTowerSetting> towerSettings;
        private List<LedSetting> ledSettings;
        private List<CardboardSetting> cardboardSettings;
        private List<DebugSetting> debugSettings;
        private List<ActionTableData> actionTableDatas;
        private bool IsPrmLoading = false;

        // シェーダー
        private HashSet<Material> allMaterials = new HashSet<Material>();
        private Shader clipShader;
        private Shader standardShader;

        // パラメータ描画用
        private GameObject canvaObj;
        // 衝突検知
        private GameObject uiCollision;
        // 断面切断
        private CanvasMenuViewScript viewScript;
        private GameObject uiView;
        // プログレスバー
        private GameObject uiProgress;
        private Slider prgSlider;
        private TextMeshProUGUI prgText;
        private TextMeshProUGUI prgText2;

        // マテリアルキャッシュ
        private Dictionary<string, Material> materialCache = new Dictionary<string, Material>();

        void Awake()
        {
            DebugLog($"***** Start Load *****");

            // シェーダーロード
            clipShader = Shader.Find("Custom/ClipTransparent");
            standardShader = Shader.Find("Standard");

            // キャンバス生成
            CreateCanvas();

            // ロード開始
            StartCoroutine(LoadParameter());
        }

        /// <summary>
        /// パラメータロード
        /// </summary>
        /// <returns></returns>
        private IEnumerator LoadParameter()
        {
            // ロード開始
            GlobalScript.isLoading = true;
            // デバッグ時間開始
            swDebug.Restart();
            SetProgress(0);
            SetProgressLabel("Loading Prefab Files");
            DebugLog($"***** Load Start *****", true);

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
                DebugLog($"***** Load Prefab Model *****");
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
                DebugLog($"***** Parameter Load *****");
                IsPrmLoading = true;
                LoadParameterFiles();
            }
            catch (Exception ex)
            {
                DebugLog($"***** " + ex.Message + " *****");
            }
            {
                // パラメータロード待ち
                while (IsPrmLoading)
                {
                    yield return null; // 1フレーム待
                }

                DebugLog($"***** Set Debug Info *****");
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
                        tag.input = ScriptableObject.CreateInstance<GlobalScript.CbTagInfo>();
                        tag.input.Database = setting.database;
                        tag.input.MechId = setting.mechId;
                        tag.input.Tag = setting.input;
                        tag.output = ScriptableObject.CreateInstance<GlobalScript.CbTagInfo>();
                        tag.output.Database = setting.database;
                        tag.output.MechId = setting.mechId;
                        tag.output.Tag = setting.output;
                        tag.cntIn = ScriptableObject.CreateInstance<GlobalScript.CbTagInfo>();
                        tag.cntIn.Database = setting.database;
                        tag.cntIn.MechId = setting.mechId;
                        tag.cntIn.Tag = setting.inputCnt;
                        tag.cntOut = ScriptableObject.CreateInstance<GlobalScript.CbTagInfo>();
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
                        GlobalScript.callbackTags.Add(tag);
                    }
                }

                DebugLog($"***** Set Database *****");
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
                    else if (p.isDirectMode)
                    {
                        // 直接通信モード
                        foreach (var direct in p.directDatas)
                        {
                            if (direct.isMcProtocol)
                            {
                                var db = (ComMcProtocol)globalSetting.AddComponent<ComMcProtocol>();
                                db.SetParameter(p.No, p.Cycle, p.Server, p.Port, p.Database, p.User, p.Password, p.isClientMode, ex, direct);
                            }
                            else if (direct.isMicks)
                            {
                                var db = (ComMicks)globalSetting.AddComponent<ComMicks>();
                                db.SetParameter(p.No, p.Cycle, p.Server, p.Port, p.Database, p.User, p.Password, p.isClientMode, ex, direct);
                            }
                        }
                    }
                }
                /*
                // 無視オブジェクト無効化
                if (buildConfig.isRelease)
                {
                    // リリースモード時
                    DebugLog($"***** Hidden Models *****");
                    foreach (var prefab in prefabs)
                    {
                        if (prefab.name[0] != '_')
                        {
                            // 生成用ワーク保持
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
                            // 生成用段ボール保持
                            foreach (var cb in cardboardSettings)
                            {
                                var unit = unitSettings.Find(d => (d.mechId == cb.mechId) && (d.name == cb.name));
                                if (unit != null)
                                {
                                    var cardboard = prefab.GetComponentsInChildren<Transform>().ToList().FindAll(d => d.name == unit.parent);
                                    if (cardboard.Count > 0)
                                    {
                                        cardboard[0].transform.parent = prefabObj.transform;
                                        var c = works.Find(d => d.key == cb.name);
                                        if (c == null)
                                        {
                                            c = new ObjEntry { key = cb.name };
                                            works.Add(c);
                                        }
                                        c.obj = Instantiate(cardboard[0].gameObject);
                                        var cbs =  c.obj.AddComponent<CardboardScript>();
                                        cbs.SetParameter(unit, cb);
                                        c.obj.SetActive(false);
                                    }
                                }
                            }
                            // 非表示モデル
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
                */

                DebugLog($"***** Load Prefab Model *****");
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
//                if (!buildConfig.isRelease)
                {
                    // 生成用ワーク保持
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
                    // 生成用段ボール保持
                    foreach (var cb in cardboardSettings)
                    {
                        var unit = unitSettings.Find(d => (d.mechId == cb.mechId) && (d.name == cb.name));
                        if (unit != null)
                        {
                            var cardboard = GameObject.FindObjectsByType<GameObject>(FindObjectsSortMode.None).ToList().FindAll(d => d.name == unit.parent);
                            if (cardboard.Count > 0)
                            {
                                var c = works.Find(d => d.key == cb.name);
                                if (c == null)
                                {
                                    cardboard[0].transform.parent = prefabObj.transform;
                                    c = new ObjEntry { key = cb.name };
                                    works.Add(c);
                                    c.obj = Instantiate(cardboard[0].gameObject);
                                    var cbs = c.obj.AddComponent<CardboardScript>();
                                    cbs.SetParameter(unit, cb);
                                    c.obj.SetActive(false);
                                }
                            }
                        }
                    }
                    // デバッグモード時
                    DebugLog($"***** Hidden Models *****");
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
                    // ユニット設定にDB情報セット
                    var db = postgresSettings.Find(d => d.No == unitSetting.dbNo);
                    if (db != null)
                    {
                        unitSetting.Database = db.Name;
                    }
                    /*
                     * 必要ない可能性が高いので無効化
                    if (unitSetting.parent != null)
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
                    */
                    if (SetProgress(unitSettings.IndexOf(unitSetting) / (unitSettings.Count * 3f)))
                    {
                        yield return null; // 1フレーム待
                    }
                }

                // ユニットオブジェクト先に生成しておく
                movableObjs.Clear();
                undefinedUnits.Clear();
                // 段ボールはユニットは作成しない
                foreach (var cb in cardboardSettings)
                {
                    unitSettings.RemoveAll(d => d.name == cb.name);
                }
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
                            unit.parent = unit.parent == "" ? "_signalTower" + (towerSettings.IndexOf(st) + 1) : unit.parent;
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
                DebugLog($"***** Load Units *****");
                SetProgressLabel("Loading Units");
                // 親モデル検索用 ※ループ内から移動(ユニットを先に作成しているので問題ない？)
                allObjects = GameObject.FindObjectsByType<GameObject>(FindObjectsSortMode.None).ToList();
                foreach (var unitSetting in unitSettings)
                {
                    unitSetting.childrenObject = new List<GameObject>();
                    var gameObjects = allObjects.FindAll(d => d.name == unitSetting.parent);
                    if(gameObjects.Count == 0)
                    {
                        // 空オブジェクト作成
                        var dummy = new GameObject(unitSetting.parent);
                        dummy.name = unitSetting.name;
                        dummy.isStatic = true;
                        gameObjects.Add(dummy);
                        DebugLog($"エラー：ユニット名「{unitSetting.name}」の親モデル「{unitSetting.parent}」が存在しません。");
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
                                continue;
                            }
                        }
                        // ロボット紐づけ
                        unitSetting.robotSetting = robotSettings.Find(d => (d.mechId == unitSetting.mechId) && (d.name == unitSetting.name));
                        // ワーク生成設定紐づけ
                        unitSetting.workSettings = wkSettings.FindAll(d => (d.mechId == unitSetting.mechId) && (d.name == unitSetting.name));
                        // ワーク削除設定紐づけ
                        unitSetting.workDeleteSettings = wkDeleteSettings.FindAll(d => (d.mechId == unitSetting.mechId) && (d.name == unitSetting.name));
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
                        // LED設定紐づけ
                        unitSetting.ledSetting = ledSettings.Find(d => (d.mechId == unitSetting.mechId) && (d.name == unitSetting.name));
                        // 機構拡張設定紐づけ
                        unitSetting.exMechSetting = exMechSettings.Find(d => (d.mechId == unitSetting.mechId) && (d.name == unitSetting.name));
                        if (unitSetting.exMechSetting != null)
                        {
                            // 機構拡張設定
                            foreach (var data in unitSetting.exMechSetting.datas)
                            {
                                // モデルを設定しておく
                                data.gameObject = allObjects.FindAll(d => d.name == data.model).Find(d => GetScenePath(d).Contains(data.group));
                                foreach (var child in data.children)
                                {
                                    child.gameObject = allObjects.FindAll(d => d.name == child.model).Find(d => GetScenePath(d).Contains(child.group));
                                }
                            }
                        }
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
                                (unitSetting.ledSetting != null) ||             // LED設定あり
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
                    // プログレスバー設定
                    if (SetProgress((unitSettings.IndexOf(unitSetting) + unitSettings.Count) / (unitSettings.Count * 3f)))
                    {
                        yield return null; // 1フレーム待
                    }
                }
                // 使い勝手向上のため動作可能オブジェクトを移動
                var allMobableObjs = GameObject.FindObjectsByType<GameObject>(FindObjectsSortMode.None).ToList().FindAll(d => d.name.Contains(movableName + "_"));
                // 名前順にソート
                allMobableObjs.Sort((a, b) => a.transform.parent.name.CompareTo(b.transform.parent.name));
                var moveObjs = new List<GameObject>();
                foreach (var obj in allMobableObjs)
                {
                    // 親子関係を切らないように検索
                    var parents = obj.transform.parent.GetComponentsInParent<Transform>().Where(d => d.parent != null).ToList();
                    parents.Remove(obj.transform.parent);
                    var isFind = false;
                    foreach (var p in parents)
                    {
                        var tmp = p.GetComponentsInChildren<Transform>().Where(d => (d.parent.transform == p.transform) && d.name.Contains(movableName + "_")).ToList();
                        if (tmp.Count > 0) 
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
                    if (SetProgress(2 / 3f + allMobableObjs.IndexOf(obj) / (allMobableObjs.Count * 3f)))
                    {
                        yield return null; // 1フレーム待
                    }
                }
                foreach (var m in moveObjs)
                {
                    var mechId = m.name.Split('_')[1]!;
                    var uo = undefinedUnits.Find(d => d.key == mechId)!.obj;
                    var mo = movableObjs.Find(d => d.key == mechId)!.obj;
                    m.transform.parent.transform.parent = m.transform.parent.gameObject.isStatic ? uo.transform : mo.transform;
                    // 衝突検知は親が持つ
                    var rbs = m.transform.parent.GetComponentsInChildren<Rigidbody>().ToList();
                    if (rbs.Count > 1)
                    {
                        // 2つ以上のRigidbodyが有った場合は親以外のRigidbodyは削除(衝突検知は親で行う)
                        var prb = rbs.Find(d => d.transform.parent == m.transform.parent);
                        if (prb != null)
                        {
                            // 最上流のオブジェクト取得
                            rbs.Remove(prb);
                            var removeRbs = new List<Rigidbody>();
                            foreach (var rb in rbs)
                            {
                                if (rb.transform.GetComponent<SuctionScript>() == null)
                                {
                                    // 吸引以外は無視
                                    Destroy(rb);
                                }
                            }
                        }
                    }
                }
                foreach (var m in allMobableObjs)
                {
                    Destroy(m);
                }

                // シェーダー適用
                {
                    allMaterials = new();
                    var renderers = new List<Renderer>();
                    renderers.AddRange(prefabObj.GetComponentsInChildren<Renderer>().ToList());
                    foreach (var m in movableObjs)
                    {
                        renderers.AddRange(m.obj.GetComponentsInChildren<Renderer>().ToList());
                    }
                    foreach (Renderer renderer in renderers)
                    {
                        foreach (Material mat in renderer.materials)
                        {
                            if (mat != null)
                            {
                                if (mat.shader == standardShader)
                                {
                                    allMaterials.Add(mat);
                                }
                            }
                        }
                    }
                    //  描画エリア取得
                    renderers = prefabObj.GetComponentsInChildren<Renderer>().ToList();
                    if (renderers.Count > 0)
                    {
                        // 最初のRendererで初期化
                        GlobalScript.clipInfo.bounds = renderers[0].bounds;
                        // 残りのRendererを包含
                        foreach (Renderer rend in renderers)
                        {
                            GlobalScript.clipInfo.bounds.Encapsulate(rend.bounds);
                        }
                        // 初期値セット
                        viewScript.clipToggle_onValueChanged(true);
                    }
                }

                // デバッグ情報
                if (GlobalScript.buildConfig.isRelease)
                {
                    // 静的バッチングに変更
                    MeshRenderer[] renderers = prefabObj.GetComponentsInChildren<MeshRenderer>();
                    GameObject[] batchTargets = new GameObject[renderers.Length];
                    for (int i = 0; i < renderers.Length; i++)
                    {
                        renderers[i].gameObject.isStatic = true;
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
                    // 静的バッチングを実行（親にまとめてバッチング）※tri数が多くなるのと静的バッチングが実行されないので無効化
                    StaticBatchingUtility.Combine(batchTargets, prefabObj);
                }

                // VRならPrefab非表示にしておく
                if ((Application.platform == RuntimePlatform.Android) || (Application.platform == RuntimePlatform.IPhonePlayer))
                {
                    prefabObj.SetActive(false);
                }
                else if(GlobalScript.buildConfig.isCollision)
                {
                    SetProgressLabel("Creating All Collision Configurations");
                    yield return null; // 1フレーム待
                    GlobalScript.CreateCollider(prefabObj);
                    foreach (var obj in movableObjs)
                    {
                        GlobalScript.CreateCollider(obj.obj);
                    }
                }
            }
            if (SetProgress(100))
            {
                yield return null; // 1フレーム待
            }
            // イベント登録
            viewScript.SetEvents(allMaterials, standardShader, clipShader);
            GlobalScript.isLoading = false;
            GlobalScript.isLoaded = true;
            DebugLog($"***** Load Finished *****", true);
        }

        /// <summary>
        /// 動作パラメータのみ更新
        /// </summary>
        /// <returns></returns>
        private IEnumerator LoadActParameter()
        {
            swDebug.Restart();
            DebugLog($"***** Load Start *****", true);
            var motions = new List<AxisMotionBase>();
            var works = new List<ObjectScript>();
            IsPrmLoading = true;
            LoadParameterFiles();
            // パラメータロード待ち
            while (IsPrmLoading)
            {
                yield return null; // 1フレーム待
            }
            foreach (var obj in movableObjs)
            {
                motions.AddRange(obj.obj.GetComponentsInChildren<AxisMotionBase>().ToList());
                works.AddRange(obj.obj.GetComponentsInChildren<ObjectScript>().ToList());
            }
            foreach (var work in works)
            {
                Destroy(work.gameObject);
            }
            foreach (var p in postgresSettings)
            {
                var ex = dataExSettings.Find(d => d.dbNo == p.No);
                if (p.isPostgres)
                {
                    // Postgres
                    var db = (ComPostgres)globalSetting.GetComponent<ComPostgres>();
                    if (db == null)
                    {
                        db = (ComPostgres)globalSetting.AddComponent<ComPostgres>();
                    }
                    db.SetParameter(p.No, p.Cycle, p.Server, p.Port, p.Database, p.User, p.Password, p.isClientMode, ex);
                }
                else if (p.isMongo)
                {
                    // MongoDB
                    var db = (ComMongo)globalSetting.GetComponent<ComMongo>();
                    if (db == null)
                    {
                        db = (ComMongo)globalSetting.AddComponent<ComMongo>();
                    }
                    db.SetParameter(p.No, p.Cycle, p.Server, p.Port, p.Database, p.User, p.Password, p.isClientMode, ex);
                }
                else if (p.isMqtt)
                {
                    // MQTT
                    var db = (ComMqtt)globalSetting.GetComponent<ComMqtt>();
                    if (db == null)
                    {
                        db = (ComMqtt)globalSetting.AddComponent<ComMqtt>();
                    }
                    db.SetParameter(p.No, p.Cycle, p.Server, p.Port, p.Database, p.User, p.Password, p.isClientMode, ex);
                }
                else if (p.isInner)
                {
                    // 内部通信
                    var db = (ComInner)globalSetting.GetComponent<ComInner>();
                    if (db == null)
                    {
                        db = (ComInner)globalSetting.AddComponent<ComInner>();
                    }
                    db.SetParameter(p.No, p.Cycle, p.Server, p.Port, p.Database, p.User, p.Password, p.isClientMode, ex, innerSettings, actionSettings);
                }
                else if (p.isDirectMode)
                {
                    // 直接通信モード
                    foreach (var obj in globalSetting.GetComponentsInChildren<ComProtocolBase>())
                    {
                        Destroy(obj);
                    }
                    foreach (var direct in p.directDatas)
                    {
                        if (direct.isMcProtocol)
                        {
                            var db = (ComMcProtocol)globalSetting.AddComponent<ComMcProtocol>();
                            db.SetParameter(p.No, p.Cycle, p.Server, p.Port, p.Database, p.User, p.Password, p.isClientMode, ex, direct);
                        }
                        else if (direct.isMicks)
                        {
                            var db = (ComMicks)globalSetting.AddComponent<ComMicks>();
                            db.SetParameter(p.No, p.Cycle, p.Server, p.Port, p.Database, p.User, p.Password, p.isClientMode, ex, direct);
                        }
                    }
                }
            }
            foreach (var unitSetting in unitSettings)
            {
                if (unitSetting.name == "シート束後端整列")
                {
                }
                var motion = motions.Find(d => (d.unitSetting.mechId == unitSetting.mechId) && (d.unitSetting.name == unitSetting.name));
                if (motion != null)
                {
                    // ロボット紐づけ
                    motion.unitSetting.robotSetting = robotSettings.Find(d => (d.mechId == unitSetting.mechId) && (d.name == unitSetting.name));
                    // ワーク生成設定紐づけ
                    motion.unitSetting.workSettings = wkSettings.FindAll(d => (d.mechId == unitSetting.mechId) && (d.name == unitSetting.name));
                    // ワーク削除設定紐づけ
                    motion.unitSetting.workDeleteSettings = wkDeleteSettings.FindAll(d => (d.mechId == unitSetting.mechId) && (d.name == unitSetting.name));
                    // センサ設定紐づけ
                    motion.unitSetting.sensorSettings = sensorSettings.FindAll(d => (d.mechId == unitSetting.mechId) && (d.name == unitSetting.name));
                    // 吸引設定紐づけ
                    motion.unitSetting.suctionSetting = suctionSettings.Find(d => (d.mechId == unitSetting.mechId) && (d.name == unitSetting.name));
                    // 物体形状設定紐づけ
                    motion.unitSetting.shapeSetting = shapeSettings.Find(d => (d.mechId == unitSetting.mechId) && (d.name == unitSetting.name));
                    // スイッチ設定紐づけ
                    motion.unitSetting.switchSetting = switchSettings.Find(d => (d.mechId == unitSetting.mechId) && (d.name == unitSetting.name));
                    // シグナルタワー設定紐づけ
                    motion.unitSetting.towerSetting = towerSettings.Find(d => (d.mechId == unitSetting.mechId) && (d.name == unitSetting.name));
                    // LED設定紐づけ
                    motion.unitSetting.ledSetting = ledSettings.Find(d => (d.mechId == unitSetting.mechId) && (d.name == unitSetting.name));
                    // 機構拡張設定紐づけ
                    motion.unitSetting.exMechSetting = exMechSettings.Find(d => (d.mechId == unitSetting.mechId) && (d.name == unitSetting.name));
                    // 動作設定との紐づけ
                    motion.unitSetting.actionSetting = actionSettings.Find(d => (d.mechId == unitSetting.mechId) && (d.name == unitSetting.name));
                    // チャック設定
                    var chuckSetting = chuckUnitSettings.Find(d => (d.mechId == unitSetting.mechId) && (d.name == unitSetting.name));
                    if (chuckSetting != null)
                    {
                        foreach (var chuck in chuckSetting.children)
                        {
                            chuck.setting = unitSettings.Find(d => d.name == chuck.name);
                        }
                    }
                    // 動作設定のみ更新
                    motion.RenewUnitSetting(true);
                    motion.RenewChuckSetting(chuckSetting);
                    motion.RenewMoveDir();
                }
            }
            DebugLog($"***** Load Finished *****", true);
        }

        /// <summary>
        /// パラメータリロード
        /// </summary>
        public void ReloadParameter()
        {
            if (!GlobalScript.isLoading)
            {
                GlobalScript.isLoading = true;
                GlobalScript.isLoaded = false;
                DebugLog($"Start Reload");
                // 情報削除
                foreach (var obj in globalSetting.GetComponentsInChildren<ComBaseScript>())
                {
                    Destroy(obj);
                }
                /*
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
                */
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
        /// 動作パラメータリロード
        /// </summary>
        public void ReloadActParameter()
        {
            if (!GlobalScript.isLoading)
            {
                GlobalScript.isLoading = true;
                GlobalScript.isLoaded = false;
                StartCoroutine(LoadActParameter());
                GlobalScript.isLoading = false;
                GlobalScript.isLoaded = true;
                GlobalScript.isReqLoadEvent = true;
            }
        }

        /// <summary>
        /// パラメータロード
        /// </summary>
        private async void LoadParameterFiles()
        {
            DebugLog($"***** Parameter Load : Postgres *****");
            postgresSettings = (List<PostgresSetting>)await GlobalScript.LoadListJson<List<PostgresSetting>>("Postgres");
            DebugLog($"***** Parameter Load : DataExchangeInfo *****");
            dataExSettings = (List<DataExchangeSetting>)await GlobalScript.LoadListJson<List<DataExchangeSetting>>("DataExchangeInfo");
            DebugLog($"***** Parameter Load : UnitInfo *****");
            unitSettings = (List<UnitSetting>)await GlobalScript.LoadListJson<List<UnitSetting>>("UnitInfo");
            DebugLog($"***** Parameter Load : ActionInfo *****");
            actionSettings = (List<UnitActionSetting>)await GlobalScript.LoadListJson<List<UnitActionSetting>>("ActionInfo");
            DebugLog($"***** Parameter Load : InnerProcessInfo *****");
            innerSettings = (List<InnerProcessSetting>)await GlobalScript.LoadListJson<List<InnerProcessSetting>>("InnerProcess");
            DebugLog($"***** Parameter Load : HiddenUnitInfo *****");
            hiddenSettings = (List<HiddenUnit>)await GlobalScript.LoadListJson<List<HiddenUnit>>("HiddenUnitInfo");
            DebugLog($"***** Parameter Load : ChuckUnitInfo *****");
            chuckUnitSettings = (List<ChuckUnitSetting>)await GlobalScript.LoadListJson<List<ChuckUnitSetting>>("ChuckUnitInfo");
            DebugLog($"***** Parameter Load : RobotInfo *****");
            robotSettings = (List<RobotSetting>)await GlobalScript.LoadListJson<List<RobotSetting>>("RobotInfo");
            DebugLog($"***** Parameter Load : PlanarMotorInfo *****");
            pmSettings = (List<PlanarMotorSetting>)await GlobalScript.LoadListJson<List<PlanarMotorSetting>>("PlanarMotorInfo");
            DebugLog($"***** Parameter Load : ConveyerInfo *****");
            cvSettings = (List<ConveyerSetting>)await GlobalScript.LoadListJson<List<ConveyerSetting>>("ConveyerInfo");
            DebugLog($"***** Parameter Load : WorkCreateInfo *****");
            wkSettings = (List<WorkCreateSetting>)await GlobalScript.LoadListJson<List<WorkCreateSetting>>("WorkCreateInfo");
            DebugLog($"***** Parameter Load : WorkDeleteInfo *****");
            wkDeleteSettings = (List<WorkDeleteSetting>)await GlobalScript.LoadListJson<List<WorkDeleteSetting>>("WorkDeleteInfo");
            DebugLog($"***** Parameter Load : SensorInfo *****");
            sensorSettings = (List<SensorSetting>)await GlobalScript.LoadListJson<List<SensorSetting>>("SensorInfo");
            DebugLog($"***** Parameter Load : SuctionInfo *****");
            suctionSettings = (List<SuctionSetting>)await GlobalScript.LoadListJson<List<SuctionSetting>>("SuctionInfo");
            DebugLog($"***** Parameter Load : ShapeInfo *****");
            shapeSettings = (List<ShapeSetting>)await GlobalScript.LoadListJson<List<ShapeSetting>>("ShapeInfo");
            DebugLog($"***** Parameter Load : ExMechInfo *****");
            exMechSettings = (List<ExMechSetting>)await GlobalScript.LoadListJson<List<ExMechSetting>>("ExMechInfo");
            DebugLog($"***** Parameter Load : SwitchInfo *****");
            switchSettings = (List<SwitchSetting>)await GlobalScript.LoadListJson<List<SwitchSetting>>("SwitchInfo");
            DebugLog($"***** Parameter Load : SignalTowerInfo *****");
            towerSettings = (List<SignalTowerSetting>)await GlobalScript.LoadListJson<List<SignalTowerSetting>>("SignalTowerInfo");
            DebugLog($"***** Parameter Load : LedInfo *****");
            ledSettings = (List<LedSetting>)await GlobalScript.LoadListJson<List<LedSetting>>("LedInfo");
            DebugLog($"***** Parameter Load : CardboardInfo *****");
            cardboardSettings = (List<CardboardSetting>)await GlobalScript.LoadListJson<List<CardboardSetting>>("CardboardInfo");
            DebugLog($"***** Parameter Load : DebugInfo *****");
            debugSettings = (List<DebugSetting>)await GlobalScript.LoadListJson<List<DebugSetting>>("DebugInfo");
            DebugLog($"***** Parameter Load : BuildConfig *****");
            GlobalScript.buildConfig = (BuildConfig)await GlobalScript.LoadListJson<BuildConfig>("BuildConfig");
            DebugLog($"***** Parameter Load : ActionTable *****");
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
            /*
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
            */
            g = gameObjects.Find(d => GetScenePath(d).Contains(group));
            return g != null;
        }

        /// <summary>
        /// シーンパスを取得する
        /// </summary>
        /// <param name="obj"></param>
        /// <returns></returns>
        private List<string> GetScenePath(GameObject obj)
        {
            var path = new List<string>();
            path.Add(obj.name);
            Transform current = obj.transform;

            while (current.parent != null)
            {
                current = current.parent;
                path.Add(current.name);
            }
            return path;
        }

        /// <summary>
        /// キャンバス追加
        /// </summary>
        private void CreateCanvas()
        {
            // キャンバス取得
            var canvasObjs = GameObject.FindObjectsByType<GameObject>(FindObjectsSortMode.None).Where(d => d.name == "Canvas").ToList();
            canvaObj = canvasObjs.Count == 0 ? new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster)) : canvasObjs[0];
            /*
            var collider = GlobalScript.LoadPrefabObject("Prefabs/Canvas", "ColliderSetting");
            if (collider.Count > 0)
            {
                uiCollision = Instantiate(collider[0]);
                uiCollision.transform.SetParent(canvaObj.transform, false);
                ((RectTransform)uiCollision.transform).anchoredPosition = new Vector2(-((RectTransform)uiCollision.transform).rect.width / 2, -((RectTransform)uiCollision.transform).rect.height / 2);
                // コンポネント取得
                collisionToggle = uiCollision.GetComponentInChildren<Toggle>();
            }
            */
            var clip = GlobalScript.LoadPrefabObject("Prefabs/Canvas", "ViewSetting");
            if (clip.Count > 0)
            {
                uiView = Instantiate(clip[0]);
                uiView.transform.SetParent(canvaObj.transform, false);
                viewScript = uiView.AddComponent<CanvasMenuViewScript>();
            }
            var progress = GlobalScript.LoadPrefabObject("Prefabs/Canvas", "ProgressSetting");
            if (progress.Count > 0)
            {
                uiProgress = Instantiate(progress[0]);
                uiProgress.transform.SetParent(canvaObj.transform, false);
                ((RectTransform)uiProgress.transform).anchoredPosition = new Vector2();
                // コンポネント取得
                prgSlider = uiProgress.GetComponentInChildren<Slider>();
                prgText = uiProgress.GetComponentsInChildren<TextMeshProUGUI>().ToList().Find(d => d.name == "prgText");
                prgText2 = uiProgress.GetComponentsInChildren<TextMeshProUGUI>().ToList().Find(d => d.name == "prgText2");
            }
        }

        /// <summary>
        /// プログレスバーセット
        /// </summary>
        /// <param name="value"></param>
        private bool SetProgress(float value)
        {
            uiProgress.SetActive(value < 1);
            if (Math.Abs(prgSlider.value - value) * 100 > 3)
            {
                prgSlider.value = value;
                prgText.text = (value * 100).ToString("0.0") + "%";
                return true;
            }
            return false;
        }

        /// <summary>
        /// プログレスラベルセット 
        /// </summary>
        private void SetProgressLabel(string text)
        {
            prgText2.text = text;
        }

        /// <summary>
        /// デバッグログ
        /// </summary>
        private void DebugLog(string message, bool isForce = false)
        {
            if (isDebug || isForce)
            {
                Debug.Log(swDebug.ElapsedMilliseconds + "msec : " + message);
            }
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
