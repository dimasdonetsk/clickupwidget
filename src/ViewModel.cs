using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Media;

namespace ClickUpWidget
{
    public abstract class Observable : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
        {
            if (Equals(field, value)) return false;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
            return true;
        }

        protected void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public sealed class RelayCommand : ICommand
    {
        private readonly Action<object?> _run;
        public RelayCommand(Action<object?> run) { _run = run; }
        public RelayCommand(Action run) { _run = _ => run(); }
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => _run(parameter);
    }

    /// <summary>Рядок задачі.</summary>
    public sealed class TaskRow : Observable
    {
        public TaskItem Task { get; }
        public string Project => Task.Project;
        public bool HasProject => Task.Project.Length > 0;
        public Brush ChipBack { get; }
        public Brush ChipFore { get; }
        public string Title => Task.Title;
        public string Tooltip => Task.RawName;
        public string DueText { get; }
        public bool HasDue => DueText.Length > 0;
        public Brush StatusBrush { get; }
        /// <summary>Статус-тип «open» (не почато) — кружечок без заливки.</summary>
        public bool Hollow { get; }

        private bool _isRunning;
        public bool IsRunning { get => _isRunning; set => Set(ref _isRunning, value); }

        public TaskRow(TaskItem task, DateTime now)
        {
            Task = task;
            (ChipBack, ChipFore) = Theme.Chip(task.Project);
            DueText = TaskText.FormatDue(task.Due, now);
            StatusBrush = Theme.StatusBrush(task.StatusColor);
            Hollow = string.Equals(task.StatusType, "open", StringComparison.OrdinalIgnoreCase);
        }
    }

    public sealed class StatusGroup
    {
        public string Name { get; init; } = "";
        public Brush Brush { get; init; } = Brushes.Gray;
        public bool Hollow { get; init; }
        public int Count => Rows.Count;
        public ObservableCollection<TaskRow> Rows { get; } = new();
    }

    /// <summary>Усе, що показують панель і флайаут.</summary>
    public sealed class DashboardViewModel : Observable
    {
        public ObservableCollection<TaskRow> DueRows { get; } = new();
        public ObservableCollection<StatusGroup> Groups { get; } = new();

        private int _openCount;
        public int OpenCount { get => _openCount; set { if (Set(ref _openCount, value)) Raise(nameof(HasTasks)); } }
        public bool HasTasks => OpenCount > 0;

        private int _dueCount;
        public int DueCount { get => _dueCount; set { if (Set(ref _dueCount, value)) Raise(nameof(HasDue)); } }
        public bool HasDue => DueCount > 0;

        private string _todayText = "";
        public string TodayText { get => _todayText; set => Set(ref _todayText, value); }

        // Таймер
        private bool _hasTimer;
        public bool HasTimer { get => _hasTimer; set => Set(ref _hasTimer, value); }
        private string _timerTitle = "";
        public string TimerTitle { get => _timerTitle; set => Set(ref _timerTitle, value); }
        private string _timerProject = "";
        public string TimerProject { get => _timerProject; set { if (Set(ref _timerProject, value)) Raise(nameof(HasTimerProject)); } }
        public bool HasTimerProject => TimerProject.Length > 0;
        private Brush _timerChipBack = Brushes.Transparent;
        public Brush TimerChipBack { get => _timerChipBack; set => Set(ref _timerChipBack, value); }
        private Brush _timerChipFore = Brushes.Transparent;
        public Brush TimerChipFore { get => _timerChipFore; set => Set(ref _timerChipFore, value); }
        private string _timerClock = "";
        public string TimerClock { get => _timerClock; set => Set(ref _timerClock, value); }

        // Стан
        private string _stateText = "Завантажую…";
        public string StateText { get => _stateText; set => Set(ref _stateText, value); }
        private bool _needsToken;
        public bool NeedsToken { get => _needsToken; set => Set(ref _needsToken, value); }
        private bool _loaded;
        public bool Loaded { get => _loaded; set => Set(ref _loaded, value); }

        private string _updateText = "";
        public string UpdateText { get => _updateText; set { if (Set(ref _updateText, value)) Raise(nameof(HasUpdate)); } }
        public bool HasUpdate => UpdateText.Length > 0;

        // Дії (їх підставляє AppController)
        public ICommand OpenTask { get; set; } = new RelayCommand(() => { });
        public ICommand ToggleTimer { get; set; } = new RelayCommand(() => { });
        public ICommand StopTimer { get; set; } = new RelayCommand(() => { });
        public ICommand Refresh { get; set; } = new RelayCommand(() => { });
        public ICommand ShowMenu { get; set; } = new RelayCommand(() => { });
        public ICommand OpenClickUp { get; set; } = new RelayCommand(() => { });
        public ICommand AddToken { get; set; } = new RelayCommand(() => { });
        public ICommand ApplyUpdate { get; set; } = new RelayCommand(() => { });
    }
}
