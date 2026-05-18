/* eslint-disable */
// liquid-ripple.js — water-ripple distortion of the hero background.
//
// Renders the mountain image into a low-res offscreen canvas and
// applies a per-pixel displacement driven by a classic 2-buffer
// wave-equation height field. Cursor movement injects energy into
// the field (a circular splash), which then propagates and damps
// out over ~1 s. The result is composited back to the visible
// canvas with CSS scaling so we keep 60 fps without melting the GPU.
//
// Performance budget — the field is 480 × 240 (115k cells) which is
// the sweet spot in vanilla JS: any larger and the inner ImageData
// loop drops frames on mid-range laptops; any smaller and the
// ripples look pixelated even with CSS smoothing.
//
// Sits at z-index 0 inside the hero, beneath the snow tunnel
// (z-index 1) and the content layer (z-index ~1+).
(function () {
    'use strict';

    const W = 480;          // sim grid width
    const H = 240;          // sim grid height
    const DAMPING = 0.985;  // 0..1 — higher = ripples last longer

    function init() {
        if (window.matchMedia('(prefers-reduced-motion: reduce)').matches) return;

        const hero = document.querySelector('.home-hero');
        if (!hero) return;

        // Drop the canvas as the first child so it sits below the
        // snow tunnel (which is the existing first child). We set a
        // negative z-index relative to the content layer.
        const canvas = document.createElement('canvas');
        canvas.id = 'liquid-ripple-canvas';
        canvas.setAttribute('aria-hidden', 'true');
        canvas.width = W;
        canvas.height = H;
        canvas.style.cssText = [
            'position: absolute',
            'inset: 0',
            'width: 100%',
            'height: 100%',
            'z-index: 0',
            'pointer-events: none',
            'display: block',
            // Sharp upscaling would expose the 480×240 grid — the
            // browser's default bilinear filter gives a pleasant
            // soft look that matches the "rippling water" reading.
            'image-rendering: auto',
        ].join(';');
        hero.insertBefore(canvas, hero.firstChild);

        const ctx = canvas.getContext('2d', { alpha: true });
        if (!ctx) { canvas.remove(); return; }

        // Load the same image the CSS uses for the hero background.
        // While it loads, the CSS background is still visible behind
        // our transparent canvas — graceful progressive enhancement.
        const img = new Image();
        img.crossOrigin = 'anonymous';
        img.src = '/images/mountains-bg.jpg';

        img.onload = () => start();
        // If the image fails (e.g. offline), bail silently — the
        // CSS bg will keep showing.
        img.onerror = () => canvas.remove();

        function start() {
            // Pre-render the source image into a hidden canvas at
            // the simulation resolution so per-pixel sampling is
            // cheap (no resampling cost during the hot loop).
            const sourceCanvas = document.createElement('canvas');
            sourceCanvas.width = W;
            sourceCanvas.height = H;
            const sctx = sourceCanvas.getContext('2d');
            // Cover-style draw: scale + crop the image to fill W×H
            // while preserving its aspect ratio, matching the
            // `background-size: cover` CSS rule.
            const srcRatio = img.naturalWidth / img.naturalHeight;
            const dstRatio = W / H;
            let sx, sy, sw, sh;
            if (srcRatio > dstRatio) {
                sh = img.naturalHeight;
                sw = img.naturalHeight * dstRatio;
                sx = (img.naturalWidth - sw) / 2;
                sy = 0;
            } else {
                sw = img.naturalWidth;
                sh = img.naturalWidth / dstRatio;
                sx = 0;
                sy = (img.naturalHeight - sh) / 2;
            }
            sctx.drawImage(img, sx, sy, sw, sh, 0, 0, W, H);
            const sourceData = sctx.getImageData(0, 0, W, H);
            const sourcePixels = sourceData.data;

            // Two ping-ponged height buffers — classic Hugo Elias
            // ripple algorithm. `prev` holds last frame's heights,
            // `cur` holds the in-flight frame we're computing.
            let prev = new Float32Array(W * H);
            let cur = new Float32Array(W * H);
            const targetData = ctx.createImageData(W, H);
            const targetPixels = targetData.data;

            let lastPointerX = -9999, lastPointerY = -9999;

            // Inject ripple energy along the cursor's path between
            // moves. Drawing single dots leaves gaps when the mouse
            // moves fast; interpolating along the segment gives a
            // continuous wake.
            function disturb(gx, gy, strength) {
                if (gx < 2 || gy < 2 || gx >= W - 2 || gy >= H - 2) return;
                const idx = gy * W + gx;
                // Small circular blob — width 3 px gives a smooth
                // ripple front rather than a single-cell spike.
                cur[idx] -= strength;
                cur[idx - 1] -= strength * 0.6;
                cur[idx + 1] -= strength * 0.6;
                cur[idx - W] -= strength * 0.6;
                cur[idx + W] -= strength * 0.6;
            }

            window.addEventListener('pointermove', e => {
                const rect = canvas.getBoundingClientRect();
                const gx = ((e.clientX - rect.left) / rect.width) * W;
                const gy = ((e.clientY - rect.top) / rect.height) * H;
                if (gx < 0 || gx >= W || gy < 0 || gy >= H) {
                    lastPointerX = -9999; lastPointerY = -9999;
                    return;
                }
                if (lastPointerX > -1000) {
                    // Walk the segment in 4-px steps so a fast move
                    // still lays a continuous wake.
                    const dx = gx - lastPointerX;
                    const dy = gy - lastPointerY;
                    const steps = Math.max(1, Math.floor(Math.hypot(dx, dy) / 4));
                    for (let i = 0; i <= steps; i++) {
                        const t = i / steps;
                        disturb(
                            (lastPointerX + dx * t) | 0,
                            (lastPointerY + dy * t) | 0,
                            45);
                    }
                } else {
                    disturb(gx | 0, gy | 0, 45);
                }
                lastPointerX = gx; lastPointerY = gy;
            }, { passive: true });

            // Random ambient drip every ~3 s so the water feels
            // alive even when the cursor is parked off the hero.
            let nextDrip = performance.now() + 2500;

            let raf = 0;
            let running = true;

            function step(now) {
                // ── Wave-equation update ─────────────────────────
                // Each cell's new height = average of its four
                // neighbours - its previous height, attenuated by
                // the damping factor. Skip a 1-px border so we
                // don't need bounds checks in the inner loop.
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
                // Swap buffers — prev is now what we just computed,
                // cur becomes the slot we'll write into next frame.
                const tmp = prev; prev = cur; cur = tmp;

                // ── Render: sample source with displacement ─────
                // Approximate the gradient of the height field by
                // finite differences and use that as a UV offset
                // into the source image. Strength 8 looks like
                // gentle water; bump to 14+ for a heavier distortion.
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
                        // Slight specular highlight on steep
                        // gradients (the ripple crests) — adds
                        // visible "wet light" without another pass.
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

                if (running) raf = requestAnimationFrame(step);
            }
            raf = requestAnimationFrame(step);

            document.addEventListener('visibilitychange', () => {
                if (document.hidden) {
                    running = false;
                    if (raf) cancelAnimationFrame(raf);
                } else if (!running) {
                    running = true;
                    raf = requestAnimationFrame(step);
                }
            });
        }
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init, { once: true });
    } else {
        init();
    }
})();
