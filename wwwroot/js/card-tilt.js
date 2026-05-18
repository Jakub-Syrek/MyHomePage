/* eslint-disable */
// card-tilt.js — 3D tilt + glare for category cards.
//
// Each .category-card listens for pointer movement inside its own
// bounding box. The cursor position (normalised to [-1, 1] on both
// axes) drives a perspective rotation up to ±10° and an offset glare
// gradient that tracks the cursor. The icon parallaxes a few pixels
// in the opposite direction of the tilt so it reads as "in front" of
// the card. All transforms ease back to neutral on pointerleave.
//
// Bails on touch / pointer:coarse devices — tilting requires hover,
// and a touch tap shouldn't kick off a "stuck" tilted card.
(function () {
    'use strict';

    function init() {
        // Skip on devices without a fine pointer (touch / coarse).
        if (window.matchMedia('(hover: none), (pointer: coarse)').matches) return;
        if (window.matchMedia('(prefers-reduced-motion: reduce)').matches) return;

        const cards = document.querySelectorAll('.category-card');
        if (!cards.length) return;

        const MAX_TILT = 10;       // degrees
        const ICON_PARALLAX = 8;   // px

        cards.forEach(setupCard);

        function setupCard(card) {
            // Establish a perspective context — without this the
            // rotateX/Y values do nothing visible. Set on the card
            // itself rather than the parent so we don't have to
            // touch existing layout.
            card.style.transformStyle = 'preserve-3d';
            card.style.willChange = 'transform';

            // Inject a glare layer absolutely positioned over the
            // card. `pointer-events: none` so clicks still hit the
            // <a>. The radial gradient + mix-blend keeps it visible
            // on both the bright bg-rgba(255,255,255,0.32) card and
            // its hover state without ever flashing pure white.
            const glare = document.createElement('span');
            glare.setAttribute('aria-hidden', 'true');
            glare.style.cssText = [
                'position: absolute',
                'inset: 0',
                'border-radius: inherit',
                'pointer-events: none',
                'opacity: 0',
                'transition: opacity 0.25s ease',
                'background: radial-gradient(circle at var(--gx, 50%) var(--gy, 50%), rgba(255,255,255,0.55) 0%, rgba(255,255,255,0.18) 28%, transparent 60%)',
                'mix-blend-mode: screen',
            ].join(';');
            // `position: relative` is the cheapest way to make the
            // absolute glare snap to the card bounds; preserve any
            // existing positioning the card already has.
            const computed = getComputedStyle(card);
            if (computed.position === 'static') card.style.position = 'relative';
            card.appendChild(glare);

            // The icon already sits inside the card — give it its
            // own transform so we can parallax it without fighting
            // the card's transform.
            const icon = card.querySelector('.category-card-icon');
            if (icon) icon.style.willChange = 'transform';

            card.addEventListener('pointermove', onMove);
            card.addEventListener('pointerleave', onLeave);

            function onMove(e) {
                const rect = card.getBoundingClientRect();
                const px = (e.clientX - rect.left) / rect.width;   // 0..1
                const py = (e.clientY - rect.top) / rect.height;
                // Map 0..1 → -1..1 so the centre of the card is the
                // pivot and the corners hit max tilt.
                const nx = px * 2 - 1;
                const ny = py * 2 - 1;
                // Negate Y so cursor-top tilts the card AWAY from
                // the viewer at the top edge (matches physical
                // intuition for tilting a piece of paper).
                const tiltX = -ny * MAX_TILT;
                const tiltY = nx * MAX_TILT;

                card.style.transform =
                    `perspective(900px) rotateX(${tiltX.toFixed(2)}deg) rotateY(${tiltY.toFixed(2)}deg) translateY(-3px)`;

                // Glare follows the cursor exactly; offset percent
                // is a CSS custom prop so the gradient re-centres
                // without rebuilding the gradient string each frame.
                glare.style.setProperty('--gx', (px * 100).toFixed(1) + '%');
                glare.style.setProperty('--gy', (py * 100).toFixed(1) + '%');
                glare.style.opacity = '1';

                if (icon) {
                    icon.style.transform =
                        `translate3d(${(nx * -ICON_PARALLAX).toFixed(1)}px, ${(ny * -ICON_PARALLAX).toFixed(1)}px, 0) scale(1.06)`;
                }
            }

            function onLeave() {
                // Let the CSS hover-transition (transform 0.3s ease)
                // do the heavy lifting — just clear the inline style
                // so the card eases back to the stylesheet's resting
                // state.
                card.style.transform = '';
                glare.style.opacity = '0';
                if (icon) icon.style.transform = '';
            }
        }
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init, { once: true });
    } else {
        init();
    }
})();
