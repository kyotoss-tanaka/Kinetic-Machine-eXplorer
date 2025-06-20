#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

public class DataBaseTreeView : TreeView
{
    // TreeViewItemクラスを継承したクラスを宣言
    private class TreeViewItemWithTag : TreeViewItem
    {
        public string value { get; set; }
        public string device { get; set; }
        public TagInfo tag { get; set; }
        public List<GameObject> useTagObjects { get; set; } = new List<GameObject>();
        public string objects
        {
            get
            {
                if (useTagObjects.Count == 0)
                {
                    return "";
                }
                var ret = "[";
                try
                {
                    for (var i = 0; i < useTagObjects.Count; i++)
                    {
                        if (i != 0)
                        {
                            ret += "], [";
                        }
                        ret += useTagObjects[i].name;
                    }
                }
                catch
                {
                    return "";
                }
                return ret + "]";
            }
        }
    }

    /// <summary>
    /// 要素を移動できるか
    /// </summary>
    protected override bool CanStartDrag(CanStartDragArgs args) => true;

    /// <summary>
    /// ドラッグ開始時の処理
    /// </summary>
    protected override void SetupDragAndDrop(SetupDragAndDropArgs args)
    {
        DragAndDrop.PrepareStartDrag();
        var draggedRows = GetRows().Where(item => args.draggedItemIDs.Contains(item.id)).ToList();
        DragAndDrop.SetGenericData("TagDragging", draggedRows);
        DragAndDrop.objectReferences = new UnityEngine.Object[] { ((TreeViewItemWithTag)draggedRows[0]).tag }; // this IS required for dragging to work
        string title = draggedRows.Count == 1 ? draggedRows[0].displayName : "< Multiple >";
        DragAndDrop.StartDrag(title);
    }

    // コンストラクタ
    public DataBaseTreeView(TreeViewState state) : base(state, CreateHeader())
    {
        Reload();
    }

    /// <summary>
    /// ヘッダ作成
    /// </summary>
    /// <returns></returns>
    private static MultiColumnHeader CreateHeader()
    {
        // MultiColumnHeaderState.Column型の配列を作成
        var columns = new[]
        {
           new MultiColumnHeaderState.Column
           {
               // カラムのヘッダに表示する要素
               headerContent = new GUIContent("Name"),
               width = 200,
               minWidth = 150,
               autoResize = true
           },
           new MultiColumnHeaderState.Column
           {
               headerContent = new GUIContent("Device"),
               width = 200,
               minWidth = 150,
               autoResize = true
           },
           new MultiColumnHeaderState.Column
           {
               headerContent = new GUIContent("Value"),
               width = 200,
               minWidth = 150,
               autoResize = true
           },
           new MultiColumnHeaderState.Column
           {
               headerContent = new GUIContent("Objects"),
               width = 400,
               minWidth = 150,
               autoResize = true
           },
       };

        // 配列から MultiColumnHeaderState を構築
        var state = new MultiColumnHeaderState(columns);
        // State から MultiColumnHeader を構築
        return new MultiColumnHeader(state);
    }

    protected override TreeViewItem BuildRoot()
    {
        var root = new TreeViewItem { id = 0, depth = -1, displayName = "Root" };

        // 子要素は一旦空で
        root.children = new List<TreeViewItem>();

        // DB情報セット
        int id = 0;
        var items = new List<TreeViewItem>();
        foreach (var db in GlobalScript.tagDatas)
        {
            items.Add(new TreeViewItemWithTag { id = ++id, depth = 0, displayName = db.Key, value = "" });
            foreach (var mech in db.Value)
            {
                items.Add(new TreeViewItemWithTag { id = ++id, depth = 1, displayName = "MechId : " + mech.Key, value = "" });
                foreach (var tag in mech.Value)
                {
                    var value = tag.Value.isFloat ? tag.Value.fValue.ToString() : tag.Value.Value.ToString();
                    var item = new TreeViewItemWithTag { id = ++id, depth = 2, displayName = tag.Key, value = value };
                    item.tag = GlobalScript.GetTagInfo(db.Key, mech.Key, tag.Key);
                    item.useTagObjects = GlobalScript.GetUseTagObjects(item.tag);
                    item.device = item.tag != null ? item.tag.Device : "";
                    items.Add(item);
                }
            }
        }

        // 親子関係を登録
        SetupParentsAndChildrenFromDepths(root, items);

        return root;
    }

    protected override void RowGUI(RowGUIArgs args)
    {
        var item = (TreeViewItemWithTag)args.item;

        // 各列のフィールドを描画
        for (var i = 0; i < args.GetNumVisibleColumns(); ++i)
        {
            var rect = args.GetCellRect(i);
            var columnIndex = args.GetColumn(i);

            switch (columnIndex)
            {
                case 0:
                    // 1列目は要素名
                    // インデントする必要がある
                    rect.xMin += GetContentIndent(item);
                    EditorGUI.LabelField(rect, item.displayName);
                    break;
                case 1:
                    EditorGUI.LabelField(rect, item.device);
                    break;
                case 2:
                    EditorGUI.LabelField(rect, item.value);
                    break;
                case 3:
                    EditorGUI.LabelField(rect, item.objects);
                    break;
            }
        }
    }

    protected override void SelectionChanged(IList<int> selectedIds)
    {
        SingleClickedItem(selectedIds[0]);
        base.SelectionChanged(selectedIds);
    }

    protected override void SingleClickedItem(int id)
    {
        var objs = new List<GameObject>();
        var item = (TreeViewItemWithTag)FindItem(id, rootItem);
        foreach (var obj in item.useTagObjects)
        {
            if (!objs.Contains(obj))
            {
                objs.Add(obj);
            }
        }
        if (objs.Count > 0)
        {
            Selection.activeObject = objs[0];
        }
        base.SingleClickedItem(id);
    }

    protected override void DoubleClickedItem(int id)
    {
        var objs = new List<GameObject>();
        var item = (TreeViewItemWithTag)FindItem(id, rootItem);
        foreach (var obj in item.useTagObjects)
        {
            if (!objs.Contains(obj))
            {
                objs.Add(obj);
            }
        }
        if (objs.Count > 0)
        {
            var index = objs.IndexOf((GameObject)Selection.activeObject);
            index = (index + 1) % objs.Count;
            Selection.activeObject = objs[index];
        }
        base.DoubleClickedItem(id);
    }
}
#endif