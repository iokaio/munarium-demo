// SPDX-License-Identifier: Apache-2.0
// Real BFF requests against a controlled backend; no provider or Azure calls.
const assert = require('node:assert/strict');
const http = require('node:http');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const { spawn } = require('node:child_process');
const { once } = require('node:events');

(async () => {
  const scratch = fs.mkdtempSync(path.join(os.tmpdir(), 'demo-chat-context-'));
  const turns = [];
  let sessions = 0;
  const backend = http.createServer(async (request, response) => {
    let raw = '';
    for await (const chunk of request) raw += chunk;
    const body = raw ? JSON.parse(raw) : {};
    response.setHeader('Content-Type', 'application/json');
    if (request.url === '/v1/access-tokens') {
      response.end(JSON.stringify({ token: 'test-capability', jti: 'test', expires_at: '2099-01-01T00:00:00Z' }));
    } else if (/\/runbooks\/[^/]+\/sessions$/.test(request.url)) {
      sessions++;
      response.end(JSON.stringify({ session_id: 'fresh-session-' + sessions, runbook_ref: 'history-revolution@3', permitted_collections: ['letters'] }));
    } else if (/\/sessions\/[^/]+\/turns(?:\/stream)?$/.test(request.url)) {
      turns.push({ path: request.url, body });
      const turn = { session_id: request.url.split('/')[3], ordinal: 1, collections_searched: ['letters'], hits: [],
        completion: { provider: 'test', model: 'test', text: 'Controlled answer', input_tokens: 1, output_tokens: 1, was_override: true } };
      if (request.url.endsWith('/stream')) {
        response.setHeader('Content-Type', 'text/event-stream');
        response.end('event: done\ndata: ' + JSON.stringify(turn) + '\n\n');
      } else response.end(JSON.stringify(turn));
    } else { response.statusCode = 404; response.end('{}'); }
  });
  backend.listen(0, '127.0.0.1');
  await once(backend, 'listening');
  const app = spawn('dotnet', [path.resolve(__dirname, '../src/Demo.Web/bin/Release/net10.0/Demo.Web.dll')], {
    cwd: path.resolve(__dirname, '../src/Demo.Web'), windowsHide: true,
    env: { ...process.env, ASPNETCORE_ENVIRONMENT: 'Development', ASPNETCORE_URLS: 'http://127.0.0.1:0',
      Gate__Disabled: 'true', MUNARIUM_BASE_URL: `http://127.0.0.1:${backend.address().port}`,
      MUNARIUM_MGMT_TOKEN: 'test-only', DEMO_GATE_SECRET: 'test-only', DEMO_STORE_PATH: path.join(scratch, 'demo.sqlite') },
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
      if (app.exitCode !== null) throw new Error(log);
      await new Promise(resolve => setTimeout(resolve, 100));
    }
    assert(base, log);
    const question = 'What did Washington write to Congress about supplying the army at Valley Forge?';
    const history = [{ role: 'user', text: 'OBSOLETE_TOPIC that must never be carried forward' },
      { role: 'assistant', text: 'OBSOLETE_ANSWER that must never be carried forward' },
      { role: 'user', text: 'Which maps depict Boston fortifications?' },
      { role: 'assistant', text: 'Boston map metadata and fortifications. '.repeat(80) }];
    for (const suffix of ['', '/stream']) {
      for (const followUp of [undefined, false, true]) {
        const previousSessions = sessions;
        const response = await fetch(base + '/api/chat/revolution' + suffix, {
          method: 'POST', headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ sessionId: 'existing-session', message: question, history, family: 'gpt', tier: 'capable', followUp }),
        });
        const body = await response.text();
        assert.equal(response.status, 200, body);
        assert(body.includes('Controlled answer'), body);
        const sent = turns.at(-1);
        if (followUp) {
          assert.equal(sessions, previousSessions);
          assert(sent.path.includes('existing-session'));
          assert(sent.body.query.includes('Boston map metadata'));
          assert(!sent.body.query.includes('OBSOLETE_'));
          assert(sent.body.query.endsWith('Current question: ' + question));
          assert(sent.body.query.length < 4500);
        } else {
          assert.equal(sessions, previousSessions + 1, 'Independent question must pin the latest runbook');
          assert(sent.path.includes('fresh-session-'));
          assert.equal(sent.body.query, question, 'Prior answers must not enter retrieval');
        }
      }
    }
    console.log('PASS: unary and streaming chat isolate new questions, protect older clients, pin fresh runbooks, and preserve explicit follow-ups.');
  } finally {
    app.kill();
    if (app.exitCode === null) await once(app, 'exit');
    backend.close();
  }
})().catch(error => { console.error(error); process.exitCode = 1; });
