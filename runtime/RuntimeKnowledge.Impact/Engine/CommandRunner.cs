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
                        var maxDepth = GetDepthFlag(args, "--max-depth", 4);
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
                            var supplementContent = ReadText(supplementPath);
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

                        var description = ReadText(descriptionFile);
                        var record = Assessments.New(model, requestId, description);
                        files.Add(("assessment.json", EcmaJson.File(record)));
                    }
                    break;

                case "assessment-render":
                    {
                        var inputFile = GetFlag(args, "--input");
                        if (inputFile == null)
                            return (1, Array.Empty<(string, string)>(), "input is required");

                        var inputContent = ReadText(inputFile);
                        var record = JsonNode.Parse(inputContent)!.AsObject();
                        var viewInputs = GetMultiFlag(args, "--view-input");
                        var views = viewInputs.Select(vi =>
                            JsonNode.Parse(ReadText(vi))!.AsObject()).ToList();

                        var cited = CitablePaths(model, record);
                        var frozen = Assessments.Freeze(record, model, cited, views);
                        var markdown = record["schemaVersion"]?.GetValue<int>() == 2
                            ? AssessmentMarkdown.Render(frozen)
                            : AssessmentMarkdownV1.Render(record, model, cited, views);
                        files.Add(("assessment.md", markdown));
                    }
                    break;

                case "feedback-render":
                    {
                        var inputFile = GetFlag(args, "--input");
                        var assessmentFile = GetFlag(args, "--assessment-file");
                        if (inputFile == null)
                            return (1, Array.Empty<(string, string)>(), "input is required");
                        if (assessmentFile == null)
                            return (1, Array.Empty<(string, string)>(), "assessment file is required");

                        var feedback = JsonNode.Parse(ReadText(inputFile))!.AsObject();
                        var record = JsonNode.Parse(ReadText(assessmentFile))!.AsObject();
                        var views = GetMultiFlag(args, "--view-input")
                            .Select(vi => JsonNode.Parse(ReadText(vi))!.AsObject()).ToList();
                        var digest = Assessments.Validate(record, model, CitablePaths(model, record), views);
                        var assessment = new JsonObject
                        {
                            ["requestId"] = record["requestId"]?.GetValue<string>() ?? "unknown",
                            ["digest"] = digest
                        };

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

    // Decodes as Node's readFileSync(path, 'utf8') does: a leading byte order mark is kept, not stripped.
    private static string ReadText(string path) => System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(path));

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

    private static JsonArray CitablePaths(ImpactModel model, JsonObject record)
    {
        var starts = (record["confirmedLeads"] as JsonArray ?? []).Select(n => pair(n!.AsObject())).ToList();
        return starts.Count > 0 ? Paths.Read(model, starts, 4)["paths"]!.AsArray() : new JsonArray();
    }

    // As the pilot's Number(): an integral decimal such as "4.0" is accepted; anything else is out of range.
    private static int GetDepthFlag(string[] args, string name, int defaultValue)
    {
        var flag = GetFlag(args, name);
        if (flag == null) return defaultValue;
        if (!double.TryParse(flag, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var depth)
            || depth != Math.Floor(depth) || depth < 1 || depth > 6)
            throw new ImpactException("depth must be from 1 to 6");
        return (int)depth;
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
