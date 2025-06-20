#if UNITY_EDITOR
using System.Linq;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

public class DataBaseEditor : EditorWindow
{
    // TreeViewの状態を保持できるようにするための情報
    private TreeViewState _treeViewState;
    // TreeViewのインスタンス
    private DataBaseTreeView _treeView;
    // 検索窓のインスタンス
    private SearchField _searchField;

    [MenuItem("Kyotoss/DataBase", false, 151)]
    private static void ShowWindow()
    {
        DataBaseEditor window = GetWindow<DataBaseEditor>();
        window.titleContent = new GUIContent("DataBase");
    }

    private void OnGUI()
    {
        // 値がnullの場合は初期化
        _treeViewState ??= new TreeViewState();
        _treeView ??= new DataBaseTreeView(_treeViewState);
        _searchField ??= new SearchField();

        if (GUILayout.Button(EditorGUIUtility.TrIconContent("Refresh", "Reload"), GUILayout.Width(30)))
        {
            // DB更新
            GlobalScript.RenewDatabase(FindObjectsByType<ComPostgres>(FindObjectsSortMode.None).ToList());
            GlobalScript.RenewDatabase(FindObjectsByType<ComMongo>(FindObjectsSortMode.None).ToList());
            GlobalScript.RenewDatabase(FindObjectsByType<ComOpcUA>(FindObjectsSortMode.None).ToList());

            _treeView.Reload();
            _treeView.ExpandAll();
        }

        // 検索窓の領域を取得
        var searchRect = EditorGUILayout.GetControlRect(false, GUILayout.ExpandWidth(true), GUILayout.Height(EditorGUIUtility.singleLineHeight));
        // 検索窓を描画
        _treeView.searchString = _searchField.OnGUI(searchRect, _treeView.searchString);

        // ウィンドウ全体の領域を取得
        var treeViewRect = EditorGUILayout.GetControlRect(false, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));

        // TreeViewを描画
        _treeView.OnGUI(treeViewRect);
    }
}
#endif