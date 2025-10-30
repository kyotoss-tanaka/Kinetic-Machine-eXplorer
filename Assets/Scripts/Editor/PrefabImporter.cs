#if UNITY_EDITOR
#if DEV_PC
using NUnit.Framework.Constraints;
using Oculus.Platform;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using TMPro;
using Unity.XR.CoreUtils.Editor;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.Pixyz.UnitySDK;
using UnityEditor.PixyzPlugin4Unity;
using UnityEditor.PixyzPlugin4Unity.Actions;
using UnityEditor.PixyzPlugin4Unity.Analytics;
using UnityEditor.PixyzPlugin4Unity.LODs;
using UnityEditor.PixyzPlugin4Unity.RuleEngine;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Pixyz.Algo;
using UnityEngine.Pixyz.API;
using UnityEngine.Pixyz.CAD;
using UnityEngine.Pixyz.Core;
using UnityEngine.Pixyz.Geom;
using UnityEngine.Pixyz.IO;
using UnityEngine.Pixyz.Material;
using UnityEngine.Pixyz.Polygonal;
using UnityEngine.Pixyz.Scene;
using UnityEngine.Pixyz.UnitySDK;
using UnityEngine.PixyzPlugin4Unity.Components;
using UnityEngine.SceneManagement;
using UnityEngine.WSA;
using static PrefabImporter.KssPrefabImport;

public class PrefabImporter
{
    private static bool isProcessing = false;

    private static bool isSuccess = false;

    private static string m_FileName = "";

    private static uint m_Root = 0;

    private static Shader lineShader;

    private static Dictionary<uint, UnityEngine.Object> m_EntityToObject;

    private static System.Diagnostics.Stopwatch m_Stopwatch;

    private static string m_ProdNo = "";

    private static List<string> m_FilePaths = new();

    private static int m_CurrentUndo = -1;

    private static KssPrefabImport prefabImporter;

    private static KssImporterScriptableObject m_Settings;

    #region Pixyz interfaces
    protected static PiXYZAPI m_API;
    protected static CoreInterface Core => m_API.Core;
    protected static AlgoInterface Algo => m_API.Algo;
    protected static SceneInterface Scene => m_API.Scene;
    protected static GeomInterface Geom => m_API.Geom;
    protected static PolygonalInterface Polygonal => m_API.Polygonal;
    protected static MaterialInterface Material => m_API.Material;
    protected static IOInterface IO => m_API.IO;
    #endregion

    #region クラス定義
    public class KssImporterScriptableObject : ScriptableObject
    {
        [SerializeField] private GameObject m_Prefab = null; // readonly
        [SerializeField] private SceneAsset m_Scene = null; // readonly

        public GameObject Prefab => m_Prefab;
        public SceneAsset Scene => m_Scene;

        [field: SerializeField] public ImportMode ImportMode { get; set; } = ImportMode.Prefab;

        [field: SerializeField] public bool ImportAllFilesInFolder { get; set; } = false;
        [field: SerializeField] public bool PreferAlternativeImporters { get; set; } = false;
        [field: SerializeField] public float Scale { get; set; } = 0.001f;
        [field: SerializeField] public ModelOrientation Orientation { get; set; } = ModelOrientation.Automatic;
        [field: SerializeField] public bool IsLeftHanded { get; set; } = false;
        [field: SerializeField] public bool IsZUp { get; set; } = false;
        [field: SerializeField] public bool AvoidNegativeScale { get; set; } = true;
        [field: SerializeField] public bool PreserveHierarchy { get; set; } = true;
        [field: SerializeField] public RuleSet RuleSet { get; set; } = null;


        // Import
        [field: SerializeField] public bool ImportMetadata { get; set; } = true;
        [field: SerializeField] public bool ImportPatchBoundaries { get; set; } = true;
        [field: SerializeField] public bool ImportLines { get; set; } = true;
        [field: SerializeField] public bool ImportPoints { get; set; } = false;
        [field: SerializeField] public bool ImportNestedPrefabs { get; set; } = false;
        [field: SerializeField] public bool ImportAnimations { get; set; } = false;
        [field: SerializeField] public bool ImportVariants { get; set; } = false;
        [field: SerializeField] public bool ImportPMI { get; set; } = false;

        // Transforms
        [field: SerializeField] public bool StitchPatches { get; set; } = false;

        // Geometry
        [field: SerializeField] public bool Use16BitsBuffers { get; set; } = false;
        [field: SerializeField] public bool ReorientFaces { get; set; } = false;
        [field: SerializeField] public bool RepairInstances { get; set; } = true;
        [field: SerializeField] public Quality MeshQuality { get; set; } = Quality.Maximum;

        // Rendering
        [field: SerializeField] public bool CreateLightmapUVs { get; set; } = false;
        [field: SerializeField] public int LightmapResolution { get; set; } = 1024;
        [field: SerializeField] public int LightmapPadding { get; set; } = 4;

        // Materials
        [field: SerializeField] public bool CreateUVs { get; set; } = false;
        [field: SerializeField] public float UVSize { get; set; } = 1.0f;


        //TODO: delete in 4.0
        [System.Obsolete]
        public string[] MaterialNames => null;

        // LODs
        [field: SerializeField] public LODGenerator LODGenerator { get; set; } = null;

        private KssPrefabImport m_Importer;

        public KssPrefabImport.ImportCompletedHandler ImportCompleted;
        internal KssPrefabImport.SyncedProgress.ProgressHandler ProgressChanged;
        internal KssPrefabImport.SyncedProgress.CancelStartedHandler CancelStarted;
        internal KssPrefabImport.ImportStartedHandler ImportStarted;

        public bool IsImporting => m_Importer != null;
        public bool IsCanceling => IsImporting ? m_Importer.ImportProgress.IsCanceling : false;
        public float LastProgress { private set; get; }
        public string LastMessage { private set; get; }

        //Used to chain multiple import
        public KssImporterScriptableObject LinkedImporter { set; private get; }

        /*
        protected static ImporterScriptableObject BrowseAndCreate(string[] supportedFormats, Type type)
        {
            string filePath = EditorUtils.SelectFile(supportedFormats);

            if (string.IsNullOrEmpty(filePath))
                return null;

            filePath = Path.GetRelativePath(Application.dataPath, filePath);

            return Create(filePath, type);
        }

        internal static ImporterScriptableObject Create(string filePath, Type type)
        {
            if (string.IsNullOrEmpty(filePath))
                return null;

            ImporterScriptableObject scriptableObject;
            RuleSet defaultRuleSet = null;

            try
            {
                AssetDatabase.StartAssetEditing();

                string name = System.IO.Path.GetFileNameWithoutExtension(filePath);
                string path = PixyzProjectSettings.PrefabFolder;
                scriptableObject = EditorUtils.CreateAsset(type, name, path, focusOnSave: false) as ImporterScriptableObject;

                Preset[] presets = Preset.GetDefaultPresetsForObject(scriptableObject);

                if (presets.Length > 0)
                {
                    presets[presets.Length - 1].ApplyTo(scriptableObject);
                }
                else
                {
                    if (defaultRuleSet == null)
                    {
                        defaultRuleSet = GetDefaultRuleSet();
                    }
                    scriptableObject.RuleSet = defaultRuleSet;
                }

                // Those fields are not part of the preset
                scriptableObject.FilePath = filePath;
                scriptableObject.m_Prefab = null;
                scriptableObject.m_Scene = null;

                EditorUtility.SetDirty(scriptableObject);
            }
            catch
            {
                AssetDatabase.StopAssetEditing();
                throw;
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                AssetDatabase.SaveAssets();
            }

            Selection.activeObject = scriptableObject;
            EditorUtility.FocusProjectWindow();

            return scriptableObject;
        }
        */
        /// <summary>
        /// Reset values to their defaults.
        /// </summary>
        public void Reset()
        {
            //            ResetValues();

            m_Prefab = null;
            m_Scene = null;
            ImportMode = ImportMode.Prefab;

            ImportAllFilesInFolder = false;
            PreferAlternativeImporters = false;
            Scale = 0.001f;
            Orientation = ModelOrientation.Automatic;
            IsLeftHanded = false;
            IsZUp = false;
            AvoidNegativeScale = true;
            PreserveHierarchy = true;
            RuleSet = null;

            ImportMetadata = true;
            ImportPatchBoundaries = true;
            ImportLines = true;
            ImportPoints = false;
            ImportNestedPrefabs = false;
            ImportAnimations = false;
            ImportVariants = false;
            ImportPMI = false;
            StitchPatches = false;
            Use16BitsBuffers = false;
            ReorientFaces = false;
            RepairInstances = true;
            MeshQuality = Quality.Maximum;
            CreateLightmapUVs = false;
            LightmapResolution = 1024;
            LightmapPadding = 4;
            CreateUVs = false;
            UVSize = 1.0f;
            LODGenerator = null;
        }

