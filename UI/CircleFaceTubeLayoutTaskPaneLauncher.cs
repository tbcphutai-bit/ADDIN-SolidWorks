using System;
using System.Drawing;
using System.Windows.Forms;
using ADDIN.Commands.CircleFaceTubeLayout;
using ADDIN.UI;

namespace ADDIN
{
    /// <summary>
    /// Isolated TaskPane launch tile for Circle Along Face V3 (Tạo ống 架台).
    /// All command execution, dialog lifecycle, and SOLIDWORKS API calls stay inside
    /// CircleFaceTubeLayoutCommandEntry without touching BomTaskPaneControl or PropertyManagerPage.
    /// </summary>
    public partial class BomTaskPaneControl
    {
        private ModernButton btnCircleAlongFace;

        private void EnsureCircleAlongFaceButton()
        {
            if (btnCircleAlongFace != null || tabModelMacroPage == null)
                return;

            btnCircleAlongFace = new ModernButton
            {
                Name = "btnCircleAlongFace",
                Text = "Tạo ống 架台",
                Subtitle = "Part / Assembly · 3D Sketch",
                Kind = ModernButtonKind.Tile,
                Icon = TaiIcon.Curve,
                Tone = ModernButtonTone.Accent,
                Size = new Size(180, 44),
                AutoSize = false,
                Enabled = true,
                TabIndex = 34
            };

            btnCircleAlongFace.Click += (sender, args) =>
            {
                if (IsDisposed || Disposing || !Enabled)
                    return;
                CircleFaceTubeLayoutCommandEntry.Open(swApp, this);
            };

            btnCircleAlongFace.Disposed += (sender, args) =>
            {
                CircleFaceTubeLayoutCommandEntry.CloseActiveSession();
            };

            tabModelMacroPage.Controls.Add(btnCircleAlongFace);
            SetBomCommandToolTip(btnCircleAlongFace,
                "TẠO ỐNG 架台\nChọn một mặt tấm, xem trước và tạo 3D Sketch đường tâm ống.");
        }
    }
}
