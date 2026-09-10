// SPDX-License-Identifier: Apache-2.0
// Real Razor pages and browser interactions with deterministic, local API responses.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const { spawn } = require('node:child_process');
const { once } = require('node:events');

(async () => {
  const { chromium } = require(process.env.PLAYWRIGHT_MODULE || 'playwright');
  const scratch = fs.mkdtempSync(path.join(os.tmpdir(), 'demo-workspace-'));
  const output = process.env.DEMO_DESIGN_OUTPUT || scratch;
  fs.mkdirSync(output, { recursive: true });
  const app = spawn('dotnet', [path.resolve(__dirname, '../src/Demo.Web/bin/Release/net10.0/Demo.Web.dll')], {
    cwd: path.resolve(__dirname, '../src/Demo.Web'), windowsHide: true,
    env: { ...process.env, ASPNETCORE_ENVIRONMENT: 'Development', ASPNETCORE_URLS: 'http://127.0.0.1:0',
      Gate__Disabled: 'true', MUNARIUM_BASE_URL: 'http://127.0.0.1:1', MUNARIUM_MGMT_TOKEN: 'test-only',
      DEMO_GATE_SECRET: 'test-only', DEMO_STORE_PATH: path.join(scratch, 'demo.sqlite') },
    stdio: ['ignore', 'pipe', 'pipe'],
  });
  let log = '', browser;
  app.stdout.on('data', data => { log += data; }); app.stderr.on('data', data => { log += data; });
  try {
    let base;
    for (let n = 0; n < 100; n++) {
      base = log.match(/Now listening on: (http:\/\/127\.0\.0\.1:\d+)/)?.[1];
      if (base) break;
      if (app.exitCode !== null) throw new Error(log);
      await new Promise(resolve => setTimeout(resolve, 100));
    }
    assert(base, log);
    browser = await chromium.launch({ headless: true });
    const page = await browser.newPage({ viewport: { width: 1440, height: 1000 } });
    const errors = [], sent = [];
    page.on('pageerror', error => errors.push(String(error)));
    await page.route('**/api/**', async route => {
      const url = route.request().url();
      let body = {};
      if (url.endsWith('/api/models')) body = { families: {
        claude: { provider: 'anthropic', fast: 'claude-fast', capable: 'claude-capable', frontier: 'claude-frontier' },
        gpt: { provider: 'openai', fast: 'gpt-fast', capable: 'gpt-capable', frontier: 'gpt-frontier' },
        openrouter: { provider: 'openrouter', fast: 'router-fast', capable: 'router-capable', frontier: 'router-frontier' },
        ollama: { provider: 'ollama', fast: 'mistral-nemo:12b', capable: 'qwen3.8:27b', expiresAt: new Date(Date.now() + 3600000).toISOString() },
      } };
      else if (url.includes('/api/chat/')) {
        sent.push(route.request().postDataJSON());
        body = { answer: 'A controlled answer for layout verification.\n\n' + 'Evidence remains readable as the conversation grows.\n'.repeat(90),
          model: 'qwen3.8:27b', provider: 'ollama', outputTokens: 100, inputTokens: 200,
          remaining: 8, verification: { verified: true, checks: ['citations'], violations: [] },
          hits: [{ docId: 'test/document', snippet: 'Controlled source excerpt' }] };
        if (url.endsWith('/stream')) {
          const progress = (sent.at(-1).message === 'No progress' ? [] : [
            { stage: 'expansion', provider: 'ollama', model: 'qwen3.8:27b', terms: ['history turn ' + sent.length] },
            { stage: 'model', provider: 'demo-ollama', tier: 'capable', was_override: true },
            { stage: 'completion', provider: 'ollama', model: 'qwen3.8:27b', attempt: 0 },
          ]).map(event => 'event: progress\ndata: ' + JSON.stringify(event) + '\n\n').join('');
          const ending = sent.at(-1).message === 'Failed turn'
            ? 'event: error\ndata: ' + JSON.stringify({ error: 'test', message: 'Controlled failure' })
            : 'event: done\ndata: ' + JSON.stringify(body);
          await route.fulfill({ contentType: 'text/event-stream', body: progress + ending + '\n\n' });
          return;
        }
      } else if (url.includes('/api/search/')) body = { hits: [{ docId: 'controlled/search-result', snippet: 'Search stays available.', score: 1 }] };
      await route.fulfill({ contentType: 'application/json', body: JSON.stringify(body) });
    });
    await page.goto(base + '/');
    assert.equal(await page.locator('.dm-collection-card').count(), 6);
    await page.screenshot({ path: path.join(output, 'overview-desktop.png'), fullPage: true });
    for (const corpus of ['revolution', 'support', 'dataroom', 'advisory', 'patents', 'intel']) {
      const response = await page.goto(base + '/' + corpus);
      assert.equal(response.status(), 200, corpus + ' must render');
      await page.locator('[data-dm-family="ollama"]').waitFor({ state: 'visible' });
      assert.equal(await page.locator('.dm-sidebar [data-dm-family]').count(), 4);
      assert.equal(await page.locator('.dm-sidebar [data-dm-tier]').count(), 3);
      assert(await page.locator('.dm-sidebar [data-dm-chip]').count() >= 4);
      const dimensions = await page.evaluate(() => ({ height: innerHeight, scroll: document.documentElement.scrollHeight,
        width: innerWidth, scrollWidth: document.documentElement.scrollWidth,
        composerBottom: document.querySelector('.dm-composer').getBoundingClientRect().bottom,
        transcriptHeight: document.querySelector('.dm-transcript').clientHeight }));
      assert(dimensions.scroll <= dimensions.height + 1, corpus + ': page must not scroll');
      assert(dimensions.scrollWidth <= dimensions.width + 1, corpus + ': horizontal overflow');
      assert(Math.abs(dimensions.composerBottom - dimensions.height) <= 1, corpus + ': composer must anchor to viewport bottom');
      assert(dimensions.transcriptHeight > 500, corpus + ': transcript must fill available height');
      await page.getByRole('button', { name: 'About this collection' }).click();
      await page.locator('#collection-briefing.show').waitFor();
      assert.equal(await page.locator('#collection-briefing a[download]').count(), 2);
      if (corpus === 'revolution') await page.screenshot({ path: path.join(output, 'collection-details.png') });
      await page.keyboard.press('Escape');
      await page.locator('#collection-briefing').waitFor({ state: 'hidden' });
      await page.locator('[data-dm-family="ollama"]').click();
      assert(await page.locator('[data-dm-tier="frontier"]').isDisabled());
      await page.locator('[data-dm-tier="capable"]').click();
      assert.equal(await page.locator('[data-dm-tier="capable"]').getAttribute('aria-pressed'), 'true');
      assert.match(await page.locator('[data-dm-model-note]').innerText(), /qwen3\.8:27b/);
      await page.locator('[data-dm-chip]').first().click();
      assert((await page.locator('[data-dm-input]').inputValue()).length > 10);
      if (corpus === 'revolution') await page.screenshot({ path: path.join(output, 'workspace-desktop.png') });
      if (corpus === 'dataroom') {
        await page.locator('.dm-persona-settings summary').click();
        await page.locator('[data-dm-persona="cleanteam"]').click();
        assert.match(await page.locator('[data-dm-persona-scope]').innerText(), /Clean team: all/);
      }
    }
    await page.goto(base + '/revolution');
    await page.locator('[data-dm-family="ollama"]').click();
    await page.locator('[data-dm-tier="capable"]').click();
    await page.locator('[data-dm-input]').fill('First line');
    await page.locator('[data-dm-input]').press('Shift+Enter');
    assert.match(await page.locator('[data-dm-input]').inputValue(), /\n/);
    await page.locator('[data-dm-input]').press('Enter');
    await page.locator('.dm-bubble-assistant').waitFor();
    assert.equal(sent.length, 1, 'Enter sends exactly one question');
    assert.match(await page.locator('[data-dm-progress="expansion"]').textContent(), /Search preparation via ollama \/ qwen3\.8:27b/);
    assert.match(await page.locator('[data-dm-progress="model"]').textContent(), /Answer model resolved: ollama \/ qwen3\.8:27b \(your selection\)/);
    assert.match(await page.locator('[data-dm-model-note]').innerText(), /Answer model:.*qwen3\.8:27b/);
    const before = await page.locator('.dm-composer').boundingBox();
    const sidebarBefore = await page.locator('.dm-sidebar-settings').boundingBox();
    await page.locator('[data-dm-transcript]').evaluate(el => { el.scrollTop = el.scrollHeight; });
    const after = await page.locator('.dm-composer').boundingBox();
    assert.deepEqual(after, before, 'Long transcript must not move composer');
    assert.deepEqual(await page.locator('.dm-sidebar-settings').boundingBox(), sidebarBefore, 'Chat scrolling must not move sidebar controls');
    const firstSteps = page.locator('.dm-bubble-assistant').first().locator('.dm-turn-steps');
    const firstToggle = firstSteps.getByRole('button');
    const firstDetails = firstSteps.locator('.dm-steps-details');
    assert.equal(await firstToggle.getAttribute('aria-expanded'), 'false');
    assert.equal(await firstDetails.isVisible(), false);
    assert.equal(await firstToggle.getAttribute('aria-controls'), await firstDetails.getAttribute('id'));
    await firstToggle.click();
    assert(await firstDetails.isVisible());
    assert.equal(await firstToggle.getAttribute('aria-expanded'), 'true');
    assert.equal(await firstDetails.locator('[aria-live], .spinner-border').count(), 0, 'Archived steps are static');
    assert.equal(await page.locator('[data-dm-thinking-log] > *').count(), 0);
    const firstRecord = await firstDetails.textContent();
    await firstToggle.press('Enter');
    assert.equal(await firstDetails.isVisible(), false);
    await firstToggle.press('Space');
    assert(await firstDetails.isVisible());
    assert.equal(sent.length, 1, 'Expanding and collapsing never sends another request');
    assert.deepEqual(await page.locator('.dm-composer').boundingBox(), before, 'Expanded steps do not move composer');
    await page.screenshot({ path: path.join(output, 'server-steps-expanded.png') });
    await page.locator('[data-dm-input]').fill('Second question');
    await page.locator('[data-dm-send]').click();
    await page.locator('.dm-bubble-assistant').nth(1).waitFor();
    assert.equal(await firstDetails.textContent(), firstRecord, 'New turns leave earlier steps unchanged');
    assert(await firstDetails.isVisible(), 'Earlier expansion stays open');
    const secondSteps = page.locator('.dm-bubble-assistant').nth(1).locator('.dm-steps-details');
    assert.equal(await secondSteps.isVisible(), false);
    assert.match(await secondSteps.textContent(), /history turn 2/);
    assert.notEqual(await secondSteps.getAttribute('id'), await firstDetails.getAttribute('id'));
    await page.locator('[data-dm-input]').fill('No progress');
    await page.locator('[data-dm-send]').click();
    await page.locator('.dm-bubble-assistant').nth(2).waitFor();
    assert.equal(await page.locator('.dm-bubble-assistant').nth(2).locator('.dm-turn-steps').count(), 0, 'No stale steps on turns without progress');
    await page.locator('[data-dm-input]').fill('Failed turn');
    await page.locator('[data-dm-send]').click();
    const failedTurn = page.locator('.dm-bubble-error').filter({ hasText: 'Controlled failure' });
    await failedTurn.waitFor();
    await failedTurn.getByRole('button', { name: 'Show server steps' }).click();
    assert.match(await failedTurn.locator('.dm-steps-details').innerText(), /history turn 4/);
    await page.getByRole('tab', { name: 'Search sources' }).click();
    await page.locator('[data-dm-search-input]').fill('source');
    await page.locator('[data-dm-search-form] button').click();
    await page.getByText('controlled/search-result', { exact: true }).waitFor();
    await page.locator('[data-dm-chip]').first().click();
    assert(await page.locator('#tab-chat').isVisible(), 'Question selection returns to chat');
    for (const size of [{ width: 390, height: 844 }, { width: 360, height: 640 }, { width: 844, height: 390 }]) {
      await page.setViewportSize(size);
      await page.goto(base + '/revolution');
      const composer = await page.locator('.dm-composer').boundingBox();
      await page.screenshot({ path: path.join(output, `mobile-${size.width}-${size.height}.png`) });
      assert(Math.abs(composer.y + composer.height - size.height) <= 1, 'Mobile composer anchored: ' + JSON.stringify({ size, composer }));
      assert(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), 'Mobile horizontal overflow');
      await page.getByRole('button', { name: 'Explore', exact: true }).click();
      await page.locator('#collection-sidebar.show').waitFor();
      await page.locator('[data-dm-chip]').first().click();
      await page.locator('#collection-sidebar').waitFor({ state: 'hidden' });
      assert(await page.locator('[data-dm-input]').evaluate(el => el === document.activeElement));
      if (size.width === 390) await page.screenshot({ path: path.join(output, 'workspace-mobile.png') });
    }
    assert.deepEqual(errors, []);
    console.log('PASS: six collection layouts, persistent controls and composer, flyouts, search, persona, model selection, keyboard input, and mobile sizes.');
    console.log('Screenshots: ' + output);
  } finally {
    if (browser) await browser.close();
    app.kill(); if (app.exitCode === null) await once(app, 'exit');
  }
})().catch(error => { console.error(error); process.exitCode = 1; });
