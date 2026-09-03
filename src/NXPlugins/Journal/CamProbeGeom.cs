// CamProbeGeom.cs — U-5 探针：从 Operation 读回关联几何（面集）可行性实证
// 结案（2026-09-03）：腔铣面级可枚举（CutAreaGeometry→13 Face，UF 类型/点/法向可取），
//   但 NXOpen.Face / UFModl 无面质心/面积 API → face_anchors 数值契约无生产源 →
//   PlanExporter 首版退「组级 + anchor 兜底」（决议见 docs/nx-plan-exporter-spec.md §5）。
// 本文件仅保留实测证据价值；U-5c（AskMassProps3d(face)）为后续独立实测候选。
//
// 读链（2026-09-03 反射实证）：op → CreateBuilder(op)（OperationBuilder.Geometry:
//   GeometryCiBuilder）；铣 Builder.PartGeometry/CutAreaGeometry : CAM.Geometry →
//   .GeometryList（Length/GetContents→GeometrySet[]）→ .GetItems()→TaggedObject[]
//   （含 NXOpen.Face）；op.CutRegionsData.GetCentroidPoints()/GetAreas()（区域级）；
//   UF：UFModl.AskFaceData(tag, out type, double[3] point, double[3] dir, double[6] box,
//   ref radius, ref rad_data, out norm_dir)。
// 注意：面面积无 UF AskFaceArea（.NET 未暴露）→ U-5b 已归档为负结果；U-5c（AskMassProps3d
//       对 face 对象）为唯一存活候选待实测（区域面积经 CutRegionsData 可用，仅腔铣）。
//
// 执行：干净 NX 会话（test.prt 未打开）→ Execute → NX Open 编译后 exe。
// 只读：不 Commit 不保存。参数：args[0]=输出（缺省 samples/camprobe-geom.txt）。

using System;
using System.Collections.Generic;
using System.IO;
using NXOpen;
using NXOpen.CAM;
using NXOpen.UF;
using Path = System.IO.Path;
using Operation = NXOpen.CAM.Operation;

public class CamProbeGeom
{
    private const string DefaultOut = @"C:\Users\21505\Code\nx-vibe-v2\samples\camprobe-geom.txt";
    private const string TestPart = @"C:\Users\21505\Code\nx-vibe-v2\samples\test.prt";
    private static readonly List<string> _lines = new List<string>();

