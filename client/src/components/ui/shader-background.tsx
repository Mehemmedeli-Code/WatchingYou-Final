import { useEffect, useRef } from "react";

/**
 * WebGL "silk" background, adapted from the 21st.dev Shader Builder output.
 *
 * Changes from the supplied component:
 *  - `"use client"` removed — a Next.js App Router marker with no meaning in Vite.
 *  - Palette retuned to this project's tokens so the field reads as the same black and neon
 *    green as the rest of the site rather than a second, competing green.
 *  - Shader compilation and linking are now checked. The original assumed success; on a
 *    driver that rejects the program it would draw nothing and report nothing, which is a
 *    miserable thing to debug. A failure now logs once and leaves the flat background.
 *  - `prefers-reduced-motion` honoured: the field still renders, but frozen. A constantly
 *    moving backdrop behind every page is exactly what that setting exists to stop.
 *  - Cursor interaction left off. It is per-pixel work on every pointer move, on every page,
 *    for an effect nobody looks at directly.
 *
 * Kept as-is: the DPR cap, the 2-megapixel ceiling, the IntersectionObserver and
 * visibilitychange pauses, and the deferred context release. Those are the parts that stop
 * a decorative canvas from costing real battery.
 */

const VERT = `attribute vec2 a_position;
void main() {
  gl_Position = vec4(a_position, 0.0, 1.0);
}`;

