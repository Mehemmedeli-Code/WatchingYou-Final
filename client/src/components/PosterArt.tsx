import { useId, useState, type ReactNode } from "react";

/**
 * Eight vector film posters for the scatter hero: six genres, a cinema hall and a film reel.
 *
 * All in the site's black and green, but fixed values rather than the theme tokens: tinted
 * with the theme they washed out to white in the light theme. A fixed palette looks the same
 * in both.
 *
 * Inline SVG, so the hero makes no image requests and stays sharp at any card size. The
 * important part of each picture sits in the middle, because the cards crop to different
 * shapes.
 */

/** Gradient and filter ids must be unique on the page; useId makes them so. */
function useKey() {
  return useId().replace(/:/g, "");
}

function Frame({ children, id }: { children: ReactNode; id: string }) {
  return (
    <svg viewBox="0 0 100 140" preserveAspectRatio="xMidYMid slice" className="h-full w-full" aria-hidden="true">
      <defs>
        {/* A soft vignette over every poster: it is what makes flat shapes read as printed art. */}
        <radialGradient id={`${id}-vig`} cx="50%" cy="45%" r="75%">
          <stop offset="60%" stopColor="#000" stopOpacity="0" />
          <stop offset="100%" stopColor="#000" stopOpacity="0.55" />
        </radialGradient>
      </defs>
      {children}
      <rect width="100" height="140" fill={`url(#${id}-vig)`} />
      <rect x="2.5" y="2.5" width="95" height="135" fill="none" stroke="#D6FFE8" strokeOpacity="0.14" strokeWidth="0.5" />
    </svg>
  );
}

// ------------------------------------------------------------------ noir: rain, a streetlamp, a hat

const Noir = () => {
  const id = useKey();
  return (
    <Frame id={id}>
      <defs>
        <linearGradient id={`${id}-sky`} x1="0" y1="0" x2="0" y2="1">
          <stop offset="0%" stopColor="#07180F" />
          <stop offset="100%" stopColor="#092617" />
        </linearGradient>
        <radialGradient id={`${id}-cone`} cx="62%" cy="30%" r="70%">
          <stop offset="0%" stopColor="#53FDA6" stopOpacity="0.75" />
          <stop offset="100%" stopColor="#53FDA6" stopOpacity="0" />
        </radialGradient>
      </defs>
      <rect width="100" height="140" fill={`url(#${id}-sky)`} />
      {/* skyline */}
      <path d="M0 78h8v-18h6v10h7v-26h9v20h6v-12h8v28h7v-34h10v22h7v-14h8v24h8v-6h6V140H0Z" fill="#06140D" />
      {[12, 26, 44, 64, 80].map((x, i) => (
        <rect key={x} x={x} y={62 + (i % 3) * 6} width="2" height="2.5" fill="#53FDA6" opacity="0.8" />
      ))}
      {/* the lamp and its light */}
      <path d="M62 44 L38 140 H96 Z" fill={`url(#${id}-cone)`} />
      <rect x="61" y="40" width="1.6" height="100" fill="#06120C" />
      <path d="M57 40h10l-2 5h-6Z" fill="#06120C" />
      <circle cx="62" cy="45" r="2.2" fill="#85FFC0" />
      {/* the figure in the coat and hat */}
      <path d="M44 138 L46 102 Q46 94 51 92 L56 92 Q61 94 61 102 L63 138 Z" fill="#06120C" />
      <circle cx="53.5" cy="86" r="5" fill="#06120C" />
      <ellipse cx="53.5" cy="82" rx="9" ry="1.8" fill="#06120C" />
      <path d="M48.5 82 Q48.5 75 53.5 75 Q58.5 75 58.5 82Z" fill="#06120C" />
      {/* rain */}
      {Array.from({ length: 34 }, (_, i) => {
        const x = (i * 29) % 100;
        const y = (i * 47) % 130;
        return <line key={i} x1={x} y1={y} x2={x - 2} y2={y + 8} stroke="#06E879" strokeWidth="0.35" opacity="0.35" />;
      })}
      <rect y="132" width="100" height="8" fill="#53FDA6" opacity="0.08" />
    </Frame>
  );
};

// ------------------------------------------------------------------ western: sunset, mesas, a rider

