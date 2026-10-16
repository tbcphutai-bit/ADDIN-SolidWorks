using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using ADDIN.Commands;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace ADDIN.UI
{
    // MARK: pick cut features (or a face / edge of the cut) and add or remove the
    // MARK token in their names. Same lifetime rules as CHANGE NAME HOLE.
    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.AutoDispatch)]
    public sealed class MarkFeaturePropertyManagerPage : NativeCommandPropertyManagerPage
    {
        private const string PageTitle = "MARK";
        private readonly MarkFeatureCommand command;
        private readonly Action<int, bool> completed;
        private PropertyManagerPageCombobox modeBox;
        private PropertyManagerPageListbox previewBox;
        private List<MarkFeatureCommand.Target> plan = new List<MarkFeatureCommand.Target>();
        private string previewError;
        private bool set;

        internal MarkFeaturePropertyManagerPage(ISldWorks app, MarkFeatureCommand command, bool set,
            Action<NativeCommandPropertyManagerPage> closed, Action<int, bool> completed)
            : base(app, PageTitle, closed)
        { this.command = command; this.set = set; this.completed = completed; }

        protected override bool Supports(int type) { return type == (int)swDocumentTypes_e.swDocPART; }
        protected override bool UsesSelectionBoxes { get { return true; } }
        protected override int SelectionLimit(int mark) { return mark == 1 ? int.MaxValue : 1; }
        protected override int SelectionMark(object item, int type)
        {
            if (type != (int)swSelectType_e.swSelBODYFEATURES &&
                type != (int)swSelectType_e.swSelSKETCHES &&
                type != (int)swSelectType_e.swSelEXTSKETCHSEGS &&
                type != (int)swSelectType_e.swSelSKETCHSEGS &&
                type != (int)swSelectType_e.swSelEXTSKETCHPOINTS &&
                type != (int)swSelectType_e.swSelSKETCHPOINTS &&
                type != (int)swSelectType_e.swSelFACES &&
                type != (int)swSelectType_e.swSelEDGES) return 0;
            return command.CanResolve(item) ? 1 : 0;
        }

        protected override void BuildControls()
        {
            var pick = Group(10, "1. Chỗ cần khắc dấu");

            var box = Selection(pick, 11, "Feature / mặt / cạnh / sketch 2D", 1,
                swSelectType_e.swSelBODYFEATURES,
                swSelectType_e.swSelSKETCHES,
                swSelectType_e.swSelEXTSKETCHSEGS,
                swSelectType_e.swSelSKETCHSEGS,
                swSelectType_e.swSelEXTSKETCHPOINTS,
                swSelectType_e.swSelSKETCHPOINTS,
                swSelectType_e.swSelFACES,
                swSelectType_e.swSelEDGES);
            box.SingleEntityOnly = false; box.Height = 70;
            var action = Group(20, "2. Thao tác");
            modeBox = Combo(action, 21, "Thao tác", new[] { "Đặt MARK", "Bỏ MARK" });
            modeBox.CurrentSelection = (short)(set ? 0 : 1);
            var result = Group(40, "3. Feature sẽ đổi tên");
            previewBox = List(result, 42, "Feature sẽ đổi tên", new string[0]);
            previewBox.Height = 90;
        }

        protected override string HelpText
        {
            get
            {
                return "Chọn feature / sketch 2D trong cây, hoặc bấm vào mặt / cạnh của rãnh hay nét sketch 2D. ✓ đổi tên; ✕ hủy.\n" +
                    "Pattern của feature đã MARK tự theo feature gốc.\n" +
                    "Miệng lỗ vát (Chamfer) tự thành nét khắc, không cần đặt MARK.";
            }
        }

        protected override void OnShown() { if (Interactive) Handle(RefreshPreview); }

        private void RefreshPreview()
        {
            plan = new List<MarkFeatureCommand.Target>();
            previewError = null;
            if (previewBox != null) previewBox.Clear();
            if (Count(1) < 1) return;
            try
            {
                var picks = new List<object>();
                for (int i = 1; i <= Count(1); i++) picks.Add(Manager.GetSelectedObject6(i, 1));
                plan = command.Plan(Model, picks, set);
                if (plan.Count > 0) previewBox.AddItems(plan.Select(t => t.Line).ToArray());
            }
            catch (Exception ex) { plan = new List<MarkFeatureCommand.Target>(); previewError = ex.Message; }
        }

        public override void OnSelectionboxListChanged(int id, int count) { Handle(RefreshPreview); }
        public override void OnComboboxSelectionChanged(int id, int item)
        { Handle(() => { if (id == 21) { set = item == 0; RefreshPreview(); } }); }

        protected override bool Validate(out string message)
        {
            if (Count(1) < 1) { message = "Chọn feature, sketch 2D, hoặc mặt / cạnh của rãnh cần khắc dấu."; return false; }
            if (previewError != null) { message = previewError; return false; }
            int changes = plan.Count(t => t.NewName != null);
            if (changes == 0) { message = set ? "Các feature đã chọn đều đã có MARK." : "Các feature đã chọn không có MARK."; return false; }
            message = "Sẵn sàng: đổi tên " + changes + " feature. Bấm ✓.";
            return true;
        }

        protected override Action CaptureAccepted()
        {
            string message;
            if (!Validate(out message)) throw new InvalidOperationException(message);
            // Names only: SOLIDWORKS tears the page and its selection down before the command runs.
            var renames = plan.Where(t => t.NewName != null)
                .Select(t => new KeyValuePair<string, string>(t.Name, t.NewName)).ToList();
            bool mode = set;
            return () =>
            {
                int changed = command.Apply(Model, renames);
                if (completed != null) completed(changed, mode);
            };
        }
    }
}
