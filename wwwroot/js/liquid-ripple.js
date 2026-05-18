/* eslint-disable */
// liquid-ripple.js — water-ripple distortion of every page's hero
// background. Same install pattern as snow-tunnel.js: scans the DOM
// for `[data-hero-effects]` and a `data-hero-image` URL, then runs a
// 480×240 Hugo-Elias wave simulation on top of a pre-rendered copy of
// that image. A MutationObserver re-scans after every Blazor SPA
// navigation so subview pages (Running, Bicycle, …) get the effect
// without any per-page wiring.
(function () {
    'use strict';
    if (window.__liquidRippleInstalled) return;
    window.__liquidRippleInstalled = true;

    const W = 480, H = 240, DAMPING = 0.985;
    const INIT_FLAG = '__liquidRippleInit';

    function installOnHero(hero) {
        if (hero[INIT_FLAG]) return;
        hero[INIT_FLAG] = true;
        if (window.matchMedia('(prefers-reduced-motion: reduce)').matches) return;

        const imgUrl = hero.getAttribute('data-hero-image');
        if (!imgUrl) return;

        const canvas = document.createElement('canvas');
        canvas.id = 'liquid-ripple-canvas';
        canvas.setAttribute('aria-hidden', 'true');
        canvas.width = W; canvas.height = H;
        hero.insertBefore(canvas, hero.firstChild);

        const ctx = canvas.getContext('2d', { alpha: true });
        if (!ctx) { canvas.remove(); return; }

        const img = new Image();
        img.crossOrigin = 'anonymous';
        img.onload = () => start();
        img.onerror = () => canvas.remove();
        img.src = imgUrl;

        function start() {
            if (!canvas.isConnected) return; // hero unmounted while image loaded

            const sourceCanvas = document.createElement('canvas');
            sourceCanvas.width = W; sourceCanvas.height = H;
            const sctx = sourceCanvas.getContext('2d');
            const srcRatio = img.naturalWidth / img.naturalHeight;
            const dstRatio = W / H;
            let sx, sy, sw, sh;
            if (srcRatio > dstRatio) {
                sh = img.naturalHeight;
                sw = img.naturalHeight * dstRatio;
                sx = (img.naturalWidth - sw) / 2; sy = 0;
            } else {
                sw = img.naturalWidth;
                sh = img.naturalWidth / dstRatio;
                sx = 0; sy = (img.naturalHeight - sh) / 2;
            }
            sctx.drawImage(img, sx, sy, sw, sh, 0, 0, W, H);
            const sourceData = sctx.getImageData(0, 0, W, H);
            const sourcePixels = sourceData.data;

            let prev = new Float32Array(W * H);
            let cur = new Float32Array(W * H);
            const targetData = ctx.createImageData(W, H);
            const targetPixels = targetData.data;

            let lastPointerX = -9999, lastPointerY = -9999;

            function disturb(gx, gy, strength) {
                if (gx < 2 || gy < 2 || gx >= W - 2 || gy >= H - 2) return;
                const idx = gy * W + gx;
                cur[idx]      -= strength;
                cur[idx - 1]  -= strength * 0.6;
                cur[idx + 1]  -= strength * 0.6;
                cur[idx - W]  -= strength * 0.6;
                cur[idx + W]  -= strength * 0.6;
            }

            function onMove(e) {
                if (!canvas.isConnected) return;
                const rect = canvas.getBoundingClientRect();
                const gx = ((e.clientX - rect.left) / rect.width) * W;
                const gy = ((e.clientY - rect.top) / rect.height) * H;
                if (gx < 0 || gx >= W || gy < 0 || gy >= H) {
                    lastPointerX = -9999; lastPointerY = -9999;
                    return;
                }
                if (lastPointerX > -1000) {
                    const dx = gx - lastPointerX;
                    const dy = gy - lastPointerY;
                    const steps = Math.max(1, Math.floor(Math.hypot(dx, dy) / 4));
                    for (let i = 0; i <= steps; i++) {
                        const t = i / steps;
                        disturb(
                            (lastPointerX + dx * t) | 0,
                            (lastPointerY + dy * t) | 0, 45);
                    }
                } else {
                    disturb(gx | 0, gy | 0, 45);
                }
                lastPointerX = gx; lastPointerY = gy;
            }
            window.addEventListener('pointermove', onMove, { passive: true });

            let nextDrip = performance.now() + 2500;
            let raf = 0;

            function step(now) {
                // Self-stop when Blazor unmounts the hero — releases
                // the listener and avoids a leaked rAF loop after
                // navigating away.
                if (!canvas.isConnected) {
                    window.removeEventListener('pointermove', onMove);
                    raf = 0; return;
                }

                for (let y = 1; y < H - 1; y++) {
                    const row = y * W;
                    for (let x = 1; x < W - 1; x++) {
                        const i = row + x;
                        const next =
                            (prev[i - 1] + prev[i + 1] +
                             prev[i - W] + prev[i + W]) * 0.5 - cur[i];
                        cur[i] = next * DAMPING;
                    }
                }
                const tmp = prev; prev = cur; cur = tmp;

                const STRENGTH = 9;
                for (let y = 1; y < H - 1; y++) {
                    const row = y * W;
                    for (let x = 1; x < W - 1; x++) {
                        const i = row + x;
                        const gradX = (prev[i - 1] - prev[i + 1]) | 0;
                        const gradY = (prev[i - W] - prev[i + W]) | 0;
                        let sxr = x + ((gradX * STRENGTH) >> 4);
                        let syr = y + ((gradY * STRENGTH) >> 4);
                        if (sxr < 0) sxr = 0; else if (sxr >= W) sxr = W - 1;
                        if (syr < 0) syr = 0; else if (syr >= H) syr = H - 1;
                        const si = (syr * W + sxr) << 2;
                        const di = i << 2;
                        const high = Math.min(40, Math.abs(gradX) + Math.abs(gradY));
                        targetPixels[di]     = Math.min(255, sourcePixels[si]     + high);
                        targetPixels[di + 1] = Math.min(255, sourcePixels[si + 1] + high);
                        targetPixels[di + 2] = Math.min(255, sourcePixels[si + 2] + high);
                        targetPixels[di + 3] = 255;
                    }
                }
                ctx.putImageData(targetData, 0, 0);

                if (now > nextDrip) {
                    disturb(
                        20 + Math.floor(Math.random() * (W - 40)),
                        20 + Math.floor(Math.random() * (H - 40)),
                        70 + Math.random() * 50);
                    nextDrip = now + 2200 + Math.random() * 2800;
                }
                raf = requestAnimationFrame(step);
            }
            raf = requestAnimationFrame(step);

            document.addEventListener('visibilitychange', () => {
                if (document.hidden && raf) { cancelAnimationFrame(raf); raf = 0; }
                else if (!raf && canvas.isConnected) raf = requestAnimationFrame(step);
            });
        }
    }

    function scan() {
        document.querySelectorAll('[data-hero-effects]')
            .forEach(installOnHero);
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