const Western = () => {
  const id = useKey();
  return (
    <Frame id={id}>
      <defs>
        <linearGradient id={`${id}-sky`} x1="0" y1="0" x2="0" y2="1">
          <stop offset="0%" stopColor="#092616" />
          <stop offset="45%" stopColor="#05A454" />
          <stop offset="75%" stopColor="#00DE72" />
          <stop offset="100%" stopColor="#4BFBA2" />
        </linearGradient>
        <linearGradient id={`${id}-sun`} x1="0" y1="0" x2="0" y2="1">
          <stop offset="0%" stopColor="#A1FFCE" />
          <stop offset="100%" stopColor="#0DEA7D" />
        </linearGradient>
        <clipPath id={`${id}-bands`}>
          <rect x="0" y="0" width="100" height="62" />
          {[64, 69, 73.5, 77.5].map((y, i) => <rect key={y} x="0" y={y} width="100" height={3.2 - i * 0.6} />)}
        </clipPath>
      </defs>
      <rect width="100" height="140" fill={`url(#${id}-sky)`} />
      <circle cx="50" cy="66" r="26" fill={`url(#${id}-sun)`} clipPath={`url(#${id}-bands)`} />
      <path d="M0 84 L10 84 L14 70 L30 70 L34 84 L58 84 L60 76 L78 76 L82 84 L100 84 V140 H0Z" fill="#0A2C1A" />
      <path d="M0 96 Q30 88 60 96 T100 94 V140 H0Z" fill="#081A10" />
      {/* cactus */}
      <path d="M16 118 V96 q0-3 3-3 q3 0 3 3 V118 M16 106 h-4 v-6 q0-2 2-2 M22 102 h4 v-7 q0-2-2-2" stroke="#06120C" strokeWidth="3.4" fill="none" strokeLinecap="round" />
      {/* horse and rider */}
      <path d="M58 104 q6-4 14-3 l6-5 2 2 -3 5 q2 3 0 6 l-1 10 h-2 l0-8 h-12 l-2 8 h-2 l1-10 q-3-2-1-5Z" fill="#06120C" />
      <path d="M65 101 l1-9 q0-3 3-3 h1 q2 0 2 3 l0 8Z" fill="#06120C" />
      <ellipse cx="69" cy="87.5" rx="5" ry="1.2" fill="#06120C" />
      <path d="M66.5 87.5 q0-4 2.5-4 q2.5 0 2.5 4Z" fill="#06120C" />
    </Frame>
  );
};

// ------------------------------------------------------------------ sci-fi: a ringed planet and a ship

const SciFi = () => {
  const id = useKey();
  return (
    <Frame id={id}>
      <defs>
        <radialGradient id={`${id}-space`} cx="50%" cy="30%" r="90%">
          <stop offset="0%" stopColor="#092516" />
          <stop offset="60%" stopColor="#07150D" />
          <stop offset="100%" stopColor="#05100A" />
        </radialGradient>
        <linearGradient id={`${id}-planet`} x1="0" y1="0" x2="1" y2="1">
          <stop offset="0%" stopColor="#45F99E" />
          <stop offset="50%" stopColor="#069F52" />
          <stop offset="100%" stopColor="#092415" />
        </linearGradient>
        <linearGradient id={`${id}-ring`} x1="0" y1="0" x2="1" y2="0">
          <stop offset="0%" stopColor="#6AFFB3" stopOpacity="0" />
          <stop offset="50%" stopColor="#6AFFB3" stopOpacity="0.9" />
          <stop offset="100%" stopColor="#6AFFB3" stopOpacity="0" />
        </linearGradient>
      </defs>
      <rect width="100" height="140" fill={`url(#${id}-space)`} />
      {Array.from({ length: 46 }, (_, i) => (
        <circle key={i} cx={(i * 37) % 100} cy={(i * 61) % 140} r={i % 7 === 0 ? 0.7 : 0.35} fill="#D6FFE8" opacity={0.35 + (i % 5) * 0.12} />
      ))}
      {/* back half of the ring, then the planet, then the front half */}
      <ellipse cx="50" cy="60" rx="44" ry="9" fill="none" stroke={`url(#${id}-ring)`} strokeWidth="2.2" transform="rotate(-14 50 60)" />
      <circle cx="50" cy="60" r="25" fill={`url(#${id}-planet)`} />
      <path d="M28 54 q22 -6 44 2 M26 64 q24 -5 48 3" stroke="#D6FFE8" strokeOpacity="0.18" strokeWidth="1.4" fill="none" />
      <path d="M6 71 A44 9 -14 0 0 94 49" transform="rotate(0)" fill="none" stroke={`url(#${id}-ring)`} strokeWidth="2.2" />
      <circle cx="80" cy="28" r="5" fill="#22EF8A" />
      <circle cx="78.4" cy="26.6" r="5" fill="#07150D" opacity="0.55" />
      {/* the ship, with its exhaust trail */}
      <path d="M8 118 L40 104" stroke="#45F99E" strokeWidth="1.2" opacity="0.5" strokeLinecap="round" />
      <path d="M40 104 l12 -6 -3 7 5 2 -12 2 Z" fill="#AEFFD4" />
      <circle cx="47" cy="103" r="1" fill="#45F99E" />
    </Frame>
  );
};

