using UnityEditor;
using UnityEngine;
public class BuildAssetBundles
{
    [MenuItem("Kyotoss/Build AssetBundles", false, 101)]
    static void BuildAllAssetBundles()
    {
        string outputPath = "Assets/AssetBundles";
        if (!System.IO.Directory.Exists(outputPath))
            System.IO.Directory.CreateDirectory(outputPath);

        BuildPipeline.BuildAssetBundles(
            outputPath,
            BuildAssetBundleOptions.None,
            BuildTarget.StandaloneWindows64
        );

        Debug.Log("AssetBundles built at: " + outputPath);
    }
}
