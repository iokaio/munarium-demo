// SPDX-License-Identifier: Apache-2.0
// Real local HTTP checks for Production defaults, operator isolation and error redaction.
const assert = require('node:assert/strict');
const crypto = require('node:crypto');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const http = require('node:http');
const { spawn } = require('node:child_process');
const { once } = require('node:events');

(async () => {
  const secret = crypto.randomBytes(32).toString('hex');
  const scratch = fs.mkdtempSync(path.join(os.tmpdir(), 'demo-public-config-'));
  const backend = http.createServer((req, res) => {
    res.writeHead(500, { 'Content-Type': 'application/problem+json' });
    res.end(JSON.stringify({ type: 'https://private-origin.example.invalid/internal-error',
      detail: 'Private operational detail must not reach the browser' }));
  });
  backend.listen(0, '127.0.0.1'); await once(backend, 'listening');
  async function start(extra = {}) {
    const child = spawn('dotnet', [path.resolve(__dirname, '../src/Demo.Web/bin/Release/net10.0/Demo.Web.dll')], {
      cwd: path.resolve(__dirname, '../src/Demo.Web'), windowsHide: true,
      env: { ...process.env, ASPNETCORE_ENVIRONMENT: 'Production', ASPNETCORE_URLS: 'http://127.0.0.1:0',
        MUNARIUM_BASE_URL: `http://127.0.0.1:${backend.address().port}`, MUNARIUM_MGMT_TOKEN: 'test-only-management',
        DEMO_GATE_SECRET: secret, Gate__Disabled: 'true', OperatorConsole__Enabled: 'false',
        DEMO_ADMIN_PASSWORD: '', DEMO_ADMIN_USER: '', DEMO_STORE_PATH: path.join(scratch, 'visitors.sqlite'), ...extra },
      stdio: ['ignore', 'pipe', 'pipe'],
    });
    let log = '';
    child.stdout.on('data', b => { log += b; }); child.stderr.on('data', b => { log += b; });
    for (let n = 0; n < 150; n++) {
      const base = log.match(/Now listening on: (http:\/\/127\.0\.0\.1:\d+)/)?.[1];
      if (base || child.exitCode !== null || child.signalCode !== null) return { child, base, log };
      await new Promise(resolve => setTimeout(resolve, 100));
    }
    child.kill(); throw new Error('Local test application did not start');
  }
  async function stop(child) {
    if (child.exitCode === null && child.signalCode === null) { child.kill(); await once(child, 'exit'); }
  }
  let app;
  try {
    for (const extra of [{ DEMO_GATE_SECRET: 'weak' }, { DEMO_ADMIN_PASSWORD: 'weak', DEMO_ADMIN_USER: 'operator' }]) {
      const result = await start(extra);
      try { assert(!result.base && result.child.exitCode !== 0, 'Weak Production configuration must fail at startup'); }
      finally { await stop(result.child); }
    }
    app = await start(); assert(app.base, app.log);
    assert.equal((await fetch(app.base + '/support', { redirect: 'manual' })).status, 302,
      'Production must ignore a requested Development bypass');
    const now = Math.floor(Date.now() / 1000);
    const payload = `${now + 3600}.${now}.${crypto.randomBytes(16).toString('hex')}.${Buffer.from('em-public-check').toString('hex')}`;
    const cookie = 'demo_gate=' + payload + '.' + crypto.createHmac('sha256', secret + '|cookie').update(payload).digest('hex');
    for (const endpoint of ['/admin/console', '/matrix-admin/']) {
      assert.equal((await fetch(app.base + endpoint, { headers: { Cookie: cookie }, redirect: 'manual' })).status, 404);
    }
    for (const endpoint of ['/api/search/support', '/api/chat/support', '/api/chat/support/stream']) {
      const response = await fetch(app.base + endpoint, { method: 'POST', headers: { Cookie: cookie, 'Content-Type': 'application/json' },
        body: JSON.stringify({ query: 'fixture', message: 'fixture', family: 'claude', tier: 'fast' }) });
      const body = await response.text();
      assert(response.status >= 400 || (endpoint.endsWith('/stream') && body.includes('event: error')), endpoint);
      assert(!body.includes('private-origin') && !body.includes('Private operational detail'), endpoint + ': upstream detail leaked');
    }
    await stop(app.child); app = await start({ OperatorConsole__Enabled: 'true' }); assert(app.base, app.log);
    for (const endpoint of ['/admin/console', '/matrix-admin/']) {
      assert.equal((await fetch(app.base + endpoint, { headers: { Cookie: cookie }, redirect: 'manual' })).status, 401,
        'Visitor admission must not grant operator access');
    }
    await stop(app.child);
    const admin = { DEMO_ADMIN_USER: 'test-operator', DEMO_ADMIN_PASSWORD: crypto.randomBytes(24).toString('hex') };
    app = await start(admin); assert(app.base, app.log);
    const { request } = require('playwright');
    const contexts = [];
    async function context() { const c = await request.newContext({ baseURL: app.base }); contexts.push(c); return c; }
    async function form(client, url, fields) {
      const page = await (await client.get(url.split('?')[0])).text();
      const csrf = page.match(/name="__RequestVerificationToken"[^>]*value="([^"]+)"/)?.[1];
      assert(csrf, 'Antiforgery token missing');
      return client.post(url, { form: { ...fields, __RequestVerificationToken: csrf } });
    }
    try {
      const operator = await context();
      await form(operator, '/admin?handler=Login', { Username: admin.DEMO_ADMIN_USER, Password: admin.DEMO_ADMIN_PASSWORD });
      const issued = await (await form(operator, '/admin?handler=IssueCode', { Email: 'fixture@example.invalid' })).text();
      const code = issued.match(/<code class="fs-6">([^<]+)<\/code>/)?.[1]; assert(code, 'Operator did not issue a code');
      let visitor = await context();
      const admitted = await form(visitor, '/gate', { Email: 'fixture@example.invalid', Code: code, ReturnUrl: '/support' });
      assert(admitted.url().endsWith('/support'), 'Issued code must admit its visitor without sending mail');
      await stop(app.child); app = await start(admin); assert(app.base, app.log);
      visitor = await context();
      const restored = await form(visitor, '/gate', { Email: 'fixture@example.invalid', Code: code, ReturnUrl: '/support' });
      assert(restored.url().endsWith('/support'), 'Visitor code must survive a web-process restart');
      const newOperator = await context();
      await form(newOperator, '/admin?handler=Login', { Username: admin.DEMO_ADMIN_USER, Password: admin.DEMO_ADMIN_PASSWORD });
      await form(newOperator, '/admin?handler=BlockEmail', { Email: 'fixture@example.invalid' });
      const blockedVisitor = await context();
      const refused = await form(blockedVisitor, '/gate', { Email: 'fixture@example.invalid', Code: code, ReturnUrl: '/support' });
      assert(refused.url().endsWith('/gate'), 'Blocked visitor must not be admitted');
    } finally { for (const client of contexts) await client.dispose(); }
    console.log('PASS: Production secrets/gate, operator isolation, error redaction, code issuance/reuse, SQLite restart persistence, and blocking.');
  } finally {
    if (app) await stop(app.child);
    backend.close();
  }
})().catch(error => { console.error(error); process.exitCode = 1; });
