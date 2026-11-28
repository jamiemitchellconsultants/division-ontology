using RuntimeKnowledge.Impact.Engine;
using RuntimeKnowledge.Impact.Json;
using Xunit;

namespace RuntimeKnowledge.Tests.Impact;

public sealed class EcmaTextTests
{
    // Expected values are node's String.prototype.toLowerCase() output.
    [Theory]
    [InlineData("ΟΣ", "ος")]
    [InlineData("ΣΑ", "σα")]
    [InlineData("ΟΔΥΣΣΕΥΣ", "οδυσσευς")]
    [InlineData("İstanbul", "i̇stanbul")]
    [InlineData("Σ", "σ")]
    [InlineData("ΑΣ.", "ας.")]
    [InlineData("ΑΣ́Β", "ασ́β")]
    [InlineData("Claim ΟΣ", "claim ος")]
    [InlineData("\U00010400Σ", "\U00010428ς")]
    [InlineData("\U00010400Σa", "\U00010428σa")]
    public void Lower_casing_matches_javascript(string text, string expected) =>
        Assert.Equal(expected, EcmaText.ToLower(text));

    [Fact]
    public void A_greek_label_matches_its_lower_case_query()                       // B1 (PR #220 review 30)
    {
        var model = TestModels.MakeModel(input => input["sources"]![0]!["graph"]!["nodes"]![0]!["label"] = "ΟΣ");
        var leads = Candidates.Find(model, "ος")["leads"]!.AsArray();
        Assert.Contains(leads, l => l!["key"]!["nodeIri"]!.GetValue<string>() == "https://alpha.example/#system");
    }

    [Fact]
    public void Tokens_keep_letters_outside_the_basic_multilingual_plane()               // B1 (re-review 30)
    {
        Assert.Equal(["x", "\U00010428", "y"], EcmaText.Tokens("x \U00010428 y"));
        var model = TestModels.MakeModel(input => input["sources"]![0]!["graph"]!["nodes"]![0]!["label"] = "Alpha \U00010400");
        var leads = Candidates.Find(model, "zeta \U00010428")["leads"]!.AsArray();
        Assert.Contains(leads, l => l!["key"]!["nodeIri"]!.GetValue<string>() == "https://alpha.example/#system");
    }
}
