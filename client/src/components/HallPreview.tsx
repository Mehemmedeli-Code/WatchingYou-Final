import { useEffect, useRef, useState } from "react";
// Type-only: erased at build time, so the runtime still loads Three.js lazily.
import type * as ThreeTypes from "three";
import { ChevronLeft, ChevronRight, X } from "lucide-react";
import { createPortal } from "react-dom";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { get } from "@/lib/api";
import { t } from "@/lib/i18n";

export interface HallGeometry {
  id: string;
  name: string;
  format?: string | null;
  rows: number;
  seatsPerRow: number;
  blockColumns: number;
  aisleWidth: number;
  rowPitch: number;
  rowRise: number;
  seatWidth: number;
  roomWidth: number;
  roomDepth: number;
  roomHeight: number;
  screenWidth: number;
  screenHeight: number;
  screenCurveRadius: number;
  firstRowDistance: number;
}

const ROW_LABELS = "ABCDEFGHIJKLMNOP";

/**
 * "What will I actually see from seat E6?" — the one question a flat seat map cannot answer.
 *
 * The scene is the one built in Claude Design, ported off that tool's `three-d-stage`
 * runtime onto plain Three.js so it can live in this bundle. Two things changed in the move:
 *
 *  - Every dimension now comes from the hall record instead of being hardcoded. Sixteen
 *    rooms across four cinemas are genuinely different sizes, and a preview that drew the
 *    same box for all of them would be decoration rather than information.
 *  - Three.js is loaded with a dynamic import, so the ~600 KB only arrives when somebody
 *    actually opens the preview. Every other page of the site is unaffected.
 */
export interface PreviewSeat { row: number; seat: number }

