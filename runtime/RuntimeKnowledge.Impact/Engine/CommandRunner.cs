using System.Text.Json.Nodes;
using RuntimeKnowledge.Impact.Bundle;
using RuntimeKnowledge.Impact.Engine;
using RuntimeKnowledge.Impact.Json;
using RuntimeKnowledge.Impact.Model;
using RuntimeKnowledge.Impact.Render;

namespace RuntimeKnowledge.Impact.Engine;

/// <summary>Pure command runner that maps CLI arguments to engine operations.</summary>
public static class CommandRunner
{
    public static (int Exit, IReadOnlyList<(string Name, string Text)> Files, string? Error) Run(string[] args)
    {
        try
        {
            if (args.Length < 2)
                return (1, Array.Empty<(string, string)>(), "unknown command");

            var command = args[0];
            var bundleDir = GetFlag(args, "--bundle");
            var manifestId = GetFlag(args, "--manifest");

            if (bundleDir == null || manifestId == null)
                return (1, Array.Empty<(string, string)>(), "missing required flags: --bundle, --manifest");

            var bundle = ImpactBundle.Open(bundleDir, manifestId);
            var model = ImpactModel.Build(bundle);

            var files = new List<(string Name, string Text)>();

            switch (command)
            {
                case "search":
                    {
                        var query = GetFlag(args, "--query") ?? "";
                        var exactIri = GetFlag(args, "--exact-iri");
                        var result = Candidates.Find(model, query, exactIri);
                        files.Add(("candidates.json", EcmaJson.File(result)));
                    }
                    break;

                case "paths":
                    {
                        var starts = GetMultiFlag(args, "--start")
                            .Select(pair)
                            .ToList();
                        var maxDepth = GetIntFlag(args, "--max-depth", 4);
                        var result = Paths.Read(model, starts, maxDepth);
                        files.Add(("paths.json", EcmaJson.File(result)));
                    }
                    break;

                case "view":
                    {
                        var focusStr = GetFlag(args, "--focus");
                        var level = GetFlag(args, "--level");
                        if (focusStr == null || level == null)
                            return (1, Array.Empty<(string, string)>(), "focus and level required");

                        var (sourceId, nodeIri) = pair(focusStr);
                        JsonObject? supplement = null;
                        var supplementPath = GetFlag(args, "--supplement");
                        if (supplementPath != null)
                        {
                            var supplementContent = File.ReadAllText(supplementPath);
                            supplement = JsonNode.Parse(supplementContent)!.AsObject();
                        }

                        var view = Views.Project(model, (sourceId, nodeIri), level, supplement);
                        var (json, markdown) = ViewMarkdown.Render(view);
                        files.Add(("view-" + level + ".json", json));
                        files.Add(("view-" + level + ".md", markdown));
                    }
                    break;

                case "assessment-init":
                    {
                        var requestId = GetFlag(args, "--request-id");
                        var descriptionFile = GetFlag(args, "--description-file");
                        if (requestId == null || descriptionFile == null)
                            return (1, Array.Empty<(string, string)>(), "request ID and description file required");

                        var description = File.ReadAllText(descriptionFile);
                        var record = Assessments.New(model, requestId, description);
                        files.Add(("assessment.json", EcmaJson.File(record)));
                    }
                    break;

                case "assessment-render":
                    {
                        var inputFile = GetFlag(args, "--input");
                        if (inputFile == null)
                            return (1, Array.Empty<(string, string)>(), "input is required");

                        var inputContent = File.ReadAllText(inputFile);
                        var record = JsonNode.Parse(inputContent)!.AsObject();
                        var viewInputs = GetMultiFlag(args, "--view-input");
                        var views = viewInputs.Select(vi =>
                            JsonNode.Parse(File.ReadAllText(vi))!.AsObject()).ToList();

                        var confirmedLeads = record["confirmedLeads"]?.AsArray();
                        var starts = confirmedLeads != null && confirmedLeads.Count > 0
                            ? confirmedLeads.Select(n => pair(n!.AsObject())).ToList()
                            : new List<(string, string)>();
                        var paths = starts.Count > 0
                            ? Paths.Read(model, starts, 4)
                            : new JsonObject();

                        var digest = Assessments.Validate(record, model,
                            (paths["paths"] as JsonArray ?? new JsonArray()), views);
                        var markdown = AssessmentMarkdownV1.Render(record, model,
                            (paths["paths"] as JsonArray ?? new JsonArray()), views);
                        files.Add(("assessment.md", markdown));
                    }
                    break;

                case "feedback-render":
                    {
                        var inputFile = GetFlag(args, "--input");
                        var assessmentFile = GetFlag(args, "--assessment-file");
                        if (inputFile == null)
                            return (1, Array.Empty<(string, string)>(), "input is required");

                        var inputContent = File.ReadAllText(inputFile);
                        var feedback = JsonNode.Parse(inputContent)!.AsObject();

                        // Load the assessment to compute the digest via validation
                        JsonObject assessment;
                        if (assessmentFile != null)
                        {
                            var assessmentContent = File.ReadAllText(assessmentFile);
                            var record = JsonNode.Parse(assessmentContent)!.AsObject();

                            var confirmedLeads = record["confirmedLeads"]?.AsArray();
                            var starts = confirmedLeads != null && confirmedLeads.Count > 0
                                ? confirmedLeads.Select(n => pair(n!.AsObject())).ToList()
                                : new List<(string, string)>();
                            var paths = starts.Count > 0
                                ? Paths.Read(model, starts, 4)
                                : new JsonObject();

                            var digest = Assessments.Validate(record, model,
                                (paths["paths"] as JsonArray ?? new JsonArray()),
                                Array.Empty<JsonObject>());
                            var requestId = record["requestId"]?.GetValue<string>() ?? "unknown";
                            assessment = new JsonObject
                            {
                                ["requestId"] = requestId,
                                ["digest"] = digest
                            };
                        }
                        else
                        {
                            var requestId = feedback["requestId"]?.GetValue<string>() ?? "unknown";
                            var assessmentDigest = feedback["assessmentDigest"]?.GetValue<string>() ?? "";
                            assessment = new JsonObject
                            {
                                ["requestId"] = requestId,
                                ["digest"] = assessmentDigest
                            };
                        }

                        var markdown = FeedbackMarkdown.Render(feedback, assessment);
                        files.Add(("feedback.md", markdown));
                    }
                    break;

                default:
                    return (1, Array.Empty<(string, string)>(), "unknown command");
            }

            return (0, files, null);
        }
        catch (ImpactException ex)
        {
            return (1, Array.Empty<(string, string)>(), ex.Message);
        }
        catch (Exception ex)
        {
            return (2, Array.Empty<(string, string)>(), ex.ToString());
        }
    }

    private static string? GetFlag(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i < 0 || i + 1 >= args.Length ? null : args[i + 1];
    }

    private static IReadOnlyList<string> GetMultiFlag(string[] args, string name)
    {
        var result = new List<string>();
        var i = 0;
        while ((i = Array.IndexOf(args, name, i)) >= 0 && i + 1 < args.Length)
        {
            result.Add(args[i + 1]);
            i += 2;
        }
        return result;
    }

    private static int GetIntFlag(string[] args, string name, int defaultValue)
    {
        var flag = GetFlag(args, name);
        return flag == null ? defaultValue : int.Parse(flag);
    }

    private static (string SourceId, string NodeIri) pair(string value)
    {
        var eq = value.IndexOf('=');
        if (eq < 1 || eq == value.Length - 1)
            throw new ImpactException("expected source ID=value");
        return (value[..eq], value[(eq + 1)..]);
    }

    private static (string SourceId, string NodeIri) pair(JsonObject obj)
    {
        return (obj["sourceId"]!.GetValue<string>(), obj["nodeIri"]!.GetValue<string>());
    }
}
