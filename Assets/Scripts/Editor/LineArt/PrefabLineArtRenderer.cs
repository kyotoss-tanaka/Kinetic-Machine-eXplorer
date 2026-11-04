#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// プレハブから等角図風の線画（白地に黒線・隠れ線なし）を PNG で書き出す
/// 面の向き・奥行き・部品番号を描いてから、その差を線にする
/// </summary>
public static class PrefabLineArtRenderer
{
    const string ShaderDir = "Assets/Scripts/Editor/LineArt/";

    public class Settings
    {
        /// <summary>見る方向（真上から見て Z+ 側を 0°、時計回り）</summary>
        public float[] azimuths = { 45f, 135f, 225f, 315f };
        /// <summary>見下ろす角度</summary>
        public float elevation = 30f;
        /// <summary>出力画像の幅(px)</summary>
        public int width = 2400;
        /// <summary>内部で何倍の大きさで描いて縮小するか（2 のべき乗）</summary>
        public int supersample = 2;
        /// <summary>これ以上折れている面の境目を線にする(°)</summary>
        public float creaseAngle = 35f;
        /// <summary>線の太さ（内部解像度での px）</summary>
        public float thickness = 1f;
    }

    /// <summary>描く部品（メッシュとワールド行列）</summary>
    public struct Part
    {
        public Mesh mesh;
        public Matrix4x4 matrix;
        public Transform source;
    }

    /// <summary>カメラの向き・範囲と画像の大きさ</summary>
    public class ViewFrame
    {
        public Matrix4x4 view;
        public Matrix4x4 proj;
        /// <summary>内部の描画サイズ</summary>
        public int width, height;
        /// <summary>出力サイズ</summary>
        public int outWidth, outHeight;
        /// <summary>内部の 1px のワールド長さ</summary>
        public float pixel;
    }

    /// <summary>描画に使うマテリアル一式</summary>
    public sealed class Context : IDisposable
    {
        public readonly Settings settings;
        public readonly Material gbufferMat;
        public readonly Material edgeMat;
        public readonly Material overlayMat;
        public readonly Material idMat;

        public Context(Settings settings)
        {
            this.settings = settings;
            gbufferMat = new Material(LoadShader("LineArtGBuffer.shader"));
            edgeMat = new Material(LoadShader("LineArtEdge.shader"));
            overlayMat = new Material(LoadShader("LineArtOverlay.shader"));
            idMat = new Material(LoadShader("LineArtId.shader"));
        }

        static Shader LoadShader(string file)
        {
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderDir + file);
            if (shader == null) throw new FileNotFoundException("シェーダーが読めません: " + ShaderDir + file);
            return shader;
        }

