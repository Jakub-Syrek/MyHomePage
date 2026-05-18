/* eslint-disable */
// card-tilt.js — 3D tilt + glare for cards that opt in via the
// `.category-card` class. Idempotent + DOM-mutation aware so Blazor
// SPA navigation between the home page and subviews picks up new
// card sets without re-loading the script.
(function () {
    'use strict';
    if (window.__cardTiltInstalled) return;
    window.__cardTiltInstalled = true;

    if (window.matchMedia('(hover: none), (pointer: coarse)').matches) return;
    if (window.matchMedia('(prefers-reduced-motion: reduce)').matches) return;

    const DEFAULT_MAX_TILT = 10;
    const ICON_PARALLAX = 8;
    const INIT_FLAG = '__cardTiltInit';
    // Anything matching this selector gets the tilt + glare. The
    // category cards on the home page have `.category-card`; the
    // header nav tiles use `[data-fx-tile]` so the same script
    // covers both without duplicating selectors. New tile-like UI
    // can opt in by adding `data-fx-tile`.
    const TILT_SELECTOR = '.category-card, [data-fx-tile]';

    function setup(card) {
        if (card[INIT_FLAG]) return;
        card[INIT_FLAG] = true;

        // Per-element tilt cap via `data-tilt-max`. Falls back to
        // DEFAULT_MAX_TILT — keeps the big home-page cards at the
        // dramatic ±10° while header tiles can dial down to ~6° so
        // a rapid swipe across nine of them doesn't feel jittery.
        const MAX_TILT =
            parseFloat(card.getAttribute('data-tilt-max')) || DEFAULT_MAX_TILT;

        card.style.transformStyle = 'preserve-3d';
        card.style.willChange = 'transform';

        const glare = document.createElement('span');
        glare.setAttribute('aria-hidden', 'true');
        glare.style.cssText = [
            'position: absolute', 'inset: 0', 'border-radius: inherit',
            'pointer-events: none', 'opacity: 0',
            'transition: opacity 0.25s ease',
            'background: radial-gradient(circle at var(--gx, 50%) var(--gy, 50%), rgba(255,255,255,0.55) 0%, rgba(255,255,255,0.18) 28%, transparent 60%)',
            'mix-blend-mode: screen',
        ].join(';');
        if (getComputedStyle(card).position === 'static') card.style.position = 'relative';
        card.appendChild(glare);

        const icon = card.querySelector('.category-card-icon');
        if (icon) icon.style.willChange = 'transform';

        card.addEventListener('pointermove', e => {
            const rect = card.getBoundingClientRect();
            const px = (e.clientX - rect.left) / rect.width;
            const py = (e.clientY - rect.top) / rect.height;
            const nx = px * 2 - 1, ny = py * 2 - 1;
            card.style.transform =
                `perspective(900px) rotateX(${(-ny * MAX_TILT).toFixed(2)}deg) rotateY(${(nx * MAX_TILT).toFixed(2)}deg) translateY(-3px)`;
            glare.style.setProperty('--gx', (px * 100).toFixed(1) + '%');
            glare.style.setProperty('--gy', (py * 100).toFixed(1) + '%');
            glare.style.opacity = '1';
            if (icon) {
                icon.style.transform =
                    `translate3d(${(nx * -ICON_PARALLAX).toFixed(1)}px, ${(ny * -ICON_PARALLAX).toFixed(1)}px, 0) scale(1.06)`;
            }
        });
        card.addEventListener('pointerleave', () => {
            card.style.transform = '';
            glare.style.opacity = '0';
            if (icon) icon.style.transform = '';
        });
    }

    function scan() {
        document.querySelectorAll(TILT_SELECTOR).forEach(setup);
    }
    function startup() {
        scan();
        new MutationObserver(scan).observe(document.body, {
            childList: true, subtree: true,
        });
    }
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', startup, { once: true });
    } else {
        startup();
    }
})();
