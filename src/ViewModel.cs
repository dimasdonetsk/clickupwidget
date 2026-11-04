using System;
using System.Collections.Generic;
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

    /// <summary>Рядок, під яким можна відкрити поле коментаря (задача на панелі або подія в інбоксі).</summary>
    public interface IComposerHost
    {
        string TaskId { get; }
        string TaskTitle { get; }
        ComposerModel? Composer { get; set; }
    }

    /// <summary>Поле коментаря під рядком. Текст живе тут, щоб не губитися при перемальовуванні.</summary>
    public sealed class ComposerModel : Observable
    {
        public string TaskId { get; init; } = "";
        public string TaskTitle { get; init; } = "";
        /// <summary>Для відповіді в інбоксі: на чий коментар відповідаємо (одразу підставляється @ім'я).</summary>
        public Person? ReplyTo { get; init; }

        private string _text = "";
        public string Text { get => _text; set => Set(ref _text, value); }

        private bool _busy;
        public bool Busy { get => _busy; set => Set(ref _busy, value); }

        private string _error = "";
        public string Error { get => _error; set { if (Set(ref _error, value)) Raise(nameof(HasError)); } }
        public bool HasError => Error.Length > 0;

        /// <summary>Кого згадали через список @ (ім'я в тексті → людина).</summary>
        public List<Person> Mentions { get; } = new();
    }

    /// <summary>Рядок задачі.</summary>
    public sealed class TaskRow : Observable, IComposerHost
    {
        public string TaskId => Task.Id;
        public string TaskTitle => Task.Title;

        private ComposerModel? _composer;
        public ComposerModel? Composer { get => _composer; set { if (Set(ref _composer, value)) Raise(nameof(IsComposing)); } }
        public bool IsComposing => Composer != null;

        public bool ShowChip { get; init; } = true;
        public bool ShowDue { get; init; } = true;
        public bool ShowPriority { get; init; }
        public bool HasPriority => ShowPriority && !string.IsNullOrEmpty(Task.Priority);
        public Brush PriorityBrush => Theme.StatusBrush(TaskText.PriorityColor(Task.Priority));
        public string PriorityText => TaskText.PriorityLabel(Task.Priority);

        public TaskItem Task { get; }
        public string Project => Task.Project;
        public bool HasProject => ShowChip && Task.Project.Length > 0;
        public Brush ChipBack { get; }
        public Brush ChipFore { get; }
        public string Title => Task.Title;
        public string Tooltip => Task.RawName;
        public string DueText { get; }
        public bool HasDue => ShowDue && DueText.Length > 0;
        public Brush StatusBrush { get; }
        /// <summary>Статус-тип «open» (не почато) — кружечок без заливки.</summary>
        public bool Hollow { get; }

        private bool _isRunning;
        public bool IsRunning { get => _isRunning; set => Set(ref _isRunning, value); }

        /// <summary>Відомо, в якому списку задача (без цього не завантажити статуси).</summary>
        public bool HasList => Task.ListId.Length > 0;

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
        private TaskRow? _runningRow;
        /// <summary>Задача з таймером. Показується один раз: у картці таймера, а якщо картку сховано — першим рядком.</summary>
        public TaskRow? RunningRow { get => _runningRow; set { if (Set(ref _runningRow, value)) Raise(nameof(HasRunningRow)); } }
        public bool HasRunningRow => RunningRow != null;
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

        private int _layout;
        /// <summary>Змінюється, коли в меню змінили «Що показувати» — вигляд перечитує налаштування.</summary>
        public int Layout { get => _layout; set => Set(ref _layout, value); }

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

    /// <summary>Рядок інбоксу: заголовок за типом події, текст і/або «було → стало».</summary>
    public sealed class InboxRow : Observable, IComposerHost
    {
        public InboxEvent Event { get; }
        public string TaskId => Event.TaskId;
        public string TaskTitle => Event.Title;

        private ComposerModel? _composer;
        public ComposerModel? Composer { get => _composer; set { if (Set(ref _composer, value)) Raise(nameof(IsComposing)); } }
        public bool IsComposing => Composer != null;

        public string Project => Event.Project;
        public bool HasProject => Event.Project.Length > 0;
        public Brush ChipBack { get; }
        public Brush ChipFore { get; }
        public string Title => Event.Title;
        public string Header { get; }
        public string Text => Event.Text;
        public bool HasText => Event.Text.Length > 0;

        /// <summary>Рядок «було → стало» (статус, дедлайн, пріоритет, закриття).</summary>
        public bool ShowChange { get; }
        /// <summary>Кольорові кружечки біля «було/стало» (для статусів і пріоритету).</summary>
        public bool ShowDots { get; }
        public bool HasFrom => FromLabel.Length > 0;
        public string FromLabel { get; }
        public string ToLabel { get; }
        public Brush FromBrush { get; }
        public Brush ToBrush { get; }
        /// <summary>На коментар можна відповісти з @ім'ям автора.</summary>
        public bool CanReply => Event.Kind is Inbox.KindComment or Inbox.KindMention && !Event.Mine;

        private string _timeText = "";
        public string TimeText { get => _timeText; set => Set(ref _timeText, value); }

        private bool _isUnread;
        public bool IsUnread { get => _isUnread; set => Set(ref _isUnread, value); }

        public InboxRow(InboxEvent e)
        {
            Event = e;
            (ChipBack, ChipFore) = Theme.Chip(e.Project);
            FromBrush = Theme.StatusBrush(e.FromColor);
            ToBrush = Theme.StatusBrush(e.ToColor);
            string who = e.Who.Length > 0 ? e.Who : "Хтось";

            (Header, ShowChange, ShowDots, FromLabel, ToLabel) = e.Kind switch
            {
                Inbox.KindComment => (e.Mine ? "Ти написав" : who, false, false, "", ""),
                Inbox.KindMention => (who + " згадав(-ла) тебе", false, false, "", ""),
                Inbox.KindStatus => ("Статус", true, true, TaskText.StatusLabel(e.From), TaskText.StatusLabel(e.To)),
                Inbox.KindClosed => ("Задачу закрито", true, true, "", TaskText.StatusLabel(e.To)),
                Inbox.KindDeleted => ("Задачу видалено", false, false, "", ""),
                Inbox.KindAssigned => ("Нова задача на тебе", false, false, "", ""),
                Inbox.KindUnassigned => ("Тебе прибрали з задачі", false, false, "", ""),
                Inbox.KindDue => ("Дедлайн", true, false, e.From, e.To),
                Inbox.KindPriority => ("Пріоритет", true, true, e.From, e.To),
                Inbox.KindRenamed => ("Перейменовано", false, false, "", ""),
                Inbox.KindAssignees => ("Виконавці", false, false, "", ""),
                _ => (e.Kind, false, false, "", ""),
            };
        }
    }

    public sealed class InboxViewModel : Observable
    {
        public ObservableCollection<InboxRow> Rows { get; } = new();

        private int _unread;
        public int Unread { get => _unread; set { if (Set(ref _unread, value)) Raise(nameof(HasUnread)); } }
        public bool HasUnread => Unread > 0;

        private bool _isEmpty = true;
        public bool IsEmpty { get => _isEmpty; set => Set(ref _isEmpty, value); }

        public ICommand Open { get; set; } = new RelayCommand(() => { });
        public ICommand MarkRead { get; set; } = new RelayCommand(() => { });
        public ICommand ShowMenu { get; set; } = new RelayCommand(() => { });
    }
}
