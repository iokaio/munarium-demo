// SPDX-License-Identifier: Apache-2.0
(function () {
    'use strict';
    const workspace = document.querySelector('.dm-workspace');
    if (!workspace) return;
    const input = workspace.querySelector('[data-dm-input]');
    const form = workspace.querySelector('[data-dm-form]');
    input.addEventListener('keydown', function (event) {
        if (event.key === 'Enter' && !event.shiftKey && !event.isComposing) {
            event.preventDefault();
            if (!workspace.querySelector('[data-dm-send]').disabled) form.requestSubmit();
        }
    });
    // Reflect changing visual viewport height when a phone keyboard opens.
    function resizeViewport() {
        document.documentElement.style.setProperty('--dm-viewport-height', (window.visualViewport?.height || window.innerHeight) + 'px');
    }
    resizeViewport();
    window.visualViewport?.addEventListener('resize', resizeViewport);
    window.addEventListener('resize', resizeViewport);

    const sidebar = document.getElementById('collection-sidebar');
    sidebar.querySelectorAll('[data-dm-chip]').forEach(function (chip) {
        chip.addEventListener('click', function () {
            if (window.matchMedia('(max-width: 991.98px)').matches) {
                const canvas = bootstrap.Offcanvas.getInstance(sidebar);
                if (canvas) {
                    sidebar.addEventListener('hidden.bs.offcanvas', function () { input.focus(); }, { once: true });
                    canvas.hide();
                }
            }
        });
    });
    // Model availability can change during polling as well as on a click.
    function syncPressed() {
        workspace.querySelectorAll('[data-dm-family], [data-dm-tier]').forEach(function (button) {
            const value = String(button.classList.contains('active'));
            if (button.getAttribute('aria-pressed') !== value) button.setAttribute('aria-pressed', value);
        });
    }
    new MutationObserver(syncPressed).observe(workspace.querySelector('.dm-model-controls'), { subtree: true, attributes: true, attributeFilter: ['class'] });
    syncPressed();
})();
