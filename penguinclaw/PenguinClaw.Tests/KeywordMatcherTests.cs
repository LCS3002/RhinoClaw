using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace PenguinClaw.Tests
{
    /// <summary>
    /// Tests for the keyword matching / tokenizer logic used in RhinoCommandRegistry.
    /// The logic is replicated here as pure functions to avoid any Rhino dependencies.
    /// </summary>
    public class KeywordMatcherTests
    {
        // ── Tokenizer (pure replication of RhinoCommandRegistry logic) ──────────

        private static string[] Tokenize(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return Array.Empty<string>();
            return text.ToLowerInvariant()
                       .Split(new[] { ' ', '_', '-', '.', '/', '\\', ',' }, StringSplitOptions.RemoveEmptyEntries);
        }

        private static int ScorePair(string query, string keywords)
        {
            var qTokens = Tokenize(query);
            var kTokens = new HashSet<string>(Tokenize(keywords));
            return qTokens.Count(t => kTokens.Contains(t));
        }

        private static List<(string name, int score)> ScoreAll(
            string query,
            IEnumerable<(string name, string keywords)> tools)
        {
            return tools
                .Select(t => (t.name, score: ScorePair(query, t.keywords)))
                .Where(x => x.score > 0)
                .OrderByDescending(x => x.score)
                .ToList();
        }

        // ── Tokenizer tests ──────────────────────────────────────────────────────

        [Fact]
        public void Tokenize_SimpleWords_SplitsBySpace()
        {
            var tokens = Tokenize("make a box");
            Assert.Contains("make", tokens);
            Assert.Contains("a", tokens);
            Assert.Contains("box", tokens);
        }

        [Fact]
        public void Tokenize_Underscore_SplitsByUnderscore()
        {
            var tokens = Tokenize("boolean_union");
            Assert.Contains("boolean", tokens);
            Assert.Contains("union", tokens);
        }

        [Fact]
        public void Tokenize_MixedCase_LowercasesAll()
        {
            var tokens = Tokenize("Make A BOX");
            Assert.Contains("make", tokens);
            Assert.Contains("a", tokens);
            Assert.Contains("box", tokens);
        }

        [Fact]
        public void Tokenize_EmptyString_ReturnsEmpty()
        {
            Assert.Empty(Tokenize(""));
        }

        [Fact]
        public void Tokenize_WhitespaceOnly_ReturnsEmpty()
        {
            Assert.Empty(Tokenize("   "));
        }

        [Fact]
        public void Tokenize_NullString_ReturnsEmpty()
        {
            Assert.Empty(Tokenize(null!));
        }

        [Fact]
        public void Tokenize_Hyphenated_SplitsByHyphen()
        {
            var tokens = Tokenize("cap-surface");
            Assert.Contains("cap", tokens);
            Assert.Contains("surface", tokens);
        }

        [Fact]
        public void Tokenize_Comma_SplitsByComma()
        {
            var tokens = Tokenize("box,sphere,cylinder");
            Assert.Contains("box", tokens);
            Assert.Contains("sphere", tokens);
            Assert.Contains("cylinder", tokens);
        }

        [Fact]
        public void Tokenize_MultipleSpaces_NoDuplicates()
        {
            var tokens = Tokenize("box   sphere");
            Assert.Equal(2, tokens.Length);
        }

        // ── Scoring tests ────────────────────────────────────────────────────────

        [Fact]
        public void Score_ExactMatch_ReturnsPositive()
        {
            var score = ScorePair("box", "box geometry primitive");
            Assert.True(score > 0);
        }

        [Fact]
        public void Score_NoMatch_ReturnsZero()
        {
            var score = ScorePair("sphere", "linear array polar");
            Assert.Equal(0, score);
        }

        [Fact]
        public void Score_MultipleMatches_HigherThanSingle()
        {
            var scoreHigh = ScorePair("boolean union merge", "boolean union merge operation");
            var scoreLow  = ScorePair("boolean union merge", "boolean something else");
            Assert.True(scoreHigh > scoreLow);
        }

        [Fact]
        public void Score_CaseInsensitive()
        {
            var scoreUpper = ScorePair("BOX", "box geometry");
            var scoreLower = ScorePair("box", "box geometry");
            Assert.Equal(scoreLower, scoreUpper);
        }

        // ── ScoreAll / ranking tests ─────────────────────────────────────────────

        [Fact]
        public void ScoreAll_BestMatchFirst()
        {
            var tools = new[]
            {
                ("boolean_union", "boolean union merge solid"),
                ("boolean_difference", "boolean difference subtract cut"),
                ("list_layers", "list layers document"),
            };
            var results = ScoreAll("boolean union", tools);
            Assert.Equal("boolean_union", results[0].name);
        }

        [Fact]
        public void ScoreAll_ZeroScoreNotIncluded()
        {
            var tools = new[]
            {
                ("list_layers", "list layers"),
                ("delete_object", "delete remove object"),
            };
            var results = ScoreAll("boolean union", tools);
            Assert.Empty(results);
        }

        [Fact]
        public void ScoreAll_ReturnsOnlyMatchingTools()
        {
            var tools = new[]
            {
                ("move_object",   "move translate object"),
                ("delete_object", "delete remove object"),
                ("list_layers",   "list layers document"),
            };
            var results = ScoreAll("move object", tools);
            Assert.True(results.All(r => r.name != "list_layers"));
        }

        [Fact]
        public void ScoreAll_TieBreaking_SameScoreMultipleResults()
        {
            var tools = new[]
            {
                ("tool_a", "box geometry"),
                ("tool_b", "box primitive"),
            };
            var results = ScoreAll("box", tools);
            // Both score 1 — both should appear
            Assert.Equal(2, results.Count);
        }

        [Fact]
        public void ScoreAll_EmptyTools_ReturnsEmpty()
        {
            var results = ScoreAll("box sphere", Array.Empty<(string, string)>());
            Assert.Empty(results);
        }

        [Fact]
        public void ScoreAll_EmptyQuery_ReturnsEmpty()
        {
            var tools = new[] { ("list_layers", "list layers") };
            var results = ScoreAll("", tools);
            Assert.Empty(results);
        }

        [Fact]
        public void ScoreAll_HighRelevanceToolBeatsLow()
        {
            var tools = new[]
            {
                ("gh_sphere",    "grasshopper sphere geometry"),
                ("gh_box",       "grasshopper box geometry primitive create"),
                ("gh_list_item", "grasshopper list item index"),
            };
            var results = ScoreAll("grasshopper box geometry primitive", tools);
            Assert.Equal("gh_box", results[0].name);
        }

        [Fact]
        public void Tokenize_SlashSeparated_Splits()
        {
            var tokens = Tokenize("path/to/file");
            Assert.Contains("path", tokens);
            Assert.Contains("to", tokens);
            Assert.Contains("file", tokens);
        }

        [Fact]
        public void Score_PartialWordMatch_CountsOnly_ExactTokens()
        {
            // "boxes" should NOT match "box"
            var score = ScorePair("boxes", "box geometry");
            Assert.Equal(0, score);
        }
    }
}