// ------------------------------------------------------------------ romance: two figures, a moon, lanterns

const Romance = () => {
  const id = useKey();
  return (
    <Frame id={id}>
      <defs>
        <linearGradient id={`${id}-sky`} x1="0" y1="0" x2="0" y2="1">
          <stop offset="0%" stopColor="#082215" />
          <stop offset="50%" stopColor="#088F4A" />
          <stop offset="100%" stopColor="#13EB81" />
        </linearGradient>
        <radialGradient id={`${id}-moon`} cx="50%" cy="50%" r="50%">
          <stop offset="0%" stopColor="#B8FFD9" />
          <stop offset="70%" stopColor="#76FFB9" />
          <stop offset="100%" stopColor="#76FFB9" stopOpacity="0" />
        </radialGradient>
      </defs>
      <rect width="100" height="140" fill={`url(#${id}-sky)`} />
      <circle cx="50" cy="52" r="30" fill={`url(#${id}-moon)`} opacity="0.95" />
      {/* lanterns drifting up */}
      {[[18, 30], [80, 22], [70, 44], [26, 58], [88, 70], [10, 80]].map(([x, y], i) => (
        <g key={i} opacity={0.7 + (i % 2) * 0.2}>
          <rect x={x - 1.6} y={y - 2.2} width="3.2" height="4.4" rx="0.8" fill="#16EC83" />
          <circle cx={x} cy={y} r="3.4" fill="#16EC83" opacity="0.25" />
        </g>
      ))}
      {/* the bridge */}
      <path d="M0 104 Q50 84 100 104 V140 H0Z" fill="#081D12" />
      <path d="M0 104 Q50 84 100 104" stroke="#092617" strokeWidth="2" fill="none" />
      {[14, 28, 42, 58, 72, 86].map((x) => <line key={x} x1={x} y1={100 - Math.sin((x / 100) * Math.PI) * 17} x2={x} y2={96 - Math.sin((x / 100) * Math.PI) * 17} stroke="#092617" strokeWidth="1.4" />)}
      {/* the couple, leaning together */}
      <path d="M40 96 l1-16 q0-5 4-5 q4 0 4 5 l1 14Z" fill="#07160E" />
      <circle cx="45" cy="70" r="4" fill="#07160E" />
      <path d="M52 94 l0-15 q0-5 4-5 q4 0 4 5 l2 13 q-5 3-10 2Z" fill="#07160E" />
      <circle cx="56.5" cy="69" r="3.8" fill="#07160E" />
      <path d="M58 66 q6 4 5 14" stroke="#07160E" strokeWidth="2.4" fill="none" strokeLinecap="round" />
      <path d="M48 78 q2 -2 6 0" stroke="#07160E" strokeWidth="2" fill="none" />
    </Frame>
  );
};

// ------------------------------------------------------------------ horror: red moon, a house on the hill

