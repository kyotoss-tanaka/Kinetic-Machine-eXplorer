#if UNITY_EDITOR
using System.Linq;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

public class DataBaseEditor : EditorWindow
{
    // TreeView�̏�Ԃ�ێ��ł���悤�ɂ��邽�߂̏��
    private TreeViewState _treeViewState;
    // TreeView�̃C���X�^���X
    private DataBaseTreeView _treeView;
    // �������̃C���X�^���X
    private SearchField _searchField;

    [MenuItem("Kyotoss/DataBase", false, 151)]
    private static void ShowWindow()
    {
        DataBaseEditor window = GetWindow<DataBaseEditor>();
        window.titleContent = new GUIContent("DataBase");
    }

    private void OnGUI()
    {
        // �l��null�̏ꍇ�͏�����
        _treeViewState ??= new TreeViewState();
        _treeView ??= new DataBaseTreeView(_treeViewState);
        _searchField ??= new SearchField();

        if (GUILayout.Button(EditorGUIUtility.TrIconContent("Refresh", "Reload"), GUILayout.Width(30)))
        {
            // DB�X�V
            GlobalScript.RenewDatabase(FindObjectsByType<ComPostgres>(FindObjectsSortMode.None).ToList());
            GlobalScript.RenewDatabase(FindObjectsByType<ComMongo>(FindObjectsSortMode.None).ToList());
            GlobalScript.RenewDatabase(FindObjectsByType<ComOpcUA>(FindObjectsSortMode.None).ToList());

            _treeView.Reload();
            _treeView.ExpandAll();
        }

        // �������̗̈���擾
        var searchRect = EditorGUILayout.GetControlRect(false, GUILayout.ExpandWidth(true), GUILayout.Height(EditorGUIUtility.singleLineHeight));
        // ��������`��
        _treeView.searchString = _searchField.OnGUI(searchRect, _treeView.searchString);

        // �E�B���h�E�S�̗̂̈���擾
        var treeViewRect = EditorGUILayout.GetControlRect(false, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));

        // TreeView��`��
        _treeView.OnGUI(treeViewRect);
    }
}
#endif