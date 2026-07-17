using System.Text;

var path = args.Length > 0
    ? args[0]
    : @"C:\SGN26\addin\ADDIN\Commands\LenhMakeHole.cs";

var lines = File.ReadAllLines(path, Encoding.UTF8).ToList();

for (var i = 0; i < lines.Count; i++)
{
    lines[i] = lines[i]
        .Replace("fillFeature.Name = \"Repair Hole Fill Surface \" + candidate.Index;", "fillFeature.Name = \"RH-F\" + candidate.Index;")
        .Replace("feature.Name = \"Repair Hole Center Point \" + index;", "feature.Name = \"RH-P\" + index;")
        .Replace("feature.Name = \"Repair Hole Delete Fill Surface Body\";", "feature.Name = \"RH-DelSurf\";")
        .Replace("feature.Name = \"Repair Hole\";", "feature.Name = \"RH-Cut\";")
        .Replace("sketchFeature.Name = \"Repair Hole Centers\";", "sketchFeature.Name = \"RH-Pts\";");
}

var start = -1;
for (var i = 0; i + 1 < lines.Count; i++)
{
    if (lines[i] == "\t\tFacePlaneFrame planeFrame = CreateFacePlaneFrame(face);"
        && lines[i + 1] == "\t\tforeach (double[] center in centers)")
    {
        start = i;
        break;
    }
}
if (start >= 0)
{
    var end = -1;
    for (var i = start; i < lines.Count; i++)
    {
        if (lines[i] == "\t\tDebug.WriteLine(\"[REPAIR HOLE] sketch circles created=\" + circleCount + \"/\" + centers.Count);")
        {
            end = i;
            break;
        }
    }
    if (end < 0)
    {
        throw new InvalidOperationException("Repair loop end not found.");
    }

    var newLoop = new[]
    {
    "\t\tFacePlaneFrame planeFrame = CreateFacePlaneFrame(face);",
    "\t\tdouble[] safeBase = GetRepairSafeSketchCircleBase(face, planeFrame, centers);",
    "\t\tforeach (double[] center in centers)",
    "\t\t{",
    "\t\t\tnum++;",
    "\t\t\tdouble[] target = ProjectRepairPointToSketchPlane(planeFrame, center);",
    "\t\t\tif (!IsPoint(target))",
    "\t\t\t{",
    "\t\t\t\tDebug.WriteLine(\"[REPAIR HOLE] skip center \" + num + \": cannot project to sketch plane.\");",
    "\t\t\t\tcontinue;",
    "\t\t\t}",
    "\t\t\tdouble[] safeCenter = GetRepairSafeSketchCircleCenter(planeFrame, safeBase, diameterM, num);",
    "\t\t\tSketchSegment sketchSegment = CreateRepairSketchCircle(model, planeFrame, safeCenter, diameterM);",
    "\t\t\tif (sketchSegment == null)",
    "\t\t\t{",
    "\t\t\t\tsketchSegment = model.SketchManager.CreateCircleByRadius(target[0], target[1], target[2], diameterM / 2.0);",
    "\t\t\t}",
    "\t\t\tif (sketchSegment != null)",
    "\t\t\t{",
    "\t\t\t\tcircleCount++;",
    "\t\t\t\tlist.Add(sketchSegment);",
    "\t\t\t\tFeature pointFeature = ((repairPointFeatures != null && num - 1 < repairPointFeatures.Count) ? repairPointFeatures[num - 1] : null);",
    "\t\t\t\tif (!TryConstrainRepairCircleToReferencePoint(model, sketchSegment, pointFeature, num))",
    "\t\t\t\t{",
    "\t\t\t\t\tDebug.WriteLine(\"[REPAIR HOLE] center \" + num + \": created circle, but point relation failed.\");",
    "\t\t\t\t}",
    "\t\t\t\tcontinue;",
    "\t\t\t}",
    "\t\t\tDebug.WriteLine(\"[REPAIR HOLE] skip center \" + num + \": CreateCircle failed at target (\" + (target[0] * 1000.0).ToString(\"0.###\", CultureInfo.InvariantCulture) + \",\" + (target[1] * 1000.0).ToString(\"0.###\", CultureInfo.InvariantCulture) + \",\" + (target[2] * 1000.0).ToString(\"0.###\", CultureInfo.InvariantCulture) + \")\");",
    "\t\t}",
    "\t\tDebug.WriteLine(\"[REPAIR HOLE] sketch circles created=\" + circleCount + \"/\" + centers.Count);"
    };

    lines.RemoveRange(start, end - start + 1);
    lines.InsertRange(start, newLoop);
}

