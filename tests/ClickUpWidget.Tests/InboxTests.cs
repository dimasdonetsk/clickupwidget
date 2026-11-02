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
        public void NewAssignmentIsNotAnEvent()
        {
            InboxState state = Seeded(T("a", "to do", 100));
            var (events, check) = Inbox.Diff(state, new[] { T("a", "to do", 100), T("new", "to do", 1700) }, 2000, new Dictionary<string, string>());
            Assert.Empty(events);
            Assert.Empty(check);
            Assert.True(state.Tasks.ContainsKey("new"));
        }

        [Fact]
        public void UpdatedTaskIsCheckedForComments()
        {
            InboxState state = Seeded(T("a", "to do", 100), T("b", "to do", 100));
            var (_, check) = Inbox.Diff(state, new[] { T("a", "to do", 1200), T("b", "to do", 100) }, 2000, new Dictionary<string, string>());
            Assert.Equal(new[] { "a" }, check.Select(t => t.Id));
        }

        [Fact]
        public void OnlyNewCommentsFromOthers()
        {
            InboxState state = Seeded(T("a", "to do", 100));      // запам'ятовано на 1000
            var comments = new[]
            {
                new CommentInfo("1", "старий", 5, "Хтось", 900),
                new CommentInfo("2", "Поправ\nбанер  будь ласка", 7, "Олена", 1400),
                new CommentInfo("3", "мій коментар", 42, "Я", 1450),
            };
            List<InboxEvent> events = Inbox.ApplyComments(state, T("a", "to do", 1500), comments, myUserId: 42);

            InboxEvent e = Assert.Single(events);
            Assert.Equal("Олена", e.Who);
            Assert.Equal("Поправ банер будь ласка", e.Text);
            Assert.Equal(1450, state.Tasks["a"].LastCommentMs);
            Assert.Equal(1500, state.Tasks["a"].UpdatedMs);

            // Повторне читання тих самих коментарів нічого не додає.
            Assert.Empty(Inbox.ApplyComments(state, T("a", "to do", 1500), comments, 42));
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
