using FluentAssertions;

using Sleeky.Todo.Domain.Services;

namespace Sleeky.Todo.Domain.Tests.Services;

[TestClass]
public sealed class SearchTokenizerTests
{
    [TestMethod]
    public void TokensAreLowercasedAndSplitOnNonAlphanumericRuns()
    {
        SearchTokenizer.Tokenize("Buy Milk, please -- Today!")
            .Should().Equal("buy", "milk", "please", "today");
    }

    [TestMethod]
    public void DigitsAndMixedRunsSurviveAsTokens()
    {
        SearchTokenizer.Tokenize("Pay invoice 2026-08 for VAT21")
            .Should().Equal("pay", "invoice", "2026", "08", "for", "vat21");
    }

    [TestMethod]
    public void RepeatedWordsAcrossValuesAppearOnce()
    {
        SearchTokenizer.Tokenize("Milk milk MILK", "Buy milk")
            .Should().Equal("milk", "buy");
    }

    [TestMethod]
    public void NullAndEmptyValuesContributeNothing()
    {
        SearchTokenizer.Tokenize(null, string.Empty, "   ", "!!!")
            .Should().BeEmpty();
    }

    [TestMethod]
    public void NonAsciiLettersAreKeptWithoutFoldingDiacritics()
    {
        SearchTokenizer.Tokenize("Café Ünterlagen 日本語")
            .Should().Equal("café", "ünterlagen", "日本語");
    }

    /// <summary>
    /// Invariant lowercasing maps one character to one character, so it never
    /// expands U+0130 into <c>i</c> plus a combining dot the way a full case
    /// mapping would. The token therefore keeps the dotted capital, and a
    /// search for <c>istanbul</c> does not reach it. Both sides run this same
    /// method, so the two agree; what is pinned here is that they agree on
    /// leaving the character alone rather than on some folded form.
    /// </summary>
    [TestMethod]
    public void InvariantLowercasingDoesNotExpandTheDottedCapitalI()
    {
        SearchTokenizer.Tokenize("İstanbul")
            .Should().Equal("İstanbul");
        SearchTokenizer.Tokenize("istanbul")
            .Should().Equal("istanbul");
    }

    [TestMethod]
    public void LongRunsAreTruncatedToTheMaximumTokenLength()
    {
        string run = new string('a', SearchTokenizer.MaximumTokenLength + 10);

        IReadOnlyList<string> tokens = SearchTokenizer.Tokenize($"{run} tail");

        tokens.Should().Equal(
            new string('a', SearchTokenizer.MaximumTokenLength),
            "tail");
    }

    /// <summary>
    /// Truncation is what makes these one token, so the deduplication that
    /// follows has to run on the truncated value rather than on the raw run.
    /// </summary>
    [TestMethod]
    public void RunsThatDifferOnlyBeyondTheLimitCollapseIntoOneToken()
    {
        string prefix = new string('b', SearchTokenizer.MaximumTokenLength);

        SearchTokenizer.Tokenize($"{prefix}one {prefix}two")
            .Should().Equal(prefix);
    }

    [TestMethod]
    public void TokenizeRejectsANullValueArray()
    {
        Action act = () => SearchTokenizer.Tokenize(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
