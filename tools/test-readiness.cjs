// SPDX-License-Identifier: Apache-2.0
// Exercise real HTTP endpoints against a controlled backend; no Azure/provider calls.
const assert = require('node:assert/strict');
const http = require('node:http');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const { spawn } = require('node:child_process');
const { once } = require('node:events');

(async () => {
  const scratch = fs.mkdtempSync(path.join(os.tmpdir(), 'demo-readiness-'));
  let backendStatus = 200;
  let backendBody = '{"ok":true}';
  let lastPath;
  const backend = http.createServer((request, response) => {
    lastPath = request.url;
    response.writeHead(backendStatus, { 'Content-Type': 'application/json' });
    response.end(backendBody);
  });
  backend.listen(0, '127.0.0.1');
  await once(backend, 'listening');
  const app = spawn('dotnet', [path.resolve(__dirname, '../src/Demo.Web/bin/Release/net10.0/Demo.Web.dll')], {
    cwd: path.resolve(__dirname, '../src/Demo.Web'),
    windowsHide: true,
    env: {
      ...process.env,
      ASPNETCORE_ENVIRONMENT: 'Production',
      ASPNETCORE_URLS: 'http://127.0.0.1:0',
      MUNARIUM_BASE_URL: `http://127.0.0.1:${backend.address().port}`,
      MUNARIUM_MGMT_TOKEN: 'unused-test-token',
      DEMO_GATE_SECRET: 'test-only-readiness-gate-32-characters',
      DEMO_STORE_PATH: path.join(scratch, 'demo.sqlite'),
    },
    stdio: ['ignore', 'pipe', 'pipe'],
  });
  let log = '';
  app.stdout.on('data', data => { log += data; });
  app.stderr.on('data', data => { log += data; });
  try {
    let base;
    for (let attempt = 0; attempt < 100; attempt++) {
      base = log.match(/Now listening on: (http:\/\/127\.0\.0\.1:\d+)/)?.[1];
      if (base) break;
      if (app.exitCode !== null) throw new Error(`Demo exited: ${log}`);
      await new Promise(resolve => setTimeout(resolve, 100));
    }
    assert(base, `Demo did not start: ${log}`);
    const gated = await fetch(base + '/dataroom', { redirect: 'manual' });
    assert.equal(gated.status, 302, 'Visitor pages must remain gated');
    for (const [status, body, expected] of [
      [200, '{"ok":true}', 200],
      [503, '{"ok":false}', 503],
      [200, '{"ok":false}', 503],
      [200, '{"ok":"true"}', 503],
      [200, '<html>login</html>', 503],
      [200, '{}', 503],
    ]) {
      backendStatus = status;
      backendBody = body;
      const response = await fetch(base + '/readyz', { redirect: 'manual' });
      assert.equal(response.status, expected, `Backend ${status}: ${body}`);
      assert.deepEqual(await response.json(), { ok: expected === 200, munarium: expected === 200 });
      assert.equal(lastPath, '/readyz', 'Admission must use backend readiness');
    }
    backend.close();
    await once(backend, 'close');
    assert.equal((await fetch(base + '/readyz')).status, 503, 'Unreachable backend');
    assert.equal((await fetch(base + '/livez')).status, 200, 'Backend failure must not restart the web app');
    console.log('PASS: ungated readiness, backend failure/body validation, visitor gate, independent liveness');
  } finally {
    app.kill();
    if (app.exitCode === null) await once(app, 'exit');
    backend.close();
    // Keep test data in its uniquely named OS temp directory for failure diagnosis.
  }
})().catch(error => { console.error(error); process.exitCode = 1; });