const Horror = () => {
  const id = useKey();
  return (
    <Frame id={id}>
      <defs>
        <linearGradient id={`${id}-sky`} x1="0" y1="0" x2="0" y2="1">
          <stop offset="0%" stopColor="#06120C" />
          <stop offset="60%" stopColor="#081E12" />
          <stop offset="100%" stopColor="#06120C" />
        </linearGradient>
        <radialGradient id={`${id}-moon`} cx="45%" cy="40%" r="60%">
          <stop offset="0%" stopColor="#00CD69" />
          <stop offset="100%" stopColor="#0A4727" />
        </radialGradient>
        <linearGradient id={`${id}-fog`} x1="0" y1="0" x2="0" y2="1">
          <stop offset="0%" stopColor="#00E576" stopOpacity="0" />
          <stop offset="100%" stopColor="#00E576" stopOpacity="0.35" />
        </linearGradient>
      </defs>
      <rect width="100" height="140" fill={`url(#${id}-sky)`} />
      <circle cx="58" cy="44" r="24" fill={`url(#${id}-moon)`} />
      <circle cx="58" cy="44" r="30" fill="#02B75E" opacity="0.08" />
      {/* bats */}
      {[[24, 30, 1], [34, 22, 0.7], [84, 70, 0.8]].map(([x, y, s], i) => (
        <path key={i} transform={`translate(${x} ${y}) scale(${s})`} d="M0 0 q-3 -3 -7 -1 q2 1 2 3 q2 -1 5 1 q3 -2 5 -1 q0 -2 2 -3 q-4 -2 -7 1Z" fill="#050F0A" />
      ))}
      {/* the hill and the house */}
      <path d="M0 96 Q40 72 100 90 V140 H0Z" fill="#050F0A" />
      <path d="M48 80 v-16 l8 -8 8 8 v16Z M58 64 v-10 h3 v7Z" fill="#050F0A" />
      <rect x="53" y="68" width="3" height="4" fill="#11EB80" />
      <rect x="59" y="68" width="3" height="4" fill="#11EB80" opacity="0.5" />
      {/* dead trees */}
      <path d="M14 110 V80 M14 92 l-7 -7 M14 86 l6 -8 M20 78 l3 -3 M7 85 l-3 1" stroke="#050F0A" strokeWidth="2.2" fill="none" strokeLinecap="round" />
      <path d="M86 116 V88 M86 98 l7 -8 M86 94 l-6 -6" stroke="#050F0A" strokeWidth="2.2" fill="none" strokeLinecap="round" />
      <rect y="96" width="100" height="44" fill={`url(#${id}-fog)`} />
    </Frame>
  );
};

// ------------------------------------------------------------------ the reel: a 35mm reel, film strip and clapperboard

const Reel = () => {
  const id = useKey();
  const spokes = [0, 60, 120, 180, 240, 300];
  return (
    <Frame id={id}>
      <defs>
        <radialGradient id={`${id}-bg`} cx="50%" cy="40%" r="80%">
          <stop offset="0%" stopColor="#0A3A20" />
          <stop offset="100%" stopColor="#030705" />
        </radialGradient>
        <linearGradient id={`${id}-metal`} x1="0" y1="0" x2="1" y2="1">
          <stop offset="0%" stopColor="#5BFFAB" />
          <stop offset="50%" stopColor="#00C766" />
          <stop offset="100%" stopColor="#0B7A3F" />
        </linearGradient>
      </defs>
      <rect width="100" height="140" fill={`url(#${id}-bg)`} />
      {/* film strip running diagonally behind */}
      <g transform="rotate(-24 50 70)">
        <rect x="-20" y="86" width="140" height="18" fill="#06140D" stroke="#0B7A3F" strokeWidth="0.5" />
        {Array.from({ length: 16 }, (_, i) => (
          <g key={i}>
            <rect x={-18 + i * 9} y="87.5" width="3.2" height="2.4" rx="0.5" fill="#00C766" opacity="0.8" />
            <rect x={-18 + i * 9} y="100.1" width="3.2" height="2.4" rx="0.5" fill="#00C766" opacity="0.8" />
          </g>
        ))}
        {[0, 1, 2, 3, 4, 5, 6].map((i) => (
          <rect key={i} x={-16 + i * 20} y="91" width="16" height="8" fill="#0A3A20" stroke="#00C766" strokeOpacity="0.35" strokeWidth="0.3" />
        ))}
      </g>
      {/* the reel */}
      <circle cx="50" cy="52" r="27" fill={`url(#${id}-metal)`} />
      <circle cx="50" cy="52" r="24" fill="#06140D" />
      <circle cx="50" cy="52" r="21" fill="none" stroke="#0B7A3F" strokeWidth="0.6" />
      {spokes.map((a) => {
        const r = (a * Math.PI) / 180;
        return <circle key={a} cx={50 + Math.cos(r) * 13} cy={52 + Math.sin(r) * 13} r="6" fill={`url(#${id}-metal)`} opacity="0.95" />;
      })}
      {spokes.map((a) => {
        const r = (a * Math.PI) / 180;
        return <circle key={`h${a}`} cx={50 + Math.cos(r) * 13} cy={52 + Math.sin(r) * 13} r="4.4" fill="#030705" />;
      })}
      <circle cx="50" cy="52" r="5" fill={`url(#${id}-metal)`} />
      <circle cx="50" cy="52" r="1.8" fill="#030705" />
      {/* the clapperboard */}
      <g transform="translate(20 104)">
        <rect x="0" y="6" width="44" height="26" rx="1.5" fill="#06140D" stroke="#00C766" strokeWidth="0.8" />
        <path d="M0 6 L44 6 L44 0.5 L0 0.5 Z" fill="#D6FFE8" transform="rotate(-12 0 6)" />
        {[0, 1, 2, 3, 4].map((i) => (
          <path key={i} d={`M${2 + i * 9} 0.5 l6 0 -3 5.5 -6 0Z`} fill="#06140D" transform="rotate(-12 0 6)" />
        ))}
        {[0, 1, 2, 3, 4].map((i) => (
          <path key={`b${i}`} d={`M${2 + i * 9} 6.5 l6 0 -3 5 -6 0Z`} fill="#D6FFE8" />
        ))}
        <line x1="4" y1="20" x2="40" y2="20" stroke="#0B7A3F" strokeWidth="0.5" />
        <line x1="4" y1="26" x2="40" y2="26" stroke="#0B7A3F" strokeWidth="0.5" />
        <line x1="22" y1="14" x2="22" y2="30" stroke="#0B7A3F" strokeWidth="0.5" />
      </g>
    </Frame>
  );
};

