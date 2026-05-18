/* eslint-disable */
// snow-tunnel.js — perspective-projected snow tunnel.
//
// Auto-attaches to every element flagged `[data-hero-effects]` on the
// page, so the home hero AND every category subview (Running,
// Bicycle, Mountains, …) get the effect once they mount. A MutationObserver
// re-scans after every Blazor SPA navigation, and each hero element is
// marked once so we never double-initialise.
//
// Honours prefers-reduced-motion and stops its rAF loop when the host
// canvas is detached from the document (e.g. Blazor swapped the page).
(function () {
    'use strict';
    if (window.__snowTunnelInstalled) return;
    window.__snowTunnelInstalled = true;

    const INIT_FLAG = '__snowTunnelInit';

    function installOnHero(hero) {
        if (hero[INIT_FLAG]) return;
        hero[INIT_FLAG] = true;

        if (window.matchMedia('(prefers-reduced-motion: reduce)').matches) return;

        // Look for a pre-rendered canvas inside the hero. Both the
        // home page and CategoryGallery emit one with class
        // `snow-tunnel-canvas`; if absent we inject one ourselves so
        // older callers still get the effect.
        let canvas = hero.querySelector('canvas.snow-tunnel-canvas');
        if (!canvas) {
            canvas = document.createElement('canvas');
            canvas.className = 'snow-tunnel-canvas';
            canvas.setAttribute('aria-hidden', 'true');
            hero.insertBefore(canvas, hero.firstChild);
        }

        const ctx = canvas.getContext('2d', { alpha: true });
        if (!ctx) return;

        const dpr = Math.min(window.devicePixelRatio || 1, 2);

        let width = 0, height = 0;
        let centerX = 0, centerY = 0;
        let targetCenterX = 0, targetCenterY = 0;
        let cursorX = -9999, cursorY = -9999;
        let smoothCursorX = -9999, smoothCursorY = -9999;
        let cursorInside = false;
        let cursorIntensity = 0;

        const FOV = 280, FAR_Z = 1400, NEAR_Z = 8, SPEED = 220;

        let flakes = [];
        function flakeCount() {
            const area = width * height;
            return Math.min(650, Math.max(220, Math.round(area / 3600)));
        }

        function resize() {
            width = canvas.clientWidth;
            height = canvas.clientHeight;
            if (width === 0 || height === 0) return;
            canvas.width = Math.round(width * dpr);
            canvas.height = Math.round(height * dpr);
            ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
            centerX = width / 2; centerY = height / 2;
            targetCenterX = centerX; targetCenterY = centerY;

            const desired = flakeCount();
            while (flakes.length < desired) flakes.push(spawnFlake(true));
            if (flakes.length > desired) flakes.length = desired;
        }

        function spawnFlake(initial) {
            const screenX = (Math.random() - 0.5) * width * 1.4;
            const screenY = (Math.random() - 0.5) * height * 1.4;
            const z = initial
                ? NEAR_Z + Math.random() * (FAR_Z - NEAR_Z)
                : FAR_Z - Math.random() * 200;
            return {
                x: (screenX * z) / FOV,
                y: (screenY * z) / FOV,
                z: z,
                size: 1.6 + Math.random() * 2.4,
                rotation: Math.random() * Math.PI * 2,
                spin: (Math.random() - 0.5) * 1.4,
                hue: 200 + Math.random() * 40,
                wobble: Math.random() * Math.PI * 2,
                wobbleSpeed: 0.4 + Math.random() * 1.1,
                branchAlpha: 0.6 + Math.random() * 0.4,
            };
        }

        function drawFlake(f, dt) {
            f.z -= SPEED * dt;
            f.rotation += f.spin * dt;
            f.wobble += f.wobbleSpeed * dt;

            if (f.z < NEAR_Z) { Object.assign(f, spawnFlake(false)); return; }

            const wobbleX = Math.cos(f.wobble) * 8;
            const wobbleY = Math.sin(f.wobble * 0.7) * 6;
            const screenX = centerX + ((f.x + wobbleX) * FOV) / f.z;
            const screenY = centerY + ((f.y + wobbleY) * FOV) / f.z;
            if (screenX < -60 || screenX > width + 60 ||
                screenY < -60 || screenY > height + 60) return;

            const scale = FOV / f.z;
            const radius = f.size * scale;
            const fadeIn = Math.min(1, (FAR_Z - f.z) / 350);
            const fadeNear = Math.min(1, f.z / NEAR_Z);
            const alpha = Math.min(1, fadeIn * (0.55 + fadeNear * 0.45));

            ctx.save();
            ctx.translate(screenX, screenY);
            ctx.rotate(f.rotation);

            if (radius > 2) {
                ctx.shadowColor = `hsla(${f.hue}, 100%, 92%, ${alpha * 0.7})`;
                ctx.shadowBlur = Math.min(18, radius * 1.6);
            }
            ctx.fillStyle = `hsla(${f.hue}, 100%, 96%, ${alpha})`;
            ctx.beginPath();
            ctx.arc(0, 0, Math.max(0.4, radius * 0.45), 0, Math.PI * 2);
            ctx.fill();

            if (radius > 1.6) {
                ctx.shadowBlur = 0;
                ctx.strokeStyle = `hsla(${f.hue}, 100%, 95%, ${alpha * f.branchAlpha})`;
                ctx.lineWidth = Math.max(0.6, radius * 0.18);
                ctx.lineCap = 'round';

                for (let i = 0; i < 6; i++) {
                    const angle = (Math.PI / 3) * i;
                    const cos = Math.cos(angle);
                    const sin = Math.sin(angle);
                    const tip = radius * 1.6;
                    ctx.beginPath();
                    ctx.moveTo(0, 0);
                    ctx.lineTo(cos * tip, sin * tip);
                    ctx.stroke();

                    if (radius > 3) {
                        const mid = tip * 0.6;
                        const barb = tip * 0.32;
                        const ba = 0.6;
                        ctx.beginPath();
                        ctx.moveTo(cos * mid, sin * mid);
                        ctx.lineTo(
                            cos * mid + Math.cos(angle + ba) * barb,
                            sin * mid + Math.sin(angle + ba) * barb);
                        ctx.moveTo(cos * mid, sin * mid);
                        ctx.lineTo(
                            cos * mid + Math.cos(angle - ba) * barb,
                            sin * mid + Math.sin(angle - ba) * barb);
                        ctx.stroke();
                    }
                }
            }
            ctx.restore();
        }

        function drawCursorGlow() {
            if (cursorIntensity <= 0.01) return;
            const radius = Math.min(width, height) * 0.45;
            const grad = ctx.createRadialGradient(
                smoothCursorX, smoothCursorY, 0,
                smoothCursorX, smoothCursorY, radius);
            grad.addColorStop(0,   `rgba(230, 240, 255, ${0.55 * cursorIntensity})`);
            grad.addColorStop(0.35,`rgba(170, 205, 255, ${0.22 * cursorIntensity})`);
            grad.addColorStop(0.7, `rgba(120, 170, 240, ${0.08 * cursorIntensity})`);
            grad.addColorStop(1,   'rgba(0, 0, 0, 0)');
            ctx.save();
            ctx.globalCompositeOperation = 'screen';
            ctx.fillStyle = grad;
            ctx.fillRect(0, 0, width, height);
            ctx.restore();
        }

        function drawVignette() {
            const grad = ctx.createRadialGradient(
                centerX, centerY, 0,
                centerX, centerY, Math.max(width, height) * 0.7);
            grad.addColorStop(0, 'rgba(20, 32, 70, 0.10)');
            grad.addColorStop(0.55, 'rgba(15, 25, 60, 0.18)');
            grad.addColorStop(1, 'rgba(8, 15, 45, 0.34)');
            ctx.fillStyle = grad;
            ctx.fillRect(0, 0, width, height);
        }

        let last = performance.now();
        let raf = 0;

        function frame(now) {
            // Bail out if the host hero (or its canvas) has been
            // unmounted by Blazor — the rAF loop must die with it,
            // otherwise it leaks CPU on every page navigation.
            if (!canvas.isConnected) { raf = 0; return; }

            const dt = Math.min(0.05, (now - last) / 1000);
            last = now;

            centerX += (targetCenterX - centerX) * 0.06;
            centerY += (targetCenterY - centerY) * 0.06;

            if (smoothCursorX < -1000) { smoothCursorX = cursorX; smoothCursorY = cursorY; }
            smoothCursorX += (cursorX - smoothCursorX) * 0.22;
            smoothCursorY += (cursorY - smoothCursorY) * 0.22;
            const intensityTarget = cursorInside ? 1 : 0;
            cursorIntensity += (intensityTarget - cursorIntensity) * 0.08;

            ctx.clearRect(0, 0, width, height);
            drawVignette();
            flakes.sort((a, b) => b.z - a.z);
            for (let i = 0; i < flakes.length; i++) drawFlake(flakes[i], dt);
            drawCursorGlow();

            raf = requestAnimationFrame(frame);
        }

        function onPointerMove(e) {
            const rect = canvas.getBoundingClientRect();
            const px = e.clientX - rect.left;
            const py = e.clientY - rect.top;
            targetCenterX = width / 2 + (px - width / 2) * 0.25;
            targetCenterY = height / 2 + (py - height / 2) * 0.25;
            cursorX = px; cursorY = py;
            cursorInside =
                px >= 0 && px <= width && py >= 0 && py <= height;
        }
        function onPointerLeave() { cursorInside = false; }

        resize();
        window.addEventListener('resize', resize, { passive: true });
        // ResizeObserver catches the post-mount layout where the
        // canvas was sized 0 at install time (Blazor SPA navigation
        // mounts before final layout). Cheap — fires only on real
        // dimension changes.
        if (typeof ResizeObserver !== 'undefined') {
            new ResizeObserver(resize).observe(canvas);
        }
        window.addEventListener('pointermove', onPointerMove, { passive: true });
        window.addEventListener('pointerout', onPointerLeave, { passive: true });
        window.addEventListener('blur', onPointerLeave, { passive: true });
        document.addEventListener('visibilitychange', () => {
            if (document.hidden) {
                if (raf) cancelAnimationFrame(raf);
                raf = 0;
            } else if (!raf && canvas.isConnected) {
                last = performance.now();
                raf = requestAnimationFrame(frame);
            }
        });
        raf = requestAnimationFrame(frame);
    }

    function scan() {
        document.querySelectorAll('[data-hero-effects]')
            .forEach(installOnHero);
    }

    function start() {
        scan();
        // Blazor SPA navigation swaps DOM nodes — observe so we
        // pick up the new hero on every page transition.
        new MutationObserver(scan).observe(document.body, {
            childList: true, subtree: true,
        });
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', start, { once: true });
    } else {
        start();
    }
})();