        public void PrepareForImport(KssPrefabImport m_Importer)
        {
            if (this.m_Importer == null)
            {
                this.m_Importer = m_Importer;
                ResetValues();
                //Assign m_Scene to null so it lose any possible reference to its previous value
                //Keeping the old reference would cause the asset field to not be linked to the newly imported asset
                if (ImportMode == ImportMode.Scene && Scene == null)
                    //Since the object Scene might still exist with null content we set it to null to dispose of it
                    m_Scene = null;

//                m_Importer = Activator.CreateInstance(typeof(KssPrefabImport), new object[] { GetAbsoluteFilePath(), this }) as KssPrefabImport;
                m_Importer.ImportCompleted += OnImportCompleted;
                m_Importer.ImportProgress.ProgressChanged += OnProgressChanged;
                m_Importer.ImportProgress.ProgressCancelStarted += OnProgressCancelStarted;
            }
        }

        private void ResetValues()
        {
            LastProgress = 0f;
            LastMessage = "";
        }

        private void OnProgressChanged(object parent, float progress, string message)
        {
            LastProgress = progress;
            LastMessage = message;
            ProgressChanged?.Invoke(parent, progress, message);
        }

        private void OnProgressCancelStarted()
        {
            CancelStarted?.Invoke();
        }

        private void OnImportCompleted(UnityEngine.Object asset, bool success)
        {
            ResetValues();

            if (success)
            {
                if (ImportMode == ImportMode.Prefab)
                    m_Prefab = (GameObject)asset;
                else if (ImportMode == ImportMode.Scene)
                    m_Scene = (SceneAsset)asset;

                // Ensure ScriptableObject modifications are taken into account.
                EditorUtility.SetDirty(this);
            }
            ImportCompleted?.Invoke(asset, success);
//            ChainWithLinkedImporter();
        }

        /*
        private void ChainWithLinkedImporter()
        {
            LinkedImporter?.Import();
            LinkedImporter = null;
        }
        */

        public void CancelImport()
        {
            if (m_Importer != null)
            {
                m_Importer.ImportProgress?.StartCancel();
            }
        }
        /*

        internal string GetAbsoluteFilePath()
        {
            if (string.IsNullOrEmpty(FilePath))
            {
                return "";
            }
            if (Path.IsPathRooted(FilePath))
            {
                return FilePath;
            }
            return Path.GetFullPath(Path.Combine(Application.dataPath, FilePath));
        }

        internal bool SourcePathExists()
        {
            return File.Exists(GetAbsoluteFilePath());
        }

        internal bool AlreadyImported() => (ImportMode == ImportMode.Prefab && Prefab != null) || (ImportMode == ImportMode.Scene && Scene != null);
        internal static bool CheckRuleSet(RuleEngine.RuleSet ruleSet, bool checkErrors, out string error)
        {
            error = null;

            if (ruleSet == null)
                return false;

            bool valid = true;
            for (int i = 0; i < ruleSet.RulesCount; i++)
            {
                Rule rule = ruleSet.GetRule(i);

                if (rule.IsEnabled && rule.BlocksCount == 1 && rule.GetBlock(0).Action.GetType() == typeof(RunRules))
                {
                    RuleSet[] nestedRuleSets = (rule.GetBlock(0).Action as RunRules).ruleSets;
                    for (int j = 0; j < nestedRuleSets.Length; j++)
                    {
                        if (!CheckRuleSet(nestedRuleSets[j], checkErrors, out error))
                        {
                            valid = false;
                            break;
                        }
                    }
                }
                else if (rule.IsEnabled && rule.BlocksCount > 0)
                {
                    RuleBlock startBlock = rule.GetBlock(0);
                    if (startBlock.Action.GetType() != typeof(GetContextGameObjects))
                    {
                        valid = false;
                        error = "First block of a rule must be a GetContextGameObjects action";
                        break;
                    }
                }
            }

            if (checkErrors)
                valid = valid && ruleSet.IsValid();

            return valid;
        }

        private static RuleSet GetDefaultRuleSet()
        {
            //This will load any Ruleset named default whether it is at the new AssetTransformer or old Pixyz path
            RuleSet defaultRuleSet = Resources.Load<RuleEngine.RuleSet>("Default");
            if (defaultRuleSet == null)
            {
                // Todo: check how to create a directory with AssetDB + remove from loop
                Directory.CreateDirectory(Preferences.DefaultImportRuleSetLocation);
                defaultRuleSet = EditorUtils.CreateAsset(typeof(RuleSet), "Default", Preferences.DefaultImportRuleSetLocation, focusOnSave: false) as RuleSet;
                Rule rule = new() { Name = "Rule" };
                rule.AppendBlock(new RuleBlock((new GetContextGameObjects().Id)));
                defaultRuleSet.AppendRule(rule);
                EditorUtility.SetDirty(defaultRuleSet);
            }

            return defaultRuleSet;
        }
        */
    }
    #endregion クラス定義

    [MenuItem("Kyotoss/Create Prefab Files", false, 1)]
    public static void ImportFolder()
    {
        isProcessing = true;
        // ダイアログを開いて、OK時にコールバックで処理を行う
        InputDialogWindow.Show("Prefab Creator", "インポート元フォルダ(ファイル)を入力してください：", @"H:\data", (string input) =>
        {
            if (string.IsNullOrEmpty(input))
            {
                isProcessing = false;
                return;
            }
            if (prefabImporter == null)
            {
                prefabImporter = new KssPrefabImport();
            }
            if (m_Settings == null)
            {
                m_Settings = ScriptableObject.CreateInstance<KssImporterScriptableObject>();
            }
            m_Settings.PrepareForImport(prefabImporter);
            prefabImporter.Process(input);
        });
    }

    // validate関数（trueなら有効、falseなら無効）
    [MenuItem("Kyotoss/Create Prefab Files", true)]
    private static bool ValidateCreatePrefabFiles()
    {
        return !isProcessing; // 処理中なら無効化
    }

    public class KssPrefabImport
    {
        private Tolerances m_Tolerances;

        private Tolerances m_LineTolerances;

        public delegate void ImportStartedHandler(KssPrefabImport importer);
        public delegate void ImportCompletedHandler(UnityEngine.Object asset, bool success);
        public event ImportCompletedHandler ImportCompleted;

        private SyncedProgress m_ImportProgress;
        public SyncedProgress ImportProgress
        {
            get
            {
                m_ImportProgress ??= new SyncedProgress(this, true, "Importing " + m_FileName, true);
                return m_ImportProgress;
            }
        }

        #region クラス定義
        public struct Tolerances
        {
            public readonly double MaxSag;

            public readonly double SagRatio;

            public readonly double MaxAngle;

            public readonly double SurfacicTolerance;

            public readonly double LineicTolerance;

            public readonly double NormalTolerance;

            public readonly double UVTolerance;

            public readonly double PointCloudDensity;

            public Tolerances(Quality quality = Quality.Medium)
            {
                MaxSag = (SagRatio = (MaxAngle = (SurfacicTolerance = (LineicTolerance = (NormalTolerance = (UVTolerance = (PointCloudDensity = -1.0)))))));
                switch (quality)
                {
                    case Quality.Maximum:
                        MaxSag = 0.05;
                        SagRatio = 0.0001;
                        break;
                    case Quality.High:
                        MaxSag = 0.1;
                        SagRatio = 0.0002;
                        break;
                    case Quality.Medium:
                        MaxSag = 0.2;
                        SagRatio = 0.0003;
                        break;
                    case Quality.Low:
                        MaxSag = 1.0;
                        SagRatio = 0.001;
                        break;
                    case Quality.Custom:
                        MaxSag = PixyzProjectSettings.CustomImportMaxSag;
                        SagRatio = PixyzProjectSettings.CustomImportSagRatio;
                        break;
                    default:
                        MaxSag = 3.0;
                        SagRatio = 0.01;
                        break;
                }

                switch (quality)
                {
                    case Quality.Maximum:
                        SurfacicTolerance = 0.01;
                        LineicTolerance = -1.0;
                        NormalTolerance = -1.0;
                        break;
                    case Quality.High:
                        SurfacicTolerance = 0.5;
                        LineicTolerance = 0.1;
                        NormalTolerance = 1.0;
                        break;
                    case Quality.Medium:
                        SurfacicTolerance = 1.0;
                        LineicTolerance = -1.0;
                        NormalTolerance = 8.0;
                        break;
                    case Quality.Low:
                        SurfacicTolerance = 3.0;
                        LineicTolerance = -1.0;
                        NormalTolerance = 15.0;
                        break;
                    case Quality.Custom:
                        SurfacicTolerance = PixyzProjectSettings.CustomSurfacicTolerance;
                        LineicTolerance = PixyzProjectSettings.CustomLineicTolerance;
                        NormalTolerance = PixyzProjectSettings.CustomNormalTolerance;
                        break;
                    default:
                        SurfacicTolerance = 10.0;
                        NormalTolerance = 20.0;
                        break;
                }

                switch (quality)
                {
                    case Quality.Maximum:
                        PointCloudDensity = 1.0;
                        break;
                    case Quality.High:
                        PointCloudDensity = 0.8;
                        break;
                    case Quality.Medium:
                        PointCloudDensity = 0.6;
                        break;
                    case Quality.Low:
                        PointCloudDensity = 0.3;
                        break;
                    case Quality.Custom:
                        PointCloudDensity = PixyzProjectSettings.CustomPointCloudDensity;
                        break;
                    default:
                        PointCloudDensity = 0.15;
                        break;
                }
            }
        }

