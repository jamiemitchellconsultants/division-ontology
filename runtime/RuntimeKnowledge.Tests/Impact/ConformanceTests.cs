using System.Text.Json.Nodes;
using RuntimeKnowledge.Impact.Engine;
using Xunit;

namespace RuntimeKnowledge.Tests.Impact;

public sealed class ConformanceTests
{
    public static TheoryData<string, int> EngineSteps()
    {
        var data = new TheoryData<string, int>();
        foreach (var scenario in new[] { "pilot", "walkthrough" })
        {
            var steps = CorpusPaths.Steps(scenario);
            for (var i = 0; i < steps.Count; i++)
                if (steps[i]!["manifest"] is not null) data.Add(scenario, i);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(EngineSteps))]
    public void Every_engine_step_reproduces_its_baseline(string scenario, int index)
    {
        var step = CorpusPaths.Steps(scenario)[index]!;
        var staging = Directory.CreateTempSubdirectory("impact-conformance-").FullName;
        try
        {
            var args = ToolArgs(scenario, index, step, staging);
            var (exit, files, error) = CommandRunner.Run(args);
            Assert.Equal(step["exit"]!.GetValue<int>(), exit);
            Assert.Equal(step["stderr"]!.GetValue<string>(), error is null ? "" : error + "\n");
            Assert.Equal(step["files"]!.AsArray().Select(f => f!.GetValue<string>()), files.Select(f => f.Name).Order(StringComparer.Ordinal));
            foreach (var (name, text) in files)
                Assert.Equal(File.ReadAllText(CorpusPaths.Expected(scenario, index, name)), text);
        }
        finally { Directory.Delete(staging, true); }
    }

    private static string[] ToolArgs(string scenario, int index, JsonNode step, string staging)
    {
        var argv = step["argv"]!.AsArray().Select(x => x!.GetValue<string>()).ToList();
        var command = argv[0];
        var args = new List<string> { command };

        // Drop Node front-end only flags, keep tool flags
        var i = 1;
        while (i < argv.Count)
        {
            var flag = argv[i];
            if (flag == "--catalog-commit" || flag == "--registry-commit" ||
                flag == "--checkout" || flag == "--output")
            {
                i += 2; // skip flag and value
                continue;
            }

            var value = i + 1 < argv.Count ? argv[i + 1] : null;

            // Resolve $input/<step>/<name> references (the step dir is already in the path)
            if (value != null && value.StartsWith("$input/"))
            {
                var relPath = value["$input/".Length..];
                value = Path.Combine(CorpusPaths.Corpus, scenario, "inputs", relPath);
            }

            // Resolve $checkout and $output references (skip these)
            if (flag == "--checkout" || flag == "--output")
            {
                i += 2;
                continue;
            }

            args.Add(flag);
            if (value != null) args.Add(value);
            i += 2;
        }

        // Add tool flags
        args.Add("--bundle");
        args.Add(CorpusPaths.Objects);
        args.Add("--manifest");
        args.Add(step["manifest"]!.GetValue<string>());
        args.Add("--staging");
        args.Add(staging);

        return args.ToArray();
    }
}
