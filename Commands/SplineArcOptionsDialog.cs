using System;
using System.Globalization;
using System.Windows.Forms;

namespace ADDIN.Commands
{
    internal sealed partial class SplineArcOptionsDialog : Form
    {
        public SplineArcOptionsDialog()
        {
            InitializeComponent();
            UpdateStepMode();
        }

        public SplineArcOptions Options { get; private set; }

        private void btnCreate_Click(object sender, EventArgs e)
        {
            int manualSegmentCount;
            double toleranceMm;

            if (!TryReadPositiveInteger(txtStep, out manualSegmentCount)
                || manualSegmentCount < 1
                || manualSegmentCount > 200)
            {
                MessageBox.Show(
                    this,
                    "So doan muon chia phai la so nguyen trong khoang 1..200.",
                    "Spline -> Cung R",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                txtStep.Focus();
                txtStep.SelectAll();
                return;
            }

            if (!TryReadPositive(txtTolerance, out toleranceMm))
            {
                MessageBox.Show(
                    this,
                    "Sai so cho phep phai la so lon hon 0 mm.",
                    "Spline -> Cung R",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                txtTolerance.Focus();
                txtTolerance.SelectAll();
                return;
            }

            Options = new SplineArcOptions
            {
                AutomaticStep = chkAutomaticStep.Checked,
                ManualSegmentCount = manualSegmentCount,
                MaximumDeviationMm = toleranceMm,
                SplitWhenOverTolerance = chkAdaptive.Checked,
                AddRadiusDimensions = chkRadiusDimensions.Checked,
                AddStepDimensions = chkStepDimensions.Checked
            };

            DialogResult = DialogResult.OK;
            Close();
        }

        private void chkAutomaticStep_CheckedChanged(object sender, EventArgs e)
        {
            UpdateStepMode();
        }

        private void UpdateStepMode()
        {
            bool manualStep = !chkAutomaticStep.Checked;
            txtStep.Enabled = manualStep;
            lblStep.Enabled = manualStep;
        }

        private static bool TryReadPositive(TextBox textBox, out double value)
        {
            string text = (textBox?.Text ?? string.Empty).Trim();
            bool parsed =
                double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value)
                || double.TryParse(
                    text.Replace(',', '.'),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out value);
            return parsed && value > 0.0 && !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private static bool TryReadPositiveInteger(TextBox textBox, out int value)
        {
            value = 0;
            string text = (textBox?.Text ?? string.Empty).Trim();
            return int.TryParse(
                    text,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out value)
                && value > 0;
        }
    }
}
