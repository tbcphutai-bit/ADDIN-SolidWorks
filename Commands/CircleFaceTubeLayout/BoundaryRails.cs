using System;
using System.Collections.Generic;
using System.Linq;

namespace ADDIN.Commands.CircleFaceTubeLayout
{
    internal sealed class BoundaryPiece
    {
        public readonly double Length;
        public readonly Point3 Start, End, StartTangent, EndTangent;
        public readonly Func<double, Point3> At;

        public BoundaryPiece(double length, Point3 start, Point3 end, Point3 startTangent,
            Point3 endTangent, Func<double, Point3> at)
        {
            if (!(length > 1e-7) || double.IsInfinity(length))
                throw new LayoutException("The exterior loop has a degenerate edge.");
            Length = length;
            Start = start;
            End = end;
            StartTangent = startTangent.Unit();
            EndTangent = endTangent.Unit();
            At = at;
        }
    }

    internal sealed class BoundaryRail
    {
        private readonly List<BoundaryPiece> pieces;
        public readonly double Length;

        public BoundaryRail(List<BoundaryPiece> pieces)
        {
            this.pieces = pieces;
            Length = pieces.Sum(p => p.Length);
        }

        public Point3 Start => pieces[0].Start;
        public Point3 End => pieces[pieces.Count - 1].End;
        public int PieceCount => pieces.Count;

        public Point3 At(double station)
        {
            if (station <= 0) return Start;
            if (station >= Length) return End;
            foreach (BoundaryPiece piece in pieces)
            {
                if (station <= piece.Length) return piece.At(station);
                station -= piece.Length;
            }
            return End;
        }

        public double Bow()
        {
            var chord = End - Start;
            var direction = chord.Unit();
            double bow = 0;
            for (int i = 1; i < 8; i++)
                bow = Math.Max(bow, (At(Length * i / 8) - Start).Cross(direction).Length);
            return bow / Length;
        }
    }

    internal sealed class BoundaryRails
    {
        public readonly BoundaryRail Small, Large;
        public BoundaryRails(BoundaryRail small, BoundaryRail large) { Small = small; Large = large; }

        // A four-sided patch can contain any number of smooth coedge splits.
        // The two linking sides, not proximity, determine the end orientation.
        public static BoundaryRails Find(IList<BoundaryPiece> loop, Action<string> log)
        {
            var corners = new List<int>();
            const double cosCorner = 0.9396926208; // 20 degrees
            const double joinTolerance = 0.00002;
            for (int i = 0; i < loop.Count; i++)
            {
                BoundaryPiece previous = loop[(i + loop.Count - 1) % loop.Count], next = loop[i];
                if ((previous.End - next.Start).Length > joinTolerance)
                    throw new LayoutException("Exterior coedges do not form a connected loop.");
                double dot = previous.EndTangent.Dot(next.StartTangent);
                log(TubeLayoutSolver.F("JOIN coedge={0} tangent_dot={1:F7}", i + 1, dot));
                if (dot < cosCorner) corners.Add(i);
            }
            if (corners.Count != 4)
                throw new LayoutException("INFEASIBLE: expected four identifiable corners in the outer loop; found " + corners.Count + ".");

            var chains = new BoundaryRail[4];
            var bows = new double[4];
            for (int i = 0; i < 4; i++)
            {
                var pieces = new List<BoundaryPiece>();
                int j = corners[i], end = corners[(i + 1) % 4];
                do
                {
                    pieces.Add(loop[j]);
                    j = (j + 1) % loop.Count;
                } while (j != end);
                chains[i] = new BoundaryRail(pieces);
                bows[i] = chains[i].Bow();
                log(TubeLayoutSolver.F("CHAIN index={0} pieces={1} length_mm={2:F6} bow_ratio={3:F6} start_m={4} end_m={5}",
                    i + 1, chains[i].PieceCount, chains[i].Length / TubeLayoutSolver.Mm, bows[i], chains[i].Start, chains[i].End));
            }

            double Score(int a, int b)
            {
                double taper = Math.Abs(chains[a].Length - chains[b].Length) / Math.Max(chains[a].Length, chains[b].Length);
                return Math.Min(bows[a], bows[b]) + taper * 0.05;
            }
            double scoreA = Score(0, 2), scoreB = Score(1, 3);
            log(TubeLayoutSolver.F("END_SCORES opposite_1_3={0:F7} opposite_2_4={1:F7}", scoreA, scoreB));
            if (Math.Abs(scoreA - scoreB) < 0.003)
                throw new LayoutException("INFEASIBLE: cannot reliably distinguish the end rails from the linking sides.");

            int index = scoreA > scoreB ? 0 : 1;
            BoundaryRail aRail = chains[index], bRail = chains[index + 2];
            BoundaryRail small = aRail.Length < bRail.Length ? aRail : bRail;
            BoundaryRail large = ReferenceEquals(small, aRail) ? bRail : aRail;
            if (large.Length - small.Length < 0.005 * large.Length)
                throw new LayoutException("INFEASIBLE: opposite end lengths are too similar to identify small and large ends.");

            // The opposite chain traverses the loop in reverse. Check that this
            // pairing also makes geometric sense on the patch, including twist.
            var spanA = large.End - small.Start;
            var spanB = large.Start - small.End;
            double paired = spanA.Length + spanB.Length;
            double crossed = (large.Start - small.Start).Length + (large.End - small.End).Length;
            log(TubeLayoutSolver.F("PAIRING paired_mm={0:F6} crossed_mm={1:F6} large_reversed=true",
                paired / TubeLayoutSolver.Mm, crossed / TubeLayoutSolver.Mm));
            if (spanA.Length < joinTolerance || spanB.Length < joinTolerance || paired > crossed * 1.02)
                throw new LayoutException("INFEASIBLE: the topological end pairing is geometrically ambiguous.");
            return new BoundaryRails(small, large);
        }
    }
}
