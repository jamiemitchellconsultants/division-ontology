using RuntimeKnowledge.Worker;
using Xunit;

namespace RuntimeKnowledge.Tests;

public sealed class WorkerCommandsTests
{
    [Theory]
    [InlineData("repair-models")]                                                       // ADR 0036, amended
    [InlineData("stage", "abc")]
    [InlineData("activate", "abc")]
    [InlineData("describe-current")]
    [InlineData("writes", "pause")]
    [InlineData("replay", "--through", "4")]
    public void Bundle_commands_reach_the_bundle_handler(params string[] args) =>
        Assert.True(WorkerCommands.IsBundleCommand(args));

    [Theory]
    [InlineData("repair-models", "extra")]
    [InlineData("index-loop")]
    [InlineData("bundle", "repair-models")]
    public void Other_commands_do_not(params string[] args) =>
        Assert.False(WorkerCommands.IsBundleCommand(args));
}