        /// <summary>
        /// Progress class to report progress information to the thread that created the object (see IProgress interface).
        /// </summary>
        public class SyncedProgress
        {
            internal delegate void ProgressHandler(object parent, float progress, string message);

            internal event ProgressHandler ProgressChanged;

            //For later unification with Toolbox progress
            internal delegate void CancelHandler(object parent, string message);
            internal event CancelHandler ProgressCanceled;

            internal delegate void CancelStartedHandler();
            internal event CancelStartedHandler ProgressCancelStarted;

            internal delegate void FinishHandler(object parent, string message);
            internal event FinishHandler ProgressFinished;

            private enum Status
            {
                Running,
                Canceled,
                Finished,
                Failed,
                CancelMainThread
            }

            private struct ProgressInformation
            {
                public float progress;
                public string message;
                public bool async;
                public Status status;

                public ProgressInformation(float progress, string message, bool async, Status status = Status.Running)
                {
                    this.progress = progress;
                    this.message = message;
                    this.async = async;
                    this.status = status;
                }
            }

            private IProgress<ProgressInformation> m_Progress;

            private object m_Parent;

            internal bool Enabled { get; set; }
            internal bool IsCanceling { get; private set; }
            private int m_CancelProgressId = -1;
            private bool m_CancelFinished = false;
            private int m_ProgressId = -1;
            private float m_CurrentProgress = -1f;
            private bool m_ProgressBarPopulated = false;
            private bool m_MainThreadProgressBarPopulated = false;
            private Thread m_KeepAliveThread;

            public string progressBarName = "";

            internal int ProcessId() { return m_ProgressId; }

            internal SyncedProgress(object parent, bool useUnityEditorProgressBar = false, string progressBarName = "", bool allowCanceling = true, bool keepAlive = true)
            {
                this.progressBarName = progressBarName;
                Enabled = true;
                IsCanceling = false;
                m_Parent = parent;
                m_CurrentProgress = -1f;
                m_MainThreadProgressBarPopulated = false;
                m_Progress = new Progress<ProgressInformation>(value => {

                    switch (value.status)
                    {
                        case Status.Running:
                            ProgressChanged?.Invoke(m_Parent, value.progress, value.message);
                            m_CurrentProgress = value.progress;

                            if (!value.async && useUnityEditorProgressBar)
                            {
                                m_MainThreadProgressBarPopulated = true;
                                EditorUtility.DisplayProgressBar(this.progressBarName, value.message, value.progress);
                            }
                            else if (value.async && useUnityEditorProgressBar && m_MainThreadProgressBarPopulated)
                            {
                                m_MainThreadProgressBarPopulated = false;
                                EditorUtility.ClearProgressBar();
                            }

                            break;

                        case Status.Canceled:
                            ProgressCanceled?.Invoke(m_Parent, value.message);

                            if (m_MainThreadProgressBarPopulated)
                                EditorUtility.ClearProgressBar();

                            break;

                        case Status.Finished:
                            ProgressFinished?.Invoke(m_Parent, value.message);

                            if (m_MainThreadProgressBarPopulated)
                                EditorUtility.ClearProgressBar();

                            break;

                        case Status.Failed:
                            if (m_MainThreadProgressBarPopulated)
                                EditorUtility.ClearProgressBar();
                            break;
                    };
                });
                if (useUnityEditorProgressBar)
                {
                    progressBarName = progressBarName == "" ? m_Parent.GetType().Name : progressBarName;
                    m_ProgressId = UnityEditor.Progress.Start(progressBarName);

                    if (allowCanceling)
                    {
                        UnityEditor.Progress.RegisterCancelCallback(m_ProgressId, () =>
                        {
                            // This is called when starting the cancelation (e.g. clicking on the x in Background Tasks) and when the actual cancel happens in FinishCancel, hence the bool checks.
                            if (m_CancelFinished)
                                return true;
                            if (!IsCanceling)
                                StartCancel();
                            return false;
                        });
                    }

                    if (keepAlive)
                    {
                        m_KeepAliveThread = new(() =>
                        {
                            while (true)
                            {
                                UnityEditor.Progress.Report(m_ProgressId, m_CurrentProgress);
                                Thread.Sleep(1000);
                            }
                        });
                        m_KeepAliveThread.Start();
                    }

                    m_ProgressBarPopulated = true;
                }
            }

            private void AbortAndWaitKeepAliveThread()
            {
                m_KeepAliveThread?.Abort();
                m_KeepAliveThread?.Join();
            }

            /// <summary>
            /// Reports progress information to the thread that created the object.
            /// </summary>
            /// <param name="progress">The current progress value (0 to 1).</param>
            /// <param name="message">Optional message describing the current progress state.</param>
            /// <param name="async">Indicates whether the progress is reported asynchronously.</param>
            public void Report(float progress, string message = "", bool async = true)
            {
                if (!Enabled)
                    return;

                if (IsCanceling)
                {
                    UnityEditor.Progress.Report(m_CancelProgressId, progress);
                }
                else
                {
                    m_Progress.Report(new ProgressInformation(progress, message, async, Status.Running));
                    if (m_ProgressBarPopulated)
                    {
                        UnityEditor.Progress.Report(m_ProgressId, progress, message);
                    }
                }
            }

            /// <summary>
            /// Marks the progress as finished and clears any associated progress bars.
            /// </summary>
            /// <param name="message">Optional message describing the completion state.</param>
            public void Finish(string message = "")
            {
                m_Progress.Report(new ProgressInformation(100, message, false, Status.Finished));

                if (m_ProgressBarPopulated)
                {
                    AbortAndWaitKeepAliveThread();
                    UnityEditor.Progress.Finish(m_ProgressId, UnityEditor.Progress.Status.Succeeded);
                }
            }

            /// <summary>
            /// Initiates the cancellation process for the progress.
            /// </summary>
            public void StartCancel()
            {
                if (Enabled)
                {
                    IsCanceling = true;
                    m_CancelProgressId = UnityEditor.Progress.Start("Canceling...", parentId: m_ProgressId);
                    ProgressCancelStarted?.Invoke();
                }
            }

            /// <summary>
            /// Completes the cancellation process and clears any associated progress bars.
            /// </summary>
            /// <param name="message">Optional message describing the cancellation state.</param>
            public void FinishCancel(string message = "")
            {
                if (Enabled)
                {
                    m_Progress.Report(new ProgressInformation(-1, message, false, Status.Canceled));
                    if (m_ProgressBarPopulated)
                    {
                        AbortAndWaitKeepAliveThread();
                        m_CancelFinished = true;
                        UnityEditor.Progress.Cancel(m_ProgressId);
                        UnityEditor.Progress.Finish(m_CancelProgressId);
                    }
                }
            }

            /// <summary>
            /// Marks the progress as failed and clears any associated progress bars.
            /// </summary>
            /// <param name="message">Optional message describing the failure state.</param>
            public void Failed(string message = "")
            {
                m_Progress.Report(new ProgressInformation(-1, message, false, Status.Failed));

                if (m_ProgressBarPopulated)
                {
                    AbortAndWaitKeepAliveThread();
                    UnityEditor.Progress.Finish(m_ProgressId, UnityEditor.Progress.Status.Succeeded);
                }
            }
        }

        /// <summary>
        /// Represents a progress tracking class for Asset Transformer operations.
        /// </summary>
        public class NativeProgress : IDisposable
        {
            readonly SyncedProgress m_SyncedProgress = null;
            readonly PiXYZAPI m_API = null;
            readonly float m_Min;
            readonly float m_Max;
            readonly float m_StepCount;
            readonly bool m_UseSteppedProgress;
            float m_CurrentStep;
            readonly string m_Name = null;
            string m_CurrentStepName = "";
            readonly uint m_CallbackId = 0;
            readonly uint m_CallbackId2 = 0;
            readonly uint m_CallbackId3 = 0;

            readonly CoreInterface.ProgressStepStartDelegate m_ProgressStepStartedDelegate;
            readonly CoreInterface.ProgressStepFinishedDelegate m_ProgressStepFinishedDelegate;