const FRAG = `#ifdef GL_FRAGMENT_PRECISION_HIGH
precision highp float;
#else
precision mediump float;
#endif

uniform vec3 u_colors[8];
uniform vec4 u_scene;      // resolution.xy, time, colour count
uniform vec4 u_shape;      // scale, intensity, paramA, warp
uniform vec4 u_surface;    // detail, contrast, brightness, saturation
uniform vec4 u_finish;     // hue, vignette, blur, grain
uniform vec4 u_transform;  // seed, rotation, drift, OKLab toggle
uniform vec4 u_space;      // offset.xy, pointer.xy

#define u_resolution u_scene.xy
#define u_time u_scene.z
#define u_colorCount u_scene.w
#define u_scale u_shape.x
#define u_intensity u_shape.y
#define u_warp u_shape.w
#define u_detail u_surface.x
#define u_contrast u_surface.y
#define u_brightness u_surface.z
#define u_saturation u_surface.w
#define u_vignette u_finish.y
#define u_grain u_finish.w
#ifdef GL_FRAGMENT_PRECISION_HIGH
#define u_seed u_transform.x
#else
#define u_seed mod(u_transform.x, 31.0)
#endif
#define u_drift u_transform.z
#define u_oklab u_transform.w
#define u_offset u_space.xy

float hash21(vec2 p) {
#ifndef GL_FRAGMENT_PRECISION_HIGH
  p = mod(p, 31.0);
#endif
  p = fract(p * vec2(234.34, 435.345));
  p += dot(p, p + 34.23);
  return fract(p.x * p.y);
}

// Dave Hoskins hash for grain: the multiply hash above shows a faint axis-aligned
// mesh at integer fragment coords, which reads as a net over flat areas.
float grainHash(vec2 p) {
  vec3 p3 = fract(vec3(p.xyx) * 0.1031);
  p3 += dot(p3, p3.yzx + 33.33);
  return fract((p3.x + p3.y) * p3.z);
}

float noise(vec2 p) {
  vec2 i = floor(p);
  vec2 f = fract(p);
  vec2 u = f * f * (3.0 - 2.0 * f);
  return mix(
    mix(hash21(i), hash21(i + vec2(1.0, 0.0)), u.x),
    mix(hash21(i + vec2(0.0, 1.0)), hash21(i + vec2(1.0, 1.0)), u.x),
    u.y);
}

float fbm(vec2 p) {
  float v = 0.0;
  float a = 0.5;
  for (int i = 0; i < 5; i++) {
    v += a * noise(p);
    p = p * 2.03 + vec2(17.0, 9.2);
    a *= 0.5;
  }
  return v;
}

vec3 srgbToLinear(vec3 c) {
  return mix(c / 12.92, pow((c + 0.055) / 1.055, vec3(2.4)), step(0.04045, c));
}
vec3 linearToSrgb(vec3 c) {
  // max() guards the sRGB branch: an out-of-gamut OKLab mix can send a channel
  // negative, and pow(negative, x) is NaN, which mix() would then propagate.
  return mix(c * 12.92, 1.055 * pow(max(c, vec3(0.0)), vec3(1.0 / 2.4)) - 0.055,
    step(0.0031308, c));
}
vec3 linToOklab(vec3 c) {
  float l = 0.4122214708 * c.r + 0.5363325363 * c.g + 0.0514459929 * c.b;
  float m = 0.2119034982 * c.r + 0.6806995451 * c.g + 0.1073969566 * c.b;
  float s = 0.0883024619 * c.r + 0.2817188376 * c.g + 0.6299787005 * c.b;
  l = pow(max(l, 0.0), 1.0 / 3.0);
  m = pow(max(m, 0.0), 1.0 / 3.0);
  s = pow(max(s, 0.0), 1.0 / 3.0);
  return vec3(
    0.2104542553 * l + 0.7936177850 * m - 0.0040720468 * s,
    1.9779984951 * l - 2.4285922050 * m + 0.4505937099 * s,
    0.0259040371 * l + 0.7827717662 * m - 0.8086757660 * s);
}
vec3 oklabToLin(vec3 c) {
  float l = c.x + 0.3963377774 * c.y + 0.2158037573 * c.z;
  float m = c.x - 0.1055613458 * c.y - 0.0638541728 * c.z;
  float s = c.x - 0.0894841775 * c.y - 1.2914855480 * c.z;
  l = l * l * l; m = m * m * m; s = s * s * s;
  return vec3(
    4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s,
    -1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s,
    -0.0041960863 * l - 0.7034186147 * m + 1.7076147010 * s);
}
vec3 mixColour(vec3 a, vec3 b, float t) {
  if (u_oklab > 0.5) {
    vec3 la = linToOklab(srgbToLinear(a));
    vec3 lb = linToOklab(srgbToLinear(b));
    return clamp(linearToSrgb(oklabToLin(mix(la, lb, t))), 0.0, 1.0);
  }
  return mix(a, b, t);
}

// WebGL1 forbids dynamic uniform indexing in fragment shaders, hence the constant loop.
vec3 palette(float x) {
  float n = max(u_colorCount - 1.0, 1.0);
  float f = clamp(x, 0.0, 1.0) * n;
  vec3 col = u_colors[0];
  for (int i = 0; i < 7; i++) {
    if (float(i) < n)
      col = mixColour(col, u_colors[i + 1],
        smoothstep(0.0, 1.0, clamp(f - float(i), 0.0, 1.0)));
  }
  return col;
}

vec3 shade(vec2 p, float t) {
  vec2 q = p * 1.6;
  float amp = 0.25 + u_intensity * 0.85;
  for (float i = 1.0; i < 5.0; i += 1.0) {
    q.x += amp / i * cos(i * 2.4 * q.y + t * 0.8 + u_seed);
    q.y += amp / i * cos(i * 1.7 * q.x + t * 0.6);
  }
  return palette(0.5 + 0.5 * sin(q.x + q.y));
}

void main() {
  vec2 screenUv = gl_FragCoord.xy / u_resolution.xy;
  vec2 p = (gl_FragCoord.xy - 0.5 * u_resolution.xy)
    / min(u_resolution.x, u_resolution.y);

  p *= u_scale;
  p += u_offset;
  if (u_drift > 0.0001)
    p += u_drift * vec2(sin(u_time * 0.31), cos(u_time * 0.23));
  if (u_warp > 0.0) {
    p += u_warp * (vec2(
      fbm(p * u_detail + u_seed),
      fbm(p * u_detail + vec2(5.2, 1.3))) - 0.5);
  }

  vec3 col = shade(p, u_time);

  if (abs(u_contrast - 1.0) > 0.0001)
    col = (col - 0.5) * u_contrast + 0.5;
  if (abs(u_saturation - 1.0) > 0.0001) {
    float luma = dot(col, vec3(0.299, 0.587, 0.114));
    col = mix(vec3(luma), col, u_saturation);
  }
  if (abs(u_brightness) > 0.0001)
    col += u_brightness;
  if (u_vignette > 0.0001) {
    float vd = length(screenUv - 0.5) * 1.41421356;
    col *= 1.0 - u_vignette * smoothstep(0.35, 1.0, vd);
  }
  if (u_grain > 0.0001)
    col += (grainHash(
      gl_FragCoord.xy + vec2(u_seed * 17.0, u_seed * 31.0)) - 0.5) * u_grain;

  gl_FragColor = vec4(clamp(col, 0.0, 1.0), 1.0);
}
`;

/**
 * Palette taken straight from the design tokens: surface, the deep green of the status
 * background, accent-dim and accent. Weighted towards the dark end so text keeps its
 * contrast — this sits behind every word on the site.
 */