// ------------------------------------------------------------------ the hall: an audience, a projector beam, a screen

const Hall = () => {
  const id = useKey();
  return (
    <Frame id={id}>
      <defs>
        <linearGradient id={`${id}-room`} x1="0" y1="0" x2="0" y2="1">
          <stop offset="0%" stopColor="#030705" />
          <stop offset="100%" stopColor="#07160E" />
        </linearGradient>
        <linearGradient id={`${id}-screen`} x1="0" y1="0" x2="0" y2="1">
          <stop offset="0%" stopColor="#D6FFE8" />
          <stop offset="100%" stopColor="#5BFFAB" />
        </linearGradient>
        <linearGradient id={`${id}-beam`} x1="1" y1="1" x2="0" y2="0">
          <stop offset="0%" stopColor="#5BFFAB" stopOpacity="0.55" />
          <stop offset="100%" stopColor="#5BFFAB" stopOpacity="0.04" />
        </linearGradient>
        <radialGradient id={`${id}-spill`} cx="50%" cy="30%" r="60%">
          <stop offset="0%" stopColor="#00E676" stopOpacity="0.28" />
          <stop offset="100%" stopColor="#00E676" stopOpacity="0" />
        </radialGradient>
      </defs>
      <rect width="100" height="140" fill={`url(#${id}-room)`} />
      <rect width="100" height="140" fill={`url(#${id}-spill)`} />
      {/* the screen, slightly curved, with a scene on it */}
      <path d="M12 24 Q50 20 88 24 L88 62 Q50 58 12 62 Z" fill={`url(#${id}-screen)`} />
      <path d="M12 52 L30 40 L42 48 L58 34 L88 54 L88 62 Q50 58 12 62Z" fill="#00C766" opacity="0.55" />
      <circle cx="70" cy="33" r="5" fill="#FFFFFF" opacity="0.9" />
      {/* the projector beam from the back of the room */}
      <path d="M92 118 L12 24 L88 24 Z" fill={`url(#${id}-beam)`} />
      {Array.from({ length: 14 }, (_, i) => (
        <circle key={i} cx={40 + ((i * 23) % 46)} cy={40 + ((i * 31) % 60)} r="0.35" fill="#D6FFE8" opacity="0.6" />
      ))}
      {/* rows of heads and seat backs, darkest at the front */}
      {[{ y: 88, n: 7, r: 4.2, c: "#0A3A20" }, { y: 102, n: 6, r: 5, c: "#07220F" }, { y: 118, n: 5, r: 6, c: "#040C07" }].map((row, k) => (
        <g key={k}>
          <rect x="0" y={row.y + row.r * 0.8} width="100" height={140 - row.y} fill={row.c} />
          {Array.from({ length: row.n }, (_, i) => {
            const x = ((i + 0.5) * 100) / row.n + (k % 2 ? 4 : 0);
            return (
              <g key={i}>
                <path d={`M${x - row.r * 1.5} ${row.y + row.r * 2.4} Q${x - row.r * 1.4} ${row.y + row.r * 0.6} ${x} ${row.y + row.r * 0.5} Q${x + row.r * 1.4} ${row.y + row.r * 0.6} ${x + row.r * 1.5} ${row.y + row.r * 2.4} Z`} fill={row.c} />
                <circle cx={x} cy={row.y} r={row.r} fill={row.c} />
              </g>
            );
          })}
        </g>
      ))}
      {/* the rim of light the screen throws on the heads nearest it */}
      <path d="M0 86 H100" stroke="#00E676" strokeOpacity="0.25" strokeWidth="0.6" />
    </Frame>
  );
};

