using MongoDB.Driver;
using NUnit.Framework;
using Oculus.Interaction;
using Oculus.Interaction.Surfaces;
using Org.BouncyCastle.Ocsp;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using TMPro;
using Unity.IO.LowLevel.Unsafe;
using Unity.VisualScripting;
using UnityEditor;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.AddressableAssets.Initialization;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using XCharts.Runtime;
using static GlobalScript;
using static UnityEngine.UI.CanvasScaler;

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
        private GameObject deviceObj;
        private GameObject prePrefabObj;
        private GameObject mtRoom;
        private List<GameObject> hiddenObjs = new List<GameObject>();
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
        private List<LinearSetting> linearSettings;
        private List<PlanarMotorSetting> pmSettings;
        private List<ConveyerSetting> cvSettings;
        private List<WorkCreateSetting> wkSettings;
        private List<WorkDeleteSetting> wkDeleteSettings;
        private List<SensorSetting> sensorSettings;
        private List<SuctionSetting> suctionSettings;
        private List<ShapeSetting> shapeSettings;
        private List<ExMechSetting> exMechSettings;
        private List<BacketSetting> backetSettings;
        private List<SwitchSetting> switchSettings;
        private List<SignalTowerSetting> towerSettings;
        private List<LedSetting> ledSettings;
        private List<PrefabSetting> prefabSettings;
        private List<CardboardSetting> cardboardSettings;
        private List<ChangeOverSetting> changeOverSettings;
        private List<DebugSetting> debugSettings;
        private List<ActionTableData> actionTableDatas;
        private UnitSetting innerUnit;

        // シェーダー
        private HashSet<Material> allMaterials = new HashSet<Material>();
        private HashSet<Material> allLineMaterials = new HashSet<Material>();
        private Shader opaqueShader;
        private Shader transparentShader;
        private Shader linesShader;
        private Shader opaqueDanmen;
        private Shader transparentDanmen;

        // スライス用プレーン
        private GameObject slicePlane;

        // パラメータ描画用
        private GameObject canvaObj;

        // メニュー一覧
        private GameObject uiInfoMenu;
        private CanvasMenuInfoScript menuInfoScript;

        private GameObject uiInfoPrefab;
        private CanvasPrefabInfoScript prefabInfoScript;

        // プログレスバー
        private int devMax = 4;
        private GameObject uiProgress;
        private Slider prgSlider;
        private TextMeshProUGUI prgText;
        private TextMeshProUGUI prgText2;

        private bool isLines = false;
        private GlobalScript.ClipInfo clipInfo = new();

        // マルチオブジェクトファクトリー
        MultiObjectFactoryScript multiObjectFactory;

        void Awake()
        {
            // 精神と時の部屋取得
            mtRoom = transform.parent.GetComponentsInChildren<Transform>(true).Where(d => d.name == "精神と時の部屋").First().gameObject;
            GlobalScript.isXRMode = transform.parent.GetComponentsInChildren<Transform>().Where(d => (d.name == "VRSetting") || (d.name == "MRSetting")).FirstOrDefault() != null;
            GlobalScript.isXRPrefab = false;

            // メインプロセス実行
            globalSetting = FindObjectsByType<GameObject>(FindObjectsSortMode.None).Where(d => d.name == "GlobalSetting").ToList()[0];
            globalSetting.AddComponent<MainProcess>();

            // マルチオブジェクトファクトリー作成
            multiObjectFactory =  globalSetting.AddComponent<MultiObjectFactoryScript>();

            CommonFunction.DebugLog($"***** Start Load *****");

            // シェーダーロード
            linesShader = Shader.Find("Shader Graphs/LinesShader");
            opaqueShader = Shader.Find("Shader Graphs/OpaqueShader");
            transparentShader = Shader.Find("Shader Graphs/TransparentShader");
            opaqueDanmen = Shader.Find("Shader Graphs/OpaqueDANMEN");
            transparentDanmen = Shader.Find("Shader Graphs/TransparentDANMEN");

            // スライス用プレーン取得
            slicePlane = FindObjectsByType<GameObject>(FindObjectsSortMode.None).Where(d => d.name == "SlicePlane").ToList()[0];

            isLines = GlobalScript.isLiens;
            clipInfo.isOn = GlobalScript.clipInfo.isOn;
            clipInfo.isRvs = GlobalScript.clipInfo.isRvs;
            clipInfo.mode = GlobalScript.clipInfo.mode;
            clipInfo.x = GlobalScript.clipInfo.x;
            clipInfo.y = GlobalScript.clipInfo.y;
            clipInfo.z = GlobalScript.clipInfo.z;

            // キャンバス生成
            CreateCanvas();

            // ロード開始
            StartCoroutine(LoadParameter());
        }

        private void Update()
        {
            // 線表示更新
            RenewLines();

            // 断面表示更新
            RenewDanmen();
        }

        private void OnEnable()
        {
            InputManager.Instance.RegisterKey(Key.F5, HandleKey);
            InputManager.Instance.RegisterKey(Key.F12, HandleKey);
        }

        private void OnDisable()
        {
            InputManager.Instance.UnregisterKey(Key.F5, HandleKey);
            InputManager.Instance.UnregisterKey(Key.F12, HandleKey);
        }

        /// <summary>
        /// パラメータロード
        /// </summary>
        /// <returns></returns>
        private IEnumerator LoadParameter(bool isEditMode = false)
        {
            // ロード開始
            GlobalScript.isLoading = true;

            // デバッグ時間開始
            CommonFunction.DebugInfoInit();
            CommonFunction.DebugLog($"***** Load Start *****", true);
            SetProgress(0, devMax, 0);
            SetProgressLabel("Loading Prefab Files");
            // ロード中はフレームレートを下げる（WebGL/Windows 両方・F5も同様）：重いシーンの毎フレーム描画が
            // 単一スレッドのロード処理とCPUを取り合うため、描画を抑えるとロードが大幅に速くなる。完了後に戻す。
            {
                int loadFps = (GlobalScript.webGlSetting != null) ? GlobalScript.webGlSetting.loadFrameRate : 1;
                if (loadFps > 0)
                {
                    QualitySettings.vSyncCount = 0;   // vSync有効だと targetFrameRate が無視される
                    Application.targetFrameRate = loadFps;
                }
                Debug.Log($"[FrameRate] ロード中 targetFrameRate={Application.targetFrameRate} (loadFrameRate設定={loadFps})");
            }

            // データ削除
            yield return null; // 1フレーム待
            GlobalScript.ClearDictionary();
            yield return null; // 1フレーム待

            // 必要オブジェクト作成
            prefabObj = new GameObject("PrefabObjects");
            deviceObj = new GameObject("DeviceObjects");
            if (prePrefabObj == null)
            {
                prePrefabObj = new GameObject("PreLoadPrefab");
            }
            {
                // 各種設定ファイルロード
                CommonFunction.DebugLog($"***** Parameter Load *****");

                // Taskを実行
                var task = LoadParameterFiles();
                // 完了するまで待つ
                yield return new WaitUntil(() => task.IsCompleted);
                if (prefabSettings == null)
                {
                    SetProgressLabel("Parameter Files Not Found");
                    yield break;
                }

                CommonFunction.DebugLog($"***** Load Prefab Model *****");
                yield return StartCoroutine(LoadPrefabModel());

                // スイッチモデルロード
                if (switchPrefabs.Count == 0)
                {
                    switchPrefabs = GlobalScript.LoadSwitchModel();
                }
                // シグナルタワーモデルロード
                if (towerPrefabs.Count == 0)
                {
                    towerPrefabs = GlobalScript.LoadSignalTowerModel();
                }
            }
            {
                // 折り返し用データ
                CommonFunction.DebugLog($"***** Set Debug Info *****");
                SetDebugComInfo();

                CommonFunction.DebugLog($"***** Set Database *****");
                SetDatabaseSetting();

                yield return null; // 1フレーム待

                if (isEditMode)
                {
                    // 編集モード
                    movableObjs.Clear();
                    undefinedUnits.Clear();
                }
                else
                {
                    // 無視オブジェクト更新
                    RenewHiddenObjs();

                    // ワーク作成
                    CreateWork();

                    // 段ボール作成
                    CreateCardboard();

                    // ワークセット
                    foreach (var work in works)
                    {
                        GlobalScript.works[work.key] = work.obj;
                    }

                    // ユニットにDB設定を保持
                    foreach (var unitSetting in unitSettings)
                    {
                        // ユニット設定にDB情報セット
                        var db = postgresSettings.Find(d => d.No == unitSetting.dbNo);
                        if (db != null)
                        {
                            unitSetting.Database = db.Name;
                        }
                    }
                    // 全オブジェクト名を一度だけ取得（ToList(275k)＋per設定の Find(.name) を回避）
                    var existingNames = new HashSet<string>();
                    foreach (var g in FindObjectsByType<GameObject>(FindObjectsSortMode.None))
                    {
                        existingNames.Add(g.name);
                    }

                    // ユニットオブジェクト先に生成しておく
                    CreateUnitObject();

                    // 存在しないスイッチモデル生成
                    CreateSwitchModel(existingNames);

                    // 存在しないシグナルタワーモデル生成
                    CreateSinalTowerModel(existingNames);

                    // 親モデルに動作スクリプトを付与
                    CommonFunction.DebugLog($"***** Load Units *****", true);
                    // Unit/Organize ループは実処理が軽く、フレーム待ち(yield)が主体（各～8回）。
                    // 極低fps(loadFrameRate=1)のままだと 1yield≒1秒 になり極端に遅くなるため、この区間だけ
                    // 描画の自然速度(上限30fps)に戻し yield を安価にする。重いInstantiateは既に完了しており低fpsの恩恵はない。
                    Application.targetFrameRate = 30;
                    Debug.Log($"[FrameRate] Unit整理中は yield多のため targetFrameRate=30 に一時変更");
                    int ui = -1;
                    foreach (var unitSetting in unitSettings)
                    {
                        ui++;   // 進捗用インデックス（unitSettings.IndexOf の O(N²) を回避）
                        if (prefabs.Count == 0)
                        {
                            continue;
                        }
                        SetProgressLabel($"Loading Unit : {unitSetting.name}");
                        // デバッグ用
                        innerUnit = unitSetting;
                        unitSetting.childrenObject = new List<GameObject>();

                        var gameObjects = new List<GameObject>();
                        if (unitSetting.moveObject != null)
                        {
                            //　子モデルセット
                            foreach (var child in unitSetting.children)
                            {
                                if (child.childObject != null)
                                {
                                    unitSetting.childrenObject.Add(child.childObject);
                                }
                            }
                            // 非表示オブジェクトなら非表示から削除
                            hiddenObjs.Remove(unitSetting.moveObject);
                            // ロボット紐づけ
                            unitSetting.robotSetting = robotSettings.Find(d => (d.mechId == unitSetting.mechId) && (d.name == unitSetting.name));
                            // リニア紐づけ
                            unitSetting.linearSetting = linearSettings.Find(d => (d.mechId == unitSetting.mechId) && (d.name == unitSetting.name));
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
                            // バケット設定紐づけ
                            unitSetting.backetSetting = backetSettings.Find(d => (d.mechId == unitSetting.mechId) && (d.name == unitSetting.name));
                            // 型替え設定紐づけ
                            unitSetting.changeOverSetting = changeOverSettings.Find(d => (d.mechId == unitSetting.mechId) && (d.name == unitSetting.name));
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
                                        // ロボットタイプ判別
                                        var roboType = GetRobotType(unitSetting);
                                        robo.headUnit = unitSettings.Find(d => d.name == robo.head);
                                        if (robo.isTm)
                                        {
                                            // タイムチャートユニットセット
                                            robo.tmUnits.Add(unitSettings.Find(d => d.name == robo.tmUnitNames[0]));
                                            robo.tmUnits.Add(unitSettings.Find(d => d.name == robo.tmUnitNames[1]));
                                            robo.tmUnits.Add(unitSettings.Find(d => d.name == robo.tmUnitNames[2]));
                                            robo.tmUnits.Add(unitSettings.Find(d => d.name == robo.tmUnitNames[3]));
                                            robo.tmUnits.Add(unitSettings.Find(d => d.name == robo.tmUnitNames[4]));
                                            robo.tmUnits.Add(unitSettings.Find(d => d.name == robo.tmUnitNames[5]));
                                        }
                                        else
                                        {
                                            // 空ユニットセット
                                            robo.tmUnits.Add(null);
                                            robo.tmUnits.Add(null);
                                            robo.tmUnits.Add(null);
                                            robo.tmUnits.Add(null);
                                            robo.tmUnits.Add(null);
                                            robo.tmUnits.Add(null);
                                        }
                                        if (roboType == RobotType.ARM)
                                        {
                                            var rObj = unitSetting.moveObject.AddComponent<ArmRobot>();
                                            rObj.SetParameter(unitSetting, robo);
                                        }
                                        else if (roboType == RobotType.CEILING_ARM)
                                        {
                                            var rObj = unitSetting.moveObject.AddComponent<CeilingArmRobot>();
                                            rObj.SetParameter(unitSetting, robo);
                                        }
                                        else if (roboType == RobotType.MPS2_3AS)
                                        {
                                            var rObj = unitSetting.moveObject.AddComponent<MPS2_3AS>();
                                            rObj.SetParameter(unitSetting, robo);
                                        }
                                        else if (roboType == RobotType.MPS2_4AS)
                                        {
                                            var rObj = unitSetting.moveObject.AddComponent<MPS2_4AS>();
                                            rObj.SetParameter(unitSetting, robo);
                                        }
                                        else if (roboType == RobotType.MPX_PI)
                                        {
                                            var rObj = unitSetting.moveObject.AddComponent<MPX_PI>();
                                            rObj.SetParameter(unitSetting, robo);
                                        }
                                        else if (roboType == RobotType.MPX_R6)
                                        {
                                            var rObj = unitSetting.moveObject.AddComponent<MPX_R6>();
                                            rObj.SetParameter(unitSetting, robo);
                                        }
                                        else if (roboType == RobotType.MPX_R3)
                                        {
                                            var rObj = unitSetting.moveObject.AddComponent<MPX_R3>();
                                            rObj.SetParameter(unitSetting, robo);
                                        }
                                        else if (roboType == RobotType.MPX_R3S)
                                        {
                                            var rObj = unitSetting.moveObject.AddComponent<MPX_R3S>();
                                            rObj.SetParameter(unitSetting, robo);
                                        }
                                        else if (roboType == RobotType.YF03N4)
                                        {
                                            var rObj = unitSetting.moveObject.AddComponent<YF03N4>();
                                            rObj.SetParameter(unitSetting, robo);
                                        }
                                        else if (roboType == RobotType.RS007L)
                                        {
                                        }
                                        else if (roboType == RobotType.CRX_30iA)
                                        {
                                            var rObj = unitSetting.moveObject.AddComponent<CRX_30iA>();
                                            rObj.SetParameter(unitSetting, robo);
                                        }
                                    }
                                    else
                                    {
                                        Debug.Log($"エラー：ユニット名(ロボット名)「{unitSetting.name}」の動作設定が存在しません。");
                                    }
                                }
                                else if (unitSetting.actionSetting.isLinear)
                                {
                                    // リニアなら
                                    var instance = unitSetting.unitObject.AddComponent<MotionLinear>();
                                    // チャック設定更新
                                    var linearSetting = linearSettings.Find(d => (d.mechId == unitSetting.mechId) && (d.name == unitSetting.name));
                                    instance.SetUnitSettings(unitSetting, chuckSetting, linearSetting);
                                }
                                else if (unitSetting.actionSetting.isPlanarMotor)
                                {
                                    // 平面リニアなら(再構築のみ)
                                    var instance = unitSetting.unitObject.AddComponent<AxisMotionBase>();
                                    instance.SetUnitSettings(unitSetting, chuckSetting);
                                    var pm = pmSettings.Find(d => (d.mechId == unitSetting.mechId) && (d.name == unitSetting.name));
                                    if (pm != null)
                                    {
                                        pm.moverUnit = unitSettings.Find(d => d.name == pm.mover);
                                        var pmObj = unitSetting.unitObject.AddComponent<Br6DScript>();
                                        pmObj.SetParameter(unitSetting, pm);
                                    }
                                    else
                                    {
                                        Debug.Log($"エラー：ユニット名(平面リニア名)「{unitSetting.name}」の動作設定が存在しません。");
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
                                else if (unitSetting.actionSetting.isChangeOver)
                                {
                                    // 型替え部品なら
                                    var instance = unitSetting.unitObject.AddComponent<MotionChangeOver>();
                                    instance.SetUnitSettings(unitSetting, chuckSetting, unitSetting.changeOverSetting);
                                }
                            }
                            else
                            {
                                // 動作設定なし
                                var isFamiry = unitSettings.Find(d => d.children.Find(x => x.name == unitSetting.name) != null) != null;
                                var isChuck = chuckUnitSettings.Find(d => d.children.Find(x => x.name == unitSetting.name) != null) != null;
                                if (isFamiry ||                                     // 親子関係あり
                                    (!unitSetting.sync && (unitSetting.parent != "")) ||             // ※実質全て？同期ユニットがおかしくなるので有効にするなら対策必要
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
                        else if(unitSetting.parent != "")
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
                        if (SetProgress(2, devMax, (float)ui / unitSettings.Count))
                        {
                            yield return null; // 1フレーム待
                        }
                        CommonFunction.DebugLog($"***** {unitSetting.name} Loaded *****", false);
                    }

                    CommonFunction.DebugLog($"***** Organize Units *****", true);
                    yield return null; // 1フレーム待(下のオブジェクト取得時にNULLにならないようにするために必要)
                    // 使い勝手向上のため動作可能オブジェクトを移動
                    // 全GameObjectを ToList せず単一パスで抽出（275k要素のList二重確保を回避）
                    string movablePrefix = movableName + "_";
                    var allMobableObjs = new List<GameObject>();
                    foreach (var g in FindObjectsByType<GameObject>(FindObjectsSortMode.None))
                    {
                        if (g.name.Contains(movablePrefix))
                        {
                            allMobableObjs.Add(g);
                        }
                    }
                    // 名前順にソート
                    allMobableObjs.Sort((a, b) => a.transform.parent.name.CompareTo(b.transform.parent.name));
                    var moveObjs = new List<GameObject>();
                    for (int mi = 0; mi < allMobableObjs.Count; mi++)   // IndexOf(O(N²)) 回避
                    {
                        var obj = allMobableObjs[mi];
                        if (obj.IsDestroyed())
                        {
                            continue;
                        }
                        // 祖先のいずれかが movable な直下の子を持つなら、このobjは最上流ではない
                        // （元: GetComponentsInChildren(全子孫)+Where+ToList → 早期exitのforeach＋直下の子のみ走査）
                        var selfParent = obj.transform.parent;
                        bool isFind = false;
                        foreach (var p in selfParent.GetComponentsInParent<Transform>())
                        {
                            if (p.parent == null || p == selfParent)
                            {
                                continue;   // ルート/自分の親は除外（元の Where(parent!=null)+Remove(self) と等価）
                            }
                            foreach (var d in p.GetComponentsInChildren<Transform>())
                            {
                                if (d.parent == p && d.name.Contains(movablePrefix))
                                {
                                    isFind = true;
                                    break;
                                }
                            }
                            if (isFind)
                            {
                                break;
                            }
                        }
                        if (!isFind)
                        {
                            moveObjs.Add(obj);   // 最上流の動作可能親オブジェクト
                        }
                        SetProgressLabel($"Organize Unit : {obj.transform.parent.name}");
                        if (SetProgress(3, devMax, (float)mi / allMobableObjs.Count))
                        {
                            yield return null; // 1フレーム待
                        }
                    }
                    // 以降は重い同期処理（再親子付け/多数Destroy/コライダー生成）に戻るため、再び低fpsへ復帰。
                    {
                        int loadFps = (GlobalScript.webGlSetting != null) ? GlobalScript.webGlSetting.loadFrameRate : 1;
                        if (loadFps > 0) { Application.targetFrameRate = loadFps; }
                        Debug.Log($"[FrameRate] Unit整理完了、低fpsへ復帰 targetFrameRate={Application.targetFrameRate}");
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
                    Destroy(deviceObj);

                    // 非表示処理
                    {
                        foreach (var o in hiddenObjs)
                        {
                            if (!o.IsDestroyed())
                            {
                                o.SetActive(false);
                            }
                        }
                    }
                }
                // コライダーセット
                {
                    //  描画エリア取得
                    var renderers = prefabObj.GetComponentsInChildren<Renderer>().ToList();
                    if (renderers.Count > 0)
                    {
                        // 最初のRendererで初期化
                        GlobalScript.clipInfo.bounds = renderers[0].bounds;
                        // 残りのRendererを包含
                        foreach (Renderer rend in renderers)
                        {
                            GlobalScript.clipInfo.bounds.Encapsulate(rend.bounds);
                        }
                    }
                    // BoxCollider作成(プレハブオブジェクトとセンサは反応させない)
                    CreateBoxCollider(renderers, true);

                    // 動作オブジェクト追加
                    renderers = new();
                    foreach (var m in movableObjs)
                    {
                        renderers.AddRange(m.obj.GetComponentsInChildren<Renderer>().ToList());
                    }
                    // BoxCollider作成
                    CreateBoxCollider(renderers, false);
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
                        if ((Application.platform == RuntimePlatform.Android) || (Application.platform == RuntimePlatform.IPhonePlayer))// || GlobalScript.isVRPrefab)
                        {
                            // 透明オブジェクトチェック
                            Material material = renderers[i].sharedMaterial;
                            if (material != null)
                            {
                                if (material.HasProperty("_Mode"))
                                {
                                    // _Modeプロパティの値を取得
                                    if (material.GetFloat("_Mode") == 3f)
                                    {
                                        // 透明は非表示
                                        renderers[i].gameObject.SetActive(false);
                                    }
                                }
                                /*
                                else if (material.HasColor("_BaseColor"))
                                {
                                    if (material.GetColor("_BaseColor").a < 1f)
                                    {
                                        // 透明は非表示
                                        renderers[i].gameObject.SetActive(false);
                                    }
                                }
                                */
                            }
                        }
                    }
                    // 静的バッチングを実行（親にまとめてバッチング）※tri数が多くなるのと静的バッチングが実行されないので無効化
                    //                    StaticBatchingUtility.Combine(batchTargets, prefabObj);
                }

                // VRならPrefab非表示にしておく
                if ((Application.platform == RuntimePlatform.Android) || (Application.platform == RuntimePlatform.IPhonePlayer))
                {
                    prefabObj.SetActive(false);
                }
                else if (GlobalScript.buildConfig.isCollision)
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
            // 全てのマテリアル更新
            RefreshAllMaterials();

            //　プログレスバー終了
            if (SetProgress(devMax, devMax, 0))
            {
                yield return null; // 1フレーム待
            }

            // イベント登録
            menuInfoScript.SetEvents(unitSettings);
            prefabInfoScript.SetEvents();

            GlobalScript.isLoading = false;
            GlobalScript.isLoaded = true;
            // ロード完了後、実行時フレームレートに戻す。WebGLモード(実機 or EditorのWebGLテストトグルON)は
            // WebGlSetting.targetFrameRate(既定30)、それ以外(Windows/Android/通常Editor)は120。
#if UNITY_WEBGL && !UNITY_EDITOR
            bool webglMode = true;
#elif UNITY_EDITOR
            bool webglMode = UnityEditor.EditorPrefs.GetBool("KMX_EditorWebGLMode", false);
#else
            bool webglMode = false;
#endif
            if (webglMode)
            {
                int runFps = (GlobalScript.webGlSetting != null) ? GlobalScript.webGlSetting.targetFrameRate : 30;
                Application.targetFrameRate = runFps > 0 ? runFps : 120;
            }
            else
            {
                Application.targetFrameRate = 120;
            }
            Debug.Log($"[FrameRate] 実行時 targetFrameRate={Application.targetFrameRate} (webglMode={webglMode})");
            CommonFunction.DebugLog($"***** Load Finished *****", true);
        }

        /// <summary>
        /// 全てのマテリアル更新
        /// </summary>
        private void RefreshAllMaterials()
        {
            if (allMaterials.Count == 0)
            {
                var objs = new List<GameObject>();
                objs.Add(prefabObj);
                objs.AddRange(movableObjs.Where(d => d.obj != null).Select(d => d.obj));
                foreach (var obj in objs)
                {
                    // シェーダーセット
                    foreach (Renderer renderer in obj.transform.GetComponentsInChildren<Renderer>())
                    {
                        if ((renderer.GetComponentInParent<SwitchScript>() != null) || 
                            (renderer.GetComponentInParent<SignalTowerScript>() != null))
                        {
                            // スイッチかシグナルタワーなら光らなくなるので無視
                            continue;
                        }
                        foreach (Material mat in renderer.sharedMaterials)
                        {
                            if (mat != null)
                            {
                                if (mat.name.Contains("Default Line Material"))
                                {
                                    if (!allLineMaterials.Contains(mat))
                                    {
                                        allLineMaterials.Add(mat);
                                    }
                                }
                                else
                                {
                                    if (!allMaterials.Contains(mat))
                                    {
                                        allMaterials.Add(mat);
                                    }
                                }
                            }
                        }
                    }
                }
                foreach (var mat in allMaterials)
                {
                    if (mat.HasProperty("_Surface"))
                    {
                        float surface = mat.GetFloat("_Surface");
                        bool isTransparent = surface > 0.5f;
                        if (isTransparent)
                        {
                            mat.shader = transparentShader;
                        }
                        else
                        {
                            mat.shader = opaqueShader;
                        }
                    }
                    else if (mat.shader.name.Contains("Transparent"))
                    {
                        mat.shader = transparentShader;
                    }
                    else if (mat.shader.name.Contains("Opaque"))
                    {
                        mat.shader = opaqueShader;
                    }
                }
                foreach (var mat in allLineMaterials)
                {
                    mat.shader = linesShader;
                    mat.SetColor("_Color", new Color(0, 0, 0, 1f));
                    mat.SetFloat("_Alpha", GlobalScript.isLiens ? 0.5f : 0f);
                }
            }
        }

        /// <summary>
        /// 動作パラメータのみ更新
        /// </summary>
        /// <returns></returns>
        private IEnumerator LoadActParameter()
        {
            CommonFunction.DebugInfoInit();
            CommonFunction.DebugLog($"***** Load Start *****", true);
            var motions = new List<AxisMotionBase>();
            var works = new List<ObjectScript>();

            // ユニット設定を保持
            var dctUnitSetting = new Dictionary<string, GameObject>();
            foreach (var setting in unitSettings)
            {
                dctUnitSetting.Add(setting.name, setting.unitObject);
            }

            // Taskを実行
            var task = LoadParameterFiles();
            // 完了するまで待つ
            yield return new WaitUntil(() => task.IsCompleted);

            foreach (var obj in movableObjs)
            {
                motions.AddRange(obj.obj.GetComponentsInChildren<AxisMotionBase>().ToList());
                works.AddRange(obj.obj.GetComponentsInChildren<ObjectScript>().ToList());
            }
            foreach (var work in works)
            {
                Destroy(work.gameObject);
            }
            // ユニット設定戻し
            foreach (var unitSetting in unitSettings)
            {
                if (dctUnitSetting.ContainsKey(unitSetting.name))
                {
                    unitSetting.unitObject = dctUnitSetting[unitSetting.name];
                }
            }
            multiObjectFactory.DeleteSetting();

            // 通信設定
            SetDatabaseSetting();
            foreach (var unitSetting in unitSettings)
            {
                var motion = motions.Find(d => (d.unitSetting.mechId == unitSetting.mechId) && (d.unitSetting.name == unitSetting.name));
                if (motion != null)
                {
                    // ロボット紐づけ
                    motion.unitSetting.robotSetting = robotSettings.Find(d => (d.mechId == unitSetting.mechId) && (d.name == unitSetting.name));
                    // リニア紐づけ
                    unitSetting.linearSetting = linearSettings.Find(d => (d.mechId == unitSetting.mechId) && (d.name == unitSetting.name));
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
                    // バケット設定紐づけ
                    unitSetting.backetSetting = backetSettings.Find(d => (d.mechId == unitSetting.mechId) && (d.name == unitSetting.name));
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

                    // 単軸動作以外かチェック
                    if ((motion.GetType()== typeof(AxisMotionBase)) && (motion.unitSetting.actionSetting != null))
                    {
                        var mobj = motion.unitSetting.moveObject.GetComponent<KinematicsBase>();
                        if (motion != null)
                        {
                            if (motion.unitSetting.actionSetting.isRobo)
                            {
                                // ロボットタイプ取得
                                var robo = robotSettings.Find(d => (d.mechId == unitSetting.mechId) && (d.name == unitSetting.name));
                                if (robo != null)
                                {
                                    robo.headUnit = unitSettings.Find(d => d.name == robo.head);
                                    if (robo.isTm)
                                    {
                                        // タイムチャートユニットセット
                                        robo.tmUnits.Add(unitSettings.Find(d => d.name == robo.tmUnitNames[0]));
                                        robo.tmUnits.Add(unitSettings.Find(d => d.name == robo.tmUnitNames[1]));
                                        robo.tmUnits.Add(unitSettings.Find(d => d.name == robo.tmUnitNames[2]));
                                        robo.tmUnits.Add(unitSettings.Find(d => d.name == robo.tmUnitNames[3]));
                                        robo.tmUnits.Add(unitSettings.Find(d => d.name == robo.tmUnitNames[4]));
                                        robo.tmUnits.Add(unitSettings.Find(d => d.name == robo.tmUnitNames[5]));
                                    }
                                    else
                                    {
                                        // 空ユニットセット
                                        robo.tmUnits.Add(null);
                                        robo.tmUnits.Add(null);
                                        robo.tmUnits.Add(null);
                                        robo.tmUnits.Add(null);
                                        robo.tmUnits.Add(null);
                                        robo.tmUnits.Add(null);
                                    }
                                    mobj.SetParameter(motion.unitSetting, robo);
                                }
                            }
                            else if (motion.unitSetting.actionSetting.isPlanarMotor)
                            {
                                var pm = pmSettings.Find(d => (d.mechId == unitSetting.mechId) && (d.name == unitSetting.name));
                                if (pm != null)
                                {
                                    var mover = motions.Find(d => (d.unitSetting.mechId == unitSetting.mechId) && (d.unitSetting.name == pm.mover));
                                    pm.moverUnit = mover.unitSetting;
                                    mobj.SetParameter(motion.unitSetting, pm);
                                }
                            }
                            else if (motion.unitSetting.actionSetting.isConveyer)
                            {
                            }
                        }
                    }
                }
            }
            // メニュー設定
            menuInfoScript.SetEvents(unitSettings);

            Resources.UnloadUnusedAssets();
            CommonFunction.DebugLog($"***** Load Finished *****", true);
        }

        /// <summary>
        /// パラメータリロード
        /// </summary>
        public void ReloadParameter(bool isEditMode = false)
        {
            if (!GlobalScript.isLoading)
            {
                GlobalScript.isLoading = true;
                GlobalScript.isLoaded = false;
                CommonFunction.DebugLog($"Start Reload");
                // 情報削除
                DeleteObjects();
                // ロード開始
                StartCoroutine(LoadParameter(isEditMode));
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
                // 情報削除
                foreach (var obj in globalSetting.GetComponentsInChildren<ComBaseScript>())
                {
                    Destroy(obj);
                }
                // ComHmi も破棄（重複/WS多重接続を防止）
                foreach (var obj in globalSetting.GetComponentsInChildren<ComHmi>())
                {
                    Destroy(obj);
                }
                StartCoroutine(LoadActParameter());
                GlobalScript.isLoading = false;
                GlobalScript.isLoaded = true;
                GlobalScript.isReqLoadEvent = true;
            }
        }

        /// <summary>
        /// オブジェクト削除
        /// </summary>
        private void DeleteObjects()
        {
            foreach (var obj in globalSetting.GetComponentsInChildren<ComBaseScript>())
            {
                Destroy(obj);
            }
            // ComHmi は ComBaseScript 非継承のため別途破棄（リロードでの重複/WS多重接続を防止）
            foreach (var obj in globalSetting.GetComponentsInChildren<ComHmi>())
            {
                Destroy(obj);
            }
            /*
            foreach (var obj in globalSetting.GetComponentsInChildren<Br6DScript>())
            {
                Destroy(obj);
            }
            */
            foreach (var obj in switchModel)
            {
                CommonFunction.DestroyWithMaterials(obj);
            }
            foreach (var obj in towerModel)
            {
                CommonFunction.DestroyWithMaterials(obj);
            }

            CommonFunction.DestroyWithMaterials(prefabObj);
            foreach (var obj in movableObjs)
            {
                CommonFunction.DestroyWithMaterials(obj.obj);
            }
        }

        /// <summary>
        /// パラメータロード
        /// </summary>
        private async Task LoadParameterFiles()
        {
            // 全JSONを並列ロード（WebGLは各 await が HTTP往復＝逐次だと初回が遅い→同時発行でブラウザが並行fetch）。
            // ※非WebGLは同期読込なので実質逐次（害なし）。ファイル間に依存は無いので並列で安全。
            var tPostgres = GlobalScript.LoadListJson<List<PostgresSetting>>("Postgres");
            var tDataEx = GlobalScript.LoadListJson<List<DataExchangeSetting>>("DataExchangeInfo");
            var tUnit = GlobalScript.LoadListJson<List<UnitSetting>>("UnitInfo");
            var tAction = GlobalScript.LoadListJson<List<UnitActionSetting>>("ActionInfo");
            var tInner = GlobalScript.LoadListJson<List<InnerProcessSetting>>("InnerProcess");
            var tHidden = GlobalScript.LoadListJson<List<HiddenUnit>>("HiddenUnitInfo");
            var tChuck = GlobalScript.LoadListJson<List<ChuckUnitSetting>>("ChuckUnitInfo");
            var tRobot = GlobalScript.LoadListJson<List<RobotSetting>>("RobotInfo");
            var tPm = GlobalScript.LoadListJson<List<PlanarMotorSetting>>("PlanarMotorInfo");
            var tCv = GlobalScript.LoadListJson<List<ConveyerSetting>>("ConveyerInfo");
            var tWk = GlobalScript.LoadListJson<List<WorkCreateSetting>>("WorkCreateInfo");
            var tWkDel = GlobalScript.LoadListJson<List<WorkDeleteSetting>>("WorkDeleteInfo");
            var tSensor = GlobalScript.LoadListJson<List<SensorSetting>>("SensorInfo");
            var tSuction = GlobalScript.LoadListJson<List<SuctionSetting>>("SuctionInfo");
            var tShape = GlobalScript.LoadListJson<List<ShapeSetting>>("ShapeInfo");
            var tExMech = GlobalScript.LoadListJson<List<ExMechSetting>>("ExMechInfo");
            var tBacket = GlobalScript.LoadListJson<List<BacketSetting>>("BacketInfo");
            var tLinear = GlobalScript.LoadListJson<List<LinearSetting>>("LinearInfo");
            var tSwitch = GlobalScript.LoadListJson<List<SwitchSetting>>("SwitchInfo");
            var tTower = GlobalScript.LoadListJson<List<SignalTowerSetting>>("SignalTowerInfo");
            var tLed = GlobalScript.LoadListJson<List<LedSetting>>("LedInfo");
            var tPrefab = GlobalScript.LoadListJson<List<PrefabSetting>>("PrefabInfo");
            var tCardboard = GlobalScript.LoadListJson<List<CardboardSetting>>("CardboardInfo");
            var tChangeOver = GlobalScript.LoadListJson<List<ChangeOverSetting>>("ChangeOverInfo");
            var tDebug = GlobalScript.LoadListJson<List<DebugSetting>>("DebugInfo");
            var tBuildConfig = GlobalScript.LoadListJson<BuildConfig>("BuildConfig");
            var tActionTable = GlobalScript.LoadListJson<List<ActionTableData>>("ActionTableInfo");
            var tUseDevice = GlobalScript.LoadListJson<List<UseDeviceData>>("UseDeviceList");
            var tHmx = GlobalScript.LoadListJson<HmxLinkSetting>("HmxLink");
            var tMo = GlobalScript.LoadListJson<List<ManualOpData>>("ManualOpInfo");
            var tTimeChart = GlobalScript.LoadListJson<List<TimeChartData>>("TimeChartDataList");
            var tWebGl = GlobalScript.LoadListJson<WebGlSetting>("WebGlSetting");

            postgresSettings = (List<PostgresSetting>)await tPostgres;
            dataExSettings = (List<DataExchangeSetting>)await tDataEx;
            unitSettings = (List<UnitSetting>)await tUnit;
            actionSettings = (List<UnitActionSetting>)await tAction;
            innerSettings = (List<InnerProcessSetting>)await tInner;
            hiddenSettings = (List<HiddenUnit>)await tHidden;
            chuckUnitSettings = (List<ChuckUnitSetting>)await tChuck;
            robotSettings = (List<RobotSetting>)await tRobot;
            pmSettings = (List<PlanarMotorSetting>)await tPm;
            cvSettings = (List<ConveyerSetting>)await tCv;
            wkSettings = (List<WorkCreateSetting>)await tWk;
            wkDeleteSettings = (List<WorkDeleteSetting>)await tWkDel;
            sensorSettings = (List<SensorSetting>)await tSensor;
            suctionSettings = (List<SuctionSetting>)await tSuction;
            shapeSettings = (List<ShapeSetting>)await tShape;
            exMechSettings = (List<ExMechSetting>)await tExMech;
            backetSettings = (List<BacketSetting>)await tBacket;
            linearSettings = (List<LinearSetting>)await tLinear;
            switchSettings = (List<SwitchSetting>)await tSwitch;
            towerSettings = (List<SignalTowerSetting>)await tTower;
            ledSettings = (List<LedSetting>)await tLed;
            prefabSettings = (List<PrefabSetting>)await tPrefab;
            cardboardSettings = (List<CardboardSetting>)await tCardboard;
            changeOverSettings = (List<ChangeOverSetting>)await tChangeOver;
            debugSettings = (List<DebugSetting>)await tDebug;
            GlobalScript.buildConfig = (BuildConfig)await tBuildConfig;
            actionTableDatas = (List<ActionTableData>)await tActionTable;
            GlobalScript.useDeviceDatas = (List<UseDeviceData>)await tUseDevice;
            try
            {
                // hmx-link（デジタルツイン）設定。無ければ既定(無効)のまま
                var hmx = await tHmx;
                if (hmx != null)
                {
                    GlobalScript.hmxLink = (HmxLinkSetting)hmx;
                }
            }
            catch { }
            try
            {
                // WebGL 専用設定（フレームレート等）。無ければ既定値のまま。
                var wg = await tWebGl;
                if (wg != null)
                {
                    GlobalScript.webGlSetting = (WebGlSetting)wg;
                }
            }
            catch { }
            // プラットフォームで enabled を上書き。WebGL以外は HmxLink.json の enabled を無視（＝無効）。
#if UNITY_WEBGL && !UNITY_EDITOR
            if (GlobalScript.hmxLink != null) { GlobalScript.hmxLink.enabled = true; }   // 実WebGL=有効
#elif UNITY_EDITOR
            // Editor は WebGLテストトグル(KMX_EditorWebGLMode)に追従。OFF＝WebGL以外なので enabled 無視（無効）。
            if (GlobalScript.hmxLink != null)
            {
                GlobalScript.hmxLink.enabled = UnityEditor.EditorPrefs.GetBool("KMX_EditorWebGLMode", false);
            }
#else
            if (GlobalScript.hmxLink != null) { GlobalScript.hmxLink.enabled = false; }  // Windows/Android=無効
#endif
            try
            {
                // 手動操作(JOG)定義。無ければ空のまま（手動操作なし）
                var mo = await tMo;
                if (mo != null)
                {
                    GlobalScript.manualOps = (List<ManualOpData>)mo;
                }
            }
            catch { }
            GlobalScript.timeChartDatas = (List<TimeChartData>)await tTimeChart;
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
            if ((group == null) || (group == ""))
            {
                if (gameObjects.Count > 0)
                {
                    g = gameObjects[0];
                    return true;
                }
            }
            else if (group[0] == '*')
            {
                group = group.Substring(1);
                g = gameObjects.Find(d => String.Join("\\", CommonFunction.GetScenePath(d).AsEnumerable().Reverse()).Contains(group));
            }
            else
            {
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
                g = gameObjects.Find(d => CommonFunction.GetScenePath(d).Contains(group));
            }
            return g != null;
        }

        /// <summary>
        /// ロボットタイプを取得する
        /// </summary>
        /// <param name="unitSetting"></param>
        /// <returns></returns>
        private RobotType GetRobotType(UnitSetting unitSetting)
        {
            var children = unitSetting.moveObject.GetComponentsInChildren<Transform>().ToList();
            // パラレルタイプ取得
            return children.Find(d => d.name.Contains("YF03N4_")) != null ? RobotType.YF03N4 :
                   children.Find(d => d.name.Contains("駆動部変則120度")) != null ? RobotType.MPX_PI :
                   children.Find(d => d.name.Contains("MPS2-3AS_")) != null ? RobotType.MPS2_3AS :
                   children.Find(d => d.name.Contains("MPS2-4AS_")) != null ? RobotType.MPS2_4AS : 
                   children.Find(d => d.name.Contains("W0250623-")) != null ? RobotType.MPX_R3 :
                   children.Find(d => d.name.Contains("W0282303-")) != null ? RobotType.MPX_R3S : // 小型
                   children.Find(d => d.name.Contains("W0578936-")) != null ? RobotType.MPX_R6 :
                   children.Find(d => d.name.Contains("W0652706-")) != null ? RobotType.MPX_R6 : // 逆勝手
                   children.Find(d => d.name.Contains("W0334624-")) != null ? RobotType.ARM :
                   children.Find(d => d.name.Contains("W0677866-")) != null ? RobotType.CEILING_ARM :
                   children.Find(d => d.name.Contains("CRX-30IA")) != null ? RobotType.CRX_30iA : 
                   RobotType.UNDEFINED;
        }

        /// <summary>
        /// キャンバス追加
        /// </summary>
        private void CreateCanvas()
        {
            // キャンバス取得
            var canvasObjs = FindObjectsByType<GameObject>(FindObjectsSortMode.None).Where(d => d.name == "Canvas").ToList();
            canvaObj = canvasObjs.Count == 0 ? new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster)) : canvasObjs[0];

            // プログレスバー
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

            // メニュー表示
            var menu = GlobalScript.LoadPrefabObject("Prefabs/Canvas", "InfoMenu");
            if (menu.Count > 0)
            {
                uiInfoMenu = Instantiate(menu[0]);
                uiInfoMenu.transform.SetParent(canvaObj.transform, false);
                menuInfoScript = uiInfoMenu.AddComponent<CanvasMenuInfoScript>();
                uiInfoMenu.AddComponent<WebGlHide>();   // WebGLビルドでは左下メニュー(InfoMenu)を自動非表示
            }

            // Prefab表示
            var prefab = GlobalScript.LoadPrefabObject("Prefabs/Canvas", "InfoPrefab");
            if (prefab.Count > 0)
            {
                uiInfoPrefab = Instantiate(prefab[0]);
                uiInfoPrefab.transform.SetParent(canvaObj.transform, false);
                prefabInfoScript = uiInfoPrefab.AddComponent<CanvasPrefabInfoScript>();
            }
        }

        /// <summary>
        /// プログレスバーセット
        /// </summary>
        /// <param name="value"></param>
        private bool SetProgress(int index, int max, float value)
        {
            if (index == max)
            {
                value = 1f;
            }
            else
            {
                value = (index + value) / max;
            }

            GlobalScript.loadProgress = value;   // ローディング画面(KmxLoadingScreen)へ進捗共有

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
            GlobalScript.loadLabel = text;   // ローディング画面(KmxLoadingScreen)へコメント共有
        }

        /// <summary>
        /// エラー時にUnityを終了させるラッパー
        /// </summary>
        private void EndApplication()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;//ゲームプレイ終了
#else
            Application.Quit();//ゲームプレイ終了
#endif
        }

        /// <summary>
        /// 外部AddressablesからPrefabをロード
        /// </summary>
        private IEnumerator LoadAddressablePrefabs(List<PrefabSetting> prefabSettings, List<GameObject> prefabs)
        {
            // 外部フォルダパスを指定
            string externalPath = "";
            if (Application.isEditor)
            {
                externalPath = Path.GetFullPath(Path.Combine(Application.streamingAssetsPath, "../../ServerData"));
            }
            else
            {
                externalPath = Path.GetFullPath(Path.Combine(Application.streamingAssetsPath, "../ServerData"));
            }
            var dirs = Directory.GetDirectories(externalPath);
            foreach (var dir in dirs)
            {
                string catalogPath = Path.GetFullPath(Path.Combine(dir, "catalog.bin"));
                if (!File.Exists(catalogPath))
                {
                    continue;
                }

                // 初期化
                yield return Addressables.InitializeAsync();

                // 外部カタログをロード
                Addressables.ClearResourceLocators();
                var catalogHandle = Addressables.LoadContentCatalogAsync(catalogPath);
                yield return catalogHandle;


                if (catalogHandle.Status != AsyncOperationStatus.Succeeded)
                {
                    Debug.LogError($"Failed to load catalog at {catalogPath}");
                    continue;
                }

                // Prefabラベルを持つアセットを探す
                var locationsHandle = Addressables.LoadResourceLocationsAsync("Prefab", typeof(GameObject));
                yield return locationsHandle;

                if (locationsHandle.Status != AsyncOperationStatus.Succeeded)
                {
                    Debug.LogError("Failed to load resource locations.");
                    continue;
                }

                // 一括ロード
                int index = 0;
                var loadHandle = Addressables.LoadAssetsAsync<GameObject>(
                    locationsHandle.Result,
                    obj =>
                    {
                        index++;
                        SetProgress(0, devMax, (float)index / locationsHandle.Result.Count);
                        if (obj != null)
                        {
                            if (prefabSettings.Find(d => d.name.Contains(obj.name)) != null)
                            {
                                prefabs.Add(obj);
                            }
                            SetProgressLabel($"Loaded Prefab: {obj.name}");
//                            Debug.Log($"Loaded: {obj.name}.prefab");
                        }
                        else
                        {
                        }
                    }
                );
                yield return loadHandle;

                if (loadHandle.Status == AsyncOperationStatus.Succeeded)
                {
                    //                Debug.Log($"Loaded {prefabs.Count} prefabs from {dir}");
                }
                else
                {
                    //                Debug.LogError("Failed to load prefabs.");
                }
                locationsHandle.Result.Clear();
            }
        }

        /// <summary>
        /// コライダー作成
        /// </summary>
        /// <param name="root"></param>
        private void CreateBoxCollider(List<Renderer> meshRenderers, bool ignore = false)
        {
            foreach (var mr in meshRenderers)
            {
                // Line / 特殊用途は除外
                if (mr.GetComponent<LineRenderer>() != null)
                    continue;

                // 既にBoxColliderがあるならスキップ
                if (mr.GetComponent<BoxCollider>() != null)
                    continue;

                // 既に親がBoxCollider があるならスキップ
                if (mr.GetComponentInParent<BoxCollider>() != null)
                    continue;

                var mf = mr.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null)
                    continue;

                if (!GlobalScript.isXRPrefab)
                {
                    if (mr.GetComponent<Collider>() == null)
                    {
                        if (ignore)
                        {
                            // センサ当たり判定無視スクリプト追加
                            mr.gameObject.AddComponent<IgnoreCollisionScript>();
                        }
                        // ローカル bounds を使用
                        Bounds b = mf.sharedMesh.bounds;
                        var box = mr.gameObject.AddComponent<BoxCollider>();
                        box.center = b.center;
                        box.size = b.size;
                        box.isTrigger = true;
                        if (GlobalScript.isXRMode)
                        {
                            // XRモード時はインタラクタ追加
                            var cs = mr.gameObject.AddComponent<ColliderSurface>();
                            cs.InjectCollider(box);
                            var ray = mr.gameObject.AddComponent<RayInteractable>();
                            ray.InjectSurface(cs);
                        }
                    }
                }
            }
        }

        #region ロード処理本体
        /// <summary>
        /// プレハブロード
        /// </summary>
        /// <returns></returns>
        private IEnumerator LoadPrefabModel()
        {
            var rootObjects = SceneManager.GetActiveScene().GetRootGameObjects().ToList();
            // 既にprefabがあるかチェック
            if (prefabs.Count == 0)
            {
                foreach (var prefab in prefabSettings)
                {
                    var data = rootObjects.Find(d => (d.name == Path.GetFileNameWithoutExtension(prefab.name)) || (d.name == Path.GetFileNameWithoutExtension(prefab.name) + "_VR"));
                    if (data != null)
                    {
                        GlobalScript.isXRPrefab = data.name.Contains("_VR");
                        if (GlobalScript.isXRPrefab)
                        {
                            data.name = data.name.Replace("_VR", "");
                        }
                        prefabs.Add(data);
                        rootObjects.Remove(data);
                        data.SetActive(false);
                        data.transform.parent = prePrefabObj.transform;
                    }
                }
            }
            if (prefabs.Count == 0)
            {
                //                    prefabs = GlobalScript.CreateInitialModel();
                yield return StartCoroutine(LoadAddressablePrefabs(prefabSettings, prefabs));
            }
            if (prefabs.Count == 0)
            {
                SetProgressLabel("Prefab Files Not Found");
                yield break;
            }
            foreach (var prefab in prefabs)
            {
                if (prefab.name[0] != '_')
                {
                    var prefabData = rootObjects.Find(d => d.name == prefab.name);
                    if (prefabData == null)
                    {
                        prefabData = Instantiate(prefab);
                        prefabData.SetActive(true);
                        prefabData.name = prefab.name;
                        prefabData.transform.position = new();
                        prefabData.transform.parent = prefabObj.transform;
                    }
                    else
                    {
                        prefabData.name = prefab.name;
                        prefabData.transform.position = new();
                        prefabData.transform.parent = prefabObj.transform;
                    }
                }
            }
        }

        /// <summary>
        /// デバッグ通信情報セット
        /// </summary>
        void SetDebugComInfo()
        {
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
        }

        /// <summary>
        /// データベース設定
        /// </summary>
        private void SetDatabaseSetting()
        {
            // hmx-link(デジタルツイン)モードの判定:
            //   WebGL                  → 実PLC接続(ComMcProtocol等)不可のため常に ComHmi を使う（自動 true）
            //   Windows/Android/Editor → HmxLink.json の enabled 次第（既定 false）。Editorテスト時は true にする
            // enabled は LoadParameterFiles でプラットフォーム/Editorトグルにより設定済み（WebGL以外は無効）
            bool useHmx = GlobalScript.hmxLink != null && GlobalScript.hmxLink.enabled;
            if (useHmx)
            {
                string wsUrl = GlobalScript.hmxLink != null ? GlobalScript.hmxLink.wsUrl : "ws://localhost:8765";
                int hmxInterval = GlobalScript.hmxLink != null ? GlobalScript.hmxLink.interval : 200;
                foreach (var p in postgresSettings)
                {
                    if (p.isInner || p.isDirectMode)
                    {
                        var hmi = globalSetting.AddComponent<ComHmi>();
                        hmi.Setup(p.Name, wsUrl, hmxInterval);
                    }
                }
                return;
            }
#if UNITY_WEBGL && !UNITY_EDITOR
            // WebGL: 外部通信(PLC/DB)は socket/スレッド非対応のため生成不可。
            // 内部処理 ComInner のみ生成して、機械を内部シミュレーションで動作させる。
            // TODO(HMI): 将来 HMIバックエンド通信(WebSocket/HTTP)へ切替時は、ここで ComHmi 等を生成する。
            foreach (var p in postgresSettings)
            {
                if (p.isInner)
                {
                    var ex = dataExSettings.Find(d => d.dbNo == p.No);
                    var db = (ComInner)globalSetting.AddComponent<ComInner>();
                    db.SetParameter(p.No, p.Cycle, p.Server, p.Port, p.Database, p.User, p.Password, p.isClientMode, ex, innerSettings, actionSettings);
                }
            }
#else
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
                else if (p.isRedis)
                {
                    // Redis
                    var db = (ComRedis)globalSetting.AddComponent<ComRedis>();
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
                        // データ取得
                        ex = dataExSettings.Find(d => (d.dbNo == p.No) && (d.mechId == direct.mechId));
                        if (ex == null)
                        {
                            ex = new DataExchangeSetting { dbNo = p.No, mechId = direct.mechId, datas = new() };
                        }
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
                        else if (direct.isOpcUa)
                        {
                            var db = (ComOpcUa)globalSetting.AddComponent<ComOpcUa>();
                            db.SetParameter(p.No, p.Cycle, p.Server, p.Port, p.Database, p.User, p.Password, p.isClientMode, ex, direct);
                        }
                    }
                }
            }
