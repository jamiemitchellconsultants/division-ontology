using System;
using System.Text;
using RuntimeKnowledge.Impact.Engine;

var stagingDir = GetStagingDir(args);
var result = CommandRunner.Run(args);
if (result.Exit != 0)
{
    if (result.Error != null)
        Console.Error.WriteLine(result.Error);
    return result.Exit;
}

var utf8NoBom = new UTF8Encoding(false);
foreach (var (name, text) in result.Files)
{
    var path = stagingDir != null ? Path.Combine(stagingDir, name) : name;
    File.WriteAllText(path, text, utf8NoBom);
}

return 0;

static string? GetStagingDir(string[] args)
{
    var i = Array.IndexOf(args, "--staging");
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}
