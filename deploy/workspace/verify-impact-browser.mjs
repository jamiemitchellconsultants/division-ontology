import assert from 'node:assert/strict';
import { AxeBuilder } from '@axe-core/playwright';

// Q6: the impact pages work from the keyboard and have no serious or critical WCAG 2.2 AA violations.
export async function verifyImpactPages({ reader, origin }) {
  const page = await reader.page.context().newPage();
  try {
    await page.goto(origin + '/impact');
    await page.getByLabel('Change request description').fill('order');
    await page.keyboard.press('Enter');

    // Reach the first supported lead with Tab and choose it with Space.
    const box = page.locator('input[type=checkbox]:not([disabled])').first();
    await box.waitFor();
    for (let i = 0; i < 40 && !(await box.evaluate(el => el === document.activeElement)); i++) await page.keyboard.press('Tab');
    assert.ok(await box.evaluate(el => el === document.activeElement), 'a supported lead is reachable with Tab');
    await page.keyboard.press('Space');
    assert.ok(await box.isChecked(), 'Space chooses the focused lead');

    const trace = page.getByRole('button', { name: 'Trace impact' });
    for (let i = 0; i < 40 && !(await trace.evaluate(el => el === document.activeElement)); i++) await page.keyboard.press('Tab');
    assert.ok(await trace.evaluate(el => el === document.activeElement), 'Trace impact is reachable with Tab');
    await page.keyboard.press('Enter');
    await page.getByRole('heading', { name: 'Limits of this result' }).waitFor();

    const results = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21aa', 'wcag22aa']).analyze();
    const serious = results.violations.filter(v => ['serious', 'critical'].includes(v.impact));
    assert.deepEqual(serious.map(v => v.id), [], 'Q6 impact pages');
    console.log(JSON.stringify({ check: 'impact-pages', axeViolations: results.violations.length, serious: serious.length }));
  } finally {
    await page.close();
  }
}
