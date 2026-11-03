using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace ClickUpWidget.Tests
{
    public class InboxTests
    {
        private static TaskItem T(string id, string status, long updated) =>
            new() { Id = id, Status = status, UpdatedMs = updated, Title = "Задача " + id, Project = "АП", StatusType = "custom" };

        private static InboxState Seeded(params TaskItem[] tasks)
        {
            var state = new InboxState();
            Inbox.Diff(state, tasks, 1000, new Dictionary<string, string>());
            return state;
        }

        [Fact]
        public void FirstRunOnlyRemembers()
        {
            var state = new InboxState();
            var (events, check) = Inbox.Diff(state, new[] { T("a", "to do", 100) }, 1000, new Dictionary<string, string>());
            Assert.Empty(events);
            Assert.Empty(check);
            Assert.True(state.Seeded);
        }

        [Fact]
        public void StatusChangeBySomeoneElseIsAnEvent()
        {
            InboxState state = Seeded(T("a", "to do", 100));
            var (events, _) = Inbox.Diff(state, new[] { T("a", "client reviews", 1500) }, 2000, new Dictionary<string, string>());
            InboxEvent e = Assert.Single(events);
            Assert.Equal("status", e.Kind);
            Assert.Equal("to do", e.From);
            Assert.Equal("client reviews", e.To);
        }

        [Fact]
        public void MyOwnStatusChangeIsSkipped()
        {
            InboxState state = Seeded(T("b", "in progress", 100));
            var self = new Dictionary<string, string> { ["b"] = "done" };
            var (events, _) = Inbox.Diff(state, new[] { T("b", "done", 1600) }, 2000, self);
            Assert.Empty(events);
            Assert.Empty(self);
        }

        [Fact]
        public void NewTaskInMyListIsAnAssignment()
        {
            InboxState state = Seeded(T("a", "to do", 100));
            var (events, check) = Inbox.Diff(state, new[] { T("a", "to do", 100), T("new", "to do", 1700) }, 2000, new Dictionary<string, string>());
            InboxEvent e = Assert.Single(events);
            Assert.Equal(Inbox.KindAssigned, e.Kind);
            Assert.Equal("new", e.TaskId);
            Assert.Empty(check);
            Assert.True(state.Tasks.ContainsKey("new"));
        }

        [Fact]
        public void AssignmentsAreHiddenByDefault()
        {
            var s = new Settings();
            Assert.False(s.InboxShows(new InboxEvent { Kind = Inbox.KindAssigned }));
            Assert.True(s.InboxShows(new InboxEvent { Kind = Inbox.KindComment }));
            Assert.False(s.InboxShows(new InboxEvent { Kind = Inbox.KindComment, Mine = true }));
            Assert.True(s.InboxShows(new InboxEvent { Kind = Inbox.KindDue }));
        }

        private static TaskItem Full(string id, string name, DateTime? due = null, string? priority = null, params Person[] assignees) =>
            new()
            {
                Id = id, RawName = name, Title = TaskText.Split(name).Title, Project = TaskText.Split(name).Project,
                Status = "to do", StatusType = "custom", UpdatedMs = 100, Due = due, Priority = priority, Assignees = assignees,
            };

        private static readonly Person Me = new(42, "Я", "", null, "Я");
        private static readonly Person Olena = new(7, "Олена", "", null, "О");
        private static readonly Person Petro = new(8, "Петро", "", null, "П");

        [Fact]
        public void DetectsDuePriorityRenameAndAssignees()
        {
            var state = new InboxState();
            var self = new Dictionary<string, string>();
            Inbox.Diff(state, new[] { Full("a", "АП: Банер - верстка", new DateTime(2026, 11, 5), "normal", Me, Olena) }, 1000, self, Me.Id);

            TaskItem changed = Full("a", "АП: Банер НП - верстка", new DateTime(2026, 11, 7), "urgent", Me, Petro);
            InboxDiff diff = Inbox.Diff(state, new[] { changed }, 2000, self, Me.Id);

            Assert.Equal(new[] { Inbox.KindDue, Inbox.KindPriority, Inbox.KindRenamed, Inbox.KindAssignees },
                diff.Events.Select(e => e.Kind));
            InboxEvent due = diff.Events[0];
            Assert.Equal("5 лис", due.From);
            Assert.Equal("7 лис", due.To);
            Assert.Equal("Звичайний", diff.Events[1].From);
            Assert.Equal("Терміновий", diff.Events[1].To);
            Assert.Equal("Було: Банер — верстка", diff.Events[2].Text);
            Assert.Equal("+ Петро · − Олена", diff.Events[3].Text);

            // Той самий стан вдруге — нових подій немає.
            Assert.Empty(Inbox.Diff(state, new[] { changed }, 3000, self, Me.Id).Events);
        }

        [Fact]
        public void OldStateIsUpgradedSilently()
        {
            // Запис зі старої версії: без назви, дедлайну, пріоритету й виконавців.
            var state = new InboxState { Seeded = true };
            state.Tasks["a"] = new SeenTask { Status = "to do", UpdatedMs = 100 };
            TaskItem t = Full("a", "GM: Лист", new DateTime(2026, 11, 9), "high", Me, Olena);

            Assert.Empty(Inbox.Diff(state, new[] { t }, 2000, new Dictionary<string, string>(), Me.Id).Events);
            Assert.True(state.Tasks["a"].Full);

            TaskItem later = Full("a", "GM: Лист", new DateTime(2026, 11, 10), "high", Me, Olena);
            Assert.Equal(Inbox.KindDue, Assert.Single(Inbox.Diff(state, new[] { later }, 3000, new Dictionary<string, string>(), Me.Id).Events).Kind);
        }

        [Fact]
        public void GoneTasksAreResolved()
        {
            var self = new Dictionary<string, string>();
            var state = new InboxState();
            Inbox.Diff(state, new[] { Full("closed", "АП: Один"), Full("unassigned", "АП: Два"), Full("deleted", "АП: Три"), Full("mine", "АП: Чотири") },
                1000, self, Me.Id);
            InboxDiff diff = Inbox.Diff(state, Array.Empty<TaskItem>(), 2000, self, Me.Id);
            Assert.Equal(4, diff.Gone.Count);

            var closed = new TaskItem { Id = "closed", Title = "Один", Status = "done", StatusType = "closed", Assignees = new[] { Me } };
            Assert.Equal(Inbox.KindClosed, Inbox.ResolveGone(state, "closed", closed, Me.Id, 2000, self)!.Kind);

            var other = new TaskItem { Id = "unassigned", Title = "Два", Status = "to do", StatusType = "custom", Assignees = new[] { Olena } };
            Assert.Equal(Inbox.KindUnassigned, Inbox.ResolveGone(state, "unassigned", other, Me.Id, 2000, self)!.Kind);

            InboxEvent deleted = Inbox.ResolveGone(state, "deleted", null, Me.Id, 2000, self)!;
            Assert.Equal(Inbox.KindDeleted, deleted.Kind);
            Assert.Equal("Три", deleted.Title);

            // Я сам закрив задачу через віджет — без події.
            self["mine"] = "done";
            var mine = new TaskItem { Id = "mine", Status = "done", StatusType = "closed", Assignees = new[] { Me } };
            Assert.Null(Inbox.ResolveGone(state, "mine", mine, Me.Id, 2000, self));
            Assert.Empty(state.Tasks);
        }

        [Fact]
        public void UpdatedTaskIsCheckedForComments()
        {
            InboxState state = Seeded(T("a", "to do", 100), T("b", "to do", 100));
            var (_, check) = Inbox.Diff(state, new[] { T("a", "to do", 1200), T("b", "to do", 100) }, 2000, new Dictionary<string, string>());
            Assert.Equal(new[] { "a" }, check.Select(t => t.Id));
        }

        [Fact]
        public void OnlyNewCommentsAndMineFlagged()
        {
            InboxState state = Seeded(T("a", "to do", 100));      // запам'ятовано на 1000
            var comments = new[]
            {
                new CommentInfo("1", "старий", 5, "Хтось", 900),
                new CommentInfo("2", "Поправ\nбанер  будь ласка", 7, "Олена", 1400),
                new CommentInfo("3", "мій коментар", 42, "Я", 1450),
            };
            List<InboxEvent> events = Inbox.ApplyComments(state, T("a", "to do", 1500), comments, myUserId: 42);

            Assert.Equal(2, events.Count);
            InboxEvent e = events[0];
            Assert.Equal("Олена", e.Who);
            Assert.Equal(7, e.WhoId);
            Assert.Equal("Поправ банер будь ласка", e.Text);
            Assert.False(e.Mine);
            Assert.True(events[1].Mine);      // власний коментар записано, але за замовчуванням не показується
            Assert.Equal(1450, state.Tasks["a"].LastCommentMs);
            Assert.Equal(1500, state.Tasks["a"].UpdatedMs);

            // Повторне читання тих самих коментарів нічого не додає.
            Assert.Empty(Inbox.ApplyComments(state, T("a", "to do", 1500), comments, 42));
        }

        [Fact]
        public void MentionOfMeIsSeparateKind()
        {
            InboxState state = Seeded(T("a", "to do", 100));
            var comments = new[] { new CommentInfo("9", "@Я глянь", 7, "Олена", 1400, new long[] { 42 }) };
            InboxEvent e = Assert.Single(Inbox.ApplyComments(state, T("a", "to do", 1500), comments, 42));
            Assert.Equal(Inbox.KindMention, e.Kind);
        }

        [Fact]
        public void AddDeduplicatesAndSortsNewestFirst()
        {
            var state = new InboxState();
            var a = new InboxEvent { Key = "c:1", AtMs = 100 };
            var b = new InboxEvent { Key = "c:2", AtMs = 300 };
            Inbox.Add(state, new[] { a, b });
            Inbox.Add(state, new[] { a });
            Assert.Equal(new[] { "c:2", "c:1" }, state.Events.Select(e => e.Key));
            state.LastReadMs = 200;
            Assert.Equal(1, Inbox.Unread(state));
        }

        [Fact]
        public void KeepsAtMostMaxEvents()
        {
            var state = new InboxState();
            Inbox.Add(state, Enumerable.Range(0, 100).Select(i => new InboxEvent { Key = "k" + i, AtMs = i }));
            Assert.Equal(Inbox.MaxEvents, state.Events.Count);
            Assert.Equal(99, state.Events[0].AtMs);
        }

        [Fact]
        public void TimeAgoInWords()
        {
            var now = new DateTime(2026, 11, 2, 17, 0, 0);
            long Ms(DateTime d) => new DateTimeOffset(d).ToUnixTimeMilliseconds();
            Assert.Equal("щойно", Inbox.Ago(Ms(now.AddSeconds(-20)), now));
            Assert.Equal("5 хв", Inbox.Ago(Ms(now.AddMinutes(-5)), now));
            Assert.Equal("3 год", Inbox.Ago(Ms(now.AddHours(-3)), now));
            Assert.Equal("вчора", Inbox.Ago(Ms(now.AddDays(-1)), now));
            Assert.Equal("3 дн", Inbox.Ago(Ms(now.AddDays(-3)), now));
        }
    }
}
