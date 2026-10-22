import{U as m,t as e,y as n,z as c}from"./app.js";import{I as o}from"./input.js";/**
 * @license lucide-react v0.468.0 - ISC
 *
 * This source code is licensed under the ISC license.
 * See the LICENSE file in the root directory of this source tree.
 */const d=m("Search",[["circle",{cx:"11",cy:"11",r:"8",key:"4ej97u"}],["path",{d:"m21 21-4.3-4.3",key:"1qie3q"}]]);function u({children:a,className:r,active:s=!0,radius:t="rounded-md"}){return e.jsxs("div",{className:n("rr-beam relative",t,r),"data-active":s?"1":"0",children:[a,e.jsx("span",{className:"rr-beam-line","aria-hidden":!0}),e.jsx("span",{className:"rr-beam-glow","aria-hidden":!0})]})}function x({value:a,onChange:r,placeholder:s,className:t,autoFocus:i}){return e.jsx(u,{className:n("w-full",t),children:e.jsxs("div",{className:"relative",children:[e.jsx(d,{size:15,"aria-hidden":!0,className:"pointer-events-none absolute left-3 top-1/2 -translate-y-1/2 text-ink-mute"}),e.jsx(o,{className:"pl-9",value:a,onChange:l=>r(l.target.value),placeholder:s??c("common.search"),"aria-label":c("common.search"),autoFocus:i,type:"search"})]})})}export{x as S};
