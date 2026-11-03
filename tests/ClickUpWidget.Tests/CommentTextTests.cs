using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace ClickUpWidget.Tests
{
    public class CommentTextTests
    {
        private static readonly Person Olena = new(7, "Олена", "olena@remrkt.me", null, "О");
        private static readonly Person OlenaP = new(9, "Олена Петренко", "op@remrkt.me", null, "ОП");
        private static readonly Person Petro = new(8, "Petro Ivanov", "petro@remrkt.me", null, "PI");
        private static readonly Person Me = new(42, "Dmytro Romanchuk", "dmytro@remrkt.me", null, "DR");

        [Theory]
        [InlineData("Привіт @оле", 11, 7, "оле")]
        [InlineData("@", 1, 0, "")]
        [InlineData("a\n@pe", 5, 2, "pe")]
        public void FindsMentionBeingTyped(string text, int caret, int start, string query)
        {
            var m = CommentText.ActiveMention(text, caret);
            Assert.NotNull(m);
            Assert.Equal(start, m!.Value.Start);
            Assert.Equal(query, m.Value.Query);
        }

        [Theory]
        [InlineData("mail@remrkt", 11)]   // email, не згадка
        [InlineData("@оле на", 7)]        // курсор уже після пробілу
        [InlineData("привіт", 6)]
        public void NoMentionWhenNotTyping(string text, int caret)
        {
            Assert.Null(CommentText.ActiveMention(text, caret));
        }

        [Fact]
        public void TaskMembersFirstWithoutMe()
        {
            var list = CommentText.Candidates("", new[] { Petro, Me }, new[] { Olena, Petro, Me, OlenaP }, Me.Id);
            Assert.Equal(new[] { 8L, 7L, 9L }, list.Select(c => c.Person.Id));
            Assert.True(list[0].InTask);
            Assert.False(list[1].InTask);
        }

        [Theory]
        [InlineData("оле", 2)]
        [InlineData("петр", 1)]     // друге слово в імені
        [InlineData("iv", 1)]       // Ivanov
        [InlineData("op@", 1)]      // email
        [InlineData("zzz", 0)]
        public void MatchesNamesWordsAndEmail(string query, int count)
        {
            Assert.Equal(count, CommentText.Candidates(query, new Person[0], new[] { Olena, OlenaP, Petro }, Me.Id).Count);
        }

        [Fact]
        public void BuildsTagsForMentions()
        {
            List<CommentPart> parts = CommentText.Build("@Олена Петренко глянь, @Petro Ivanov теж. mail@x.com", new[] { Olena, OlenaP, Petro });
            Assert.Equal(4, parts.Count);
            Assert.Equal(9, parts[0].UserId);                 // довше ім'я має пріоритет
            Assert.Equal(" глянь, ", parts[1].Text);
            Assert.Equal(8, parts[2].UserId);
            Assert.Equal(" теж. mail@x.com", parts[3].Text);
        }

        [Fact]
        public void PlainTextWithoutMentions()
        {
            CommentPart p = Assert.Single(CommentText.Build("просто текст", new Person[0]));
            Assert.Equal("просто текст", p.Text);
        }

        [Fact]
        public void NameMustEndAtWordBoundary()
        {
            // «@Оленаааа» — не згадка Олени.
            CommentPart p = Assert.Single(CommentText.Build("@Оленаааа привіт", new[] { Olena }));
            Assert.Null(p.UserId);
        }
    }
}
