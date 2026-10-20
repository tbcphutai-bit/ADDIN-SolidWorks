using System;
using System.Drawing;
using System.Windows.Forms;
using ADDIN.Commands.AutoFrameJoint;
using ADDIN.UI;

namespace ADDIN
{
    /// <summary>
    /// One TaskPane launch tile for the native Auto Frame Joint selection PMP.
    /// All geometry and SOLIDWORKS API work stays in AutoFrameJointCommandEntry.
    /// Do not modify the existing TaskPane Designer, Check Hole or Mirror.
    /// </summary>
    public partial class BomTaskPaneControl
    {
        private ModernButton btnAutoFrameJoint;

        private void EnsureAutoFrameJointButton()
        {
            if (btnAutoFrameJoint != null || tabModelMacroPage == null)
                return;

            btnAutoFrameJoint = new ModernButton
            {
                Name = "btnAutoFrameJoint",
                Text = "Giao góc Frame",
                Subtitle = "Phân tích · Xem A/B",
                Kind = ModernButtonKind.Tile,
                Icon = TaiIcon.Assembly,
                Tone = ModernButtonTone.Accent,
                Size = new Size(180, 44),
                AutoSize = false,
                Enabled = false,
                TabIndex = 33
            };

            btnAutoFrameJoint.Click += (sender, args) =>
            {
                if (!CanRunModelMacro(btnAutoFrameJoint))
                    return;
                try
                {
                    AutoFrameJointCommandEntry.Open(swApp);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Trace.WriteLine(
                        "[AFJ-LAUNCH] " + ex.ToString());
                    MessageBox.Show(this,
                        "Không thể mở Auto Frame Joint: " + ex.Message,
                        "Auto Frame Joint",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            };

            tabModelMacroPage.Controls.Add(btnAutoFrameJoint);
            SetBomCommandToolTip(btnAutoFrameJoint,
                "Chọn Frame A và Frame B trong Assembly. " +
                "Mở PropertyManager để chọn Frame A/B, phân tích dài/ngắn " +
                "và xem bản sao hình học hiện tại. Chưa mô phỏng vết cắt, không sửa Part.");
        }
    }
}
