using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace ADDIN.Commands.BendTools
{
    internal sealed class ToolDxfDocument
    {
        internal ToolProfileProvenance Source;
        internal readonly List<ToolDxfEntity> Entities=new List<ToolDxfEntity>();
    }

    // A deliberately bounded analytic reader, independent of the DXF-check
    // command's display/tessellation/repair code. Autodesk DXF OCS/INSERT rules
    // are applied; unsupported geometry is reported, never approximated.
    internal static class ToolDxfReader
    {
        private sealed class Tag { internal int Code; internal string Value; }
        private sealed class Record
        {
            internal string Type, Section;
            internal int Index;
            internal readonly List<Tag> Tags=new List<Tag>();
            internal readonly List<Record> Vertices=new List<Record>();
            internal bool SequenceEnded;
            internal string Text(int code, string fallback="") => Tags.FirstOrDefault(t=>t.Code==code)?.Value ?? fallback;
            internal double Number(int code, double fallback=0)
            {
                string text=Text(code,null); if (text==null) return fallback;
                double value;
                if (!double.TryParse(text,NumberStyles.Float,CultureInfo.InvariantCulture,out value) || !ToolPoint.Finite(value))
                    throw new InvalidDataException("Invalid number in group "+code+" at record "+Index+".");
                return value;
            }
            internal int Integer(int code, int fallback=0)
            {
                double value=Number(code,fallback);
                if (value!=Math.Truncate(value) || value<int.MinValue || value>int.MaxValue) throw new InvalidDataException("Invalid integer in group "+code+".");
                return (int)value;
            }
            internal ToolPoint Point(int code=10)
            {
                if (Text(code,null)==null || Text(code+10,null)==null) throw new InvalidDataException("Missing required point coordinates.");
                return new ToolPoint(Number(code),Number(code+10));
            }
        }
        private sealed class Block { internal Record Header; internal readonly List<Record> Entities=new List<Record>(); }

        internal static ToolDxfDocument Read(byte[] bytes, ToolProfileProvenance source, CancellationToken cancellation)
        {
            var doc=new ToolDxfDocument { Source=source };
            try
            {
                if (bytes.Length>32*1024*1024) throw new InvalidDataException("DXF exceeds 32 MB.");
                string raw=Encoding.GetEncoding(28591).GetString(bytes);
                if (bytes.Length>=3 && bytes[0]==239 && bytes[1]==187 && bytes[2]==191) raw=raw.Substring(3);
                if (raw.StartsWith("AutoCAD Binary DXF",StringComparison.Ordinal)) throw new InvalidDataException("Binary DXF is unsupported.");
                var tags=Pairs(raw,cancellation);
                source.DxfVersion=Header(tags,"$ACADVER",1) ?? "AC1009";
                int version; bool unicode=int.TryParse(source.DxfVersion.Substring(2),out version) && version>=1021;
                string codePage=Header(tags,"$DWGCODEPAGE",3) ?? "ANSI_1252";
                int page; if (!int.TryParse(new string(codePage.Where(char.IsDigit).ToArray()),out page)) page=1252;
                Encoding encoding=unicode ? new UTF8Encoding(false,true) : Encoding.GetEncoding(page,EncoderFallback.ExceptionFallback,DecoderFallback.ExceptionFallback);
                tags=Pairs(encoding.GetString(bytes).TrimStart('\uFEFF'),cancellation);
                string units=Header(tags,"$INSUNITS",70); int unit;
                if (units!=null && int.TryParse(units,NumberStyles.Integer,CultureInfo.InvariantCulture,out unit)) source.InsUnits=unit;
                source.UnitToMm=UnitScale(source.InsUnits);
                source.InsBaseX=HeaderNumber(tags,"$INSBASE",10); source.InsBaseY=HeaderNumber(tags,"$INSBASE",20); source.InsBaseZ=HeaderNumber(tags,"$INSBASE",30);
                if (!source.UnitToMm.HasValue) throw new InvalidDataException("Missing/unsupported INSUNITS; no millimetre scale assumed.");
                var records=Records(tags);
                var model=new List<Record>(); var blocks=new Dictionary<string,Block>(StringComparer.OrdinalIgnoreCase);
                Block block=null; Record poly=null;
                foreach (var record in records)
                {
                    if (record.Section!="ENTITIES" && record.Section!="BLOCKS") continue;
                    if (record.Type=="SECTION" || record.Type=="ENDSEC") continue;
                    if (record.Type=="BLOCK")
                    {
                        string name=record.Text(2); if (blocks.ContainsKey(name)) throw new InvalidDataException("Duplicate block definition.");
                        block=new Block { Header=record }; blocks.Add(name,block); poly=null; continue;
                    }
                    if (record.Type=="ENDBLK") { if (poly!=null) throw new InvalidDataException("POLYLINE lacks SEQEND."); block=null; continue; }
                    if (record.Type=="VERTEX" && poly!=null) { poly.Vertices.Add(record); continue; }
                    if (record.Type=="SEQEND") { if (poly!=null) poly.SequenceEnded=true; poly=null; continue; }
                    if (poly!=null) throw new InvalidDataException("POLYLINE lacks SEQEND.");
                    if (record.Section=="ENTITIES") model.Add(record); else if (block!=null) block.Entities.Add(record);
                    if (record.Type=="POLYLINE") poly=record;
                }
                if (poly!=null) throw new InvalidDataException("POLYLINE lacks SEQEND.");
                double scale=source.UnitToMm.Value;
                Expand(model,blocks,new ToolTransform(scale,0,0,scale),"",new List<string>(),"",new HashSet<string>(StringComparer.OrdinalIgnoreCase),doc,cancellation);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) when (ex is InvalidDataException || ex is ArgumentException || ex is DecoderFallbackException || ex is OverflowException)
            { source.Error=ex.Message; doc.Entities.Clear(); }
            return doc;
        }

        private static double? UnitScale(int? code)
        {
            switch (code)
            {
                case 1:return 25.4; case 2:return 304.8; case 4:return 1; case 5:return 10; case 6:return 1000;
                case 9:return .0254; case 10:return 914.4; case 13:return .001; case 14:return 100;
                default:return null;
            }
        }
        private static List<Tag> Pairs(string text, CancellationToken cancellation)
        {
            var tags=new List<Tag>();
            using (var reader=new StringReader(text.TrimStart('\uFEFF')))
            {
                string line;
                while ((line=reader.ReadLine())!=null)
                {
                    cancellation.ThrowIfCancellationRequested(); string value=reader.ReadLine(); int code;
                    if (value==null || !int.TryParse(line.Trim(),NumberStyles.Integer,CultureInfo.InvariantCulture,out code) || code<0 || code>1071)
                        throw new InvalidDataException("Invalid/truncated DXF group pair.");
                    tags.Add(new Tag { Code=code,Value=value.Trim(' ','\t') });
                    if (tags.Count>2000000) throw new InvalidDataException("DXF has too many group pairs.");
                }
            }
            return tags;
        }
        private static string Header(List<Tag> tags, string name, int code)
        {
            bool header=false, variable=false;
            for (int i=0;i<tags.Count;i++)
            {
                var t=tags[i];
                if (t.Code==0 && t.Value=="SECTION") header=i+1<tags.Count && tags[i+1].Value=="HEADER";
                if (t.Code==0 && t.Value=="ENDSEC") header=false;
                if (!header) continue;
                if (t.Code==9) variable=t.Value==name;
                else if (variable && t.Code==code) return t.Value;
            }
            return null;
        }
        private static double HeaderNumber(List<Tag> tags, string name, int code)
        {
            string raw=Header(tags,name,code); if (raw==null) return 0;
            double n; if (!double.TryParse(raw,NumberStyles.Float,CultureInfo.InvariantCulture,out n) || !ToolPoint.Finite(n)) throw new InvalidDataException("Invalid "+name+".");
            return n;
        }
        private static List<Record> Records(List<Tag> tags)
        {
            var records=new List<Record>(); Record current=null; string section=null; bool eof=false, entities=false;
            for (int i=0;i<tags.Count;i++)
            {
                var tag=tags[i];
                if (tag.Code==0)
                {
                    if (tag.Value=="SECTION")
                    {
                        if (i+1>=tags.Count || tags[i+1].Code!=2) throw new InvalidDataException("Invalid DXF section.");
                        section=tags[i+1].Value; if (section=="ENTITIES") entities=true;
                    }
                    current=new Record { Type=tag.Value, Section=section, Index=records.Count }; records.Add(current);
                    if (tag.Value=="ENDSEC") section=null;
                    if (tag.Value=="EOF") eof=true;
                }
                else current?.Tags.Add(tag);
            }
            if (!eof || !entities) throw new InvalidDataException("DXF lacks ENTITIES/EOF.");
            return records;
        }
        private static ToolTransform Ocs(Record record)
        {
            double x=record.Number(210), y=record.Number(220), z=record.Number(230,1);
            if (x!=0 || y!=0 || z==0) throw new InvalidDataException("Tilted/invalid OCS is unsupported by the XY reader; no projection made.");
            // Arbitrary axis algorithm for normals parallel to WCS Z:
            // Ax=Wy x N -> -X for -Z, Ay=N x Ax -> +Y.
            return new ToolTransform(Math.Sign(z),0,0,1);
        }
        private static void Zero(Record record, params int[] codes)
        {
            if (codes.Any(code=>record.Number(code)!=0)) throw new InvalidDataException("Nonzero Z/elevation/thickness is unsupported; no flattening made.");
        }
        private static ToolDxfEntity Entity(Record record, ToolTransform matrix, string inheritedLayer, List<string> names, string path)
        {
            string sourceLayer=record.Text(8,"0");
            bool annotation=new[] {"TEXT","MTEXT","DIMENSION","ATTRIB","ATTDEF","LEADER","MLEADER","VIEWPORT","POINT"}.Contains(record.Type);
            return new ToolDxfEntity { Type=record.Type,Handle=record.Text(5),SourceRecord=record.Index,SourceLayer=sourceLayer,
                Layer=sourceLayer=="0" && inheritedLayer.Length>0 ? inheritedLayer : sourceLayer,
                InstancePath=path,Blocks=names.ToArray(),Transform=matrix,Annotation=annotation,
                PaperSpace=record.Integer(67)==1 || !string.Equals(record.Text(410,"Model"),"Model",StringComparison.OrdinalIgnoreCase),
                InsertHandles=path.Split('/').Where(p=>p.Contains(":")) .Select(p=>p.Split(':')[1]).ToArray(),
                Curves=Array.AsReadOnly(new ToolCurve[0]),SourceVertices=Array.AsReadOnly(new ToolPoint[0]),SourceBulges=Array.AsReadOnly(new double[0]) };
        }

        private static void Expand(IList<Record> records, Dictionary<string,Block> blocks, ToolTransform matrix, string layer,
            List<string> names, string path, HashSet<string> visiting, ToolDxfDocument doc, CancellationToken cancellation)
        {
            foreach (var record in records)
            {
                cancellation.ThrowIfCancellationRequested();
                if (doc.Entities.Count>=200000) throw new InvalidDataException("Expanded DXF exceeds entity limit.");
                var entity=Entity(record,matrix,layer,names,path);
                try
                {
                    if (record.Type=="INSERT" && !entity.PaperSpace)
                    {
                        string name=record.Text(2); var childNames=names.Concat(new[] {name}).ToList(); entity.Blocks=childNames.ToArray();
                        entity.InsertHandles=entity.InsertHandles.Concat(new[] {record.Text(5)}).ToArray();
                        Block block;
                        if (names.Count>=32 || visiting.Contains(name)) throw new InvalidDataException("Cyclic/deep INSERT.");
                        if (!blocks.TryGetValue(name,out block)) throw new InvalidDataException("Missing INSERT block: "+name);
                        if ((block.Header.Integer(70)&(4|8|16))!=0) throw new InvalidDataException("External reference block is not resolved as local geometry.");
                        Zero(record,30); Zero(block.Header,30);
                        int columns=record.Integer(70,1), rows=record.Integer(71,1);
                        if (columns<=0 || rows<=0 || (long)columns*rows>10000) throw new InvalidDataException("Unsupported INSERT array size.");
                        double sx=record.Number(41,1), sy=record.Number(42,1), sz=record.Number(43,1);
                        if (sx==0 || sy==0 || sz==0) throw new InvalidDataException("Zero INSERT scale.");
                        double a=record.Number(50)*Math.PI/180, cos=Math.Cos(a), sin=Math.Sin(a);
                        var basePoint=block.Header.Point(); var insertion=record.Point(); var ocs=Ocs(record);
                        visiting.Add(name);
                        try
                        {
                            for (int row=0;row<rows;row++) for (int col=0;col<columns;col++)
                            {
                                var local=ocs.Then(new ToolTransform(1,0,0,1,insertion.X,insertion.Y))
                                    .Then(new ToolTransform(cos,sin,-sin,cos))
                                    .Then(new ToolTransform(1,0,0,1,col*record.Number(44),row*record.Number(45)))
                                    .Then(new ToolTransform(sx,0,0,sy))
                                    .Then(new ToolTransform(1,0,0,1,-basePoint.X,-basePoint.Y));
                                Expand(block.Entities,blocks,matrix.Then(local),entity.Layer,childNames,
                                    path+"/"+record.Index+":"+record.Text(5)+":"+name+"["+col+","+row+"]",visiting,doc,cancellation);
                            }
                        }
                        finally { visiting.Remove(name); }
                        continue;
                    }
                    if (!entity.Annotation && !entity.PaperSpace) Geometry(record,entity,matrix);
                }
                catch (Exception ex) when (ex is InvalidDataException || ex is OverflowException || ex is ArgumentException)
                { entity.UnsupportedReason=ex.Message; entity.Curves=Array.AsReadOnly(new ToolCurve[0]); }
                doc.Entities.Add(entity);
            }
        }
        private static void Geometry(Record r, ToolDxfEntity entity, ToolTransform parent)
        {
            var curves=new List<ToolCurve>(); var points=new List<ToolPoint>(); var bulges=new List<double>();
            ToolTransform matrix=parent;
            if (r.Type=="LINE")
            {
                Zero(r,30,31,39); curves.Add(ToolCurve.Line(r.Point(),r.Point(11),parent));
            }
            else if (r.Type=="ARC" || r.Type=="CIRCLE")
            {
                Zero(r,30,39); matrix=parent.Then(Ocs(r));
                if (r.Text(40,null)==null || r.Type=="ARC" && (r.Text(50,null)==null || r.Text(51,null)==null)) throw new InvalidDataException("Missing required arc radius/angles.");
                var center=r.Point(); double radius=r.Number(40), start=r.Number(50)*Math.PI/180;
                double sweep=r.Type=="CIRCLE" ? 2*Math.PI : (r.Number(51)-r.Number(50))*Math.PI/180;
                if (r.Type=="ARC") { sweep%=2*Math.PI; if (sweep<=0) sweep+=2*Math.PI; if (Math.Abs(sweep-2*Math.PI)<1e-12) throw new InvalidDataException("ARC has ambiguous equal start/end angles."); }
                var a=new ToolPoint(center.X+radius*Math.Cos(start),center.Y+radius*Math.Sin(start));
                var b=new ToolPoint(center.X+radius*Math.Cos(start+sweep),center.Y+radius*Math.Sin(start+sweep));
                curves.Add(ToolCurve.Arc(a,b,center,radius,sweep,matrix)); if (r.Type=="CIRCLE") entity.SourceClosed=true;
            }
            else if (r.Type=="LWPOLYLINE" || r.Type=="POLYLINE")
            {
                Zero(r,30,38,39,40,41,43); matrix=parent.Then(Ocs(r));
                int flags=r.Integer(70); entity.SourceClosed=(flags&1)!=0;
                if ((flags&~129)!=0) throw new InvalidDataException("3D/mesh/fitted polyline is unsupported.");
                if (r.Type=="LWPOLYLINE")
                {
                    List<Tag> vertex=null; var vertices=new List<List<Tag>>();
                    foreach (var tag in r.Tags)
                    {
                        if (tag.Code==10) { vertex=new List<Tag>(); vertices.Add(vertex); }
                        if (vertex!=null && new[] {10,20,40,41,42}.Contains(tag.Code)) vertex.Add(tag);
                    }
                    if (vertices.Count!=r.Integer(90,-1)) throw new InvalidDataException("LWPOLYLINE vertex count mismatch.");
                    foreach (var tags in vertices)
                    {
                        var v=new Record { Index=r.Index }; v.Tags.AddRange(tags);
                        if (v.Text(20,null)==null) throw new InvalidDataException("Missing polyline vertex Y.");
                        Zero(v,40,41); points.Add(v.Point()); bulges.Add(v.Number(42));
                    }
                }
                else
                {
                    if (!r.SequenceEnded) throw new InvalidDataException("POLYLINE lacks SEQEND.");
                    foreach (var v in r.Vertices)
                    {
                        Zero(v,30,40,41); if (v.Integer(70)!=0) throw new InvalidDataException("Fitted/3D VERTEX is unsupported.");
                        points.Add(v.Point()); bulges.Add(v.Number(42));
                    }
                }
                if (points.Count<2) throw new InvalidDataException("Polyline has fewer than two vertices.");
                int count=entity.SourceClosed==true ? points.Count : points.Count-1;
                for (int i=0;i<count;i++) curves.Add(ToolCurve.Bulge(points[i],points[(i+1)%points.Count],bulges[i],matrix,i));
            }
            else throw new InvalidDataException("Unsupported DXF entity: "+r.Type);
            entity.Transform=matrix; entity.SourceVertices=points.AsReadOnly(); entity.SourceBulges=bulges.AsReadOnly(); entity.Curves=curves.AsReadOnly();
        }
    }
}