for (var i = 0; i + 3 < lines.Count; i++)
{
    if (lines[i] == "\t\t\t\tif (!TryConstrainRepairCircleToReferencePoint(model, sketchSegment, pointFeature, num))"
        && lines[i + 1] == "\t\t\t\t{")
    {
        lines.RemoveRange(i, 4);
        lines.Insert(i, "\t\t\t\tTryConstrainRepairCircleToReferencePoint(model, sketchSegment, pointFeature, num);");
        break;
    }
}

var marker = lines.FindIndex(line =>
    line == "\tprivate Feature CreateRepairHoleCut(ModelDoc2 model, Face2 face, double[] center, double diameterM, double depthM)");
if (marker < 0)
{
    throw new InvalidOperationException("CreateRepairHoleCut marker not found.");
}

var hasHelpers = lines.Any(line => line == "\tprivate double[] GetRepairSafeSketchCircleBase(Face2 face, FacePlaneFrame planeFrame, List<double[]> centers)");
if (!hasHelpers)
{
    var helpers = new[]
    {
        "\tprivate double[] GetRepairSafeSketchCircleBase(Face2 face, FacePlaneFrame planeFrame, List<double[]> centers)",
        "\t{",
        "\t\tdouble[] center;",
        "\t\tif (face != null && planeFrame != null && TryGetFaceBoxCenter(face, planeFrame, out center) && IsPoint(center))",
        "\t\t{",
        "\t\t\treturn planeFrame.ProjectToPlane(center);",
        "\t\t}",
        "\t\tif (centers != null)",
        "\t\t{",
        "\t\t\tforeach (double[] item in centers)",
        "\t\t\t{",
        "\t\t\t\tif (IsPoint(item))",
        "\t\t\t\t{",
        "\t\t\t\t\treturn ProjectRepairPointToSketchPlane(planeFrame, item);",
        "\t\t\t\t}",
        "\t\t\t}",
        "\t\t}",
        "\t\tif (planeFrame != null && IsPoint(planeFrame.Origin))",
        "\t\t{",
        "\t\t\treturn planeFrame.Origin;",
        "\t\t}",
        "\t\treturn new double[3];",
        "\t}",
        "",
        "\tprivate double[] GetRepairSafeSketchCircleCenter(FacePlaneFrame planeFrame, double[] basePoint, double diameterM, int index)",
        "\t{",
        "\t\tif (!IsPoint(basePoint))",
        "\t\t{",
        "\t\t\treturn null;",
        "\t\t}",
        "\t\tdouble spacing = Math.Max(diameterM * 2.5, 0.005);",
        "\t\tint column = Math.Max(0, index - 1) % 5;",
        "\t\tint row = Math.Max(0, index - 1) / 5;",
        "\t\tdouble[] axisU = (planeFrame != null && IsPoint(planeFrame.AxisU)) ? planeFrame.AxisU : new double[3] { 1.0, 0.0, 0.0 };",
        "\t\tdouble[] axisV = (planeFrame != null && IsPoint(planeFrame.AxisV)) ? planeFrame.AxisV : new double[3] { 0.0, 1.0, 0.0 };",
        "\t\treturn Add(Add(basePoint, Scale(axisU, spacing * column)), Scale(axisV, spacing * row));",
        "\t}",
        "",
        "\tprivate SketchSegment CreateRepairSketchCircle(ModelDoc2 model, FacePlaneFrame planeFrame, double[] center, double diameterM)",
        "\t{",
        "\t\tif (model == null || !IsPoint(center) || diameterM <= 1E-06)",
        "\t\t{",
        "\t\t\treturn null;",
        "\t\t}",
        "\t\tdouble radius = diameterM / 2.0;",
        "\t\tdouble[] axisU = (planeFrame != null && IsPoint(planeFrame.AxisU)) ? planeFrame.AxisU : new double[3] { 1.0, 0.0, 0.0 };",
        "\t\tdouble[] edge = Add(center, Scale(axisU, radius));",
        "\t\ttry",
        "\t\t{",
        "\t\t\tSketchSegment sketchSegment = model.SketchManager.CreateCircle(center[0], center[1], center[2], edge[0], edge[1], edge[2]) as SketchSegment;",
        "\t\t\tif (sketchSegment != null)",
        "\t\t\t{",
        "\t\t\t\treturn sketchSegment;",
        "\t\t\t}",
        "\t\t}",
        "\t\tcatch",
        "\t\t{",
        "\t\t}",
        "\t\ttry",
        "\t\t{",
        "\t\t\treturn model.SketchManager.CreateCircleByRadius(center[0], center[1], center[2], radius);",
        "\t\t}",
        "\t\tcatch",
        "\t\t{",
        "\t\t\treturn null;",
        "\t\t}",
        "\t}",
        ""
    };
    lines.InsertRange(marker, helpers);
}

File.WriteAllLines(path, lines, Encoding.UTF8);
Console.WriteLine("patched");
