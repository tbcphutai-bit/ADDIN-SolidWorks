using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Input;
using SolidWorks.Interop.sldworks;
using ADDIN.Commands;

namespace ADDIN.UI
{
    internal class RepairHoleViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        public event Action<bool> TableVisibilityChanged;

        private bool _isTableVisible;
        private Face2 _cachedFace;
        private List<LenhMakeHole.RepairHoleGroup> _cachedGroups;
        private LenhMakeHole _makeHoleCommand;
        private Func<MakeHoleOptions> _getOptionsFunc;

        public ObservableCollection<RepairHoleRowViewModel> Rows { get; } =
            new ObservableCollection<RepairHoleRowViewModel>();

        public bool IsTableVisible
        {
            get => _isTableVisible;
            set
            {
                if (_isTableVisible != value)
                {
                    _isTableVisible = value;
                    OnPropertyChanged(nameof(IsTableVisible));
                    OnPropertyChanged(nameof(TableVisibility));
                    TableVisibilityChanged?.Invoke(_isTableVisible);
                }
            }
        }

        public Visibility TableVisibility => IsTableVisible ? Visibility.Visible : Visibility.Collapsed;

        public ICommand ScanCommand { get; }
        public ICommand RepairCommand { get; }
        public ICommand ResetCommand { get; }
        public ICommand CancelCommand { get; }

        public RepairHoleViewModel()
        {
            ScanCommand = new RelayCommand(ExecuteScan);
            RepairCommand = new RelayCommand(ExecuteRepair);
            ResetCommand = new RelayCommand(ResetPanel);
            CancelCommand = new RelayCommand(ExecuteCancel);

            // Design-time support for Visual Studio XAML Designer
            if (DesignerProperties.GetIsInDesignMode(new DependencyObject()))
            {
                _isTableVisible = true;
                Rows.Add(new RepairHoleRowViewModel
                {
                    HoleTypeText = "Méo",
                    CurrentSizeText = "Ø4.00",
                    Count = 6,
                    RecommendationText = "3.3 / 4.2",
                    RepairInput = "3.3"
                });
                Rows.Add(new RepairHoleRowViewModel
                {
                    HoleTypeText = "Slot",
                    CurrentSizeText = "5x25",
                    Count = 2,
                    RecommendationText = "5.2 / 6.0",
                    RepairInput = "5x25"
                });
            }
        }

        public void Initialize(LenhMakeHole makeHoleCommand, Func<MakeHoleOptions> getOptionsFunc)
        {
            _makeHoleCommand = makeHoleCommand;
            _getOptionsFunc = getOptionsFunc;
        }

        public void ResetPanel()
        {
            Rows.Clear();
            _cachedFace = null;
            _cachedGroups = null;
            _makeHoleCommand?.ClearRepairHoleCache();
            IsTableVisible = false;
        }

