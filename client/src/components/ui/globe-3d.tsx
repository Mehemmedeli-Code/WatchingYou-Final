import { Suspense, useCallback, useMemo, useRef, useState } from "react";
import { Canvas, useFrame, useThree } from "@react-three/fiber";
import { OrbitControls, Html, useTexture } from "@react-three/drei";
import * as THREE from "three";
import { cn } from "@/lib/utils";

/**
 * Globe with city pins, adapted from the 21st.dev 3d-globe component.
 *
 * What changed, and why:
 *
 *  - `"use client"` removed; a Next.js marker, meaningless in a Vite build.
 *  - A pin is a **city**, not a person. The original drew one marker per entry, which is fine
 *    for thirteen capitals and fatal for a real membership: a million markers is a million
 *    draw calls and a frozen tab. Aggregating server-side means the payload grows with the
 *    number of cities — a few hundred — however many people are in them.
 *  - The face stack and the count are drawn in one `Html` node per city rather than one per
 *    person, so a city of a million costs exactly what a city of three costs.
 *  - Earth textures come from the app's own `public/` folder instead of a CDN: a map that
 *    silently turns into a grey ball when someone else's CDN moves is worse than no map.
 *  - Atmosphere and lighting kept; the colours follow the site's accent.
 */

export interface GlobeCityMarker {
  city: string;
  countryCode?: string | null;
  latitude: number;
  longitude: number;
  memberCount: number;
  faces: { userId: string; displayName: string; avatarUrl?: string | null }[];
}

/** Latitude and longitude onto the sphere. */
function toVector3(lat: number, lng: number, radius: number) {
  const phi = (90 - lat) * (Math.PI / 180);
  const theta = (lng + 180) * (Math.PI / 180);
  return new THREE.Vector3(
    -(radius * Math.sin(phi) * Math.cos(theta)),
    radius * Math.cos(phi),
    radius * Math.sin(phi) * Math.sin(theta),
  );
}

const RADIUS = 2;

function CityPin({
  marker,
  onOpen,
}: {
  marker: GlobeCityMarker;
  onOpen: (marker: GlobeCityMarker) => void;
}) {
  const [visible, setVisible] = useState(true);
  const [hovered, setHovered] = useState(false);
  const anchorRef = useRef<THREE.Group>(null);
  const { camera } = useThree();

  const surface = useMemo(() => toVector3(marker.latitude, marker.longitude, RADIUS * 1.001),
    [marker.latitude, marker.longitude]);
  const top = useMemo(() => toVector3(marker.latitude, marker.longitude, RADIUS * 1.2),
    [marker.latitude, marker.longitude]);

  const { centre, quaternion } = useMemo(() => {
    const direction = top.clone().sub(surface).normalize();
    const q = new THREE.Quaternion().setFromUnitVectors(new THREE.Vector3(0, 1, 0), direction);
    return { centre: surface.clone().lerp(top, 0.5), quaternion: q };
  }, [surface, top]);

  // Hide pins on the far side. Without this the back of the globe shows through and every
  // city is legible at once, which is unreadable and wrong.
  useFrame(() => {
    if (!anchorRef.current) return;
    const world = new THREE.Vector3();
    anchorRef.current.getWorldPosition(world);
    setVisible(world.clone().normalize().dot(camera.position.clone().normalize()) > 0.12);
  });

  const extra = marker.memberCount - marker.faces.length;

  return (
    <group visible={visible}>
      <mesh position={centre} quaternion={quaternion}>
        <cylinderGeometry args={[0.004, 0.004, top.distanceTo(surface), 8]} />
        <meshBasicMaterial color={hovered ? "#5BFFAB" : "#00A85A"} transparent opacity={0.8} />
      </mesh>

      <mesh position={surface} quaternion={quaternion}>
        <coneGeometry args={[0.02, 0.05, 8]} />
        <meshBasicMaterial color="#00E676" />
      </mesh>

      <group ref={anchorRef} position={top}>
        <Html transform sprite distanceFactor={9} style={{ pointerEvents: visible ? "auto" : "none" }}>
          <button
            type="button"
            onMouseEnter={() => setHovered(true)}
            onMouseLeave={() => setHovered(false)}
            onClick={() => onOpen(marker)}
            className={cn(
              "flex items-center gap-1 rounded-full border border-[#1F271F] bg-[#0A0C0A]/90 px-1 py-1 shadow-lg transition-transform",
              hovered && "scale-110 border-[#00E676]",
            )}
          >
            {/* Three faces and a count — the same cost whether the city holds three people
                or a million. */}
            <span className="flex -space-x-1.5">
              {marker.faces.map((face) =>
                face.avatarUrl ? (
                  <img
                    key={face.userId}
                    src={face.avatarUrl}
                    alt=""
                    className="h-3 w-3 rounded-full border border-[#0A0C0A] object-cover"
                    draggable={false}
                  />
                ) : (
                  <span
                    key={face.userId}
                    className="flex h-3 w-3 items-center justify-center rounded-full border border-[#0A0C0A] bg-[#2A3322] text-[4px] text-[#EDEDED]"
                  >
                    {face.displayName.slice(0, 1).toUpperCase()}
                  </span>
                ),
              )}
            </span>

            {extra > 0 ? (
              <span className="rounded-full bg-[#00E676] px-1 text-[5px] font-bold leading-[10px] text-[#0A0C0A]">
                +{extra > 999 ? `${Math.round(extra / 1000)}k` : extra}
              </span>
            ) : null}

            <span className="pr-0.5 text-[5px] font-medium text-[#EDEDED]">{marker.city}</span>
          </button>
        </Html>
      </group>
    </group>
  );
}

