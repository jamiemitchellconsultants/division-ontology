using System.Text.Json.Nodes;
using RuntimeKnowledge.Impact;
using RuntimeKnowledge.Impact.Json;
using ImpactModel = RuntimeKnowledge.Impact.Model.ImpactModel;

namespace RuntimeKnowledge.Impact.Engine;

/// <summary>Port of projectView from the Node C4 pilot.</summary>
public static class Views
{
    private static readonly string[] Levels = ["context", "container", "component", "code"];

    public static JsonObject Project(ImpactModel model, (string SourceId, string NodeIri) focus, string level, JsonObject? supplement = null)
    {
        if (!Levels.Contains(level))
            throw new ImpactException("invalid C4 level");

        var gaps = new List<JsonObject>();
        foreach (var g in model.Registry.Gaps)
            gaps.Add(g.DeepClone()!.AsObject());
        foreach (var gap in Coverage.Gaps(model))
            gaps.Add(gap);

        var focusMapping = model.Registry.MappingFor(focus.SourceId, focus.NodeIri);
        var expected = level switch
        {
            "context" => "context",
            "container" => "context",
            "component" => "container",
            "code" => "component",
            _ => throw new ImpactException("invalid C4 level")
        };

        var view = new JsonObject
        {
            ["snapshotId"] = model.SnapshotId.DeepClone()!.AsObject(),
            ["registryCommit"] = model.Registry.Commit,
            ["registryDigests"] = new JsonObject
            {
                ["mappings"] = model.Registry.MappingSha256,
                ["bindings"] = model.Registry.BindingSha256
            },
            ["focus"] = new JsonObject { ["sourceId"] = focus.SourceId, ["nodeIri"] = focus.NodeIri },
            ["level"] = level,
            ["boundary"] = new JsonObject { ["sourceId"] = focus.SourceId, ["nodeIri"] = focus.NodeIri },
            ["coverage"] = new JsonArray(Coverage.Inventory(model).Select(r => r.DeepClone()!).ToArray()),
            ["items"] = new JsonArray(),
            ["edges"] = new JsonArray(),
            ["nextFocus"] = new JsonArray(),
            ["gaps"] = new JsonArray(gaps.Select(g => g.DeepClone()!).ToArray())
        };

        if (focusMapping == null)
        {
            view["gaps"]!.AsArray().Add(new JsonObject
            {
                ["code"] = "no-c4-mapping",
                ["sourceId"] = focus.SourceId,
                ["nodeIri"] = focus.NodeIri,
                ["reason"] = "Node has no C4 mapping; C4 views need a reviewed mapping"
            });
            return view;
        }

        if (focusMapping["level"]!.GetValue<string>() != expected)
        {
            var gap = new JsonObject { ["code"] = "unsupported-focus" };
            gap["sourceId"] = focus.SourceId;
            gap["nodeIri"] = focus.NodeIri;
            gap["reason"] = "Focus lacks the required supported mapping";
            view["gaps"]!.AsArray().Add(gap);
            return view;
        }

        // Selected mappings
        var selected = new List<JsonObject>();

        if (level == "context")
        {
            // Context: all context-level mappings that are the focus or share source and are connected by an assertion
            foreach (var m in model.Registry.Mappings)
            {
                if (m["level"]!.GetValue<string>() != "context") continue;
                var mSourceId = m["sourceId"]!.GetValue<string>();
                var mNodeIri = m["nodeIri"]!.GetValue<string>();
                if (mSourceId == focus.SourceId && mNodeIri == focus.NodeIri)
                {
                    selected.Add(m);
                    continue;
                }
                if (mSourceId != focus.SourceId) continue;
                var source = model.Source(mSourceId);
                if (source == null) continue;
                foreach (var a in source.Assertions)
                {
                    var aSubject = a["subject"]!.GetValue<string>();
                    var aObject = a["object"]!.GetValue<string>();
                    if ((aSubject == focus.NodeIri && aObject == mNodeIri) ||
                        (aObject == focus.NodeIri && aSubject == mNodeIri))
                    {
                        selected.Add(m);
                        break;
                    }
                }
            }
        }
        else
        {
            // Container/component: level mappings whose parent is the focus
            foreach (var m in model.Registry.Mappings)
            {
                if (m["level"]!.GetValue<string>() != level) continue;
                var parent = m["parent"]!.AsObject();
                if (parent["sourceId"]!.GetValue<string>() == focus.SourceId &&
                    parent["nodeIri"]!.GetValue<string>() == focus.NodeIri)
                {
                    selected.Add(m);
                }
            }

            // Add directly connected items
            if (level == "container" || level == "component")
            {
                var inner = new HashSet<string>();
                foreach (var m in selected)
                    inner.Add(Key(m["sourceId"]!.GetValue<string>(), m["nodeIri"]!.GetValue<string>()));

                foreach (var m in model.Registry.Mappings)
                {
                    var mSourceId = m["sourceId"]!.GetValue<string>();
                    var mNodeIri = m["nodeIri"]!.GetValue<string>();
                    if (inner.Contains(Key(mSourceId, mNodeIri))) continue;
                    if (mSourceId == focus.SourceId && mNodeIri == focus.NodeIri) continue;

                    var source = model.Source(mSourceId);
                    if (source == null) continue;

                    foreach (var a in source.Assertions)
                    {
                        var rel = a["relationship"]!.GetValue<string>();
                        if (rel == "https://ontology-kit.dev/schema#contains") continue;
                        var aSubject = a["subject"]!.GetValue<string>();
                        var aObject = a["object"]!.GetValue<string>();
                        var subjectKey = Key(mSourceId, aSubject);
                        var objectKey = Key(mSourceId, aObject);
                        if ((inner.Contains(subjectKey) && aObject == mNodeIri) ||
                            (inner.Contains(objectKey) && aSubject == mNodeIri))
                        {
                            selected.Add(m);
                            break;
                        }
                    }
                }
            }
        }

        // Build items
        var items = new List<JsonObject>();
        foreach (var m in selected)
        {
            var mSourceId = m["sourceId"]!.GetValue<string>();
            var mNodeIri = m["nodeIri"]!.GetValue<string>();
            var source = model.Source(mSourceId);
            if (source == null) continue;

            var node = source.Node(mNodeIri);
            var assertionIri = m["supportAssertionIri"]!.GetValue<string>();
            var assertion = source.Assertion(assertionIri);
            var evidenceIri = m["supportEvidenceIri"]!.GetValue<string>();
            var evidence = source.EvidenceNode(evidenceIri);

            if (node == null || assertion == null || evidence == null)
            {
                var gap = new JsonObject
                {
                    ["code"] = "unsupported-node",
                    ["sourceId"] = mSourceId,
                    ["nodeIri"] = mNodeIri,
                    ["reason"] = "Mapping support is unavailable"
                };
                view["gaps"]!.AsArray().Add(gap);
                continue;
            }

            var insideBoundary = level == "context"
                ? (mSourceId == focus.SourceId && mNodeIri == focus.NodeIri)
                : (m["parent"] != null && m["parent"]!.AsObject()["sourceId"]!.GetValue<string>() == focus.SourceId &&
                   m["parent"]!.AsObject()["nodeIri"]!.GetValue<string>() == focus.NodeIri);

            var item = new JsonObject
            {
                ["key"] = new JsonObject { ["sourceId"] = mSourceId, ["nodeIri"] = mNodeIri },
                ["label"] = node["label"]!.GetValue<string>(),
                ["role"] = m["role"]!.GetValue<string>(),
                ["insideBoundary"] = insideBoundary,
                ["supported"] = true,
                ["assertionIri"] = assertionIri,
                ["evidenceIris"] = new JsonArray(evidenceIri),
                ["evidence"] = new JsonArray(evidence.DeepClone()!),
                ["commit"] = source.Commit
            };
            items.Add(item);

            var index = Array.IndexOf(Levels, level);
            if (index < 3 && model.Registry.Mappings.Any(mp =>
                mp["parent"] != null &&
                mp["parent"]!.AsObject()["sourceId"]!.GetValue<string>() == mSourceId &&
                mp["parent"]!.AsObject()["nodeIri"]!.GetValue<string>() == mNodeIri &&
                mp["level"]!.GetValue<string>() == Levels[index + 1]))
            {
                view["nextFocus"]!.AsArray().Add(new JsonObject
                {
                    ["focus"] = new JsonObject { ["sourceId"] = mSourceId, ["nodeIri"] = mNodeIri },
                    ["level"] = Levels[index + 1]
                });
            }
        }

        // Supplement
        if (supplement != null)
        {
            if (level != "code")
                throw new ImpactException("code supplement requires a code view");

            var inspected = CodeSupplement.Verify(model, (focus.SourceId, focus.NodeIri), supplement);
            var inspectedItem = new JsonObject
            {
                ["key"] = new JsonObject
                {
                    ["sourceId"] = inspected["sourceId"]!.GetValue<string>(),
                    ["nodeIri"] = "inspected:" + inspected["blobId"]!.GetValue<string>() + ":" +
                                  inspected["startLine"]!.GetValue<int>() + "-" + inspected["endLine"]!.GetValue<int>()
                },
                ["label"] = inspected["path"]!.GetValue<string>(),
                ["role"] = "code",
                ["insideBoundary"] = true,
                ["supported"] = true,
                ["basis"] = inspected["basis"]!.GetValue<string>(),
                ["path"] = inspected["path"]!.GetValue<string>(),
                ["evidenceIris"] = new JsonArray(),
                ["evidence"] = new JsonArray(inspected.DeepClone()!),
                ["commit"] = inspected["commit"]!.GetValue<string>()
            };
            items.Add(inspectedItem);
        }

        // Edges
        var included = new HashSet<string>(items.Select(i => Key(i["key"]!.AsObject()["sourceId"]!.GetValue<string>(),
            i["key"]!.AsObject()["nodeIri"]!.GetValue<string>())));

        foreach (var source in model.Sources)
            foreach (var assertion in source.Assertions)
            {
                var fromKey = Key(source.Id, assertion["subject"]!.GetValue<string>());
                var toKey = Key(source.Id, assertion["object"]!.GetValue<string>());
                if (!included.Contains(fromKey) || !included.Contains(toKey)) continue;

                var evidenceNodes = new JsonArray();
                if (assertion["evidenceIris"] is JsonArray eiris)
                {
                    foreach (var eiri in eiris)
                    {
                        var eiriStr = eiri!.GetValue<string>();
                        var enode = source.EvidenceNode(eiriStr);
                        if (enode != null) evidenceNodes.Add(enode.DeepClone()!);
                    }
                }

                view["edges"]!.AsArray().Add(new JsonObject
                {
                    ["from"] = new JsonObject { ["sourceId"] = source.Id, ["nodeIri"] = assertion["subject"]!.GetValue<string>() },
                    ["to"] = new JsonObject { ["sourceId"] = source.Id, ["nodeIri"] = assertion["object"]!.GetValue<string>() },
                    ["assertionIri"] = assertion["iri"]!.GetValue<string>(),
                    ["relationship"] = assertion["relationship"]!.GetValue<string>(),
                    ["basis"] = assertion["basis"]!.GetValue<string>(),
                    ["evidenceIris"] = new JsonArray(assertion["evidenceIris"]!.AsArray().Select(e => e!.DeepClone()!).ToArray()),
                    ["evidence"] = evidenceNodes,
                    ["commit"] = source.Commit
                });
            }

        // Sort items and edges
        items.Sort((a, b) => string.CompareOrdinal(Key(a["key"]!.AsObject()["sourceId"]!.GetValue<string>(),
            a["key"]!.AsObject()["nodeIri"]!.GetValue<string>()),
            Key(b["key"]!.AsObject()["sourceId"]!.GetValue<string>(),
            b["key"]!.AsObject()["nodeIri"]!.GetValue<string>())));

        var edges = view["edges"]!.AsArray();
        var edgeList = new List<JsonObject>();
        foreach (var e in edges) edgeList.Add(e!.AsObject());
        edgeList.Sort((a, b) => string.CompareOrdinal(a["assertionIri"]!.GetValue<string>(), b["assertionIri"]!.GetValue<string>()));
        view["edges"] = new JsonArray(edgeList.Select(e => e.DeepClone()!).ToArray());

        view["items"] = new JsonArray(items.Select(i => i.DeepClone()!).ToArray());

        if (!items.Any())
        {
            var gap = new JsonObject
            {
                ["code"] = "empty-level",
                ["reason"] = "No supported items at this level; coverage is unknown"
            };
            view["gaps"]!.AsArray().Add(gap);
        }

        return view;
    }

    private static string Key(string sourceId, string nodeIri) => EcmaJson.Stringify(new JsonObject
    {
        ["sourceId"] = sourceId,
        ["nodeIri"] = nodeIri
    });
}