        private void ExecuteScan()
        {
            if (_makeHoleCommand == null)
            {
                System.Windows.Forms.MessageBox.Show(
                    "Lệnh Make Hole / Repair Hole chưa được khởi tạo.",
                    "Repair Hole",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            bool ok = _makeHoleCommand.ScanSelectedFaceForRepair(
                out Face2 face,
                out List<LenhMakeHole.RepairHoleGroup> groups,
                out string message);

            if (!ok)
            {
                ResetPanel();
                System.Windows.Forms.MessageBox.Show(
                    message,
                    "Repair Hole",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            _cachedFace = face;
            _cachedGroups = groups;
            Rows.Clear();

            foreach (var group in groups)
            {
                string sizeText = group.IsSlotFamily
                    ? group.SeedSlotWidthMm.ToString("0.##", CultureInfo.InvariantCulture) + "x" + group.SeedSlotLengthMm.ToString("0.##", CultureInfo.InvariantCulture)
                    : "Ø" + group.EquivalentDiameterMm.ToString("0.00", CultureInfo.InvariantCulture);

                string typeText = group.IsSlotFamily ? "Slot" : "Méo";
                string rcmText = (group.RecommendedDisplayText ?? "").Replace("Ø", "").Trim();
                if (string.IsNullOrEmpty(rcmText))
                {
                    rcmText = "-";
                }

                Rows.Add(new RepairHoleRowViewModel
                {
                    Group = group,
                    HoleTypeText = typeText,
                    CurrentSizeText = sizeText,
                    Count = group.Count,
                    RecommendationText = rcmText,
                    RepairInput = ""
                });
            }

            IsTableVisible = true;
        }

        private void ExecuteRepair()
        {
            if (Rows == null || Rows.Count == 0)
            {
                System.Windows.Forms.MessageBox.Show(
                    "Không có nhóm lỗ méo nào để Repair.",
                    "Repair Hole",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            var selected = new List<LenhMakeHole.RepairHoleBatchItem>();
            var emptyRows = new List<string>();

            foreach (var row in Rows)
            {
                var group = row.Group;
                if (group == null)
                    continue;

                string raw = row.RepairInput?.Trim();
                if (string.IsNullOrWhiteSpace(raw))
                {
                    emptyRows.Add(group.DisplayText ?? row.CurrentSizeText);
                    continue;
                }

                if (!LenhMakeHole.TryParseRepairTargetText(
                        raw,
                        out bool repairAsLoose,
                        out double repairDiameterMm,
                        out double slotWidthMm,
                        out double slotLengthMm,
                        out string normalizedTarget))
                {
                    System.Windows.Forms.MessageBox.Show(
                        "Kích thước Repair không hợp lệ tại " + (group.DisplayText ?? row.CurrentSizeText)
                        + ".\r\n\r\n"
                        + "Nhập 4.2 để tạo lỗ tròn Ø4.2, hoặc nhập 5x25 để tạo Slot 5x25.",
                        "Repair Hole",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }

                selected.Add(new LenhMakeHole.RepairHoleBatchItem
                {
                    SourceEquivalentDiameterMm = group.EquivalentDiameterMm,
                    SourceWidthMm = group.WidthMm,
                    SourceHeightMm = group.HeightMm,
                    SourceCount = group.Count,
                    SourceDisplayText = group.DisplayText,
                    RepairAsLoose = repairAsLoose,
                    RepairDiameterMm = repairDiameterMm,
                    RepairSlotWidthMm = slotWidthMm,
                    RepairSlotLengthMm = slotLengthMm,
                    RepairInputText = normalizedTarget
                });
            }

            if (emptyRows.Count > 0)
            {
                DialogResult skipResult = System.Windows.Forms.MessageBox.Show(
                    "Có " + emptyRows.Count.ToString(CultureInfo.InvariantCulture)
                    + " nhóm lỗ chưa nhập Repair Size.\r\n\r\nBạn có muốn BỎ QUA các nhóm này và tiếp tục Repair các nhóm đã nhập không?",
                    "Repair Hole",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question,
                    MessageBoxDefaultButton.Button2);

                if (skipResult != DialogResult.Yes)
                {
                    return;
                }
            }

            if (selected.Count == 0)
            {
                System.Windows.Forms.MessageBox.Show(
                    "Chưa có nhóm lỗ nào được nhập kích thước Repair.",
                    "Repair Hole",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            if (_makeHoleCommand.CheckRebuildRepairRequired(_cachedFace, _cachedGroups, selected, out var rebuildItems)
                && rebuildItems.Count > 0)
            {
                string warningMsg = _makeHoleCommand.GetRebuildRepairWarningMessage(rebuildItems);
                DialogResult rebuildResult = System.Windows.Forms.MessageBox.Show(
                    warningMsg,
                    "Repair Hole",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2);

                if (rebuildResult != DialogResult.Yes)
                {
                    // Return to table preserving all input values
                    return;
                }
            }

            MakeHoleOptions options = _getOptionsFunc != null ? _getOptionsFunc() : new MakeHoleOptions();

            bool ok = _makeHoleCommand.ExecuteRepairHoleBatch(
                _cachedFace,
                selected,
                options,
                out int repairedCount,
                out string repairMsg);

            if (!ok)
            {
                System.Windows.Forms.MessageBox.Show(
                    "Lỗi Repair Hole: " + repairMsg,
                    "Repair Hole",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Hand);
                return;
            }

            string resultMsg = "Đã tạo Repair Hole: " + repairedCount + " lỗ."
                + (string.IsNullOrWhiteSpace(repairMsg) ? "" : "\r\n" + repairMsg);
            System.Windows.Forms.MessageBox.Show(
                resultMsg,
                "Repair Hole",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);

            // After successful repair: Clear table and collapse it again
            ResetPanel();
        }

        private void ExecuteCancel()
        {
            ResetPanel();
        }

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
