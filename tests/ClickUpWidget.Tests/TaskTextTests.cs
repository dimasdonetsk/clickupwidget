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
    }
}
