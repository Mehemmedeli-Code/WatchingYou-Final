import { gsap } from "gsap";
import { useEffect, useRef } from "react";

/**
 * Skiper 39 Canvas_Landing_004 — React + Canvas
 * Inspired by and adapted from https://codepen.io/zadvorsky/pen/xxwbBQV
 * Illustration by https://www.openpeeps.com/
 *
 * License & Usage (Skiper UI, free version):
 * - Free to use and modify in both personal and commercial projects.
 * - Attribution to Skiper UI is required when using the free version.
 *
 * Author: @gurvinder-singh02 · https://gxuri.me
 *
 * Changes in this copy, and why:
 *  - `"use client"` removed; it is a Next.js marker and means nothing in a Vite build.
 *  - The image load is guarded. The original started the animation from `img.onload` with no
 *    check that the component was still mounted, so leaving the page before the sprite sheet
 *    arrived left a render loop running against a canvas that no longer existed — and React's
 *    development mode, which mounts effects twice, started two.
 *  - `prefers-reduced-motion` draws the crowd once, standing still.
 *  - People are scaled to the canvas. The original drew each figure at the sprite sheet's own
 *    size, which suits a full-screen canvas and cuts heads off in a band a few hundred pixels
 *    tall.
 *  - `any` replaced with the shapes actually passed around.
 */

interface CrowdCanvasProps {
  src: string;
  rows?: number;
  cols?: number;
  className?: string;
}

type Stage = { width: number; height: number };

type Peep = {
  image: HTMLImageElement;
  rect: number[];
  width: number;
  height: number;
  x: number;
  y: number;
  anchorY: number;
  scaleX: number;
  walk: gsap.core.Timeline | null;
  render: (ctx: CanvasRenderingContext2D) => void;
};

export function CrowdCanvas({ src, rows = 15, cols = 7, className }: CrowdCanvasProps) {
  const canvasRef = useRef<HTMLCanvasElement>(null);

  useEffect(() => {
    const canvas = canvasRef.current;
    const ctx = canvas?.getContext("2d");
    if (!canvas || !ctx) return;

    let disposed = false;
    const still = window.matchMedia?.("(prefers-reduced-motion: reduce)").matches ?? false;

    const randomRange = (min: number, max: number) => min + Math.random() * (max - min);
    const randomIndex = <T,>(array: T[]) => randomRange(0, array.length) | 0;
    const removeAt = <T,>(array: T[], i: number) => array.splice(i, 1)[0];
    const removeItem = <T,>(array: T[], item: T) => removeAt(array, array.indexOf(item));
    const removeRandom = <T,>(array: T[]) => removeAt(array, randomIndex(array));

    const stage: Stage = { width: 0, height: 0 };
    let scale = 1;
    const allPeeps: Peep[] = [];
    const available: Peep[] = [];
    const crowd: Peep[] = [];

    const createPeep = (image: HTMLImageElement, rect: number[]): Peep => {
      const peep: Peep = {
        image,
        rect,
        width: rect[2],
        height: rect[3],
        x: 0,
        y: 0,
        anchorY: 0,
        scaleX: 1,
        walk: null,
        render: (context) => {
          context.save();
          context.translate(peep.x, peep.y);
          context.scale(peep.scaleX, 1);
          context.drawImage(peep.image, peep.rect[0], peep.rect[1], peep.rect[2], peep.rect[3], 0, 0, peep.width, peep.height);
          context.restore();
        },
      };
      return peep;
    };

    const resetPeep = (peep: Peep) => {
      const direction = Math.random() > 0.5 ? 1 : -1;
      // The vertical scatter scales with the figures, so a smaller band keeps the same depth.
      const offsetY = (100 - 250 * gsap.parseEase("power2.in")(Math.random())) * scale;
      const startY = stage.height - peep.height + offsetY;
      const startX = direction === 1 ? -peep.width : stage.width + peep.width;
      const endX = direction === 1 ? stage.width : 0;

      peep.scaleX = direction;
      peep.x = startX;
      peep.y = startY;
      peep.anchorY = startY;

      return { startY, endX };
    };

    const walk = (peep: Peep, { startY, endX }: { startY: number; endX: number }) => {
      const xDuration = 10;
      const yDuration = 0.25;

      const timeline = gsap.timeline();
      timeline.timeScale(randomRange(0.5, 1.5));
      timeline.to(peep, { duration: xDuration, x: endX, ease: "none" }, 0);
      timeline.to(peep, { duration: yDuration, repeat: xDuration / yDuration, yoyo: true, y: startY - 10 * scale }, 0);
      return timeline;
    };

    const removeFromCrowd = (peep: Peep) => {
      removeItem(crowd, peep);
      available.push(peep);
    };

    const addToCrowd = (): Peep => {
      const peep = removeRandom(available);
      peep.walk = walk(peep, resetPeep(peep)).eventCallback("onComplete", () => {
        removeFromCrowd(peep);
        addToCrowd();
      });

      crowd.push(peep);
      // Painted back to front, so nearer people overlap those further away.
      crowd.sort((a, b) => a.anchorY - b.anchorY);
      return peep;
    };

    const render = () => {
      ctx.clearRect(0, 0, canvas.width, canvas.height);
      ctx.save();
      ctx.scale(devicePixelRatio, devicePixelRatio);
      crowd.forEach((peep) => peep.render(ctx));
      ctx.restore();
    };

    const resize = () => {
      stage.width = canvas.clientWidth;
      stage.height = canvas.clientHeight;
      canvas.width = stage.width * devicePixelRatio;
      canvas.height = stage.height * devicePixelRatio;

      // A figure stands at most three quarters of the band's height; never enlarged past the
      // artwork's own size, which would only blur it.
      const artHeight = allPeeps[0]?.rect[3] ?? 1;
      scale = Math.min(1, (stage.height * 0.75) / artHeight);
      allPeeps.forEach((peep) => {
        peep.width = peep.rect[2] * scale;
        peep.height = peep.rect[3] * scale;
      });

      crowd.forEach((peep) => peep.walk?.kill());
      crowd.length = 0;
      available.length = 0;
      available.push(...allPeeps);

      while (available.length) addToCrowd().walk?.progress(Math.random());

      if (still) {
        // Scattered once and frozen: the people are there, nobody walks.
        crowd.forEach((peep) => peep.walk?.pause());
        render();
      }
    };

    const image = document.createElement("img");
    image.onload = () => {
      if (disposed) return;

      const width = image.naturalWidth / rows;
      const height = image.naturalHeight / cols;
      for (let i = 0; i < rows * cols; i++) {
        allPeeps.push(createPeep(image, [(i % rows) * width, ((i / rows) | 0) * height, width, height]));
      }

      resize();
      if (!still) gsap.ticker.add(render);
    };
    image.src = src;

    window.addEventListener("resize", resize);

    return () => {
      disposed = true;
      image.onload = null;
      window.removeEventListener("resize", resize);
      gsap.ticker.remove(render);
      crowd.forEach((peep) => peep.walk?.kill());
    };
  }, [src, rows, cols]);

  return <canvas ref={canvasRef} className={className ?? "absolute bottom-0 h-full w-full"} />;
}

export default CrowdCanvas;