        public void Dispose()
        {
            UnityEngine.Object.DestroyImmediate(gbufferMat);
            UnityEngine.Object.DestroyImmediate(edgeMat);
            UnityEngine.Object.DestroyImmediate(overlayMat);
            UnityEngine.Object.DestroyImmediate(idMat);
        }
    }

    [MenuItem("Kyotoss/Export Line Art (Selected Prefab)", false, 30)]
    static void ExportSelected()
    {
        var prefab = Selection.activeObject as GameObject;
        string outDir = EditorUtility.SaveFolderPanel("線画の保存先", "", "");
        if (string.IsNullOrEmpty(outDir)) return;
        var files = Export(prefab, outDir, new Settings());
        EditorUtility.RevealInFinder(files[0]);
    }

    [MenuItem("Kyotoss/Export Line Art (Selected Prefab)", true)]
    static bool ValidateExportSelected()
    {
        return Selection.activeObject is GameObject go && PrefabUtility.IsPartOfPrefabAsset(go);
    }

    /// <summary>
    /// バッチモード用：-lineArtPrefab &lt;Assets/...prefab&gt; -lineArtOut &lt;フォルダ&gt; [-lineArtWidth N] [-lineArtAzimuths 45,135]
    /// </summary>
    public static void RenderFromCommandLine()
    {
        try
        {
            string[] args = Environment.GetCommandLineArgs();
            string prefabPath = GetArg(args, "-lineArtPrefab");
            string outDir = GetArg(args, "-lineArtOut");
            var settings = ReadSettings(args);

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null) throw new FileNotFoundException("プレハブが読めません: " + prefabPath);
            Export(prefab, outDir, settings);
            EditorApplication.Exit(0);
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            EditorApplication.Exit(1);
        }
    }

    public static string GetArg(string[] args, string name)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    /// <summary>コマンドラインの -lineArtWidth / -lineArtAzimuths を読む</summary>
    public static Settings ReadSettings(string[] args)
    {
        var settings = new Settings();
        string width = GetArg(args, "-lineArtWidth");
        if (width != null) settings.width = int.Parse(width);
        string azimuths = GetArg(args, "-lineArtAzimuths");
        if (azimuths != null) settings.azimuths = Array.ConvertAll(azimuths.Split(','), float.Parse);
        return settings;
    }

    public static List<string> Export(GameObject prefab, string outDir, Settings settings)
    {
        var parts = new List<Part>();
        CollectParts(prefab.transform, Matrix4x4.identity, parts, null);
        var strays = FindStrays(parts, Enumerable.Range(0, parts.Count).ToList());
        var all = Enumerable.Range(0, parts.Count).Where(i => !strays.Contains(i)).ToList();
        Debug.Log($"[LineArt] {prefab.name}: 描く部品 {all.Count} 個（離れた小部品 {strays.Count} 個を除外: {PartNames(parts, strays)}）");

        Directory.CreateDirectory(outDir);
        var files = new List<string>();
        using (var ctx = new Context(settings))
        {
            foreach (float az in settings.azimuths)
            {
                var frame = Fit(parts, all, az, settings);
                var gbuffer = RenderGBuffer(ctx, parts, all, frame);
                var tex = DrawLines(ctx, gbuffer, frame);
                RenderTexture.ReleaseTemporary(gbuffer);
                var cropped = Crop(tex, ContentRect(tex, 24));
                string path = Path.Combine(outDir, $"{prefab.name}_{az:0}.png");
                File.WriteAllBytes(path, cropped.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(tex);
                UnityEngine.Object.DestroyImmediate(cropped);
                files.Add(path);
                Debug.Log($"[LineArt] 書き出し: {path}");
            }
        }
        return files;
    }

    /// <summary>
    /// 表示されているメッシュを集める（プレハブはシーンに置かずにそのまま読む）
    /// rootPose は root の置き場所（root 自身の localToWorld の代わりに使う）。skip に入っている物と子は描かない
    /// </summary>
    public static void CollectParts(Transform root, Matrix4x4 rootPose, List<Part> parts, HashSet<Transform> skip)
    {
        // root から見た行列を rootPose に付け替える
        var rebase = rootPose * root.worldToLocalMatrix;
        if (rootPose == Matrix4x4.identity) rebase = Matrix4x4.identity;
        var stack = new Stack<Transform>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var t = stack.Pop();
            if (!t.gameObject.activeSelf || (skip != null && skip.Contains(t))) continue;
            foreach (Transform c in t) stack.Push(c);

            Mesh mesh = null;
            var mr = t.GetComponent<MeshRenderer>();
            if (mr != null && mr.enabled)
            {
                var mf = t.GetComponent<MeshFilter>();
                mesh = mf != null ? mf.sharedMesh : null;
            }
            var smr = t.GetComponent<SkinnedMeshRenderer>();
            if (mesh == null && smr != null && smr.enabled) mesh = smr.sharedMesh;
            if (mesh == null) continue;
            parts.Add(new Part { mesh = mesh, matrix = rebase * t.localToWorldMatrix, source = t });
        }
    }

    /// <summary>
    /// 機械から遠く離れた小さな部品（目印の点など）を外す。図の範囲が広がって機械が小さくなるのを防ぐ
    /// 部品の中心の 1%〜99% の範囲を本体とし、本体から離れていて本体より十分小さい物を外す。外す部品の番号を返す
    /// </summary>
    public static HashSet<int> FindStrays(List<Part> parts, IList<int> indices)
    {
        var removed = new HashSet<int>();
        if (indices.Count < 20) return removed;
        var boxes = indices.Select(i => WorldBounds(parts[i])).ToList();
        float Pct(Func<Bounds, float> axis, float q)
        {
            var v = boxes.Select(axis).OrderBy(x => x).ToList();
            return v[Mathf.Clamp(Mathf.RoundToInt(q * (v.Count - 1)), 0, v.Count - 1)];
        }
        var core = new Bounds();
        core.SetMinMax(
            new Vector3(Pct(b => b.center.x, 0.01f), Pct(b => b.center.y, 0.01f), Pct(b => b.center.z, 0.01f)),
            new Vector3(Pct(b => b.center.x, 0.99f), Pct(b => b.center.y, 0.99f), Pct(b => b.center.z, 0.99f)));
        float diag = core.size.magnitude;
        core.Expand(diag * 0.25f);
        for (int k = 0; k < indices.Count; k++)
        {
            if (core.Intersects(boxes[k]) || boxes[k].size.magnitude > diag * 0.05f) continue;
            removed.Add(indices[k]);
        }
        return removed;
    }

    /// <summary>ログ用に部品名を並べる</summary>
    public static string PartNames(List<Part> parts, IEnumerable<int> indices, int max = 20)
    {
        var names = indices.Select(i => parts[i].source != null ? parts[i].source.name : parts[i].mesh.name).Distinct().ToList();
        return string.Join(", ", names.Take(max)) + (names.Count > max ? $" ほか {names.Count - max} 件" : "");
    }

    static Bounds WorldBounds(Part p)
    {
        var b = p.mesh.bounds;
        var wb = new Bounds(p.matrix.MultiplyPoint3x4(b.center), Vector3.zero);
        for (int i = 0; i < 8; i++)
        {
            wb.Encapsulate(p.matrix.MultiplyPoint3x4(new Vector3(
                (i & 1) == 0 ? b.min.x : b.max.x,
                (i & 2) == 0 ? b.min.y : b.max.y,
                (i & 4) == 0 ? b.min.z : b.max.z)));
        }
        return wb;
    }

    /// <summary>
    /// 指定の部品が全部入るようにカメラを合わせる
    /// </summary>
    public static ViewFrame Fit(List<Part> parts, IList<int> indices, float azimuth, Settings s)
    {
        float az = azimuth * Mathf.Deg2Rad;
        float el = s.elevation * Mathf.Deg2Rad;
        var toCamera = new Vector3(Mathf.Cos(el) * Mathf.Sin(az), Mathf.Sin(el), Mathf.Cos(el) * Mathf.Cos(az));
        var rot = Quaternion.LookRotation(-toCamera, Vector3.up);
        var inv = Quaternion.Inverse(rot);

        // カメラから見た範囲（各部品の箱の角で見積もる。余白は最後に切り詰める）
        var min = Vector3.positiveInfinity;
        var max = Vector3.negativeInfinity;
        foreach (int idx in indices)
        {
            var p = parts[idx];
            var b = p.mesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                var corner = new Vector3(
                    (i & 1) == 0 ? b.min.x : b.max.x,
                    (i & 2) == 0 ? b.min.y : b.max.y,
                    (i & 4) == 0 ? b.min.z : b.max.z);
                var local = inv * p.matrix.MultiplyPoint3x4(corner);
                min = Vector3.Min(min, local);
                max = Vector3.Max(max, local);
            }
        }
        var size = max - min;
        float margin = 0.02f * Mathf.Max(size.x, size.y);
        float hw = size.x / 2 + margin;
        float hh = size.y / 2 + margin;
        float pad = size.z * 0.1f + 1e-3f;

        int ss = Mathf.Max(1, Mathf.ClosestPowerOfTwo(s.supersample));
        var f = new ViewFrame();
        f.outWidth = s.width;
        f.outHeight = Mathf.Min(16384 / ss, Mathf.RoundToInt(s.width * hh / hw));
        f.width = f.outWidth * ss;
        f.height = f.outHeight * ss;

        var camPos = rot * new Vector3((min.x + max.x) / 2, (min.y + max.y) / 2, min.z - pad);
        // Unity のカメラと同じく視線は -z
        f.view = Matrix4x4.Scale(new Vector3(1, 1, -1)) * Matrix4x4.TRS(camPos, rot, Vector3.one).inverse;
        f.proj = Matrix4x4.Ortho(-hw, hw, -hh, hh, pad * 0.5f, size.z + pad * 1.5f);
        f.pixel = 2 * hw / f.width;
        return f;
    }

    /// <summary>
    /// 下描き（rg=面の向き b=奥行き a=部品番号+1、背景 a=-1）を描く。呼び出し側で RenderTexture.ReleaseTemporary する
    /// </summary>
    public static RenderTexture RenderGBuffer(Context ctx, List<Part> parts, IList<int> indices, ViewFrame f)
    {
        var gbuffer = RenderTexture.GetTemporary(f.width, f.height, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
        gbuffer.filterMode = FilterMode.Point;
        using (var cmd = new CommandBuffer { name = "LineArtGBuffer" })
        {
            cmd.SetRenderTarget(gbuffer);
            cmd.ClearRenderTarget(true, true, new Color(0, 0, 1e9f, -1));
            cmd.SetGlobalMatrix("_LineArtView", f.view);
            cmd.SetGlobalMatrix("_LineArtProj", GL.GetGPUProjectionMatrix(f.proj, true));
            var mpb = new MaterialPropertyBlock();
            foreach (int idx in indices)
            {
                mpb.SetFloat("_ObjectId", idx + 1);
                var mesh = parts[idx].mesh;
                for (int sub = 0; sub < mesh.subMeshCount; sub++)
                {
                    cmd.DrawMesh(mesh, parts[idx].matrix, ctx.gbufferMat, sub, 0, mpb);
                }
            }
            Graphics.ExecuteCommandBuffer(cmd);
        }
        return gbuffer;
    }

    static void SetEdgeParams(Material mat, Settings s, ViewFrame f)
    {
        mat.SetFloat("_CreaseCos", Mathf.Cos(s.creaseAngle * Mathf.Deg2Rad));
        mat.SetFloat("_DepthTol", f.pixel * 4 * s.thickness);
        mat.SetFloat("_ContactTol", f.pixel * 0.5f);
        mat.SetFloat("_Thickness", s.thickness);
    }

    /// <summary>白地に黒線の画像（出力サイズ）</summary>
    public static Texture2D DrawLines(Context ctx, RenderTexture gbuffer, ViewFrame f)
    {
        SetEdgeParams(ctx.edgeMat, ctx.settings, f);
        return BlitAndRead(gbuffer, ctx.edgeMat, f, TextureFormat.RGB24);
    }

    /// <summary>強調表示の透明画像（出力サイズ）。highlight は強調する部品だけの下描き、scene は全体の下描き</summary>
    public static Texture2D DrawOverlay(Context ctx, RenderTexture highlight, RenderTexture scene, ViewFrame f, Color color)
    {
        SetEdgeParams(ctx.overlayMat, ctx.settings, f);
        ctx.overlayMat.SetTexture("_SceneTex", scene);
        ctx.overlayMat.SetColor("_Color", color);
        ctx.overlayMat.SetFloat("_VisibleTol", f.pixel * 0.5f);
        return BlitAndRead(highlight, ctx.overlayMat, f, TextureFormat.RGBA32);
    }

    /// <summary>各画素の部品番号+1（出力サイズ、背景は -1。下の行から順）</summary>
    public static float[] ReadIds(Context ctx, RenderTexture gbuffer, ViewFrame f)
    {
        var rt = RenderTexture.GetTemporary(f.outWidth, f.outHeight, 0, RenderTextureFormat.RFloat, RenderTextureReadWrite.Linear);
        rt.filterMode = FilterMode.Point;
        Graphics.Blit(gbuffer, rt, ctx.idMat);
        var tex = ReadBack(rt, TextureFormat.RFloat);
        RenderTexture.ReleaseTemporary(rt);
        var ids = tex.GetPixelData<float>(0).ToArray();
        UnityEngine.Object.DestroyImmediate(tex);
        return ids;
    }

    /// <summary>内部サイズで描いて、半分ずつ縮小して（2x2 の平均で線をなめらかに）読み出す</summary>
    static Texture2D BlitAndRead(RenderTexture src, Material mat, ViewFrame f, TextureFormat format)
    {
        var full = RenderTexture.GetTemporary(f.width, f.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        full.filterMode = FilterMode.Bilinear;
        Graphics.Blit(src, full, mat);
        var current = full;
        while (current.width > f.outWidth)
        {
            var half = RenderTexture.GetTemporary(current.width / 2, current.height / 2, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            half.filterMode = FilterMode.Bilinear;
            Graphics.Blit(current, half);
            RenderTexture.ReleaseTemporary(current);
            current = half;
        }
        var tex = ReadBack(current, format);
        RenderTexture.ReleaseTemporary(current);
        return tex;
    }

    static Texture2D ReadBack(RenderTexture rt, TextureFormat format)
    {
        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var tex = new Texture2D(rt.width, rt.height, format, false, true);
        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        tex.Apply();
        RenderTexture.active = prev;
        return tex;
    }

    /// <summary>
    /// 線のある範囲（border px の余白付き）
    /// </summary>
    public static RectInt ContentRect(Texture2D tex, int border)
    {
        var px = tex.GetPixels32();
        int w = tex.width, h = tex.height;
        int x0 = w, y0 = h, x1 = -1, y1 = -1;
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                if (px[y * w + x].r >= 250) continue;
                x0 = Mathf.Min(x0, x); x1 = Mathf.Max(x1, x);
                y0 = Mathf.Min(y0, y); y1 = Mathf.Max(y1, y);
            }
        }
        if (x1 < 0) return new RectInt(0, 0, w, h);
        x0 = Mathf.Max(0, x0 - border); y0 = Mathf.Max(0, y0 - border);
        x1 = Mathf.Min(w - 1, x1 + border); y1 = Mathf.Min(h - 1, y1 + border);
        return new RectInt(x0, y0, x1 - x0 + 1, y1 - y0 + 1);
    }

    /// <summary>範囲を切り出した新しい画像（元の画像はそのまま）</summary>
    public static Texture2D Crop(Texture2D tex, RectInt r)
    {
        var cropped = new Texture2D(r.width, r.height, tex.format, false, true);
        cropped.SetPixels(tex.GetPixels(r.x, r.y, r.width, r.height));
        cropped.Apply();
        return cropped;
    }
}
#endif