    public static void Main(string[] args)
    {
        string outPath = args.Length > 0 && !string.IsNullOrEmpty(args[0]) ? args[0] : DefaultOut;
        try
        {
            _lines.Add("== CamProbeGeom (U-5) ==");
            _lines.Add("time: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            Session theSession = Session.GetSession();
            var parts = theSession.Parts;
            PartLoadStatus ls;
            Part part = parts.OpenDisplay(TestPart, out ls);
            parts.SetWork(part);
            CAMSetup cam = part.CAMSetup;
            _lines.Add("opened: " + part.Name + "  CAMSetup=" + (cam != null));

            ProbeOp(cam, "CAVITY_MILL");
            ProbeOp(cam, "打点_COPY_COPY_COPY");
        }
        catch (Exception ex)
        {
            _lines.Add("!! 顶层异常: " + ex.Message);
            if (ex.InnerException != null) _lines.Add("   inner: " + ex.InnerException.Message);
        }
        Finish(outPath);
    }

    private static void ProbeOp(CAMSetup cam, string opName)
    {
        _lines.Add("");
        _lines.Add("== 工序: " + opName + " ==");
        Operation op = FindOp(cam.GetRoot(CAMSetup.View.ProgramOrder), opName);
        if (op == null) { Note("未找到"); return; }

        // 1) 通用 Builder → Geometry
        OperationBuilder b = cam.CAMOperationCollection.CreateBuilder(op);
        try
        {
            R("CreateBuilder 类型", () => b.GetType().FullName);
            R("Geometry(HoleBossGeom 存在?)", () =>
                (b.Geometry == null ? "(null)" : "CiBuilder ok; HoleBossGeom="
                 + (b.Geometry.HoleBossGeom == null ? "(null)" : "有")));
        }
        finally { b.Destroy(); }

        // 2) 铣 Builder 面级读链（PartGeometry/CutAreaGeometry → sets → items → Face）
        try
        {
            CavityMillingBuilder mb = cam.CAMOperationCollection.CreateCavityMillingBuilder(op);
            try
            {
                ProbeGeometry("PartGeometry", mb.PartGeometry, "腔铣部件几何");
                ProbeGeometry("CutAreaGeometry", mb.CutAreaGeometry, "切削区域几何");
            }
            finally { mb.Destroy(); }
        }
        catch (Exception e) { _lines.Add("  CreateCavityMillingBuilder 失败(可能非腔铣): " + e.Message); }

        // 3) 区域级（CutRegionsData）
        R("CutRegionsData", () =>
        {
            CutRegionsData cr = op.CutRegionsData;
            if (cr == null) return "(null)";
            Point3d[] c = cr.GetCentroidPoints();
            double[] a = cr.GetAreas();
            string s = "regions=" + cr.NumberRegions;
            for (int i = 0; i < Math.Min(3, a.Length); i++)
                s += string.Format("  r{0}: area={1:0.###} centroid=({2:0.###},{3:0.###},{4:0.###})",
                    i, a[i], c[i].X, c[i].Y, c[i].Z);
            return s;
        });
    }

    // 面级读链：CAM.Geometry → GeometryList → sets → GetItems → Face 计数 + UF 属性取样
    private static void ProbeGeometry(string label, Geometry geo, string desc)
    {
        Note("-- " + label + " (" + desc + ") geo=" + (geo == null ? "(null)" : "有"));
        if (geo == null) return;
        R("  GeometryList", () =>
        {
            GeometrySetList gl = geo.GeometryList;
            int sets = gl.Length;
            int faceCount = 0, otherCount = 0;
            var faceTypes = new HashSet<string>();
            var sample = new List<string>();
            GeometrySet[] gs = gl.GetContents();
            for (int i = 0; i < gs.Length && i < 50; i++)
            {
                TaggedObject[] items = gs[i].GetItems();
                for (int j = 0; j < items.Length; j++)
                {
                    Face f = items[j] as Face;
                    if (f != null)
                    {
                        faceCount++;
                        if (sample.Count < 3) sample.Add(DescribeFace(f));
                    }
                    else otherCount++;
                }
            }
            return "sets=" + sets + " faces=" + faceCount + " others=" + otherCount
                   + (sample.Count > 0 ? " | 取样: " + string.Join(" ; ", sample) : "");
        });
    }

    // Face → UF AskFaceData 的 类型/法向/代表点（面积通道暂缺，见文件头）
    private static string DescribeFace(Face f)
    {
        try
        {
            UFSession uf = UFSession.GetUFSession();
            int type;
            double[] pt = new double[3], dir = new double[3], box = new double[6];
            double radius = 0, radData = 0;
            int normDir;
            uf.Modl.AskFaceData(f.Tag, out type, pt, dir, box, out radius, out radData, out normDir);
            return string.Format("face type={0} pt=({1:0.###},{2:0.###},{3:0.###}) dir=({4:0.###},{5:0.###},{6:0.###})",
                type, pt[0], pt[1], pt[2], dir[0], dir[1], dir[2]);
        }
        catch (Exception e) { return "(AskFaceData 异常: " + e.Message + ")"; }
    }

    private static Operation FindOp(NCGroup group, string name)
    {
        try
        {
            foreach (CAMObject m in group.GetMembers())
            {
                if (m is Operation && m.Name == name) return (Operation)m;
                if (m is NCGroup)
                {
                    Operation hit = FindOp((NCGroup)m, name);
                    if (hit != null) return hit;
                }
            }
        }
        catch (Exception e) { _lines.Add("  FindOp(" + name + ") 异常: " + e.Message); }
        return null;
    }

    private static void Note(string s) { _lines.Add("  " + s); }

    private static void R(string label, Func<string> f)
    {
        try { _lines.Add("  " + label + " = " + f()); }
        catch (Exception e) { _lines.Add("  " + label + " 异常: " + e.Message); }
    }

    private static void Finish(string outPath)
    {
        try { File.WriteAllLines(outPath, _lines.ToArray()); }
        catch (Exception ex)
        {
            string fb = Path.Combine(Path.GetTempPath(), "camprobe-geom-fallback.txt");
            try { File.WriteAllLines(fb, _lines.ToArray()); }
            catch { }
            _lines.Add("!! 输出写失败: " + ex.Message + "  fallback=" + fb);
        }
    }
}