// ------------------------------------------------------------------ musical: a stage, spotlights, a dancer

const Musical = () => {
  const id = useKey();
  return (
    <Frame id={id}>
      <defs>
        <linearGradient id={`${id}-back`} x1="0" y1="0" x2="0" y2="1">
          <stop offset="0%" stopColor="#07170E" />
          <stop offset="100%" stopColor="#082114" />
        </linearGradient>
        <linearGradient id={`${id}-spot`} x1="0" y1="0" x2="0" y2="1">
          <stop offset="0%" stopColor="#ABFFD3" stopOpacity="0.9" />
          <stop offset="100%" stopColor="#ABFFD3" stopOpacity="0.05" />
        </linearGradient>
        <linearGradient id={`${id}-curtain`} x1="0" y1="0" x2="1" y2="0">
          <stop offset="0%" stopColor="#092817" />
          <stop offset="50%" stopColor="#0B6A37" />
          <stop offset="100%" stopColor="#092817" />
        </linearGradient>
      </defs>
      <rect width="100" height="140" fill={`url(#${id}-back)`} />
      {/* spotlights */}
      <path d="M22 0 L36 0 L66 112 L30 112 Z" fill={`url(#${id}-spot)`} opacity="0.55" />
      <path d="M64 0 L78 0 L70 112 L34 112 Z" fill={`url(#${id}-spot)`} opacity="0.45" />
      <ellipse cx="50" cy="112" rx="22" ry="4" fill="#ABFFD3" opacity="0.45" />
      {/* the stage */}
      <path d="M0 112 H100 V140 H0Z" fill="#07190F" />
      <path d="M0 112 H100" stroke="#00DE72" strokeWidth="1" />
      {/* curtains, with a pelmet */}
      <path d="M0 0 H18 Q14 60 20 140 H0Z" fill={`url(#${id}-curtain)`} />
      <path d="M100 0 H82 Q86 60 80 140 H100Z" fill={`url(#${id}-curtain)`} />
      <path d="M0 0 H100 V10 Q75 16 50 10 Q25 16 0 10Z" fill="#0A4727" />
      <path d="M0 10 Q25 16 50 10 Q75 16 100 10" stroke="#00DE72" strokeWidth="0.8" fill="none" />
      {/* the dancer, mid-spin */}
      <circle cx="50" cy="72" r="3.6" fill="#06120B" />
      <path d="M50 76 L47 92 L54 92 Z" fill="#06120B" />
      <path d="M40 106 Q50 88 60 106 Q50 102 40 106Z" fill="#06120B" />
      <path d="M49 79 L38 70 M51 79 L62 72" stroke="#06120B" strokeWidth="1.8" strokeLinecap="round" />
      <path d="M48 104 L45 112 M52 104 L57 111" stroke="#06120B" strokeWidth="1.8" strokeLinecap="round" />
      {/* sparkles */}
      {[[30, 40], [70, 34], [62, 58], [36, 60], [80, 86]].map(([x, y], i) => (
        <path key={i} d={`M${x} ${y - 2.4} L${x + 0.6} ${y - 0.6} L${x + 2.4} ${y} L${x + 0.6} ${y + 0.6} L${x} ${y + 2.4} L${x - 0.6} ${y + 0.6} L${x - 2.4} ${y} L${x - 0.6} ${y - 0.6}Z`} fill="#89FFC2" opacity="0.85" />
      ))}
    </Frame>
  );
};

// ------------------------------------------------------------------ photographs, in the site's duotone

