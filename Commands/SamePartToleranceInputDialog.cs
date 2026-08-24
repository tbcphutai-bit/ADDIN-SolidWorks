using System;
using System.Windows.Forms;

namespace ADDIN.Commands
{
    internal sealed partial class SamePartToleranceInputDialog : Form
    {
        private static SamePartToleranceOptions lastOptions = new SamePartToleranceOptions();

        public SamePartToleranceInputDialog()
        {
            InitializeComponent();
            LoadValues(lastOptions);
        }

        public SamePartToleranceOptions Options { get; private set; }

        private void LoadValues(SamePartToleranceOptions options)
        {
            SamePartToleranceOptions value = options ?? new SamePartToleranceOptions();
            SetValue(numAreaAbsolute, value.AreaAbsoluteMm2);
            SetValue(numAreaRelative, value.AreaRelativePercent);
            SetValue(numEdgeLength, value.EdgeLengthMm);
            SetValue(numVolumeAbsolute, value.VolumeAbsoluteMm3);
            SetValue(numVolumeRelative, value.VolumeRelativePercent);
            SetValue(numPrincipalMoment, value.PrincipalMomentRelativePercent);
            SetValue(numHoleLinear, value.HoleLinearMm);
            SetValue(numHoleRadius, value.HoleRadiusMm);
        }

        private static void SetValue(NumericUpDown control, double value)
        {
            decimal converted;
            try { converted = Convert.ToDecimal(value); }
            catch { converted = control.Minimum; }
            control.Value = Math.Min(control.Maximum, Math.Max(control.Minimum, converted));
        }

        private void btnRun_Click(object sender, EventArgs e)
        {
            Options = new SamePartToleranceOptions
            {
                AreaAbsoluteMm2 = (double)numAreaAbsolute.Value,
                AreaRelativePercent = (double)numAreaRelative.Value,
                EdgeLengthMm = (double)numEdgeLength.Value,
                VolumeAbsoluteMm3 = (double)numVolumeAbsolute.Value,
                VolumeRelativePercent = (double)numVolumeRelative.Value,
                PrincipalMomentRelativePercent = (double)numPrincipalMoment.Value,
                HoleLinearMm = (double)numHoleLinear.Value,
                HoleRadiusMm = (double)numHoleRadius.Value
            };
            lastOptions = Options.Clone();
            System.Diagnostics.Debug.WriteLine(
                "[CHECK SAME PART][TOLERANCE] Accepted. AreaAbs=" + Options.AreaAbsoluteMm2
                + ", AreaRelPercent=" + Options.AreaRelativePercent
                + ", Edge=" + Options.EdgeLengthMm
                + ", VolumeAbs=" + Options.VolumeAbsoluteMm3
                + ", VolumeRelPercent=" + Options.VolumeRelativePercent
                + ", MomentPercent=" + Options.PrincipalMomentRelativePercent
                + ", HoleLinear=" + Options.HoleLinearMm
                + ", HoleRadius=" + Options.HoleRadiusMm);
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