            /// <summary>
            /// Initializes a new instance of the <see cref="NativeProgress"/> class.
            /// </summary>
            /// <param name="api">The PiXYZ API instance used for progress tracking.</param>
            /// <param name="syncedProgress">The synchronized progress instance for reporting progress.</param>
            /// <param name="name">Optional name for the progress instance.</param>
            /// <param name="min">The minimum progress value (default is 0).</param>
            /// <param name="max">The maximum progress value (default is 1).</param>
            /// <param name="stepCount">The number of steps for stepped progress (default is -1 for continuous progress).</param>
            /// <exception cref="ArgumentException">Thrown if <paramref name="min"/> is greater than or equal to <paramref name="max"/>.</exception>
            /// <exception cref="ArgumentNullException">Thrown if <paramref name="api"/> or <paramref name="syncedProgress"/> is null.</exception>
            public NativeProgress(PiXYZAPI api, SyncedProgress syncedProgress, string name = null, float min = 0f, float max = 1f, int stepCount = -1)
            {
                if (min >= max)
                    throw new ArgumentException("min must be less than max");

                m_API = api ?? throw new ArgumentNullException(nameof(api));
                m_SyncedProgress = syncedProgress ?? throw new ArgumentNullException(nameof(syncedProgress));
                m_Name = name;

                m_Min = min;
                m_Max = max;
                m_StepCount = stepCount;
                m_CurrentStep = 0;
                m_UseSteppedProgress = stepCount > 1;

                CoreInterface.ProgressChangedDelegate mProgressChangedDelegate = new(OnProgressChanged);
                m_CallbackId = m_API.Core.AddProgressChangedCallback(mProgressChangedDelegate, IntPtr.Zero);

                m_ProgressStepStartedDelegate = new CoreInterface.ProgressStepStartDelegate(OnStepStarted);
                m_CallbackId2 = m_API.Core.AddProgressStepStartCallback(m_ProgressStepStartedDelegate, IntPtr.Zero);

                m_ProgressStepFinishedDelegate = new CoreInterface.ProgressStepFinishedDelegate(OnStepFinished);
                m_CallbackId3 = m_API.Core.AddProgressStepFinishedCallback(m_ProgressStepFinishedDelegate, IntPtr.Zero);
            }

            private void OnStepStarted(IntPtr userData, string stepName)
            {
                m_CurrentStepName = stepName;
            }

            private void OnProgressChanged(IntPtr userdata, int progress)
            {
                if (m_SyncedProgress == null)
                {
                    UnityEngine.Debug.LogError("Should not happen");
                    return;
                }

                if (progress < 0)
                    return;

                if (m_UseSteppedProgress)
                {
                    float interval = (m_Max - m_Min) / m_StepCount;
                    float currentMin = m_Min + interval * m_CurrentStep;
                    m_SyncedProgress.Report(currentMin + interval * progress / 100f, m_Name ?? m_CurrentStepName);
                }
                else
                {
                    m_SyncedProgress.Report(progress / 100f, m_Name ?? m_CurrentStepName);
                }
            }

            private void OnStepFinished(IntPtr userData)
            {
                if (m_UseSteppedProgress && m_CurrentStep >= m_StepCount)
                {
                    m_SyncedProgress?.Finish(m_CurrentStepName + " finished");
                }

                m_CurrentStep++;
            }

            /// <summary>
            /// Disposes of the NativeProgress instance and removes all associated callbacks.
            /// </summary>
            public void Dispose()
            {
                m_API.Core.RemoveProgressChangedCallback(m_CallbackId);
                m_API.Core.RemoveProgressStepStartCallback(m_CallbackId2);
                m_API.Core.RemoveProgressStepFinishedCallback(m_CallbackId3);
            }
        }
        #endregion クラス定義

        public KssPrefabImport()
        {
        }

        public void Process(string folder)
        {
            ImportProcess(folder);
        }

        /// <summary>
        /// インポート処理開始
        /// </summary>
        /// <param name="folder"></param>
        private async void ImportProcess(string folder)
        {
            if (m_API == null)
            {
                m_API = PixyzPlugin.Pixyz;
            }
            if (m_Stopwatch == null)
            {
                m_Stopwatch = new();
            }
            if (lineShader == null)
            {
                lineShader = Shader.Find("Universal Render Pipeline/Lit");
            }
            try
            {
                folder = Path.GetFullPath(folder);
            }
            catch
            {
                isProcessing = false;
                return;
            }
            var saveFiles = new List<string>();
            if (File.Exists(folder))
            {
                var ext = Path.GetExtension(folder).ToLower();
                if (ext == ".sldasm")
                {
                    saveFiles = new List<string> { folder };

                    m_ProdNo = Path.GetFileName(Directory.GetParent(Directory.GetParent(folder).FullName).FullName);
                }
            }
            else
            {
                if (Directory.Exists(folder))
                {
                    var dirs = Directory.GetDirectories(folder);
                    m_ProdNo = Path.GetFileName(Path.GetFullPath(folder));
                    foreach (var dir in dirs)
                    {
                        var tmp = Path.GetFileName(dir);
                        if ((tmp[tmp.Length - 1] == '0') && (tmp[0] != 'Z'))
                        {
                            // ロードすべきフォルダ
                            var files = Directory.GetFiles(Path.Combine(dir)).ToList();
                            if (files.Count > 0)
                            {
                                Regex regex = new Regex(@"[A-Za-z0-9]{3,}-[A-Za-z]+0-00-00");
                                var file = files.Find(d => regex.IsMatch(d));
                                saveFiles.Add(file);
                            }
                        }
                    }
                }
            }
            if (saveFiles.Count > 0)
            {
                for (var i = 0; i < saveFiles.Count; i++)
                {
                    m_FilePaths = new() { saveFiles[i] };
                    await ImportProcessTask();
                }
                isSuccess = true;
            }
            if (isSuccess)
            {
                // タイトル、メッセージ、ボタン名
                EditorUtility.DisplayDialog("情報", "Prefab作成処理が完了しました。", "OK");
                string projectPath = Directory.GetParent(UnityEngine.Application.dataPath).FullName;
                string folderPath = Path.Combine(Path.Combine(Path.Combine(projectPath, "Assets"), PixyzProjectSettings.PrefabFolder), m_ProdNo);
                // エクスプローラーで開く
                System.Diagnostics.Process.Start("explorer.exe", folderPath);
            }
            else
            {
                // タイトル、メッセージ、ボタン名
                EditorUtility.DisplayDialog("情報", "Prefab作成処理に失敗しました。", "OK");
            }
            isProcessing = false;
        }