/**
 * A real cinema photograph, toned black and green.
 *
 * Drawn posters, however detailed, read as illustration; a film site's front door wants the
 * real thing — a projector beam, a clapperboard, an audience in the dark. The photos come from
 * Unsplash (free to use under the Unsplash License). Each is turned grey and multiplied over a
 * green gradient, so the shadows stay black and the highlights turn the site's green, and eight
 * different photographs look like one set.
 *
 * The drawn poster sits underneath. If the photo cannot load — offline, or the host is down —
 * the card still shows a picture rather than an empty green box.
 */
function Photo({ id, alt, fallback }: { id: string; alt: string; fallback: ReactNode }) {
  const [failed, setFailed] = useState(false);
  const src = `https://images.unsplash.com/photo-${id}?w=700&q=70&auto=format&fit=crop`;

  return (
    <div className="relative h-full w-full overflow-hidden bg-[#030705]">
      <div className="absolute inset-0">{fallback}</div>
      {failed ? null : (
        <div className="absolute inset-0 bg-[linear-gradient(160deg,#5BFFAB_0%,#00C766_45%,#0B3D22_100%)]">
          <img
            src={src}
            alt={alt}
            loading="lazy"
            decoding="async"
            draggable={false}
            referrerPolicy="no-referrer"
            onError={() => setFailed(true)}
            className="h-full w-full object-cover mix-blend-multiply [filter:grayscale(1)_contrast(1.25)_brightness(0.95)]"
          />
          {/* depth: a darker floor and a soft vignette, the way a lit frame falls off */}
          <div className="absolute inset-0 bg-[radial-gradient(ellipse_at_50%_40%,transparent_55%,rgba(0,0,0,0.6)_100%)]" />
          <div className="absolute inset-x-0 bottom-0 h-1/3 bg-gradient-to-t from-black/60 to-transparent" />
        </div>
      )}
    </div>
  );
}

// Card order follows the scatter in stack-spread.tsx: small top-left, tall top-right, tall
// left, wide top-centre, tall right, wide bottom-left, bottom-centre, small bottom-right.
export const POSTERS: { key: string; alt: string; render: () => ReactNode }[] = [
  { key: "neon", alt: "Cinema neon sign", render: () => <Photo id="1674473191471-126ceaf72e95" alt="Cinema neon sign" fallback={<Musical />} /> },
  { key: "beam", alt: "Projector beam in a dark hall", render: () => <Photo id="1478720568477-152d9b164e26" alt="Projector beam in a dark hall" fallback={<Hall />} /> },
  { key: "clapper", alt: "Clapperboard on set", render: () => <Photo id="1485846234645-a62644f84728" alt="Clapperboard on set" fallback={<Reel />} /> },
  { key: "audience", alt: "Audience facing the screen", render: () => <Photo id="1485095329183-d0797cdc5676" alt="Audience facing the screen" fallback={<Hall />} /> },
  { key: "camera", alt: "Cinema camera", render: () => <Photo id="1574304904744-2e6919c5a2a9" alt="Cinema camera" fallback={<Noir />} /> },
  { key: "theatre", alt: "Cinema auditorium", render: () => <Photo id="1561722798-9a732d141027" alt="Cinema auditorium" fallback={<SciFi />} /> },
  { key: "reels", alt: "Film reels", render: () => <Photo id="1440404653325-ab127d49abc1" alt="Film reels" fallback={<Reel />} /> },
  { key: "projector", alt: "Film projector", render: () => <Photo id="1568876694728-451bbf694b83" alt="Film projector" fallback={<Western />} /> },
];

/** The drawn posters on their own, for anywhere that must not depend on a remote image. */
export const DRAWN_POSTERS: { key: string; alt: string; render: () => ReactNode }[] = [
  { key: "musical", alt: "Musical poster", render: () => <Musical /> },
  { key: "hall", alt: "Cinema hall", render: () => <Hall /> },
  { key: "horror", alt: "Horror poster", render: () => <Horror /> },
  { key: "reel", alt: "Film reel and clapperboard", render: () => <Reel /> },
  { key: "romance", alt: "Romance poster", render: () => <Romance /> },
  { key: "scifi", alt: "Science fiction poster", render: () => <SciFi /> },
  { key: "western", alt: "Western poster", render: () => <Western /> },
  { key: "noir", alt: "Film noir poster", render: () => <Noir /> },
];
