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
    public void Lower_casing_matches_javascript(string text, string expected) =>
        Assert.Equal(expected, EcmaText.ToLower(text));

    [Fact]
    public void A_greek_label_matches_its_lower_case_query()                       // B1 (PR #220 review 30)
    {
        var model = TestModels.MakeModel(input => input["sources"]![0]!["graph"]!["nodes"]![0]!["label"] = "ΟΣ");
        var leads = Candidates.Find(model, "ος")["leads"]!.AsArray();
        Assert.Contains(leads, l => l!["key"]!["nodeIri"]!.GetValue<string>() == "https://alpha.example/#system");
    }
}