#endif
        }

        /// <summary>
        /// 無視オブジェクト更新
        /// </summary>
        private void RenewHiddenObjs()
        {
            hiddenObjs.Clear();
            // 全GameObjectと名前を一度だけ取得（設定毎・mode毎の FindObjectsByType+ToList+.name の繰り返しを回避）
            var allObjs = FindObjectsByType<GameObject>(FindObjectsSortMode.None);
            var names = new string[allObjs.Length];
            for (int i = 0; i < allObjs.Length; i++)
            {
                names[i] = allObjs[i].name;   // .name は毎回string確保 → 1パスにまとめる
            }
            foreach (var m in hiddenSettings)
            {
                if (!m.isEnable)
                {
                    continue;
                }
                for (int i = 0; i < allObjs.Length; i++)
                {
                    string nm = names[i];
                    bool match =
                        m.mode == 0 ? nm == m.name :          // 一致
                        m.mode == 1 ? nm.StartsWith(m.name) :  // 前方一致
                        m.mode == 2 ? nm.EndsWith(m.name) :    // 後方一致
                        m.mode == 3 ? nm.Contains(m.name) :    // 含まれている
                        false;
                    if (!match)
                    {
                        continue;
                    }
                    var o = allObjs[i];
                    if ((m.parent == null) || (m.parent == "") || CommonFunction.GetScenePath(o).Contains(m.parent))
                    {
                        hiddenObjs.Add(o);
                    }
                }
            }
        }

        /// <summary>
        /// ワーク作成
        /// </summary>
        private void CreateWork()
        {
            multiObjectFactory.DeleteSetting();

            // 名前→最初の1個を一度だけ辞書化（ループ毎の FindObjectsByType+ToList を回避）
            var byName = BuildNameLookup();
            foreach (var wk in wkSettings)
            {
                if (byName.TryGetValue(wk.work, out var src))
                {
                    var w = works.Find(d => d.key == wk.work);
                    if (w == null)
                    {
                        w = new ObjEntry { key = wk.work };
                        works.Add(w);
                        w.obj = Instantiate(src);
                        w.obj.SetActive(false);
                    }
                }
            }
        }

        /// <summary>
        /// シーン内 GameObject を 名前→最初の1個 で辞書化する。
        /// 元コードの FindObjectsByType().FindAll(d=>d.name==X)[0] と等価（出現順の先頭を保持）。
        /// </summary>
        private Dictionary<string, GameObject> BuildNameLookup()
        {
            var byName = new Dictionary<string, GameObject>();
            foreach (var g in FindObjectsByType<GameObject>(FindObjectsSortMode.None))
            {
                if (!byName.ContainsKey(g.name))
                {
                    byName[g.name] = g;
                }
            }
            return byName;
        }
        
        /// <summary>
        /// 段ボール作成
        /// </summary>
        private void CreateCardboard()
        {
            // 名前→最初の1個を一度だけ辞書化（ループ毎の FindObjectsByType+ToList を回避）
            var byName = BuildNameLookup();
            // 生成用段ボール保持
            foreach (var cb in cardboardSettings)
            {
                var unit = unitSettings.Find(d => (d.mechId == cb.mechId) && (d.name == cb.name));
                if (unit != null && byName.TryGetValue(unit.parent, out var cardboard))
                {
                    var c = works.Find(d => d.key == cb.name);
                    if (c == null)
                    {
                        cardboard.transform.parent = prefabObj.transform;
                        c = new ObjEntry { key = cb.name };
                        works.Add(c);
                        c.obj = Instantiate(cardboard);
                        var cbs = c.obj.AddComponent<CardboardScript>();
                        cbs.SetParameter(unit, cb);
                        c.obj.SetActive(false);
                    }
                }
            }
        }

        /// <summary>
        /// ユニットオブジェクトを作成しておく
        /// </summary>
        private void CreateUnitObject()
        {
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
                // ゲームオブジェクト紐づけ
                if (unitSetting.isRoboTimeChart)
                {
                    // タイムチャート使用ユニット
                    unitSetting.moveObject = new GameObject("RoboTimeChart");
                    unitSetting.moveObject.transform.parent = unitSetting.unitObject.transform;
                    unitSetting.moveObject.transform.position = Vector3.zero;
                    unitSetting.path = "";
                    unitSetting.group = "";
                    unitSetting.parent = "";
                    unitSetting.children = new();
                    // モデル情報と
                }
                else if (unitSetting.path != "")
                {
                    var obj = prefabObj.transform.Find(unitSetting.path);
                    unitSetting.moveObject = obj != null ? obj.gameObject : null;
                }
            }
            // 子供オブジェクト
            foreach (var unitSetting in unitSettings)
            {
                foreach (var child in unitSetting.children)
                {
                    if (!child.isUnit)
                    {
                        // ユニット以外
                        if (child.path != "")
                        {
                            var obj = prefabObj.transform.Find(child.path);
                            child.childObject = obj != null ? obj.gameObject : null;
                        }
                    }
                    else
                    {
                        // ユニット
                        var obj = unitSettings.Find(d => d.name == child.name);
                        child.childObject = obj != null ? obj.unitObject : null;
                    }
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
            // 拡張機構設定
            foreach (var ex in exMechSettings)
            {
                foreach(var data in ex.datas)
                {
                    if ((data.path != null) && (data.path != ""))
                    {
                        var obj = prefabObj.transform.Find(data.path);
                        data.gameObject = obj != null ? obj.gameObject : null;
                        foreach (var child in data.children)
                        {
                            var cobj = prefabObj.transform.Find(child.path);
                            child.gameObject = cobj != null ? cobj.gameObject : null;
                        }
                    }
                }
            }
            // バケット設定
            foreach (var backet in backetSettings)
            {
                if ((backet.path != null) && (backet.path != ""))
                {
                    var obj = prefabObj.transform.Find(backet.path);
                    backet.gameObject = obj != null ? obj.gameObject : null;
                }
            }
            // リニア設定
            foreach (var linear in linearSettings)
            {
                if ((linear.path != null) && (linear.path != ""))
                {
                    var obj = prefabObj.transform.Find(linear.path);
                    linear.gameObject = obj != null ? obj.gameObject : null;
                }
            }
            SortUnitSettings(unitNames, unitSettings, ref tmpUnits);
            unitSettings = tmpUnits;
        }

        /// <summary>
        /// スイッチモデル作成
        /// </summary>
        /// <param name="existingNames">シーンに既存の GameObject 名の集合</param>
        private void CreateSwitchModel(HashSet<string> existingNames)
        {
            switchModel.Clear();
            if (switchPrefabs.Count > 0)
            {
                foreach (var sw in switchSettings)
                {
                    var unit = unitSettings.Find(d => (d.mechId == sw.mechId) && (d.name == sw.name));
                    if ((unit != null) && ((unit.group == null) || (unit.group == "")))
                    {
                        unit.parent = unit.parent == "" ? "_switch" + (switchSettings.IndexOf(sw) + 1) : unit.parent;
                        if (!existingNames.Contains(unit.parent))
                        {
                            // モデルが存在しないので作成
                            var obj = Instantiate(switchPrefabs[0]);
                            obj.name = unit.parent;
                            obj.transform.parent = deviceObj.transform;
                            obj.transform.localPosition = new Vector3(sw.pos[0], sw.pos[1], sw.pos[2]);
                            obj.transform.localEulerAngles = new Vector3(sw.rot[0], sw.rot[1], sw.rot[2]);
                            switchModel.Add(obj);
                            // 動作オブジェクトセット
                            unit.moveObject = obj;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// シグナルタワー作成
        /// </summary>
        /// <param name="existingNames">シーンに既存の GameObject 名の集合</param>
        private void CreateSinalTowerModel(HashSet<string> existingNames)
        {
            towerModel.Clear();
            if (towerPrefabs.Count > 0)
            {
                foreach (var st in towerSettings)
                {
                    var unit = unitSettings.Find(d => (d.mechId == st.mechId) && (d.name == st.name));
                    if ((unit != null) && ((unit.group == null) || (unit.group == "")))
                    {
                        unit.parent = unit.parent == "" ? "_signalTower" + (towerSettings.IndexOf(st) + 1) : unit.parent;
                        if (!existingNames.Contains(unit.parent))
                        {
                            // モデルが存在しないので作成
                            var obj = Instantiate(towerPrefabs[st.type]);
                            obj.name = unit.parent;
                            obj.transform.parent = deviceObj.transform;
                            obj.transform.localPosition = new Vector3(st.pos[0], st.pos[1], st.pos[2]);
                            obj.transform.localEulerAngles = new Vector3(st.rot[0], st.rot[1], st.rot[2]);
                            towerModel.Add(obj);
                            // 動作オブジェクトセット
                            unit.moveObject = obj;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// キーイベント
        /// </summary>
        /// <param name="key"></param>
        private void HandleKey(Key key, bool value, bool isCtrl, bool isShift)
        {
            if (value)
            {
                if (key == Key.F5)
                {
                    if (isShift)
                    {
                        // パラメータのみロード
                        ReloadActParameter();
                    }
                    else
                    {
                        // プレハブもロード
                        ReloadParameter(isCtrl);
                    }
                }
                else if (key == Key.F12)
                {
                    mtRoom.SetActive(!mtRoom.activeSelf);
                }
            }
        }
        #endregion ロード処理

        #region 各種処理
        /// <summary>
        /// 線更新
        /// </summary>
        private void RenewLines()
        {
            if (isLines != GlobalScript.isLiens)
            {
                foreach (Material mat in allLineMaterials)
                {
                    mat.SetFloat("_Alpha", GlobalScript.isLiens ? 0.5f : 0f);
                }
                isLines = GlobalScript.isLiens;
            }
        }

        /// <summary>
        /// 断面更新
        /// </summary>
        private void RenewDanmen()
        {
            if (clipInfo.isOn != GlobalScript.clipInfo.isOn)
            {
                if (GlobalScript.clipInfo.isOn)
                {
                    // シェーダー切り替え
                    foreach (Material mat in allMaterials)
                    {
                        if (mat.shader.name.Contains("Transparent"))
                        {
                            mat.shader = transparentDanmen;
                        }
                        else
                        {
                            mat.shader = opaqueDanmen;
                        }
                    }
                    slicePlane.transform.transform.localPosition = new Vector3(GlobalScript.clipInfo.x, GlobalScript.clipInfo.y, GlobalScript.clipInfo.z);
                }
                else
                {
                    // シェーダー通常
                    foreach (Material mat in allMaterials)
                    {
                        if (mat.shader.name.Contains("Transparent"))
                        {
                            mat.shader = transparentShader;
                        }
                        else
                        {
                            mat.shader = opaqueShader;
                        }
                    }
                    slicePlane.transform.transform.localPosition = Vector3.zero;
                    slicePlane.transform.localEulerAngles = Vector3.zero;
                    GlobalScript.clipInfo.mode = GlobalScript.ClipInfo.SlideMode.None;
                    clipInfo.mode = GlobalScript.clipInfo.mode;
                }
                clipInfo.isOn = GlobalScript.clipInfo.isOn;
            }
            if (clipInfo.isOn)
            {
                var isChange = false;
                if ((clipInfo.mode != GlobalScript.clipInfo.mode) || (clipInfo.isRvs != GlobalScript.clipInfo.isRvs))
                {
                    // スライスモード変更
                    if (GlobalScript.clipInfo.mode == GlobalScript.ClipInfo.SlideMode.X)
                    {
                        slicePlane.transform.localEulerAngles = new Vector3(0, 0, GlobalScript.clipInfo.isRvs ? -90 : 90);
                    }
                    else if (GlobalScript.clipInfo.mode == GlobalScript.ClipInfo.SlideMode.Y)
                    {
                        slicePlane.transform.localEulerAngles = new Vector3(GlobalScript.clipInfo.isRvs ? 180 : 0, 0, 0);
                    }
                    else if (GlobalScript.clipInfo.mode == GlobalScript.ClipInfo.SlideMode.Z)
                    {
                        slicePlane.transform.localEulerAngles = new Vector3(GlobalScript.clipInfo.isRvs ? 90 : -90, 0, 0);
                    }
                    clipInfo.mode = GlobalScript.clipInfo.mode;
                    clipInfo.isRvs = GlobalScript.clipInfo.isRvs;
                    isChange = true;
                }
                if (isChange || (clipInfo.value != GlobalScript.clipInfo.value))
                {
                    slicePlane.transform.transform.localPosition = new Vector3(GlobalScript.clipInfo.x, GlobalScript.clipInfo.y, GlobalScript.clipInfo.z);
                    clipInfo.x = GlobalScript.clipInfo.x;
                    clipInfo.y = GlobalScript.clipInfo.y;
                    clipInfo.z = GlobalScript.clipInfo.z;
                    clipInfo.value = GlobalScript.clipInfo.value;
                }
            }
        }
        #endregion 各種処理
    }
}
