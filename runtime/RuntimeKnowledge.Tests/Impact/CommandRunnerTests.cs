using System.Text.Json.Nodes;
using RuntimeKnowledge.Impact.Engine;
using Xunit;

namespace RuntimeKnowledge.Tests.Impact;

public sealed class CommandRunnerTests
{
    [Fact]
    public void Input_files_keep_a_leading_byte_order_mark_as_node_did()
    {
        var dir = Directory.CreateTempSubdirectory("impact-runner-");
        try
        {
            var description = Path.Combine(dir.FullName, "description.txt");
            File.WriteAllBytes(description, [0xEF, 0xBB, 0xBF, (byte)'C', (byte)'h', (byte)'a', (byte)'n', (byte)'g', (byte)'e', (byte)'\n']);
            var staging = Directory.CreateDirectory(Path.Combine(dir.FullName, "out")).FullName;
            var (exit, files, error) = CommandRunner.Run(["assessment-init",
                "--bundle", CorpusPaths.Objects, "--manifest", CorpusPaths.FirstManifest("pilot", "assessment-init"),
                "--staging", staging, "--request-id", "CR-1", "--description-file", description]);
            Assert.True(exit == 0, error);
            var record = JsonNode.Parse(files.Single(f => f.Name == "assessment.json").Text)!;
            Assert.Equal("﻿Change\n", record["description"]!.GetValue<string>());
        }
        finally { dir.Delete(recursive: true); }
    }
}
