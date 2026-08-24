using System;
using System.Windows.Forms;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace ADDIN.Commands
{
    public class XoaTextMauVang
    {
        private readonly ISldWorks swApp;

        public XoaTextMauVang(ISldWorks app)
        {
            swApp = app;
        }

        public int DeleteDanglingText()
        {
            ModelDoc2 drawingModel = swApp?.ActiveDoc as ModelDoc2;

            if (drawingModel == null ||
                drawingModel.GetType() != (int)swDocumentTypes_e.swDocDRAWING)
            {
                MessageBox.Show(
                    "Vui long mo Drawing.",
                    "Xoa Text mau vang",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);

                return 0;
            }

            DrawingDoc drawing = drawingModel as DrawingDoc;
            if (drawing == null)
                return 0;

            int countDeleted = 0;

            // Quét từ Sheet View rồi tới toàn bộ Drawing View
            SolidWorks.Interop.sldworks.View view =
                drawing.GetFirstView() as SolidWorks.Interop.sldworks.View;

            while (view != null)
            {
                countDeleted += DeleteBrokenTextInView(
                    drawingModel,
                    view);

                view =
                    view.GetNextView() as SolidWorks.Interop.sldworks.View;
            }

            drawingModel.ClearSelection2(true);

            // Không hiện MessageBox khi xóa xong
            return countDeleted;
        }

        private int DeleteBrokenTextInView(
            ModelDoc2 drawingModel,
            SolidWorks.Interop.sldworks.View view)
        {
            object[] annotations = view.GetAnnotations() as object[];

            if (annotations == null || annotations.Length == 0)
                return 0;

            int countDeleted = 0;

            foreach (object item in annotations)
            {
                Annotation annotation = item as Annotation;

                if (annotation == null)
                    continue;

                // Chỉ xử lý Note / Text
                if (annotation.GetType() !=
                    (int)swAnnotationType_e.swNote)
                    continue;

                Note note =
                    annotation.GetSpecificAnnotation() as Note;

                if (note == null)
                    continue;

                // ==========================================
                // KHÔNG XỬ LÝ BALLOON
                // Balloon có class riêng
                // ==========================================
                bool isBomBalloon;

                try
                {
                    isBomBalloon = note.IsBomBalloon();
                }
                catch
                {
                    continue;
                }

                if (isBomBalloon)
                    continue;

                // ==========================================
                // CHỈ XỬ LÝ NOTE CÓ LEADER
                //
                // Note tự do không có leader tuyệt đối
                // không được coi là lỗi.
                // ==========================================
                int leaderCount;

                try
                {
                    leaderCount = annotation.GetLeaderCount();
                }
                catch
                {
                    continue;
                }

                if (leaderCount <= 0)
                    continue;

                // ==========================================
                // KIỂM TRA ENTITY MÀ NOTE ĐANG LIÊN KẾT
                // ==========================================
                int attachedCount;

                try
                {
                    attachedCount =
                        annotation.GetAttachedEntityCount3();
                }
                catch
                {
                    continue;
                }

                object attachedEntities = null;

                try
                {
                    attachedEntities =
                        annotation.GetAttachedEntities3();
                }
                catch
                {
                    // Nếu API không resolve được attachment,
                    // xem như reference đã bị lỗi.
                    attachedEntities = null;
                }

                bool brokenReference =
                    IsBrokenAttachment(
                        attachedCount,
                        attachedEntities);

                if (!brokenReference)
                    continue;

                // ==========================================
                // XÓA NOTE LỖI LIÊN KẾT
                // ==========================================
                drawingModel.ClearSelection2(true);

                if (!annotation.Select3(false, null))
                    continue;

                drawingModel.EditDelete();
                countDeleted++;
            }

            drawingModel.ClearSelection2(true);

            return countDeleted;
        }

        private bool IsBrokenAttachment(
            int attachedCount,
            object attachedEntities)
        {
            // Có leader nhưng không còn entity để bám
            if (attachedCount <= 0)
                return true;

            if (attachedEntities == null)
                return true;

            Array entities = attachedEntities as Array;

            if (entities == null)
                return true;

            if (entities.Length == 0)
                return true;

            // Nếu array tồn tại nhưng toàn bộ phần tử đều null
            // thì reference cũng đã hỏng.
            bool hasValidEntity = false;

            foreach (object entity in entities)
            {
                if (entity != null)
                {
                    hasValidEntity = true;
                    break;
                }
            }

            return !hasValidEntity;
        }
    }
}