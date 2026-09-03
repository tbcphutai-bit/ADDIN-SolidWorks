using System;
using System.Collections.Generic;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace ADDIN.Helpers
{
    public class TopologyEdgeMatcher
    {
        private readonly ISldWorks swApp;
        private readonly ModelDoc2 partDoc;

        public TopologyEdgeMatcher(ISldWorks app, ModelDoc2 doc)
        {
            swApp = app;
            partDoc = doc;
        }

        public struct EdgeSignature
        {
            public double[] Midpoint;
            public double[] Direction;
            public double Length;
            public Edge OriginalEdge;
        }

        /// <summary>
        /// Trích xuất chữ ký hình học toán học của một Cạnh 3D
        /// </summary>
        public EdgeSignature ExtractSignature(Edge edge)
        {
            EdgeSignature sig = new EdgeSignature { OriginalEdge = edge };
            try
            {
                Curve curve = edge.GetCurve() as Curve;
                if (curve != null)
                {
                    Vertex startVertex = edge.GetStartVertex() as Vertex;
                    Vertex endVertex = edge.GetEndVertex() as Vertex;
                    double[] startPoint = startVertex?.GetPoint() as double[];
                    double[] endPoint = endVertex?.GetPoint() as double[];

                    if (startPoint != null && endPoint != null)
                    {
                        sig.Midpoint = new double[] 
                        { 
                            (startPoint[0] + endPoint[0]) / 2.0, 
                            (startPoint[1] + endPoint[1]) / 2.0, 
                            (startPoint[2] + endPoint[2]) / 2.0 
                        };

                        double dx = endPoint[0] - startPoint[0];
                        double dy = endPoint[1] - startPoint[1];
                        double dz = endPoint[2] - startPoint[2];
                        sig.Length = Math.Sqrt(dx * dx + dy * dy + dz * dz);

                        if (sig.Length > 1e-9)
                        {
                            sig.Direction = new double[] { dx / sig.Length, dy / sig.Length, dz / sig.Length };
                        }
                    }
                }
            }
            catch { }
            return sig;
        }

        /// <summary>
        /// Phản chiếu chữ ký qua mặt phẳng đối xứng và tìm kiếm Cạnh khớp nhất trên Body mới
        /// </summary>
        public Edge FindMirroredEdge(Body2 targetBody, EdgeSignature sourceSig, double[] planeOrigin, double[] planeNormal)
        {
            if (targetBody == null || sourceSig.Midpoint == null || sourceSig.Direction == null) 
                return null;

            // 1. Tính toán điểm Midpoint đối xứng qua mặt phẳng
            double[] p = sourceSig.Midpoint;
            double[] o = planeOrigin;
            double[] n = planeNormal; // Giả sử đã chuẩn hóa (length = 1.0)

            double d = (o[0] - p[0]) * n[0] + (o[1] - p[1]) * n[1] + (o[2] - p[2]) * n[2];
            double[] reflectedMidpoint = new double[] 
            { 
                p[0] + 2.0 * d * n[0], 
                p[1] + 2.0 * d * n[1], 
                p[2] + 2.0 * d * n[2] 
            };

            // 2. Quét toàn bộ các cạnh trên Body mới để tìm ứng viên phù hợp nhất
            object[] targetEdgesObj = targetBody.GetEdges() as object[];
            if (targetEdgesObj == null) return null;

            Edge bestMatchEdge = null;
            double minCost = double.MaxValue;

            foreach (object obj in targetEdgesObj)
            {
                Edge candidateEdge = obj as Edge;
                if (candidateEdge == null) continue;

                EdgeSignature candidateSig = ExtractSignature(candidateEdge);
                if (candidateSig.Midpoint == null) continue;

                // Kiểm tra độ lệch chiều dài (Dung sai 5%)
                if (Math.Abs(candidateSig.Length - sourceSig.Length) > sourceSig.Length * 0.05)
                    continue;

                // Tính khoảng cách giữa 2 trung điểm sau khi phản chiếu
                double dist = Math.Sqrt(
                    Math.Pow(candidateSig.Midpoint[0] - reflectedMidpoint[0], 2) +
                    Math.Pow(candidateSig.Midpoint[1] - reflectedMidpoint[1], 2) +
                    Math.Pow(candidateSig.Midpoint[2] - reflectedMidpoint[2], 2)
                );

                // Chi phí (Cost) dựa trên khoảng cách vị trí
                double cost = dist;
                if (cost < minCost)
                {
                    minCost = cost;
                    bestMatchEdge = candidateEdge;
                }
            }

            // Dung sai không gian cho phép (Ví dụ: khoảng cách tối đa 1mm)
            return (minCost <= 0.001) ? bestMatchEdge : null;
        }
    }
}
