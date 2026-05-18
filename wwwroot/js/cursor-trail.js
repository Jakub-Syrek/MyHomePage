/* eslint-disable */
// cursor-trail.js — custom glowing cursor + fading particle trail
// that activates whenever the pointer is over any element marked
// `[data-hero-effects]`. The trail canvas lives at body level
// (position: fixed, z-index: 9999) so it covers home and subview
// pages alike without per-page wiring. Magnetic snap targets any
// `.category-card` on screen — on subviews there are no such cards,
// so the cursor falls back to plain tracking.
(function () {
    'use strict';
    if (window.__cursorTrailInstalled) return;
    window.__cursorTrailInstalled = true;

    if (window.matchMedia('(hover: none), (pointer: coarse)').matches) return;
    if (window.matchMedia('(prefers-reduced-motion: reduce)').matches) return;

    function init() {
        const canvas = document.createElement('canvas');
        canvas.setAttribute('aria-hidden', 'true');
        canvas.style.cssText = [
            'position: fixed', 'inset: 0',
            'width: 100vw', 'height: 100vh',
            'pointer-events: none', 'z-index: 9999',
        ].join(';');
        document.body.appendChild(canvas);

        const ctx = canvas.getContext('2d');
        if (!ctx) { canvas.remove(); return; }

        const dpr = Math.min(window.devicePixelRatio || 1, 2);

        // Hide the OS cursor over any hero-effects region. The rule
        // is scoped to `[data-hero-effects]` and its descendants so
        // the rest of the page (header, footer, etc.) keeps the
        // native cursor.
        const styleEl = document.createElement('style');
        styleEl.textContent =
            '[data-hero-effects], [data-hero-effects] * { cursor: none !important; }';
        document.head.appendChild(styleEl);

        const MAGNET_RADIUS = 90;
        const MAGNET_STRENGTH = 0.45;
        const TRAIL_LIFE = 0.42;
        const MAX_PARTICLES = 90;

        let width = 0, height = 0;
        let mouseX = -9999, mouseY = -9999;
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

        function isOverHero(x, y) {
            // Walk every hero region — a page may end up with more
            // than one in flight during a Blazor transition.
            const heroes = document.querySelectorAll('[data-hero-effects]');
            for (let i = 0; i < heroes.length; i++) {
                const r = heroes[i].getBoundingClientRect();
                if (x >= r.left && x <= r.right && y >= r.top && y <= r.bottom)
                    return true;
            }
            return false;
        }

        function nearestMagnetTarget(x, y) {
            const cards = document.querySelectorAll('.category-card');
            let bestDist = MAGNET_RADIUS;
            let bestX = null, bestY = null;
            cards.forEach(card => {
                const r = card.getBoundingClientRect();
                const cx = r.left + r.width / 2;
                const cy = r.top + r.height / 2;
                const dx = cx - x, dy = cy - y;
                const d = Math.sqrt(dx * dx + dy * dy);
                if (d < bestDist &&
                    x > r.left - MAGNET_RADIUS && x < r.right + MAGNET_RADIUS &&
                    y > r.top - MAGNET_RADIUS && y < r.bottom + MAGNET_RADIUS) {
                    bestDist = d;
                    bestX = cx; bestY = cy;
                }
            });
            return bestX === null ? null : { x: bestX, y: bestY, dist: bestDist };
        }

        window.addEventListener('pointermove', e => {
            mouseX = e.clientX; mouseY = e.clientY;
            if (renderX < -1000) { renderX = mouseX; renderY = mouseY; }
            active = isOverHero(mouseX, mouseY);
            styleEl.disabled = !active;
        }, { passive: true });
        window.addEventListener('blur', () => {
            active = false; styleEl.disabled = true;
        });

        let lastTime = performance.now();
        let raf = 0;

        function frame(now) {
            const dt = Math.min(0.05, (now - lastTime) / 1000);
            lastTime = now;

            ctx.clearRect(0, 0, width, height);

            if (active) {
                let targetX = mouseX, targetY = mouseY;
                const magnet = nearestMagnetTarget(mouseX, mouseY);
                if (magnet) {
                    const t = (1 - magnet.dist / MAGNET_RADIUS) * MAGNET_STRENGTH;
                    targetX = mouseX * (1 - t) + magnet.x * t;
                    targetY = mouseY * (1 - t) + magnet.y * t;
                }
                renderX += (targetX - renderX) * 0.35;
                renderY += (targetY - renderY) * 0.35;

                if (particles.length < MAX_PARTICLES) {
                    particles.push({
                        x: renderX, y: renderY,
                        born: now,
                        radius: 4 + Math.random() * 3,
                    });
                }
            }

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
                const halo = ctx.createRadialGradient(renderX, renderY, 0, renderX, renderY, 22);
                halo.addColorStop(0,   'rgba(220, 240, 255, 0.55)');
                halo.addColorStop(0.45,'rgba(140, 190, 255, 0.20)');
                halo.addColorStop(1,   'rgba(80, 130, 220, 0)');
                ctx.fillStyle = halo;
                ctx.beginPath();
                ctx.arc(renderX, renderY, 22, 0, Math.PI * 2);
                ctx.fill();

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
                raf = 0;
            } else if (!raf) {
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
