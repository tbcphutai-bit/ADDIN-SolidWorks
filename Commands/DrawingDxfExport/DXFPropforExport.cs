// Adapted from user-supplied DXF.zip; see SOURCE.md.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ADDIN.Commands.DrawingDxfExporting
{
    internal class DXFPropforExport
    {
        public string SourceConfiguration { get; set; }
        public string OrderNumber { get; set; }
        public string PartNumber { get; set; }
        public string QTY { get; set; }
        public string UnitName { get; set; }
        public string Thickness { get; set; }
        public string material { get; set; }
        public string compPath { get; set; }
        public string DrwPath { get; set; }
        public string PartPath { get; set; }
        public string FileName { get; set; }
        public string DxfSaveFileName { get; set; }

        /// <summary>true: SPLINE, false: POLYLINE, null: theo cài đặt của máy.</summary>
        public bool? ExportSplinesAsSplines { get; set; }

        /// <summary>Số đường uốn SolidWorks báo trên view trải phẳng (-1: chưa đọc).</summary>
        public int ExpectedBendLineCount { get; set; } = -1;

        /// <summary>
        /// MARK edges whose LASER_MARK marker could not be created on the
        /// temporary Drawing. Reported after the DXF finalizer, which can still
        /// move such an edge by closing the LASER_MARK contour.
        /// </summary>
        public int LaserMarkMissingMarkerCount { get; set; }

        /// <summary>LASER_MARK rings that replaced a through bore (ring R, bore r, mm) - see DxfLayerFinalizer.</summary>
        public List<KeyValuePair<double, double>> MarkRingSubstitutions { get; } =
            new List<KeyValuePair<double, double>>();
        public string LaserMarkMissingMarkerDetail { get; set; } = "";

        /// <summary>
        /// Option: keep user sketches drawn on the source FlatPattern Drawing
        /// View whose layer name contains MARK, exported on LASER_MARK.
        /// </summary>
        public bool KeepDrawingViewMarkSketches { get; set; }

        /// <summary>
        /// Output files already published in the current Flatten->DXF batch
        /// (path -> source Part file name). Two Parts with the same
        /// 手配番号/部品番号 must not silently overwrite each other.
        /// </summary>
        internal IDictionary<string, string> BatchOutputPaths { get; set; }

        /// <summary>MARK sketches read from the source Drawing before Copy.</summary>
        internal DxfOutPut.DrawingViewMarkSketchCollection DrawingViewMarkSketches { get; set; }

        /// <summary>
        /// Private layer of the temporary Drawing that receives marker arcs
        /// SOLIDWORKS drew wrongly (null when the layer could not be created).
        /// The DXF finalizer deletes everything on it.
        /// </summary>
        public string MarkDiscardLayerName { get; set; }

        /// <summary>How the MARK arcs/circles of this export were built.</summary>
        internal DxfOutPut.MarkerArcStats MarkerArcStats { get; } =
            new DxfOutPut.MarkerArcStats();
    }
}
