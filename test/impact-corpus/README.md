# Impact corpus

This directory is the **B37 baseline** for the impact assessment engine. It captures the exact
byte-for-byte outputs of the Node engine (at commit `c6c071c`) for every C4 scenario used in
acceptance testing.

## Replay

Run the capture to regenerate the baseline from the Node engine:

```bash
node test/impact-corpus/capture.mjs
```

Then run the conformance test to verify:

```bash
node --test test/impact-corpus.test.mjs
```

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
