// SPDX-License-Identifier: Apache-2.0
/* Munarium demo — chat, search, gallery and persona wiring.
   Vanilla JS, no framework. One state object per page. */

(function () {
    'use strict';

    // ---- helpers -----------------------------------------------------------

    function esc(text) {
        // textContent->innerHTML escapes & < >, but NOT quotes — and this
        // output is interpolated into title="..." attributes. Model-authored
        // text (verification violations quote the model's own words) routinely
        // contains quotes, so escape them explicitly or a reply can break out
        // of the attribute and inject markup.
        const div = document.createElement('div');
        div.textContent = text == null ? '' : String(text);
        return div.innerHTML.replace(/"/g, '&quot;').replace(/'/g, '&#39;');
    }

    function collectionBadgeFromPath(docId) {
        // rev/founders/gw/b3/x -> "founders"; support/helphub/x -> "helphub";
        // northgate/03_finance/x -> "03_finance".
        const parts = String(docId || '').split('/');
        return parts.length > 1 ? parts[1] : '';
    }

    async function postJson(url, body) {
        let resp;
        try {
            resp = await fetch(url, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(body || {}),
            });
        } catch (e) {
            return { ok: false, status: 0, data: { error: 'network', message: 'Network error — check your connection and try again.' } };
        }
        let data = null;
        try { data = await resp.json(); } catch (e) { /* non-JSON error body */ }
        return { ok: resp.ok, status: resp.status, data: data || {} };
    }

    function friendlyError(result) {
        const d = result.data || {};
        if (d.error === 'asleep') {
            return d.message || 'The demo environment appears to be asleep. It scales to zero when idle — try again in a minute.';
        }
        if (d.error === 'turn-budget') {
            return d.message || 'You have used all of today’s demo turns. The budget resets at midnight UTC.';
        }
        if (d.error === 'gate') {
            return 'Your access has expired. Reload the page and enter today’s code.';
        }
        if (d.error === 'model-budget') {
            return (d.message || 'This model tier has reached its daily token budget.') +
                ' Try the Fast or Capable tier, or come back after midnight UTC.';
        }
        if (d.error === 'frontier-budget') {
            return d.message || 'You have used your Frontier requests for this collection today — Fast and Capable remain available.';
        }
        if (result.status === 429) {
            return d.message || 'Rate limited — slow down a little.';
        }
        return d.message || ('Something went wrong (HTTP ' + result.status + '). Try again.');
    }

    // ---- chat panel --------------------------------------------------------

    const panel = document.querySelector('[data-dm-chat]');
    const state = {
        corpus: panel ? panel.getAttribute('data-corpus') : null,
        sessionId: null,
        history: [],
        family: 'claude',
        tier: 'fast',
        persona: null,
        busy: false,
    };

    // Persona selector present? (dataroom)
    const personaButtons = Array.from(document.querySelectorAll('[data-dm-persona]'));
    if (personaButtons.length) {
        state.persona = personaButtons.find(function (b) { return b.classList.contains('active'); })
            ?.getAttribute('data-dm-persona') || 'associate';
    }

    function updatePersonaScope() {
        const note = document.querySelector('[data-dm-persona-scope]');
        if (!note) return;
        const descriptions = {
            associate: 'Buyer associate: general deal folders only. Security incident records require Clean team.',
            counsel: 'Deal counsel: general, finance and legal folders. Security incident records require Clean team.',
            cleanteam: 'Clean team: all data-room folders, including employment and security incident records.',
        };
        note.textContent = descriptions[state.persona];
        personaButtons.forEach(function (button) {
            button.setAttribute('aria-pressed', String(button.getAttribute('data-dm-persona') === state.persona));
        });
    }

    function transcriptEl() { return panel.querySelector('[data-dm-transcript]'); }

    function clearEmptyNote() {
        const empty = panel.querySelector('[data-dm-empty]');
        if (empty) empty.remove();
    }

    function scrollTranscript() {
        const t = transcriptEl();
        t.scrollTop = t.scrollHeight;
    }

    // Put the TOP of a bubble at the top of the scroll area. An answer runs
    // to a thousand characters plus its citations, so scrolling to the bottom
    // (right for a short user echo) lands the reader at the END of the reply
    // and they have to scroll back up to read the first sentence.
    //
    // Measured against the container rather than offsetTop: offsetTop is
    // relative to the nearest positioned ancestor, which need not be the
    // transcript, and this stays correct either way.
    function scrollBubbleToTop(el) {
        const t = transcriptEl();
        t.scrollTop += el.getBoundingClientRect().top - t.getBoundingClientRect().top;
    }

    function addBubble(cls, html, alignTop) {
        clearEmptyNote();
        const div = document.createElement('div');
        div.className = 'dm-bubble ' + cls;
        div.innerHTML = html;
        transcriptEl().appendChild(div);
        if (alignTop) scrollBubbleToTop(div); else scrollTranscript();
        return div;
    }

    function verificationBadge(v) {
        if (!v) return '';
        if (v.verified) {
            let label = 'verified';
            if (v.retries > 0) label += ' after ' + v.retries + ' repair' + (v.retries > 1 ? 's' : '');
            return '<span class="badge dm-badge-verified" title="Deterministic checks: ' + esc((v.checks || []).join(', ')) + '">' +
                '<i class="mdi mdi-check-decagram"></i> ' + esc(label) + '</span>';
        }
        const n = (v.violations || []).length;
        return '<span class="badge dm-badge-unverified" title="' + esc((v.violations || []).join('\n')) + '">' +
            '<i class="mdi mdi-alert"></i> unverified — ' + n + ' violation' + (n === 1 ? '' : 's') + '</span>';
    }

    function modelBadge(d) {
        if (!d.model && !d.provider) return '';
        const label = esc(d.provider ? d.provider + ' / ' + d.model : d.model);
        const over = d.wasOverride ? ' <i class="mdi mdi-swap-horizontal" title="model override applied"></i>' : '';
        let tokens = '';
        if (d.inputTokens || d.outputTokens) {
            tokens = ' <span class="badge dm-badge-model" title="Tokens this turn, across every paid call it took (verification retries included)">' +
                esc(String(d.inputTokens || 0)) + ' in / ' + esc(String(d.outputTokens || 0)) + ' out</span>';
        }
        return '<span class="badge dm-badge-model" title="The exact model that produced this answer, as reported by the server">' +
            label + over + '</span>' + tokens;
    }

    function citationsHtml(hits) {
        if (!hits || !hits.length) return '';
        const items = hits.slice(0, 8).map(function (h) {
            const badge = h.collection
                ? '<span class="badge dm-badge-collection me-1">' + esc(h.collection) + '</span>'
                : '';
            const snippet = h.snippet ? '<span class="dm-snippet">' + esc(h.snippet) + '</span>' : '';
            return '<div class="dm-citation">' + badge +
                '<span class="dm-doc-id">' + esc(h.docId) + '</span>' + snippet + '</div>';
        }).join('');
        return '<div class="dm-citations"><div class="f-13 text-muted mb-1">Sources served this turn:</div>' + items + '</div>';
    }

    function setThinking(on) {
        const el = panel.querySelector('[data-dm-thinking]');
        if (el) el.classList.toggle('d-none', !on);
        const btn = panel.querySelector('[data-dm-send]');
        if (btn) btn.disabled = on;
        const followUp = panel.querySelector('[data-dm-follow-up]');
        if (followUp) followUp.disabled = on || state.history.length === 0;
        state.busy = on;
        if (on) {
            const log = panel.querySelector('[data-dm-thinking-log]');
            if (log) log.innerHTML = '';
            thinkingProgress.searched = 0;
            thinkingProgress.hits = 0;
            thinkingProgress.probed = 0;
            thinkingProgress.wasOverride = false;
        }
    }

    // ---- live turn progress (SSE) ------------------------------------------

    const thinkingProgress = { searched: 0, hits: 0, probed: 0 };
    let stepsId = 0;

    function archiveProgress(bubble) {
        const log = panel.querySelector('[data-dm-thinking-log]');
        if (!log || !log.childElementCount) return;
        const section = document.createElement('div');
        section.className = 'dm-turn-steps';
        const button = document.createElement('button');
        button.type = 'button';
        button.className = 'dm-steps-toggle';
        button.textContent = 'Show server steps';
        button.setAttribute('aria-expanded', 'false');
        const details = document.createElement('div');
        details.id = 'server-steps-' + (++stepsId);
        details.className = 'dm-steps-details';
        details.hidden = true;
        button.setAttribute('aria-controls', details.id);
        // Move this turn's final progress lines out of the live region. Later
        // turns reset only the live log, leaving this static record intact.
        while (log.firstChild) details.appendChild(log.firstChild);
        button.addEventListener('click', function () {
            details.hidden = !details.hidden;
            button.setAttribute('aria-expanded', String(!details.hidden));
            button.textContent = details.hidden ? 'Show server steps' : 'Hide server steps';
        });
        section.append(button, details);
        bubble.insertBefore(section, bubble.querySelector('.dm-citations'));
    }

    function progressLine(key, html) {
        const log = panel.querySelector('[data-dm-thinking-log]');
        if (!log) return;
        let line = log.querySelector('[data-dm-progress="' + key + '"]');
        if (!line) {
            line = document.createElement('div');
            line.setAttribute('data-dm-progress', key);
            log.appendChild(line);
        }
        line.innerHTML = html;
    }

    function renderProgress(ev) {
        // ev is the server's TurnProgressEvent, snake_case, tagged by `stage`.
        switch (ev.stage) {
            case 'probe':
                // One per permitted collection, as each probe completes; the
                // selection line below replaces the running count.
                thinkingProgress.probed = (thinkingProgress.probed || 0) + 1;
                progressLine('selection', '<i class="mdi mdi-radar me-1"></i>Probing collections… ' +
                    thinkingProgress.probed + ' probed');
                break;
            case 'selection':
                // Evidence-driven collection selection (runbook collectionSelection):
                // every permitted collection was probed; the retrieval lines that
                // follow count only the selected ones.
                progressLine('selection', '<i class="mdi mdi-filter-variant me-1"></i>Probed ' +
                    (ev.probed || 0) + ' collection' + (ev.probed === 1 ? '' : 's') +
                    ' — selected ' + (ev.selected || 0) + ' by evidence');
                break;
            case 'expansion':
                // The server reports the actual model used to widen the query.
                progressLine('expansion', '<i class="mdi mdi-text-search me-1"></i>Search preparation via <strong>' +
                    esc(ev.provider + ' / ' + ev.model) + '</strong> — ' +
                    ((ev.terms || []).length) + ' term' + ((ev.terms || []).length === 1 ? '' : 's') +
                    ((ev.terms || []).length ? ': ' + esc(ev.terms.slice(0, 8).join(', ')) + ((ev.terms.length > 8) ? ', …' : '') : '') +
                    ' (' + (ev.input_tokens || 0) + ' in / ' + (ev.output_tokens || 0) + ' out tokens)');
                break;
            case 'retrieval':
                thinkingProgress.searched += 1;
                if (!ev.skipped) thinkingProgress.hits += (ev.hits || 0);
                progressLine('retrieval', '<i class="mdi mdi-magnify me-1"></i>Searched ' +
                    thinkingProgress.searched + ' collection' + (thinkingProgress.searched === 1 ? '' : 's') +
                    ' — ' + thinkingProgress.hits + ' candidate passages');
                break;
            case 'merge':
                progressLine('merge', '<i class="mdi mdi-call-merge me-1"></i>Merged to ' +
                    (ev.hits || 0) + ' passages for the model');
                break;
            case 'model':
                thinkingProgress.wasOverride = ev.was_override;
                progressLine('model', '<i class="mdi mdi-chip me-1"></i>Answer model resolved: <strong>' +
                    esc(ev.provider + (ev.model ? ' / ' + ev.model : (ev.tier ? ' (' + ev.tier + ' tier)' : ''))) +
                    '</strong>' + (ev.was_override ? ' (your selection)' : ''));
                break;
            case 'completion':
                if (ev.attempt === 0) {
                    // Tier resolution initially names a ProviderConfig. Replace
                    // that alias with the concrete model returned by the call.
                    progressLine('model', '<i class="mdi mdi-chip me-1"></i>Answer model resolved: <strong>' +
                        esc(ev.provider + ' / ' + ev.model) + '</strong>' +
                        (thinkingProgress.wasOverride ? ' (your selection)' : ''));
                    progressLine('completion', '<i class="mdi mdi-text-box-check-outline me-1"></i>Answer from <strong>' +
                        esc(ev.provider + ' / ' + ev.model) + '</strong> — ' +
                        (ev.input_tokens || 0) + ' in / ' + (ev.output_tokens || 0) + ' out tokens');
                } else {
                    progressLine('retry' + ev.attempt, '<i class="mdi mdi-restart me-1"></i>Corrective retry #' +
                        ev.attempt + ' — ' + (ev.input_tokens || 0) + ' in / ' + (ev.output_tokens || 0) + ' out tokens');
                }
                break;
            case 'verify':
                if (ev.violations === 0) {
                    progressLine('verify', '<i class="mdi mdi-check-decagram me-1"></i>Verified (' +
                        esc((ev.checks || []).join(', ')) + ')');
                } else {
                    progressLine('verify', '<i class="mdi mdi-alert-outline me-1"></i>Verification found ' +
                        ev.violations + ' violation' + (ev.violations === 1 ? '' : 's') +
                        (ev.attempt === 0 ? ' — repairing…' : ' after retry #' + ev.attempt));
                }
                break;
        }
    }

    // POST + read an SSE response. Calls onEvent(name, parsedData) per frame.
    // Resolves {done: data} | {error: data} | {fallback: {ok,status,data}}.
    async function streamPost(url, body, onEvent) {
        let resp;
        try {
            resp = await fetch(url, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json', 'Accept': 'text/event-stream' },
                body: JSON.stringify(body || {}),
            });
        } catch (e) {
            return { fallback: { ok: false, status: 0, data: { error: 'network', message: 'Network error — check your connection and try again.' } } };
        }
        const ctype = resp.headers.get('content-type') || '';
        if (!resp.ok || ctype.indexOf('text/event-stream') === -1 || !resp.body) {
            let data = null;
            try { data = await resp.json(); } catch (e) { /* non-JSON */ }
            return { fallback: { ok: resp.ok, status: resp.status, data: data || {} } };
        }
        const reader = resp.body.getReader();
        const decoder = new TextDecoder();
        let buf = '';
        let outcome = null;
        function handleFrame(frame) {
            let name = 'message';
            const dataLines = [];
            frame.split(/\r?\n/).forEach(function (line) {
                if (line.indexOf('event:') === 0) name = line.slice(6).trim();
                else if (line.indexOf('data:') === 0) dataLines.push(line.slice(5).replace(/^ /, ''));
            });
            if (!dataLines.length) return;
            let parsed = null;
            try { parsed = JSON.parse(dataLines.join('\n')); } catch (e) { return; }
            if (name === 'done') outcome = { done: parsed };
            else if (name === 'error') outcome = { error: parsed };
            else onEvent(name, parsed);
        }
        for (;;) {
            let chunk;
            try { chunk = await reader.read(); } catch (e) { break; }
            if (chunk.done) break;
            buf += decoder.decode(chunk.value, { stream: true });
            let idx;
            while ((idx = buf.indexOf('\n\n')) !== -1) {
                handleFrame(buf.slice(0, idx));
                buf = buf.slice(idx + 2);
            }
        }
        if (!outcome) {
            outcome = { fallback: { ok: false, status: 0, data: { error: 'stream', message: 'The answer stream ended unexpectedly. Try again.' } } };
        }
        return outcome;
    }

    function updateRemaining(n) {
        const el = panel.querySelector('[data-dm-remaining]');
        if (el && typeof n === 'number') el.textContent = n + ' demo turns left today';
    }

    function renderAnswer(d, message) {
        state.sessionId = d.sessionId || state.sessionId;
        state.history.push({ role: 'user', text: message });
        state.history.push({ role: 'assistant', text: d.answer || '' });
        if (state.history.length > 2) state.history = state.history.slice(-2);
        const followUp = panel.querySelector('[data-dm-follow-up]');
        if (followUp) followUp.disabled = false;
        updateRemaining(d.turnsRemaining);

        const meta = '<div class="dm-answer-meta">' + modelBadge(d) + verificationBadge(d.verification) + '</div>';
        // alignTop: start the reader at the first line of the answer.
        const bubble = addBubble('dm-bubble-assistant', esc(d.answer) + meta + citationsHtml(d.hits), true);
        archiveProgress(bubble);
    }

    async function sendMessage(message) {
        if (!panel || state.busy) return;
        message = (message || '').trim();
        if (!message) return;
        if (!state.family) {
            addBubble('dm-bubble-error', 'No model provider is available. Try again after the operator restores one.');
            return;
        }

        const followUp = panel.querySelector('[data-dm-follow-up]');
        const useHistory = followUp?.checked === true && state.history.length > 0;
        if (followUp) followUp.checked = false;
        // A new subject starts a new context, while the visible transcript stays.
        if (!useHistory) state.history = [];

        addBubble('dm-bubble-user', esc(message));
        setThinking(true);

        const body = {
            sessionId: state.sessionId,
            message: message,
            history: state.history,
            followUp: useHistory,
            family: state.family,
            tier: state.tier,
            persona: state.persona,
        };

        // Streaming first: live phase progress while the server retrieves,
        // completes and verifies. Falls back to the unary endpoint when the
        // stream is unavailable.
        const streamed = await streamPost('/api/chat/' + state.corpus + '/stream', body, function (name, ev) {
            if (name === 'progress') renderProgress(ev);
        });

        if (streamed.done) {
            setThinking(false);
            renderAnswer(streamed.done, message);
            return;
        }
        if (streamed.error) {
            setThinking(false);
            const bubble = addBubble('dm-bubble-error', '<i class="mdi mdi-sleep me-1"></i>' +
                esc(friendlyError({ status: 0, data: streamed.error })));
            archiveProgress(bubble);
            return;
        }

        // Unary fallback ONLY when the stream route itself is absent (an older
        // deployment) — any other failure is surfaced as-is, never re-posted:
        // the stream endpoint already consumed a demo turn, and a blind retry
        // would consume a second one.
        const fb = streamed.fallback || { ok: false, status: 0, data: {} };
        const result = (fb.status === 404 || fb.status === 405)
            ? await postJson('/api/chat/' + state.corpus, body)
            : fb;

        setThinking(false);

        if (!result.ok) {
            const bubble = addBubble('dm-bubble-error', '<i class="mdi mdi-sleep me-1"></i>' + esc(friendlyError(result)));
            archiveProgress(bubble);
            return;
        }
        renderAnswer(result.data, message);
    }

    // ---- model disclosure (which concrete model each choice resolves to) --

    state.modelCatalog = null;
    let catalogLoading = false;
    let ollamaExpiryTimer;

    function applyOllamaAvailability(family) {
        const expires = Date.parse(family && family.expiresAt);
        const available = !!family && Number.isFinite(expires) && expires > Date.now();
        const button = panel.querySelector('[data-dm-family="ollama"]');
        button.hidden = !available;
        button.disabled = !available;
        button.classList.toggle('d-none', !available);
        clearTimeout(ollamaExpiryTimer);
        if (available) ollamaExpiryTimer = setTimeout(function () { applyOllamaAvailability(null); }, Math.min(expires - Date.now(), 10800000));
        if (!available && state.family === 'ollama') {
            state.family = ['claude', 'gpt', 'openrouter'].find(function (name) {
                return state.modelCatalog && state.modelCatalog[name];
            }) || '';
            panel.querySelectorAll('[data-dm-family]').forEach(function (b) {
                b.classList.toggle('active', b.getAttribute('data-dm-family') === state.family);
            });
            addBubble('dm-bubble-error', state.family
                ? 'Ollama is no longer available. Another configured provider is selected.'
                : 'No model provider is available. Try again after the operator restores one.');
        }
        updateTierAvailability();
        updateModelNote();
    }

    function updateTierAvailability() {
        const frontier = panel.querySelector('[data-dm-tier="frontier"]');
        frontier.disabled = state.family === 'ollama';
        if (frontier.disabled && state.tier === 'frontier') {
            state.tier = 'fast';
            panel.querySelectorAll('[data-dm-tier]').forEach(function (b) {
                b.classList.toggle('active', b.getAttribute('data-dm-tier') === 'fast');
            });
        }
    }

    function updateModelNote() {
        const note = panel && panel.querySelector('[data-dm-model-note]');
        if (!note) return;
        const fam = state.modelCatalog && state.modelCatalog[state.family];
        if (!fam) { note.textContent = ''; return; }
        // Lookup by tier name; the server omits `frontier` for visitors whose
        // access code does not unlock it, so a missing entry blanks the note
        // rather than promising a model the turn would refuse.
        const model = fam[state.tier] || null;
        if (!model) { note.textContent = ''; return; }
        note.innerHTML = '<i class="mdi mdi-information-outline me-1"></i>Answer model: <strong>' +
            esc(fam.provider + ' / ' + model) + '</strong>';
    }

    async function loadModelCatalog() {
        if (!panel || catalogLoading) return;
        catalogLoading = true;
        let data;
        try {
            const resp = await fetch('/api/models', { cache: 'no-store', signal: AbortSignal.timeout(6000) });
            if (!resp.ok) throw new Error('Model catalog unavailable');
            data = await resp.json();
        } catch (e) {
            state.modelCatalog = null;
            applyOllamaAvailability(null);
            state.family = '';
            panel.querySelectorAll('[data-dm-family]').forEach(function (button) {
                button.hidden = true;
                button.disabled = true;
                button.classList.add('d-none');
                button.classList.remove('active');
            });
            return;
        } finally { catalogLoading = false; }
        state.modelCatalog = (data && data.families) || null;
        applyOllamaAvailability(state.modelCatalog && state.modelCatalog.ollama);
        // Full disclosure on the buttons themselves too.
        panel.querySelectorAll('[data-dm-family]').forEach(function (btn) {
            const fam = state.modelCatalog && state.modelCatalog[btn.getAttribute('data-dm-family')];
            if (btn.getAttribute('data-dm-family') !== 'ollama') {
                btn.hidden = !fam;
                btn.disabled = !fam;
                btn.classList.toggle('d-none', !fam);
            }
            if (fam) {
                btn.title = 'fast: ' + (fam.fast || '?') + ' · capable: ' + (fam.capable || '?') +
                    (fam.frontier ? ' · frontier: ' + fam.frontier : '');
            }
        });
        const available = Array.from(panel.querySelectorAll('[data-dm-family]')).filter(function (b) { return !b.disabled; });
        if (!available.some(function (b) { return b.getAttribute('data-dm-family') === state.family; })) {
            state.family = available.length ? available[0].getAttribute('data-dm-family') : '';
            panel.querySelectorAll('[data-dm-family]').forEach(function (b) {
                b.classList.toggle('active', b.getAttribute('data-dm-family') === state.family);
            });
        }
        updateTierAvailability();
        updateModelNote();
    }

    if (panel) {
        // family / tier toggles
        panel.querySelectorAll('[data-dm-family]').forEach(function (btn) {
            btn.addEventListener('click', function () {
                panel.querySelectorAll('[data-dm-family]').forEach(function (b) { b.classList.remove('active'); });
                btn.classList.add('active');
                state.family = btn.getAttribute('data-dm-family');
                updateTierAvailability();
                updateModelNote();
            });
        });
        panel.querySelectorAll('[data-dm-tier]').forEach(function (btn) {
            btn.addEventListener('click', function () {
                panel.querySelectorAll('[data-dm-tier]').forEach(function (b) { b.classList.remove('active'); });
                btn.classList.add('active');
                state.tier = btn.getAttribute('data-dm-tier');
                updateModelNote();
            });
        });
        loadModelCatalog();
        setInterval(function () { if (!document.hidden) loadModelCatalog(); }, 15000);
        document.addEventListener('visibilitychange', function () {
            if (!document.hidden) loadModelCatalog();
        });

        // send: button + Enter
        const form = panel.querySelector('[data-dm-form]');
        const input = panel.querySelector('[data-dm-input]');
        form.addEventListener('submit', function (ev) {
            ev.preventDefault();
            const msg = input.value;
            input.value = '';
            sendMessage(msg);
        });

        // example chips prefill the chat box
        document.querySelectorAll('[data-dm-chip]:not([data-dm-gallery-q])').forEach(function (chip) {
            chip.addEventListener('click', function () {
                input.value = (chip.getAttribute('data-dm-question') || chip.textContent).trim();
                const followUp = panel.querySelector('[data-dm-follow-up]');
                if (followUp) followUp.checked = false;
                input.focus();
                // Bring the chat tab forward if a different tab is open.
                const chatTabBtn = document.querySelector('[data-bs-target="#tab-chat"]');
                if (chatTabBtn && window.bootstrap) {
                    window.bootstrap.Tab.getOrCreateInstance(chatTabBtn).show();
                }
            });
        });

        // persona switch: new session, cleared transcript, a note in between
        personaButtons.forEach(function (btn) {
            btn.addEventListener('click', async function () {
                const persona = btn.getAttribute('data-dm-persona');
                if (persona === state.persona || state.busy) return;
                personaButtons.forEach(function (b) { b.classList.remove('active'); });
                btn.classList.add('active');
                state.persona = persona;
                updatePersonaScope();
                state.sessionId = null;
                state.history = [];
                const followUp = panel.querySelector('[data-dm-follow-up]');
                if (followUp) { followUp.checked = false; followUp.disabled = true; }
                transcriptEl().innerHTML = '';
                addBubble('dm-bubble-note',
                    '<i class="mdi mdi-shield-account me-1"></i>Switched persona to <strong>' + esc(persona) +
                    '</strong> — new session with the new clearance, transcript cleared.');
                const result = await postJson('/api/session/' + state.corpus, { persona: persona });
                if (result.ok) {
                    state.sessionId = result.data.sessionId;
                    const cols = result.data.permittedCollections || [];
                    if (cols.length) {
                        addBubble('dm-bubble-note', 'This clearance can see: ' +
                            esc(cols.join(', ')));
                    }
                } else {
                    addBubble('dm-bubble-error', esc(friendlyError(result)));
                }
            });
        });
    }

    // ---- search tab --------------------------------------------------------

    document.querySelectorAll('[data-dm-search-form]').forEach(function (form) {
        const corpus = form.getAttribute('data-corpus');
        const usePersona = form.getAttribute('data-use-persona') === 'true';
        const input = form.querySelector('[data-dm-search-input]');
        const out = form.parentElement.querySelector('[data-dm-search-results]');

        form.addEventListener('submit', async function (ev) {
            ev.preventDefault();
            const query = (input.value || '').trim();
            if (!query) return;
            out.innerHTML = '<div class="f-14 text-muted"><span class="spinner-border spinner-border-sm me-2"></span>Searching…</div>';

            const body = { query: query };
            if (usePersona) body.persona = state.persona;
            const result = await postJson('/api/search/' + corpus, body);

            if (!result.ok) {
                out.innerHTML = '<div class="alert alert-warning f-14">' + esc(friendlyError(result)) + '</div>';
                return;
            }
            const hits = result.data.hits || [];
            if (!hits.length) {
                out.innerHTML = '<div class="f-14 text-muted">No hits.</div>';
                return;
            }
            out.innerHTML = hits.map(function (h) {
                // The server names the collection each hit came from; fall
                // back to the path segment only if it ever omits it.
                const label = h.collection || collectionBadgeFromPath(h.docId);
                const badge = '<span class="badge dm-badge-collection me-1">' + esc(label) + '</span>';
                return '<div class="dm-hit">' + badge +
                    '<span class="dm-doc-id">' + esc(h.docId) + '</span>' +
                    ' <span class="dm-score">score ' + esc(h.score) + '</span>' +
                    '<div class="dm-snippet mt-1">' + esc(h.snippet) + '</div></div>';
            }).join('');
        });
    });

    // ---- curated Q&A gallery (support) ------------------------------------

    const gallery = document.querySelector('[data-dm-gallery]');
    if (gallery) {
        const corpus = gallery.getAttribute('data-corpus');
        const out = document.querySelector('[data-dm-gallery-out]');
        let busy = false;

        gallery.querySelectorAll('[data-dm-gallery-q]').forEach(function (chip) {
            chip.addEventListener('click', async function () {
                if (busy) return;
                busy = true;
                const question = chip.textContent.trim();
                const item = document.createElement('div');
                item.className = 'dm-gallery-item';
                item.innerHTML = '<div class="dm-gallery-q">' + esc(question) + '</div>' +
                    '<div class="f-14 text-muted"><span class="spinner-border spinner-border-sm me-2"></span>Running a one-turn session…</div>';
                out.prepend(item);

                // Fresh session per gallery question — one turn, no history.
                const session = await postJson('/api/session/' + corpus, {});
                if (!session.ok) {
                    item.querySelector('.text-muted').outerHTML =
                        '<div class="alert alert-warning f-14 mb-0">' + esc(friendlyError(session)) + '</div>';
                    busy = false;
                    return;
                }
                const result = await postJson('/api/chat/' + corpus, {
                    sessionId: session.data.sessionId,
                    message: question,
                    history: [],
                    family: state.family || 'claude',
                    tier: state.tier || 'fast',
                });
                if (!result.ok) {
                    item.querySelector('.text-muted').outerHTML =
                        '<div class="alert alert-warning f-14 mb-0">' + esc(friendlyError(result)) + '</div>';
                    busy = false;
                    return;
                }
                const d = result.data;
                item.querySelector('.text-muted').outerHTML =
                    '<div class="dm-gallery-a">' + esc(d.answer) + '</div>' +
                    '<div class="dm-answer-meta">' + modelBadge(d) + verificationBadge(d.verification) + '</div>' +
                    citationsHtml(d.hits);
                busy = false;
            });
        });
    }
})();