        /// <summary>
        /// インポート処理
        /// </summary>
        /// <returns></returns>
        private Task ImportProcessTask()
        {
            ImportProgress.Enabled = true;

            // 初期設定
            SetModuleProperties();
            SetTolerances();

            var task = Task.Factory.StartNew(() =>
            {
                Core.SetCurrentThreadAsProcessThread();
                Core.ResetSession();

                if (ImportProgress.IsCanceling)
                    return;

                if (m_Settings.Orientation == ModelOrientation.Automatic)
                {
                    // Orientation is set to automatic. Model will be correctly oriented inside Pixyz (y-up / right-handed).
                    m_Settings.IsLeftHanded = false;
                    m_Settings.IsZUp = false;
                }

                ImportNative();

            });
            return task.ContinueWith(t =>
            {
                Core.SetCurrentThreadAsProcessThread();
                try
                {
                    PostProcess(rootAsset =>
                    {
                        EditorUtility.ClearProgressBar();
                        HandleSuccess(rootAsset);
                    });
                }
                catch (Exception e)
                {
                    EditorUtility.ClearProgressBar();
                    HandleException(e);
                }
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        #region Importer
        static string m_OpenedPrefabPath = null;
        static bool m_ContextSceneExists = false;
        static Scene m_ImportScene;
        private void SetUpImportContext()
        {
            // PIXPLUG-1057
            PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null)
            {
                m_OpenedPrefabPath = stage.assetPath;
                StageUtility.GoToMainStage();
            }

            // If we are in an untitled scene, we import there
            if (!string.IsNullOrEmpty(SceneManager.GetActiveScene().path))
            {
                m_ImportScene = CreateNewScene();
                m_ContextSceneExists = true;
            }
        }

        private Scene CreateNewScene()
        {
            LightmapData[] originalLightmaps = LightmapSettings.lightmaps;
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            scene.name = "Importer";
            LightmapSettings.lightmaps = originalLightmaps;
            return scene;
        }

        private void HandleSuccess(UnityEngine.Object rootAsset)
        {
            TearDownImportContext();
            Core.ResetSession();
            ImportCompleted?.Invoke(rootAsset, true);
            ImportProgress?.Finish();
        }

        private void HandleException(Exception e)
        {
            TearDownImportContext();
            Core.ResetSession();
            ImportCompleted?.Invoke(null, false);
            ImportProgress?.Failed();
        }
        private void TearDownImportContext()
        {
            if (m_ContextSceneExists)
                EditorSceneManager.CloseScene(m_ImportScene, true);

            if (!string.IsNullOrEmpty(m_OpenedPrefabPath))
                PrefabStageUtility.OpenPrefab(m_OpenedPrefabPath);

            Undo.CollapseUndoOperations(m_CurrentUndo);
        }
        private void ApplyCoordinateSystemConversion(GameObject root)
        {
            /*
            Matrix4x4 transformation = root.transform.GetLocalMatrix();
            if (!m_Settings.IsLeftHanded && !m_Settings.AvoidNegativeScale)
            {
                Matrix4x4 symmetry = Matrix4x4.identity;
                symmetry.m00 = -1.0f;
                transformation *= symmetry;
            }
            if (m_Settings.IsZUp)
            {
                var rotate = Matrix4x4.identity;
                rotate.m11 = rotate.m22 = 0.0f;
                rotate.m12 = 1.0f;
                rotate.m21 = -1.0f;
                transformation *= rotate;
            }
            if (transformation != Matrix4x4.identity)
            {
                root.transform.SetFromLocalMatrix(transformation);
            }
            */
        }

        private static void SimplifyMaterials(PiXYZAPI pxz)
        {
            MaterialList materials = pxz.Material.GetAllMaterials();
            pxz.Scene.MergeMaterials(materials, true);
            ImageList images = pxz.Material.GetAllImages(materials);
            if (images.length > 0)
            {
                pxz.Scene.MergeImages(images);
            }
        }

        private static void TransformModel(uint root, bool avoidNegativeScale)
        {
            Scene.StartModifyAllVariants();

            try
            {
                OccurrenceList roots = new(new uint[] { root });

                if (avoidNegativeScale)
                {
                    // Apply a left handed conversion on root
                    // Then, calling Scene.RemoveSymmetryMatrices applies this transformation directly in vertices
                    Matrix4 matLeftHanded = new Matrix4();
                    matLeftHanded.Identity();
                    matLeftHanded.tab[0] = new Array4(new double[] { -1.0, 0.0, 0.0, 0.0 });
                    Scene.ApplyTransformation(root, matLeftHanded);
                    // No need to call TransformPmiView here, as the coordinates stay the same.
                    Scene.RemoveSymmetryMatrices(root);
                }
            }
            catch
            {
                Scene.EndModifyAllVariants();
                throw;
            }
            Scene.EndModifyAllVariants();
        }
        #endregion Importer

        #region CADImporter
        private void SetModuleProperties()
        {
            Core.SetModuleProperty("IO", "PreferAlternativeImporters", "False");
            Core.SetModuleProperty("IO", "FlipCoordinateSystem", "True");
            Core.SetModuleProperty("Tessellate", "GenerateQuads", "False");

            Core.SetModuleProperty("IO", "AliasApiDllPath", Preferences.AliasExecutable);
            Core.SetModuleProperty("IO", "VredExecutablePath", Preferences.VREDExecutable);
            Core.SetModuleProperty("IO", "ImportAnimations", "False");
        }

        private void SetTolerances()
        {
            m_Tolerances = new Tolerances(Quality.Maximum);

            //Set Line Tolerance with a quality above the one selected (when possible)
            if (m_Settings.MeshQuality != Quality.Custom && m_Settings.MeshQuality != Quality.Maximum)
            {
                m_LineTolerances = new Tolerances((Quality)((int)m_Settings.MeshQuality - 1));
            }
            else
            {
                m_LineTolerances = new Tolerances(m_Settings.MeshQuality);
            }
        }


        /// <summary>
        /// Tessellate lines with a higher quality setting, to preserve sharper details on these very visible elements.
        /// </summary>
        private void TessellateLines(uint root)
        {
            List<uint> lines = new();
            OccurrenceList partOccs = Scene.GetPartOccurrences(root);
            ComponentList parts = Scene.GetComponents(partOccs, ComponentType.Part);
            ModelList models = Scene.GetPartsModels(new PartList(parts.list));
            for (int i = 0; i < parts.length; i++)
            {
                try
                {
                    string brepShape = Core.GetProperty(parts[i], "BRepShapeInitial");
                    if (String.IsNullOrEmpty(brepShape))
                        continue;

                    if (models[i] == 0)
                        continue;

                    EdgeList edges = m_API.CAD.GetModelEdges(models[i]);
                    if (edges.length > 0)
                    {
                        lines.Add(Scene.GetComponentOccurrence(parts[i]));
                    }
                }
                catch
                {
                    continue;
                }
            }

            if (lines.Count > 0)
            {
                OccurrenceList cadLines = new OccurrenceList(lines.ToArray());
                Algo.TessellateRelativelyToAABB(cadLines,
                    maxSag: m_LineTolerances.MaxSag,
                    sagRatio: m_LineTolerances.SagRatio,
                    maxLength: -1,
                    maxAngle: m_LineTolerances.MaxAngle,
                    createNormals: true,
                    uvMode: UnityEngine.Pixyz.Algo.UVGenerationMode.NoUV,
                    createTangents: false,
                    createFreeEdges: false,
                    keepBRepShape: false,
                    overrideExistingTessellation: false
                );
            }
        }

        /// <summary> Read the file in the Asset Transformer Core native assemblies </summary>
        private void ImportNative()
        {
            // TODO: to remove with PIXPLUG-966
            if (m_Settings.ImportAllFilesInFolder && m_Settings.ImportAnimations)
            {
                Debug.LogWarning("Import multiple files with animations at once may result in unexpected results");
            }
            // TODO: to remove with PIXPLUG-969
            if (m_Settings.ImportAllFilesInFolder && m_Settings.ImportPMI)
            {
                Debug.LogWarning("Import multiple files with PMI at once may result in unexpected results");
            }

            Core.SetModuleProperty("IO", "LoadVariant", m_Settings.ImportVariants || m_Settings.ImportPMI ? "True" : "False");
            Core.SetModuleProperty("IO", "PreferLoadMesh", "false");

            Core.SetModuleProperty("IO", "LoadPMI", m_Settings.ImportPMI ? "True" : "False");


            // Import in Pixyz
            using (new NativeProgress(m_API, ImportProgress, "Reading file..."))
            {
                // Import in Pixyz
                OccurrenceList occurrences = IO.ImportFiles(new FilesList(m_FilePaths.ToArray()));

                if (occurrences.length > 1)
                {
                    m_Root = Scene.GetRoot();
                }
                else
                {
                    m_Root = occurrences[0];
                    m_FileName = Path.GetFileNameWithoutExtension(m_FilePaths[0]);
                }
            }

            ImportProgress.Report(0f, "Reading file : " + m_FileName);

            // Process in Pixyz
            ProcessNative();

            if (m_Settings.ImportNestedPrefabs)
            {
                Scene.CleanInstances(true, true);
            }

            if (ImportProgress.IsCanceling)
                return;

            //ProcessLODGeneratorNative(m_Root);

            ImportProgress.Report(1f, "Finishing importing...");
        }

        private void ProcessNative()
        {
            OccurrenceList roots = new OccurrenceList(new uint[] { m_Root });

            if (ImportProgress.IsCanceling) return;

            if (!m_Settings.ImportPoints)
            {
                Algo.DeleteFreeVertices(roots);
                if (ImportProgress.IsCanceling) return;
            }

            if (!m_Settings.ImportLines)
            {
                Algo.DeleteLines(roots);
                if (ImportProgress.IsCanceling) return;
            }
            else //Custom Line Tesselation
            {
                TessellateLines(m_Root);
            }

            OccurrenceList openShells = Scene.MergePartOccurrencesWithSingleOpenShellByAssemblies(m_Root);
            if (ImportProgress.IsCanceling) return;

            OccurrenceList unstitchedFacesOccurrences = new();
            if (!m_Settings.ReorientFaces)
            {
                unstitchedFacesOccurrences = Scene.FindPartOccurrencesWithUnstitchedOpenShells(m_Root);
                if (ImportProgress.IsCanceling) return;
            }

            // Compute a minimal tolerance to avoid repairing really small models with a tolerance bigger than 2/10000 of their sizes.
            double tolerance = GetRelativeTolerance(m_API, roots, 0.1, 0.0002);

            using (new NativeProgress(m_API, ImportProgress, "Repairing model..."))
                Algo.RepairCAD(roots, tolerance, false);

            if (ImportProgress.IsCanceling) return;

            using (new NativeProgress(m_API, ImportProgress, "Repairing model..."))
                Algo.RepairMesh(roots, tolerance, true, false);

            if (ImportProgress.IsCanceling) return;

            // Decimate (if original model has mesh data)
            // Decimate before tessellation to avoid decimating the awesome tessellation
            if (m_Settings.MeshQuality != Quality.Maximum)
            {
                using (new NativeProgress(m_API, ImportProgress, "Decimating model..."))
                    Algo.Decimate(roots, m_Tolerances.SurfacicTolerance, m_Tolerances.LineicTolerance, m_Tolerances.NormalTolerance, m_Tolerances.UVTolerance, false);
                if (ImportProgress.IsCanceling) return;
            }

            // Tessellate (if original model has BREP data)
            // Do not tesselate if there is existig tesselation (only with pxz file)
            // Delete all BREP info to free memory space
            using (new NativeProgress(m_API, ImportProgress, "Tessellating model..."))
            {
                Algo.TessellateRelativelyToAABB(roots,
                    maxSag: m_Tolerances.MaxSag,
                    sagRatio: m_Tolerances.SagRatio,
                    maxLength: -1,
                    maxAngle: m_Tolerances.MaxAngle,
                    createNormals: true,
                    uvMode: UnityEngine.Pixyz.Algo.UVGenerationMode.NoUV,
                    createTangents: false,
                    createFreeEdges: false,
                    keepBRepShape: false,
                    overrideExistingTessellation: false
                );
            }

            if (ImportProgress.IsCanceling) return;

            // Tesselate can re-create lines from brep curves into meshes
            if (!m_Settings.ImportLines)
            {
                Algo.DeleteLines(roots);
                if (ImportProgress.IsCanceling) return;
            }

            if (m_Settings.RepairInstances)
            {
                using (new NativeProgress(m_API, ImportProgress, "Repairing instances..."))
                {
                    Algo.ConvertSimilarPartOccurrencesToInstancesFast(roots,
                        dimensionsSimilarity: 0.99,
                        polycountSimilarity: 0.99,
                        ignoreSymmetry: true
                    );
                }
                if (ImportProgress.IsCanceling) return;
            }

            if (m_Settings.ReorientFaces)
            {
                Algo.OrientPolygonFaces(roots);
                if (ImportProgress.IsCanceling) return;
            }
            else
            {
                if (openShells.length > 0)
                {
                    Algo.OrientPolygonFaces(openShells);
                    if (ImportProgress.IsCanceling) return;
                }
                if (unstitchedFacesOccurrences.length > 0)
                {
                    Algo.OrientPolygonFaces(unstitchedFacesOccurrences);
                    if (ImportProgress.IsCanceling) return;
                }
            }
            // Create free edges after tessellation to create them also on imported meshes
            if (m_Settings.ImportPatchBoundaries)
            {
                using (new NativeProgress(m_API, ImportProgress, "Creating patch boundaries..."))
                    Algo.CreateFreeEdgesFromPatches(roots);
            }

            Algo.DeletePatches(roots);

            SimplifyMaterials(m_API);

            if (ImportProgress.IsCanceling) return;

            Algo.CreateNormals(roots, 45, overriding: false, useAreaWeighting: true);

            if (ImportProgress.IsCanceling) return;

            if (m_Settings.Use16BitsBuffers)
            {
                Algo.ExplodeByVertexCount(roots, 65534, 65534, false);
            }

            if (ImportProgress.IsCanceling) return;

            Algo.OrientNormals(roots);

            if (ImportProgress.IsCanceling) return;

            if (m_Settings.CreateUVs && m_Settings.UVSize > 0)
            {
                using (new NativeProgress(m_API, ImportProgress, "Generating UVs and tangents...", stepCount: 2))
                {
                    Algo.MapUvOnAABB(roots, false, m_Settings.UVSize * 1000, channel: 0, overrideExistingUVs: true);
                    Algo.CreateTangents(roots, uvChannel: 0, overriding: false);
                }
            }

            if (ImportProgress.IsCanceling) return;

            if (m_Settings.CreateLightmapUVs && m_Settings.LightmapResolution > 0)
            {
                // Compute lightmap uvs in one UV space per part. Probably slower but more logical. However Unity scales itself lightmap uvs for each part...
                using (new NativeProgress(m_API, ImportProgress, "Generating Lightmap UVs...", stepCount: 3))
                {
                    Algo.MapUvOnAABB(roots, false, 1, channel: 1, overrideExistingUVs: true);
                    Algo.RepackUV(roots, channel: 1, shareMap: false, resolution: m_Settings.LightmapResolution, padding: (uint)m_Settings.LightmapPadding, uniformRatio: false, removeOverlaps: false);
                    Algo.NormalizeUV(roots, sourceUVChannel: 1, destinationUVChannel: -1, uniform: true, sharedUVSpace: false, ignoreNullIslands: false);
                }
            }

            if (ImportProgress.IsCanceling) return;

            TransformModel(m_Root, m_Settings.AvoidNegativeScale);
        }

        /// <summary>
        /// ポストプロセス
        /// </summary>
        /// <param name="resetVariablesCallback"></param>
        private void PostProcess(Action<UnityEngine.Object> resetVariablesCallback)
        {
            GetSubTreeStatsReturn stats = Scene.GetSubTreeStats(new OccurrenceList(new uint[] { Scene.GetRoot() }));
            
            if (stats.partOccurrenceCount > 10000 && m_Settings.ImportMode != ImportMode.Scene)
            {
                /*
                PersistantAskDialog askDialog = PersistantAskDialog.Create(
                        "Large File Warning",
                        $"This file contains {stats.partCount.ToString("#,0").Replace(",", " ")} meshes and {stats.triangleCount.ToString("#,0").Replace(",", " ")} triangles.\n\nPrefab mode will be slow to import and bloat the Asset Database. Scene mode provides \nfaster import and better performance for large files.\n\nRecommendation: Use Scene Mode for better performance.",
                        "att_large_file_warning",
                        525,
                        150,
                        "Switch to Scene Mode",
                        "Keep Prefab Mode"
                    );
                askDialog.OnSelectionMade += (bool accepted) =>
                {
                    if (accepted)
                    {
                        //This will change the setting in the Inspector as well
                        m_OriginalSettings.ImportMode = ImportMode.Scene;
                        m_Settings.ImportMode = ImportMode.Scene;
                    }
                    PostProcessInternal(resetVariablesCallback);
                };
                askDialog.Show();
                */
            }
            else
            {
                PostProcessInternal(resetVariablesCallback);
            }
        }

        private void PostProcessInternal(Action<UnityEngine.Object> resetVariablesCallback)
        {
            EditorUtility.DisplayProgressBar($"{m_FileName}", "Creating Unity assets...", -1f);

            // Import from Pixyz to Unity
            CreateUnityAssets(out GameObject root, out GameObject[] nestedPrefabs,
                m_Settings.ImportMetadata,
                m_Settings.ImportVariants,
                m_Settings.ImportPMI,
                m_Settings.ImportAnimations,
                m_Settings.ImportNestedPrefabs);

            // Save statistics for analytics (before rule engine and lods)
            SaveStatisticsForAnalytics(root);

            // Run rules if there are any
            /*
            if (m_Settings.RuleSet != null && m_Settings.RuleSet.RulesCount > 0)
            {
                EditorUtility.ClearProgressBar();
                ProcessRuleEngine(root, (success) =>
                {
                    GenerateLODs(root);
                    StampModel(root);
                    UnityEngine.Object rootAsset = SaveImportedModel(root, nestedPrefabs);
                    resetVariablesCallback(rootAsset);
                });
            }
            else
            {
                GenerateLODs(root);
                StampModel(root);
                UnityEngine.Object rootAsset = SaveImportedModel(root, nestedPrefabs);
                resetVariablesCallback(rootAsset);
            }
            */
            GenerateLODs(root);
            StampModel(root);
            UnityEngine.Object rootAsset = SaveImportedModel(root, nestedPrefabs);
            resetVariablesCallback(rootAsset);
        }

        private void CreateUnityAssets(out GameObject root, out GameObject[] nestedPrefabs, bool importMetadata, bool importVariants, bool importPmi, bool importAnimations, bool importPrototypes)
        {
            List<ComponentType> syncedComponents = new List<ComponentType>() { ComponentType.Part };
            if (importMetadata) syncedComponents.Add(ComponentType.Metadata);
            if (importVariants) syncedComponents.Add(ComponentType.Variant);
            if (importPmi) syncedComponents.Add(ComponentType.PMI);
            if (importAnimations)
            {
                syncedComponents.Add(ComponentType.Animation);
                syncedComponents.Add(ComponentType.Joint);
            }

            SetUpImportContext();

            // Converting the Scene from Core data to Unity data structure
            SceneConverter converter = new SceneConverter()
            {
                API = m_API,
                ScaleFactor = 0.001f,//m_Settings.Scale,
                LineShader = lineShader,
                PointShader = lineShader,
                SyncComponents = syncedComponents.ToArray(),
                EnableUndo = false,
                ImportPrototypes = importPrototypes,
                HierarchyLoad = SceneConverter.HierarchyLoadType.New
            };

            m_EntityToObject = new Dictionary<uint, UnityEngine.Object>();
            root = converter.ToUnity(Scene.GetRoot(), entityToObject: m_EntityToObject);
            ApplyCoordinateSystemConversion(root);
            root.name = m_FileName;
            nestedPrefabs = converter.PrototypesPrefabs;

            // Data is now in Unity format, we can safely reset the session
            Core.ResetSession();
        }

        private void SaveStatisticsForAnalytics(GameObject root)
        {
            if (!EditorAnalytics.enabled)
                return;

            //        Statistics.GetStatistics(root, ref m_SceneStatistics, false, true);
        }

        private void GenerateLODs(GameObject root)
        {
            /*
            // Run LODs generation
            if (m_Settings.LODGenerator != null && m_Settings.LODGenerator.Rules.Count > 0)
            {
                EditorUtility.DisplayProgressBar($"{m_FileName}", "Generating LODs...", -1f);
                ProcessLODGenerator(root);
            }
            */
        }

        private ImportStamp StampModel(GameObject root)
        {
            m_Stopwatch.Stop();

            ImportStamp importStamp = null;

            if (PixyzProjectSettings.AddImportStampComponent)
            {
                importStamp = root.AddComponent<ImportStamp>();
                importStamp.Stamp(m_FilePaths[0], m_Stopwatch.ElapsedTicks);

                Component[] components = root.GetComponents<Component>();
                int index = Array.IndexOf(components, importStamp);
                while (index > 0)
                {
                    UnityEditorInternal.ComponentUtility.MoveComponentUp(importStamp);
                    index--;
                }
                Undo.ClearUndo(importStamp); // Calls to MoveComponentUp adds an Undo operation to the stack, which mess with our importer.
            }
            return importStamp;
        }

        private UnityEngine.Object SaveImportedModel(GameObject root, GameObject[] nestedPrefabs)
        {
            // Save Prefab and assets
            EditorUtility.DisplayProgressBar($"{m_FileName}", "Saving Prefab...", -1f);
            return SavePrefabAndAssets(root, nestedPrefabs);
        }

        /// <summary>
        /// Save prefab at PixyzProjectSettings.PrefabFolder if Prefab does not already exist, otherwise replace it at its current location.
        /// </summary>
        private GameObject SavePrefabAndAssets(GameObject root, GameObject[] nestedPrefabs)
        {
            if (root == null)
                return null;

            GameObject prefab = null;

            SetupFolder(out string path, out string fileName);

            prefab = SavePrefab(root, path);

            GameObject.DestroyImmediate(root);

            if (nestedPrefabs.Length > 0)
            {
                string nestedFolder = Path.Join(Path.GetDirectoryName(path), fileName);

                if (AssetDatabase.IsValidFolder(nestedFolder))
                    AssetDatabase.DeleteAsset(nestedFolder);

                AssetDatabase.CreateFolder(Path.GetDirectoryName(path), fileName);

                for (int i = 0; i < nestedPrefabs.Length; i++)
                {
                    GameObject nestedPrefab = nestedPrefabs[i];
                    string nestedPrefabPath = Path.Join(nestedFolder, nestedPrefab.name + ".prefab");
                    AssetDatabase.MoveAsset(AssetDatabase.GetAssetPath(nestedPrefab), nestedPrefabPath);

                    var instances = PrefabUtility.FindAllInstancesOfPrefab(nestedPrefab);
                    foreach (var instance in instances)
                    {
                        if (instance.transform.parent == null)
                        {
                            PrefabUtility.ApplyPrefabInstance(instance, InteractionMode.AutomatedAction);
                            GameObject.DestroyImmediate(instance);
                        }
                    }
                }
            }
            return prefab;
        }

        private void SetupFolder(out string path, out string fileName)
        {
            string folderPath = "Assets/" + PixyzProjectSettings.PrefabFolder + "/" + m_ProdNo;
            fileName = m_FileName;
            path = folderPath + "/" + m_FileName + ".prefab";
            if (!Directory.Exists(folderPath))
            {
                Directory.CreateDirectory(folderPath);
            }
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        #endregion CADImporter

        #region PixyzPrefabUtilities
        private GameObject SavePrefab(GameObject gameObject, string path)
        {
            GameObject prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            UnityEngine.Object[] oldDependencies = prefabAsset == null ? null : GetSafeToDeletePrefabDependencies(path).ToArray();

            string projectPath = Directory.GetParent(UnityEngine.Application.dataPath).FullName;
            Directory.CreateDirectory(Path.GetDirectoryName(projectPath + "/" + path));

            if (PixyzProjectSettings.StoreAssetsInPrefabs)
            {
                prefabAsset = PrefabUtility.SaveAsPrefabAssetAndConnect(gameObject, path, InteractionMode.AutomatedAction);
            }

            UnityEngine.Object[] dependencies = GetGameObjectDependencies(gameObject).ToArray();
            SavePrefabDependencies(path, PixyzProjectSettings.StoreAssetsInPrefabs, dependencies, oldDependencies);

            prefabAsset = PrefabUtility.SaveAsPrefabAssetAndConnect(gameObject, path, InteractionMode.AutomatedAction);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            //Force Prefab Instances refresh. This is to avoid mesh not showing because prefab didn't refresh internally even though all data are correctly set.
            GameObject[] instances = PrefabUtility.FindAllInstancesOfPrefab(prefabAsset);
            foreach (GameObject instance in instances)
            {
                UnityEngine.Pixyz.UnitySDK.Components.Metadata comp = instance.AddComponent<UnityEngine.Pixyz.UnitySDK.Components.Metadata>();
                PrefabUtility.RevertAddedComponent(comp, InteractionMode.AutomatedAction);
            }

            return prefabAsset;
        }

        /// <summary>
        /// This function returns dependencies of a prefab that are considered safe to delete:
        /// - not used anymore by the prefab
        /// - stored inside the prefab (sub asset) OR in a folder next to the prefab with the same name
        /// </summary>
        private HashSet<UnityEngine.Object> GetSafeToDeletePrefabDependencies(string prefabPath, bool checkAssetDatabase = false, bool getTextures = true, bool getMaterials = true, bool getMeshes = true)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);

            if (prefab == null)
                return new HashSet<UnityEngine.Object>();

            UnityEngine.Object[] folderAssets = GetPrefabFolderDependencies(prefabPath);
            HashSet<UnityEngine.Object> subAssets = AssetDatabase.LoadAllAssetRepresentationsAtPath(prefabPath).ToHashSet();
            return folderAssets.Union(subAssets).ToHashSet();
        }

        private UnityEngine.Object[] GetPrefabFolderDependencies(string prefabPath)
        {
            string folderPath = Path.GetDirectoryName(prefabPath);
            string folderName = Path.GetFileNameWithoutExtension(prefabPath);

            if (!Directory.Exists(Path.Combine(folderPath, folderName)))
                return new UnityEngine.Object[0];

            string[] assetPaths = AssetDatabase.FindAssets("", new string[] { folderPath + "/" + folderName });

            List<UnityEngine.Object> prefabAssets = new List<UnityEngine.Object>();
            for (int i = 0; i < assetPaths.Length; i++)
            {
                UnityEngine.Object asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(AssetDatabase.GUIDToAssetPath(assetPaths[i]));
                if (kSupportedTypes.Contains(asset.GetType()))
                    prefabAssets.Add(asset);
                prefabAssets.Add(asset);
            }

            return prefabAssets.ToArray();
        }

        private readonly static System.Type[] kSupportedTypes = new System.Type[] { typeof(Mesh), typeof(Material), typeof(Texture2D), typeof(AnimatorController), typeof(AnimationClip) };

        private HashSet<UnityEngine.Object> GetGameObjectDependencies(GameObject prefabInstanceObject, bool checkAssetDatabase = false, bool getTextures = true, bool getMaterials = true, bool getMeshes = true)
        {
            HashSet<UnityEngine.Object> dependencies = new HashSet<UnityEngine.Object>();
            Component[] components = prefabInstanceObject.GetComponentsInChildren<Component>(true);
            foreach (Component component in components)
            {
                if (component == null || component is Transform)
                    continue;
                SerializedObject objSO = new SerializedObject(component);
                SerializedProperty property = objSO.GetIterator();
                do
                {
                    if (property.propertyType != SerializedPropertyType.ObjectReference
                        || !property.objectReferenceValue)
                        continue;

                    if (!kSupportedTypes.Contains(property.objectReferenceValue.GetType()))
                        continue;

                    switch (property.objectReferenceValue)
                    {
                        case Material material:
                            Shader shader = material.shader;
                            if (getTextures)
                            {
                                for (int i = 0; i < shader.GetPropertyCount(); i++)
                                {
                                    if (shader.GetPropertyType(i) == UnityEngine.Rendering.ShaderPropertyType.Texture)
                                    {
                                        UnityEngine.Texture texture = material.GetTexture(shader.GetPropertyName(i));
                                        if (texture != null &&
                                            getTextures && (!AssetDatabase.Contains(texture) || !checkAssetDatabase))
                                        {
                                            dependencies.Add(texture);
                                        }
                                    }
                                }
                            }

                            if (getMaterials && (!AssetDatabase.Contains(material) || !checkAssetDatabase))
                            {
                                dependencies.Add(material);
                                Material matRef = material;
                                Material matRefParent;
                                while (matRef.isVariant)
                                {
                                    matRefParent = matRef.parent;
                                    if (!AssetDatabase.Contains(matRefParent) || !checkAssetDatabase)
                                    {
                                        dependencies.Add(matRefParent);
                                    }
                                    matRef = matRef.parent;
                                }
                            }
                            break;
                        case Mesh mesh:
                            if (getMeshes && (!AssetDatabase.Contains(mesh) || !checkAssetDatabase))
                            {
                                dependencies.Add(mesh);
                            }
                            break;
                        case AnimatorController animator:
                            if (!AssetDatabase.Contains(animator) || !checkAssetDatabase)
                            {
                                foreach (AnimationClip clip in animator.animationClips)
                                {
                                    dependencies.Add(clip);
                                }
                                //dependencies.Add(animator);
                            }
                            break;
                        default:
                            break;
                    }

                } while (property.Next(true));
            }
            return dependencies;
        }

        private void SavePrefabDependencies(string prefabPath, bool includeDepInPrefab, UnityEngine.Object[] dependencies, UnityEngine.Object[] oldDependencies)
        {
            string depPath = Path.Combine(Path.GetDirectoryName(prefabPath), Path.GetFileNameWithoutExtension(prefabPath));

            if (dependencies != null)
                SaveDependencyTexturesAsPNGs(depPath, ref dependencies, ref oldDependencies);

            try
            {
                AssetDatabase.StartAssetEditing();
                SaveAssets(dependencies, includeDepInPrefab ? prefabPath : depPath, includeDepInPrefab);
                DeleteAssets(GetUnusedAssets(dependencies, oldDependencies));
            }
            catch
            {
                AssetDatabase.StopAssetEditing();
                throw;
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }

            AssetDatabase.Refresh();
        }

        private void SaveDependencyTexturesAsPNGs(string path, ref UnityEngine.Object[] dependencies, ref UnityEngine.Object[] oldDependencies)
        {
            Texture2D[] textures = dependencies.OfType<Texture2D>().ToArray();

            if (textures == null || textures.Length == 0)
                return;
            /*
            EnsurePath(path);

            Material[] materials = dependencies.OfType<Material>().ToArray();
            AssetUtilities.SaveTexturesAsPNGs(path, textures, materials);

            //Remove them so they are skipped by SaveAssets and DeleteAssets following methods
            dependencies = dependencies.Except(textures).ToArray();
            if (oldDependencies != null)
            {
                oldDependencies = oldDependencies.Except(textures).ToArray();
            }
            */
        }

        private UnityEngine.Object[] GetUnusedAssets(UnityEngine.Object[] newDependencies, UnityEngine.Object[] oldDependencies)
        {
            if (oldDependencies == null)
                return new UnityEngine.Object[0];

            return oldDependencies.Except(newDependencies).ToArray();
        }

        private void EnsurePath(string dirPath)
        {
            if (dirPath.EndsWith(".prefab"))
                return;

            if (!Directory.Exists(dirPath))
                Directory.CreateDirectory(dirPath);
        }
        #endregion PixyzPrefabUtilities

        #region AssetUtilities
        internal void SaveAsset(UnityEngine.Object asset, string path, bool isSubAsset)
        {
            if (AssetDatabase.Contains(asset))
                return;

            AssetDatabase.AddObjectToAsset(asset, path);
        }

        internal void SaveAssets(UnityEngine.Object[] assets, string path, bool isSubAsset)
        {
            if (assets == null || assets.Length == 0)
                return;

            for (int i = 0; i < assets.Length; i++)
            {
                SaveAsset(assets[i], path, isSubAsset);
            }
        }

        internal void DeleteAssets(UnityEngine.Object[] assets, bool deleteEmptyFolders = true)
        {
            if (assets == null || assets.Length == 0)
                return;

            string[] oldAssetPaths = new string[assets.Length];

            for (int i = 0; i < assets.Length; i++)
            {
                if (assets[i] is Transform || assets[i] is GameObject || assets[i] == null)
                    continue;

                if (AssetDatabase.IsMainAsset(assets[i]))
                {
                    string path = AssetDatabase.GetAssetPath(assets[i]);
                    if (deleteEmptyFolders)
                        oldAssetPaths[i] = path;
                    AssetDatabase.DeleteAsset(path);
                }
                else
                {
                    AssetDatabase.RemoveObjectFromAsset(assets[i]);
                }
            }

            if (deleteEmptyFolders)
            {
                List<string> deleted = new List<string>();
                for (int i = 0; i < oldAssetPaths.Length; i++)
                {
                    if (string.IsNullOrEmpty(oldAssetPaths[i]))
                        continue;

                    string dirPath = Path.GetDirectoryName(oldAssetPaths[i]);
                    if (!deleted.Contains(dirPath) && Directory.GetFiles(dirPath).Length == 0)
                    {
                        Directory.Delete(dirPath);
                        File.Delete(dirPath + ".meta");
                        deleted.Add(dirPath);
                    }
                }
            }
        }

        internal void SaveAsset(UnityEngine.Object asset, string path)
        {
            if (asset is Material)
            {
                path += ".mat";
            }
            else
            {
                path += ".asset";
            }

            // Ensures directory exists (recursive)
            string projectPath = Directory.GetParent(UnityEngine.Application.dataPath).FullName;
            Directory.CreateDirectory(Path.GetDirectoryName(projectPath + "/" + path));

            path = AssetDatabase.GenerateUniqueAssetPath(path);

            AssetDatabase.CreateAsset(asset, path);
        }
        #endregion AssetUtilities

        #region PixyzUtilities
        public static double GetRelativeTolerance(PiXYZAPI pxz, OccurrenceList occurrences, double max = 0.1, double ratio = 0.0002)
        {
            // Compute a minimal tolerance to avoid repairing tiny models with a tolerance bigger than 2/10000 of their sizes.
            Bounds bounds = pxz.Scene.GetAABB(occurrences).ToUnity();
            double tolerance = max;
            double ratioCompute = (bounds.max - bounds.min).magnitude * ratio;
            tolerance = Math.Min(tolerance, ratioCompute);
            return tolerance;
        }
        #endregion PixyzUtilities
    }
}

public class InputDialogWindow : EditorWindow
{
    private string titleText;
    private string message;
    private string inputText;
    private Action<string> onOk;

