using Uncycled.Server.Net;
using Uncycled.Server.Rooms;
using Xunit;

namespace Uncycled.Server.Tests
{
    /// <summary>Design 5.9: a joining name equal to one already in the room gets " 2", " 3", ...</summary>
    public class NameRulesTests
    {
        [Fact]
        public void A_free_name_is_kept()
        {
            Assert.Equal("Sam", NameRules.Unique("Sam", new[] { "Alex", "Bot 1" }));
        }

        [Fact]
        public void A_taken_name_gets_the_next_free_number()
        {
            Assert.Equal("Sam 2", NameRules.Unique("Sam", new[] { "Sam" }));
            Assert.Equal("Sam 3", NameRules.Unique("Sam", new[] { "Sam", "Sam 2" }));
            Assert.Equal("Sam 2", NameRules.Unique("Sam", new[] { "Sam", "Sam 3" }));
        }

        [Fact]
        public void The_comparison_ignores_case()
        {
            Assert.Equal("sam 2", NameRules.Unique("sam", new[] { "SAM" }));
            Assert.Equal("Sam 3", NameRules.Unique("Sam", new[] { "sam", "SAM 2" }));
        }

        [Fact]
        public void The_suffix_stays_inside_the_name_length_limit()
        {
            string full = new string('a', Codec.MaxNameLength);

            string result = NameRules.Unique(full, new[] { full });

            Assert.Equal(Codec.MaxNameLength, result.Length);
            Assert.EndsWith(" 2", result);
            Assert.NotEqual(full, result);
        }

        [Fact]
        public void A_truncated_base_does_not_leave_a_double_space()
        {
            string name = new string('a', Codec.MaxNameLength - 3) + " bc"; // 'a' x13 + " bc" = 16 chars
            string result = NameRules.Unique(name, new[] { name });

            Assert.DoesNotContain("  ", result);
            Assert.True(result.Length <= Codec.MaxNameLength);
        }

        [Fact]
        public void The_cut_never_splits_an_emoji()
        {
            string name = "a" + string.Concat(System.Linq.Enumerable.Repeat("\U0001F600", 7)); // 15 UTF-16 units

            string result = NameRules.Unique(name, new[] { name });

            Assert.True(result.Length <= Codec.MaxNameLength);
            Assert.EndsWith(" 2", result);
            for (int i = 0; i < result.Length; i++)
            {
                if (char.IsHighSurrogate(result[i]))
                {
                    Assert.True(i + 1 < result.Length && char.IsLowSurrogate(result[i + 1]), "lone high surrogate at " + i);
                    i++;
                }
                else
                {
                    Assert.False(char.IsLowSurrogate(result[i]), "lone low surrogate at " + i);
                }
            }
        }

        [Fact]
        public void The_cut_keeps_a_joined_emoji_sequence_whole()
        {
            string family = "\U0001F468\u200D\U0001F469\u200D\U0001F467"; // one text element, 8 units
            string name = family + family; // 16 units, the limit

            string result = NameRules.Unique(name, new[] { name });

            Assert.Equal(family + " 2", result);
        }
    }
}
