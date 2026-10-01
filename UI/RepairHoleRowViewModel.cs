using System.ComponentModel;
using ADDIN.Commands;

namespace ADDIN.UI
{
    internal class RepairHoleRowViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        private string _holeTypeText;
        private string _currentSizeText;
        private int _count;
        private string _recommendationText;
        private string _repairInput = "";

        public string HoleTypeText
        {
            get => _holeTypeText;
            set
            {
                if (_holeTypeText != value)
                {
                    _holeTypeText = value;
                    OnPropertyChanged(nameof(HoleTypeText));
                }
            }
        }

        public string CurrentSizeText
        {
            get => _currentSizeText;
            set
            {
                if (_currentSizeText != value)
                {
                    _currentSizeText = value;
                    OnPropertyChanged(nameof(CurrentSizeText));
                }
            }
        }

        public int Count
        {
            get => _count;
            set
            {
                if (_count != value)
                {
                    _count = value;
                    OnPropertyChanged(nameof(Count));
                }
            }
        }

        public string RecommendationText
        {
            get => _recommendationText;
            set
            {
                if (_recommendationText != value)
                {
                    _recommendationText = value;
                    OnPropertyChanged(nameof(RecommendationText));
                }
            }
        }

        public string RepairInput
        {
            get => _repairInput;
            set
            {
                if (_repairInput != value)
                {
                    _repairInput = value;
                    OnPropertyChanged(nameof(RepairInput));
                }
            }
        }

        public LenhMakeHole.RepairHoleGroup Group { get; set; }

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
