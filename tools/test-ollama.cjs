// SPDX-License-Identifier: Apache-2.0
// Exercise real BFF admission and, when PLAYWRIGHT_MODULE is set, the actual browser toolbar.
const assert = require('node:assert/strict');
const http = require('node:http');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const { spawn } = require('node:child_process');
const { once } = require('node:events');

(async () => {
  let ready = false, malformed = false, delay = false;
  let readyFast = 'qwen3.6:35b';
  let expiresAt = new Date(Date.now() + 3600000).toISOString();
  const turns = [];
  const backend = http.createServer(async (req, res) => {
    res.setHeader('Content-Type', 'application/json');
    let raw = '';
    for await (const chunk of req) raw += chunk;
    if (req.url === '/readyz') {
      assert.equal(req.headers.authorization, 'Bearer gateway-test');
      if (delay) { setTimeout(() => res.end('{}'), 4000); return; }
      res.end(malformed ? '{' : JSON.stringify({ ready, expiresAt, fast: readyFast, capable: 'qwen3.6:27b' }));
    } else if (req.url === '/v1/providers') {
      res.end(JSON.stringify({ providers: [
        { name: 'demo-anthropic', provider: 'anthropic', credential_ok: true, fast: 'cloud-fast', capable: 'cloud-capable', frontier: 'cloud-frontier' },
        { name: 'demo-ollama', provider: 'ollama', credential_ok: true, fast: 'qwen3.6:35b', capable: 'qwen3.6:27b' },
      ] }));
    } else if (req.url === '/v1/access-tokens') {
      res.end(JSON.stringify({ token: 'test', expires_at: '2099-01-01T00:00:00Z' }));
    } else if (/\/runbooks\/[^/]+\/sessions$/.test(req.url)) {
      res.end(JSON.stringify({ session_id: 'test-session', runbook_ref: 'test@1', permitted_collections: [] }));
    } else if (/\/sessions\/[^/]+\/turns(?:\/stream)?$/.test(req.url)) {
      const body = JSON.parse(raw); turns.push(body);
      const result = { session_id: 'test-session', collections_searched: [], hits: [], completion: {
        provider: body.model_override.provider, model: 'test', text: 'Controlled answer', input_tokens: 1, output_tokens: 1,
      } };
      if (req.url.endsWith('/stream')) {
        res.setHeader('Content-Type', 'text/event-stream');
        res.end('event: done\ndata: ' + JSON.stringify(result) + '\n\n');
      } else res.end(JSON.stringify(result));
    } else { res.statusCode = 404; res.end('{}'); }
  });
  backend.listen(0, '127.0.0.1'); await once(backend, 'listening');
  const scratch = fs.mkdtempSync(path.join(os.tmpdir(), 'demo-ollama-'));
  const endpoint = `http://127.0.0.1:${backend.address().port}`;
  const app = spawn('dotnet', [path.resolve(__dirname, '../src/Demo.Web/bin/Release/net10.0/Demo.Web.dll')], {
    cwd: path.resolve(__dirname, '../src/Demo.Web'), windowsHide: true,
    env: { ...process.env, ASPNETCORE_ENVIRONMENT: 'Development', ASPNETCORE_URLS: 'http://127.0.0.1:0', Gate__Disabled: 'true',
      MUNARIUM_BASE_URL: endpoint, MUNARIUM_MGMT_TOKEN: 'test', DEMO_GATE_SECRET: 'test', DEMO_STORE_PATH: path.join(scratch, 'demo.sqlite'),
      DEMO_OLLAMA_URL: endpoint, DEMO_OLLAMA_KEY: 'gateway-test' }, stdio: ['ignore', 'pipe', 'pipe'],
  });
  let log = '', browser;
  app.stdout.on('data', chunk => { log += chunk; }); app.stderr.on('data', chunk => { log += chunk; });
  try {
    let base;
    for (let n = 0; n < 100; n++) {
      base = log.match(/Now listening on: (http:\/\/127\.0\.0\.1:\d+)/)?.[1];
      if (base) break;
      if (app.exitCode !== null) throw new Error(log);
      await new Promise(resolve => setTimeout(resolve, 100));
    }
    assert(base, log);
    const catalog = async () => {
      const response = await fetch(base + '/api/models');
      assert.equal(response.headers.get('cache-control'), 'no-store');
      const body = await response.json();
      assert(body.families.claude, 'Cloud providers must survive Ollama unavailability');
      assert(!JSON.stringify(body).includes('gateway-test'));
      return body.families.ollama;
    };
    const chat = (suffix = '', family = 'ollama', tier = 'fast') => fetch(base + '/api/chat/revolution' + suffix, {
      method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ message: 'Who wrote this?', family, tier }),
    });
    assert.equal(await catalog(), null, 'Not warm must stay hidden');
    for (const suffix of ['', '/stream']) assert.equal((await chat(suffix)).status, 503);
    assert.equal(turns.length, 0, 'Unavailable requests must not reach the model or consume turns');
    ready = true;
    assert.equal((await catalog()).fast, 'qwen3.6:35b', 'Readiness must not wait for the five-minute catalog cache');
    readyFast = ''; assert.equal(await catalog(), null, 'Missing ready model must fail closed');
    readyFast = 'different-model'; assert.equal(await catalog(), null, 'Catalog and warm model must agree');
    readyFast = 'qwen3.6:35b';
    for (const suffix of ['', '/stream']) {
      const response = await chat(suffix); assert.equal(response.status, 200); await response.text();
      assert.equal(turns.at(-1).model_override.provider, 'demo-ollama');
      assert.equal((await chat(suffix, 'ollama', 'frontier')).status, 400);
      assert.equal((await chat(suffix, 'made-up')).status, 400);
    }
    expiresAt = new Date(Date.now() - 1000).toISOString();
    assert.equal(await catalog(), null);
    assert.equal((await chat()).status, 503);
    expiresAt = new Date(Date.now() + 4 * 3600000).toISOString();
    assert.equal(await catalog(), null, 'Implausible lease must fail closed');
    expiresAt = new Date(Date.now() + 3600000).toISOString();
    malformed = true; assert.equal(await catalog(), null); malformed = false;
    delay = true; assert.equal(await catalog(), null, 'Probe timeout must preserve cloud choices'); delay = false;
    ready = false;
    assert.equal((await chat('', 'claude')).status, 200, 'Ollama being off must not prevent cloud chat');
    console.log('PASS: readiness, warmup, expiry, malformed/slow probes, routing, both chat paths, and cloud independence.');
    if (process.env.PLAYWRIGHT_MODULE) {
      const { chromium } = require(process.env.PLAYWRIGHT_MODULE);
      browser = await chromium.launch({ headless: true });
      const page = await browser.newPage();
      const errors = []; page.on('pageerror', error => errors.push(String(error)));
      await page.goto(base + '/revolution');
      const button = page.locator('[data-dm-family="ollama"]');
      await page.waitForResponse(response => response.url().endsWith('/api/models'));
      assert.equal(await button.isVisible(), false);
      ready = true;
      await page.reload(); await button.waitFor({ state: 'visible' });
      await page.locator('[data-dm-tier="frontier"]').click();
      await button.click();
      assert.equal(await page.locator('[data-dm-tier="frontier"]').isDisabled(), true);
      assert.match(await page.locator('[data-dm-model-note]').innerText(), /qwen3.6:35b/);
      // Lease changes during polling must also remove a selected option.
      expiresAt = new Date(Date.now() + 18000).toISOString();
      await button.waitFor({ state: 'hidden', timeout: 40000 });
      assert.match(await page.locator('[data-dm-family="claude"]').getAttribute('class'), /active/);
      assert.equal(await page.locator('[data-dm-tier="frontier"]').isDisabled(), false);
      assert.deepEqual(errors, []);
      console.log('PASS: real browser hides/shows Ollama, restricts Frontier, and expires a selected provider.');
    }
  } finally {
    if (browser) await browser.close();
    app.kill(); if (app.exitCode === null) await once(app, 'exit');
    backend.closeAllConnections(); backend.close();
  }
})().catch(error => { console.error(error); process.exitCode = 1; });
