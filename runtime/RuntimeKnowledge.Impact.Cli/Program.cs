using RuntimeKnowledge.Impact.Engine;

var result = CommandRunner.Run(args);
if (result.Exit != 0)
{
    if (result.Error != null)
        Console.Error.WriteLine(result.Error);
    return result.Exit;
}

var stagingDir = args.Length > 1 ? args[1] : null;
foreach (var (name, text) in result.Files)
{
    var path = stagingDir != null ? Path.Combine(stagingDir, name) : name;
    File.WriteAllText(path, text, Encoding.UTF8);
}

return 0;
