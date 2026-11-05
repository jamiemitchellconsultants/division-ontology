# Synthetic three-system fixture

This disposable, local acceptance input models an ecommerce site, a warehouse and an accounting system. It creates three independent Git repositories with committed Turtle ontologies and source knowledge graphs, plus an isolated division repository with a catalog built by the real builder and example C4 mappings and bindings. No GitHub repositories or network access are needed. The remote URLs are fictional identity markers; always pass the local checkout overrides.

All descriptions, evidence, mappings and review decisions are invented for testing. They are not evidence about production systems. The scenario follows an ecommerce order to warehouse fulfilment and then accounting invoicing; accounting also describes an unjoined external bank. The ontologies include sales, purchasing and general ledger concepts and repeated labels which remain distinct. Evidence is committed architecture documentation, not application code. Each graph intentionally reports unknown code and excluded private coverage, so code views cannot demonstrate real code inspection.

## Set up and explore

Requires Node.js 20+, Git and `npm ci` in this project. From the project root, choose an **absolute path that does not yet exist**, outside all Git checkouts:

```sh
node test/fixtures/representative/setup.mjs setup /tmp/division-demo
```

The generated `/tmp/division-demo/fixture.json` contains the synthetic catalog and registry commit, source paths and output path. The three source checkouts are clean and the division catalog is generated at the same source commits as the graph evidence. Each setup creates new Git commits; use the commit reported by that setup, not an old example. For convenience, from the project root:

```sh
fixture=/tmp/division-demo
project="$PWD"
revision=$(git -C "$fixture/division" rev-parse HEAD)
cd "$fixture/division"
node "$project/src/c4/cli.mjs" paths \
  --catalog-commit "$revision" --registry-commit "$revision" \
  --checkout "fixture/ecommerce=$fixture/ecommerce" \
  --checkout "fixture/warehouse=$fixture/warehouse" \
  --checkout "fixture/accounting=$fixture/accounting" \
  --output "$fixture/output" \
  --start 'fixture/ecommerce=https://example.test/ecommerce/graph#system'
```

Inspect `output/paths.json`: it contains the source assertions and two separate reviewed-for-the-fixture binding steps, along with source commits and coverage gaps. Change `paths` to `search` and supply `--query 'Order'` in place of `--start` to find distinct source concepts, or use `view` with `--focus 'fixture/ecommerce=https://example.test/ecommerce/graph#system' --level context` for a context view. A container view uses the same focus; a component view uses `fixture/ecommerce=https://example.test/ecommerce/graph#container`. A code view for the `component` node reports missing code coverage. These are starting points for a user-led walkthrough, not recorded acceptance results.

To run the catalog consistency check against the same source checkouts, from the synthetic division directory:

```sh
node "$project/src/cli.mjs" check \
  --checkout "fixture/ecommerce=$fixture/ecommerce" \
  --checkout "fixture/warehouse=$fixture/warehouse" \
  --checkout "fixture/accounting=$fixture/accounting"
```

## Tear down

Return to the project root and run:

```sh
cd "$project"
node test/fixtures/representative/setup.mjs teardown /tmp/division-demo
```

This removes the entire marked fixture directory, **including any reports or changes saved inside it**. Copy anything you want to retain elsewhere first. Setup refuses to use an existing path, and teardown refuses a directory without the fixture marker. Neither operation changes this project's `sources.json`, production registries or generated `dist/`.