    public static void Show(string title, string message, string defaultText, Action<string> onOkAction)
    {
        var win = CreateInstance<InputDialogWindow>();
        win.titleContent = new GUIContent(title);
        win.titleText = title;
        win.message = message;
        win.inputText = defaultText ?? "";
        win.onOk = onOkAction;
        win.minSize = new UnityEngine.Vector2(420, 160);
        win.maxSize = new UnityEngine.Vector2(800, 300);
        win.ShowUtility();
    }
    private void OnGUI()
    {
        EditorGUILayout.LabelField(message, EditorStyles.wordWrappedLabel);
        GUILayout.Space(6);

        EditorGUILayout.BeginHorizontal();
        inputText = EditorGUILayout.TextField(inputText);

        if (GUILayout.Button("フォルダ参照", GUILayout.Width(100)))
        {
            string selected = EditorUtility.OpenFolderPanel("フォルダ選択", inputText, "");
            if (!string.IsNullOrEmpty(selected))
                inputText = selected;
        }

        if (GUILayout.Button("ファイル参照", GUILayout.Width(100)))
        {
            string selected = EditorUtility.OpenFilePanelWithFilters("ファイル選択", inputText, new string[] { "SolidWorksファイル", "sldasm,SLDASM" });  // "フィルタ名", "拡張子リスト(カンマ区切り)";
            if (!string.IsNullOrEmpty(selected))
                inputText = selected;
        }

        EditorGUILayout.EndHorizontal();

        GUILayout.FlexibleSpace();
        EditorGUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();

        if (GUILayout.Button("OK", GUILayout.Width(100)))
        {
            onOk?.Invoke(inputText);
            Close();
        }

        if (GUILayout.Button("キャンセル", GUILayout.Width(100)))
        {
            onOk?.Invoke(null);
            Close();
        }

        EditorGUILayout.EndHorizontal();
    }
}
#endif
#endif
