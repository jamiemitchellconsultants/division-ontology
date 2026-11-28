import assert from 'node:assert/strict';
import { AxeBuilder } from '@axe-core/playwright';

const WCAG = ['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa'];

async function noSeriousViolations(page, label) {
  const results = await new AxeBuilder({ page }).withTags(WCAG).analyze();
  const serious = results.violations.filter(v => ['serious', 'critical'].includes(v.impact));
  assert.deepEqual(serious.map(v => v.id), [], `Q6 ${label}`);
  return results.violations.length;
}

// Moves focus with Tab until the control is focused, then activates it with the given key.
async function tabTo(page, locator, key, name) {
  await locator.waitFor();
  for (let i = 0; i < 60 && !(await locator.evaluate(el => el === document.activeElement)); i++) await page.keyboard.press('Tab');
  assert.ok(await locator.evaluate(el => el === document.activeElement), `${name} is reachable with Tab`);
  if (key) await page.keyboard.press(key);
}

async function searchAndTrace(page, origin) {
  await page.goto(origin + '/impact');
  await page.getByLabel('Change request description').fill('order');
  await page.keyboard.press('Enter');
  const box = page.locator('input[type=checkbox]:not([disabled])').first();
  await tabTo(page, box, 'Space', 'a supported lead');
  assert.ok(await box.isChecked(), 'Space chooses the focused lead');
  await tabTo(page, page.getByRole('button', { name: 'Trace impact' }), 'Enter', 'Trace impact');
  // Search already shows its own limits, so only the paths heading and a step prove the trace itself succeeded.
  await page.getByRole('heading', { name: 'Impact paths' }).waitFor();
  await page.locator('.impact-path .impact-step').first().waitFor();
}

// Q6: the impact pages work from the keyboard and have no serious or critical WCAG 2.2 AA violations.
export async function verifyImpactPages({ reader, origin }) {
  const page = await reader.page.context().newPage();
  try {
    await searchAndTrace(page, origin);
    const violations = await noSeriousViolations(page, 'impact pages');
    console.log(JSON.stringify({ check: 'impact-pages', axeViolations: violations, serious: 0 }));
  } finally {
    await page.close();
  }
}

// Q6, B19, B22: a manager drafts and saves an assessment by keyboard; a reader opens it and cannot edit it.
export async function verifyAssessmentPages({ manager, reader, origin }) {
  const draft = await manager.page.context().newPage();
  let savedUrl;
  try {
    await searchAndTrace(draft, origin);
    await tabTo(draft, draft.getByLabel('Change request id'), null, 'Change request id');
    await draft.keyboard.type(`CR-verify-${Date.now()}`);
    await tabTo(draft, draft.getByRole('button', { name: 'Start assessment' }), 'Enter', 'Start assessment');
    await draft.getByRole('heading', { name: 'Draft impact assessment' }).waitFor();

    await tabTo(draft, draft.getByRole('button', { name: 'Confirm' }).first(), 'Enter', 'Confirm');
    await draft.getByText('Confirmed', { exact: true }).first().waitFor();
    await tabTo(draft, draft.getByRole('button', { name: 'Preview' }), 'Enter', 'Preview');
    await draft.getByRole('heading', { name: 'Preview' }).waitFor();
    const draftViolations = await noSeriousViolations(draft, 'assessment draft page');

    await tabTo(draft, draft.getByRole('button', { name: 'Save' }), 'Enter', 'Save');
    await draft.waitForURL(/\/assessments\/[0-9a-f-]{36}$/);
    await draft.getByRole('heading', { name: 'History' }).waitFor();
    assert.equal(await draft.getByRole('button', { name: 'Edit' }).count(), 1, 'a manager can edit on the same snapshot');
    savedUrl = draft.url();
    const savedViolations = await noSeriousViolations(draft, 'saved assessment page');

    const opened = await reader.page.context().newPage();
    try {
      await opened.goto(savedUrl);
      await opened.getByRole('heading', { name: 'History' }).waitFor();
      assert.equal(await opened.getByRole('button', { name: 'Edit' }).count(), 0, 'a reader sees no Edit button');
      assert.equal(await opened.getByRole('button', { name: /approve|sign[- ]?off/i }).count(), 0, 'no approval control (B31)');
    } finally {
      await opened.close();
    }
    console.log(JSON.stringify({ check: 'assessment-pages', draftViolations, savedViolations, serious: 0 }));
  } finally {
    await draft.close();
  }
}