export function HallPreview({
  hallId,
  seats,
  onClose,
}: {
  hallId: string;
  seats: PreviewSeat[];
  onClose: () => void;
}) {
  const mountRef = useRef<HTMLDivElement>(null);
  const [hall, setHall] = useState<HallGeometry | null>(null);
  const [readout, setReadout] = useState("");
  const [failed, setFailed] = useState(false);
  const [index, setIndex] = useState(0);

  // Moving the camera between seats must not rebuild the room, so the scene hands this
  // function back and a separate effect calls it when the chosen seat changes.
  const sitRef = useRef<((row: number, seat: number) => void) | null>(null);

  useEffect(() => {
    get<HallGeometry>(`/api/halls/${hallId}`).then(setHall).catch(() => setFailed(true));
  }, [hallId]);

  useEffect(() => {
    const onKey = (event: KeyboardEvent) => {
      if (event.key === "Escape") onClose();
      // Arrow keys step through the seats you picked, which is the whole point of showing
      // more than one.
      if (event.key === "ArrowRight") setIndex((i) => (i + 1) % seats.length);
      if (event.key === "ArrowLeft") setIndex((i) => (i - 1 + seats.length) % seats.length);
    };
    document.addEventListener("keydown", onKey);
    const previous = document.body.style.overflow;
    document.body.style.overflow = "hidden";
    return () => {
      document.removeEventListener("keydown", onKey);
      document.body.style.overflow = previous;
    };
  }, [onClose, seats.length]);

  useEffect(() => {
    if (!hall || !mountRef.current) return;

    const mount = mountRef.current;
    let disposed = false;
    let cleanup = () => {};

    (async () => {
      const THREE = await import("three");
      if (disposed) return;

      const M = {
        wall:  new THREE.MeshStandardMaterial({ color: 0x2a1f24, roughness: 0.95 }),
        panel: new THREE.MeshStandardMaterial({ color: 0x40222b, roughness: 0.9 }),
        floor: new THREE.MeshStandardMaterial({ color: 0x241a1e, roughness: 1.0 }),
        seat:  new THREE.MeshStandardMaterial({ color: 0x7d1f2b, roughness: 0.85 }),
        frame: new THREE.MeshStandardMaterial({ color: 0x1a1a1d, roughness: 0.55, metalness: 0.35 }),
        brass: new THREE.MeshStandardMaterial({ color: 0xb99457, roughness: 0.4, metalness: 0.35 }),
        step:  new THREE.MeshStandardMaterial({ color: 0x2a2320, emissive: 0xffb46a, emissiveIntensity: 0.55, roughness: 0.7 }),
        exit:  new THREE.MeshStandardMaterial({ color: 0x0f2a18, emissive: 0x3ddc84, emissiveIntensity: 1.4, roughness: 0.6 }),
      };

      // A canvas texture stands in for a projected image. Without it the screen reads as a
      // white rectangle and the whole point — how big it looks from here — is lost.
      const canvas = document.createElement("canvas");
      canvas.width = 512; canvas.height = 224;
      const ctx = canvas.getContext("2d")!;
      const screenTex = new THREE.CanvasTexture(canvas);
      screenTex.colorSpace = THREE.SRGBColorSpace;

      const screenMat = new THREE.MeshStandardMaterial({
        color: 0x111111, map: screenTex, emissive: 0xffffff, emissiveMap: screenTex,
        emissiveIntensity: 1.6, roughness: 0.9, side: THREE.DoubleSide,
      });

      const drawScreen = (time: number) => {
        const g = ctx.createLinearGradient(0, 0, 0, canvas.height);
        g.addColorStop(0, "#cdd8e6"); g.addColorStop(0.5, "#9aabbf"); g.addColorStop(1, "#5d6b7c");
        ctx.fillStyle = g; ctx.fillRect(0, 0, canvas.width, canvas.height);

        const v = ctx.createRadialGradient(canvas.width / 2, canvas.height / 2, canvas.height * 0.2,
                                           canvas.width / 2, canvas.height / 2, canvas.width * 0.62);
        v.addColorStop(0, "rgba(255,255,255,0.10)"); v.addColorStop(1, "rgba(10,14,20,0.35)");
        ctx.fillStyle = v; ctx.fillRect(0, 0, canvas.width, canvas.height);

        const y = ((time * 18) % (canvas.height * 1.6)) - canvas.height * 0.3;
        const band = ctx.createLinearGradient(0, y, 0, y + 120);
        band.addColorStop(0, "rgba(255,255,255,0)");
        band.addColorStop(0.5, "rgba(255,255,255,0.12)");
        band.addColorStop(1, "rgba(255,255,255,0)");
        ctx.fillStyle = band; ctx.fillRect(0, y, canvas.width, 120);
        screenTex.needsUpdate = true;
      };
      drawScreen(0);

      const scene = new THREE.Scene();
      scene.background = new THREE.Color(0x0b0b0d);

      const box = (w: number, h: number, d: number, mat: ThreeTypes.Material,
                   x: number, y: number, z: number, parent: ThreeTypes.Object3D) => {
        const mesh = new THREE.Mesh(new THREE.BoxGeometry(w, h, d), mat);
        mesh.position.set(x, y, z);
        parent.add(mesh);
        return mesh;
      };

      const W = hall.roomWidth, D = hall.roomDepth, H = hall.roomHeight;
      const room = new THREE.Group();

      box(W, 0.3, D, M.floor, 0, -0.15, 0, room);
      box(W, 0.3, D, M.wall, 0, H, 0, room);
      box(0.3, H, D, M.wall, -W / 2, H / 2, 0, room);
      box(0.3, H, D, M.wall, W / 2, H / 2, 0, room);
      box(W, H, 0.3, M.wall, 0, H / 2, -D / 2, room);
      box(W, H, 0.3, M.wall, 0, H / 2, D / 2, room);

      const slats = Math.max(6, Math.round(D / 1.7));
      for (let i = 0; i < slats; i++) {
        const z = -D / 2 + 2.2 + i * ((D - 3.5) / slats);
        box(0.18, H * 0.5, 0.55, M.panel, -W / 2 + 0.3, H * 0.38, z, room);
        box(0.18, H * 0.5, 0.55, M.panel, W / 2 - 0.3, H * 0.38, z, room);
      }

      // Curved, concave towards the audience — the reason the far seats still fill the eye.
      const SW = hall.screenWidth, SH = hall.screenHeight, R = hall.screenCurveRadius;
      const screenY = Math.max(2.6, H * 0.46);
      const theta = SW / R;
      const screen = new THREE.Mesh(
        new THREE.CylinderGeometry(R, R, SH, 64, 1, true, Math.PI - theta / 2, theta), screenMat);
      screen.position.set(0, screenY, -D / 2 + 0.6 + R);
      room.add(screen);

      box(SW + 0.7, 0.35, 1.4, M.frame, 0, screenY + SH / 2 + 0.15, -D / 2 + 1.9, room);
      box(SW + 0.7, 0.35, 1.4, M.frame, 0, screenY - SH / 2 - 0.15, -D / 2 + 1.9, room);
      box(0.5, SH + 0.6, 1.4, M.frame, -SW / 2 - 0.25, screenY, -D / 2 + 1.9, room);
      box(0.5, SH + 0.6, 1.4, M.frame, SW / 2 + 0.25, screenY, -D / 2 + 1.9, room);
      box(W - 1.0, 1.2, 2.0, M.frame, 0, 0.6, -D / 2 + 1.4, room);

      const { rows, blockColumns: cols, seatWidth: SEAT_W, rowPitch: PITCH, rowRise: RISE, aisleWidth: aisle } = hall;
      const GAP = 0.06;
      const rowZ = (r: number) => -D / 2 + hall.firstRowDistance + r * PITCH;
      const rowY = (r: number) => 0.28 + r * RISE;

      const makeSeat = () => {
        const g = new THREE.Group();
        box(SEAT_W, 0.16, 0.62, M.seat, 0, 0.46, 0, g);
        const back = box(SEAT_W, 0.86, 0.16, M.seat, 0, 0.90, 0.28, g);
        back.rotation.x = 0.14;
        box(SEAT_W * 0.9, 0.14, 0.13, M.seat, 0, 0.50, -0.01, back);
        box(0.09, 0.12, 0.60, M.frame, -SEAT_W / 2 - 0.03, 0.60, -0.04, g);
        box(0.09, 0.12, 0.60, M.frame, SEAT_W / 2 + 0.03, 0.60, -0.04, g);
        box(SEAT_W * 0.8, 0.38, 0.12, M.frame, 0, 0.19, 0.10, g);
        return g;
      };

      for (let r = 0; r < rows; r++) {
        box(W - 0.8, RISE + 0.2, PITCH, M.floor, 0, rowY(r) - (RISE + 0.2) / 2, rowZ(r), room);
        box(W - 0.8, 0.06, 0.05, M.step, 0, rowY(r) - 0.05, rowZ(r) + PITCH / 2 - 0.02, room);
        box(aisle, 0.12, PITCH * 0.5, M.floor, 0, rowY(r) + 0.06, rowZ(r) - PITCH * 0.25, room);

        for (let side = -1; side <= 1; side += 2) {
          for (let c = 0; c < cols; c++) {
            const s = makeSeat();
            s.position.set(side * (aisle / 2 + 0.3 + c * (SEAT_W + GAP) + SEAT_W / 2), rowY(r), rowZ(r) - 0.12);
            room.add(s);
          }
        }
      }

      box(0.9, 0.32, 0.1, M.exit, -W / 2 + 0.5, 2.6, D / 2 - 0.45, room);
      box(0.9, 0.32, 0.1, M.exit, W / 2 - 0.5, 2.6, D / 2 - 0.45, room);
      box(2.2, 0.9, 0.12, M.frame, 0, H - 2.0, D / 2 - 0.4, room);

      for (let i = 0; i < 5; i++) {
        for (let side = -1; side <= 1; side += 2) {
          const light = new THREE.Mesh(new THREE.CylinderGeometry(0.16, 0.2, 0.12, 24), M.brass);
          light.position.set(side * (W / 2 - 1.3), H - 0.25, -D / 4 + i * (D / 7));
          room.add(light);
        }
      }

      const screenLight = new THREE.PointLight(0xbfd2e8, 140, 40, 2);
      screenLight.position.set(0, screenY, -D / 2 + 3.2);
      room.add(screenLight);
      const aisleGlow = new THREE.PointLight(0xff9c5a, 16, 30, 2);
      aisleGlow.position.set(0, 1.2, 4);
      room.add(aisleGlow);
      room.add(new THREE.AmbientLight(0x30262c, 1.6));

      scene.add(room);

      const renderer = new THREE.WebGLRenderer({ antialias: true });
      renderer.setPixelRatio(Math.min(window.devicePixelRatio, 2));
      renderer.setSize(mount.clientWidth, mount.clientHeight);
      mount.appendChild(renderer.domElement);

      const camera = new THREE.PerspectiveCamera(62, mount.clientWidth / mount.clientHeight, 0.05, 300);

      // Not orbit controls. You are sitting down: the head turns, the seat does not move.
      // Orbiting would let someone drift out of the seat and answer a different question
      // from the one they asked.
      let yaw = 0, pitch = 0;
      let dragging = false, lastX = 0, lastY = 0;
      const PITCH_LIMIT = 1.15;   // about 66 degrees up or down, roughly a neck's range

      const applyLook = () => {
        camera.rotation.order = "YXZ";
        camera.rotation.set(pitch, yaw, 0);
      };

      const onPointerDown = (e: PointerEvent) => {
        dragging = true; lastX = e.clientX; lastY = e.clientY;
        renderer.domElement.setPointerCapture(e.pointerId);
      };
      const onPointerMove = (e: PointerEvent) => {
        if (!dragging) return;
        yaw -= (e.clientX - lastX) * 0.0032;
        pitch = Math.max(-PITCH_LIMIT, Math.min(PITCH_LIMIT, pitch - (e.clientY - lastY) * 0.0032));
        lastX = e.clientX; lastY = e.clientY;
        applyLook();
      };
      const onPointerUp = (e: PointerEvent) => {
        dragging = false;
        renderer.domElement.releasePointerCapture(e.pointerId);
      };

      renderer.domElement.addEventListener("pointerdown", onPointerDown);
      renderer.domElement.addEventListener("pointermove", onPointerMove);
      renderer.domElement.addEventListener("pointerup", onPointerUp);
      renderer.domElement.addEventListener("pointercancel", onPointerUp);
      renderer.domElement.style.cursor = "grab";
      renderer.domElement.style.touchAction = "none";

      const EYE = 1.22;
      const screenCentre = new THREE.Vector3(0, screenY, -D / 2 + 0.6);

      // Seat numbering runs inward from the left wall, across the aisle, then outward again —
      // the same order the flat map uses, so the two agree about which seat is which.
      const seatX = (n: number) => {
        const c = n <= cols ? cols - n : n - cols - 1;
        const side = n <= cols ? -1 : 1;
        return side * (aisle / 2 + 0.3 + c * (SEAT_W + GAP) + SEAT_W / 2);
      };

      const sitAt = (rowNumber: number, seatNumber: number) => {
        const r0 = Math.min(Math.max(rowNumber - 1, 0), rows - 1);
        const n0 = Math.min(Math.max(seatNumber, 1), cols * 2);
        const x = seatX(n0), y = rowY(r0) + EYE, z = rowZ(r0) - 0.12;

        camera.position.set(x, y, z);

        // Start facing the screen, then let the head turn from there.
        const toScreen = screenCentre.clone().sub(camera.position);
        yaw = Math.atan2(-toScreen.x, -toScreen.z);
        pitch = Math.atan2(toScreen.y, Math.hypot(toScreen.x, toScreen.z));
        applyLook();

        // Only the seat is named. This used to grade it — "too close", "sharp angle" — and a
        // customer told their seat is a bad one tends not to buy any seat at all. The view
        // itself shows them what the seat is like; that is the honest part, and it is enough.
        setReadout(`${ROW_LABELS[r0]}${n0}`);
      };

      sitRef.current = sitAt;
      const first = seats[0] ?? { row: 1, seat: 1 };
      sitAt(first.row, first.seat);

      const onResize = () => {
        camera.aspect = mount.clientWidth / mount.clientHeight;
        camera.updateProjectionMatrix();
        renderer.setSize(mount.clientWidth, mount.clientHeight);
      };
      window.addEventListener("resize", onResize);

      let frame = 0;
      const start = performance.now();
      const loop = (now: number) => {
        frame = requestAnimationFrame(loop);
        const time = (now - start) / 1000;
        drawScreen(time);
        const flicker = 1.0 + Math.sin(time * 1.9) * 0.06;
        screenMat.emissiveIntensity = 1.5 * flicker;
        screenLight.intensity = 120 * flicker;
        renderer.render(scene, camera);
      };
      frame = requestAnimationFrame(loop);

      cleanup = () => {
        cancelAnimationFrame(frame);
        window.removeEventListener("resize", onResize);
        sitRef.current = null;
        renderer.domElement.removeEventListener("pointerdown", onPointerDown);
        renderer.domElement.removeEventListener("pointermove", onPointerMove);
        renderer.domElement.removeEventListener("pointerup", onPointerUp);
        renderer.domElement.removeEventListener("pointercancel", onPointerUp);
        // Geometries and materials are not garbage-collected on their own; leaving them
        // behind would leak a whole room every time the preview is opened.
        scene.traverse((object) => {
          const mesh = object as ThreeTypes.Mesh;
          mesh.geometry?.dispose?.();
          const material = mesh.material as ThreeTypes.Material | ThreeTypes.Material[] | undefined;
          if (Array.isArray(material)) material.forEach((m) => m.dispose());
          else material?.dispose?.();
        });
        screenTex.dispose();
        renderer.dispose();
        renderer.domElement.remove();
      };
    })().catch(() => setFailed(true));

    return () => { disposed = true; cleanup(); };
    // Deliberately not keyed on the chosen seat: moving between seats repositions the
    // camera, it does not rebuild the room.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [hall]);

  useEffect(() => {
    const seat = seats[index];
    if (seat) sitRef.current?.(seat.row, seat.seat);
  }, [index, seats]);

  return createPortal(
    <div
      data-force-dark
      className="fixed inset-0 z-[95] flex flex-col bg-black/90 backdrop-blur-sm"
      role="dialog"
      aria-modal="true"
      aria-label={t("view.title")}
    >
      <div className="flex flex-wrap items-center justify-between gap-3 border-b border-line px-4 py-3 sm:px-5">
        <div>
          <p className="font-display text-lg text-ink">{t("view.title")}</p>
          <p className="text-xs text-ink-mute">
            {hall ? `${hall.name}${hall.format ? ` · ${hall.format}` : ""}` : t("common.loading")}
          </p>
        </div>
        <div className="flex flex-wrap items-center gap-2 sm:gap-3">
          {seats.length > 1 ? (
            <div className="flex items-center gap-1.5">
              <Button
                size="sm"
                variant="outline"
                aria-label={t("common.prev")}
                onClick={() => setIndex((i) => (i - 1 + seats.length) % seats.length)}
              >
                <ChevronLeft size={15} aria-hidden />
              </Button>
              <span className="min-w-[5ch] text-center text-xs text-ink-mute">
                {index + 1} / {seats.length}
              </span>
              <Button
                size="sm"
                variant="outline"
                aria-label={t("common.next")}
                onClick={() => setIndex((i) => (i + 1) % seats.length)}
              >
                <ChevronRight size={15} aria-hidden />
              </Button>
            </div>
          ) : null}

          {readout ? <Badge tone="good">{readout}</Badge> : null}

          <Button size="sm" variant="outline" onClick={onClose}>
            <X size={15} aria-hidden />
            {t("common.cancel")}
          </Button>
        </div>
      </div>

      <div ref={mountRef} className="relative flex-1">
        {failed ? (
          <p className="absolute inset-0 flex items-center justify-center p-6 text-center text-sm text-ink-mute">
            {t("view.unavailable")}
          </p>
        ) : null}
      </div>

      <p className="border-t border-line px-5 py-2 text-xs text-ink-mute">
        {t("view.hint")}{seats.length > 1 ? ` · ${t("view.arrowHint")}` : ""}
      </p>
    </div>,
    document.body,
  );
}
