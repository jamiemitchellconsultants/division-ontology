namespace RuntimeKnowledge.Worker;

public static class WorkerCommands
{
    /// <summary>The operator commands that need the bundle store and the snapshot or replay stores.</summary>
    public static bool IsBundleCommand(string[] args) => args is ["stage", _] or ["activate", _] or ["repair-models"]
        or ["describe-current"] or ["writes", "pause" or "resume"] or ["replay", "--through", _]
        or ["activate-replay", "--generation", _, "--expect-position", _];
}
