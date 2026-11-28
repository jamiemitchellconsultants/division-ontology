# Impact corpus

This directory is the **B37 baseline** for the impact assessment engine. The C4 scenarios
(`pilot`, `walkthrough`) were captured byte for byte from the Node engine at commit `c6c071c`.
Since then they have changed only in commits that name the B37 item allowing each change. Later
scenarios, such as `confluence`, record the .NET engine's intended behaviour.

## Replay

Replaying never writes to this directory. Run the conformance tests:

```bash
node --test test/impact-corpus.test.mjs
dotnet test runtime/RuntimeKnowledge.Tests --filter "FullyQualifiedName~Conformance"
```

## Capturing

`capture.mjs` overwrites `expected/` and `steps.json` with whatever the engine does now. The Node
engine has been removed, so a capture records the .NET engine and turns any bug into the
baseline. Run it only where a task in the detailed plan says to. The command line rebuilds the tool
whenever the engine sources change; clearing the cache first makes the capture independent of it:

```bash
rm -rf .cache/impact-cli && npm run impact:build
node test/impact-corpus/capture.mjs
node test/impact-corpus/export-bundles.mjs
```

Then read the whole `git diff test/impact-corpus`. Every change must be an instance of the B37 item
the task names. Anything else is a defect to fix in the engine, not a diff to commit.

## Structure

Each scenario has its own sub-directory:

- `steps.json` — the recorded argv, exit code, stderr and output files for every step.
- `expected/<step>/` — the captured output bytes for each step.
- `inputs/<step>/` — the input files (descriptions, supplements, assessment records) referenced
  by the step.

## Tokens

The capture replaces absolute paths with tokens so the baseline is portable:

- `$output` — the `--output` directory value.
- `$checkout:<id>` — the path in each `--checkout <id>=<path>`.
- `$input/<step>/<basename>` — each copied input file.

## Changing expected files

Any change to `expected/` must be its own commit, with a message naming the item of B37's list
that allows the change. Never change expected files alongside other work.
