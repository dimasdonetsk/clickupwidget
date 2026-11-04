using System;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace ClickUpWidget.Tests
{
    public class TaskTextTests
    {
        // Справжні назви задач з ClickUp, як їх пишуть у команді.
        [Theory]
        [InlineData("📝 GM: Хелловін – подарунок без страху не вгадати - контент", "GM", "Хелловін – подарунок без страху не вгадати — контент")]
        [InlineData("АП: Телемедицина - верстка", "АП", "Телемедицина — верстка")]
        [InlineData("ML: Sales funnel", "ML", "Sales funnel")]
        [InlineData("💻 АП: Запуск сценарію \"Активація\" - верстка", "АП", "Запуск сценарію \"Активація\" — верстка")]
        [InlineData("⚙️ GM: Запуск сценарію \"Welcome Flow (footer)\" - створення сценарію", "GM", "Запуск сценарію \"Welcome Flow (footer)\" — створення сценарію")]
        [InlineData("Просто задача без префікса", "", "Просто задача без префікса")]
        [InlineData("Нарада о 10:30 з клієнтом", "", "Нарада о 10:30 з клієнтом")]
        [InlineData("im: маленькими", "IM", "маленькими")]
        public void SplitsProjectAndTitle(string raw, string project, string title)
        {
            (string p, string t) = TaskText.Split(raw);
            Assert.Equal(project, p);
            Assert.Equal(title, t);
        }

        [Fact]
        public void FormatsDueInWords()
        {
            var now = new DateTime(2026, 11, 2, 14, 0, 0);   // понеділок
            Assert.Equal("сьогодні", TaskText.FormatDue(now.Date.AddHours(4), now));
            Assert.Equal("завтра", TaskText.FormatDue(now.AddDays(1), now));
            Assert.Equal("вчора", TaskText.FormatDue(now.AddDays(-1), now));
            Assert.Equal("чт", TaskText.FormatDue(now.AddDays(3), now));
            Assert.Equal("", TaskText.FormatDue(null, now));
            Assert.Equal("20 лис", TaskText.FormatDue(new DateTime(2026, 11, 20), now));
            Assert.Equal("28 жов", TaskText.FormatDue(new DateTime(2026, 10, 28), now));
        }

        [Fact]
        public void DueNowIncludesOverdueAndToday()
        {
            var now = new DateTime(2026, 11, 2, 9, 0, 0);
            Assert.True(TaskText.IsDueNow(now.AddDays(-3), now));
            Assert.True(TaskText.IsDueNow(now.Date.AddHours(23), now));
            Assert.False(TaskText.IsDueNow(now.AddDays(1), now));
            Assert.False(TaskText.IsDueNow(null, now));
        }

        [Fact]
        public void GroupsInProgressFirstThenByClickUpOrder()
        {
            TaskItem T(string status, int order, int dueDay) => new()
            {
                Id = status + dueDay, Status = status, StatusOrder = order, StatusType = "custom",
                Due = new DateTime(2026, 11, dueDay), Title = status + dueDay,
            };
            var groups = TaskText.Group(new[]
            {
                T("to do", 0, 5), T("client reviews", 3, 4), T("in progress", 1, 9), T("to do", 0, 3), T("in progress", 1, 2),
            });

            Assert.Equal(new[] { "in progress", "to do", "client reviews" }, groups.Select(g => g.Status));
            Assert.Equal(new[] { "to do3", "to do5" }, groups[1].Tasks.Select(t => t.Title));
        }

        [Fact]
        public void HidesDoneAndClosed()
        {
            Assert.False(TaskText.IsOpen(new TaskItem { StatusType = "closed" }));
            Assert.False(TaskText.IsOpen(new TaskItem { StatusType = "done" }));
            Assert.True(TaskText.IsOpen(new TaskItem { StatusType = "custom" }));
            Assert.True(TaskText.IsOpen(new TaskItem { StatusType = "open" }));
        }

        [Theory]
        [InlineData("cancelled")]
        [InlineData("Canceled")]
        [InlineData("backlog")]
        [InlineData("BACKLOG")]
        [InlineData("Скасовано")]
        public void HidesCancelledAndBacklog(string status)
        {
            Assert.False(TaskText.IsOpen(new TaskItem { Status = status, StatusType = "custom" }));
        }

        [Theory]
        [InlineData("to do")]
        [InlineData("in progress")]
        [InlineData("internal control")]
        [InlineData("client reviews")]
        public void KeepsWorkingStatuses(string status)
        {
            Assert.True(TaskText.IsOpen(new TaskItem { Status = status, StatusType = "custom" }));
        }

        [Fact]
        public void FormatsTime()
        {
            Assert.Equal("2 год 14 хв", TaskText.FormatSpan(new TimeSpan(2, 14, 59)));
            Assert.Equal("35 хв", TaskText.FormatSpan(TimeSpan.FromMinutes(35)));
            Assert.Equal("00:23:41", TaskText.FormatClock(new TimeSpan(0, 23, 41)));
            Assert.Equal("26:00:05", TaskText.FormatClock(new TimeSpan(1, 2, 0, 5)));
        }

        [Fact]
        public void ParsesApiTask()
        {
            const string json = """
            {
              "id": "1240npf6wyt",
              "name": "IM: Ми запускаємо комунікацію в Email – перша розсилка - верстка",
              "status": { "status": "in progress", "color": "#4194f6", "type": "custom", "orderindex": 1 },
              "due_date": "1793448000000",
              "url": "https://app.clickup.com/t/1240npf6wyt",
              "list": { "id": "901", "name": "Email" }
            }
            """;
            using JsonDocument doc = JsonDocument.Parse(json);
            TaskItem t = ClickUpClient.ParseTask(doc.RootElement);

            Assert.Equal("IM", t.Project);
            Assert.Equal("Ми запускаємо комунікацію в Email – перша розсилка — верстка", t.Title);
            Assert.Equal("in progress", t.Status);
            Assert.Equal("#4194f6", t.StatusColor);
            Assert.Equal(1, t.StatusOrder);
            Assert.Equal("901", t.ListId);
            Assert.NotNull(t.Due);
            Assert.Equal(new DateTimeOffset(2026, 10, 31, 12, 0, 0, TimeSpan.Zero).LocalDateTime, t.Due);
        }

        [Fact]
        public void TaskWithoutDueOrStatusColorStillParses()
        {
            using JsonDocument doc = JsonDocument.Parse(
                """{ "id": "a", "name": "Без дати", "status": { "status": "to do", "type": "open", "orderindex": "0" }, "due_date": null }""");
            TaskItem t = ClickUpClient.ParseTask(doc.RootElement);
            Assert.Null(t.Due);
            Assert.Null(t.StatusColor);
            Assert.Equal("", t.ListId);
        }

        [Fact]
        public void UserChoiceOverridesDefaultStatusRules()
        {
            var vis = new System.Collections.Generic.Dictionary<string, bool> { ["backlog"] = true, ["client reviews"] = false };
            var hidden = new System.Collections.Generic.List<string>();
            Assert.True(TaskText.IsVisible(new TaskItem { Status = "Backlog", StatusType = "custom" }, vis, hidden));
            Assert.False(TaskText.IsVisible(new TaskItem { Status = "client reviews", StatusType = "custom" }, vis, hidden));
            Assert.True(TaskText.IsVisible(new TaskItem { Status = "to do", StatusType = "open" }, vis, hidden));
            Assert.False(TaskText.IsVisible(new TaskItem { Status = "cancelled", StatusType = "custom" }, vis, hidden));
            Assert.False(TaskText.IsVisible(new TaskItem { Status = "closed", StatusType = "closed" }, vis, hidden));
        }

        [Fact]
        public void HiddenProjectsAreHidden()
        {
            var vis = new System.Collections.Generic.Dictionary<string, bool>();
            var hidden = new System.Collections.Generic.List<string> { "RM", "" };
            Assert.False(TaskText.IsVisible(new TaskItem { Project = "RM", Status = "to do", StatusType = "open" }, vis, hidden));
            Assert.False(TaskText.IsVisible(new TaskItem { Project = "", Status = "to do", StatusType = "open" }, vis, hidden));
            Assert.True(TaskText.IsVisible(new TaskItem { Project = "АП", Status = "to do", StatusType = "open" }, vis, hidden));
        }

        // Без червоних акцентів: замість яскравих кольорів ClickUp (#f50000 у «Терміновий») — спокійні.
        [Theory]
        [InlineData("urgent")]
        [InlineData("URGENT")]
        [InlineData("high")]
        [InlineData("normal")]
        public void PriorityColorsAreCalm(string priority)
        {
            string? hex = TaskText.PriorityColor(priority);
            Assert.NotNull(hex);
            var c = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);
            // «Червоний» — коли червоний канал сильно переважає і зелений, і синій.
            Assert.False(c.R > 180 && c.G < 120 && c.B < 120, priority + " виглядає червоним: " + hex);
        }

        // Задача з таймером показується один раз — зверху, а не ще й у списках.
        [Fact]
        public void RunningTaskIsNotInLists()
        {
            var tasks = new[]
            {
                new TaskItem { Id = "a", Status = "in progress", StatusType = "custom" },
                new TaskItem { Id = "b", Status = "to do", StatusType = "open" },
                new TaskItem { Id = "c", Status = "to do", StatusType = "open" },
            };

            var lists = TaskText.WithoutRunning(tasks, "a");
            Assert.Equal(new[] { "b", "c" }, lists.Select(t => t.Id));
            // Група, де була лише задача з таймером, зникає разом з нею.
            Assert.DoesNotContain(TaskText.Group(lists), g => g.Status == "in progress");

            Assert.Equal(3, TaskText.WithoutRunning(tasks, null).Count);
        }

        [Fact]
        public void TimerOnTaskOutsideMyListStillHasTitleAndLink()
        {
            TaskItem t = TaskText.FromTimer(new RunningTimer("86c1x2", "АП: Банер - верстка", DateTime.UtcNow));
            Assert.Equal("86c1x2", t.Id);
            Assert.Equal("АП", t.Project);
            Assert.Equal("Банер — верстка", t.Title);
            Assert.Equal("https://app.clickup.com/t/86c1x2", t.Url);
            Assert.Equal("", t.ListId);   // невідомий список — змінити статус звідси не можна
        }

        [Fact]
        public void LowOrNoPriorityIsGrey()
        {
            Assert.Null(TaskText.PriorityColor("low"));
            Assert.Null(TaskText.PriorityColor(null));
        }
    }
}