const UNIFORMS = {
  colors: [
    [0.039, 0.047, 0.039],  // #0A0C0A  surface
    [0.055, 0.227, 0.141],  // #0E3A24  deep green
    [0.000, 0.659, 0.353],  // #00A85A  accent-dim
    [0.000, 0.902, 0.463],  // #00E676  accent
    [0.000, 0.902, 0.463],
    [0.000, 0.902, 0.463],
    [0.000, 0.902, 0.463],
    [0.000, 0.902, 0.463],
  ] as [number, number, number][],
  colorCount: 4,
  scale: 1.5,
  intensity: 0.55,
  warp: 0.0,
  detail: 2.4,
  contrast: 1.005,
  // Pushed well down from the original -0.05: readable body text matters more than a
  // vivid backdrop, and the container dims it further on top of this.
  brightness: -0.34,
  saturation: 0.9,
  vignette: 0.35,
  grain: 0.035,
  seed: 1.0,
  offsetX: 0.0,
  offsetY: 0.0,
  drift: 0.0,
  oklab: 0.0,
  timeScale: 0.42,
};

const pendingContextReleases = new WeakMap<HTMLCanvasElement, number>();

export function ShaderBackground({ className }: { className?: string }) {
  const canvasRef = useRef<HTMLCanvasElement>(null);

  useEffect(() => {
    const canvas = canvasRef.current;
    if (!canvas) return;

    const pendingRelease = pendingContextReleases.get(canvas);
    if (pendingRelease !== undefined) window.clearTimeout(pendingRelease);
    pendingContextReleases.delete(canvas);

    const context = canvas.getContext("webgl", { antialias: false });
    if (!context) return;   // no WebGL: the flat token background stays, nothing breaks
    const gl = context;

    const compile = (type: number, source: string) => {
      const shader = gl.createShader(type)!;
      gl.shaderSource(shader, source);
      gl.compileShader(shader);
      if (!gl.getShaderParameter(shader, gl.COMPILE_STATUS)) {
        console.warn("Shader background disabled:", gl.getShaderInfoLog(shader));
        gl.deleteShader(shader);
        return null;
      }
      return shader;
    };

    const vertexShader = compile(gl.VERTEX_SHADER, VERT);
    const fragmentShader = compile(gl.FRAGMENT_SHADER, FRAG);
    if (!vertexShader || !fragmentShader) return;

    const program = gl.createProgram()!;
    gl.attachShader(program, vertexShader);
    gl.attachShader(program, fragmentShader);
    gl.linkProgram(program);
    gl.deleteShader(vertexShader);
    gl.deleteShader(fragmentShader);

    if (!gl.getProgramParameter(program, gl.LINK_STATUS)) {
      console.warn("Shader background disabled:", gl.getProgramInfoLog(program));
      gl.deleteProgram(program);
      return;
    }
    gl.useProgram(program);

    // One full-screen triangle: cheaper than a quad and has no diagonal seam.
    const buffer = gl.createBuffer();
    gl.bindBuffer(gl.ARRAY_BUFFER, buffer);
    gl.bufferData(gl.ARRAY_BUFFER, new Float32Array([-1, -1, 3, -1, -1, 3]), gl.STATIC_DRAW);
    const positionLocation = gl.getAttribLocation(program, "a_position");
    gl.enableVertexAttribArray(positionLocation);
    gl.vertexAttribPointer(positionLocation, 2, gl.FLOAT, false, 0, 0);

    const uniform = {
      colors: gl.getUniformLocation(program, "u_colors"),
      scene: gl.getUniformLocation(program, "u_scene"),
      shape: gl.getUniformLocation(program, "u_shape"),
      surface: gl.getUniformLocation(program, "u_surface"),
      finish: gl.getUniformLocation(program, "u_finish"),
      transform: gl.getUniformLocation(program, "u_transform"),
      space: gl.getUniformLocation(program, "u_space"),
    };

    gl.uniform3fv(uniform.colors, new Float32Array(UNIFORMS.colors.flat()));
    gl.uniform4f(uniform.shape, UNIFORMS.scale, UNIFORMS.intensity, 0.5, UNIFORMS.warp);
    gl.uniform4f(uniform.surface, UNIFORMS.detail, UNIFORMS.contrast, UNIFORMS.brightness, UNIFORMS.saturation);
    gl.uniform4f(uniform.finish, 0, UNIFORMS.vignette, 0, UNIFORMS.grain);
    gl.uniform4f(uniform.transform, UNIFORMS.seed, 0, UNIFORMS.drift, UNIFORMS.oklab);
    gl.uniform4f(uniform.space, UNIFORMS.offsetX, UNIFORMS.offsetY, 0, 0);

    const reduceMotion = window.matchMedia?.("(prefers-reduced-motion: reduce)").matches ?? false;
    const timeScale = reduceMotion ? 0 : UNIFORMS.timeScale;

    let bounds = canvas.getBoundingClientRect();
    let frame = 0;
    let visible = document.visibilityState === "visible";
    let inView = true;
    let disposed = false;
    const start = performance.now();

    const resizeCanvas = () => {
      // A soft, blurred field: rendering it at half resolution and letting the browser scale it
      // up looks the same, and costs a quarter of the GPU work of full resolution — the page
      // stays responsive on laptops with integrated graphics.
      const dpr = 0.5;
      const rawWidth = Math.max(1, Math.round(bounds.width * dpr));
      const rawHeight = Math.max(1, Math.round(bounds.height * dpr));
      // And never more than ~0.6 megapixels per frame, whatever the screen.
      const pixelScale = Math.min(1, Math.sqrt(600_000 / Math.max(1, rawWidth * rawHeight)));
      const width = Math.max(1, Math.round(rawWidth * pixelScale));
      const height = Math.max(1, Math.round(rawHeight * pixelScale));
      if (canvas.width !== width || canvas.height !== height) {
        canvas.width = width;
        canvas.height = height;
        gl.viewport(0, 0, width, height);
      }
    };

    function requestRender() {
      if (!disposed && visible && inView && frame === 0) frame = requestAnimationFrame(render);
    }

    // A slow drift reads the same at 30 frames a second, at half the cost of 60.
    let lastDrawn = 0;
    function render(now: number) {
      frame = 0;
      // `context` is re-checked rather than asserted: render is a hoisted declaration, so
      // the compiler cannot carry the earlier narrowing in here, and an assertion would
      // just hide that rather than state it.
      if (disposed || !visible || !inView || !context) return;
      if (timeScale !== 0 && now - lastDrawn < 33) { requestRender(); return; }
      lastDrawn = now;

      resizeCanvas();
      context.uniform4f(uniform.scene, canvas!.width, canvas!.height,
        ((now - start) / 1000) * timeScale, UNIFORMS.colorCount);
      context.drawArrays(context.TRIANGLES, 0, 3);

      // Frozen for reduced motion: draw one frame and stop asking for more.
      if (timeScale !== 0) requestRender();
    }

    const updateLayout = () => {
      bounds = canvas.getBoundingClientRect();
      resizeCanvas();
      requestRender();
    };
    window.addEventListener("resize", updateLayout);

    const resizeObserver = new ResizeObserver(updateLayout);
    resizeObserver.observe(canvas);

    // Scrolled past, or the tab is hidden: stop drawing entirely.
    const intersectionObserver = new IntersectionObserver(([entry]) => {
      inView = entry?.isIntersecting ?? true;
      if (inView) requestRender();
      else if (frame !== 0) { cancelAnimationFrame(frame); frame = 0; }
    });
    intersectionObserver.observe(canvas);

    const onVisibilityChange = () => {
      visible = document.visibilityState === "visible";
      if (visible) requestRender();
      else if (frame !== 0) { cancelAnimationFrame(frame); frame = 0; }
    };
    document.addEventListener("visibilitychange", onVisibilityChange);

    requestRender();

    return () => {
      disposed = true;
      cancelAnimationFrame(frame);
      resizeObserver.disconnect();
      intersectionObserver.disconnect();
      document.removeEventListener("visibilitychange", onVisibilityChange);
      window.removeEventListener("resize", updateLayout);
      gl.deleteBuffer(buffer);
      gl.deleteProgram(program);

      // Deferred so a StrictMode double-mount reuses the context instead of
      // burning one of the browser's handful of WebGL slots.
      const releaseTimer = window.setTimeout(() => {
        if (pendingContextReleases.get(canvas) !== releaseTimer) return;
        pendingContextReleases.delete(canvas);
        gl.getExtension("WEBGL_lose_context")?.loseContext();
        canvas.width = 1;
        canvas.height = 1;
      }, 0);
      pendingContextReleases.set(canvas, releaseTimer);
    };
  }, []);

  return (
    <canvas
      ref={canvasRef}
      className={className}
      aria-hidden
      style={{ display: "block", width: "100%", height: "100%" }}
    />
  );
}

export default ShaderBackground;
