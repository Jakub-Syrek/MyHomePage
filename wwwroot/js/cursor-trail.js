/* eslint-disable */
// cursor-trail.js — custom glowing cursor with a fading particle
// trail and magnetic attraction to category cards.
//
// The native cursor is hidden over the home-hero block (and over
// cards inside it) and replaced with a soft luminous dot on a
// fullscreen canvas. Each pointer move spawns a particle that fades
// over ~400 ms, producing a comet-tail effect. When the cursor gets
// within MAGNET_RADIUS px of a category-card centre the dot is
// pulled toward that centre — a tactile "magnetic click" feel.
//
// Skips on touch / coarse-pointer devices (no cursor to replace)
// and on prefers-reduced-motion.
(function () {
    'use strict';

    function init() {
        if (window.matchMedia('(hover: none), (pointer: coarse)').matches) return;
        if (window.matchMedia('(prefers-reduced-motion: reduce)').matches) return;

        const hero = document.querySelector('.home-hero');
        if (!hero) return;

        const canvas = document.createElement('canvas');
        canvas.setAttribute('aria-hidden', 'true');
        canvas.style.cssText = [
            'position: fixed',
            'inset: 0',
            'width: 100vw',
            'height: 100vh',
            'pointer-events: none',
            'z-index: 9999',
        ].join(';');
        document.body.appendChild(canvas);

        const ctx = canvas.getContext('2d');
        if (!ctx) { canvas.remove(); return; }

        const dpr = Math.min(window.devicePixelRatio || 1, 2);

        // Hide the OS cursor while inside the hero. Cards inherit
        // the rule via CSS so the cursor stays hidden even when
        // hovering an <a>.
        const styleEl = document.createElement('style');
        styleEl.textContent =
            '.home-hero, .home-hero * { cursor: none !important; }';
        document.head.appendChild(styleEl);

        const MAGNET_RADIUS = 90;       // px from a card centre to start pulling
        const MAGNET_STRENGTH = 0.45;   // 0 = none, 1 = snap-to-centre
        const TRAIL_LIFE = 0.42;        // seconds
        const MAX_PARTICLES = 90;

        let width = 0, height = 0;
        let mouseX = -9999, mouseY = -9999;
        // The rendered cursor position eases toward (mouseX,mouseY)
        // unless magnetic pull overrides it. Drawn separately from
        // particles so the dot stays sharp.
        let renderX = -9999, renderY = -9999;
        let active = false;
        let particles = [];

        function resize() {
            width = window.innerWidth;
            height = window.innerHeight;
            canvas.width = Math.round(width * dpr);
            canvas.height = Math.round(height * dpr);
            ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
        }
        resize();
        window.addEventListener('resize', resize, { passive: true });

        function nearestMagnetTarget(x, y) {
            // Snap the cursor toward the nearest visible card if
            // we're inside its magnet radius. Returns the centre of
            // the strongest attractor, or null when none is in
            // range. Pulled fresh each frame so layout / scroll
            // changes are picked up automatically.
            const cards = document.querySelectorAll('.category-card');
            let bestDist = MAGNET_RADIUS;
            let bestX = null, bestY = null;
            cards.forEach(card => {
                const r = card.getBoundingClientRect();
                const cx = r.left + r.width / 2;
                const cy = r.top + r.height / 2;
                const dx = cx - x, dy = cy - y;
                const d = Math.sqrt(dx * dx + dy * dy);
                // Also require the cursor to be inside the card
                // half-extents so the magnet doesn't kick in from
                // way above or below a tile.
                if (d < bestDist &&
                    x > r.left - MAGNET_RADIUS && x < r.right + MAGNET_RADIUS &&
                    y > r.top - MAGNET_RADIUS && y < r.bottom + MAGNET_RADIUS) {
                    bestDist = d;
                    bestX = cx; bestY = cy;
                }
            });
            return bestX === null ? null : { x: bestX, y: bestY, dist: bestDist };
        }

        function onMove(e) {
            mouseX = e.clientX;
            mouseY = e.clientY;
            if (renderX < -1000) { renderX = mouseX; renderY = mouseY; }

            // Only activate the custom cursor when the pointer is
            // over the hero — outside it, let the page have its
            // normal cursor back.
            const heroRect = hero.getBoundingClientRect();
            active =
                mouseX >= heroRect.left && mouseX <= heroRect.right &&
                mouseY >= heroRect.top && mouseY <= heroRect.bottom;
            styleEl.disabled = !active;
        }
        window.addEventListener('pointermove', onMove, { passive: true });
        window.addEventListener('blur', () => { active = false; styleEl.disabled = true; });

        let lastTime = performance.now();
        let raf = 0;

        function frame(now) {
            const dt = Math.min(0.05, (now - lastTime) / 1000);
            lastTime = now;

            ctx.clearRect(0, 0, width, height);

            if (active) {
                // Magnetic snap — bias the eased target toward the
                // nearest card centre, weighted by how close we
                // already are (closer = stronger pull).
                let targetX = mouseX, targetY = mouseY;
                const magnet = nearestMagnetTarget(mouseX, mouseY);
                if (magnet) {
                    const t = (1 - magnet.dist / MAGNET_RADIUS) * MAGNET_STRENGTH;
                    targetX = mouseX * (1 - t) + magnet.x * t;
                    targetY = mouseY * (1 - t) + magnet.y * t;
                }

                renderX += (targetX - renderX) * 0.35;
                renderY += (targetY - renderY) * 0.35;

                // Spawn a fresh particle every frame so the trail
                // density scales with mouse speed automatically —
                // fast moves drop fewer particles per pixel travelled
                // but the speed itself produces the stretching.
                if (particles.length < MAX_PARTICLES) {
                    particles.push({
                        x: renderX, y: renderY,
                        born: now,
                        radius: 4 + Math.random() * 3,
                    });
                }
            }

            // Trail
            for (let i = particles.length - 1; i >= 0; i--) {
                const p = particles[i];
                const age = (now - p.born) / 1000;
                if (age > TRAIL_LIFE) { particles.splice(i, 1); continue; }
                const alpha = (1 - age / TRAIL_LIFE) * 0.55;
                const r = p.radius * (1 - age / TRAIL_LIFE);
                ctx.beginPath();
                ctx.fillStyle = `rgba(180, 215, 255, ${alpha.toFixed(3)})`;
                ctx.shadowColor = 'rgba(140, 195, 255, 0.65)';
                ctx.shadowBlur = 10;
                ctx.arc(p.x, p.y, r, 0, Math.PI * 2);
                ctx.fill();
            }
            ctx.shadowBlur = 0;

            if (active) {
                // Outer halo
                const halo = ctx.createRadialGradient(renderX, renderY, 0, renderX, renderY, 22);
                halo.addColorStop(0,   'rgba(220, 240, 255, 0.55)');
                halo.addColorStop(0.45,'rgba(140, 190, 255, 0.20)');
                halo.addColorStop(1,   'rgba(80, 130, 220, 0)');
                ctx.fillStyle = halo;
                ctx.beginPath();
                ctx.arc(renderX, renderY, 22, 0, Math.PI * 2);
                ctx.fill();

                // Bright core dot
                ctx.fillStyle = 'rgba(245, 250, 255, 0.95)';
                ctx.beginPath();
                ctx.arc(renderX, renderY, 4.5, 0, Math.PI * 2);
                ctx.fill();
            }

            raf = requestAnimationFrame(frame);
        }
        raf = requestAnimationFrame(frame);

        document.addEventListener('visibilitychange', () => {
            if (document.hidden) {
                if (raf) cancelAnimationFrame(raf);
            } else {
                lastTime = performance.now();
                raf = requestAnimationFrame(frame);
            }
        });
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init, { once: true });
    } else {
        init();
    }
})();
