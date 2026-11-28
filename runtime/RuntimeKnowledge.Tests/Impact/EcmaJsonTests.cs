using System.Text.Json.Nodes;
using RuntimeKnowledge.Impact.Json;
using Xunit;

namespace RuntimeKnowledge.Tests.Impact;

public sealed class EcmaJsonTests
{
    private static JsonObject Vector() => new()
    {
        ["b"] = "quote\" back\\ nl\n tab\t ctl\u0001 e\u00e9 emoji\U0001F600 ls\u2028 lone\ud800 lt< amp&",
        ["a"] = new JsonArray(1, new JsonObject { ["z"] = 2, ["y"] = null })
    };

    [Fact]
    public void Compact_output_matches_JSON_stringify() =>
        Assert.Equal("{\"b\":\"quote\\\" back\\\\ nl\\n tab\\t ctl\\u0001 e\u00e9 emoji\U0001F600 ls\u2028 lone\\ud800 lt< amp&\",\"a\":[1,{\"z\":2,\"y\":null}]}",
            EcmaJson.Stringify(Vector()));

    [Fact]
    public void Indented_output_matches_JSON_stringify_with_two_spaces() =>
        Assert.Equal("{\n  \"b\": \"quote\\\" back\\\\ nl\\n tab\\t ctl\\u0001 e\u00e9 emoji\U0001F600 ls\u2028 lone\\ud800 lt< amp&\",\n  \"a\": [\n    1,\n    {\n      \"z\": 2,\n      \"y\": null\n    }\n  ]\n}",
            EcmaJson.Stringify(Vector(), indent: true));

    [Fact]
    public void Canonical_digest_matches_the_pilot() =>
        Assert.Equal("148adb88869d3cc06f3846a31a23753cc0ca7c2b6fc6c1b959da96bbb9268417", EcmaJson.CanonicalDigest(Vector()));

    [Fact]
    public void Path_id_hash_matches_the_pilot() =>
        Assert.Equal("e43f95e0e0f52ac540a4669b1f5c3ab45472263d26c436c5effa9727b7d752d2",
            EcmaJson.Sha256Hex(EcmaJson.Stringify(new JsonArray(new JsonObject
            {
                ["type"] = "assertion",
                ["subject"] = new JsonObject { ["sourceId"] = "s", ["nodeIri"] = "a" }
            }))));

    [Fact]
    public void Empty_containers_and_parsed_numbers_are_written_as_JSON_stringify_does()
    {
        Assert.Equal("{}", EcmaJson.Stringify(new JsonObject(), indent: true));
        Assert.Equal("[]", EcmaJson.Stringify(new JsonArray(), indent: true));
        Assert.Equal("{\"n\":12}", EcmaJson.Stringify(JsonNode.Parse("{\"n\":12}")));
        Assert.Throws<NotSupportedException>(() => EcmaJson.Stringify(JsonValue.Create(1.5)));
    }

    [Fact]
    public void File_form_ends_with_a_newline() => Assert.Equal("{}\n", EcmaJson.File(new JsonObject()));
}
