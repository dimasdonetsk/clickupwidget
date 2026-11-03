using System;
using System.Collections.Generic;
using System.Linq;

namespace ClickUpWidget
{
    /// <summary>Частина коментаря: або текст, або згадка людини (@).</summary>
    public sealed record CommentPart(string? Text, long? UserId)
    {
        public static CommentPart Plain(string text) => new(text, null);
        public static CommentPart Tag(long userId) => new(null, userId);
    }

    /// <summary>Розбір тексту коментаря зі згадками. Без вікон — покривається тестами.</summary>
    public static class CommentText
    {
        /// <summary>
        /// Слово після @ перед курсором, яке людина зараз набирає: «Привіт @оле|» → (7, "оле").
        /// null — курсор не в згадці. @ має стояти на початку або після пробілу/нового рядка.
        /// </summary>
        public static (int Start, string Query)? ActiveMention(string text, int caret)
        {
            if (caret < 1 || caret > text.Length) return null;
            for (int i = caret - 1; i >= 0 && caret - i <= 32; i--)
            {
                char c = text[i];
                if (c == '@')
                {
                    if (i > 0 && !char.IsWhiteSpace(text[i - 1])) return null;
                    return (i, text.Substring(i + 1, caret - i - 1));
                }
                if (char.IsWhiteSpace(c)) return null;
            }
            return null;
        }

        /// <summary>Кандидати для згадки: спершу учасники задачі, потім решта простору; без мене; не більше max.</summary>
        public static List<(Person Person, bool InTask)> Candidates(
            string query, IEnumerable<Person> taskMembers, IEnumerable<Person> allMembers, long myId, int max = 8)
        {
            var seen = new HashSet<long> { myId };
            var result = new List<(Person, bool)>();
            foreach ((IEnumerable<Person> list, bool inTask) in new[] { (taskMembers, true), (allMembers, false) })
                foreach (Person p in list)
                {
                    if (result.Count >= max) return result;
                    if (!Matches(p, query) || !seen.Add(p.Id)) continue;
                    result.Add((p, inTask));
                }
            return result;
        }

        /// <summary>Збіг з початком імені, будь-якого слова в імені або email.</summary>
        public static bool Matches(Person p, string query)
        {
            if (query.Length == 0) return true;
            if (p.Name.StartsWith(query, StringComparison.CurrentCultureIgnoreCase)) return true;
            if (p.Email.StartsWith(query, StringComparison.OrdinalIgnoreCase)) return true;
            return p.Name.Split(new[] { ' ', '.', '_', '-' }, StringSplitOptions.RemoveEmptyEntries)
                .Any(w => w.StartsWith(query, StringComparison.CurrentCultureIgnoreCase));
        }

        /// <summary>
        /// Текст → частини для ClickUp. «@Ім'я» з mentions стає згадкою; решта лишається текстом.
        /// Довші імена перевіряються першими, щоб «@Олена Петренко» не сплутати з «@Олена».
        /// </summary>
        public static List<CommentPart> Build(string text, IEnumerable<Person> mentions)
        {
            List<Person> names = mentions.GroupBy(p => p.Id).Select(g => g.First())
                .OrderByDescending(p => p.Name.Length).ToList();
            var parts = new List<CommentPart>();
            int plainStart = 0, i = 0;
            while (i < text.Length)
            {
                if (text[i] == '@' && (i == 0 || char.IsWhiteSpace(text[i - 1])))
                {
                    Person? hit = names.FirstOrDefault(p =>
                        string.CompareOrdinal(text, i + 1, p.Name, 0, p.Name.Length) == 0
                        && (i + 1 + p.Name.Length == text.Length || !char.IsLetterOrDigit(text[i + 1 + p.Name.Length])));
                    if (hit != null)
                    {
                        if (i > plainStart) parts.Add(CommentPart.Plain(text.Substring(plainStart, i - plainStart)));
                        parts.Add(CommentPart.Tag(hit.Id));
                        i += 1 + hit.Name.Length;
                        plainStart = i;
                        continue;
                    }
                }
                i++;
            }
            if (plainStart < text.Length) parts.Add(CommentPart.Plain(text.Substring(plainStart)));
            return parts;
        }
    }
}