function Earth({ markers, onOpen }: { markers: GlobeCityMarker[]; onOpen: (m: GlobeCityMarker) => void }) {
  const [map, bump] = useTexture(["/globe/earth.png", "/globe/earth-bump.png"]);

  useMemo(() => {
    map.colorSpace = THREE.SRGBColorSpace;
    map.anisotropy = 8;
  }, [map]);

  return (
    <group>
      <mesh>
        <sphereGeometry args={[RADIUS, 64, 64]} />
        <meshStandardMaterial map={map} bumpMap={bump} bumpScale={0.04} roughness={0.75} metalness={0} />
      </mesh>

      {markers.map((marker) => (
        <CityPin key={`${marker.city}-${marker.latitude}`} marker={marker} onOpen={onOpen} />
      ))}
    </group>
  );
}

function Atmosphere() {
  const material = useMemo(
    () =>
      new THREE.ShaderMaterial({
        uniforms: { tint: { value: new THREE.Color("#00E676") } },
        vertexShader: `
          varying vec3 vNormal; varying vec3 vPosition;
          void main() {
            vNormal = normalize(normalMatrix * normal);
            vPosition = (modelViewMatrix * vec4(position, 1.0)).xyz;
            gl_Position = projectionMatrix * modelViewMatrix * vec4(position, 1.0);
          }`,
        fragmentShader: `
          uniform vec3 tint; varying vec3 vNormal; varying vec3 vPosition;
          void main() {
            float fresnel = pow(1.0 - abs(dot(vNormal, normalize(-vPosition))), 3.0);
            gl_FragColor = vec4(tint, fresnel * 0.5);
          }`,
        side: THREE.BackSide,
        transparent: true,
        depthWrite: false,
      }),
    [],
  );

  return (
    <mesh scale={[1.12, 1.12, 1.12]}>
      <sphereGeometry args={[RADIUS, 64, 32]} />
      <primitive object={material} attach="material" />
    </mesh>
  );
}

export function Globe3D({
  markers,
  onOpenCity,
  className,
}: {
  markers: GlobeCityMarker[];
  onOpenCity: (marker: GlobeCityMarker) => void;
  className?: string;
}) {
  const handleOpen = useCallback((marker: GlobeCityMarker) => onOpenCity(marker), [onOpenCity]);

  return (
    <div className={cn("relative h-[420px] w-full sm:h-[540px]", className)}>
      <Canvas
        gl={{ antialias: true, alpha: true, powerPreference: "high-performance" }}
        dpr={[1, 2]}
        camera={{ fov: 45, near: 0.1, far: 1000, position: [0, 0, RADIUS * 3.4] }}
        style={{ background: "transparent" }}
      >
        <Suspense fallback={<Html center><span className="text-sm text-[#9AA69A]">…</span></Html>}>
          <ambientLight intensity={0.7} />
          <directionalLight position={[10, 5, 10]} intensity={1.4} />
          <directionalLight position={[-6, 2, -4]} intensity={0.4} color="#88ccff" />

          <Earth markers={markers} onOpen={handleOpen} />
          <Atmosphere />

          <OrbitControls
            makeDefault
            enablePan={false}
            enableZoom
            minDistance={RADIUS * 2.2}
            maxDistance={RADIUS * 5}
            rotateSpeed={0.4}
            autoRotate
            autoRotateSpeed={0.25}
            enableDamping
            dampingFactor={0.1}
          />
        </Suspense>
      </Canvas>
    </div>
  );
}

export default Globe3D;
