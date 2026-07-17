#define DEBUG
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Automation;
using System.Windows.Forms;
using SolidWorks.Interop.sldworks;

namespace ADDIN.Commands;

internal class LenhMakeHole
{
	private class FacePlaneFrame
	{
		public double[] Origin;

		public double[] Normal;

		public double[] AxisU;

		public double[] AxisV;

		public double[] ToModel(double u, double v, double w)
		{
			return new double[3]
			{
				Origin[0] + AxisU[0] * u + AxisV[0] * v + Normal[0] * w,
				Origin[1] + AxisU[1] * u + AxisV[1] * v + Normal[1] * w,
				Origin[2] + AxisU[2] * u + AxisV[2] * v + Normal[2] * w
			};
		}

		public double[] ProjectToPlane(double[] point)
		{
			if (point == null || point.Length < 3)
			{
				return point;
			}
			double num = point[0] - Origin[0];
			double num2 = point[1] - Origin[1];
			double num3 = point[2] - Origin[2];
			double num4 = num * Normal[0] + num2 * Normal[1] + num3 * Normal[2];
			return new double[3]
			{
				point[0] - Normal[0] * num4,
				point[1] - Normal[1] * num4,
				point[2] - Normal[2] * num4
			};
		}
	}

	private class RepairHoleLoopCandidate
	{
		public int Index;

		public List<Edge> Edges = new List<Edge>();

		public double[] FallbackCenter;

		public double Width;

		public double Height;
	}

	private class SelectionInfo
	{
		public Face2 Face;

		public Edge Edge;

		public Feature SeedFeature;

		public double[] SidePoint;
	}

	private class EdgeGeometry
	{
		public Curve Curve;

		public double StartParam;

		public double EndParam;

		public double[] Start;

		public double[] End;

		public double[] Mid;

		public double[] Direction;

		public double Length;
	}

	private class OffsetPath
	{
		public readonly List<double[]> Points = new List<double[]>();

		public readonly List<double[]> HolePoints = new List<double[]>();
	}

	private class HolePoint
	{
		public double X;

		public double Y;

		public double Z;
	}

	private class LooseSize
	{
		public double WidthM;

		public double LengthM;
	}

	private readonly ISldWorks swApp;

	private bool holeWizardCommandStarted;

	private double[] pendingHoleWizardSeedPoint;

	private bool pendingHybridPattern;

	private bool curvePatternCommandStarted;

	private int pendingPatternCount;

	private double pendingPatternSpacing;

	private int lastCalculatedPatternCount;

	private double lastCalculatedPatternSpacing;

	private double lastCalculatedPatternUsableLength;

	private string trackedPatternSketchName;

	private string trackedLengthVariableName;

	private string trackedPatternFeatureName;

	private string trackedPatternCountDimensionName;

	private string trackedHoleFeatureName;

	private double trackedPatternLengthMm;

	private double trackedPatternPitchMm;

	private bool trackedLengthUsesVariable;

	private const double PatternUpdateToleranceMm = 0.05;

	private string pendingPatternSketchName;

	private HashSet<string> featureNamesBeforeHoleWizard;

	private bool pendingPatternEquation;

	private string pendingPatternLengthDimensionName;

	private bool pendingPatternLengthIsExpression;

	private double pendingPatternMaxPitchMm;

	private HashSet<string> featureNamesBeforeCurvePattern;

	private System.Windows.Forms.Timer pendingPatternEquationTimer;

	private int pendingPatternEquationPollCount;

	public bool HasPendingHybridPattern => pendingHybridPattern;

	public LenhMakeHole(ISldWorks app)
	{
		swApp = app;
	}

	private bool IsLineFlowDirection(string direction)
	{
		string a = (direction ?? "").Trim();
		return string.Equals(a, "Line Flow", StringComparison.OrdinalIgnoreCase) || string.Equals(a, "Line Edge", StringComparison.OrdinalIgnoreCase) || string.Equals(a, "Straight Line", StringComparison.OrdinalIgnoreCase);
	}

	private bool IsSplineFlowDirection(string direction)
	{
		return string.Equals((direction ?? "").Trim(), "Spline Flow", StringComparison.OrdinalIgnoreCase);
	}

