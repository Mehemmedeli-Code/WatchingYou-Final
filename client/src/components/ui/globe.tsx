/**
 * A rotating Earth, done entirely in CSS: one image scrolled sideways behind a circular mask,
 * with inset shadows doing the work of a sphere.
 *
 * Two changes from the original. `h-screen` is gone — inside a page section it pushed
 * everything else off the screen; the caller now decides the height. And the size is a prop,
 * because the globe sits beside a panel here rather than alone on a page.
 *
 * It is a disc, not a sphere: there is no projection to place a pin against, which is why the
 * cities are a list next to it rather than markers on it.
 */
export function Globe({ size = 250, className }: { size?: number; className?: string }) {
  return (
    <>
      <style>
        {`
          @keyframes earthRotate {
            0% { background-position: 0 0; }
            100% { background-position: 400px 0; }
          }
          @keyframes twinkling { 0%,100% { opacity:0.1; } 50% { opacity:1; } }
          @keyframes twinkling-slow { 0%,100% { opacity:0.1; } 50% { opacity:1; } }
          @keyframes twinkling-long { 0%,100% { opacity:0.1; } 50% { opacity:1; } }
          @keyframes twinkling-fast { 0%,100% { opacity:0.1; } 50% { opacity:1; } }
        `}
      </style>

      <div className={className}>
        <div
          className="relative rounded-full overflow-hidden shadow-[0_0_20px_rgba(255,255,255,0.2),-5px_0_8px_#c3f4ff_inset,15px_2px_25px_#000_inset,-24px_-2px_34px_#c3f4ff99_inset,250px_0_44px_#00000066_inset,150px_0_38px_#000000aa_inset]"
          style={{
            width: size,
            height: size,
            backgroundImage:
              "url('https://cdn.21st.dev/assets/mirror/f2/f2fe23d0c6a8406962e4c5ef969e13dc9de3faf37d3e7258a1067173325b254f.jpg')",
            backgroundSize: "cover",
            backgroundPosition: "left",
            animation: "earthRotate 30s linear infinite",
          }}
        >
          {/* Stars */}
          <div className="absolute left-[-20px] w-1 h-1 bg-white rounded-full" style={{ animation: "twinkling 3s infinite" }} />
          <div className="absolute left-[-40px] top-[30px] w-1 h-1 bg-white rounded-full" style={{ animation: "twinkling-slow 2s infinite" }} />
          <div className="absolute left-[350px] top-[90px] w-1 h-1 bg-white rounded-full" style={{ animation: "twinkling-long 4s infinite" }} />
          <div className="absolute left-[200px] top-[290px] w-1 h-1 bg-white rounded-full" style={{ animation: "twinkling 3s infinite" }} />
          <div className="absolute left-[50px] top-[270px] w-1 h-1 bg-white rounded-full" style={{ animation: "twinkling-fast 1.5s infinite" }} />
          <div className="absolute left-[250px] top-[-50px] w-1 h-1 bg-white rounded-full" style={{ animation: "twinkling-long 4s infinite" }} />
          <div className="absolute left-[290px] top-[60px] w-1 h-1 bg-white rounded-full" style={{ animation: "twinkling-slow 2s infinite" }} />
        </div>
      </div>
    </>
  );
}

export default Globe;
