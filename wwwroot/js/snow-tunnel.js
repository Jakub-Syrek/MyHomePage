/* eslint-disable */
// snow-tunnel.js — perspective-projected snow tunnel animation.
//
// 3D flakes spawn deep in the scene (large z) and approach the camera
// (z → 0). They're projected onto the 2D canvas with perspective
// division so the closer a flake gets the bigger and more opaque it
// draws — gives a strong "flying through a blizzard" feel. The
// vanishing point gently follows the cursor for parallax. Each flake
// is a procedurally drawn six-pointed snowflake with sub-branches so
// they read as real flakes rather than dots.
//
// Honours prefers-reduced-motion (no animation, exits early). Pauses
// when the document is hidden to save battery.
(function () {
    'use strict';

    const CANVAS_ID = 'snow-tunnel-canvas';

    function init() {
        const canvas = document.getElementById(CANVAS_ID);
        if (!canvas) return;

        // Respect the OS-level reduced-motion preference — never spin
        // up an animation for users who've opted out.
        if (window.matchMedia('(prefers-reduced-motion: reduce)').matches) {
            canvas.style.display = 'none';
            return;
        }

        const ctx = canvas.getContext('2d', { alpha: true });
        if (!ctx) return;

        // Cap DPR at 2 — beyond that the cost of doubling pixel count
        // again outweighs the visible quality gain for soft circles
        // and thin lines, especially at the flake counts we run.
        const dpr = Math.min(window.devicePixelRatio || 1, 2);

        let width = 0;
        let height = 0;
        let centerX = 0;
        let centerY = 0;
        let targetCenterX = 0;
        let targetCenterY = 0;

        // Camera intrinsics. Field of view here is just a scaling
        // factor for the perspective projection: bigger FOV → flakes
        // appear larger for the same z, so the tunnel feels tighter.
        const FOV = 280;
        const FAR_Z = 1400;
        const NEAR_Z = 8;
        // Forward speed of the camera through the tunnel (units / s).
        const SPEED = 220;

        // Population scales with viewport area so a 4K monitor doesn't
        // look sparse and a phone doesn't choke. Cap so we never melt
        // a low-end CPU.
        let flakes = [];
        function flakeCount() {
            const area = width * height;
            return Math.min(360, Math.max(120, Math.round(area / 6500)));
        }

        function resize() {
            width = canvas.clientWidth;
            height = canvas.clientHeight;
            canvas.width = Math.round(width * dpr);
            canvas.height = Math.round(height * dpr);
            ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
            centerX = width / 2;
            centerY = height / 2;
            targetCenterX = centerX;
            targetCenterY = centerY;

            const desired = flakeCount();
            // Top up or trim — never recreate the whole array on every
            // resize so the tunnel keeps flowing through orientation
            // changes.
            while (flakes.length < desired) flakes.push(spawnFlake(true));
            if (flakes.length > desired) flakes.length = desired;
        }

        function spawnFlake(initial) {
            // Cast a ray from the camera through a random screen point
            // and place the flake at a random z along that ray. This
            // distributes flakes evenly across the visible frustum
            // instead of bunching them at the vanishing point.
            const screenX = (Math.random() - 0.5) * width * 1.4;
            const screenY = (Math.random() - 0.5) * height * 1.4;
            const z = initial
                ? NEAR_Z + Math.random() * (FAR_Z - NEAR_Z)
                : FAR_Z - Math.random() * 200; // fresh flakes start far away
            return {
                x: (screenX * z) / FOV,
                y: (screenY * z) / FOV,
                z: z,
                size: 1.6 + Math.random() * 2.4,        // base radius in world units
                rotation: Math.random() * Math.PI * 2,
                spin: (Math.random() - 0.5) * 1.4,      // rad / s
                hue: 200 + Math.random() * 40,          // cool blue-white tint
                wobble: Math.random() * Math.PI * 2,
                wobbleSpeed: 0.4 + Math.random() * 1.1,
                branchAlpha: 0.6 + Math.random() * 0.4,
            };
        }

        function drawFlake(f, dt) {
            f.z -= SPEED * dt;
            f.rotation += f.spin * dt;
            f.wobble += f.wobbleSpeed * dt;

            if (f.z < NEAR_Z) {
                // Reset to the far plane with a fresh trajectory so
                // we keep a steady density of flakes mid-tunnel.
                Object.assign(f, spawnFlake(false));
                return;
            }

            // Tiny sideways drift gives the snow some "wind" feel —
            // wobble is per-flake so they don't move in lockstep.
            const wobbleX = Math.cos(f.wobble) * 8;
            const wobbleY = Math.sin(f.wobble * 0.7) * 6;

            const screenX = centerX + ((f.x + wobbleX) * FOV) / f.z;
            const screenY = centerY + ((f.y + wobbleY) * FOV) / f.z;

            // Skip flakes that have wandered off-screen — projection
            // can fling near-camera flakes well past the viewport.
            if (screenX < -60 || screenX > width + 60 ||
                screenY < -60 || screenY > height + 60) return;

            const scale = FOV / f.z;
            const radius = f.size * scale;
            // Fade in from the far plane, peak at mid-z, then a tiny
            // alpha boost as flakes get really close so they don't
            // wash out against bright backgrounds.
            const fadeIn = Math.min(1, (FAR_Z - f.z) / 350);
            const fadeNear = Math.min(1, f.z / NEAR_Z);
            const alpha = Math.min(1, fadeIn * (0.55 + fadeNear * 0.45));

            ctx.save();
            ctx.translate(screenX, screenY);
            ctx.rotate(f.rotation);

            // Soft glow for the closer flakes — cheap to draw because
            // it's just shadowBlur on the same path, no extra fills.
            if (radius > 2) {
                ctx.shadowColor = `hsla(${f.hue}, 100%, 92%, ${alpha * 0.7})`;
                ctx.shadowBlur = Math.min(18, radius * 1.6);
            }

            // Bright core dot
            ctx.fillStyle = `hsla(${f.hue}, 100%, 96%, ${alpha})`;
            ctx.beginPath();
            ctx.arc(0, 0, Math.max(0.4, radius * 0.45), 0, Math.PI * 2);
            ctx.fill();

            // Six-pointed snowflake arms — only worth drawing once the
            // flake is large enough that the branches are actually
            // visible, otherwise it's just rasterisation noise.
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

                    // Two small barbs on each arm — placed at 60% of
                    // the arm length and angled out by 35°.
                    if (radius > 3) {
                        const mid = tip * 0.6;
                        const barb = tip * 0.32;
                        const ba = 0.6; // ~35°
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

        function drawVignette() {
            // Subtle radial darkening from the vanishing point outward
            // sells the "tunnel" reading — without it the canvas
            // looks like generic falling snow.
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
        let running = true;

        function frame(now) {
            const dt = Math.min(0.05, (now - last) / 1000);
            last = now;

            // Ease the vanishing-point toward the cursor target so
            // panning feels smooth, not snappy.
            centerX += (targetCenterX - centerX) * 0.06;
            centerY += (targetCenterY - centerY) * 0.06;

            ctx.clearRect(0, 0, width, height);
            drawVignette();

            // Draw far flakes first → near flakes last, so close
            // flakes correctly occlude (their alpha) the distant ones.
            flakes.sort((a, b) => b.z - a.z);
            for (let i = 0; i < flakes.length; i++) drawFlake(flakes[i], dt);

            if (running) raf = requestAnimationFrame(frame);
        }

        function onPointerMove(e) {
            // 25% of the way from screen-centre toward the cursor —
            // enough parallax to feel responsive, gentle enough to
            // not nauseate.
            const rect = canvas.getBoundingClientRect();
            const px = e.clientX - rect.left;
            const py = e.clientY - rect.top;
            targetCenterX = width / 2 + (px - width / 2) * 0.25;
            targetCenterY = height / 2 + (py - height / 2) * 0.25;
        }

        function onVisibilityChange() {
            if (document.hidden) {
                running = false;
                if (raf) cancelAnimationFrame(raf);
            } else if (!running) {
                running = true;
                last = performance.now();
                raf = requestAnimationFrame(frame);
            }
        }

        resize();
        window.addEventListener('resize', resize, { passive: true });
        window.addEventListener('pointermove', onPointerMove, { passive: true });
        document.addEventListener('visibilitychange', onVisibilityChange);
        raf = requestAnimationFrame(frame);
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init, { once: true });
    } else {
        init();
    }
})();