	public bool PatternPendingHoleWizard()
	{
		curvePatternCommandStarted = false;
		if (!(swApp?.ActiveDoc is ModelDoc2 modelDoc))
		{
			MessageBox.Show("Hay mo Part truoc.", "Make Hole", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
			return false;
		}
		if (modelDoc.GetType() != 1)
		{
			MessageBox.Show("Chi ho tro pattern Hole Wizard trong Part.", "Make Hole", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
			return false;
		}
		if (!pendingHybridPattern)
		{
			if (pendingPatternEquation)
			{
				if (TryApplyPendingCurvePatternEquation(modelDoc))
				{
					ResetPendingMakeHole();
					MessageBox.Show("Da gan cong thuc count vao Curve Pattern.", "Make Hole", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
					return true;
				}
				MessageBox.Show("Chua tim thay Curve Pattern moi de gan cong thuc. Hay OK Curve Pattern truoc, roi bam Pattern lai.", "Make Hole", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
				return false;
			}
			MessageBox.Show("Khong co Hole Wizard nao dang cho pattern.", "Make Hole", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
			return false;
		}
		if (TryRunPendingHybridPattern(modelDoc))
		{
			return true;
		}
		if (curvePatternCommandStarted)
		{
			return true;
		}
		ResetPendingMakeHole();
		MessageBox.Show("Chua pattern duoc Hole Wizard moi. Lenh Make Hole da reset, hay tao lai tu dau.", "Make Hole", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
		return false;
	}

	public void ResetPendingCommand()
	{
		ResetPendingMakeHole();
	}

	public void Run(MakeHoleOptions options)
	{
		holeWizardCommandStarted = false;
		if (options == null)
		{
			return;
		}
		if (!string.Equals(options.HoleType, "Circle", StringComparison.OrdinalIgnoreCase) && !string.Equals(options.HoleType, "Loose", StringComparison.OrdinalIgnoreCase))
		{
			MessageBox.Show("Ban dau chi ho tro Circle va Loose. Hole Wizard type se them o buoc tiep theo.", "Make Hole", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
			return;
		}
		if (!(swApp?.ActiveDoc is ModelDoc2 modelDoc))
		{
			MessageBox.Show("Hay mo Part truoc.", "Make Hole", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
			return;
		}
		if (modelDoc.GetType() != 1)
		{
			MessageBox.Show("Ban dau chi ho tro Make Hole trong Part. Assembly se lam sau.", "Make Hole", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
			return;
		}
		if (pendingHybridPattern)
		{
			MessageBox.Show("Da co lenh Pattern dang cho. Hay tao xong Hole Wizard roi bam nut Pattern.", "Make Hole", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
			return;
		}
		if (pendingHoleWizardSeedPoint != null)
		{
			if (!TryPlacePendingHoleWizardPoint(modelDoc))
			{
				ResetPendingMakeHole();
				MessageBox.Show("Hay chuyen sang tab Position cua Hole Wizard roi bam Accept lai.", "Make Hole", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
			}
			return;
		}
		SelectionInfo selection = GetSelection(modelDoc);
		if (selection.Edge == null)
		{
			MessageBox.Show("Hay chon 1 edge lam chuan. Co the chon them 1 point de chi phia offset.", "Make Hole", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
			return;
		}
		if (selection.Face == null)
		{
			selection.Face = GetFirstAdjacentFace(selection.Edge);
		}
		if (!TryGetEdgeGeometry(selection.Edge, out var geometry))
		{
			MessageBox.Show("Khong doc duoc edge da chon.", "Make Hole", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
			return;
		}
		try
		{
			bool flag = IsLineFlowDirection(options.Direction);
			bool flag2 = IsSplineFlowDirection(options.Direction);
			bool flag3 = flag || flag2;
			string text = (flag ? "Line Flow" : (flag2 ? "Spline Flow" : "Curve Flow"));
			if (!(flag3 ? CreateStraightEdgeOffsetSketch(modelDoc, selection.Face, selection.Edge, selection.SeedFeature, geometry, selection.SidePoint, options, flag, out var pointCount, out var message) : CreateOffsetOnSurfaceSketch(modelDoc, selection.Face, selection.Edge, selection.SeedFeature, geometry, selection.SidePoint, options, out pointCount, out message)))
			{
				ResetPendingMakeHole();
				MessageBox.Show(message, "Make Hole", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
				return;
			}
			modelDoc.GraphicsRedraw2();
			if (!holeWizardCommandStarted)
			{
				if (pointCount > 0)
				{
					MessageBox.Show("Da tao " + text + " va " + pointCount + " duong chia.", "Make Hole", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
				}
				else
				{
					MessageBox.Show("Da tao duong offset bang SolidWorks, nhung chua doc duoc sketch segment de chia duong. Hay xem log [MAKE HOLE] trong Output.", "Make Hole", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
				}
			}
		}
		catch (Exception ex)
		{
			ResetPendingMakeHole();
			MessageBox.Show("Loi Make Hole: " + ex.Message, "Make Hole", MessageBoxButtons.OK, MessageBoxIcon.Hand);
		}
	}

	public void RunRepairHole(MakeHoleOptions options)
	{
		if (options == null)
		{
			return;
		}
		if (!(swApp?.ActiveDoc is ModelDoc2 modelDoc))
		{
			MessageBox.Show("Hay mo Part truoc.", "Repair Hole", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
			return;
		}
		if (modelDoc.GetType() != 1)
		{
			MessageBox.Show("Repair Hole chi ho tro trong Part.", "Repair Hole", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
			return;
		}
		SelectionInfo selection = GetSelection(modelDoc);
		double num = ((options.DiameterMm > 0.0) ? (options.DiameterMm / 1000.0) : 0.0042);
		double num2 = ((options.ThicknessMm > 0.0) ? (options.ThicknessMm / 1000.0) : 0.0);
		if (num2 <= 1E-06)
		{
			MessageBox.Show("Chua co be day vat lieu (鬯ｯ・ｯ繝ｻ・ｯ郢晢ｽｻ繝ｻ・ｯ驛｢譎｢・ｽ・ｻ郢晢ｽｻ繝ｻ・ｯ鬩幢ｽ｢隴趣ｽ｢繝ｻ・ｽ繝ｻ・ｻ驛｢譎｢・ｽ・ｻ郢晢ｽｻ繝ｻ・ｮ鬯ｩ蟷｢・ｽ・｢髫ｴ雜｣・ｽ・｢郢晢ｽｻ繝ｻ・ｽ郢晢ｽｻ繝ｻ・ｻ鬩幢ｽ｢隴趣ｽ｢繝ｻ・ｽ繝ｻ・ｻ驛｢譎｢・ｽ・ｻ郢晢ｽｻ繝ｻ・ｫ鬯ｯ・ｩ陝ｷ・｢繝ｻ・ｽ繝ｻ・｢鬮ｫ・ｴ髮懶ｽ｣繝ｻ・ｽ繝ｻ・｢驛｢譎｢・ｽ・ｻ郢晢ｽｻ繝ｻ・ｽ驛｢譎｢・ｽ・ｻ郢晢ｽｻ繝ｻ・ｻ鬯ｩ蟷｢・ｽ・｢髫ｴ雜｣・ｽ・｢郢晢ｽｻ繝ｻ・ｽ郢晢ｽｻ繝ｻ・ｻ鬩幢ｽ｢隴趣ｽ｢繝ｻ・ｽ繝ｻ・ｻ驛｢譎｢・ｽ・ｻ郢晢ｽｻ繝ｻ・ｴ鬯ｯ・ｯ繝ｻ・ｯ郢晢ｽｻ繝ｻ・ｮ驛｢譎｢・ｽ・ｻ郢晢ｽｻ繝ｻ・ｯ鬯ｮ・ｫ繝ｻ・ｶ髴難ｽ｣陋帙・・ｽ・ｽ繝ｻ・･驛｢譎｢・ｽ・ｻ郢晢ｽｻ繝ｻ・ｽ郢晢ｽｻ繝ｻ・ｽ驛｢譎｢・ｽ・ｻ郢晢ｽｻ繝ｻ・ｻ鬯ｩ蟷｢・ｽ・｢郢晢ｽｻ繝ｻ・ｧ鬮ｫ・ｰ郢晢ｽｻ遶乗ｧｭ繝ｻ繝ｻ・ｽ郢晢ｽｻ繝ｻ・ｽ驛｢譎｢・ｽ・ｻ郢晢ｽｻ繝ｻ・ｽ鬩幢ｽ｢隴趣ｽ｢繝ｻ・ｽ繝ｻ・ｻ驛｢譎｢・ｽ・ｻ郢晢ｽｻ繝ｻ・ｽ鬯ｩ蟷｢・ｽ・｢髫ｴ雜｣・ｽ・｢郢晢ｽｻ繝ｻ・ｽ郢晢ｽｻ繝ｻ・ｻ鬩幢ｽ｢隴趣ｽ｢繝ｻ・ｽ繝ｻ・ｻ驛｢譎｢・ｽ・ｻ郢晢ｽｻ繝ｻ・ｲ鬯ｯ・ｯ繝ｻ・ｩ髯晢ｽｷ繝ｻ・｢郢晢ｽｻ繝ｻ・ｽ郢晢ｽｻ繝ｻ・｢鬯ｮ・ｫ繝ｻ・ｴ鬮ｮ諛ｶ・ｽ・｣郢晢ｽｻ繝ｻ・ｽ郢晢ｽｻ繝ｻ・｢鬩幢ｽ｢隴趣ｽ｢繝ｻ・ｽ繝ｻ・ｻ驛｢譎｢・ｽ・ｻ郢晢ｽｻ繝ｻ・ｽ鬩幢ｽ｢隴趣ｽ｢繝ｻ・ｽ繝ｻ・ｻ驛｢譎｢・ｽ・ｻ郢晢ｽｻ繝ｻ・ｻ鬯ｯ・ｩ陝ｷ・｢繝ｻ・ｽ繝ｻ・｢鬮ｫ・ｴ髮懶ｽ｣繝ｻ・ｽ繝ｻ・｢驛｢譎｢・ｽ・ｻ郢晢ｽｻ繝ｻ・ｽ驛｢譎｢・ｽ・ｻ郢晢ｽｻ繝ｻ・ｻ鬯ｩ蟷｢・ｽ・｢髫ｴ雜｣・ｽ・｢郢晢ｽｻ繝ｻ・ｽ郢晢ｽｻ繝ｻ・ｻ鬩幢ｽ｢隴趣ｽ｢繝ｻ・ｽ繝ｻ・ｻ驛｢譎｢・ｽ・ｻ郢晢ｽｻ繝ｻ・ｽ鬯ｯ・ｯ繝ｻ・ｩ髯晢ｽｷ繝ｻ・｢郢晢ｽｻ繝ｻ・ｽ郢晢ｽｻ繝ｻ・｢鬯ｮ・ｫ繝ｻ・ｴ鬮ｮ諛ｶ・ｽ・｣郢晢ｽｻ繝ｻ・ｽ郢晢ｽｻ繝ｻ・｢鬩幢ｽ｢隴趣ｽ｢繝ｻ・ｽ繝ｻ・ｻ驛｢譎｢・ｽ・ｻ郢晢ｽｻ繝ｻ・ｽ鬩幢ｽ｢隴趣ｽ｢繝ｻ・ｽ繝ｻ・ｻ驛｢譎｢・ｽ・ｻ郢晢ｽｻ繝ｻ・ｻ鬯ｯ・ｩ陝ｷ・｢繝ｻ・ｽ繝ｻ・｢鬮ｫ・ｴ髮懶ｽ｣繝ｻ・ｽ繝ｻ・｢驛｢譎｢・ｽ・ｻ郢晢ｽｻ繝ｻ・ｽ驛｢譎｢・ｽ・ｻ郢晢ｽｻ繝ｻ・ｻ鬯ｩ蟷｢・ｽ・｢髫ｴ雜｣・ｽ・｢郢晢ｽｻ繝ｻ・ｽ郢晢ｽｻ繝ｻ・ｻ鬩幢ｽ｢隴趣ｽ｢繝ｻ・ｽ繝ｻ・ｻ驛｢譎｢・ｽ・ｻ郢晢ｽｻ繝ｻ・ｿ鬯ｯ・ｯ繝ｻ・ｯ郢晢ｽｻ繝ｻ・ｯ驛｢譎｢・ｽ・ｻ郢晢ｽｻ繝ｻ・ｯ鬩幢ｽ｢隴趣ｽ｢繝ｻ・ｽ繝ｻ・ｻ驛｢譎｢・ｽ・ｻ郢晢ｽｻ繝ｻ・ｮ鬯ｩ蟷｢・ｽ・｢髫ｴ雜｣・ｽ・｢郢晢ｽｻ繝ｻ・ｽ郢晢ｽｻ繝ｻ・ｻ鬩幢ｽ｢隴趣ｽ｢繝ｻ・ｽ繝ｻ・ｻ驛｢譎｢・ｽ・ｻ郢晢ｽｻ繝ｻ・ｯ鬯ｯ・ｩ陝ｷ・｢繝ｻ・ｽ繝ｻ・｢鬮ｫ・ｴ髮懶ｽ｣繝ｻ・ｽ繝ｻ・｢驛｢譎｢・ｽ・ｻ郢晢ｽｻ繝ｻ・ｽ驛｢譎｢・ｽ・ｻ郢晢ｽｻ繝ｻ・ｻ鬯ｩ蟷｢・ｽ・｢髫ｴ雜｣・ｽ・｢郢晢ｽｻ繝ｻ・ｽ郢晢ｽｻ繝ｻ・ｻ鬩幢ｽ｢隴趣ｽ｢繝ｻ・ｽ繝ｻ・ｻ驛｢譎｢・ｽ・ｻ郢晢ｽｻ繝ｻ・ｷ鬯ｯ・ｯ繝ｻ・ｩ髯晢ｽｷ繝ｻ・｢郢晢ｽｻ繝ｻ・ｽ郢晢ｽｻ繝ｻ・｢鬯ｮ・ｫ繝ｻ・ｴ鬮ｮ諛ｶ・ｽ・｣郢晢ｽｻ繝ｻ・ｽ郢晢ｽｻ繝ｻ・｢鬩幢ｽ｢隴趣ｽ｢繝ｻ・ｽ繝ｻ・ｻ驛｢譎｢・ｽ・ｻ郢晢ｽｻ繝ｻ・ｽ鬩幢ｽ｢隴趣ｽ｢繝ｻ・ｽ繝ｻ・ｻ驛｢譎｢・ｽ・ｻ郢晢ｽｻ繝ｻ・ｻ鬯ｯ・ｩ陝ｷ・｢繝ｻ・ｽ繝ｻ・｢鬮ｫ・ｴ髮懶ｽ｣繝ｻ・ｽ繝ｻ・｢驛｢譎｢・ｽ・ｻ郢晢ｽｻ繝ｻ・ｽ驛｢譎｢・ｽ・ｻ郢晢ｽｻ繝ｻ・ｻ鬯ｩ蟷｢・ｽ・｢髫ｴ雜｣・ｽ・｢郢晢ｽｻ繝ｻ・ｽ郢晢ｽｻ繝ｻ・ｻ鬩幢ｽ｢隴趣ｽ｢繝ｻ・ｽ繝ｻ・ｻ驛｢譎｢・ｽ・ｻ郢晢ｽｻ繝ｻ・ｴ鬯ｯ・ｯ繝ｻ・ｯ郢晢ｽｻ繝ｻ・ｯ驛｢譎｢・ｽ・ｻ郢晢ｽｻ繝ｻ・ｩ鬯ｮ・ｯ隴趣ｽ｢繝ｻ・ｽ繝ｻ・ｷ驛｢譎｢・ｽ・ｻ郢晢ｽｻ繝ｻ・｢鬩幢ｽ｢隴趣ｽ｢繝ｻ・ｽ繝ｻ・ｻ驛｢譎｢・ｽ・ｻ郢晢ｽｻ繝ｻ・ｽ鬩幢ｽ｢隴趣ｽ｢繝ｻ・ｽ繝ｻ・ｻ驛｢譎｢・ｽ・ｻ郢晢ｽｻ繝ｻ・｢鬯ｯ・ｯ繝ｻ・ｮ郢晢ｽｻ繝ｻ・ｫ驛｢譎｢・ｽ・ｻ郢晢ｽｻ繝ｻ・ｴ鬯ｯ・ｮ繝ｻ・ｮ髫ｲ蟷｢・ｽ・ｶ郢晢ｽｻ繝ｻ・ｽ郢晢ｽｻ繝ｻ・｣鬩幢ｽ｢隴趣ｽ｢繝ｻ・ｽ繝ｻ・ｻ驛｢譎｢・ｽ・ｻ郢晢ｽｻ繝ｻ・ｽ鬩幢ｽ｢隴趣ｽ｢繝ｻ・ｽ繝ｻ・ｻ驛｢譎｢・ｽ・ｻ郢晢ｽｻ繝ｻ・｢鬯ｯ・ｩ陝ｷ・｢繝ｻ・ｽ繝ｻ・｢鬮ｫ・ｴ髮懶ｽ｣繝ｻ・ｽ繝ｻ・｢驛｢譎｢・ｽ・ｻ郢晢ｽｻ繝ｻ・ｽ驛｢譎｢・ｽ・ｻ郢晢ｽｻ繝ｻ・ｻ鬯ｩ蟷｢・ｽ・｢髫ｴ雜｣・ｽ・｢郢晢ｽｻ繝ｻ・ｽ郢晢ｽｻ繝ｻ・ｻ鬩幢ｽ｢隴趣ｽ｢繝ｻ・ｽ繝ｻ・ｻ驛｢譎｢・ｽ・ｻ郢晢ｽｻ繝ｻ・ｽ鬯ｯ・ｩ陝ｷ・｢繝ｻ・ｽ繝ｻ・｢鬮ｫ・ｴ髮懶ｽ｣繝ｻ・ｽ繝ｻ・｢驛｢譎｢・ｽ・ｻ郢晢ｽｻ繝ｻ・ｽ驛｢譎｢・ｽ・ｻ郢晢ｽｻ繝ｻ・ｻ鬯ｩ蟷｢・ｽ・｢髫ｴ雜｣・ｽ・｢郢晢ｽｻ繝ｻ・ｽ郢晢ｽｻ繝ｻ・ｻ鬩幢ｽ｢隴趣ｽ｢繝ｻ・ｽ繝ｻ・ｻ驛｢譎｢・ｽ・ｻ郢晢ｽｻ繝ｻ・ｻ de cut Repair Hole.", "Repair Hole", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
			return;
		}
		Debug.WriteLine("[REPAIR HOLE] start. diameterMm=" + (num * 1000.0).ToString("0.###", CultureInfo.InvariantCulture) + ", depthMm=" + (num2 * 1000.0).ToString("0.###", CultureInfo.InvariantCulture) + ", hasFace=" + (selection.Face != null) + ", hasEdge=" + (selection.Edge != null) + ", hasPoint=" + IsPoint(selection.SidePoint));
		if (selection.Face != null && selection.Edge == null && !IsPoint(selection.SidePoint))
		{
			if (!IsPlanarFace(selection.Face))
			{
				Debug.WriteLine("[REPAIR HOLE] selected face is not planar.");
				MessageBox.Show("Mat da chon khong phai mat phang. Hay chon mat flat/unfold.", "Repair Hole", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
				return;
			}
			try
			{
				if (!TryRepairHolesFromPlanarFace(modelDoc, selection.Face, num, num2, out var repairedCount, out var message))
				{
					MessageBox.Show(message, "Repair Hole", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
					return;
				}
				modelDoc.GraphicsRedraw2();
				MessageBox.Show("Da tao Repair Hole: " + repairedCount + " lo.", "Repair Hole", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
				return;
			}
			catch (Exception ex)
			{
				Debug.WriteLine("[REPAIR HOLE] face mode failed: " + ex);
				MessageBox.Show("Loi Repair Hole: " + ex.Message, "Repair Hole", MessageBoxButtons.OK, MessageBoxIcon.Hand);
				return;
			}
		}
		if (!TryGetRepairHoleCenter(selection, options, out var center, out var diameterM))
		{
			Debug.WriteLine("[REPAIR HOLE] no usable center. Select planar face for auto-scan, or point/edge for single repair.");
			MessageBox.Show("Hay chon mat phang flat/unfold de tu quet lo, hoac chon point/canh lo can repair.", "Repair Hole", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
			return;
		}
		Face2 repairHoleFace = GetRepairHoleFace(selection);
		if (!IsPlanarFace(repairHoleFace))
		{
			MessageBox.Show("Khong tim thay mat phang de sketch repair hole.", "Repair Hole", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
			return;
		}
		center = GetClosestPointOnFace(repairHoleFace, center);
		try
		{
			Feature feature = CreateRepairHoleCut(modelDoc, repairHoleFace, center, diameterM, num2);
			modelDoc.GraphicsRedraw2();
			if (feature == null)
			{
				MessageBox.Show("Chua tao duoc cut Repair Hole. Hay chon mat/canh ro hon roi thu lai.", "Repair Hole", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
			}
			else
			{
				MessageBox.Show("Da tao Repair Hole.", "Repair Hole", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
			}
		}
		catch (Exception ex2)
		{
			MessageBox.Show("Loi Repair Hole: " + ex2.Message, "Repair Hole", MessageBoxButtons.OK, MessageBoxIcon.Hand);
		}
	}

	private SelectionInfo GetSelection(ModelDoc2 model)
	{
		SelectionInfo selectionInfo = new SelectionInfo();
		SelectionMgr selectionMgr = model.SelectionManager as SelectionMgr;
		int num = selectionMgr?.GetSelectedObjectCount2(-1) ?? 0;
		for (int i = 1; i <= num; i++)
		{
			object selectedObject = selectionMgr.GetSelectedObject6(i, -1);
			int selectedObjectType = selectionMgr.GetSelectedObjectType3(i, -1);
			Debug.WriteLine("[MAKE HOLE] Selection #" + i + " type=" + selectedObjectType + ", object=" + ((selectedObject == null) ? "null" : selectedObject.GetType().FullName));
			if (selectionInfo.Face == null && selectedObjectType == 2)
			{
				selectionInfo.Face = selectedObject as Face2;
			}
			else if (selectionInfo.Edge == null && selectedObjectType == 1)
			{
				selectionInfo.Edge = selectedObject as Edge;
			}
			else if (selectionInfo.SidePoint == null && IsPoint(TryGetSelectionPoint(selectedObject, selectedObjectType)))
			{
				selectionInfo.SidePoint = TryGetSelectionPoint(selectedObject, selectedObjectType);
			}
			else if (selectionInfo.SeedFeature == null && selectedObjectType == 22)
			{
				selectionInfo.SeedFeature = TryGetSelectedFeature(model, selectionMgr, selectedObject, i);
			}
			else if (selectionInfo.SeedFeature == null && selectedObject is Feature)
			{
				selectionInfo.SeedFeature = selectedObject as Feature;
			}
			else if (selectionInfo.SeedFeature == null)
			{
				selectionInfo.SeedFeature = TryGetFeatureFromSelection(selectedObject);
			}
		}
		return selectionInfo;
	}

	private Feature TryGetSelectedFeature(ModelDoc2 model, SelectionMgr selMgr, object selected, int index)
	{
		if (selected is Feature feature)
		{
			Debug.WriteLine("[MAKE HOLE] Selection BODYFEATURES direct feature: " + SafeFeatureName(feature));
			return feature;
		}
		Feature feature2 = TryGetFeatureFromSelection(selected);
		if (feature2 != null)
		{
			return feature2;
		}
		string text = "";
		try
		{
			text = ((dynamic)selMgr).GetSelectedObjectName2(index);
			Debug.WriteLine("[MAKE HOLE] Selection BODYFEATURES name=" + text);
		}
		catch (Exception ex)
		{
			Debug.WriteLine("[MAKE HOLE] Selection BODYFEATURES GetSelectedObjectName2 failed: " + ex.Message);
		}
		if (!string.IsNullOrWhiteSpace(text))
		{
			try
			{
				feature2 = ((dynamic)model).FeatureByName(text) as Feature;
				Debug.WriteLine("[MAKE HOLE] Selection BODYFEATURES FeatureByName=" + ((feature2 == null) ? "null" : SafeFeatureName(feature2)));
				return feature2;
			}
			catch (Exception ex2)
			{
				Debug.WriteLine("[MAKE HOLE] Selection BODYFEATURES FeatureByName failed: " + ex2.Message);
			}
		}
		return null;
	}

	private string TryCreatePatternLengthReference(ModelDoc2 model, List<SketchSegment> patternSegments)
	{
		pendingPatternLengthIsExpression = false;
		string text = TryCreateCurveLengthDimension(model, patternSegments);
		if (!string.IsNullOrWhiteSpace(text))
		{
			trackedLengthUsesVariable = false;
			Debug.WriteLine("[MAKE HOLE] Pattern length reference uses SolidWorks dimension directly: " + text);
			return text;
		}
		string text2 = TryCreateMeasuredLengthExpression();
		if (!string.IsNullOrWhiteSpace(text2))
		{
			Debug.WriteLine("[MAKE HOLE] Pattern length reference uses measured numeric length: " + text2 + "mm");
			return text2;
		}
		string text3 = TryCreateUsableLengthEquationVariable(model);
		Debug.WriteLine("[MAKE HOLE] Pattern length reference fallback variable: " + (text3 ?? "null"));
		return text3;
	}

	private string TryCreateMeasuredLengthExpression()
	{
		double num = 0.0;
		if (lastCalculatedPatternUsableLength > 1E-06)
		{
			num = lastCalculatedPatternUsableLength * 1000.0;
		}
		else if (pendingPatternSpacing > 1E-06 && pendingPatternCount > 1)
		{
			num = pendingPatternSpacing * 1000.0 * (double)(pendingPatternCount - 1);
		}
		if (num <= 0.001)
		{
			return null;
		}
		pendingPatternLengthIsExpression = true;
		trackedLengthUsesVariable = false;
		trackedLengthVariableName = null;
		trackedPatternSketchName = pendingPatternSketchName;
		trackedPatternLengthMm = num;
		trackedPatternPitchMm = pendingPatternMaxPitchMm;
		string text = num.ToString("0.###", CultureInfo.InvariantCulture);
		Debug.WriteLine("[MAKE HOLE] Pattern length reference uses direct measured expression: " + text + "mm, sketch=" + trackedPatternSketchName);
		return text;
	}

	private string TryCreateUsableLengthEquationVariable(ModelDoc2 model)
	{
		if (model == null)
		{
			return null;
		}
		double num = 0.0;
		if (lastCalculatedPatternUsableLength > 1E-06)
		{
			num = lastCalculatedPatternUsableLength * 1000.0;
		}
		else if (pendingPatternSpacing > 1E-06 && pendingPatternCount > 1)
		{
			num = pendingPatternSpacing * 1000.0 * (double)(pendingPatternCount - 1);
		}
		if (num <= 0.001)
		{
			Debug.WriteLine("[MAKE HOLE] Usable length variable skip. invalid length.");
			return null;
		}
		string text = "TAI_MAKE_HOLE_LEN_" + DateTime.Now.ToString("HHmmssfff", CultureInfo.InvariantCulture);
		string equation = "\"" + text + "\" = " + num.ToString("0.###", CultureInfo.InvariantCulture);
		bool flag = AddOrUpdateEquation(model, equation);
		Debug.WriteLine("[MAKE HOLE] Usable length variable. ok=" + flag + ", name=" + text + ", value=" + num.ToString("0.###", CultureInfo.InvariantCulture) + "mm");
		if (flag)
		{
			TrackMakeHoleLengthVariable(text, num);
		}
		return flag ? text : null;
	}

	private bool IsBadCurveLengthDimension(string dimensionName, double dimensionMm, double expectedMm)
	{
		string text = dimensionName ?? "";
		if (dimensionMm <= 0.001)
		{
			return true;
		}
		if (expectedMm > 0.001)
		{
			double num = Math.Max(1.0, expectedMm * 0.05);
			double num2 = Math.Abs(dimensionMm - expectedMm);
			bool flag = num2 <= num;
			Debug.WriteLine("[MAKE HOLE] Length dimension check. name=" + text + ", dim=" + dimensionMm.ToString("0.###", CultureInfo.InvariantCulture) + "mm, expected=" + expectedMm.ToString("0.###", CultureInfo.InvariantCulture) + "mm, diff=" + num2.ToString("0.###", CultureInfo.InvariantCulture) + "mm, tol=" + num.ToString("0.###", CultureInfo.InvariantCulture) + "mm, accepted=" + flag);
			return !flag;
		}
		if (text.StartsWith("RD", StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}
		if (text.StartsWith("R", StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}
		return false;
	}

	private void TryDeleteDisplayDimension(ModelDoc2 model, DisplayDimension displayDimension)
	{
		if (model == null || displayDimension == null)
		{
			return;
		}
		try
		{
			if (displayDimension.GetAnnotation() is Annotation annotation && annotation.Select3(Append: false, null))
			{
				model.EditDelete();
			}
		}
		catch (Exception ex)
		{
			Debug.WriteLine("[MAKE HOLE] Delete bad length dimension failed: " + ex.Message);
		}
		try
		{
			model.ClearSelection2(All: true);
		}
		catch
		{
		}
	}

	private void TrackMakeHoleLengthVariable(string variableName, double lengthMm)
	{
		trackedPatternSketchName = pendingPatternSketchName;
		trackedLengthVariableName = variableName;
		trackedPatternLengthMm = lengthMm;
		trackedPatternPitchMm = pendingPatternMaxPitchMm;
		trackedLengthUsesVariable = !string.IsNullOrWhiteSpace(variableName);
		Debug.WriteLine("[MAKE HOLE] Track length variable. sketch=" + trackedPatternSketchName + ", variable=" + trackedLengthVariableName + ", lengthMm=" + trackedPatternLengthMm.ToString("0.###", CultureInfo.InvariantCulture));
	}

	private void ClearTrackedMakeHoleUpdate()
	{
		trackedPatternSketchName = null;
		trackedLengthVariableName = null;
		trackedPatternFeatureName = null;
		trackedPatternCountDimensionName = null;
		trackedHoleFeatureName = null;
		trackedPatternLengthMm = 0.0;
		trackedPatternPitchMm = 0.0;
		trackedLengthUsesVariable = false;
	}

	public bool IsMakeHoleUpdateRequired()
	{
		return IsMakeHoleUpdateRequired(trackedPatternPitchMm);
	}

	public bool IsMakeHoleUpdateRequired(double currentPitchMm)
	{
		if (!(swApp?.ActiveDoc is ModelDoc2 model))
		{
			return false;
		}
		if (!trackedLengthUsesVariable)
		{
			return false;
		}
		if (string.IsNullOrWhiteSpace(trackedPatternSketchName) || string.IsNullOrWhiteSpace(trackedLengthVariableName))
		{
			return false;
		}
		double currentTrackedPatternLengthMm = GetCurrentTrackedPatternLengthMm(model);
		if (currentTrackedPatternLengthMm <= 0.001)
		{
			return false;
		}
		double num = Math.Abs(currentTrackedPatternLengthMm - trackedPatternLengthMm);
		double num2 = Math.Abs(currentPitchMm - trackedPatternPitchMm);
		Debug.WriteLine("[MAKE HOLE] Check update. old=" + trackedPatternLengthMm.ToString("0.###", CultureInfo.InvariantCulture) + "mm, current=" + currentTrackedPatternLengthMm.ToString("0.###", CultureInfo.InvariantCulture) + "mm, diff=" + num.ToString("0.###", CultureInfo.InvariantCulture) + "mm, oldPitch=" + trackedPatternPitchMm.ToString("0.###", CultureInfo.InvariantCulture) + "mm, currentPitch=" + currentPitchMm.ToString("0.###", CultureInfo.InvariantCulture) + "mm, pitchDiff=" + num2.ToString("0.###", CultureInfo.InvariantCulture) + "mm");
		return num > 0.05 || num2 > 0.05;
	}

	public bool CleanupTrackedMakeHoleEquationsIfFeatureMissing()
	{
		if (!(swApp?.ActiveDoc is ModelDoc2 modelDoc))
		{
			return false;
		}
		if (string.IsNullOrWhiteSpace(trackedPatternSketchName) && string.IsNullOrWhiteSpace(trackedLengthVariableName) && string.IsNullOrWhiteSpace(trackedPatternFeatureName) && string.IsNullOrWhiteSpace(trackedPatternCountDimensionName) && string.IsNullOrWhiteSpace(trackedHoleFeatureName))
		{
			return false;
		}
		bool flag = !string.IsNullOrWhiteSpace(trackedPatternSketchName) && FindSketchByName(modelDoc, trackedPatternSketchName) == null;
		bool flag2 = !string.IsNullOrWhiteSpace(trackedPatternFeatureName) && FindFeatureByName(modelDoc, trackedPatternFeatureName) == null;
		bool flag3 = !string.IsNullOrWhiteSpace(trackedHoleFeatureName) && FindFeatureByName(modelDoc, trackedHoleFeatureName) == null;
		if (!flag && !flag2 && !flag3)
		{
			return false;
		}
		bool flag4 = false;
		if (!string.IsNullOrWhiteSpace(trackedLengthVariableName))
		{
			flag4 |= DeleteEquationByLeftSide(modelDoc, "\"" + trackedLengthVariableName + "\"");
		}
		if (!string.IsNullOrWhiteSpace(trackedPatternCountDimensionName))
		{
			flag4 |= DeleteEquationByLeftSide(modelDoc, "\"" + trackedPatternCountDimensionName + "\"");
		}
		if (!string.IsNullOrWhiteSpace(trackedPatternCountDimensionName))
		{
			flag4 |= DeleteEquationsContaining(modelDoc, trackedPatternCountDimensionName);
		}
		if (!string.IsNullOrWhiteSpace(trackedPatternFeatureName))
		{
			flag4 |= DeleteEquationsContaining(modelDoc, trackedPatternFeatureName);
		}
		if (!string.IsNullOrWhiteSpace(trackedHoleFeatureName))
		{
			flag4 |= DeleteEquationsContaining(modelDoc, trackedHoleFeatureName);
		}
		if (!flag4 && !string.IsNullOrWhiteSpace(trackedPatternFeatureName))
		{
			flag4 |= DeleteEquationByLeftSide(modelDoc, "\"D1@" + trackedPatternFeatureName + "\"");
		}
		Debug.WriteLine("[MAKE HOLE] Cleanup tracked equations. missingSketch=" + flag + ", missingPattern=" + flag2 + ", missingHole=" + flag3 + ", holeFeature=" + trackedHoleFeatureName + ", patternFeature=" + trackedPatternFeatureName + ", countDim=" + trackedPatternCountDimensionName + ", lengthVar=" + trackedLengthVariableName + ", deleted=" + flag4);
		ClearTrackedMakeHoleUpdate();
		try
		{
			modelDoc.EditRebuild3();
		}
		catch
		{
		}
		return flag4;
	}

	private double GetCurrentTrackedPatternLengthMm(ModelDoc2 model)
	{
		if (model == null || string.IsNullOrWhiteSpace(trackedPatternSketchName))
		{
			return 0.0;
		}
		Sketch sketch = FindSketchByName(model, trackedPatternSketchName);
		if (sketch == null)
		{
			Debug.WriteLine("[MAKE HOLE] Current length check failed. sketch not found: " + trackedPatternSketchName);
			return 0.0;
		}
		List<SketchSegment> usableSketchSegments = GetUsableSketchSegments(sketch);
		SketchSegment sketchSegment = FindLongestSketchSegment(usableSketchSegments);
		if (sketchSegment == null)
		{
			return 0.0;
		}
		double result = GetSketchSegmentApproxLength(sketchSegment) * 1000.0;
		Debug.WriteLine("[MAKE HOLE] Current tracked pattern length=" + result.ToString("0.###", CultureInfo.InvariantCulture) + "mm, sketch=" + trackedPatternSketchName);
		return result;
	}

	public bool UpdateTrackedMakeHolePattern()
	{
		return UpdateTrackedMakeHolePattern(trackedPatternPitchMm);
	}

	public bool UpdateTrackedMakeHolePattern(double newPitchMm)
	{
		if (!(swApp?.ActiveDoc is ModelDoc2 modelDoc))
		{
			MessageBox.Show("Hay mo Part truoc.", "Update Hole", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
			return false;
		}
		if (!trackedLengthUsesVariable || string.IsNullOrWhiteSpace(trackedLengthVariableName) || string.IsNullOrWhiteSpace(trackedPatternSketchName))
		{
			MessageBox.Show("Khong co Make Hole nao can update.", "Update Hole", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
			return false;
		}
		double currentTrackedPatternLengthMm = GetCurrentTrackedPatternLengthMm(modelDoc);
		if (currentTrackedPatternLengthMm <= 0.001)
		{
			MessageBox.Show("Khong doc duoc chieu dai curve hien tai.", "Update Hole", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
			return false;
		}
		double num = Math.Max(0.001, newPitchMm);
		double num2 = Math.Abs(currentTrackedPatternLengthMm - trackedPatternLengthMm);
		double num3 = Math.Abs(num - trackedPatternPitchMm);
		if (num2 <= 0.05 && num3 <= 0.05)
		{
			Debug.WriteLine("[MAKE HOLE] Update skipped. No changed length or pitch. lengthDiff=" + num2.ToString("0.###", CultureInfo.InvariantCulture) + "mm, pitchDiff=" + num3.ToString("0.###", CultureInfo.InvariantCulture) + "mm");
			MessageBox.Show("Kich thuoc va Pitch khong thay doi.", "Update Hole", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
			return false;
		}
		string equation = "\"" + trackedLengthVariableName + "\" = " + currentTrackedPatternLengthMm.ToString("0.###", CultureInfo.InvariantCulture);
		bool flag = AddOrUpdateEquation(modelDoc, equation);
		bool flag2 = TryUpdateTrackedPatternCountEquation(modelDoc, num);
		if (flag && flag2)
		{
			trackedPatternLengthMm = currentTrackedPatternLengthMm;
			trackedPatternPitchMm = num;
			try
			{
				modelDoc.EditRebuild3();
			}
			catch
			{
			}
			Debug.WriteLine("[MAKE HOLE] Update tracked Make Hole ok. variable=" + trackedLengthVariableName + ", newLength=" + currentTrackedPatternLengthMm.ToString("0.###", CultureInfo.InvariantCulture) + "mm, newPitch=" + trackedPatternPitchMm.ToString("0.###", CultureInfo.InvariantCulture) + "mm");
			MessageBox.Show("Da update Make Hole Pattern.", "Update Hole", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
			return true;
		}
		MessageBox.Show("Update Make Hole Pattern that bai.", "Update Hole", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
		return false;
	}

	private bool TryUpdateTrackedPatternCountEquation(ModelDoc2 model, double pitchMm)
	{
		if (model == null || string.IsNullOrWhiteSpace(trackedLengthVariableName) || pitchMm <= 1E-06)
		{
			return false;
		}
		Feature feature = FindTrackedPatternFeature(model);
		if (feature == null)
		{
			Debug.WriteLine("[MAKE HOLE] Update pattern count equation failed. Pattern feature not found.");
			return false;
		}
		string text = GetPatternCountDimensionName(feature);
		if (string.IsNullOrWhiteSpace(text))
		{
			text = "D1@" + SafeFeatureName(feature);
			Debug.WriteLine("[MAKE HOLE] Update pattern count dimension fallback=" + text);
		}
		string text2 = BuildPatternCountEquation(text, trackedLengthVariableName, pitchMm);
		bool result = AddOrUpdateEquation(model, text2);
		Debug.WriteLine("[MAKE HOLE] Update pattern count equation. ok=" + result + ", pitch=" + pitchMm.ToString("0.###", CultureInfo.InvariantCulture) + "mm, equation=" + text2);
		return result;
	}

	private Feature FindTrackedPatternFeature(ModelDoc2 model)
	{
		if (model == null)
		{
			return null;
		}
		Feature result = null;
		try
		{
			for (Feature feature = model.FirstFeature() as Feature; feature != null; feature = feature.GetNextFeature() as Feature)
			{
				string a = SafeFeatureName(feature);
				if (!string.IsNullOrWhiteSpace(trackedPatternFeatureName) && string.Equals(a, trackedPatternFeatureName, StringComparison.OrdinalIgnoreCase))
				{
					return feature;
				}
				if (string.Equals(a, "TAI_MAKE_HOLE_PATTERN", StringComparison.OrdinalIgnoreCase) || string.Equals(a, "TAI_MAKE_HOLE_PATTERN_DEF", StringComparison.OrdinalIgnoreCase))
				{
					result = feature;
				}
			}
		}
		catch (Exception ex)
		{
			Debug.WriteLine("[MAKE HOLE] Find tracked pattern feature failed: " + ex.Message);
		}
		return result;
	}

	private Feature FindFeatureByName(ModelDoc2 model, string featureName)
	{
		if (model == null || string.IsNullOrWhiteSpace(featureName))
		{
			return null;
		}
		try
		{
			for (Feature feature = model.FirstFeature() as Feature; feature != null; feature = feature.GetNextFeature() as Feature)
			{
				if (string.Equals(SafeFeatureName(feature), featureName, StringComparison.OrdinalIgnoreCase))
				{
					return feature;
				}
			}
		}
		catch (Exception ex)
		{
			Debug.WriteLine("[MAKE HOLE] Find feature by name failed: " + ex.Message);
		}
		return null;
	}

	private Feature TryGetFeatureFromSelection(object selected)
	{
		if (selected == null)
		{
			return null;
		}
		try
		{
			if (((dynamic)selected).GetFeature() is Feature feature)
			{
				Debug.WriteLine("[MAKE HOLE] Selection feature from GetFeature: " + SafeFeatureName(feature));
				return feature;
			}
		}
		catch
		{
		}
		try
		{
			if (((dynamic)selected).Feature is Feature feature2)
			{
				Debug.WriteLine("[MAKE HOLE] Selection feature from Feature property: " + SafeFeatureName(feature2));
				return feature2;
			}
		}
		catch
		{
		}
		return null;
	}

	private string SafeFeatureName(Feature feature)
	{
		if (feature == null)
		{
			return "";
		}
		try
		{
			return feature.Name ?? "";
		}
		catch
		{
			return "";
		}
	}

	private double[] TryGetSelectionPoint(object selected, int type)
	{
		if (selected is Vertex vertex)
		{
			return vertex.GetPoint() as double[];
		}
		if (selected is SketchPoint sketchPoint)
		{
			return new double[3] { sketchPoint.X, sketchPoint.Y, sketchPoint.Z };
		}
		if (type == 3 || type == 11)
		{
			try
			{
				return ((dynamic)selected).GetPoint() as double[];
			}
			catch
			{
			}
		}
		try
		{
			double[] array = ((dynamic)selected).GetRefPoint() as double[];
			if (IsPoint(array))
			{
				return array;
			}
		}
		catch
		{
		}
		if (selected is Feature feature)
		{
			try
			{
				dynamic specificFeature = feature.GetSpecificFeature2();
				double[] array2 = specificFeature.GetRefPoint() as double[];
				if (IsPoint(array2))
				{
					return array2;
				}
			}
			catch
			{
			}
		}
		try
		{
			dynamic specificFeature2 = ((dynamic)selected).GetSpecificFeature2();
			double[] array3 = specificFeature2.GetRefPoint() as double[];
			if (IsPoint(array3))
			{
				return array3;
			}
		}
		catch
		{
		}
		return null;
	}

	private bool TryGetRepairHoleCenter(SelectionInfo selection, MakeHoleOptions options, out double[] center, out double diameterM)
	{
		center = null;
		diameterM = ((options != null && options.DiameterMm > 0.0) ? (options.DiameterMm / 1000.0) : 0.0042);
		if (selection == null)
		{
			return false;
		}
		if (TryGetCircularEdgeData(selection.Edge, out var center2, out var radius))
		{
			center = center2;
			if (radius > 1E-06)
			{
				diameterM = radius * 2.0;
			}
			Debug.WriteLine("[REPAIR HOLE] center from circular edge. diameterMm=" + (diameterM * 1000.0).ToString("0.###", CultureInfo.InvariantCulture));
			return true;
		}
		if (IsPoint(selection.SidePoint))
		{
			center = selection.SidePoint;
			Debug.WriteLine("[REPAIR HOLE] center from selected point. diameterMm=" + (diameterM * 1000.0).ToString("0.###", CultureInfo.InvariantCulture));
			return true;
		}
		if (TryGetEdgeGeometry(selection.Edge, out var geometry))
		{
			center = geometry.Mid;
			Debug.WriteLine("[REPAIR HOLE] center from edge midpoint. diameterMm=" + (diameterM * 1000.0).ToString("0.###", CultureInfo.InvariantCulture));
			return true;
		}
		return false;
	}

	private bool TryGetCircularEdgeData(Edge edge, out double[] center, out double radius)
	{
		center = null;
		radius = 0.0;
		if (!(edge?.GetCurve() is Curve curve))
		{
			return false;
		}
		try
		{
			bool flag = false;
			try
			{
				flag = (bool)((dynamic)curve).IsCircle();
			}
			catch
			{
			}
			if (!flag)
			{
				return false;
			}
			if (!(((dynamic)curve).CircleParams is double[] array) || array.Length < 7)
			{
				return false;
			}
			center = new double[3]
			{
				array[0],
				array[1],
				array[2]
			};
			radius = Math.Abs(array[6]);
			return IsPoint(center) && radius > 1E-06;
		}
		catch (Exception ex)
		{
			Debug.WriteLine("[REPAIR HOLE] circular edge read failed: " + ex.Message);
			return false;
		}
	}

	private Face2 GetRepairHoleFace(SelectionInfo selection)
	{
		if (selection == null)
		{
			return null;
		}
		if (IsPlanarFace(selection.Face))
		{
			return selection.Face;
		}
		List<Face2> planarAdjacentFaces = GetPlanarAdjacentFaces(selection.Edge);
		if (planarAdjacentFaces.Count > 0)
		{
			return planarAdjacentFaces[0];
		}
		return GetFirstAdjacentFace(selection.Edge);
	}

	private double[] GetClosestPointOnFace(Face2 face, double[] point)
	{
		if (!IsPoint(point))
		{
			return point;
		}
		try
		{
			if (((dynamic)face).GetClosestPointOn(point[0], point[1], point[2]) is double[] array && array.Length >= 3)
			{
				return new double[3]
				{
					array[0],
					array[1],
					array[2]
				};
			}
		}
		catch
		{
		}
		return point;
	}

	private bool TryGetFaceNormalAtPoint(Face2 face, double[] point, out double[] normal)
	{
		normal = null;
		if (!(face?.GetSurface() is Surface surface))
		{
			return false;
		}
		try
		{
			if (surface.IsPlane() && surface.PlaneParams is double[] array && array.Length >= 6)
			{
				normal = Normalize(new double[3]
				{
					array[3],
					array[4],
					array[5]
				});
				if (normal != null)
				{
					return true;
				}
			}
		}
		catch
		{
		}
		try
		{
			if (((dynamic)face).GetClosestPointOn(point[0], point[1], point[2]) is double[] array2 && array2.Length >= 5 && ((dynamic)surface).Evaluate(array2[3], array2[4], 1, 1) is double[] array3 && array3.Length >= 9)
			{
				double[] left = new double[3]
				{
					array3[3],
					array3[4],
					array3[5]
				};
				double[] right = new double[3]
				{
					array3[6],
					array3[7],
					array3[8]
				};
				normal = Normalize(Cross(left, right));
				if (normal != null)
				{
					return true;
				}
			}
		}
		catch
		{
		}
		try
		{
			if (((dynamic)surface).EvaluateAtPoint(point[0], point[1], point[2]) is double[] array4 && array4.Length >= 6)
			{
				normal = Normalize(new double[3]
				{
					array4[3],
					array4[4],
					array4[5]
				});
				if (normal != null)
				{
					return true;
				}
			}
		}
		catch
		{
		}
		try
		{
			double[] vector = ((dynamic)face).Normal as double[];
			normal = Normalize(vector);
			return normal != null;
		}
		catch
		{
			return false;
		}
	}

	private bool TryGetEdgeGeometry(Edge edge, out EdgeGeometry geometry)
	{
		geometry = null;
		if (!(edge?.GetCurve() is Curve curve))
		{
			return false;
		}
		double Start = 0.0;
		double End = 0.0;
		double[] array = null;
		double[] array2 = null;
		try
		{
			CurveParamData curveParams = edge.GetCurveParams3();
			if (curveParams != null)
			{
				Start = curveParams.UMinValue;
				End = curveParams.UMaxValue;
				array = curveParams.StartPoint as double[];
				array2 = curveParams.EndPoint as double[];
			}
		}
		catch
		{
		}
		if (!IsPoint(array) || !IsPoint(array2))
		{
			Vertex vertex = edge.GetStartVertex() as Vertex;
			Vertex vertex2 = edge.GetEndVertex() as Vertex;
			array = vertex?.GetPoint() as double[];
			array2 = vertex2?.GetPoint() as double[];
		}
		if (!IsPoint(array) || !IsPoint(array2))
		{
			if (!curve.GetEndParams(out Start, out End, out var _, out var _))
			{
				return false;
			}
			array = curve.Evaluate(Start) as double[];
			array2 = curve.Evaluate(End) as double[];
		}
		if (!IsPoint(array) || !IsPoint(array2))
		{
			return false;
		}
		double[] array3 = Normalize(Subtract(array2, array));
		if (array3 == null)
		{
			return false;
		}
		geometry = new EdgeGeometry
		{
			Curve = curve,
			StartParam = Start,
			EndParam = End,
			Start = array,
			End = array2,
			Mid = Scale(Add(array, array2), 0.5),
			Direction = array3,
			Length = Distance(array, array2)
		};
		return geometry.Length > 0.001;
	}

	private double[] GetCurvePointOrDefault(Curve curve, double param, double[] fallback)
	{
		try
		{
			double[] array = curve?.Evaluate(param) as double[];
			if (IsPoint(array))
			{
				return new double[3]
				{
					array[0],
					array[1],
					array[2]
				};
			}
		}
		catch
		{
		}
		return fallback;
	}

	private Face2 GetFirstAdjacentFace(Edge edge)
	{
		if (edge == null)
		{
			return null;
		}
		try
		{
			object twoAdjacentFaces = ((dynamic)edge).GetTwoAdjacentFaces2();
			if (twoAdjacentFaces is Array array)
			{
				foreach (object item in array)
				{
					if (item is Face2 result)
					{
						return result;
					}
				}
			}
			if (twoAdjacentFaces is Face2 result2)
			{
				return result2;
			}
		}
		catch
		{
		}
		try
		{
			object face = ((dynamic)edge).GetFace();
			return face as Face2;
		}
		catch
		{
			return null;
		}
	}

	private OffsetPath BuildOffsetPath(EdgeGeometry edge, Face2 face, double[] fallbackNormal, double[] sidePoint, MakeHoleOptions options)
	{
		double num = options.LeftOffsetMm / 1000.0;
		double num2 = options.RightOffsetMm / 1000.0;
		double num3 = options.PitchMm / 1000.0;
		double num4 = options.EdgeOffsetMm / 1000.0;
		List<double[]> list = SampleCurve(edge, 96);
		if (list.Count < 2)
		{
			return null;
		}
		double num5 = 1.0;
		if (sidePoint != null)
		{
			int index = list.Count / 2;
			double[] sampleTangent = GetSampleTangent(list, index);
			if (!TryGetFaceNormalAtPoint(face, list[index], out var normal))
			{
				normal = fallbackNormal;
			}
			double[] array = Normalize(Cross(normal, sampleTangent));
			if (array != null)
			{
				double num6 = Dot(Subtract(sidePoint, list[index]), array);
				if (num6 < 0.0)
				{
					num5 = -1.0;
				}
			}
		}
		List<double[]> list2 = new List<double[]>();
		for (int i = 0; i < list.Count; i++)
		{
			double[] sampleTangent2 = GetSampleTangent(list, i);
			if (!TryGetFaceNormalAtPoint(face, list[i], out var normal2))
			{
				normal2 = fallbackNormal;
			}
			double[] array2 = Normalize(Cross(normal2, sampleTangent2));
			if (array2 != null)
			{
				list2.Add(Add(list[i], Scale(array2, num4 * num5)));
			}
		}
		if (list2.Count < 2)
		{
			return null;
		}
		List<double> list3 = BuildCumulativeLengths(list2);
		double num7 = list3[list3.Count - 1];
		double num8 = num7 - num - num2;
		if (num8 <= 0.001 || num3 <= 1E-06)
		{
			return null;
		}
		int num9 = Math.Max(2, (int)Math.Ceiling(num8 / num3) + 1);
		double num10 = ((num9 > 1) ? (num8 / (double)(num9 - 1)) : 0.0);
		OffsetPath offsetPath = new OffsetPath();
		offsetPath.Points.AddRange(list2);
		for (int j = 0; j < num9; j++)
		{
			double distance = num + num10 * (double)j;
			double[] array3 = InterpolateByDistance(list2, list3, distance);
			if (array3 != null)
			{
				offsetPath.HolePoints.Add(array3);
			}
		}
		return offsetPath;
	}

	private List<double[]> SampleCurve(EdgeGeometry edge, int count)
	{
		List<double[]> list = new List<double[]>();
		if (edge?.Curve == null || count < 2)
		{
			return list;
		}
		for (int i = 0; i < count; i++)
		{
			double num = ((count == 1) ? 0.0 : ((double)i / (double)(count - 1)));
			double parameter = edge.StartParam + (edge.EndParam - edge.StartParam) * num;
			double[] array = edge.Curve.Evaluate(parameter) as double[];
			if (IsPoint(array))
			{
				list.Add(new double[3]
				{
					array[0],
					array[1],
					array[2]
				});
			}
		}
		return list;
	}

	private List<double> BuildCumulativeLengths(List<double[]> points)
	{
		List<double> list = new List<double>();
		double num = 0.0;
		list.Add(num);
		for (int i = 1; i < points.Count; i++)
		{
			num += Distance(points[i - 1], points[i]);
			list.Add(num);
		}
		return list;
	}

	private double[] InterpolateByDistance(List<double[]> points, List<double> cumulative, double distance)
	{
		if (points == null || cumulative == null || points.Count == 0 || cumulative.Count != points.Count)
		{
			return null;
		}
		if (distance <= 0.0)
		{
			return points[0];
		}
		double num = cumulative[cumulative.Count - 1];
		if (distance >= num)
		{
			return points[points.Count - 1];
		}
		for (int i = 1; i < points.Count; i++)
		{
			if (!(cumulative[i] < distance))
			{
				double num2 = cumulative[i] - cumulative[i - 1];
				if (num2 <= 1E-07)
				{
					return points[i];
				}
				double scale = (distance - cumulative[i - 1]) / num2;
				return Add(points[i - 1], Scale(Subtract(points[i], points[i - 1]), scale));
			}
		}
		return points[points.Count - 1];
	}

	private double[] GetSampleTangent(List<double[]> points, int index)
	{
		if (points == null || points.Count < 2)
		{
			return null;
		}
		if (index <= 0)
		{
			return Normalize(Subtract(points[1], points[0]));
		}
		if (index >= points.Count - 1)
		{
			return Normalize(Subtract(points[points.Count - 1], points[points.Count - 2]));
		}
		return Normalize(Subtract(points[index + 1], points[index - 1]));
	}

	private bool CreateOffsetPreview3DSketch(ModelDoc2 model, OffsetPath path, out Sketch activeSketch, out List<SketchSegment> segments)
	{
		activeSketch = null;
		segments = new List<SketchSegment>();
		if (model == null || path == null || path.Points.Count < 2)
		{
			return false;
		}
		model.ClearSelection2(All: true);
		model.SketchManager.Insert3DSketch(UpdateEditRebuild: true);
		activeSketch = GetActiveSketch(model);
		for (int i = 1; i < path.Points.Count; i++)
		{
			double[] array = path.Points[i - 1];
			double[] array2 = path.Points[i];
			model.SketchManager.CreateLine(array[0], array[1], array[2], array2[0], array2[1], array2[2]);
		}
		foreach (double[] holePoint in path.HolePoints)
		{
			model.SketchManager.CreatePoint(holePoint[0], holePoint[1], holePoint[2]);
		}
		segments = GetSketchSegments(activeSketch);
		model.SketchManager.Insert3DSketch(UpdateEditRebuild: true);
		return segments.Count > 0;
	}

	private bool CreateOffsetOnSurfaceSketch(ModelDoc2 model, Face2 face, Edge edge, Feature seedFeature, EdgeGeometry edgeGeometry, double[] sidePoint, MakeHoleOptions options, out int pointCount, out string message)
	{
		pointCount = 0;
		message = "";
		if (model == null || edge == null)
		{
			message = "Hay chon 1 edge truoc.";
			return false;
		}
		double num = options.EdgeOffsetMm / 1000.0;
		if (num <= 0.0)
		{
			message = "Dim Edge X phai lon hon 0.";
			return false;
		}
		if (face == null)
		{
			face = GetFirstAdjacentFace(edge);
		}
		for (int i = 0; i < 2; i++)
		{
			bool reverse = i == 1;
			if (TryCreateOffsetOnSurfaceAttempt(model, face, edge, seedFeature, options, num, reverse, chain: true, out pointCount) || TryCreateOffsetOnSurfaceAttempt(model, face, edge, seedFeature, options, num, reverse, chain: false, out pointCount))
			{
				return true;
			}
		}
		if (TryCreateSampledOffsetSketch(model, face, edgeGeometry, sidePoint, seedFeature, options, out pointCount, out message))
		{
			return true;
		}
		message = "Khong tao duoc Offset Curve bang SolidWorks. Hay kiem tra edge va Dim Edge X.";
		return false;
	}

	private bool TryCreateSampledOffsetSketch(ModelDoc2 model, Face2 face, EdgeGeometry edgeGeometry, double[] sidePoint, Feature seedFeature, MakeHoleOptions options, out int pointCount, out string message)
	{
		pointCount = 0;
		message = "";
		if (model == null || edgeGeometry == null || face == null)
		{
			return false;
		}
		if (!TryGetFaceNormalAtPoint(face, edgeGeometry.Mid, out var normal))
		{
			message = "Khong lay duoc phap tuyen mat de tu tinh offset.";
			return false;
		}
		OffsetPath offsetPath = BuildOffsetPath(edgeGeometry, face, normal, sidePoint, options);
		if (offsetPath == null || offsetPath.Points.Count < 2 || offsetPath.HolePoints.Count == 0)
		{
			message = "Khong tu tinh duoc duong offset. Hay kiem tra Dim Edge X/Pitch.";
			return false;
		}
		if (!CreateOffsetPreview3DSketch(model, offsetPath, out var activeSketch, out var segments))
		{
			message = "Khong tao duoc 3D sketch offset fallback.";
			return false;
		}
		pointCount = offsetPath.HolePoints.Count;
		Debug.WriteLine("[MAKE HOLE] Sampled offset fallback ok. points=" + offsetPath.Points.Count + ", holePoints=" + offsetPath.HolePoints.Count + ", patternSegments=" + segments.Count);
		if (string.Equals(options.HoleType, "Circle", StringComparison.OrdinalIgnoreCase))
		{
			BeginHybridHoleWizard(model, face, activeSketch, segments, pointCount, options);
		}
		return true;
	}

	private Face2 GetLargestPlanarAdjacentFace(Edge edge)
	{
		if (edge == null)
		{
			return null;
		}
		Face2 face = null;
		double num = -1.0;
		try
		{
			if (!(((dynamic)edge).GetTwoAdjacentFaces2() is Array array))
			{
				return null;
			}
			foreach (object item in array)
			{
				Face2 face2 = item as Face2;
				Surface surface = face2?.GetSurface() as Surface;
				if (face2 != null && surface != null && surface.IsPlane())
				{
					double area = face2.GetArea();
					if (area > num)
					{
						num = area;
						face = face2;
					}
				}
			}
		}
		catch
		{
		}
		Debug.WriteLine("[MAKE HOLE] Line Edge planar face. found=" + (face != null) + ", area=" + ((num < 0.0) ? "n/a" : ((num * 1000000.0).ToString("0.###", CultureInfo.InvariantCulture) + "mm2")));
		return face;
	}

	private List<Face2> GetPlanarAdjacentFaces(Edge edge)
	{
		List<Face2> list = new List<Face2>();
		if (edge == null)
		{
			return list;
		}
		try
		{
			if (((dynamic)edge).GetTwoAdjacentFaces2() is Array array)
			{
				foreach (object item in array)
				{
					Face2 face = item as Face2;
					if (IsPlanarFace(face) && !list.Contains(face))
					{
						list.Add(face);
					}
				}
			}
		}
		catch
		{
		}
		Face2 firstAdjacentFace = GetFirstAdjacentFace(edge);
		if (IsPlanarFace(firstAdjacentFace) && !list.Contains(firstAdjacentFace))
		{
			list.Add(firstAdjacentFace);
		}
		return list;
	}

	private bool IsPlanarFace(Face2 face)
	{
		try
		{
			return face?.GetSurface() is Surface surface && surface.IsPlane();
		}
		catch
		{
			return false;
		}
	}

	private bool CreateStraightEdgeOffsetSketch(ModelDoc2 model, Face2 face, Edge edge, Feature seedFeature, EdgeGeometry edgeGeometry, double[] sidePoint, MakeHoleOptions options, bool requireStraightLine, out int pointCount, out string message)
	{
		pointCount = 0;
		message = "";
		if (model == null || edge == null || edgeGeometry == null)
		{
			message = "Hay chon 1 line edge truoc.";
			return false;
		}
		bool flag = false;
		try
		{
			flag = edgeGeometry.Curve != null && edgeGeometry.Curve.IsLine();
		}
		catch
		{
		}
		if (requireStraightLine && !flag)
		{
			message = "Direction Line Flow chi dung cho edge thang.";
			return false;
		}
		double num = options.EdgeOffsetMm / 1000.0;
		Debug.WriteLine("[MAKE HOLE] Line Edge input Dim Edge X=" + options.EdgeOffsetMm.ToString("0.###", CultureInfo.InvariantCulture) + "mm");
		if (num <= 0.0)
		{
			message = "Dim Edge X phai lon hon 0.";
			return false;
		}
		if (!TryResolveStraightEdgeOffset(face, edge, edgeGeometry, sidePoint, num, out face, out var signedDistance))
		{
			Face2 largestPlanarAdjacentFace = GetLargestPlanarAdjacentFace(edge);
			if (largestPlanarAdjacentFace != null)
			{
				face = largestPlanarAdjacentFace;
			}
			else if (face == null)
			{
				face = GetFirstAdjacentFace(edge);
			}
			if (!IsPlanarFace(face))
			{
				message = (requireStraightLine ? "Line Flow can mat phang va edge thang. Voi mat cong hay chon Curve Flow." : "Spline Flow can spline/curve tren mat phang. Voi mat cong hay chon Curve Flow.");
				return false;
			}
			signedDistance = GetStraightOffsetDistance(face, edgeGeometry, num);
		}
		return TryCreateStraightEdgeOffsetAttempt(model, face, edge, edgeGeometry, seedFeature, options, signedDistance, requireStraightLine, out pointCount, out message);
	}

	private bool TryResolveStraightEdgeOffset(Face2 preferredFace, Edge edge, EdgeGeometry edgeGeometry, double[] sidePoint, double distance, out Face2 face, out double signedDistance)
	{
		face = null;
		signedDistance = distance;
		if (edge == null || edgeGeometry == null || distance <= 0.0)
		{
			return false;
		}
		List<Face2> list = new List<Face2>();
		Face2 largestPlanarAdjacentFace = GetLargestPlanarAdjacentFace(edge);
		if (largestPlanarAdjacentFace != null)
		{
			list.Add(largestPlanarAdjacentFace);
		}
		else if (IsPlanarFace(preferredFace))
		{
			list.Add(preferredFace);
		}
		else
		{
			list.AddRange(GetPlanarAdjacentFaces(edge));
		}
		double num = double.MaxValue;
		double num2 = 0.0;
		foreach (Face2 item in list)
		{
			if (!TryGetFaceNormalAtPoint(item, edgeGeometry.Mid, out var normal))
			{
				continue;
			}
			double[] array = Normalize(Cross(normal, edgeGeometry.Direction));
			if (array == null)
			{
				continue;
			}
			double num3 = 0.0;
			if (sidePoint != null)
			{
				double num4 = Dot(Subtract(sidePoint, edgeGeometry.Mid), array);
				num3 = ((num4 < 0.0) ? (-1.0) : 1.0);
			}
			double num5 = 0.0;
			try
			{
				num5 = item.GetArea();
			}
			catch
			{
			}
			double[] array2 = ((num3 != 0.0) ? new double[2]
			{
				num3,
				0.0 - num3
			} : new double[2] { 1.0, -1.0 });
			for (int i = 0; i < array2.Length; i++)
			{
				double num6 = array2[i];
				double offsetFaceGap = GetOffsetFaceGap(item, edgeGeometry, array, distance * num6);
				double num7 = ((num3 != 0.0 && num6 != num3) ? (distance * 2.0) : 0.0);
				double num8 = offsetFaceGap + num7 - Math.Min(num5, 1.0) * 1E-06;
				Debug.WriteLine("[MAKE HOLE] Line Edge offset candidate. sign=" + num6.ToString("0", CultureInfo.InvariantCulture) + ", faceGap=" + (offsetFaceGap * 1000.0).ToString("0.###", CultureInfo.InvariantCulture) + "mm, faceArea=" + (num5 * 1000000.0).ToString("0.###", CultureInfo.InvariantCulture) + "mm2");
				if (num8 < num)
				{
					num = num8;
					num2 = num5;
					face = item;
					signedDistance = distance * num6;
				}
			}
		}
		bool result = face != null;
		Debug.WriteLine("[MAKE HOLE] Line Edge resolved offset. resolved=" + result + ", distance=" + (signedDistance * 1000.0).ToString("0.###", CultureInfo.InvariantCulture) + "mm, score=" + (num * 1000.0).ToString("0.###", CultureInfo.InvariantCulture) + "mm, faceArea=" + (num2 * 1000000.0).ToString("0.###", CultureInfo.InvariantCulture) + "mm2");
		return result;
	}

	private double GetOffsetFaceGap(Face2 face, EdgeGeometry edge, double[] offsetDirection, double signedDistance)
	{
		if (face == null || edge == null || offsetDirection == null)
		{
			return double.MaxValue;
		}
		double[][] array = new double[3][] { edge.Start, edge.Mid, edge.End };
		double num = 0.0;
		double num2 = 0.0;
		int num3 = 0;
		double[][] array2 = array;
		foreach (double[] array3 in array2)
		{
			if (IsPoint(array3))
			{
				double distanceToFace = GetDistanceToFace(face, Add(array3, Scale(offsetDirection, signedDistance)));
				if (distanceToFace == double.MaxValue)
				{
					return double.MaxValue;
				}
				num = Math.Max(num, distanceToFace);
				num2 += distanceToFace;
				num3++;
			}
		}
		if (num3 == 0)
		{
			return double.MaxValue;
		}
		return num + num2 / (double)num3;
	}

	private double GetStraightOffsetDistance(Face2 face, EdgeGeometry edge, double distance)
	{
		if (face == null || edge == null || !TryGetFaceNormalAtPoint(face, edge.Mid, out var normal))
		{
			return distance;
		}
		double[] array = Normalize(Cross(normal, edge.Direction));
		if (array == null)
		{
			return distance;
		}
		double distanceToFace = GetDistanceToFace(face, Add(edge.Mid, Scale(array, distance)));
		double distanceToFace2 = GetDistanceToFace(face, Add(edge.Mid, Scale(array, 0.0 - distance)));
		double num = ((distanceToFace <= distanceToFace2) ? distance : (0.0 - distance));
		Debug.WriteLine("[MAKE HOLE] Line Edge offset side. positiveGap=" + (distanceToFace * 1000.0).ToString("0.###", CultureInfo.InvariantCulture) + "mm, negativeGap=" + (distanceToFace2 * 1000.0).ToString("0.###", CultureInfo.InvariantCulture) + "mm, distance=" + (num * 1000.0).ToString("0.###", CultureInfo.InvariantCulture) + "mm");
		return num;
	}

	private double GetDistanceToFace(Face2 face, double[] point)
	{
		if (face == null || !IsPoint(point))
		{
			return double.MaxValue;
		}
		try
		{
			if (((dynamic)face).GetClosestPointOn(point[0], point[1], point[2]) is double[] array && array.Length >= 3)
			{
				return Distance(point, new double[3]
				{
					array[0],
					array[1],
					array[2]
				});
			}
		}
		catch
		{
		}
		return double.MaxValue;
	}

	private bool TryCreateStraightEdgeOffsetAttempt(ModelDoc2 model, Face2 face, Edge edge, EdgeGeometry edgeGeometry, Feature seedFeature, MakeHoleOptions options, double signedDistance, bool requireStraightLine, out int pointCount, out string message)
	{
		pointCount = 0;
		message = "";
		model.ClearSelection2(All: true);
		if (!SelectFace(face, append: false))
		{
			message = "Khong chon duoc mat phang cua line edge.";
			return false;
		}
		model.SketchManager.InsertSketch(UpdateEditRebuild: true);
		Sketch activeSketch = GetActiveSketch(model);
		if (activeSketch == null)
		{
			message = "Khong tao duoc sketch cho Offset Edge.";
			return false;
		}
		double num = signedDistance;
		if (TryCreateNativeStraightEdgeOffsetInActiveSketch(model, activeSketch, face, edge, edgeGeometry, num, requireStraightLine, out var offsetSegment))
		{
			Debug.WriteLine("[MAKE HOLE] " + (requireStraightLine ? "Line Flow" : "Spline Flow") + " uses native offset edge.");
			if (requireStraightLine)
			{
				TryCreateLineEdgeOffsetDimension(model, edge, offsetSegment, edgeGeometry, face, num);
			}
		}
		else if (TryCreateNativeStraightEdgeOffsetInActiveSketch(model, activeSketch, face, edge, edgeGeometry, 0.0 - num, requireStraightLine, out offsetSegment))
		{
			num = 0.0 - num;
			Debug.WriteLine("[MAKE HOLE] " + (requireStraightLine ? "Line Flow" : "Spline Flow") + " uses native offset edge reversed.");
			if (requireStraightLine)
			{
				TryCreateLineEdgeOffsetDimension(model, edge, offsetSegment, edgeGeometry, face, num);
			}
		}
		else
		{
			if (!requireStraightLine)
			{
				DeleteSketchSegments(model, GetSketchSegments(activeSketch));
				model.SketchManager.InsertSketch(UpdateEditRebuild: true);
				message = "Khong tao duoc Spline Flow bang Sketch Offset. Hay kiem tra spline/Dim Edge X.";
				return false;
			}
			DeleteSketchSegments(model, GetSketchSegments(activeSketch));
			if (TryCreateStraightOffsetLineInActiveSketch(model, face, edgeGeometry, num, out offsetSegment) && IsUsableManualOffsetSegment(offsetSegment, edgeGeometry, Math.Abs(num), face))
			{
				Debug.WriteLine("[MAKE HOLE] Line Edge uses manual planar offset line.");
				TryCreateLineEdgeOffsetDimension(model, edge, offsetSegment, edgeGeometry, face, num);
			}
			else
			{
				DeleteSketchSegments(model, GetSketchSegments(activeSketch));
				if (!TryCreateStraightOffsetLineInActiveSketch(model, face, edgeGeometry, 0.0 - num, out offsetSegment) || !IsUsableManualOffsetSegment(offsetSegment, edgeGeometry, Math.Abs(num), face))
				{
					DeleteSketchSegments(model, GetSketchSegments(activeSketch));
					model.SketchManager.InsertSketch(UpdateEditRebuild: true);
					message = "Khong tao duoc Offset Edge dung khoang cach. Hay kiem tra line edge va Dim Edge X.";
					return false;
				}
				num = 0.0 - num;
				Debug.WriteLine("[MAKE HOLE] Line Edge uses manual planar offset line reversed.");
				TryCreateLineEdgeOffsetDimension(model, edge, offsetSegment, edgeGeometry, face, num);
			}
		}
		List<SketchSegment> sketchSegments = GetSketchSegments(activeSketch);
		List<double[]> list = BuildPathPointsFromSketchSegments(sketchSegments);
		List<double[]> list2 = BuildSplitPoints(list, options);
		List<double[]> list3 = BuildDivisionPoints(list, options);
		Debug.WriteLine("[MAKE HOLE] Offset Edge ok. distance=" + (signedDistance * 1000.0).ToString("0.###", CultureInfo.InvariantCulture) + "mm, segments=" + sketchSegments.Count + ", pathPoints=" + list.Count + ", splitPoints=" + list2.Count + ", holePoints=" + list3.Count);
		if (list.Count < 2 || list2.Count == 0)
		{
			model.SketchManager.InsertSketch(UpdateEditRebuild: true);
			message = "Da offset edge nhung khong doc duoc duong de chia.";
			return false;
		}
		pointCount = SplitSketchSegments(model, activeSketch, sketchSegments, list2);
		Debug.WriteLine("[MAKE HOLE] Offset Edge split result. splitCount=" + pointCount);
		Debug.WriteLine("[MAKE HOLE] Offset Edge split endpoint dims=" + TryCreateSplitEndpointDimensions(model, activeSketch, list, list2));
		List<SketchSegment> sketchSegments2 = GetSketchSegments(activeSketch);
		model.SketchManager.InsertSketch(UpdateEditRebuild: true);
		if (string.Equals(options.HoleType, "Circle", StringComparison.OrdinalIgnoreCase))
		{
			BeginHybridHoleWizard(model, face, activeSketch, sketchSegments2, list3.Count, options);
		}
		return true;
	}

	private bool TryCreateNativeStraightEdgeOffsetInActiveSketch(ModelDoc2 model, Sketch activeSketch, Face2 face, Edge edge, EdgeGeometry edgeGeometry, double signedDistance, bool requireStraightLine, out SketchSegment offsetSegment)
	{
		offsetSegment = null;
		if (model == null || activeSketch == null || edge == null || edgeGeometry == null)
		{
			return false;
		}
		try
		{
			DeleteSketchSegments(model, GetSketchSegments(activeSketch));
			model.ClearSelection2(All: true);
			if (!SelectEdge(edge, append: false))
			{
				Debug.WriteLine("[MAKE HOLE] Native Line Edge offset skip. Cannot select source edge.");
				return false;
			}
			model.SketchOffsetEdges(signedDistance);
			List<SketchSegment> sketchSegments = GetSketchSegments(activeSketch);
			offsetSegment = (requireStraightLine ? FindBestOffsetSegment(sketchSegments, edgeGeometry, Math.Abs(signedDistance), face) : FindLongestSketchSegment(sketchSegments));
			bool flag = offsetSegment != null;
			Debug.WriteLine("[MAKE HOLE] Native " + (requireStraightLine ? "Line Flow" : "Spline Flow") + " offset. ok=" + flag + ", distance=" + (signedDistance * 1000.0).ToString("0.###", CultureInfo.InvariantCulture) + "mm, segments=" + sketchSegments.Count);
			if (!flag)
			{
				DeleteSketchSegments(model, sketchSegments);
			}
			return flag;
		}
		catch (Exception ex)
		{
			Debug.WriteLine("[MAKE HOLE] Native Line Edge offset failed: " + ex.Message);
			try
			{
				DeleteSketchSegments(model, GetSketchSegments(activeSketch));
			}
			catch
			{
			}
			return false;
		}
	}

	private SketchSegment FindBestOffsetSegment(List<SketchSegment> segments, EdgeGeometry source, double expectedDistance, Face2 face)
	{
		SketchSegment result = null;
		double num = double.MaxValue;
		if (segments == null || source == null || expectedDistance <= 1E-06)
		{
			return null;
		}
		foreach (SketchSegment segment in segments)
		{
			if (IsUsableOffsetSegment(segment, source, expectedDistance, face))
			{
				double[] sketchSegmentMidPoint = GetSketchSegmentMidPoint(segment);
				double num2 = DistancePointToLine(sketchSegmentMidPoint, source.Start, source.Direction);
				double num3 = Math.Abs(num2 - expectedDistance);
				if (num3 < num)
				{
					num = num3;
					result = segment;
				}
			}
		}
		return result;
	}

	private bool IsUsableOffsetSegment(SketchSegment segment, EdgeGeometry source, double expectedDistance, Face2 face)
	{
		double[] sketchSegmentMidPoint = GetSketchSegmentMidPoint(segment);
		if (!IsPoint(sketchSegmentMidPoint) || source == null || expectedDistance <= 1E-06)
		{
			return false;
		}
		double num = DistancePointToLine(sketchSegmentMidPoint, source.Start, source.Direction);
		double sketchSegmentApproxLength = GetSketchSegmentApproxLength(segment);
		double sketchSegmentFaceGap = GetSketchSegmentFaceGap(segment, face);
		double num2 = expectedDistance * 0.45;
		double num3 = expectedDistance * 1.55;
		bool result = num >= expectedDistance * 0.45 && num <= num3 && sketchSegmentApproxLength >= source.Length * 0.45;
		Debug.WriteLine("[MAKE HOLE] Offset segment check. usable=" + result + ", distance=" + (num * 1000.0).ToString("0.###", CultureInfo.InvariantCulture) + "mm, expected=" + (expectedDistance * 1000.0).ToString("0.###", CultureInfo.InvariantCulture) + "mm, min=" + (num2 * 1000.0).ToString("0.###", CultureInfo.InvariantCulture) + "mm, max=" + (num3 * 1000.0).ToString("0.###", CultureInfo.InvariantCulture) + "mm, length=" + (sketchSegmentApproxLength * 1000.0).ToString("0.###", CultureInfo.InvariantCulture) + "mm, faceGapDiagnostic=" + (sketchSegmentFaceGap * 1000.0).ToString("0.###", CultureInfo.InvariantCulture) + "mm");
		return result;
	}

	private bool IsUsableManualOffsetSegment(SketchSegment segment, EdgeGeometry source, double expectedDistance, Face2 face)
	{
		if (segment == null || source == null || expectedDistance <= 1E-06)
		{
			return false;
		}
		double sketchSegmentApproxLength = GetSketchSegmentApproxLength(segment);
		double sketchSegmentFaceGap = GetSketchSegmentFaceGap(segment, face);
		double num = Math.Max(0.0015, expectedDistance * 0.25);
		bool result = sketchSegmentApproxLength >= source.Length * 0.45 && (face == null || sketchSegmentFaceGap <= num || sketchSegmentFaceGap == double.MaxValue);
		Debug.WriteLine("[MAKE HOLE] Manual offset segment check. usable=" + result + ", expected=" + (expectedDistance * 1000.0).ToString("0.###", CultureInfo.InvariantCulture) + "mm, length=" + (sketchSegmentApproxLength * 1000.0).ToString("0.###", CultureInfo.InvariantCulture) + "mm, faceGap=" + ((sketchSegmentFaceGap == double.MaxValue) ? "n/a" : (sketchSegmentFaceGap * 1000.0).ToString("0.###", CultureInfo.InvariantCulture)) + "mm, faceTol=" + (num * 1000.0).ToString("0.###", CultureInfo.InvariantCulture) + "mm");
		return result;
	}

	private double GetSketchSegmentFaceGap(SketchSegment segment, Face2 face)
	{
		if (segment == null || face == null)
		{
			return 0.0;
		}
		List<double[]> list = GetSketchSegmentPointsFallback(segment);
		if (list == null || list.Count < 2)
		{
			list = BuildSegmentSamplePoints(segment);
		}
		if (list == null || list.Count == 0)
		{
			return double.MaxValue;
		}
		double num = 0.0;
		double num2 = 0.0;
		int num3 = 0;
		for (int i = 0; i < list.Count; i++)
		{
			if (list.Count > 6 && i % Math.Max(1, list.Count / 6) != 0 && i != list.Count - 1)
			{
				continue;
			}
			double[] point = list[i];
			if (IsPoint(point))
			{
				double distanceToFace = GetDistanceToFace(face, point);
				if (distanceToFace == double.MaxValue)
				{
					return double.MaxValue;
				}
				num = Math.Max(num, distanceToFace);
				num2 += distanceToFace;
				num3++;
			}
		}
		if (num3 == 0)
		{
			return double.MaxValue;
		}
		return num + num2 / (double)num3;
	}

	private double[] GetSketchSegmentMidPoint(SketchSegment segment)
	{
		List<double[]> list = GetSketchSegmentPointsFallback(segment);
		if (list == null || list.Count < 2)
		{
			list = BuildSegmentSamplePoints(segment);
		}
		if (list == null || list.Count == 0)
		{
			return null;
		}
		return list[list.Count / 2];
	}

	private double DistancePointToLine(double[] point, double[] linePoint, double[] lineDirection)
	{
		if (!IsPoint(point) || !IsPoint(linePoint) || !IsPoint(lineDirection))
		{
			return 0.0;
		}
		double[] left = Subtract(point, linePoint);
		double scale = Dot(left, lineDirection);
		double[] right = Add(linePoint, Scale(lineDirection, scale));
		return Distance(point, right);
	}

	private void DeleteSketchSegments(ModelDoc2 model, List<SketchSegment> segments)
	{
		if (model == null || segments == null || segments.Count == 0)
		{
			return;
		}
		try
		{
			model.ClearSelection2(All: true);
			bool flag = false;
			foreach (SketchSegment segment in segments)
			{
				if (SelectSketchSegment(segment, flag))
				{
					flag = true;
				}
			}
			if (flag)
			{
				model.EditDelete();
			}
			model.ClearSelection2(All: true);
		}
		catch (Exception ex)
		{
			Debug.WriteLine("[MAKE HOLE] Delete sketch segments failed: " + ex.Message);
		}
	}

	private bool TryCreateStraightOffsetLineInActiveSketch(ModelDoc2 model, Face2 face, EdgeGeometry edgeGeometry, double signedDistance, out SketchSegment segment)
	{
		segment = null;
		if (model == null || face == null || edgeGeometry == null)
		{
			return false;
		}
		if (!TryGetFaceNormalAtPoint(face, edgeGeometry.Mid, out var normal))
		{
			return false;
		}
		double[] array = Normalize(Cross(normal, edgeGeometry.Direction));
		if (array == null)
		{
			return false;
		}
		double[] array2 = Add(edgeGeometry.Start, Scale(array, signedDistance));
		double[] array3 = Add(edgeGeometry.End, Scale(array, signedDistance));
		if (!IsPoint(array2) || !IsPoint(array3) || Distance(array2, array3) <= 1E-06)
		{
			return false;
		}
		try
		{
			object obj = model.SketchManager.CreateLine(array2[0], array2[1], array2[2], array3[0], array3[1], array3[2]);
			segment = obj as SketchSegment;
			Debug.WriteLine("[MAKE HOLE] Line Edge manual offset line. ok=" + (segment != null) + ", distance=" + (signedDistance * 1000.0).ToString("0.###", CultureInfo.InvariantCulture) + "mm, start=(" + (array2[0] * 1000.0).ToString("0.###", CultureInfo.InvariantCulture) + "," + (array2[1] * 1000.0).ToString("0.###", CultureInfo.InvariantCulture) + "," + (array2[2] * 1000.0).ToString("0.###", CultureInfo.InvariantCulture) + ")");
			return segment != null;
		}
		catch (Exception ex)
		{
			Debug.WriteLine("[MAKE HOLE] Line Edge manual offset line failed: " + ex.Message);
			return false;
		}
	}

	private bool TryCreateLineEdgeOffsetDimension(ModelDoc2 model, Edge sourceEdge, SketchSegment offsetSegment, EdgeGeometry edgeGeometry, Face2 face, double signedDistance)
	{
		if (model == null || sourceEdge == null || offsetSegment == null || edgeGeometry == null)
		{
			return false;
		}
		if (!TryGetFaceNormalAtPoint(face, edgeGeometry.Mid, out var normal))
		{
			return false;
		}
		double[] array = Normalize(Cross(normal, edgeGeometry.Direction));
		if (array == null)
		{
			return false;
		}
		double num = ((signedDistance >= 0.0) ? 1.0 : (-1.0));
		double[] array2 = Add(edgeGeometry.Mid, Add(Scale(array, signedDistance + num * 0.006), Scale(edgeGeometry.Direction, 0.006)));
		try
		{
			bool previousValue;
			bool shouldRestore = TrySetInputDimensionOnCreate(model, enabled: false, out previousValue);
			object obj = null;
			try
			{
				model.ClearSelection2(All: true);
				if (!SelectEdge(sourceEdge, append: false) || !SelectSketchSegment(offsetSegment, append: true))
				{
					Debug.WriteLine("[MAKE HOLE] Line Edge X dim skip. Cannot select source edge and offset segment.");
					model.ClearSelection2(All: true);
					return false;
				}
				obj = model.AddDimension2(array2[0], array2[1], array2[2]);
			}
			finally
			{
				RestoreInputDimensionOnCreate(model, shouldRestore, previousValue);
			}
			DisplayDimension displayDimension = obj as DisplayDimension;
			Dimension dimension = null;
			if (displayDimension != null)
			{
				try
				{
					dimension = displayDimension.GetDimension2(0);
				}
				catch
				{
				}
			}
			if (dimension != null)
			{
				TrySetDimensionSystemValue(dimension, Math.Abs(signedDistance), "Line Edge X");
			}
			Debug.WriteLine("[MAKE HOLE] Line Edge X dim created. ok=" + (obj != null) + ", value=" + (Math.Abs(signedDistance) * 1000.0).ToString("0.###", CultureInfo.InvariantCulture) + "mm");
			model.ClearSelection2(All: true);
			return obj != null;
		}
		catch (Exception ex)
		{
			Debug.WriteLine("[MAKE HOLE] Line Edge X dim create failed: " + ex.Message);
			try
			{
				model.ClearSelection2(All: true);
			}
			catch
			{
			}
			return false;
		}
	}

	private bool TrySetInputDimensionOnCreate(ModelDoc2 model, bool enabled, out bool previousValue)
	{
		previousValue = false;
		try
		{
			if (swApp != null)
			{
				previousValue = swApp.GetUserPreferenceToggle(10);
				swApp.SetUserPreferenceToggle(10, enabled);
				Debug.WriteLine("[MAKE HOLE] Input dimension on create set at app to " + enabled);
				return true;
			}
		}
		catch (Exception ex)
		{
			Debug.WriteLine("[MAKE HOLE] Input dimension app preference set failed: " + ex.Message);
		}
		try
		{
			if (model == null)
			{
				return false;
			}
			previousValue = model.GetUserPreferenceToggle(10);
			model.SetUserPreferenceToggle(10, enabled);
			Debug.WriteLine("[MAKE HOLE] Input dimension on create set at model to " + enabled);
			return true;
		}
		catch (Exception ex2)
		{
			Debug.WriteLine("[MAKE HOLE] Input dimension model preference set failed: " + ex2.Message);
			return false;
		}
	}

	private void RestoreInputDimensionOnCreate(ModelDoc2 model, bool shouldRestore, bool previousValue)
	{
		if (!shouldRestore)
		{
			return;
		}
		try
		{
			if (swApp != null)
			{
				swApp.SetUserPreferenceToggle(10, previousValue);
				Debug.WriteLine("[MAKE HOLE] Input dimension on create restored at app to " + previousValue);
				return;
			}
		}
		catch (Exception ex)
		{
			Debug.WriteLine("[MAKE HOLE] Input dimension app preference restore failed: " + ex.Message);
		}
		try
		{
			if (model != null)
			{
				model.SetUserPreferenceToggle(10, previousValue);
				Debug.WriteLine("[MAKE HOLE] Input dimension on create restored at model to " + previousValue);
			}
		}
		catch (Exception ex2)
		{
			Debug.WriteLine("[MAKE HOLE] Input dimension model preference restore failed: " + ex2.Message);
		}
	}

	private bool TrySetDimensionSystemValue(Dimension dimension, double valueMeters, string label)
	{
		if (dimension == null)
		{
			return false;
		}
		try
		{
			dimension.SystemValue = valueMeters;
			Debug.WriteLine("[MAKE HOLE] Dimension value set. label=" + label + ", value=" + (valueMeters * 1000.0).ToString("0.###", CultureInfo.InvariantCulture) + "mm");
			return true;
		}
		catch (Exception ex)
		{
			Debug.WriteLine("[MAKE HOLE] Dimension value set failed. label=" + label + ", error=" + ex.Message);
			return false;
		}
	}

	private bool TryCreateOffsetOnSurfaceAttempt(ModelDoc2 model, Face2 face, Edge edge, Feature seedFeature, MakeHoleOptions options, double offsetDistance, bool reverse, bool chain, out int pointCount)
	{
		pointCount = 0;
		model.ClearSelection2(All: true);
		model.SketchManager.Insert3DSketch(UpdateEditRebuild: true);
		bool flag = SelectEdge(edge, append: false);
		if (!flag && face != null)
		{
			SelectFace(face, append: false);
			flag = SelectEdge(edge, append: true);
		}
		if (!flag)
		{
			model.SketchManager.Insert3DSketch(UpdateEditRebuild: true);
			return false;
		}
		bool flag2 = false;
		try
		{
			flag2 = model.Extension.SketchOffsetOnSurface(offsetDistance, reverse, chain, MakeConstruct: false);
		}
		catch
		{
			flag2 = false;
		}
		if (!flag2)
		{
			Debug.WriteLine("[MAKE HOLE] Offset failed. reverse=" + reverse + ", chain=" + chain + ", distance=" + offsetDistance.ToString("0.###", CultureInfo.InvariantCulture));
			model.SketchManager.Insert3DSketch(UpdateEditRebuild: true);
			return false;
		}
		Sketch sketch = GetActiveSketch(model) ?? GetNewestSketch(model);
		List<SketchSegment> list = GetSketchSegments(sketch);
		int count = list.Count;
		if (list.Count == 0)
		{
			list = GetSelectedSketchSegments(model);
		}
		List<double[]> list2 = BuildPathPointsFromSketchSegments(list);
		List<double[]> list3 = BuildSplitPoints(list2, options);
		List<double[]> list4 = BuildDivisionPoints(list2, options);
		Debug.WriteLine("[MAKE HOLE] Offset ok. reverse=" + reverse + ", chain=" + chain + ", sketch=" + ((sketch == null) ? "null" : "ok") + ", sketchSegments=" + count + ", selectedSegments=" + (list.Count - count) + ", segments=" + list.Count + ", pathPoints=" + list2.Count + ", splitPoints=" + list3.Count + ", holePoints=" + list4.Count);
		if (list2.Count < 2 || list3.Count == 0)
		{
			model.SketchManager.Insert3DSketch(UpdateEditRebuild: true);
			pointCount = 0;
			return true;
		}
		pointCount = SplitSketchSegments(model, sketch, list, list3);
		Debug.WriteLine("[MAKE HOLE] Split result. splitCount=" + pointCount);
		Debug.WriteLine("[MAKE HOLE] Split endpoint dims=" + TryCreateSplitEndpointDimensions(model, sketch, list2, list3));
		List<SketchSegment> sketchSegments = GetSketchSegments(sketch);
		SketchPoint sketchPoint = FindNearestExistingSketchPoint(sketch, (list4.Count > 0) ? list4[0] : null);
		List<SketchPoint> list5 = ((sketchPoint == null) ? new List<SketchPoint>() : new List<SketchPoint> { sketchPoint });
		model.SketchManager.Insert3DSketch(UpdateEditRebuild: true);
		if (string.Equals(options.HoleType, "Circle", StringComparison.OrdinalIgnoreCase))
		{
			Debug.WriteLine("[MAKE HOLE] HoleWizard auto call disabled to avoid SolidWorks crash. Existing points=" + list5.Count);
			BeginHybridHoleWizard(model, face, sketch, sketchSegments, list4.Count, options);
		}
		return true;
	}

	private bool SelectEdge(Edge edge, bool append)
	{
		if (edge is Entity entity)
		{
			return entity.Select4(append, null);
		}
		try
		{
			return ((dynamic)edge).Select(append);
		}
		catch
		{
			return false;
		}
	}

	private bool SelectEdgeWithMark(Edge edge, bool append, int mark)
	{
		if (edge == null)
		{
			return false;
		}
		SelectData selectData = null;
		try
		{
			selectData = ((((swApp?.ActiveDoc is ModelDoc2 modelDoc) ? modelDoc.SelectionManager : null) is SelectionMgr selectionMgr) ? selectionMgr.CreateSelectData() : null);
			if (selectData != null)
			{
				selectData.Mark = mark;
			}
		}
		catch
		{
			selectData = null;
		}
		if (edge is Entity entity)
		{
			try
			{
				return entity.Select4(append, selectData);
			}
			catch
			{
			}
		}
		try
		{
			return ((dynamic)edge).Select(append);
		}
		catch
		{
			return false;
		}
	}

	private Sketch GetActiveSketch(ModelDoc2 model)
	{
		if (model == null)
		{
			return null;
		}
		try
		{
			if (((dynamic)model).GetActiveSketch2() is Sketch result)
			{
				return result;
			}
		}
		catch
		{
		}
		try
		{
			return ((dynamic)model.SketchManager).ActiveSketch as Sketch;
		}
		catch
		{
			return null;
		}
	}

	private Sketch GetNewestSketch(ModelDoc2 model)
	{
		if (model == null)
		{
			return null;
		}
		Sketch result = null;
		try
		{
			for (Feature feature = model.FirstFeature() as Feature; feature != null; feature = feature.GetNextFeature() as Feature)
			{
				string text = "";
				try
				{
					text = feature.GetTypeName2() ?? "";
				}
				catch
				{
				}
				if ((text.IndexOf("ProfileFeature", StringComparison.OrdinalIgnoreCase) >= 0 || text.IndexOf("3D", StringComparison.OrdinalIgnoreCase) >= 0) && feature.GetSpecificFeature2() is Sketch sketch)
				{
					result = sketch;
				}
			}
		}
		catch
		{
		}
		return result;
	}

	private Feature GetNewestSketchFeatureWithSegments(ModelDoc2 model)
	{
		if (model == null)
		{
			return null;
		}
		Feature feature = null;
		try
		{
			for (Feature feature2 = model.FirstFeature() as Feature; feature2 != null; feature2 = feature2.GetNextFeature() as Feature)
			{
				if (feature2.GetSpecificFeature2() is Sketch sketch && GetSketchSegments(sketch).Count > 0)
				{
					feature = feature2;
				}
			}
		}
		catch (Exception ex)
		{
			Debug.WriteLine("[MAKE HOLE] GetNewestSketchFeatureWithSegments failed: " + ex.Message);
		}
		Debug.WriteLine("[MAKE HOLE] Newest sketch feature with segments=" + ((feature == null) ? "null" : SafeFeatureName(feature)));
		return feature;
	}

	private Sketch GetNewestSketchWithSegments(ModelDoc2 model)
	{
		Feature newestSketchFeatureWithSegments = GetNewestSketchFeatureWithSegments(model);
		if (newestSketchFeatureWithSegments == null)
		{
			return null;
		}
		try
		{
			return newestSketchFeatureWithSegments.GetSpecificFeature2() as Sketch;
		}
		catch
		{
			return null;
		}
	}

	private List<SketchSegment> GetSelectedSketchSegments(ModelDoc2 model)
	{
		List<SketchSegment> list = new List<SketchSegment>();
		SelectionMgr selectionMgr = model?.SelectionManager as SelectionMgr;
		int num = selectionMgr?.GetSelectedObjectCount2(-1) ?? 0;
		for (int i = 1; i <= num; i++)
		{
			object selected = null;
			try
			{
				selected = selectionMgr.GetSelectedObject6(i, -1);
			}
			catch
			{
			}
			AddSketchSegmentFromObject(list, selected);
		}
		return list;
	}

	private void AddSketchSegmentFromObject(List<SketchSegment> segments, object selected)
	{
		if (segments == null || selected == null)
		{
			return;
		}
		if (selected is SketchSegment item)
		{
			segments.Add(item);
		}
		else if (selected is Feature feature)
		{
			if (feature.GetSpecificFeature2() is Sketch sketch)
			{
				segments.AddRange(GetSketchSegments(sketch));
			}
		}
		else if (selected is Sketch sketch2)
		{
			segments.AddRange(GetSketchSegments(sketch2));
		}
	}

	private List<SketchSegment> GetSketchSegments(Sketch sketch)
	{
		List<SketchSegment> list = new List<SketchSegment>();
		if (sketch == null)
		{
			return list;
		}
		try
		{
			object sketchSegments = sketch.GetSketchSegments();
			if (!(sketchSegments is Array array))
			{
				return list;
			}
			foreach (object item2 in array)
			{
				if (item2 is SketchSegment item)
				{
					list.Add(item);
				}
			}
		}
		catch
		{
		}
		return list;
	}

	private List<SketchPoint> GetSketchPoints(Sketch sketch)
	{
		List<SketchPoint> list = new List<SketchPoint>();
		if (sketch == null)
		{
			return list;
		}
		object obj = null;
		try
		{
			obj = ((dynamic)sketch).GetSketchPoints2();
		}
		catch
		{
			try
			{
				obj = ((dynamic)sketch).GetSketchPoints();
			}
			catch
			{
				obj = null;
			}
		}
		if (!(obj is Array array))
		{
			return list;
		}
		foreach (object item2 in array)
		{
			if (item2 is SketchPoint item)
			{
				list.Add(item);
			}
		}
		return list;
	}

	private SketchPoint FindNearestExistingSketchPoint(Sketch sketch, double[] target)
	{
		if (sketch == null || !IsPoint(target))
		{
			Debug.WriteLine("[MAKE HOLE] Existing point search skipped. sketch=" + ((sketch == null) ? "null" : "ok") + ", target=" + (IsPoint(target) ? "ok" : "null"));
			return null;
		}
		List<SketchPoint> sketchPoints = GetSketchPoints(sketch);
		SketchPoint sketchPoint = null;
		double num = double.MaxValue;
		foreach (SketchPoint item in sketchPoints)
		{
			double[] left = new double[3] { item.X, item.Y, item.Z };
			double num2 = Distance(left, target);
			if (num2 < num)
			{
				num = num2;
				sketchPoint = item;
			}
		}
		Debug.WriteLine("[MAKE HOLE] Existing sketch point search. points=" + sketchPoints.Count + ", found=" + ((sketchPoint == null) ? "False" : "True") + ", distance=" + ((num == double.MaxValue) ? "n/a" : ((num * 1000.0).ToString("0.###", CultureInfo.InvariantCulture) + "mm")));
		return sketchPoint;
	}

	private List<double[]> BuildPathPointsFromSketchSegments(List<SketchSegment> segments)
	{
		List<double[]> list = new List<double[]>();
		if (segments == null || segments.Count == 0)
		{
			return list;
		}
		foreach (SketchSegment segment in segments)
		{
			int num = -1;
			try
			{
				num = segment.GetType();
			}
			catch
			{
			}
			if (num == 0)
			{
				AppendPathPoints(list, GetSketchSegmentPointsFallback(segment));
				continue;
			}
			Curve curve = null;
			try
			{
				curve = segment.GetCurve() as Curve;
			}
			catch
			{
			}
			if (curve == null)
			{
				AppendPathPoints(list, GetSketchSegmentPointsFallback(segment));
				continue;
			}
			if (!TryGetCurveGeometry(curve, out var geometry))
			{
				AppendPathPoints(list, GetSketchSegmentPointsFallback(segment));
				continue;
			}
			List<double[]> add = SampleCurve(geometry, 64);
			AppendPathPoints(list, add);
		}
		return list;
	}

	private List<double[]> GetSketchSegmentPointsFallback(SketchSegment segment)
	{
		List<double[]> list = new List<double[]>();
		if (segment == null)
		{
			return list;
		}
		try
		{
			Debug.WriteLine("[MAKE HOLE] Segment fallback. type=" + segment.GetType());
		}
		catch
		{
		}
		List<double[]> list2 = TryBuildArcPoints(segment, 64);
		if (list2.Count >= 2)
		{
			return list2;
		}
		AddSketchPointObject(list, TryCall(segment, "GetStartPoint2"));
		AddSketchPointObject(list, TryCall(segment, "GetEndPoint2"));
		if (list.Count >= 2)
		{
			return list;
		}
		AddSketchPointArray(list, TryCall(segment, "GetPoints2"));
		if (list.Count >= 2)
		{
			return list;
		}
		AddSketchPointArray(list, TryCall(segment, "GetPoints"));
		return list;
	}

	private List<double[]> TryBuildArcPoints(SketchSegment segment, int sampleCount)
	{
		List<double[]> list = new List<double[]>();
		if (segment == null || sampleCount < 2)
		{
			return list;
		}
		try
		{
			int type = segment.GetType();
			if (type != 1)
			{
				return list;
			}
		}
		catch
		{
			return list;
		}
		double[] sketchPointCoordinates = GetSketchPointCoordinates(TryCall(segment, "GetStartPoint2"));
		double[] sketchPointCoordinates2 = GetSketchPointCoordinates(TryCall(segment, "GetEndPoint2"));
		double[] sketchPointCoordinates3 = GetSketchPointCoordinates(TryCall(segment, "GetCenterPoint2"));
		double[] array = Normalize(TryCall(segment, "GetNormalVector") as double[]);
		if (!IsPoint(sketchPointCoordinates) || !IsPoint(sketchPointCoordinates2) || !IsPoint(sketchPointCoordinates3) || array == null)
		{
			return list;
		}
		double[] array2 = Normalize(Subtract(sketchPointCoordinates, sketchPointCoordinates3));
		double[] array3 = Normalize(Subtract(sketchPointCoordinates2, sketchPointCoordinates3));
		if (array2 == null || array3 == null)
		{
			return list;
		}
		double[] array4 = Normalize(Cross(array, array2));
		if (array4 == null)
		{
			return list;
		}
		double num = Math.Atan2(Dot(array3, array4), Dot(array3, array2));
		int num2 = 1;
		try
		{
			num2 = Convert.ToInt32(TryCall(segment, "GetRotationDir"), CultureInfo.InvariantCulture);
		}
		catch
		{
		}
		if (num2 < 0 && num > 0.0)
		{
			num -= Math.PI * 2.0;
		}
		else if (num2 >= 0 && num < 0.0)
		{
			num += Math.PI * 2.0;
		}
		double num3 = Distance(sketchPointCoordinates, sketchPointCoordinates3);
		if (num3 <= 1E-06 || Math.Abs(num) <= 1E-06)
		{
			return list;
		}
		for (int i = 0; i < sampleCount; i++)
		{
			double num4 = (double)i / (double)(sampleCount - 1);
			double num5 = num * num4;
			double[] vector = Add(Scale(array2, Math.Cos(num5)), Scale(array4, Math.Sin(num5)));
			list.Add(Add(sketchPointCoordinates3, Scale(vector, num3)));
		}
		return list;
	}

	private double[] GetSketchPointCoordinates(object rawPoint)
	{
		if (rawPoint is SketchPoint sketchPoint)
		{
			return new double[3] { sketchPoint.X, sketchPoint.Y, sketchPoint.Z };
		}
		double[] array = rawPoint as double[];
		if (IsPoint(array))
		{
			return new double[3]
			{
				array[0],
				array[1],
				array[2]
			};
		}
		try
		{
			double num = Convert.ToDouble(((dynamic)rawPoint).X, CultureInfo.InvariantCulture);
			double num2 = Convert.ToDouble(((dynamic)rawPoint).Y, CultureInfo.InvariantCulture);
			double num3 = Convert.ToDouble(((dynamic)rawPoint).Z, CultureInfo.InvariantCulture);
			return new double[3] { num, num2, num3 };
		}
		catch
		{
			return null;
		}
	}

	private object TryCall(object target, string methodName)
	{
		if (target == null || string.IsNullOrEmpty(methodName))
		{
			return null;
		}
		try
		{
			return target.GetType().InvokeMember(methodName, BindingFlags.InvokeMethod, null, target, null);
		}
		catch
		{
			try
			{
				switch (methodName)
				{
				case "GetStartPoint2":
					return ((dynamic)target).GetStartPoint2();
				case "GetEndPoint2":
					return ((dynamic)target).GetEndPoint2();
				case "GetCenterPoint2":
					return ((dynamic)target).GetCenterPoint2();
				case "GetNormalVector":
					return ((dynamic)target).GetNormalVector();
				case "GetRotationDir":
					return ((dynamic)target).GetRotationDir();
				case "GetPoints2":
					return ((dynamic)target).GetPoints2();
				case "GetPoints":
					return ((dynamic)target).GetPoints();
				}
			}
			catch
			{
			}
		}
		return null;
	}

	private void AddSketchPointArray(List<double[]> points, object rawPoints)
	{
		if (!(rawPoints is Array array))
		{
			return;
		}
		foreach (object item in array)
		{
			AddSketchPointObject(points, item);
		}
	}

	private void AddSketchPointObject(List<double[]> points, object rawPoint)
	{
		if (points == null || rawPoint == null)
		{
			return;
		}
		if (rawPoint is SketchPoint sketchPoint)
		{
			points.Add(new double[3] { sketchPoint.X, sketchPoint.Y, sketchPoint.Z });
			return;
		}
		double[] array = rawPoint as double[];
		if (IsPoint(array))
		{
			points.Add(new double[3]
			{
				array[0],
				array[1],
				array[2]
			});
			return;
		}
		try
		{
			double num = Convert.ToDouble(((dynamic)rawPoint).X, CultureInfo.InvariantCulture);
			double num2 = Convert.ToDouble(((dynamic)rawPoint).Y, CultureInfo.InvariantCulture);
			double num3 = Convert.ToDouble(((dynamic)rawPoint).Z, CultureInfo.InvariantCulture);
			points.Add(new double[3] { num, num2, num3 });
		}
		catch
		{
		}
	}

	private bool TryGetCurveGeometry(Curve curve, out EdgeGeometry geometry)
	{
		geometry = null;
		if (curve == null)
		{
			return false;
		}
		if (!curve.GetEndParams(out var Start, out var End, out var _, out var _))
		{
			return false;
		}
		double[] array = curve.Evaluate(Start) as double[];
		double[] array2 = curve.Evaluate(End) as double[];
		if (!IsPoint(array) || !IsPoint(array2))
		{
			return false;
		}
		double[] array3 = Normalize(Subtract(array2, array));
		if (array3 == null)
		{
			return false;
		}
		geometry = new EdgeGeometry
		{
			Curve = curve,
			StartParam = Start,
			EndParam = End,
			Start = array,
			End = array2,
			Mid = GetCurvePointOrDefault(curve, (Start + End) * 0.5, Scale(Add(array, array2), 0.5)),
			Direction = array3,
			Length = Distance(array, array2)
		};
		return geometry.Length > 0.001;
	}

	private void AppendPathPoints(List<double[]> path, List<double[]> add)
	{
		if (path == null || add == null)
		{
			return;
		}
		foreach (double[] item in add)
		{
			if (IsPoint(item) && (path.Count == 0 || Distance(path[path.Count - 1], item) > 1E-06))
			{
				path.Add(item);
			}
		}
	}

	private List<double[]> BuildDivisionPoints(List<double[]> pathPoints, MakeHoleOptions options)
	{
		List<double[]> list = new List<double[]>();
		if (pathPoints == null || pathPoints.Count < 2)
		{
			return list;
		}
		double num = options.LeftOffsetMm / 1000.0;
		double num2 = options.RightOffsetMm / 1000.0;
		double num3 = options.PitchMm / 1000.0;
		if (num < 0.0 || num2 < 0.0 || num3 <= 0.0)
		{
			return list;
		}
		List<double> list2 = BuildCumulativeLengths(pathPoints);
		double num4 = list2[list2.Count - 1];
		double num5 = (lastCalculatedPatternUsableLength = num4 - num - num2);
		if (num5 <= 0.001)
		{
			lastCalculatedPatternUsableLength = 0.0;
			return list;
		}
		int num6 = Math.Max(1, (int)Math.Round(num5 / num3, MidpointRounding.AwayFromZero));
		double num7 = num5 / (double)num6;
		int val = ((num7 <= num3) ? ((int)Math.Round(num5 / num3 + 1.0, MidpointRounding.AwayFromZero)) : ((int)Math.Round(num5 / num3 + 2.0, MidpointRounding.AwayFromZero)));
		val = Math.Max(2, val);
		double num8 = ((val > 1) ? (num5 / (double)(val - 1)) : 0.0);
		lastCalculatedPatternCount = val;
		lastCalculatedPatternSpacing = num8;
		Debug.WriteLine("[MAKE HOLE] Pattern formula. curveLength=" + (num4 * 1000.0).ToString("0.###", CultureInfo.InvariantCulture) + "mm, usableLength=" + (num5 * 1000.0).ToString("0.###", CultureInfo.InvariantCulture) + "mm, maxPitch=" + (num3 * 1000.0).ToString("0.###", CultureInfo.InvariantCulture) + "mm, roundedGapCount=" + num6 + ", testPitch=" + (num7 * 1000.0).ToString("0.###", CultureInfo.InvariantCulture) + "mm, holeCount=" + val + ", actualPitch=" + (num8 * 1000.0).ToString("0.###", CultureInfo.InvariantCulture) + "mm");
		for (int i = 0; i < val; i++)
		{
			double distance = num + num8 * (double)i;
			double[] array = InterpolateByDistance(pathPoints, list2, distance);
			if (array != null)
			{
				list.Add(array);
			}
		}
		return list;
	}

	private List<double[]> BuildSplitPoints(List<double[]> pathPoints, MakeHoleOptions options)
	{
		List<double[]> list = new List<double[]>();
		if (pathPoints == null || pathPoints.Count < 2)
		{
			return list;
		}
		double num = options.LeftOffsetMm / 1000.0;
		double num2 = options.RightOffsetMm / 1000.0;
		if (num <= 0.0 || num2 <= 0.0)
		{
			return list;
		}
		List<double> list2 = BuildCumulativeLengths(pathPoints);
		double num3 = list2[list2.Count - 1];
		double num4 = num3 - num2;
		if (num3 <= 0.001 || num >= num4)
		{
			return list;
		}
		double[] array = InterpolateByDistance(pathPoints, list2, num);
		double[] array2 = InterpolateByDistance(pathPoints, list2, num4);
		if (array != null)
		{
			list.Add(array);
		}
		if (array2 != null && Distance(array2, array) > 1E-06)
		{
			list.Add(array2);
		}
		return list;
	}

	private int SplitSketchSegments(ModelDoc2 model, Sketch sketch, List<SketchSegment> segments, List<double[]> splitPoints)
	{
		int num = 0;
		if (model == null || splitPoints == null || splitPoints.Count == 0)
		{
			return num;
		}
		for (int num2 = splitPoints.Count - 1; num2 >= 0; num2--)
		{
			double[] array = splitPoints[num2];
			if (IsPoint(array))
			{
				List<SketchSegment> list = GetSketchSegments(sketch);
				if (list.Count == 0)
				{
					list = GetSelectedSketchSegments(model);
				}
				if (list.Count == 0 && segments != null)
				{
					list.AddRange(segments);
				}
				SketchSegment sketchSegment = FindNearestSketchSegment(list, array);
				if (sketchSegment == null)
				{
					Debug.WriteLine("[MAKE HOLE] Split skip. No segment near point.");
				}
				else
				{
					try
					{
						model.ClearSelection2(All: false);
						if (!sketchSegment.Select4(Append: false, null))
						{
							goto IL_017b;
						}
						object obj = model.SketchManager.SplitOpenSegment(array[0], array[1], array[2]);
						if (obj == null)
						{
							goto IL_017b;
						}
						num++;
						Debug.WriteLine("[MAKE HOLE] SplitOpenSegment ok at " + array[0].ToString("0.###", CultureInfo.InvariantCulture) + ", " + array[1].ToString("0.###", CultureInfo.InvariantCulture) + ", " + array[2].ToString("0.###", CultureInfo.InvariantCulture));
						goto end_IL_00bc;
						IL_017b:
						sketchSegment.SplitEntity(array[0], array[1], array[2], array[0], array[1], array[2]);
						num++;
						end_IL_00bc:;
					}
					catch (Exception ex)
					{
						Debug.WriteLine("[MAKE HOLE] Split failed: " + ex.Message);
					}
				}
			}
		}
		return num;
	}

	private int TryCreateSplitEndpointDimensions(ModelDoc2 model, Sketch sketch, List<double[]> pathPoints, List<double[]> splitPoints)
	{
		if (model == null || sketch == null || pathPoints == null || pathPoints.Count < 2 || splitPoints == null || splitPoints.Count < 2)
		{
			return 0;
		}
		double[] array = pathPoints[0];
		double[] array2 = pathPoints[pathPoints.Count - 1];
		double[] array3 = splitPoints[0];
		double[] array4 = splitPoints[splitPoints.Count - 1];
		if (!IsPoint(array) || !IsPoint(array2) || !IsPoint(array3) || !IsPoint(array4))
		{
			return 0;
		}
		SketchPoint first = FindNearestExistingSketchPoint(sketch, array);
		SketchPoint second = FindNearestExistingSketchPoint(sketch, array3);
		SketchPoint first2 = FindNearestExistingSketchPoint(sketch, array4);
		SketchPoint second2 = FindNearestExistingSketchPoint(sketch, array2);
		double[] pointListCenter = GetPointListCenter(pathPoints);
		int num = 0;
		if (TryCreatePointDistanceDimension(model, first, second, pointListCenter, "left split endpoint"))
		{
			num++;
		}
		if (TryCreatePointDistanceDimension(model, first2, second2, pointListCenter, "right split endpoint"))
		{
			num++;
		}
		try
		{
			model.ClearSelection2(All: true);
		}
		catch
		{
		}
		return num;
	}

	private bool TryCreatePointDistanceDimension(ModelDoc2 model, SketchPoint first, SketchPoint second, double[] pathCenter, string label)
	{
		if (model == null || first == null || second == null)
		{
			Debug.WriteLine("[MAKE HOLE] Point distance dim skip. Missing point. label=" + label);
			return false;
		}
		double[] array = new double[3] { first.X, first.Y, first.Z };
		double[] array2 = new double[3] { second.X, second.Y, second.Z };
		if (Distance(array, array2) <= 1E-06)
		{
			Debug.WriteLine("[MAKE HOLE] Point distance dim skip. Same point. label=" + label);
			return false;
		}
		try
		{
			model.ClearSelection2(All: true);
			if (!SelectSketchPoint(first, append: false) || !SelectSketchPoint(second, append: true))
			{
				Debug.WriteLine("[MAKE HOLE] Point distance dim skip. Select failed. label=" + label);
				model.ClearSelection2(All: true);
				return false;
			}
			double[] pointDistanceDimensionPosition = GetPointDistanceDimensionPosition(array, array2, pathCenter);
			bool previousValue;
			bool shouldRestore = TrySetInputDimensionOnCreate(model, enabled: false, out previousValue);
			object obj = null;
			try
			{
				obj = model.AddDimension2(pointDistanceDimensionPosition[0], pointDistanceDimensionPosition[1], pointDistanceDimensionPosition[2]);
			}
			finally
			{
				RestoreInputDimensionOnCreate(model, shouldRestore, previousValue);
			}
			DisplayDimension displayDimension = obj as DisplayDimension;
			Dimension dimension = null;
			if (displayDimension != null)
			{
				try
				{
					dimension = displayDimension.GetDimension2(0);
				}
				catch
				{
				}
			}
			double num = 0.0;
			try
			{
				num = ((dimension == null) ? (Distance(array, array2) * 1000.0) : (dimension.SystemValue * 1000.0));
			}
			catch
			{
				num = Distance(array, array2) * 1000.0;
			}
			bool result = obj != null;
			Debug.WriteLine("[MAKE HOLE] Point distance dim created. ok=" + result + ", label=" + label + ", value=" + num.ToString("0.###", CultureInfo.InvariantCulture) + "mm");
			model.ClearSelection2(All: true);
			return result;
		}
		catch (Exception ex)
		{
			Debug.WriteLine("[MAKE HOLE] Point distance dim failed. label=" + label + ", error=" + ex.Message);
			try
			{
				model.ClearSelection2(All: true);
			}
			catch
			{
			}
			return false;
		}
	}

	private double[] GetPointDistanceDimensionPosition(double[] first, double[] second, double[] pathCenter)
	{
		double[] left = Scale(Add(first, second), 0.5);
		double[] array = Normalize(Subtract(second, first));
		double[] array2 = ((array == null) ? null : Normalize(Cross(array, new double[3] { 0.0, 0.0, 1.0 })));
		if (array2 == null)
		{
			array2 = ((array != null) ? Normalize(Cross(array, new double[3] { 0.0, 1.0, 0.0 })) : new double[3] { 0.0, 0.01, 0.0 });
		}
		if (array2 == null)
		{
			array2 = new double[3] { 0.0, 0.01, 0.0 };
		}
		if (IsPoint(pathCenter))
		{
			double[] right = Subtract(left, pathCenter);
			if (Dot(array2, right) < 0.0)
			{
				array2 = Scale(array2, -1.0);
			}
		}
		return Add(left, Scale(array2, 0.01));
	}

	private double[] GetPointListCenter(List<double[]> points)
	{
		if (points == null || points.Count == 0)
		{
			return null;
		}
		double[] array = new double[3];
		int num = 0;
		foreach (double[] point in points)
		{
			if (IsPoint(point))
			{
				array = Add(array, point);
				num++;
			}
		}
		return (num == 0) ? null : Scale(array, 1.0 / (double)num);
	}

	private List<SketchPoint> CreateHoleSketchPoints(ModelDoc2 model, List<double[]> holePoints)
	{
		List<SketchPoint> list = new List<SketchPoint>();
		if (model == null || holePoints == null)
		{
			return list;
		}
		foreach (double[] holePoint in holePoints)
		{
			if (!IsPoint(holePoint))
			{
				continue;
			}
			try
			{
				SketchPoint sketchPoint = model.SketchManager.CreatePoint(holePoint[0], holePoint[1], holePoint[2]);
				if (sketchPoint != null)
				{
					list.Add(sketchPoint);
				}
			}
			catch (Exception ex)
			{
				Debug.WriteLine("[MAKE HOLE] Create hole sketch point failed: " + ex.Message);
			}
		}
		Debug.WriteLine("[MAKE HOLE] Created hole sketch points=" + list.Count);
		return list;
	}

	private List<SketchPoint> CreateSeedHoleSketchPoint(ModelDoc2 model, List<double[]> holePoints)
	{
		List<SketchPoint> list = new List<SketchPoint>();
		if (model == null || holePoints == null || holePoints.Count == 0)
		{
			return list;
		}
		double[] array = holePoints[0];
		if (!IsPoint(array))
		{
			return list;
		}
		try
		{
			SketchPoint sketchPoint = model.SketchManager.CreatePoint(array[0], array[1], array[2]);
			if (sketchPoint != null)
			{
				list.Add(sketchPoint);
			}
		}
		catch (Exception ex)
		{
			Debug.WriteLine("[MAKE HOLE] Create seed hole sketch point failed: " + ex.Message);
		}
		Debug.WriteLine("[MAKE HOLE] Created seed hole sketch point=" + list.Count + ", plannedPatternCount=" + holePoints.Count);
		return list;
	}

	private void StorePendingHoleWizardSeedPoint(List<double[]> holePoints)
	{
		pendingHoleWizardSeedPoint = null;
		if (holePoints == null || holePoints.Count == 0)
		{
			Debug.WriteLine("[MAKE HOLE] No pending HoleWizard seed point.");
			return;
		}
		double[] array = holePoints[0];
		if (!IsPoint(array))
		{
			Debug.WriteLine("[MAKE HOLE] Pending HoleWizard seed point is invalid.");
			return;
		}
		pendingHoleWizardSeedPoint = new double[3]
		{
			array[0],
			array[1],
			array[2]
		};
		Debug.WriteLine("[MAKE HOLE] Stored pending HoleWizard seed point. plannedPatternCount=" + holePoints.Count + ", x=" + (array[0] * 1000.0).ToString("0.###", CultureInfo.InvariantCulture) + "mm, y=" + (array[1] * 1000.0).ToString("0.###", CultureInfo.InvariantCulture) + "mm, z=" + (array[2] * 1000.0).ToString("0.###", CultureInfo.InvariantCulture) + "mm");
	}

	private bool TryPlacePendingHoleWizardPoint(ModelDoc2 model)
	{
		if (model == null || pendingHoleWizardSeedPoint == null)
		{
			return false;
		}
		double[] array = pendingHoleWizardSeedPoint;
		Debug.WriteLine("[MAKE HOLE] Skip CreatePoint in HoleWizard PM to avoid SolidWorks AccessViolation. Pending point=" + (array[0] * 1000.0).ToString("0.###", CultureInfo.InvariantCulture) + ", " + (array[1] * 1000.0).ToString("0.###", CultureInfo.InvariantCulture) + ", " + (array[2] * 1000.0).ToString("0.###", CultureInfo.InvariantCulture) + "mm");
		return false;
	}

	private Feature TryCreateCircleHoleWizard(ModelDoc2 model, Face2 face, List<SketchPoint> holePoints, MakeHoleOptions options)
	{
		if (model == null || face == null || holePoints == null || holePoints.Count == 0 || options == null)
		{
			Debug.WriteLine("[MAKE HOLE] Skip Hole Wizard. Missing face or hole points.");
			return null;
		}
		model.ClearSelection2(All: true);
		bool flag = SelectFace(face, append: false);
		int num = 0;
		foreach (SketchPoint holePoint in holePoints)
		{
			if (SelectSketchPoint(holePoint, append: true))
			{
				num++;
			}
		}
		Debug.WriteLine("[MAKE HOLE] HoleWizard selection. face=" + flag + ", points=" + num);
		if (!flag || num == 0)
		{
			return null;
		}
		double diameter = options.DiameterMm / 1000.0;
		Debug.WriteLine("[MAKE HOLE] Skip HW2024 CreateDefinition path. InitializeHole causes SolidWorks AccessViolation on this case.");
		Feature feature = TryCallHoleWizard5(model, 25, diameter);
		try
		{
			if (feature != null)
			{
				feature.Name = "TAI_MAKE_HOLE_CIRCLE_" + options.DiameterMm.ToString("0.###", CultureInfo.InvariantCulture);
				Debug.WriteLine("[MAKE HOLE] HoleWizard created: " + feature.Name);
			}
			else
			{
				Debug.WriteLine("[MAKE HOLE] HoleWizard returned null with face+point. Try face-only diagnostic.");
				model.ClearSelection2(All: true);
				if (SelectFace(face, append: false))
				{
					Feature feature2 = TryCallHoleWizard5(model, 25, diameter);
					if (feature2 != null)
					{
						feature2.Name = "TAI_MAKE_HOLE_FACE_ONLY_TEST";
						Debug.WriteLine("[MAKE HOLE] HoleWizard face-only diagnostic created. Position point selection is the issue.");
						return feature2;
					}
				}
				Debug.WriteLine("[MAKE HOLE] HoleWizard face-only diagnostic also returned null.");
				TryStartHoleWizardCommand(model, face, holePoints);
			}
			return feature;
		}
		catch (Exception ex)
		{
			Debug.WriteLine("[MAKE HOLE] HoleWizard failed: " + ex.Message);
			return null;
		}
	}

	private Feature TryCreateHoleWizardDefinition2024(ModelDoc2 model, Face2 face, SketchPoint point, MakeHoleOptions options)
	{
		if (model == null || face == null || point == null || options == null)
		{
			return null;
		}
		IWizardHoleFeatureData2 data = null;
		try
		{
			object obj = model.FeatureManager.CreateDefinition(25);
			data = obj as IWizardHoleFeatureData2;
			Debug.WriteLine("[MAKE HOLE] HW2024 CreateDefinition. data=" + ((data == null) ? "null" : "ok"));
			if (data == null)
			{
				return null;
			}
		}
		catch (Exception ex)
		{
			Debug.WriteLine("[MAKE HOLE] HW2024 CreateDefinition failed: " + ex.Message);
			return null;
		}
		double diameter = options.DiameterMm / 1000.0;
		try
		{
			data.InitializeHole(25, 1, 39, "", 1);
			Debug.WriteLine("[MAKE HOLE] HW2024 InitializeHole ok.");
		}
		catch (Exception ex2)
		{
			Debug.WriteLine("[MAKE HOLE] HW2024 InitializeHole failed: " + ex2.Message);
		}
		TrySetHoleWizardValue("Face", delegate
		{
			data.Face = face;
		});
		TrySetHoleWizardValue("IFace", delegate
		{
			data.IFace = face;
		});
		TrySetHoleWizardValue("IVertex", delegate
		{
			((dynamic)data).IVertex = point;
		});
		TrySetHoleWizardValue("Type", delegate
		{
			data.Type = 25;
		});
		TrySetHoleWizardValue("Standard", delegate
		{
			data.Standard = "Ansi Metric";
		});
		TrySetHoleWizardValue("Standard2", delegate
		{
			data.Standard2 = 1;
		});
		TrySetHoleWizardValue("FastenerType", delegate
		{
			data.FastenerType = "";
		});
		TrySetHoleWizardValue("FastenerType2", delegate
		{
			data.FastenerType2 = 39;
		});
		TrySetHoleWizardValue("EndCondition", delegate
		{
			data.EndCondition = 1;
		});
		TrySetHoleWizardValue("Diameter", delegate
		{
			data.Diameter = diameter;
		});
		TrySetHoleWizardValue("HoleDiameter", delegate
		{
			data.HoleDiameter = diameter;
		});
		TrySetHoleWizardValue("ThruHoleDiameter", delegate
		{
			data.ThruHoleDiameter = diameter;
		});
		TrySetHoleWizardValue("Depth", delegate
		{
			data.Depth = 0.01;
		});
		TrySetHoleWizardValue("HoleDepth", delegate
		{
			data.HoleDepth = 0.01;
		});
		TrySetHoleWizardValue("ThruHoleDepth", delegate
		{
			data.ThruHoleDepth = 0.01;
		});
		try
		{
			Feature feature = model.FeatureManager.CreateFeature(data);
			Debug.WriteLine("[MAKE HOLE] HW2024 CreateFeature. feature=" + ((feature == null) ? "null" : "ok"));
			if (feature != null)
			{
				feature.Name = "TAI_MAKE_HOLE_HW_" + options.DiameterMm.ToString("0.###", CultureInfo.InvariantCulture);
				return feature;
			}
		}
		catch (Exception ex3)
		{
			Debug.WriteLine("[MAKE HOLE] HW2024 CreateFeature failed: " + ex3.Message);
		}
		return null;
	}

	private void TrySetHoleWizardValue(string name, Action setter)
	{
		try
		{
			setter();
			Debug.WriteLine("[MAKE HOLE] HW2024 set " + name + " ok.");
		}
		catch (Exception ex)
		{
			Debug.WriteLine("[MAKE HOLE] HW2024 set " + name + " failed: " + ex.Message);
		}
	}

	private bool TryStartHoleWizardCommand(ModelDoc2 model, Face2 face, List<SketchPoint> holePoints)
	{
		if (model == null)
		{
			return false;
		}
		try
		{
			model.ClearSelection2(All: true);
			bool flag = false;
			int num = 0;
			bool result = (holeWizardCommandStarted = model.Extension.RunCommand(39, "Hole Wizard"));
			Debug.WriteLine("[MAKE HOLE] RunCommand HoleWizard 3D-friendly. started=" + result + ", face=" + flag + ", points=" + num);
			return result;
		}
		catch (Exception ex)
		{
			Debug.WriteLine("[MAKE HOLE] RunCommand HoleWizard failed: " + ex.Message);
			return false;
		}
	}

	private void BeginHybridHoleWizard(ModelDoc2 model, Face2 face, Sketch activeSketch, List<SketchSegment> patternSegments, int holePointCount, MakeHoleOptions options, bool useDirectMeasuredLengthExpression = false)
	{
		if (model != null && face != null && options != null)
		{
			Feature sketchFeature = GetSketchFeature(model, activeSketch);
			if (sketchFeature == null || string.IsNullOrWhiteSpace(SafeFeatureName(sketchFeature)))
			{
				Debug.WriteLine("[MAKE HOLE] Hybrid start failed. Cannot resolve the feature that owns the active offset sketch.");
				MessageBox.Show("Khong xac dinh duoc sketch offset vua tao. Da dung lenh de tranh chon nham duong pattern.", "Make Hole", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
				return;
			}
			pendingHybridPattern = true;
			pendingPatternCount = ((lastCalculatedPatternCount > 1) ? lastCalculatedPatternCount : Math.Max(2, holePointCount));
			pendingPatternSpacing = ((lastCalculatedPatternSpacing > 1E-06) ? lastCalculatedPatternSpacing : Math.Max(0.001, options.PitchMm / 1000.0));
			pendingPatternSketchName = SafeFeatureName(sketchFeature);
			pendingPatternMaxPitchMm = Math.Max(0.001, options.PitchMm);
			pendingPatternLengthIsExpression = false;
			pendingPatternLengthDimensionName = (useDirectMeasuredLengthExpression ? TryCreateMeasuredLengthExpression() : TryCreatePatternLengthReference(model, patternSegments));
			featureNamesBeforeHoleWizard = CollectFeatureNames(model);
			Debug.WriteLine("[MAKE HOLE] Hybrid pending. count=" + pendingPatternCount + ", spacing=" + (pendingPatternSpacing * 1000.0).ToString("0.###", CultureInfo.InvariantCulture) + "mm, sketch=" + pendingPatternSketchName + ", lengthDim=" + pendingPatternLengthDimensionName + ", featureCountBefore=" + ((featureNamesBeforeHoleWizard != null) ? featureNamesBeforeHoleWizard.Count : 0));
			Debug.WriteLine("[MAKE HOLE] Hybrid open HoleWizard. started=" + TryStartHoleWizardCommand(model, face, null));
		}
	}

	private string TryCreateCurveLengthDimension(ModelDoc2 model, List<SketchSegment> segments)
	{
		if (model == null || segments == null || segments.Count == 0)
		{
			return null;
		}
		SketchSegment sketchSegment = FindLongestSketchSegment(segments);
		if (sketchSegment == null)
		{
			return null;
		}
		try
		{
			List<double[]> list = BuildSegmentSamplePoints(sketchSegment);
			if (list == null || list.Count < 2)
			{
				list = GetSketchSegmentPointsFallback(sketchSegment);
			}
			if (list == null || list.Count < 2)
			{
				Debug.WriteLine("[MAKE HOLE] Length dimension skip. No segment points.");
				return null;
			}
			double[] array = list[Math.Max(0, list.Count / 2)];
			if (!IsPoint(array))
			{
				array = Scale(Add(list[0], list[list.Count - 1]), 0.5);
			}
			model.ClearSelection2(All: true);
			if (!SelectSketchSegment(sketchSegment, append: false))
			{
				Debug.WriteLine("[MAKE HOLE] Length dimension skip. Select segment failed.");
				return null;
			}
			dynamic val = model.AddDimension2(array[0], array[1], array[2]);
			DisplayDimension displayDimension = val as DisplayDimension;
			Dimension dimension = null;
			if (displayDimension != null)
			{
				try
				{
					dimension = displayDimension.GetDimension2(0);
				}
				catch
				{
				}
			}
			if (dimension == null && (object)val != null)
			{
				try
				{
					dimension = val.GetDimension2(0) as Dimension;
				}
				catch
				{
				}
			}
			string equationDimensionName = GetEquationDimensionName(dimension);
			double dimensionMm = 0.0;
			try
			{
				dimensionMm = ((dimension == null) ? 0.0 : (dimension.SystemValue * 1000.0));
			}
			catch
			{
			}
			double expectedMm = ((lastCalculatedPatternUsableLength > 1E-06) ? (lastCalculatedPatternUsableLength * 1000.0) : 0.0);
			if (IsBadCurveLengthDimension(equationDimensionName, dimensionMm, expectedMm))
			{
				Debug.WriteLine("[MAKE HOLE] Length dimension rejected. name=" + equationDimensionName + ", value=" + dimensionMm.ToString("0.###", CultureInfo.InvariantCulture) + "mm, expected=" + expectedMm.ToString("0.###", CultureInfo.InvariantCulture) + "mm");
				TryDeleteDisplayDimension(model, displayDimension);
				model.ClearSelection2(All: true);
				return null;
			}
			Debug.WriteLine("[MAKE HOLE] Length dimension created. name=" + equationDimensionName + ", value=" + dimensionMm.ToString("0.###", CultureInfo.InvariantCulture) + "mm");
			model.ClearSelection2(All: true);
			return equationDimensionName;
		}
		catch (Exception ex)
		{
			Debug.WriteLine("[MAKE HOLE] Length dimension create failed: " + ex.Message);
			try
			{
				model.ClearSelection2(All: true);
			}
			catch
			{
			}
			return null;
		}
	}

	private string GetEquationDimensionName(Dimension dimension)
	{
		if (dimension == null)
		{
			return null;
		}
		try
		{
			string text = ((dynamic)dimension).GetNameForSelection();
			if (!string.IsNullOrWhiteSpace(text))
			{
				return text;
			}
		}
		catch
		{
		}
		try
		{
			string text2 = ((dynamic)dimension).FullName;
			if (!string.IsNullOrWhiteSpace(text2))
			{
				return text2;
			}
		}
		catch
		{
		}
		try
		{
			string name = dimension.Name;
			if (!string.IsNullOrWhiteSpace(name))
			{
				return name;
			}
		}
		catch
		{
		}
		return null;
	}

	private bool TryRunPendingHybridPattern(ModelDoc2 model)
	{
		if (model == null || !pendingHybridPattern)
		{
			return false;
		}
		Feature feature = FindNewHoleWizardFeature(model);
		if (feature == null)
		{
			Debug.WriteLine("[MAKE HOLE] Hybrid pattern skip. New HoleWizard feature not found.");
			return false;
		}
		TrackHoleFeature(feature);
		Sketch sketch = FindSketchByName(model, pendingPatternSketchName);
		List<SketchSegment> usableSketchSegments = GetUsableSketchSegments(sketch);
		Debug.WriteLine("[MAKE HOLE] Hybrid pattern data. seed=" + SafeFeatureName(feature) + ", sketch=" + pendingPatternSketchName + ", resolvedSketch=" + ((sketch == null) ? "null" : "ok") + ", usableSegments=" + usableSketchSegments.Count + ", count=" + pendingPatternCount);
		if (usableSketchSegments.Count == 0)
		{
			MessageBox.Show("Khong tim thay sketch curve de pattern. Hay giu sketch offset trong Feature Tree hoac chon lai edge va tao lai Make Hole.", "Make Hole", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
			return false;
		}
		Feature feature2 = TryCreateSeedCurvePattern(model, feature, usableSketchSegments, pendingPatternCount, new MakeHoleOptions
		{
			PitchMm = pendingPatternSpacing * 1000.0
		});
		if (feature2 == null)
		{
			return false;
		}
		ResetPendingMakeHole();
		Debug.WriteLine("[MAKE HOLE] Hybrid pattern completed.");
		MessageBox.Show("Da pattern Hole Wizard vua tao.", "Make Hole", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
		return true;
	}

	private void TrackHoleFeature(Feature holeFeature)
	{
		string value = SafeFeatureName(holeFeature);
		if (!string.IsNullOrWhiteSpace(value))
		{
			trackedHoleFeatureName = value;
			Debug.WriteLine("[MAKE HOLE] Track hole feature. holeFeature=" + trackedHoleFeatureName);
		}
	}

	private void ResetPendingMakeHole(bool clearPatternEquation = true)
	{
		pendingHybridPattern = false;
		pendingPatternCount = 0;
		pendingPatternSpacing = 0.0;
		lastCalculatedPatternCount = 0;
		lastCalculatedPatternSpacing = 0.0;
		pendingPatternSketchName = null;
		featureNamesBeforeHoleWizard = null;
		pendingHoleWizardSeedPoint = null;
		holeWizardCommandStarted = false;
		if (clearPatternEquation)
		{
			StopPendingPatternEquationMonitor();
			pendingPatternEquation = false;
			pendingPatternLengthDimensionName = null;
			pendingPatternLengthIsExpression = false;
			pendingPatternMaxPitchMm = 0.0;
			featureNamesBeforeCurvePattern = null;
		}
		Debug.WriteLine("[MAKE HOLE] Pending command reset.");
	}

	private HashSet<string> CollectFeatureNames(ModelDoc2 model)
	{
		HashSet<string> hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		try
		{
			for (Feature feature = model.FirstFeature() as Feature; feature != null; feature = feature.GetNextFeature() as Feature)
			{
				string text = SafeFeatureName(feature);
				if (!string.IsNullOrEmpty(text))
				{
					hashSet.Add(text);
				}
			}
		}
		catch (Exception ex)
		{
			Debug.WriteLine("[MAKE HOLE] CollectFeatureNames failed: " + ex.Message);
		}
		return hashSet;
	}

	private Feature FindNewHoleWizardFeature(ModelDoc2 model)
	{
		Feature feature = null;
		try
		{
			for (Feature feature2 = model.FirstFeature() as Feature; feature2 != null; feature2 = feature2.GetNextFeature() as Feature)
			{
				string text = SafeFeatureName(feature2);
				string text2 = "";
				try
				{
					text2 = feature2.GetTypeName2() ?? "";
				}
				catch
				{
				}
				bool flag = featureNamesBeforeHoleWizard == null || string.IsNullOrEmpty(text) || !featureNamesBeforeHoleWizard.Contains(text);
				bool flag2 = text2.IndexOf("Hole", StringComparison.OrdinalIgnoreCase) >= 0 || text2.IndexOf("Wzd", StringComparison.OrdinalIgnoreCase) >= 0 || text.IndexOf("鬯ｯ・ｯ繝ｻ・ｯ郢晢ｽｻ繝ｻ・ｯ驛｢譎｢・ｽ・ｻ郢晢ｽｻ繝ｻ・ｯ鬩幢ｽ｢隴趣ｽ｢繝ｻ・ｽ繝ｻ・ｻ驛｢譎｢・ｽ・ｻ郢晢ｽｻ繝ｻ・ｯ鬯ｩ蟷｢・ｽ・｢髫ｴ雜｣・ｽ・｢郢晢ｽｻ繝ｻ・ｽ郢晢ｽｻ繝ｻ・ｻ鬩幢ｽ｢隴趣ｽ｢繝ｻ・ｽ繝ｻ・ｻ驛｢譎｢・ｽ・ｻ郢晢ｽｻ繝ｻ・ｩ鬯ｯ・ｯ繝ｻ・ｮ郢晢ｽｻ繝ｻ・ｯ鬮ｯ・ｷ髣鯉ｽｨ繝ｻ・ｽ繝ｻ・ｷ驛｢譎｢・ｽ・ｻ郢晢ｽｻ繝ｻ・ｽ驛｢譎｢・ｽ・ｻ郢晢ｽｻ繝ｻ・ｹ鬯ｯ・ｩ陝ｷ・｢繝ｻ・ｽ繝ｻ・｢鬮ｫ・ｴ髮懶ｽ｣繝ｻ・ｽ繝ｻ・｢驛｢譎｢・ｽ・ｻ郢晢ｽｻ繝ｻ・ｽ驛｢譎｢・ｽ・ｻ郢晢ｽｻ繝ｻ・ｻ鬯ｩ蟷｢・ｽ・｢髫ｴ雜｣・ｽ・｢郢晢ｽｻ繝ｻ・ｽ郢晢ｽｻ繝ｻ・ｻ鬩幢ｽ｢隴趣ｽ｢繝ｻ・ｽ繝ｻ・ｻ驛｢譎｢・ｽ・ｻ郢晢ｽｻ繝ｻ・ｽ鬯ｩ蟷｢・ｽ・｢髫ｴ雜｣・ｽ・｢郢晢ｽｻ繝ｻ・ｽ郢晢ｽｻ繝ｻ・ｻ鬩幢ｽ｢隴趣ｽ｢繝ｻ・ｽ繝ｻ・ｻ驛｢譎｢・ｽ・ｻ郢晢ｽｻ繝ｻ・ｽ鬯ｯ・ｩ陝ｷ・｢繝ｻ・ｽ繝ｻ・｢鬮ｫ・ｴ髮懶ｽ｣繝ｻ・ｽ繝ｻ・｢驛｢譎｢・ｽ・ｻ郢晢ｽｻ繝ｻ・ｽ驛｢譎｢・ｽ・ｻ郢晢ｽｻ繝ｻ・ｻ鬯ｩ蟷｢・ｽ・｢髫ｴ雜｣・ｽ・｢郢晢ｽｻ繝ｻ・ｽ郢晢ｽｻ繝ｻ・ｻ鬩幢ｽ｢隴趣ｽ｢繝ｻ・ｽ繝ｻ・ｻ驛｢譎｢・ｽ・ｻ郢晢ｽｻ繝ｻ・ｨ鬯ｯ・ｯ繝ｻ・ｯ郢晢ｽｻ繝ｻ・ｩ鬮ｯ譎｢・ｽ・ｷ郢晢ｽｻ繝ｻ・｢驛｢譎｢・ｽ・ｻ郢晢ｽｻ繝ｻ・ｽ驛｢譎｢・ｽ・ｻ郢晢ｽｻ繝ｻ・｢鬯ｯ・ｮ繝ｻ・ｫ郢晢ｽｻ繝ｻ・ｴ鬯ｮ・ｮ隲幢ｽｶ繝ｻ・ｽ繝ｻ・｣驛｢譎｢・ｽ・ｻ郢晢ｽｻ繝ｻ・ｽ驛｢譎｢・ｽ・ｻ郢晢ｽｻ繝ｻ・｢鬯ｩ蟷｢・ｽ・｢髫ｴ雜｣・ｽ・｢郢晢ｽｻ繝ｻ・ｽ郢晢ｽｻ繝ｻ・ｻ鬩幢ｽ｢隴趣ｽ｢繝ｻ・ｽ繝ｻ・ｻ驛｢譎｢・ｽ・ｻ郢晢ｽｻ繝ｻ・ｽ鬯ｩ蟷｢・ｽ・｢髫ｴ雜｣・ｽ・｢郢晢ｽｻ繝ｻ・ｽ郢晢ｽｻ繝ｻ・ｻ鬩幢ｽ｢隴趣ｽ｢繝ｻ・ｽ繝ｻ・ｻ驛｢譎｢・ｽ・ｻ郢晢ｽｻ繝ｻ・ｻ鬯ｯ・ｯ繝ｻ・ｩ髯晢ｽｷ繝ｻ・｢郢晢ｽｻ繝ｻ・ｽ郢晢ｽｻ繝ｻ・｢鬯ｮ・ｫ繝ｻ・ｴ鬮ｮ諛ｶ・ｽ・｣郢晢ｽｻ繝ｻ・ｽ郢晢ｽｻ繝ｻ・｢鬩幢ｽ｢隴趣ｽ｢繝ｻ・ｽ繝ｻ・ｻ驛｢譎｢・ｽ・ｻ郢晢ｽｻ繝ｻ・ｽ鬩幢ｽ｢隴趣ｽ｢繝ｻ・ｽ繝ｻ・ｻ驛｢譎｢・ｽ・ｻ郢晢ｽｻ繝ｻ・ｻ鬯ｯ・ｩ陝ｷ・｢繝ｻ・ｽ繝ｻ・｢鬮ｫ・ｴ髮懶ｽ｣繝ｻ・ｽ繝ｻ・｢驛｢譎｢・ｽ・ｻ郢晢ｽｻ繝ｻ・ｽ驛｢譎｢・ｽ・ｻ郢晢ｽｻ繝ｻ・ｻ鬯ｩ蟷｢・ｽ・｢髫ｴ雜｣・ｽ・｢郢晢ｽｻ繝ｻ・ｽ郢晢ｽｻ繝ｻ・ｻ鬩幢ｽ｢隴趣ｽ｢繝ｻ・ｽ繝ｻ・ｻ驛｢譎｢・ｽ・ｻ郢晢ｽｻ繝ｻ・ｴ", StringComparison.OrdinalIgnoreCase) >= 0 || text.IndexOf("Hole", StringComparison.OrdinalIgnoreCase) >= 0;
				if (flag)
				{
					Debug.WriteLine("[MAKE HOLE] New feature candidate. name=" + text + ", type=" + text2 + ", hole=" + flag2);
				}
				if (flag && flag2)
				{
					feature = feature2;
				}
			}
		}
		catch (Exception ex)
		{
			Debug.WriteLine("[MAKE HOLE] FindNewHoleWizardFeature failed: " + ex.Message);
		}
		Debug.WriteLine("[MAKE HOLE] FindNewHoleWizardFeature result=" + SafeFeatureName(feature));
		return feature;
	}

	private Feature GetSketchFeature(ModelDoc2 model, Sketch sketch)
	{
		if (sketch == null)
		{
			return null;
		}
		try
		{
			return ((dynamic)sketch).GetFeature() as Feature;
		}
		catch
		{
		}
		if (model == null)
		{
			return null;
		}
		try
		{
			for (Feature feature = model.FirstFeature() as Feature; feature != null; feature = feature.GetNextFeature() as Feature)
			{
				Sketch sketch2 = null;
				try
				{
					sketch2 = feature.GetSpecificFeature2() as Sketch;
				}
				catch
				{
				}
				if (sketch2 != null && IsSameComObject(sketch2, sketch))
				{
					Debug.WriteLine("[MAKE HOLE] Resolved active sketch feature=" + SafeFeatureName(feature));
					return feature;
				}
			}
		}
		catch (Exception ex)
		{
			Debug.WriteLine("[MAKE HOLE] Resolve active sketch feature failed: " + ex.Message);
		}
		return null;
	}

	private bool IsSameComObject(object first, object second)
	{
		if (first == null || second == null)
		{
			return false;
		}
		if (first == second)
		{
			return true;
		}
		IntPtr intPtr = IntPtr.Zero;
		IntPtr intPtr2 = IntPtr.Zero;
		try
		{
			intPtr = Marshal.GetIUnknownForObject(first);
			intPtr2 = Marshal.GetIUnknownForObject(second);
			return intPtr == intPtr2;
		}
		catch
		{
			return false;
		}
		finally
		{
			if (intPtr != IntPtr.Zero)
			{
				Marshal.Release(intPtr);
			}
			if (intPtr2 != IntPtr.Zero)
			{
				Marshal.Release(intPtr2);
			}
		}
	}

	private Sketch FindSketchByName(ModelDoc2 model, string sketchName)
	{
		if (model == null || string.IsNullOrWhiteSpace(sketchName))
		{
			return null;
		}
		try
		{
			for (Feature feature = model.FirstFeature() as Feature; feature != null; feature = feature.GetNextFeature() as Feature)
			{
				if (string.Equals(SafeFeatureName(feature), sketchName, StringComparison.OrdinalIgnoreCase))
				{
					return feature.GetSpecificFeature2() as Sketch;
				}
			}
		}
		catch
		{
		}
		return null;
	}

	private Feature TryCreateSeedCurvePattern(ModelDoc2 model, Feature seedFeature, List<SketchSegment> segments, int count, MakeHoleOptions options)
	{
		if (model == null)
		{
			return null;
		}
		if (seedFeature == null)
		{
			Debug.WriteLine("[MAKE HOLE] Curve pattern skip. No seed feature selected.");
			return null;
		}
		trackedHoleFeatureName = SafeFeatureName(seedFeature);
		Debug.WriteLine("[MAKE HOLE] Track seed Hole Wizard feature=" + trackedHoleFeatureName);
		SketchSegment sketchSegment = FindLongestSketchSegment(segments);
		if (sketchSegment == null)
		{
			Debug.WriteLine("[MAKE HOLE] Curve pattern skip. No curve segment.");
			return null;
		}
		int patternCount = Math.Max(2, count);
		double num = Math.Max(0.001, options.PitchMm / 1000.0);
		try
		{
			model.ClearSelection2(All: true);
			bool flag = seedFeature.Select2(Append: false, 0);
			bool flag2 = SelectSketchSegment(sketchSegment, append: true);
			Debug.WriteLine("[MAKE HOLE] Curve pattern selection. seed=" + flag + ", curve=" + flag2 + ", count=" + patternCount + ", spacing=" + (num * 1000.0).ToString("0.###", CultureInfo.InvariantCulture) + "mm");
			if (!flag || !flag2)
			{
				return null;
			}
			Debug.WriteLine("[MAKE HOLE] Curve pattern definition path disabled. PatternFeatureArray setter causes SolidWorks AccessViolation.");
			Feature feature = TryCallLocalCurvePattern(model, seedFeature, sketchSegment, patternCount, num, 0, 0);
			if (feature == null)
			{
				feature = TryCallLocalCurvePattern(model, seedFeature, sketchSegment, patternCount, num, 4, 1);
			}
			if (feature == null)
			{
				PreparePendingCurvePatternEquation(model);
				if (TryOpenCurveDrivenPatternCommand(model, seedFeature, sketchSegment))
				{
					curvePatternCommandStarted = true;
					StartPendingPatternEquationMonitor();
					ResetPendingMakeHole(clearPatternEquation: false);
					Debug.WriteLine("[MAKE HOLE] Curve pattern opened by RunCommand fallback.");
				}
			}
			Debug.WriteLine("[MAKE HOLE] Curve pattern result. feature=" + ((feature == null) ? "null" : "ok"));
			if (feature != null)
			{
				feature.Name = "TAI_MAKE_HOLE_PATTERN";
				TryApplyCurvePatternEquation(model, feature);
			}
			return feature;
		}
		catch (Exception ex)
		{
			Debug.WriteLine("[MAKE HOLE] Curve pattern failed: " + ex.Message);
			return null;
		}
	}

	private Feature TryCallLocalCurvePattern(ModelDoc2 model, Feature seedFeature, SketchSegment curveSegment, int patternCount, double spacing, int seedMark, int curveMark)
	{
		try
		{
			model.ClearSelection2(All: true);
			bool flag = SelectFeatureWithMark(seedFeature, append: false, seedMark);
			bool flag2 = SelectSketchSegmentWithMark(curveSegment, append: true, curveMark);
			Debug.WriteLine("[MAKE HOLE] Curve pattern call selection. seedMark=" + seedMark + ", curveMark=" + curveMark + ", seed=" + flag + ", curve=" + flag2);
			if (!flag || !flag2)
			{
				return null;
			}
			Feature feature = model.FeatureManager.FeatureLocalCurveDrivenPattern(FlipDir1: false, patternCount, EqualSpacing1: true, spacing, 0, 0, 0, Direction2: false, FlipDir2: false, 1, EqualSpacing2: false, 0.0, PatternSeedOnly: false);
			Debug.WriteLine("[MAKE HOLE] Curve pattern call result. seedMark=" + seedMark + ", curveMark=" + curveMark + ", feature=" + ((feature == null) ? "null" : "ok"));
			return feature;
		}
		catch (Exception ex)
		{
			Debug.WriteLine("[MAKE HOLE] Curve pattern call failed. seedMark=" + seedMark + ", curveMark=" + curveMark + ", error=" + ex.Message);
			return null;
		}
	}

	private bool TryOpenCurveDrivenPatternCommand(ModelDoc2 model, Feature seedFeature, SketchSegment curveSegment)
	{
		if (model == null || seedFeature == null || curveSegment == null)
		{
			return false;
		}
		try
		{
			model.ClearSelection2(All: true);
			bool flag = SelectSketchSegmentWithMark(curveSegment, append: false, 0);
			bool flag2 = SelectFeatureWithMark(seedFeature, append: true, 0);
			bool flag3 = flag2 && flag && model.Extension.RunCommand(362, "Curve Driven Pattern");
			if (flag3)
			{
				TrySetCurvePatternUiByAutomation();
			}
			Debug.WriteLine("[MAKE HOLE] Curve pattern RunCommand fallback curve-first. seed=" + flag2 + ", curve=" + flag + ", started=" + flag3);
			if (flag3)
			{
				return true;
			}
		}
		catch (Exception ex)
		{
			Debug.WriteLine("[MAKE HOLE] Curve pattern RunCommand curve-first failed: " + ex.Message);
		}
		try
		{
			model.ClearSelection2(All: true);
			bool flag4 = SelectFeatureWithMark(seedFeature, append: false, 4);
			bool flag5 = SelectSketchSegmentWithMark(curveSegment, append: true, 1);
			bool flag6 = flag4 && flag5 && model.Extension.RunCommand(362, "Curve Driven Pattern");
			if (flag6)
			{
				TrySetCurvePatternUiByAutomation();
			}
			Debug.WriteLine("[MAKE HOLE] Curve pattern RunCommand fallback marked. seed=" + flag4 + ", curve=" + flag5 + ", started=" + flag6);
			return flag6;
		}
		catch (Exception ex2)
		{
			Debug.WriteLine("[MAKE HOLE] Curve pattern RunCommand fallback failed: " + ex2.Message);
			return false;
		}
	}

	private void PreparePendingCurvePatternEquation(ModelDoc2 model)
	{
		pendingPatternEquation = !string.IsNullOrWhiteSpace(pendingPatternLengthDimensionName) && pendingPatternMaxPitchMm > 1E-06;
		featureNamesBeforeCurvePattern = CollectFeatureNames(model);
		Debug.WriteLine("[MAKE HOLE] Pending pattern equation. enabled=" + pendingPatternEquation + ", lengthDim=" + pendingPatternLengthDimensionName + ", maxPitch=" + pendingPatternMaxPitchMm.ToString("0.###", CultureInfo.InvariantCulture) + "mm, featureCountBeforePattern=" + ((featureNamesBeforeCurvePattern != null) ? featureNamesBeforeCurvePattern.Count : 0));
	}

	private bool TryApplyPendingCurvePatternEquation(ModelDoc2 model)
	{
		Feature feature = FindNewCurvePatternFeature(model);
		if (feature == null)
		{
			Debug.WriteLine("[MAKE HOLE] Apply equation skip. New Curve Pattern not found.");
			return false;
		}
		return TryApplyCurvePatternEquation(model, feature);
	}

	private void StartPendingPatternEquationMonitor()
	{
		StopPendingPatternEquationMonitor();
		if (pendingPatternEquation)
		{
			pendingPatternEquationPollCount = 0;
			pendingPatternEquationTimer = new System.Windows.Forms.Timer();
			pendingPatternEquationTimer.Interval = 500;
			pendingPatternEquationTimer.Tick += PendingPatternEquationTimer_Tick;
			pendingPatternEquationTimer.Start();
			Debug.WriteLine("[MAKE HOLE] Pattern equation monitor started.");
		}
	}

	private void PendingPatternEquationTimer_Tick(object sender, EventArgs e)
	{
		pendingPatternEquationPollCount++;
		try
		{
			if (swApp?.ActiveDoc is ModelDoc2 model && pendingPatternEquation && !IsModelEditingFeature(model) && TryApplyPendingCurvePatternEquation(model))
			{
				Debug.WriteLine("[MAKE HOLE] Pattern equation monitor applied equation.");
				StopPendingPatternEquationMonitor();
				ResetPendingMakeHole();
				return;
			}
		}
		catch (Exception ex)
		{
			Debug.WriteLine("[MAKE HOLE] Pattern equation monitor failed: " + ex.Message);
		}
		if (pendingPatternEquationPollCount >= 120)
		{
			Debug.WriteLine("[MAKE HOLE] Pattern equation monitor timeout.");
			StopPendingPatternEquationMonitor();
		}
	}

	private bool IsModelEditingFeature(ModelDoc2 model)
	{
		if (model == null)
		{
			return false;
		}
		try
		{
			return ((dynamic)model).GetEditTarget() != null;
		}
		catch
		{
			return false;
		}
	}

	private void StopPendingPatternEquationMonitor()
	{
		if (pendingPatternEquationTimer != null)
		{
			try
			{
				pendingPatternEquationTimer.Stop();
				pendingPatternEquationTimer.Tick -= PendingPatternEquationTimer_Tick;
				pendingPatternEquationTimer.Dispose();
			}
			catch
			{
			}
			pendingPatternEquationTimer = null;
			pendingPatternEquationPollCount = 0;
		}
	}

	private Feature FindNewCurvePatternFeature(ModelDoc2 model)
	{
		if (model == null)
		{
			return null;
		}
		Feature feature = null;
		try
		{
			for (Feature feature2 = model.FirstFeature() as Feature; feature2 != null; feature2 = feature2.GetNextFeature() as Feature)
			{
				string text = SafeFeatureName(feature2);
				string text2 = "";
				try
				{
					text2 = feature2.GetTypeName2() ?? "";
				}
				catch
				{
				}
				bool flag = featureNamesBeforeCurvePattern == null || string.IsNullOrEmpty(text) || !featureNamesBeforeCurvePattern.Contains(text);
				bool flag2 = text2.IndexOf("Pattern", StringComparison.OrdinalIgnoreCase) >= 0 || text2.IndexOf("Curve", StringComparison.OrdinalIgnoreCase) >= 0 || text.IndexOf("Pattern", StringComparison.OrdinalIgnoreCase) >= 0 || text.IndexOf("鬯ｯ・ｯ繝ｻ・ｯ郢晢ｽｻ繝ｻ・ｯ驛｢譎｢・ｽ・ｻ郢晢ｽｻ繝ｻ・ｯ鬩幢ｽ｢隴趣ｽ｢繝ｻ・ｽ繝ｻ・ｻ驛｢譎｢・ｽ・ｻ郢晢ｽｻ繝ｻ・ｩ鬯ｯ・ｮ繝ｻ・ｯ髫ｴ雜｣・ｽ・｢郢晢ｽｻ繝ｻ・ｽ郢晢ｽｻ繝ｻ・ｷ鬩幢ｽ｢隴趣ｽ｢繝ｻ・ｽ繝ｻ・ｻ驛｢譎｢・ｽ・ｻ郢晢ｽｻ繝ｻ・｢鬯ｩ蟷｢・ｽ・｢髫ｴ雜｣・ｽ・｢郢晢ｽｻ繝ｻ・ｽ郢晢ｽｻ繝ｻ・ｻ鬩幢ｽ｢隴趣ｽ｢繝ｻ・ｽ繝ｻ・ｻ驛｢譎｢・ｽ・ｻ郢晢ｽｻ繝ｻ・ｽ鬯ｩ蟷｢・ｽ・｢髫ｴ雜｣・ｽ・｢郢晢ｽｻ繝ｻ・ｽ郢晢ｽｻ繝ｻ・ｻ鬩幢ｽ｢隴趣ｽ｢繝ｻ・ｽ繝ｻ・ｻ驛｢譎｢・ｽ・ｻ郢晢ｽｻ繝ｻ・｢鬯ｯ・ｯ繝ｻ・ｯ郢晢ｽｻ繝ｻ・ｮ驛｢譎｢・ｽ・ｻ郢晢ｽｻ繝ｻ・ｫ鬩幢ｽ｢隴趣ｽ｢繝ｻ・ｽ繝ｻ・ｻ驛｢譎｢・ｽ・ｻ郢晢ｽｻ繝ｻ・ｴ鬯ｯ・ｮ繝ｻ・｣髯具ｽｹ郢晢ｽｻ繝ｻ・ｽ繝ｻ・ｽ郢晢ｽｻ繝ｻ・ｵ鬯ｮ・ｯ繝ｻ・ｷ髣費ｽｨ陞滂ｽｲ繝ｻ・ｽ繝ｻ・ｽ郢晢ｽｻ繝ｻ・ｱ鬯ｩ蟷｢・ｽ・｢髫ｴ雜｣・ｽ・｢郢晢ｽｻ繝ｻ・ｽ郢晢ｽｻ繝ｻ・ｻ鬯ｯ・ｩ陝ｷ・｢繝ｻ・ｽ繝ｻ・｢鬮ｫ・ｴ髮懶ｽ｣繝ｻ・ｽ繝ｻ・｢驛｢譎｢・ｽ・ｻ郢晢ｽｻ繝ｻ・ｽ驛｢譎｢・ｽ・ｻ郢晢ｽｻ繝ｻ・ｻ鬯ｩ蟷｢・ｽ・｢髫ｴ雜｣・ｽ・｢郢晢ｽｻ繝ｻ・ｽ郢晢ｽｻ繝ｻ・ｻ鬩幢ｽ｢隴趣ｽ｢繝ｻ・ｽ繝ｻ・ｻ驛｢譎｢・ｽ・ｻ郢晢ｽｻ繝ｻ・ｽ鬯ｯ・ｩ陝ｷ・｢繝ｻ・ｽ繝ｻ・｢鬮ｫ・ｴ髮懶ｽ｣繝ｻ・ｽ繝ｻ・｢驛｢譎｢・ｽ・ｻ郢晢ｽｻ繝ｻ・ｽ驛｢譎｢・ｽ・ｻ郢晢ｽｻ繝ｻ・ｻ鬯ｩ蟷｢・ｽ・｢髫ｴ雜｣・ｽ・｢郢晢ｽｻ繝ｻ・ｽ郢晢ｽｻ繝ｻ・ｻ鬩幢ｽ｢隴趣ｽ｢繝ｻ・ｽ繝ｻ・ｻ驛｢譎｢・ｽ・ｻ郢晢ｽｻ繝ｻ・ｻ鬯ｯ・ｯ繝ｻ・ｩ髯晢ｽｷ繝ｻ・｢郢晢ｽｻ繝ｻ・ｽ郢晢ｽｻ繝ｻ・｢鬯ｮ・ｫ繝ｻ・ｴ鬮ｮ諛ｶ・ｽ・｣郢晢ｽｻ繝ｻ・ｽ郢晢ｽｻ繝ｻ・｢鬩幢ｽ｢隴趣ｽ｢繝ｻ・ｽ繝ｻ・ｻ驛｢譎｢・ｽ・ｻ郢晢ｽｻ繝ｻ・ｽ鬩幢ｽ｢隴趣ｽ｢繝ｻ・ｽ繝ｻ・ｻ驛｢譎｢・ｽ・ｻ郢晢ｽｻ繝ｻ・ｻ鬯ｯ・ｩ陝ｷ・｢繝ｻ・ｽ繝ｻ・｢鬮ｫ・ｴ髮懶ｽ｣繝ｻ・ｽ繝ｻ・｢驛｢譎｢・ｽ・ｻ郢晢ｽｻ繝ｻ・ｽ驛｢譎｢・ｽ・ｻ郢晢ｽｻ繝ｻ・ｻ鬯ｩ蟷｢・ｽ・｢髫ｴ雜｣・ｽ・｢郢晢ｽｻ繝ｻ・ｽ郢晢ｽｻ繝ｻ・ｻ鬩幢ｽ｢隴趣ｽ｢繝ｻ・ｽ繝ｻ・ｻ驛｢譎｢・ｽ・ｻ郢晢ｽｻ繝ｻ・｣鬯ｯ・ｯ繝ｻ・ｯ郢晢ｽｻ繝ｻ・ｯ驛｢譎｢・ｽ・ｻ郢晢ｽｻ繝ｻ・ｩ鬯ｮ・ｫ繝ｻ・ｰ郢晢ｽｻ繝ｻ・ｳ驛｢譎｢・ｽ・ｻ郢晢ｽｻ繝ｻ・ｾ鬩幢ｽ｢隴趣ｽ｢繝ｻ・ｽ繝ｻ・ｻ驛｢譎｢・ｽ・ｻ郢晢ｽｻ繝ｻ・ｽ鬩幢ｽ｢隴趣ｽ｢繝ｻ・ｽ繝ｻ・ｻ驛｢譎｢・ｽ・ｻ郢晢ｽｻ繝ｻ・ｵ鬯ｯ・ｩ陝ｷ・｢繝ｻ・ｽ繝ｻ・｢鬮ｫ・ｴ髮懶ｽ｣繝ｻ・ｽ繝ｻ・｢驛｢譎｢・ｽ・ｻ郢晢ｽｻ繝ｻ・ｽ驛｢譎｢・ｽ・ｻ郢晢ｽｻ繝ｻ・ｻ鬯ｩ蟷｢・ｽ・｢髫ｴ雜｣・ｽ・｢郢晢ｽｻ繝ｻ・ｽ郢晢ｽｻ繝ｻ・ｻ鬩幢ｽ｢隴趣ｽ｢繝ｻ・ｽ繝ｻ・ｻ驛｢譎｢・ｽ・ｻ郢晢ｽｻ繝ｻ・ｺ鬯ｯ・ｯ繝ｻ・ｩ髯晢ｽｷ繝ｻ・｢郢晢ｽｻ繝ｻ・ｽ郢晢ｽｻ繝ｻ・｢鬯ｮ・ｫ繝ｻ・ｴ鬮ｮ諛ｶ・ｽ・｣郢晢ｽｻ繝ｻ・ｽ郢晢ｽｻ繝ｻ・｢鬩幢ｽ｢隴趣ｽ｢繝ｻ・ｽ繝ｻ・ｻ驛｢譎｢・ｽ・ｻ郢晢ｽｻ繝ｻ・ｽ鬩幢ｽ｢隴趣ｽ｢繝ｻ・ｽ繝ｻ・ｻ驛｢譎｢・ｽ・ｻ郢晢ｽｻ繝ｻ・ｻ鬯ｯ・ｩ陝ｷ・｢繝ｻ・ｽ繝ｻ・｢鬮ｫ・ｴ髮懶ｽ｣繝ｻ・ｽ繝ｻ・｢驛｢譎｢・ｽ・ｻ郢晢ｽｻ繝ｻ・ｽ驛｢譎｢・ｽ・ｻ郢晢ｽｻ繝ｻ・ｻ鬯ｩ蟷｢・ｽ・｢髫ｴ雜｣・ｽ・｢郢晢ｽｻ繝ｻ・ｽ郢晢ｽｻ繝ｻ・ｻ鬩幢ｽ｢隴趣ｽ｢繝ｻ・ｽ繝ｻ・ｻ驛｢譎｢・ｽ・ｻ郢晢ｽｻ繝ｻ・｡鬯ｯ・ｯ繝ｻ・ｯ郢晢ｽｻ繝ｻ・ｯ驛｢譎｢・ｽ・ｻ郢晢ｽｻ繝ｻ・ｯ鬩幢ｽ｢隴趣ｽ｢繝ｻ・ｽ繝ｻ・ｻ驛｢譎｢・ｽ・ｻ郢晢ｽｻ繝ｻ・ｩ鬯ｯ・ｮ繝ｻ・ｯ髫ｴ雜｣・ｽ・｢郢晢ｽｻ繝ｻ・ｽ郢晢ｽｻ繝ｻ・ｷ鬩幢ｽ｢隴趣ｽ｢繝ｻ・ｽ繝ｻ・ｻ驛｢譎｢・ｽ・ｻ郢晢ｽｻ繝ｻ・｢鬯ｩ蟷｢・ｽ・｢髫ｴ雜｣・ｽ・｢郢晢ｽｻ繝ｻ・ｽ郢晢ｽｻ繝ｻ・ｻ鬩幢ｽ｢隴趣ｽ｢繝ｻ・ｽ繝ｻ・ｻ驛｢譎｢・ｽ・ｻ郢晢ｽｻ繝ｻ・ｽ鬯ｩ蟷｢・ｽ・｢髫ｴ雜｣・ｽ・｢郢晢ｽｻ繝ｻ・ｽ郢晢ｽｻ繝ｻ・ｻ鬩幢ｽ｢隴趣ｽ｢繝ｻ・ｽ繝ｻ・ｻ驛｢譎｢・ｽ・ｻ郢晢ｽｻ繝ｻ・｢鬯ｯ・ｯ繝ｻ・ｯ郢晢ｽｻ繝ｻ・ｮ驛｢譎｢・ｽ・ｻ郢晢ｽｻ繝ｻ・ｫ鬩幢ｽ｢隴趣ｽ｢繝ｻ・ｽ繝ｻ・ｻ驛｢譎｢・ｽ・ｻ郢晢ｽｻ繝ｻ・ｴ鬯ｯ・ｯ繝ｻ・ｮ郢晢ｽｻ繝ｻ・ｮ鬮ｫ・ｲ陝ｷ・｢繝ｻ・ｽ繝ｻ・ｶ驛｢譎｢・ｽ・ｻ郢晢ｽｻ繝ｻ・ｽ驛｢譎｢・ｽ・ｻ郢晢ｽｻ繝ｻ・｣鬯ｩ蟷｢・ｽ・｢髫ｴ雜｣・ｽ・｢郢晢ｽｻ繝ｻ・ｽ郢晢ｽｻ繝ｻ・ｻ鬩幢ｽ｢隴趣ｽ｢繝ｻ・ｽ繝ｻ・ｻ驛｢譎｢・ｽ・ｻ郢晢ｽｻ繝ｻ・ｽ鬯ｩ蟷｢・ｽ・｢髫ｴ雜｣・ｽ・｢郢晢ｽｻ繝ｻ・ｽ郢晢ｽｻ繝ｻ・ｻ鬩幢ｽ｢隴趣ｽ｢繝ｻ・ｽ繝ｻ・ｻ驛｢譎｢・ｽ・ｻ郢晢ｽｻ繝ｻ・｢鬯ｯ・ｯ繝ｻ・ｩ髯晢ｽｷ繝ｻ・｢郢晢ｽｻ繝ｻ・ｽ郢晢ｽｻ繝ｻ・｢鬯ｮ・ｫ繝ｻ・ｴ鬮ｮ諛ｶ・ｽ・｣郢晢ｽｻ繝ｻ・ｽ郢晢ｽｻ繝ｻ・｢鬩幢ｽ｢隴趣ｽ｢繝ｻ・ｽ繝ｻ・ｻ驛｢譎｢・ｽ・ｻ郢晢ｽｻ繝ｻ・ｽ鬩幢ｽ｢隴趣ｽ｢繝ｻ・ｽ繝ｻ・ｻ驛｢譎｢・ｽ・ｻ郢晢ｽｻ繝ｻ・ｻ鬯ｯ・ｩ陝ｷ・｢繝ｻ・ｽ繝ｻ・｢鬮ｫ・ｴ髮懶ｽ｣繝ｻ・ｽ繝ｻ・｢驛｢譎｢・ｽ・ｻ郢晢ｽｻ繝ｻ・ｽ驛｢譎｢・ｽ・ｻ郢晢ｽｻ繝ｻ・ｻ鬯ｩ蟷｢・ｽ・｢髫ｴ雜｣・ｽ・｢郢晢ｽｻ繝ｻ・ｽ郢晢ｽｻ繝ｻ・ｻ鬩幢ｽ｢隴趣ｽ｢繝ｻ・ｽ繝ｻ・ｻ驛｢譎｢・ｽ・ｻ郢晢ｽｻ繝ｻ・ｽ鬯ｯ・ｯ繝ｻ・ｩ髯晢ｽｷ繝ｻ・｢郢晢ｽｻ繝ｻ・ｽ郢晢ｽｻ繝ｻ・｢鬯ｮ・ｫ繝ｻ・ｴ鬮ｮ諛ｶ・ｽ・｣郢晢ｽｻ繝ｻ・ｽ郢晢ｽｻ繝ｻ・｢鬩幢ｽ｢隴趣ｽ｢繝ｻ・ｽ繝ｻ・ｻ驛｢譎｢・ｽ・ｻ郢晢ｽｻ繝ｻ・ｽ鬩幢ｽ｢隴趣ｽ｢繝ｻ・ｽ繝ｻ・ｻ驛｢譎｢・ｽ・ｻ郢晢ｽｻ繝ｻ・ｻ鬯ｯ・ｩ陝ｷ・｢繝ｻ・ｽ繝ｻ・｢鬮ｫ・ｴ髮懶ｽ｣繝ｻ・ｽ繝ｻ・｢驛｢譎｢・ｽ・ｻ郢晢ｽｻ繝ｻ・ｽ驛｢譎｢・ｽ・ｻ郢晢ｽｻ繝ｻ・ｻ鬯ｩ蟷｢・ｽ・｢髫ｴ雜｣・ｽ・｢郢晢ｽｻ繝ｻ・ｽ郢晢ｽｻ繝ｻ・ｻ鬩幢ｽ｢隴趣ｽ｢繝ｻ・ｽ繝ｻ・ｻ驛｢譎｢・ｽ・ｻ郢晢ｽｻ繝ｻ・ｼ鬯ｯ・ｯ繝ｻ・ｯ郢晢ｽｻ繝ｻ・ｯ驛｢譎｢・ｽ・ｻ郢晢ｽｻ繝ｻ・ｯ鬩幢ｽ｢隴趣ｽ｢繝ｻ・ｽ繝ｻ・ｻ驛｢譎｢・ｽ・ｻ郢晢ｽｻ繝ｻ・ｩ鬯ｯ・ｮ繝ｻ・ｯ髫ｴ雜｣・ｽ・｢郢晢ｽｻ繝ｻ・ｽ郢晢ｽｻ繝ｻ・ｷ鬩幢ｽ｢隴趣ｽ｢繝ｻ・ｽ繝ｻ・ｻ驛｢譎｢・ｽ・ｻ郢晢ｽｻ繝ｻ・｢鬯ｩ蟷｢・ｽ・｢髫ｴ雜｣・ｽ・｢郢晢ｽｻ繝ｻ・ｽ郢晢ｽｻ繝ｻ・ｻ鬩幢ｽ｢隴趣ｽ｢繝ｻ・ｽ繝ｻ・ｻ驛｢譎｢・ｽ・ｻ郢晢ｽｻ繝ｻ・ｽ鬯ｩ蟷｢・ｽ・｢髫ｴ雜｣・ｽ・｢郢晢ｽｻ繝ｻ・ｽ郢晢ｽｻ繝ｻ・ｻ鬩幢ｽ｢隴趣ｽ｢繝ｻ・ｽ繝ｻ・ｻ驛｢譎｢・ｽ・ｻ郢晢ｽｻ繝ｻ・｢鬯ｯ・ｯ繝ｻ・ｯ郢晢ｽｻ繝ｻ・ｮ驛｢譎｢・ｽ・ｻ郢晢ｽｻ繝ｻ・ｫ鬩幢ｽ｢隴趣ｽ｢繝ｻ・ｽ繝ｻ・ｻ驛｢譎｢・ｽ・ｻ郢晢ｽｻ繝ｻ・ｴ鬯ｯ・ｯ繝ｻ・ｮ郢晢ｽｻ繝ｻ・ｮ鬮ｫ・ｲ陝ｷ・｢繝ｻ・ｽ繝ｻ・ｶ驛｢譎｢・ｽ・ｻ郢晢ｽｻ繝ｻ・ｽ驛｢譎｢・ｽ・ｻ郢晢ｽｻ繝ｻ・｣鬯ｩ蟷｢・ｽ・｢髫ｴ雜｣・ｽ・｢郢晢ｽｻ繝ｻ・ｽ郢晢ｽｻ繝ｻ・ｻ鬩幢ｽ｢隴趣ｽ｢繝ｻ・ｽ繝ｻ・ｻ驛｢譎｢・ｽ・ｻ郢晢ｽｻ繝ｻ・ｽ鬯ｩ蟷｢・ｽ・｢髫ｴ雜｣・ｽ・｢郢晢ｽｻ繝ｻ・ｽ郢晢ｽｻ繝ｻ・ｻ鬩幢ｽ｢隴趣ｽ｢繝ｻ・ｽ繝ｻ・ｻ驛｢譎｢・ｽ・ｻ郢晢ｽｻ繝ｻ・｢鬯ｯ・ｯ繝ｻ・ｩ髯晢ｽｷ繝ｻ・｢郢晢ｽｻ繝ｻ・ｽ郢晢ｽｻ繝ｻ・｢鬯ｮ・ｫ繝ｻ・ｴ鬮ｮ諛ｶ・ｽ・｣郢晢ｽｻ繝ｻ・ｽ郢晢ｽｻ繝ｻ・｢鬩幢ｽ｢隴趣ｽ｢繝ｻ・ｽ繝ｻ・ｻ驛｢譎｢・ｽ・ｻ郢晢ｽｻ繝ｻ・ｽ鬩幢ｽ｢隴趣ｽ｢繝ｻ・ｽ繝ｻ・ｻ驛｢譎｢・ｽ・ｻ郢晢ｽｻ繝ｻ・ｻ鬯ｯ・ｩ陝ｷ・｢繝ｻ・ｽ繝ｻ・｢鬮ｫ・ｴ髮懶ｽ｣繝ｻ・ｽ繝ｻ・｢驛｢譎｢・ｽ・ｻ郢晢ｽｻ繝ｻ・ｽ驛｢譎｢・ｽ・ｻ郢晢ｽｻ繝ｻ・ｻ鬯ｩ蟷｢・ｽ・｢髫ｴ雜｣・ｽ・｢郢晢ｽｻ繝ｻ・ｽ郢晢ｽｻ繝ｻ・ｻ鬩幢ｽ｢隴趣ｽ｢繝ｻ・ｽ繝ｻ・ｻ驛｢譎｢・ｽ・ｻ郢晢ｽｻ繝ｻ・ｽ鬯ｯ・ｯ繝ｻ・ｩ髯晢ｽｷ繝ｻ・｢郢晢ｽｻ繝ｻ・ｽ郢晢ｽｻ繝ｻ・｢鬯ｮ・ｫ繝ｻ・ｴ鬮ｮ諛ｶ・ｽ・｣郢晢ｽｻ繝ｻ・ｽ郢晢ｽｻ繝ｻ・｢鬩幢ｽ｢隴趣ｽ｢繝ｻ・ｽ繝ｻ・ｻ驛｢譎｢・ｽ・ｻ郢晢ｽｻ繝ｻ・ｽ鬩幢ｽ｢隴趣ｽ｢繝ｻ・ｽ繝ｻ・ｻ驛｢譎｢・ｽ・ｻ郢晢ｽｻ繝ｻ・ｻ鬯ｯ・ｩ陝ｷ・｢繝ｻ・ｽ繝ｻ・｢鬮ｫ・ｴ髮懶ｽ｣繝ｻ・ｽ繝ｻ・｢驛｢譎｢・ｽ・ｻ郢晢ｽｻ繝ｻ・ｽ驛｢譎｢・ｽ・ｻ郢晢ｽｻ繝ｻ・ｻ鬯ｩ蟷｢・ｽ・｢髫ｴ雜｣・ｽ・｢郢晢ｽｻ繝ｻ・ｽ郢晢ｽｻ繝ｻ・ｻ鬩幢ｽ｢隴趣ｽ｢繝ｻ・ｽ繝ｻ・ｻ驛｢譎｢・ｽ・ｻ郢晢ｽｻ繝ｻ・ｳ", StringComparison.OrdinalIgnoreCase) >= 0 || text.IndexOf("Curve Pattern", StringComparison.OrdinalIgnoreCase) >= 0;
				if (flag)
				{
					Debug.WriteLine("[MAKE HOLE] New pattern candidate. name=" + text + ", type=" + text2 + ", pattern=" + flag2);
				}
				if (flag && flag2)
				{
					feature = feature2;
				}
			}
		}
		catch (Exception ex)
		{
			Debug.WriteLine("[MAKE HOLE] FindNewCurvePatternFeature failed: " + ex.Message);
		}
		Debug.WriteLine("[MAKE HOLE] FindNewCurvePatternFeature result=" + SafeFeatureName(feature));
		return feature;
	}

	private bool TryApplyCurvePatternEquation(ModelDoc2 model, Feature patternFeature)
	{
		if (model == null || patternFeature == null)
		{
			return false;
		}
		if (string.IsNullOrWhiteSpace(pendingPatternLengthDimensionName) || pendingPatternMaxPitchMm <= 1E-06)
		{
			Debug.WriteLine("[MAKE HOLE] Apply equation skip. Missing length dimension or pitch.");
			return false;
		}
		string text = GetPatternCountDimensionName(patternFeature);
		if (string.IsNullOrWhiteSpace(text))
		{
			text = "D1@" + SafeFeatureName(patternFeature);
			Debug.WriteLine("[MAKE HOLE] Pattern count dimension fallback=" + text);
		}
		string text2 = BuildPatternCountEquation(text, pendingPatternLengthDimensionName, pendingPatternMaxPitchMm);
		bool flag = AddOrUpdateEquation(model, text2);
		Debug.WriteLine("[MAKE HOLE] Apply pattern count equation. ok=" + flag + ", equation=" + text2);
		if (flag)
		{
			trackedPatternFeatureName = SafeFeatureName(patternFeature);
			trackedPatternCountDimensionName = text;
			Debug.WriteLine("[MAKE HOLE] Track pattern equation. patternFeature=" + trackedPatternFeatureName + ", countDim=" + trackedPatternCountDimensionName + ", holeFeature=" + trackedHoleFeatureName);
		}
		return flag;
	}

	private string GetPatternCountDimensionName(Feature patternFeature)
	{
		if (patternFeature == null)
		{
			return null;
		}
		try
		{
			Dimension dimension = patternFeature.Parameter("D1") as Dimension;
			string equationDimensionName = GetEquationDimensionName(dimension);
			if (!string.IsNullOrWhiteSpace(equationDimensionName))
			{
				return equationDimensionName;
			}
		}
		catch (Exception ex)
		{
			Debug.WriteLine("[MAKE HOLE] Get pattern D1 failed: " + ex.Message);
		}
		return null;
	}

	private string BuildPatternCountEquation(string patternDimensionName, string lengthDimensionName, double maxPitchMm)
	{
		string text = BuildPatternCountUiFormula(lengthDimensionName, maxPitchMm).TrimStart('=');
		return "\"" + patternDimensionName + "\" = " + text;
	}

	private string BuildPatternCountUiFormula(string lengthDimensionName, double maxPitchMm)
	{
		if (string.IsNullOrWhiteSpace(lengthDimensionName) || maxPitchMm <= 1E-06)
		{
			return "";
		}
		string text = Math.Round(maxPitchMm, 3, MidpointRounding.AwayFromZero).ToString("0.###", CultureInfo.InvariantCulture);
		string text2 = FormatPatternLengthReference(lengthDimensionName);
		return "=IIF((" + text2 + "/ROUND(" + text2 + "/" + text + ",0))<=" + text + ",ROUND(" + text2 + "/" + text + "+1,0),ROUND(" + text2 + "/" + text + "+2,0))";
	}

	private string FormatPatternLengthReference(string lengthReference)
	{
		if (string.IsNullOrWhiteSpace(lengthReference))
		{
			return "";
		}
		if (pendingPatternLengthIsExpression && double.TryParse(lengthReference, NumberStyles.Float, CultureInfo.InvariantCulture, out var result))
		{
			return result.ToString("0.###", CultureInfo.InvariantCulture);
		}
		return "\"" + lengthReference + "\"";
	}

	private bool AddOrUpdateEquation(ModelDoc2 model, string equation)
	{
		if (model == null || string.IsNullOrWhiteSpace(equation))
		{
			return false;
		}
		try
		{
			dynamic equationMgr = model.GetEquationMgr();
			if (equationMgr == null)
			{
				return false;
			}
			string value = equation.Split('=')[0].Trim();
			int num = 0;
			try
			{
				num = Convert.ToInt32(equationMgr.GetCount(), CultureInfo.InvariantCulture);
			}
			catch
			{
			}
			for (int i = 0; i < num; i++)
			{
				string text = "";
				try
				{
					text = Convert.ToString(equationMgr.Equation[i], CultureInfo.InvariantCulture);
				}
				catch
				{
				}
				if (!string.IsNullOrWhiteSpace(text) && text.TrimStart().StartsWith(value, StringComparison.OrdinalIgnoreCase))
				{
					equationMgr.Equation[i] = equation;
					TryRebuildEquations(equationMgr, model);
					return true;
				}
			}
			try
			{
				equationMgr.Add2(-1, equation, true);
			}
			catch
			{
				equationMgr.Add(equation);
			}
			TryRebuildEquations(equationMgr, model);
			return true;
		}
		catch (Exception ex)
		{
			Debug.WriteLine("[MAKE HOLE] Add equation failed: " + ex.Message);
			return false;
		}
	}

	private bool DeleteEquationByLeftSide(ModelDoc2 model, string leftSide)
	{
		if (model == null || string.IsNullOrWhiteSpace(leftSide))
		{
			return false;
		}
		try
		{
			dynamic equationMgr = model.GetEquationMgr();
			if (equationMgr == null)
			{
				return false;
			}
			int num = 0;
			try
			{
				num = Convert.ToInt32(equationMgr.GetCount(), CultureInfo.InvariantCulture);
			}
			catch
			{
			}
			for (int num2 = num - 1; num2 >= 0; num2--)
			{
				string text = "";
				try
				{
					text = Convert.ToString(equationMgr.Equation[num2], CultureInfo.InvariantCulture);
				}
				catch
				{
				}
				if (!string.IsNullOrWhiteSpace(text) && text.TrimStart().StartsWith(leftSide.Trim(), StringComparison.OrdinalIgnoreCase))
				{
					try
					{
						equationMgr.Delete(num2);
					}
					catch
					{
						equationMgr.Delete(num2, true);
					}
					TryRebuildEquations(equationMgr, model);
					Debug.WriteLine("[MAKE HOLE] Deleted equation: " + text);
					return true;
				}
			}
		}
		catch (Exception ex)
		{
			Debug.WriteLine("[MAKE HOLE] Delete equation failed: " + ex.Message);
		}
		return false;
	}

	private bool DeleteEquationsContaining(ModelDoc2 model, string token)
	{
		if (model == null || string.IsNullOrWhiteSpace(token))
		{
			return false;
		}
		bool flag = false;
		try
		{
			dynamic equationMgr = model.GetEquationMgr();
			if (equationMgr == null)
			{
				return false;
			}
			int num = 0;
			try
			{
				num = Convert.ToInt32(equationMgr.GetCount(), CultureInfo.InvariantCulture);
			}
			catch
			{
			}
			for (int num2 = num - 1; num2 >= 0; num2--)
			{
				string text = "";
				try
				{
					text = Convert.ToString(equationMgr.Equation[num2], CultureInfo.InvariantCulture);
				}
				catch
				{
				}
				if (!string.IsNullOrWhiteSpace(text) && text.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
				{
					try
					{
						equationMgr.Delete(num2);
					}
					catch
					{
						equationMgr.Delete(num2, true);
					}
					flag = true;
					Debug.WriteLine("[MAKE HOLE] Deleted related equation: " + text);
				}
			}
			if (flag)
			{
				TryRebuildEquations(equationMgr, model);
			}
		}
		catch (Exception ex)
		{
			Debug.WriteLine("[MAKE HOLE] Delete related equations failed: " + ex.Message);
		}
		return flag;
	}

	private void TryRebuildEquations(dynamic equationManager, ModelDoc2 model)
	{
		try
		{
			equationManager.EvaluateAll();
		}
		catch
		{
		}
		try
		{
			model.EditRebuild3();
		}
		catch
		{
		}
	}

	private void LogCurvePatternManualValues()
	{
		Debug.WriteLine("[MAKE HOLE] Curve pattern UI auto input disabled to avoid SolidWorks crash.");
		Debug.WriteLine("[MAKE HOLE] Manual Curve Pattern values: Instance=" + Math.Max(2, pendingPatternCount).ToString(CultureInfo.InvariantCulture) + ", EqualSpacing=True, Spacing=" + (pendingPatternSpacing * 1000.0).ToString("0.###", CultureInfo.InvariantCulture) + "mm, GeometryPattern=False.");
	}

	private void TrySetCurvePatternUiByAutomation()
	{
		try
		{
			Application.DoEvents();
			Thread.Sleep(1200);
			Application.DoEvents();
			int targetCount = Math.Max(2, pendingPatternCount);
			string text = targetCount.ToString(CultureInfo.InvariantCulture);
			AutomationElement rootElement = AutomationElement.RootElement;
			if (rootElement == null)
			{
				LogCurvePatternManualValues();
				return;
			}
			AutomationElement automationElement = FindSolidWorksAutomationWindow(rootElement);
			if (automationElement == null)
			{
				Debug.WriteLine("[MAKE HOLE] UIA SolidWorks window not found.");
				LogCurvePatternManualValues();
				return;
			}
			List<AutomationElement> list = FindCurvePatternEditableControls(automationElement);
			Debug.WriteLine("[MAKE HOLE] UIA editable controls=" + list.Count);
			for (int i = 0; i < list.Count; i++)
			{
				Debug.WriteLine("[MAKE HOLE] UIA edit #" + i + " value=" + GetAutomationValue(list[i]) + ", name=" + SafeAutomationText(list[i].Current.Name) + ", class=" + SafeAutomationText(list[i].Current.ClassName));
			}
			List<AutomationElement> controls = TakePropertyManagerControls(list);
			List<AutomationElement> list2 = FindLikelyCurvePatternCountControls(controls, targetCount);
			if (list2.Count == 0)
			{
				Debug.WriteLine("[MAKE HOLE] UIA count control not found.");
				LogCurvePatternManualValues();
				return;
			}
			int num = 0;
			foreach (AutomationElement item in list2.Where(IsEnabledAutomationElement).Take(1))
			{
				if (TrySetAutomationValue(item, text))
				{
					num++;
				}
			}
			if (num == 0)
			{
				Debug.WriteLine("[MAKE HOLE] UIA set count failed.");
				LogCurvePatternManualValues();
				return;
			}
			TryEnableCurvePatternEqualSpacingOption();
			TryDisableCurvePatternGeometryOption();
			Debug.WriteLine("[MAKE HOLE] UIA set Curve Pattern values ok. count=" + text + ", equationAfterCreate=" + pendingPatternEquation + ", countControls=" + num);
		}
		catch (Exception ex)
		{
			Debug.WriteLine("[MAKE HOLE] UIA set Curve Pattern count failed: " + ex.Message);
			LogCurvePatternManualValues();
		}
	}

	private bool IsEnabledAutomationElement(AutomationElement element)
	{
		try
		{
			return element != null && element.Current.IsEnabled && !element.Current.IsOffscreen;
		}
		catch
		{
			return false;
		}
	}

	private AutomationElement FindSolidWorksAutomationWindow(AutomationElement root)
	{
		try
		{
			AutomationElementCollection automationElementCollection = root.FindAll(TreeScope.Children, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Window));
			foreach (AutomationElement item in automationElementCollection)
			{
				string a = "";
				try
				{
					Process processById = Process.GetProcessById(item.Current.ProcessId);
					a = processById.ProcessName;
				}
				catch
				{
				}
				if (string.Equals(a, "SLDWORKS", StringComparison.OrdinalIgnoreCase))
				{
					return item;
				}
			}
		}
		catch (Exception ex)
		{
			Debug.WriteLine("[MAKE HOLE] UIA find SW window failed: " + ex.Message);
		}
		return null;
	}

	private List<AutomationElement> FindCurvePatternEditableControls(AutomationElement solidWorksWindow)
	{
		List<AutomationElement> list = new List<AutomationElement>();
		if (solidWorksWindow == null)
		{
			return list;
		}
		try
		{
			OrCondition condition = new OrCondition(new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit), new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Spinner), new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ComboBox));
			AutomationElementCollection automationElementCollection = solidWorksWindow.FindAll(TreeScope.Descendants, condition);
			foreach (AutomationElement item in automationElementCollection)
			{
				list.Add(item);
			}
		}
		catch (Exception ex)
		{
			Debug.WriteLine("[MAKE HOLE] UIA find edit controls failed: " + ex.Message);
		}
		return list;
	}

	private List<AutomationElement> TakePropertyManagerControls(List<AutomationElement> controls)
	{
		List<AutomationElement> list = new List<AutomationElement>();
		if (controls == null)
		{
			return list;
		}
		foreach (AutomationElement control in controls)
		{
			string text = "";
			string a = "";
			try
			{
				text = control.Current.ClassName ?? "";
				a = control.Current.Name ?? "";
			}
			catch
			{
			}
			if (text.IndexOf("WindowsForms", StringComparison.OrdinalIgnoreCase) >= 0 || string.Equals(a, "Hole Type", StringComparison.OrdinalIgnoreCase) || string.Equals(a, "Loose (AxB)", StringComparison.OrdinalIgnoreCase) || string.Equals(a, "Circle (H)", StringComparison.OrdinalIgnoreCase) || string.Equals(a, "Direction", StringComparison.OrdinalIgnoreCase) || string.Equals(a, "Dim Edge X", StringComparison.OrdinalIgnoreCase) || string.Equals(a, "Dim Left L", StringComparison.OrdinalIgnoreCase) || string.Equals(a, "Dim Right R", StringComparison.OrdinalIgnoreCase) || string.Equals(a, "Pitch @", StringComparison.OrdinalIgnoreCase))
			{
				break;
			}
			list.Add(control);
		}
		Debug.WriteLine("[MAKE HOLE] UIA PM controls=" + list.Count);
		return list;
	}

	private List<AutomationElement> FindLikelyCurvePatternCountControls(List<AutomationElement> controls, int targetCount)
	{
		List<AutomationElement> list = new List<AutomationElement>();
		if (controls == null || controls.Count == 0)
		{
			return list;
		}
		List<AutomationElement> list2 = controls.Where((AutomationElement c) => IsSmallIntegerAutomationValue(GetAutomationValue(c))).ToList();
		if (list2.Count == 0)
		{
			return list;
		}
		List<AutomationElement> collection = list2.Where((AutomationElement c) => string.Equals(GetAutomationValue(c), targetCount.ToString(CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase)).ToList();
		list.AddRange(collection);
		if (list.Count > 0)
		{
			return list;
		}
		List<AutomationElement> collection2 = list2.Where((AutomationElement c) => string.Equals(GetAutomationValue(c), "6", StringComparison.OrdinalIgnoreCase)).ToList();
		list.AddRange(collection2);
		if (list.Count > 0)
		{
			return list;
		}
		string firstValue = GetAutomationValue(list2[0]);
		list.AddRange(list2.Where((AutomationElement c) => string.Equals(GetAutomationValue(c), firstValue, StringComparison.OrdinalIgnoreCase)));
		return list;
	}

	private bool IsSmallIntegerAutomationValue(string value)
	{
		int result;
		return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result) && result >= 1 && result <= 999;
	}

	private string GetAutomationValue(AutomationElement element)
	{
		if (element == null)
		{
			return "";
		}
		try
		{
			if (element.TryGetCurrentPattern(ValuePattern.Pattern, out var patternObject))
			{
				return ((ValuePattern)patternObject).Current.Value ?? "";
			}
		}
		catch
		{
		}
		try
		{
			return element.Current.Name ?? "";
		}
		catch
		{
			return "";
		}
	}

	private bool TrySetAutomationValue(AutomationElement element, string value)
	{
		if (element == null)
		{
			return false;
		}
		try
		{
			if (element.TryGetCurrentPattern(ValuePattern.Pattern, out var patternObject))
			{
				((ValuePattern)patternObject).SetValue(value);
				return true;
			}
		}
		catch (Exception ex)
		{
			Debug.WriteLine("[MAKE HOLE] UIA ValuePattern set failed: " + ex.Message);
		}
		try
		{
			element.SetFocus();
			Application.DoEvents();
			SendKeys.SendWait("^a");
			SendKeys.SendWait(value);
			return true;
		}
		catch (Exception ex2)
		{
			Debug.WriteLine("[MAKE HOLE] UIA fallback SendKeys set failed: " + ex2.Message);
			return false;
		}
	}

	private string SafeAutomationText(string value)
	{
		return string.IsNullOrEmpty(value) ? "" : value.Replace(System.Environment.NewLine, " ");
	}

	private void TryEnableCurvePatternEqualSpacingOption()
	{
		try
		{
			Application.DoEvents();
			Thread.Sleep(300);
			Application.DoEvents();
			AutomationElement rootElement = AutomationElement.RootElement;
			if (rootElement == null)
			{
				Debug.WriteLine("[MAKE HOLE] Enable Equal Spacing skip. UIA root null.");
				return;
			}
			AutomationElement automationElement = FindSolidWorksAutomationWindow(rootElement);
			if (automationElement == null)
			{
				Debug.WriteLine("[MAKE HOLE] Enable Equal Spacing skip. SW window not found.");
				return;
			}
			AutomationElementCollection automationElementCollection = automationElement.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.CheckBox));
			foreach (AutomationElement item in automationElementCollection)
			{
				string text = "";
				try
				{
					text = item.Current.Name ?? "";
				}
				catch
				{
					text = "";
				}
				string text2 = text.Replace(" ", "");
				if ((text.IndexOf("Equal", StringComparison.OrdinalIgnoreCase) < 0 && text2.IndexOf("EqualSpacing", StringComparison.OrdinalIgnoreCase) < 0 && text.IndexOf("等間隔", StringComparison.OrdinalIgnoreCase) < 0) || !item.TryGetCurrentPattern(TogglePattern.Pattern, out var patternObject) || !(patternObject is TogglePattern { Current: { ToggleState: var toggleState } } togglePattern))
				{
					continue;
				}
				Debug.WriteLine("[MAKE HOLE] Equal Spacing checkbox found. name=" + text + ", state=" + toggleState);
				if (toggleState != ToggleState.On)
				{
					togglePattern.Toggle();
					Debug.WriteLine("[MAKE HOLE] Equal Spacing enabled.");
				}
				return;
			}
			Debug.WriteLine("[MAKE HOLE] Equal Spacing checkbox not found.");
		}
		catch (Exception ex)
		{
			Debug.WriteLine("[MAKE HOLE] Enable Equal Spacing failed: " + ex.Message);
		}
	}

	private void TryDisableCurvePatternGeometryOption()
	{
		try
		{
			Application.DoEvents();
			Thread.Sleep(300);
			Application.DoEvents();
			AutomationElement rootElement = AutomationElement.RootElement;
			if (rootElement == null)
			{
				Debug.WriteLine("[MAKE HOLE] Disable Geometry Pattern skip. UIA root null.");
				return;
			}
			AutomationElement automationElement = FindSolidWorksAutomationWindow(rootElement);
			if (automationElement == null)
			{
				Debug.WriteLine("[MAKE HOLE] Disable Geometry Pattern skip. SW window not found.");
				return;
			}
			AutomationElementCollection automationElementCollection = automationElement.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.CheckBox));
			foreach (AutomationElement item in automationElementCollection)
			{
				string text = "";
				try
				{
					text = item.Current.Name ?? "";
				}
				catch
				{
					text = "";
				}
				if ((text.IndexOf("Geometry", StringComparison.OrdinalIgnoreCase) < 0 && text.IndexOf("Geometry Pattern", StringComparison.OrdinalIgnoreCase) < 0) || !item.TryGetCurrentPattern(TogglePattern.Pattern, out var patternObject) || !(patternObject is TogglePattern { Current: { ToggleState: var toggleState } } togglePattern))
				{
					continue;
				}
				Debug.WriteLine("[MAKE HOLE] Geometry Pattern checkbox found. name=" + text + ", state=" + toggleState);
				if (toggleState == ToggleState.On)
				{
					togglePattern.Toggle();
					Debug.WriteLine("[MAKE HOLE] Geometry Pattern disabled.");
				}
				return;
			}
			Debug.WriteLine("[MAKE HOLE] Geometry Pattern checkbox not found.");
		}
		catch (Exception ex)
		{
			Debug.WriteLine("[MAKE HOLE] Disable Geometry Pattern failed: " + ex.Message);
		}
	}

	private bool SelectFeatureWithMark(Feature feature, bool append, int mark)
	{
		if (feature == null)
		{
			return false;
		}
		try
		{
			return feature.Select2(append, mark);
		}
		catch
		{
			try
			{
				return ((dynamic)feature).Select(append);
			}
			catch
			{
				return false;
			}
		}
	}

	private bool SelectSketchSegmentWithMark(SketchSegment segment, bool append, int mark)
	{
		if (segment == null)
		{
			return false;
		}
		SelectData selectData = null;
		try
		{
			selectData = ((((swApp?.ActiveDoc is ModelDoc2 modelDoc) ? modelDoc.SelectionManager : null) is SelectionMgr selectionMgr) ? selectionMgr.CreateSelectData() : null);
			if (selectData != null)
			{
				selectData.Mark = mark;
			}
		}
		catch
		{
			selectData = null;
		}
		try
		{
			return segment.Select4(append, selectData);
		}
		catch
		{
			try
			{
				return ((dynamic)segment).Select(append);
			}
			catch
			{
				return false;
			}
		}
	}

	private Feature TryCreateSeedCurvePatternDefinition(ModelDoc2 model, Feature seedFeature, SketchSegment curveSegment, int count, double spacing)
	{
		if (model == null || seedFeature == null || curveSegment == null)
		{
			return null;
		}
		ICurveDrivenPatternFeatureData data = null;
		try
		{
			object obj = model.FeatureManager.CreateDefinition(103);
			data = obj as ICurveDrivenPatternFeatureData;
			Debug.WriteLine("[MAKE HOLE] Curve pattern definition. data=" + ((data == null) ? "null" : "ok"));
			if (data == null)
			{
				return null;
			}
		}
		catch (Exception ex)
		{
			Debug.WriteLine("[MAKE HOLE] Curve pattern CreateDefinition failed: " + ex.Message);
			return null;
		}
		TrySetPatternValue("PatternFeatureArray", delegate
		{
			data.PatternFeatureArray = new object[1] { seedFeature };
		});
		TrySetPatternValue("D1Direction", delegate
		{
			data.D1Direction = curveSegment;
		});
		TrySetPatternValue("D1InstanceCount", delegate
		{
			data.D1InstanceCount = count;
		});
		TrySetPatternValue("D1IsEqualSpaced", delegate
		{
			data.D1IsEqualSpaced = true;
		});
		TrySetPatternValue("D1Spacing", delegate
		{
			data.D1Spacing = spacing;
		});
		TrySetPatternValue("D1ReverseDirection", delegate
		{
			data.D1ReverseDirection = false;
		});
		TrySetPatternValue("D1CurveMethod", delegate
		{
			data.D1CurveMethod = 0;
		});
		TrySetPatternValue("D1AlignmentMethod", delegate
		{
			data.D1AlignmentMethod = 0;
		});
		TrySetPatternValue("GeometryPattern", delegate
		{
			data.GeometryPattern = false;
		});
		TrySetPatternValue("VarySketch", delegate
		{
			data.VarySketch = false;
		});
		TrySetPatternValue("Dir2Specified", delegate
		{
			data.Dir2Specified = false;
		});
		TrySetPatternValue("D2PatternSeedOnly", delegate
		{
			data.D2PatternSeedOnly = false;
		});
		try
		{
			Feature feature = model.FeatureManager.CreateFeature(data);
			Debug.WriteLine("[MAKE HOLE] Curve pattern CreateFeature. feature=" + ((feature == null) ? "null" : "ok"));
			if (feature != null)
			{
				feature.Name = "TAI_MAKE_HOLE_PATTERN_DEF";
			}
			return feature;
		}
		catch (Exception ex2)
		{
			Debug.WriteLine("[MAKE HOLE] Curve pattern CreateFeature failed: " + ex2.Message);
			return null;
		}
	}

	private void TrySetPatternValue(string name, Action setter)
	{
		try
		{
			setter();
			Debug.WriteLine("[MAKE HOLE] Curve pattern set " + name + " ok.");
		}
		catch (Exception ex)
		{
			Debug.WriteLine("[MAKE HOLE] Curve pattern set " + name + " failed: " + ex.Message);
		}
	}

	private SketchSegment FindLongestSketchSegment(List<SketchSegment> segments)
	{
		if (segments == null || segments.Count == 0)
		{
			return null;
		}
		SketchSegment result = null;
		double num = 0.0;
		foreach (SketchSegment segment in segments)
		{
			double sketchSegmentApproxLength = GetSketchSegmentApproxLength(segment);
			if (sketchSegmentApproxLength > num)
			{
				num = sketchSegmentApproxLength;
				result = segment;
			}
		}
		Debug.WriteLine("[MAKE HOLE] Curve pattern segment. segments=" + segments.Count + ", length=" + (num * 1000.0).ToString("0.###", CultureInfo.InvariantCulture) + "mm");
		return result;
	}

	private List<SketchSegment> GetUsableSketchSegments(Sketch sketch)
	{
		List<SketchSegment> list = new List<SketchSegment>();
		List<SketchSegment> sketchSegments = GetSketchSegments(sketch);
		foreach (SketchSegment item in sketchSegments)
		{
			double sketchSegmentApproxLength = GetSketchSegmentApproxLength(item);
			int num = -1;
			try
			{
				num = item.GetType();
			}
			catch
			{
			}
			Debug.WriteLine("[MAKE HOLE] Pattern segment candidate. type=" + num + ", length=" + (sketchSegmentApproxLength * 1000.0).ToString("0.###", CultureInfo.InvariantCulture) + "mm");
			if (sketchSegmentApproxLength > 1E-06)
			{
				list.Add(item);
			}
		}
		return list;
	}

	private Sketch GetNewestSketchWithUsableSegments(ModelDoc2 model)
	{
		if (model == null)
		{
			return null;
		}
		Sketch result = null;
		try
		{
			for (Feature feature = model.FirstFeature() as Feature; feature != null; feature = feature.GetNextFeature() as Feature)
			{
				if (feature.GetSpecificFeature2() is Sketch sketch && GetUsableSketchSegments(sketch).Count > 0)
				{
					result = sketch;
				}
			}
		}
		catch (Exception ex)
		{
			Debug.WriteLine("[MAKE HOLE] GetNewestSketchWithUsableSegments failed: " + ex.Message);
		}
		return result;
	}

	private double GetSketchSegmentApproxLength(SketchSegment segment)
	{
		if (segment == null)
		{
			return 0.0;
		}
		List<double[]> list = GetSketchSegmentPointsFallback(segment);
		if (list == null || list.Count < 2)
		{
			list = BuildSegmentSamplePoints(segment);
		}
		if (list == null || list.Count < 2)
		{
			return 0.0;
		}
		double num = 0.0;
		for (int i = 1; i < list.Count; i++)
		{
			num += Distance(list[i - 1], list[i]);
		}
		return num;
	}

	private List<double[]> BuildSegmentSamplePoints(SketchSegment segment)
	{
		List<double[]> list = new List<double[]>();
		try
		{
			if (segment.GetCurve() is Curve curve && TryGetCurveGeometry(curve, out var geometry))
			{
				list.AddRange(SampleCurve(geometry, 32));
			}
		}
		catch
		{
		}
		return list;
	}

	private bool SelectSketchSegment(SketchSegment segment, bool append)
	{
		if (segment == null)
		{
			return false;
		}
		try
		{
			return segment.Select4(append, null);
		}
		catch
		{
			try
			{
				return ((dynamic)segment).Select(append);
			}
			catch
			{
				return false;
			}
		}
	}

	private Feature TryCallHoleWizard5(ModelDoc2 model, int holeType, double diameter)
	{
		try
		{
			return model.FeatureManager.HoleWizard5(holeType, 9, 0, "", 1, diameter, 0.01, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0, "", RevDir: false, FeatureScope: true, AutoSelect: true, AssemblyFeatureScope: false, AutoSelectComponents: false, PropagateFeatureToParts: false);
		}
		catch (Exception ex)
		{
			Debug.WriteLine("[MAKE HOLE] HoleWizard5 call failed: " + ex.Message);
			return null;
		}
	}

	private bool SelectSketchPoint(SketchPoint point, bool append)
	{
		if (point == null)
		{
			return false;
		}
		try
		{
			return point.Select4(append, null);
		}
		catch
		{
			try
			{
				return ((dynamic)point).Select(append);
			}
			catch
			{
				return false;
			}
		}
	}

	private SketchSegment FindNearestSketchSegment(List<SketchSegment> segments, double[] point)
	{
		if (segments == null || point == null)
		{
			return null;
		}
		SketchSegment sketchSegment = null;
		double num = double.MaxValue;
		foreach (SketchSegment segment in segments)
		{
			double segmentApproxDistance = GetSegmentApproxDistance(segment, point);
			if (segmentApproxDistance < num)
			{
				num = segmentApproxDistance;
				sketchSegment = segment;
			}
		}
		return sketchSegment ?? segments[0];
	}

	private double GetSegmentApproxDistance(SketchSegment segment, double[] point)
	{
		if (segment == null || point == null)
		{
			return double.MaxValue;
		}
		try
		{
			if (!(segment.GetCurve() is Curve curve) || !TryGetCurveGeometry(curve, out var geometry))
			{
				return GetPointListApproxDistance(GetSketchSegmentPointsFallback(segment), point);
			}
			List<double[]> points = SampleCurve(geometry, 32);
			return GetPointListApproxDistance(points, point);
		}
		catch
		{
			return GetPointListApproxDistance(GetSketchSegmentPointsFallback(segment), point);
		}
	}

	private double GetPointListApproxDistance(List<double[]> points, double[] point)
	{
		if (points == null || points.Count == 0 || !IsPoint(point))
		{
			return double.MaxValue;
		}
		double num = double.MaxValue;
		foreach (double[] point2 in points)
		{
			if (IsPoint(point2))
			{
				double num2 = Distance(point2, point);
				if (num2 < num)
				{
					num = num2;
				}
			}
		}
		return num;
	}

	private void CreateDivisionMarkerLines(ModelDoc2 model, Face2 face, List<double[]> pathPoints, List<double[]> divisionPoints, double markerLength)
	{
		if (model == null || pathPoints == null || pathPoints.Count < 2 || divisionPoints == null)
		{
			return;
		}
		foreach (double[] divisionPoint in divisionPoints)
		{
			if (!IsPoint(divisionPoint))
			{
				continue;
			}
			int nearestPointIndex = GetNearestPointIndex(pathPoints, divisionPoint);
			double[] sampleTangent = GetSampleTangent(pathPoints, nearestPointIndex);
			if (sampleTangent == null || !TryGetFaceNormalAtPoint(face, divisionPoint, out var normal))
			{
				model.SketchManager.CreatePoint(divisionPoint[0], divisionPoint[1], divisionPoint[2]);
				continue;
			}
			double[] array = Normalize(Cross(normal, sampleTangent));
			if (array == null)
			{
				model.SketchManager.CreatePoint(divisionPoint[0], divisionPoint[1], divisionPoint[2]);
				continue;
			}
			double num = markerLength * 0.5;
			double[] array2 = Add(divisionPoint, Scale(array, 0.0 - num));
			double[] array3 = Add(divisionPoint, Scale(array, num));
			model.SketchManager.CreateLine(array2[0], array2[1], array2[2], array3[0], array3[1], array3[2]);
		}
	}

	private int GetNearestPointIndex(List<double[]> points, double[] target)
	{
		if (points == null || points.Count == 0 || !IsPoint(target))
		{
			return 0;
		}
		int result = 0;
		double num = double.MaxValue;
		for (int i = 0; i < points.Count; i++)
		{
			double num2 = Distance(points[i], target);
			if (num2 < num)
			{
				num = num2;
				result = i;
			}
		}
		return result;
	}

	private HolePoint[] BuildHolePoints(EdgeGeometry edge, double[] faceNormal, double[] sidePoint, MakeHoleOptions options)
	{
		double num = options.LeftOffsetMm / 1000.0;
		double num2 = options.RightOffsetMm / 1000.0;
		double num3 = options.PitchMm / 1000.0;
		double scale = options.EdgeOffsetMm / 1000.0;
		double num4 = edge.Length - num - num2;
		if (num4 <= 0.001)
		{
			return new HolePoint[0];
		}
		int num5 = Math.Max(2, (int)Math.Ceiling(num4 / num3) + 1);
		double num6 = ((num5 > 1) ? (num4 / (double)(num5 - 1)) : 0.0);
		double[] array = Normalize(Cross(faceNormal, edge.Direction));
		if (array == null)
		{
			return new HolePoint[0];
		}
		if (sidePoint != null)
		{
			double[] right = Scale(Add(edge.Start, edge.End), 0.5);
			double num7 = Dot(Subtract(sidePoint, right), array);
			if (num7 < 0.0)
			{
				array = Scale(array, -1.0);
			}
		}
		HolePoint[] array2 = new HolePoint[num5];
		for (int i = 0; i < num5; i++)
		{
			double scale2 = num + num6 * (double)i;
			double[] left = Add(edge.Start, Scale(edge.Direction, scale2));
			double[] array3 = Add(left, Scale(array, scale));
			array2[i] = new HolePoint
			{
				X = array3[0],
				Y = array3[1],
				Z = array3[2]
			};
		}
		return array2;
	}

	private Feature CreateCutHoles(ModelDoc2 model, Face2 face, double[] edgeDirection, double[] faceNormal, HolePoint[] points, MakeHoleOptions options)
	{
		model.ClearSelection2(All: true);
		if (!SelectFace(face, append: false))
		{
			return null;
		}
		LooseSize size = null;
		bool flag = string.Equals(options.HoleType, "Loose", StringComparison.OrdinalIgnoreCase);
		if (flag && !TryParseLooseSize(options.LooseType, out size))
		{
			MessageBox.Show("Loose AxB khong hop le. Hay nhap dang 10x16.", "Make Hole", MessageBoxButtons.OK, MessageBoxIcon.Asterisk);
			return null;
		}
		model.SketchManager.InsertSketch(UpdateEditRebuild: true);
		foreach (HolePoint holePoint in points)
		{
			if (flag)
			{
				CreateLooseSlot(model, holePoint, edgeDirection, faceNormal, size);
			}
			else
			{
				model.SketchManager.CreateCircleByRadius(holePoint.X, holePoint.Y, holePoint.Z, options.DiameterMm / 2000.0);
			}
		}
		model.SketchManager.InsertSketch(UpdateEditRebuild: true);
		Feature feature = TryCutThroughAll(model);
		if (feature != null)
		{
			feature.Name = "Make Hole";
		}
		return feature;
	}

	private bool TryRepairHolesFromPlanarFace(ModelDoc2 model, Face2 face, double diameterM, double depthM, out int repairedCount, out string message)
	{
		repairedCount = 0;
		message = "";
		if (model == null || face == null || diameterM <= 1E-06 || depthM <= 1E-06)
		{
			message = "Thong so Repair Hole khong hop le.";
			return false;
		}
		List<Feature> temporaryFillFeatures;
		List<double[]> repairHoleCentersFromFace = GetRepairHoleCentersFromFace(model, face, diameterM, out temporaryFillFeatures);
		Debug.WriteLine("[REPAIR HOLE] kept fill surfaces=" + temporaryFillFeatures.Count);
		Debug.WriteLine("[REPAIR HOLE] face scan centers=" + repairHoleCentersFromFace.Count);
		if (repairHoleCentersFromFace.Count == 0)
		{
			message = "Khong tim thay loop lo tren mat phang da chon. Hay xem log [REPAIR HOLE] trong Output.";
			return false;
		}
		List<Feature> repairPointFeatures = TryCreateRepairReferencePoints(model, temporaryFillFeatures);
		int circleCount;
		Feature feature = CreateRepairHoleCuts(model, face, repairHoleCentersFromFace, repairPointFeatures, diameterM, depthM, out circleCount);
		if (feature == null)
		{
			message = "Da tim thay tam lo nhung chua tao duoc Extrude Cut. Hay xem log [REPAIR HOLE] trong Output.";
			return false;
		}
		TryInsertDeleteBodyForRepairFillSurfaces(model, temporaryFillFeatures);
		repairedCount = circleCount;
		return true;
	}

	private List<double[]> GetRepairHoleCentersFromFace(ModelDoc2 model, Face2 face, double diameterM, out List<Feature> temporaryFillFeatures)
	{
		List<double[]> list = new List<double[]>();
		temporaryFillFeatures = new List<Feature>();
		if (model == null || face == null)
		{
			return list;
		}
		Array array = null;
		try
		{
			array = ((dynamic)face).GetLoops() as Array;
		}
		catch (Exception ex)
		{
			Debug.WriteLine("[REPAIR HOLE] GetLoops failed: " + ex.Message);
		}
		if (array == null)
		{
			Debug.WriteLine("[REPAIR HOLE] GetLoops returned null.");
			return list;
		}
		FacePlaneFrame facePlaneFrame = CreateFacePlaneFrame(face);
		Debug.WriteLine("[REPAIR HOLE] face local frame=" + (facePlaneFrame != null));
		List<RepairHoleLoopCandidate> list2 = new List<RepairHoleLoopCandidate>();
		int num = 0;
		foreach (dynamic item in array)
		{
			num++;
			bool flag = false;
			try
			{
				flag = (bool)item.IsOuter();
			}
			catch
			{
			}
			List<double[]> list3 = new List<double[]>();
			List<double[]> list4 = new List<double[]>();
			List<Edge> list5 = new List<Edge>();
			Array array2 = null;
			try
			{
				array2 = item.GetEdges() as Array;
			}
			catch (Exception ex2)
			{
				Debug.WriteLine("[REPAIR HOLE] loop " + num + " GetEdges failed: " + ex2.Message);
			}
			int num2 = 0;
			if (array2 != null)
			{
				foreach (object item2 in array2)
				{
					if (item2 is Edge edge)
					{
						num2++;
						list5.Add(edge);
						if (TryGetCircularEdgeData(edge, out var center, out var _))
						{
							list4.Add(center);
						}
						if (TryGetEdgeGeometry(edge, out var geometry))
						{
							list3.AddRange(SampleCurve(geometry, 16));
						}
						else
						{
							list3.AddRange(SampleRepairEdge(edge, 32));
						}
					}
				}
			}
			if (!TryGetLoopCenter(list3, list4, facePlaneFrame, out var center2, out var width, out var height))
			{
				Debug.WriteLine("[REPAIR HOLE] loop " + num + " skipped. outer=" + flag + ", edges=" + num2 + ", points=" + list3.Count + ", no center");
				continue;
			}
			double major = Math.Max(width, height);
			double minor = Math.Min(width, height);
			bool flag2 = !flag && IsRepairHoleLoopSizeCandidate(major, minor, diameterM);
			Debug.WriteLine("[REPAIR HOLE] loop " + num + ". outer=" + flag + ", edges=" + num2 + ", points=" + list3.Count + ", sizeMm=(" + (width * 1000.0).ToString("0.###", CultureInfo.InvariantCulture) + " x " + (height * 1000.0).ToString("0.###", CultureInfo.InvariantCulture) + "), center=(" + (center2[0] * 1000.0).ToString("0.###", CultureInfo.InvariantCulture) + "," + (center2[1] * 1000.0).ToString("0.###", CultureInfo.InvariantCulture) + "," + (center2[2] * 1000.0).ToString("0.###", CultureInfo.InvariantCulture) + "), candidate=" + flag2);
			if (flag2)
			{
				list2.Add(new RepairHoleLoopCandidate
				{
					Index = num,
					Edges = list5,
					FallbackCenter = center2,
					Width = width,
					Height = height
				});
			}
		}
		foreach (RepairHoleLoopCandidate item3 in list2)
		{
			if (TryCreateRepairFillSurfaceCenter(model, item3, facePlaneFrame, out var fillFeature, out var center3))
			{
				if (fillFeature != null)
				{
					temporaryFillFeatures.Add(fillFeature);
				}
				if (IsPoint(center3) && !ContainsNearPoint(list, center3, Math.Max(0.0005, diameterM * 0.2)))
				{
					list.Add(center3);
					Debug.WriteLine("[REPAIR HOLE] loop " + item3.Index + " center from fill surface=(" + (center3[0] * 1000.0).ToString("0.###", CultureInfo.InvariantCulture) + "," + (center3[1] * 1000.0).ToString("0.###", CultureInfo.InvariantCulture) + "," + (center3[2] * 1000.0).ToString("0.###", CultureInfo.InvariantCulture) + ")");
					continue;
				}
			}
			Debug.WriteLine("[REPAIR HOLE] loop " + item3.Index + " skipped: fill surface center is required.");
		}
		return list;
	}

	private bool TryCreateRepairFillSurfaceCenter(ModelDoc2 model, RepairHoleLoopCandidate candidate, FacePlaneFrame planeFrame, out Feature fillFeature, out double[] center)
	{
		fillFeature = null;
		center = null;
		if (model == null || candidate == null || candidate.Edges == null || candidate.Edges.Count == 0)
		{
			return false;
		}
		fillFeature = TryCreateRepairFillSurface(model, candidate.Edges);
		if (fillFeature == null)
		{
			Debug.WriteLine("[REPAIR HOLE] loop " + candidate.Index + " fill surface=null");
			return false;
		}
		fillFeature.Name = "Repair Hole Fill Surface " + candidate.Index;
		if (TryGetRepairFillSurfaceCenter(fillFeature, planeFrame, out var center2) && IsPoint(center2))
		{
			center = ((planeFrame != null) ? planeFrame.ProjectToPlane(center2) : center2);
			Debug.WriteLine("[REPAIR HOLE] loop " + candidate.Index + " center from fill face=(" + (center[0] * 1000.0).ToString("0.###", CultureInfo.InvariantCulture) + "," + (center[1] * 1000.0).ToString("0.###", CultureInfo.InvariantCulture) + "," + (center[2] * 1000.0).ToString("0.###", CultureInfo.InvariantCulture) + ")");
			return true;
		}
		center = ((planeFrame != null) ? planeFrame.ProjectToPlane(candidate.FallbackCenter) : candidate.FallbackCenter);
		if (IsPoint(center))
		{
			return true;
		}
		Debug.WriteLine("[REPAIR HOLE] loop " + candidate.Index + " projected loop center failed.");
		return false;
	}

	private Feature TryCreateRepairFillSurface(ModelDoc2 model, List<Edge> edges)
	{
		if (model == null || edges == null || edges.Count == 0)
		{
			return null;
		}
		dynamic featureManager = model.FeatureManager;
		object[] array = edges.Cast<object>().ToArray();
		DispatchWrapper[] array2 = edges.Select((Edge edge) => new DispatchWrapper(edge)).ToArray();
		try
		{
			model.ClearSelection2(All: true);
			bool flag = false;
			foreach (Edge edge in edges)
			{
				if (SelectEdge(edge, flag))
				{
					flag = true;
				}
			}
			if (flag)
			{
				Feature feature = featureManager.InsertFillSurface(2) as Feature;
				Debug.WriteLine("[REPAIR HOLE] InsertFillSurface selected loop result=" + ((feature == null) ? "null" : SafeFeatureName(feature)));
				if (feature != null)
				{
					return feature;
				}
			}
			else
			{
				Debug.WriteLine("[REPAIR HOLE] FillSurface selected loop boundary selected=False");
			}
		}
		catch (Exception ex)
		{
			Debug.WriteLine("[REPAIR HOLE] InsertFillSurface selected loop failed: " + ex.GetType().Name + " - " + ex.Message);
		}
		finally
		{
			model.ClearSelection2(All: true);
		}
		try
		{
			Feature feature2 = featureManager.InsertFillSurface2(2, 0, array, null, null, null) as Feature;
			Debug.WriteLine("[REPAIR HOLE] InsertFillSurface2 direct result=" + ((feature2 == null) ? "null" : SafeFeatureName(feature2)));
			if (feature2 != null)
			{
				return feature2;
			}
		}
		catch (Exception ex2)
		{
			Debug.WriteLine("[REPAIR HOLE] InsertFillSurface2 direct failed: " + ex2.GetType().Name + " - " + ex2.Message);
		}
		try
		{
			Feature feature3 = featureManager.InsertFillSurface2(2, 0, array2, null, null, null) as Feature;
			Debug.WriteLine("[REPAIR HOLE] InsertFillSurface2 wrapped result=" + ((feature3 == null) ? "null" : SafeFeatureName(feature3)));
			if (feature3 != null)
			{
				return feature3;
			}
		}
		catch (Exception ex3)
		{
			Debug.WriteLine("[REPAIR HOLE] InsertFillSurface2 wrapped failed: " + ex3.GetType().Name + " - " + ex3.Message);
		}
		try
		{
			model.ClearSelection2(All: true);
			bool flag2 = false;
			foreach (Edge edge2 in edges)
			{
				if (SelectEdge(edge2, flag2))
				{
					flag2 = true;
				}
			}
			if (!flag2)
			{
				Debug.WriteLine("[REPAIR HOLE] FillSurface normal boundary selected=False");
			}
			else
			{
				Feature feature4 = featureManager.InsertFillSurface(2) as Feature;
				Debug.WriteLine("[REPAIR HOLE] InsertFillSurface selection result=" + ((feature4 == null) ? "null" : SafeFeatureName(feature4)));
				if (feature4 != null)
				{
					return feature4;
				}
			}
		}
		catch (Exception ex4)
		{
			Debug.WriteLine("[REPAIR HOLE] InsertFillSurface selection failed: " + ex4.GetType().Name + " - " + ex4.Message);
		}
		finally
		{
			model.ClearSelection2(All: true);
		}
		try
		{
			model.ClearSelection2(All: true);
			bool flag3 = false;
			foreach (Edge edge3 in edges)
			{
				if (SelectEdgeWithMark(edge3, flag3, 1))
				{
					flag3 = true;
				}
			}
			Debug.WriteLine("[REPAIR HOLE] FillSurface marked boundary selected=" + flag3);
			if (!flag3)
			{
				return null;
			}
			Feature feature5 = featureManager.InsertFillSurface(2) as Feature;
			Debug.WriteLine("[REPAIR HOLE] InsertFillSurface marked selection result=" + ((feature5 == null) ? "null" : SafeFeatureName(feature5)));
			return feature5;
		}
		catch (Exception ex5)
		{
			Debug.WriteLine("[REPAIR HOLE] InsertFillSurface marked selection failed: " + ex5.GetType().Name + " - " + ex5.Message);
			return null;
		}
		finally
		{
			model.ClearSelection2(All: true);
		}
	}

	private bool TryGetRepairFillSurfaceCenter(Feature fillFeature, FacePlaneFrame planeFrame, out double[] center)
	{
		center = null;
		List<Face2> featureFaces = GetFeatureFaces(fillFeature);
		foreach (Face2 item in featureFaces)
		{
			if (TryGetFaceTessellationCentroid(item, out center))
			{
				return true;
			}
			if (planeFrame != null && TryGetFaceBoxCenter(item, planeFrame, out center))
			{
				return true;
			}
		}
		return false;
	}

	private List<Face2> GetFeatureFaces(Feature feature)
	{
		List<Face2> list = new List<Face2>();
		if (feature == null)
		{
			return list;
		}
		object obj = null;
		try
		{
			obj = feature.GetFaces();
		}
		catch
		{
		}
		if (!(obj is Array array))
		{
			return list;
		}
		foreach (object item2 in array)
		{
			if (item2 is Face2 item)
			{
				list.Add(item);
				continue;
			}
			try
			{
				Face2 face = (item2 as Face2) ?? (item2 as Face2);
				if (face != null)
				{
					list.Add(face);
				}
			}
			catch
			{
			}
		}
		return list;
	}

	private bool TryGetFaceTessellationCentroid(Face2 face, out double[] center)
	{
		center = null;
		if (face == null)
		{
			return false;
		}
		List<double> list = new List<double>();
		try
		{
			if (face.GetTessTriangles(NoConversion: true) is Array array)
			{
				foreach (object item in array)
				{
					if (item != null)
					{
						list.Add(Convert.ToDouble(item, CultureInfo.InvariantCulture));
					}
				}
			}
		}
		catch (Exception ex)
		{
			Debug.WriteLine("[REPAIR HOLE] tessellation centroid failed: " + ex.Message);
		}
		if (list.Count < 9)
		{
			return false;
		}
		double num = 0.0;
		double[] array2 = new double[3];
		for (int i = 0; i + 8 < list.Count; i += 9)
		{
			double[] array3 = new double[3]
			{
				list[i],
				list[i + 1],
				list[i + 2]
			};
			double[] array4 = new double[3]
			{
				list[i + 3],
				list[i + 4],
				list[i + 5]
			};
			double[] array5 = new double[3]
			{
				list[i + 6],
				list[i + 7],
				list[i + 8]
			};
			double[] left = Subtract(array4, array3);
			double[] right = Subtract(array5, array3);
			double num2 = Length(Cross(left, right)) * 0.5;
			if (!(num2 <= 1E-12))
			{
				double[] vector = Scale(Add(Add(array3, array4), array5), 1.0 / 3.0);
				array2 = Add(array2, Scale(vector, num2));
				num += num2;
			}
		}
		if (num <= 1E-12)
		{
			return false;
		}
		center = Scale(array2, 1.0 / num);
		return true;
	}

	private bool TryGetFaceBoxCenter(Face2 face, FacePlaneFrame planeFrame, out double[] center)
	{
		center = null;
		if (face == null || planeFrame == null)
		{
			return false;
		}
		double[] array = null;
		try
		{
			array = face.GetBox() as double[];
		}
		catch
		{
		}
		if (array == null || array.Length < 6)
		{
			return false;
		}
		double[][] array2 = new double[4][]
		{
			new double[3]
			{
				array[0],
				array[1],
				array[2]
			},
			new double[3]
			{
				array[3],
				array[4],
				array[5]
			},
			new double[3]
			{
				array[0],
				array[4],
				array[2]
			},
			new double[3]
			{
				array[3],
				array[1],
				array[5]
			}
		};
		double num = double.MaxValue;
		double num2 = double.MinValue;
		double num3 = double.MaxValue;
		double num4 = double.MinValue;
		double num5 = 0.0;
		int num6 = 0;
		double[][] array3 = array2;
		foreach (double[] left in array3)
		{
			double[] left2 = Subtract(left, planeFrame.Origin);
			double val = Dot(left2, planeFrame.AxisU);
			double val2 = Dot(left2, planeFrame.AxisV);
			double num7 = Dot(left2, planeFrame.Normal);
			num = Math.Min(num, val);
			num2 = Math.Max(num2, val);
			num3 = Math.Min(num3, val2);
			num4 = Math.Max(num4, val2);
			num5 += num7;
			num6++;
		}
		if (num6 == 0)
		{
			return false;
		}
		center = planeFrame.ToModel((num + num2) * 0.5, (num3 + num4) * 0.5, num5 / (double)num6);
		return true;
	}

	private void DeleteTemporaryRepairFillFeatures(ModelDoc2 model, List<Feature> features)
	{
		if (model == null || features == null || features.Count == 0)
		{
			return;
		}
		try
		{
			model.ClearSelection2(All: true);
			bool flag = false;
			foreach (Feature feature in features)
			{
				if (feature != null && SelectFeatureWithMark(feature, flag, 0))
				{
					flag = true;
				}
			}
			if (flag)
			{
				model.EditDelete();
				Debug.WriteLine("[REPAIR HOLE] deleted temporary fill surfaces=" + features.Count);
			}
		}
		catch (Exception ex)
		{
			Debug.WriteLine("[REPAIR HOLE] delete temporary fill surfaces failed: " + ex.Message);
		}
		finally
		{
			model.ClearSelection2(All: true);
		}
	}

	private List<Feature> TryCreateRepairReferencePoints(ModelDoc2 model, List<Feature> fillFeatures)
	{
		List<Feature> list = new List<Feature>();
		if (model == null || fillFeatures == null || fillFeatures.Count == 0)
		{
			return list;
		}
		int num = 0;
		foreach (Feature fillFeature in fillFeatures)
		{
			num++;
			Feature item = TryCreateRepairReferencePoint(model, fillFeature, num);
			list.Add(item);
		}
		Debug.WriteLine("[REPAIR HOLE] reference point features=" + list.Count((Feature p) => p != null) + "/" + fillFeatures.Count);
		return list;
	}

	private Feature TryCreateRepairReferencePoint(ModelDoc2 model, Feature fillFeature, int index)
	{
		if (model == null || fillFeature == null)
		{
			return null;
		}
		List<Face2> featureFaces = GetFeatureFaces(fillFeature);
		foreach (Face2 item in featureFaces)
		{
			try
			{
				model.ClearSelection2(All: true);
				bool flag = SelectFace(item, append: false);
				Debug.WriteLine("[REPAIR HOLE] reference point face selected=" + flag + ", index=" + index);
				if (flag)
				{
					object raw = ((dynamic)model.FeatureManager).InsertReferencePoint(4, 0, 0.01, 1);
					Feature feature = ExtractFirstFeature(raw);
					if (feature != null)
					{
						feature.Name = "Repair Hole Center Point " + index;
						Debug.WriteLine("[REPAIR HOLE] reference point feature=" + SafeFeatureName(feature));
						return feature;
					}
					Debug.WriteLine("[REPAIR HOLE] InsertReferencePoint returned no feature. index=" + index);
				}
			}
			catch (Exception ex)
			{
				Debug.WriteLine("[REPAIR HOLE] create reference point failed. index=" + index + ", " + ex.GetType().Name + " - " + ex.Message);
			}
			finally
			{
				model.ClearSelection2(All: true);
			}
		}
		return null;
	}

	private Feature ExtractFirstFeature(object raw)
	{
		if (raw == null)
		{
			return null;
		}
		if (raw is Feature result)
		{
			return result;
		}
		if (raw is Array array)
		{
			foreach (object item in array)
			{
				if (item is Feature result2)
				{
					return result2;
				}
			}
		}
		try
		{
			return raw as Feature;
		}
		catch
		{
			return null;
		}
	}

	private Feature TryCreateRepairCenterSketch(ModelDoc2 model, List<double[]> centers)
	{
		if (model == null || centers == null || centers.Count == 0)
		{
			return null;
		}
		try
		{
			model.ClearSelection2(All: true);
			model.SketchManager.Insert3DSketch(UpdateEditRebuild: true);
			Sketch activeSketch = GetActiveSketch(model);
			int num = 0;
			foreach (double[] center in centers)
			{
				if (IsPoint(center))
				{
					SketchPoint sketchPoint = model.SketchManager.CreatePoint(center[0], center[1], center[2]);
					if (sketchPoint != null)
					{
						num++;
					}
				}
			}
			Feature sketchFeature = GetSketchFeature(model, activeSketch);
			if (sketchFeature != null)
			{
				sketchFeature.Name = "Repair Hole Centers";
			}
			model.SketchManager.Insert3DSketch(UpdateEditRebuild: true);
			Debug.WriteLine("[REPAIR HOLE] center sketch points=" + num);
			return sketchFeature;
		}
		catch (Exception ex)
		{
			Debug.WriteLine("[REPAIR HOLE] create center sketch failed: " + ex.GetType().Name + " - " + ex.Message);
			try
			{
				model.SketchManager.Insert3DSketch(UpdateEditRebuild: true);
			}
			catch
			{
			}
			return null;
		}
		finally
		{
			model.ClearSelection2(All: true);
		}
	}

	private void TryInsertDeleteBodyForRepairFillSurfaces(ModelDoc2 model, List<Feature> fillFeatures)
	{
		if (model == null || fillFeatures == null || fillFeatures.Count == 0)
		{
			return;
		}
		List<Body2> list = new List<Body2>();
		foreach (Feature fillFeature in fillFeatures)
		{
			foreach (Face2 featureFace in GetFeatureFaces(fillFeature))
			{
				Body2 body = null;
				try
				{
					body = featureFace.GetBody() as Body2;
				}
				catch
				{
					body = null;
				}
				if (body != null && !list.Contains(body))
				{
					list.Add(body);
				}
			}
		}
		if (list.Count == 0)
		{
			Debug.WriteLine("[REPAIR HOLE] delete body skipped: no surface bodies from fill surfaces.");
			return;
		}
		try
		{
			model.ClearSelection2(All: true);
			bool flag = false;
			foreach (Body2 item in list)
			{
				if (TrySelectBody(item, flag))
				{
					flag = true;
				}
			}
			Debug.WriteLine("[REPAIR HOLE] delete body selected=" + flag + ", bodies=" + list.Count);
			if (flag)
			{
				if (((dynamic)model.FeatureManager).InsertDeleteBody2(false) is Feature feature)
				{
					feature.Name = "Repair Hole Delete Fill Surface Body";
					Debug.WriteLine("[REPAIR HOLE] delete fill surface body feature=" + SafeFeatureName(feature));
				}
				else
				{
					Debug.WriteLine("[REPAIR HOLE] InsertDeleteBody2 returned null.");
				}
			}
		}
		catch (Exception ex)
		{
			Debug.WriteLine("[REPAIR HOLE] delete fill surface body failed: " + ex.GetType().Name + " - " + ex.Message);
		}
		finally
		{
			model.ClearSelection2(All: true);
		}
	}

	private bool TrySelectBody(Body2 body, bool append)
	{
		if (body == null)
		{
			return false;
		}
		ModelDoc2 modelDoc = swApp?.ActiveDoc as ModelDoc2;
		SelectData selectData = null;
		try
		{
			selectData = ((modelDoc?.SelectionManager is SelectionMgr selectionMgr) ? selectionMgr.CreateSelectData() : null);
			if (selectData != null)
			{
				selectData.Mark = 0;
			}
		}
		catch
		{
			selectData = null;
		}
		try
		{
			if ((bool)((dynamic)body).Select2(append, selectData))
			{
				return true;
			}
		}
		catch (Exception ex)
		{
			Debug.WriteLine("[REPAIR HOLE] body Select2(data) failed: " + ex.GetType().Name + " - " + ex.Message);
		}
		try
		{
			if ((bool)((dynamic)body).Select2(append, 0))
			{
				return true;
			}
		}
		catch (Exception ex2)
		{
			Debug.WriteLine("[REPAIR HOLE] body Select2(mark) failed: " + ex2.GetType().Name + " - " + ex2.Message);
		}
		string text = "";
		try
		{
			text = Convert.ToString(((dynamic)body).Name, CultureInfo.InvariantCulture);
		}
		catch
		{
			text = "";
		}
		if (!string.IsNullOrWhiteSpace(text) && modelDoc != null)
		{
			try
			{
				bool flag = modelDoc.Extension.SelectByID2(text, "SURFACEBODY", 0.0, 0.0, 0.0, append, 0, null, 0);
				Debug.WriteLine("[REPAIR HOLE] SelectByID2 SURFACEBODY name=" + text + ", selected=" + flag);
				if (flag)
				{
					return true;
				}
			}
			catch (Exception ex3)
			{
				Debug.WriteLine("[REPAIR HOLE] SelectByID2 SURFACEBODY failed: " + ex3.GetType().Name + " - " + ex3.Message);
			}
		}
		try
		{
			if ((bool)((dynamic)body).Select(append))
			{
				return true;
			}
		}
		catch (Exception ex4)
		{
			Debug.WriteLine("[REPAIR HOLE] body Select failed: " + ex4.GetType().Name + " - " + ex4.Message);
		}
		return false;
	}

	private List<double[]> SampleRepairEdge(Edge edge, int count)
	{
		List<double[]> list = new List<double[]>();
		if (edge?.GetCurve() is Curve curve)
		{
			double Start = 0.0;
			double End = 0.0;
			bool flag = false;
			try
			{
				CurveParamData curveParams = edge.GetCurveParams3();
				if (curveParams != null)
				{
					Start = curveParams.UMinValue;
					End = curveParams.UMaxValue;
					flag = Math.Abs(End - Start) > 1E-09;
				}
			}
			catch
			{
			}
			if (!flag)
			{
				try
				{
					flag = curve.GetEndParams(out Start, out End, out var _, out var _) && Math.Abs(End - Start) > 1E-09;
				}
				catch
				{
				}
			}
			if (flag && count >= 2)
			{
				for (int i = 0; i < count; i++)
				{
					double num = (double)i / (double)(count - 1);
					double parameter = Start + (End - Start) * num;
					try
					{
						double[] array = curve.Evaluate(parameter) as double[];
						if (IsPoint(array))
						{
							list.Add(new double[3]
							{
								array[0],
								array[1],
								array[2]
							});
						}
					}
					catch
					{
					}
				}
			}
		}
		if (list.Count == 0)
		{
			list.AddRange(GetRepairEdgeBoxPoints(edge));
		}
		Debug.WriteLine("[REPAIR HOLE] SampleRepairEdge points=" + list.Count);
		return list;
	}

	private List<double[]> GetRepairEdgeBoxPoints(Edge edge)
	{
		List<double[]> list = new List<double[]>();
		double[] array = null;
		try
		{
			array = ((dynamic)edge).GetBox() as double[];
		}
		catch
		{
		}
		if (array == null || array.Length < 6)
		{
			try
			{
				Entity entity = edge as Entity;
				array = ((dynamic)entity).GetBox() as double[];
			}
			catch
			{
			}
		}
		if (array == null || array.Length < 6)
		{
			return list;
		}
		double num = Math.Min(array[0], array[3]);
		double num2 = Math.Min(array[1], array[4]);
		double num3 = Math.Min(array[2], array[5]);
		double num4 = Math.Max(array[0], array[3]);
		double num5 = Math.Max(array[1], array[4]);
		double num6 = Math.Max(array[2], array[5]);
		list.Add(new double[3] { num, num2, num3 });
		list.Add(new double[3] { num4, num5, num6 });
		list.Add(new double[3] { num, num5, num3 });
		list.Add(new double[3] { num4, num2, num6 });
		return list;
	}

	private FacePlaneFrame CreateFacePlaneFrame(Face2 face)
	{
		if (!(face?.GetSurface() is Surface surface))
		{
			return null;
		}
		try
		{
			if (!surface.IsPlane())
			{
				return null;
			}
			if (!(surface.PlaneParams is double[] array) || array.Length < 6)
			{
				return null;
			}
			double[] origin = new double[3]
			{
				array[0],
				array[1],
				array[2]
			};
			double[] array2 = Normalize(new double[3]
			{
				array[3],
				array[4],
				array[5]
			});
			if (array2 == null)
			{
				return null;
			}
			double[] left = ((!(Math.Abs(array2[0]) < 0.9)) ? new double[3] { 0.0, 1.0, 0.0 } : new double[3] { 1.0, 0.0, 0.0 });
			double[] array3 = Normalize(Cross(left, array2));
			if (array3 == null)
			{
				return null;
			}
			double[] array4 = Normalize(Cross(array2, array3));
			if (array4 == null)
			{
				return null;
			}
			return new FacePlaneFrame
			{
				Origin = origin,
				Normal = array2,
				AxisU = array3,
				AxisV = array4
			};
		}
		catch (Exception ex)
		{
			Debug.WriteLine("[REPAIR HOLE] CreateFacePlaneFrame failed: " + ex.Message);
			return null;
		}
	}

	private bool TryGetLoopCenter(List<double[]> points, List<double[]> circleCenters, FacePlaneFrame planeFrame, out double[] center, out double width, out double height)
	{
		center = null;
		width = 0.0;
		height = 0.0;
		if (points == null || points.Count == 0)
		{
			return false;
		}
		if (planeFrame != null && TryGetLoopCenterOnFacePlane(points, circleCenters, planeFrame, out center, out width, out height))
		{
			return true;
		}
		double num = double.MaxValue;
		double num2 = double.MaxValue;
		double num3 = double.MaxValue;
		double num4 = double.MinValue;
		double num5 = double.MinValue;
		double num6 = double.MinValue;
		foreach (double[] point in points)
		{
			if (IsPoint(point))
			{
				num = Math.Min(num, point[0]);
				num2 = Math.Min(num2, point[1]);
				num3 = Math.Min(num3, point[2]);
				num4 = Math.Max(num4, point[0]);
				num5 = Math.Max(num5, point[1]);
				num6 = Math.Max(num6, point[2]);
			}
		}
		if (num == double.MaxValue)
		{
			return false;
		}
		double num7 = num4 - num;
		double num8 = num5 - num2;
		double num9 = num6 - num3;
		double[] array = new double[3] { num7, num8, num9 };
		Array.Sort(array);
		width = array[2];
		height = array[1];
		if (circleCenters != null && circleCenters.Count > 0)
		{
			double[] array2 = new double[3];
			int num10 = 0;
			foreach (double[] circleCenter in circleCenters)
			{
				if (IsPoint(circleCenter))
				{
					array2 = Add(array2, circleCenter);
					num10++;
				}
			}
			if (num10 > 0)
			{
				center = Scale(array2, 1.0 / (double)num10);
				return true;
			}
		}
		center = new double[3]
		{
			(num + num4) * 0.5,
			(num2 + num5) * 0.5,
			(num3 + num6) * 0.5
		};
		return true;
	}

	private bool TryGetLoopCenterOnFacePlane(List<double[]> points, List<double[]> circleCenters, FacePlaneFrame planeFrame, out double[] center, out double width, out double height)
	{
		center = null;
		width = 0.0;
		height = 0.0;
		if (points == null || planeFrame == null)
		{
			return false;
		}
		double num = double.MaxValue;
		double num2 = double.MinValue;
		double num3 = double.MaxValue;
		double num4 = double.MinValue;
		double num5 = 0.0;
		int num6 = 0;
		foreach (double[] point in points)
		{
			if (IsPoint(point))
			{
				double[] left = Subtract(point, planeFrame.Origin);
				double val = Dot(left, planeFrame.AxisU);
				double val2 = Dot(left, planeFrame.AxisV);
				double num7 = Dot(left, planeFrame.Normal);
				num = Math.Min(num, val);
				num2 = Math.Max(num2, val);
				num3 = Math.Min(num3, val2);
				num4 = Math.Max(num4, val2);
				num5 += num7;
				num6++;
			}
		}
		if (num6 == 0 || num == double.MaxValue)
		{
			return false;
		}
		width = num2 - num;
		height = num4 - num3;
		if (circleCenters != null && circleCenters.Count > 0)
		{
			double[] array = new double[3];
			int num8 = 0;
			foreach (double[] circleCenter in circleCenters)
			{
				if (IsPoint(circleCenter))
				{
					array = Add(array, circleCenter);
					num8++;
				}
			}
			if (num8 > 0)
			{
				center = planeFrame.ProjectToPlane(Scale(array, 1.0 / (double)num8));
				return true;
			}
		}
		double u = (num + num2) * 0.5;
		double v = (num3 + num4) * 0.5;
		double w = num5 / (double)num6;
		center = planeFrame.ToModel(u, v, w);
		return true;
	}

	private bool IsRepairHoleLoopSizeCandidate(double major, double minor, double diameterM)
	{
		if (major <= 1E-06 || minor <= 1E-06 || diameterM <= 1E-06)
		{
			return false;
		}
		double num = 0.0005;
		double num2 = Math.Max(diameterM * 4.0, diameterM + 0.01);
		return major >= num && minor >= num && major <= num2;
	}

	private bool ContainsNearPoint(List<double[]> points, double[] point, double tolerance)
	{
		if (points == null || !IsPoint(point))
		{
			return false;
		}
		foreach (double[] point2 in points)
		{
			if (IsPoint(point2) && Distance(point2, point) <= tolerance)
			{
				return true;
			}
		}
		return false;
	}

	private Feature CreateRepairHoleCuts(ModelDoc2 model, Face2 face, List<double[]> centers, List<Feature> repairPointFeatures, double diameterM, double depthM, out int circleCount)
	{
		circleCount = 0;
		if (model == null || face == null || centers == null || centers.Count == 0 || diameterM <= 1E-06 || depthM <= 1E-06)
		{
			return null;
		}
		model.ClearSelection2(All: true);
		if (!SelectFace(face, append: false))
		{
			Debug.WriteLine("[REPAIR HOLE] CreateRepairHoleCuts cannot select face.");
			return null;
		}
		model.SketchManager.InsertSketch(UpdateEditRebuild: true);
		int num = 0;
		List<SketchSegment> list = new List<SketchSegment>();
		FacePlaneFrame planeFrame = CreateFacePlaneFrame(face);
		foreach (double[] center in centers)
		{
			num++;
			double[] array = ProjectRepairPointToSketchPlane(planeFrame, center);
			if (!IsPoint(array))
			{
				Debug.WriteLine("[REPAIR HOLE] skip center " + num + ": cannot project to sketch plane.");
				continue;
			}
			SketchSegment sketchSegment = model.SketchManager.CreateCircleByRadius(array[0], array[1], array[2], diameterM / 2.0);
			if (sketchSegment != null)
			{
				circleCount++;
				list.Add(sketchSegment);
				Feature pointFeature = ((repairPointFeatures != null && num - 1 < repairPointFeatures.Count) ? repairPointFeatures[num - 1] : null);
				TryConstrainRepairCircleToReferencePoint(model, sketchSegment, pointFeature, num);
				continue;
			}
			Debug.WriteLine("[REPAIR HOLE] skip center " + num + ": CreateCircleByRadius failed at (" + (array[0] * 1000.0).ToString("0.###", CultureInfo.InvariantCulture) + "," + (array[1] * 1000.0).ToString("0.###", CultureInfo.InvariantCulture) + "," + (array[2] * 1000.0).ToString("0.###", CultureInfo.InvariantCulture) + ")");
		}
		Debug.WriteLine("[REPAIR HOLE] sketch circles created=" + circleCount + "/" + centers.Count);
		TryConstrainRepairHoleDiameters(model, list, diameterM);
		if (circleCount == 0)
		{
			model.SketchManager.InsertSketch(UpdateEditRebuild: true);
			return null;
		}
		Sketch activeSketch = GetActiveSketch(model);
		Feature sketchFeature = GetSketchFeature(model, activeSketch);
		Debug.WriteLine("[REPAIR HOLE] active sketch feature=" + ((sketchFeature == null) ? "null" : SafeFeatureName(sketchFeature)));
		model.SketchManager.InsertSketch(UpdateEditRebuild: true);
		model.ClearSelection2(All: true);
		Debug.WriteLine("[REPAIR HOLE] select sketch before cut=" + (sketchFeature?.Select2(Append: false, 0) ?? false));
		Feature feature = TryCutBlind(model, depthM);
		if (feature != null)
		{
			feature.Name = "Repair Hole";
			ApplyRepairCutSheetThicknessLink(model, feature, depthM);
		}
		Debug.WriteLine("[REPAIR HOLE] cut feature=" + ((feature == null) ? "null" : SafeFeatureName(feature)));
		return feature;
	}

	private Feature CreateRepairHoleCut(ModelDoc2 model, Face2 face, double[] center, double diameterM, double depthM)
	{
		if (model == null || face == null || !IsPoint(center) || diameterM <= 1E-06 || depthM <= 1E-06)
		{
			return null;
		}
		center = ProjectRepairPointToSketchPlane(CreateFacePlaneFrame(face), center);
		if (!IsPoint(center))
		{
			return null;
		}
		model.ClearSelection2(All: true);
		if (!SelectFace(face, append: false))
		{
			return null;
		}
		model.SketchManager.InsertSketch(UpdateEditRebuild: true);
		SketchSegment sketchSegment = model.SketchManager.CreateCircleByRadius(center[0], center[1], center[2], diameterM / 2.0);
		if (sketchSegment == null)
		{
			model.SketchManager.InsertSketch(UpdateEditRebuild: true);
			return null;
		}
		TryCreateRepairHoleDiameterDimension(model, sketchSegment, center, diameterM);
		model.SketchManager.InsertSketch(UpdateEditRebuild: true);
		Feature feature = TryCutBlind(model, depthM);
		if (feature != null)
		{
			feature.Name = "Repair Hole";
			ApplyRepairCutSheetThicknessLink(model, feature, depthM);
		}
		return feature;
	}

	private double[] ProjectRepairPointToSketchPlane(FacePlaneFrame planeFrame, double[] point)
	{
		if (!IsPoint(point))
		{
			return point;
		}
		if (planeFrame == null)
		{
			return point;
		}
		return planeFrame.ProjectToPlane(point);
	}

	private void TryConstrainRepairCircleToReferencePoint(ModelDoc2 model, SketchSegment circle, Feature pointFeature, int index)
	{
		if (model == null || circle == null || pointFeature == null)
		{
			Debug.WriteLine("[REPAIR HOLE] coincident skipped. index=" + index + ", hasPointFeature=" + (pointFeature != null));
			return;
		}
		SketchPoint repairCircleCenterPoint = GetRepairCircleCenterPoint(circle);
		if (repairCircleCenterPoint == null)
		{
			Debug.WriteLine("[REPAIR HOLE] coincident skipped: no circle center. index=" + index);
			return;
		}
		try
		{
			model.ClearSelection2(All: true);
			bool flag = SelectSketchPoint(repairCircleCenterPoint, append: false);
			bool flag2 = TrySelectReferencePointFeature(pointFeature, append: true);
			Debug.WriteLine("[REPAIR HOLE] coincident select. index=" + index + ", center=" + flag + ", point=" + flag2);
			if (flag && flag2)
			{
				model.SketchAddConstraints("sgCOINCIDENT");
				Debug.WriteLine("[REPAIR HOLE] coincident added. index=" + index + ", point=" + SafeFeatureName(pointFeature));
			}
		}
		catch (Exception ex)
		{
			Debug.WriteLine("[REPAIR HOLE] coincident failed. index=" + index + ", " + ex.GetType().Name + " - " + ex.Message);
		}
		finally
		{
			model.ClearSelection2(All: true);
		}
	}

	private bool TrySelectReferencePointFeature(Feature pointFeature, bool append)
	{
		if (pointFeature == null)
		{
			return false;
		}
		try
		{
			if (pointFeature.Select2(append, 0))
			{
				return true;
			}
		}
		catch
		{
		}
		try
		{
			dynamic specificFeature = pointFeature.GetSpecificFeature2();
			if ((object)specificFeature != null)
			{
				if (specificFeature is Entity entity && entity.Select4(append, null))
				{
					return true;
				}
				if ((bool)specificFeature.Select(append))
				{
					return true;
				}
			}
		}
		catch
		{
		}
		try
		{
			if (swApp?.ActiveDoc is ModelDoc2 modelDoc)
			{
				return modelDoc.Extension.SelectByID2(SafeFeatureName(pointFeature), "DATUMPOINT", 0.0, 0.0, 0.0, append, 0, null, 0);
			}
		}
		catch
		{
			return false;
		}
		return false;
	}

	private void TryConstrainRepairHoleDiameters(ModelDoc2 model, List<SketchSegment> circles, double diameterM)
	{
		if (model == null || circles == null || circles.Count == 0)
		{
			return;
		}
		try
		{
			if (circles.Count > 1)
			{
				model.ClearSelection2(All: true);
				bool flag = false;
				foreach (SketchSegment circle in circles)
				{
					if (SelectSketchSegment(circle, flag))
					{
						flag = true;
					}
				}
				if (flag)
				{
					model.SketchAddConstraints("sgSAMELENGTH");
					Debug.WriteLine("[REPAIR HOLE] equal diameter relation added. circles=" + circles.Count);
				}
			}
		}
		catch (Exception ex)
		{
			Debug.WriteLine("[REPAIR HOLE] equal diameter relation failed: " + ex.Message);
		}
		finally
		{
			model.ClearSelection2(All: true);
		}
		TryCreateRepairHoleDiameterDimension(model, circles[0], GetRepairCircleCenter(circles[0]), diameterM);
	}

	private SketchPoint GetRepairCircleCenterPoint(SketchSegment circle)
	{
		if (circle == null)
		{
			return null;
		}
		try
		{
			if (circle is SketchArc sketchArc && sketchArc.GetCenterPoint2() is SketchPoint result)
			{
				return result;
			}
		}
		catch
		{
		}
		try
		{
			return ((dynamic)circle).GetCenterPoint2() as SketchPoint;
		}
		catch
		{
			return null;
		}
	}

	private double[] GetRepairCircleCenter(SketchSegment circle)
	{
		SketchPoint repairCircleCenterPoint = GetRepairCircleCenterPoint(circle);
		if (repairCircleCenterPoint == null)
		{
			return null;
		}
		return new double[3] { repairCircleCenterPoint.X, repairCircleCenterPoint.Y, repairCircleCenterPoint.Z };
	}

	private void ApplyRepairCutSheetThicknessLink(ModelDoc2 model, Feature feature, double depthM)
	{
		if (model == null || feature == null)
		{
			return;
		}
		IExtrudeFeatureData2 extrudeFeatureData = null;
		bool flag = false;
		try
		{
			extrudeFeatureData = feature.GetDefinition() as IExtrudeFeatureData2;
			if (extrudeFeatureData == null)
			{
				Debug.WriteLine("[REPAIR HOLE] LinkToThickness skipped: feature data is not IExtrudeFeatureData2.");
				return;
			}
			flag = extrudeFeatureData.AccessSelections(model, null);
			Debug.WriteLine("[REPAIR HOLE] LinkToThickness access=" + flag);
			extrudeFeatureData.NormalCut = true;
			extrudeFeatureData.LinkToThickness = true;
			if (depthM > 1E-06)
			{
				extrudeFeatureData.SetDepth(Forward: true, depthM);
			}
			bool flag2 = feature.ModifyDefinition(extrudeFeatureData, model, null);
			Debug.WriteLine("[REPAIR HOLE] LinkToThickness modified=" + flag2 + ", normalCut=" + extrudeFeatureData.NormalCut + ", linkToThickness=" + extrudeFeatureData.LinkToThickness);
		}
		catch (Exception ex)
		{
			Debug.WriteLine("[REPAIR HOLE] LinkToThickness failed: " + ex.GetType().Name + " - " + ex.Message);
		}
		finally
		{
			if (extrudeFeatureData != null && flag)
			{
				try
				{
					extrudeFeatureData.ReleaseSelectionAccess();
				}
				catch
				{
				}
			}
		}
	}

	private void TryCreateRepairHoleDiameterDimension(ModelDoc2 model, SketchSegment circle, double[] center, double diameterM)
	{
		if (model == null || circle == null || !IsPoint(center))
		{
			return;
		}
		try
		{
			model.ClearSelection2(All: true);
			bool flag = false;
			try
			{
				flag = circle.Select4(Append: false, null);
			}
			catch
			{
				flag = ((dynamic)circle).Select(false);
			}
			if (flag)
			{
				object obj2 = model.AddDimension2(center[0] + diameterM * 0.65, center[1] + diameterM * 0.35, center[2]);
				DisplayDimension displayDimension = obj2 as DisplayDimension;
				Dimension dimension = null;
				if (displayDimension != null)
				{
					dimension = displayDimension.GetDimension2(0);
				}
				if (dimension != null)
				{
					TrySetDimensionSystemValue(dimension, diameterM, "Repair Hole diameter");
				}
			}
		}
		catch (Exception ex)
		{
			Debug.WriteLine("[REPAIR HOLE] create diameter dimension failed: " + ex.Message);
		}
		finally
		{
			model.ClearSelection2(All: true);
		}
	}

	private bool TryParseLooseSize(string text, out LooseSize size)
	{
		size = null;
		text = (text ?? "").Trim().ToLowerInvariant().Replace("mm", "")
			.Replace(" ", "")
			.Replace("*", "x")
			.Replace("×", "x");
		string[] array = text.Split(new char[1] { 'x' }, StringSplitOptions.RemoveEmptyEntries);
		if (array.Length != 2)
		{
			return false;
		}
		if (!double.TryParse(array[0].Replace(",", "."), NumberStyles.Float, CultureInfo.InvariantCulture, out var result) || !double.TryParse(array[1].Replace(",", "."), NumberStyles.Float, CultureInfo.InvariantCulture, out var result2) || result <= 0.0 || result2 <= 0.0)
		{
			return false;
		}
		double num = Math.Min(result, result2);
		double num2 = Math.Max(result, result2);
		if (num2 <= num)
		{
			return false;
		}
		size = new LooseSize
		{
			WidthM = num / 1000.0,
			LengthM = num2 / 1000.0
		};
		return true;
	}

	private void CreateLooseSlot(ModelDoc2 model, HolePoint point, double[] edgeDirection, double[] faceNormal, LooseSize size)
	{
		double num = size.WidthM / 2.0;
		double num2 = (size.LengthM - size.WidthM) / 2.0;
		double[] left = new double[3] { point.X, point.Y, point.Z };
		double[] array = Normalize(edgeDirection);
		double[] array2 = Normalize(Cross(faceNormal, array));
		if (array != null && array2 != null)
		{
			double[] array3 = Add(left, Scale(array, 0.0 - num2));
			double[] array4 = Add(left, Scale(array, num2));
			double[] array5 = Add(array3, Scale(array2, num));
			double[] array6 = Add(array4, Scale(array2, num));
			double[] array7 = Add(array3, Scale(array2, 0.0 - num));
			double[] array8 = Add(array4, Scale(array2, 0.0 - num));
			model.SketchManager.CreateLine(array5[0], array5[1], array5[2], array6[0], array6[1], array6[2]);
			model.SketchManager.CreateArc(array4[0], array4[1], array4[2], array6[0], array6[1], array6[2], array8[0], array8[1], array8[2], -1);
			model.SketchManager.CreateLine(array8[0], array8[1], array8[2], array7[0], array7[1], array7[2]);
			model.SketchManager.CreateArc(array3[0], array3[1], array3[2], array7[0], array7[1], array7[2], array5[0], array5[1], array5[2], -1);
		}
	}

	private bool SelectFace(Face2 face, bool append)
	{
		if (face == null)
		{
			return false;
		}
		if (face is Entity entity)
		{
			return entity.Select4(append, null);
		}
		try
		{
			return ((dynamic)face).Select(append);
		}
		catch
		{
			return false;
		}
	}

	private Feature TryCutBlind(ModelDoc2 model, double depthM)
	{
		if (model == null || depthM <= 1E-06)
		{
			return null;
		}
		dynamic featureManager = model.FeatureManager;
		bool[] array = new bool[2] { false, true };
		bool[] array2 = array;
		for (int i = 0; i < array2.Length; i++)
		{
			bool flag = array2[i];
			try
			{
				Feature feature = featureManager.FeatureCut4(true, false, flag, 0, 0, depthM, depthM, false, false, false, false, 0.0, 0.0, false, false, false, false, true, true, true, true, true, false, 0, 0, false, false) as Feature;
				Debug.WriteLine("[REPAIR HOLE] FeatureCut4 blind macro depthMm=" + (depthM * 1000.0).ToString("0.###", CultureInfo.InvariantCulture) + ", reverse=" + flag + ", result=" + ((feature == null) ? "null" : SafeFeatureName(feature)));
				if (feature != null)
				{
					return feature;
				}
			}
			catch (Exception ex)
			{
				Debug.WriteLine("[REPAIR HOLE] FeatureCut4 blind macro failed. reverse=" + flag + ": " + ex.GetType().Name + " - " + ex.Message);
			}
		}
		bool[] array3 = array;
		for (int j = 0; j < array3.Length; j++)
		{
			bool flag2 = array3[j];
			try
			{
				Feature feature2 = featureManager.FeatureCut3(true, false, flag2, 0, 0, depthM, depthM, false, false, false, false, 0.0, 0.0, false, false, false, false, true, true, true, false, false, false, 0, 0, false, false) as Feature;
				Debug.WriteLine("[REPAIR HOLE] FeatureCut3 blind depthMm=" + (depthM * 1000.0).ToString("0.###", CultureInfo.InvariantCulture) + ", reverse=" + flag2 + ", result=" + ((feature2 == null) ? "null" : SafeFeatureName(feature2)));
				if (feature2 != null)
				{
					return feature2;
				}
			}
			catch (Exception ex2)
			{
				Debug.WriteLine("[REPAIR HOLE] FeatureCut3 blind failed. reverse=" + flag2 + ": " + ex2.GetType().Name + " - " + ex2.Message);
			}
		}
		return null;
	}

	private Feature TryCutThroughAll(ModelDoc2 model)
	{
		dynamic featureManager = model.FeatureManager;
		try
		{
			Feature feature = featureManager.FeatureCut4(true, false, false, 0, 1, 0.003, 0.01, false, false, false, false, 0.0174532925199433, 0.0174532925199433, false, false, false, false, true, true, true, true, true, false, 0, 0, false, false) as Feature;
			Debug.WriteLine("[MAKE HOLE] FeatureCut4 macro signature result=" + ((feature == null) ? "null" : SafeFeatureName(feature)));
			if (feature != null)
			{
				return feature;
			}
		}
		catch (Exception ex)
		{
			Debug.WriteLine("[MAKE HOLE] FeatureCut4 macro signature failed: " + ex.GetType().Name + " - " + ex.Message);
		}
		try
		{
			Feature feature2 = featureManager.FeatureCut4(true, false, false, 1, 1, 0.01, 0.01, false, false, false, false, 0.0, 0.0, false, false, false, false, true, true, true, false, false, false, 0, 0, false, false, false, false, false);
			Debug.WriteLine("[MAKE HOLE] FeatureCut4 extended signature result=" + ((feature2 == null) ? "null" : SafeFeatureName(feature2)));
			if (feature2 != null)
			{
				return feature2;
			}
		}
		catch (Exception ex2)
		{
			Debug.WriteLine("[MAKE HOLE] FeatureCut4 extended signature failed: " + ex2.GetType().Name + " - " + ex2.Message);
		}
		try
		{
			Feature feature3 = featureManager.FeatureCut3(true, false, false, 1, 1, 0.01, 0.01, false, false, false, false, 0.0, 0.0, false, false, false, false, true, true, true, false, false, false, 0, 0, false, false);
			Debug.WriteLine("[MAKE HOLE] FeatureCut3 signature result=" + ((feature3 == null) ? "null" : SafeFeatureName(feature3)));
			return feature3;
		}
		catch (Exception ex3)
		{
			Debug.WriteLine("[MAKE HOLE] FeatureCut3 signature failed: " + ex3.GetType().Name + " - " + ex3.Message);
			return null;
		}
	}

	private bool IsPoint(double[] point)
	{
		return point != null && point.Length >= 3;
	}

	private double[] Add(double[] left, double[] right)
	{
		return new double[3]
		{
			left[0] + right[0],
			left[1] + right[1],
			left[2] + right[2]
		};
	}

	private double[] Subtract(double[] left, double[] right)
	{
		return new double[3]
		{
			left[0] - right[0],
			left[1] - right[1],
			left[2] - right[2]
		};
	}

	private double[] Scale(double[] vector, double scale)
	{
		return new double[3]
		{
			vector[0] * scale,
			vector[1] * scale,
			vector[2] * scale
		};
	}

	private double Dot(double[] left, double[] right)
	{
		return left[0] * right[0] + left[1] * right[1] + left[2] * right[2];
	}

	private double[] Cross(double[] left, double[] right)
	{
		return new double[3]
		{
			left[1] * right[2] - left[2] * right[1],
			left[2] * right[0] - left[0] * right[2],
			left[0] * right[1] - left[1] * right[0]
		};
	}

	private double Distance(double[] left, double[] right)
	{
		return Length(Subtract(left, right));
	}

	private double Length(double[] vector)
	{
		return Math.Sqrt(Dot(vector, vector));
	}

	private double[] Normalize(double[] vector)
	{
		double num = Length(vector);
		if (num <= 1E-07)
		{
			return null;
		}
		return Scale(vector, 1.0 / num);
	}
}
