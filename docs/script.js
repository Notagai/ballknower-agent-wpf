document.addEventListener("DOMContentLoaded",()=>{
const loader=document.querySelector(".loader");
if(sessionStorage.getItem("ballknower-home-seen")) loader?.remove();
else { sessionStorage.setItem("ballknower-home-seen","1"); }
const reduce=matchMedia("(prefers-reduced-motion: reduce)").matches;const glow=document.querySelector(".cursor-glow");if(glow&&!reduce){addEventListener("pointermove",e=>{glow.animate({left:e.clientX+"px",top:e.clientY+"px"},{duration:500,fill:"forwards"})})}
const reveals=document.querySelectorAll(".reveal");if(!reduce&&"IntersectionObserver"in window){const io=new IntersectionObserver(es=>es.forEach(e=>{if(e.isIntersecting){e.target.classList.add("is-visible");io.unobserve(e.target)}}),{threshold:.12});reveals.forEach(e=>io.observe(e))}else reveals.forEach(e=>e.classList.add("is-visible"));
const links=document.querySelectorAll('.docs-sidebar a[href^="#"]');const sections=[...links].map(a=>document.querySelector(a.getAttribute("href"))).filter(Boolean);if(sections.length&&"IntersectionObserver"in window){const io=new IntersectionObserver(es=>es.forEach(e=>{if(e.isIntersecting){links.forEach(a=>a.classList.toggle("section-active",a.getAttribute("href")==="#"+e.target.id))}}),{rootMargin:"-20% 0px -65% 0px"});sections.forEach(s=>io.observe(s))}
const toggle=document.querySelector(".nav-toggle"),nav=document.querySelector("#site-navigation");if(toggle&&nav){toggle.addEventListener("click",()=>{const open=toggle.getAttribute("aria-expanded")==="true";toggle.setAttribute("aria-expanded",String(!open));nav.classList.toggle("is-open",!open)});nav.querySelectorAll("a").forEach(a=>a.addEventListener("click",()=>{nav.classList.remove("is-open");toggle.setAttribute("aria-expanded","false")}))}
document.querySelectorAll('a[href$=".html"],a[href^="http"]').forEach(a=>{a.addEventListener("click",e=>{const u=new URL(a.href,location.href);if(u.origin!==location.origin||u.pathname===location.pathname||e.metaKey||e.ctrlKey||e.shiftKey||e.altKey)return;if(reduce)return;e.preventDefault();document.body.classList.add("page-leaving");setTimeout(()=>location.href=u.href,180)})});
const scenes=document.querySelectorAll(".scene-section");
const panels=document.querySelectorAll(".feature-panel");
let ticking=false;
function paintMotion(){
 if(reduce)return;
 const vh=innerHeight;
 scenes.forEach((s,i)=>{
  const r=s.getBoundingClientRect(),p=Math.max(-1,Math.min(1,(vh/2-(r.top+r.height/2))/vh));
  s.style.setProperty("--scene-progress",p);
  if(i===1){s.querySelector(".story-title")?.style.setProperty("transform",`translateY(${p*-35}px) scale(${1-Math.abs(p)*.04})`);s.querySelector(".story-copy")?.style.setProperty("transform",`translateY(${p*45}px)`)}
 });
 panels.forEach((panel,i)=>{
  const r=panel.getBoundingClientRect(),center=r.top+r.height/2,p=Math.max(-1,Math.min(1,(center-vh*.5)/(vh*.72))),d=Math.abs(p),dir=i%2? -1:1;
  panel.style.transform=`translate3d(${p*d*34}px,0,${-d*420}px) rotateY(${dir*p*112}deg) rotateZ(${p*dir*2.5}deg) scale(${1-d*.16})`;
  panel.style.opacity=String(.58+.42*(1-d)); panel.style.filter=`brightness(${.72+.28*(1-d)}) saturate(${.75+.25*(1-d)})`;
 });
 ticking=false;
}
addEventListener("scroll",()=>{if(!ticking){requestAnimationFrame(paintMotion);ticking=true}},{passive:true});addEventListener("resize",paintMotion);paintMotion();

const ball=document.querySelector(".ball-3d"),stage=document.querySelector(".ball-stage");
if(ball&&stage&&!reduce){
 let rx=-10,ry=-22,down=false,lastX=0,lastY=0;
 const render=()=>ball.style.transform=`rotateX(${rx}deg) rotateY(${ry}deg)`;
 stage.addEventListener("pointerdown",e=>{down=true;lastX=e.clientX;lastY=e.clientY;stage.setPointerCapture(e.pointerId);stage.classList.add("is-dragging")});
 stage.addEventListener("pointermove",e=>{if(!down)return;ry+=(e.clientX-lastX)*.42;rx-=(e.clientY-lastY)*.42;rx=Math.max(-75,Math.min(75,rx));lastX=e.clientX;lastY=e.clientY;render()});
 const release=()=>{down=false;stage.classList.remove("is-dragging")};
 stage.addEventListener("pointerup",release);stage.addEventListener("pointercancel",release);
 stage.addEventListener("wheel",e=>{e.preventDefault();ry+=e.deltaY*.18;render()},{passive:false});render();
}
const sound=document.querySelector(".sound-toggle");let ctx,master,timer;function startSound(){ctx=ctx||new (window.AudioContext||window.webkitAudioContext)();master=master||ctx.createGain();master.gain.value=.045;master.connect(ctx.destination);const o1=ctx.createOscillator(),o2=ctx.createOscillator(),g=ctx.createGain();o1.type="sine";o2.type="triangle";o1.frequency.value=55;o2.frequency.value=82.41;g.gain.value=.16;o1.connect(g);o2.connect(g);g.connect(master);o1.start();o2.start();timer=setInterval(()=>{o1.frequency.setTargetAtTime([55,65.41,73.42,61.74][Math.floor(Math.random()*4)],ctx.currentTime,.8);o2.frequency.setTargetAtTime([82.41,98,110][Math.floor(Math.random()*3)],ctx.currentTime,1.1)},2200);sound.classList.add("is-on");sound.setAttribute("aria-pressed","true");sound.querySelector(".sound-label").textContent="SOUND ON"}function stopSound(){if(ctx){master.gain.setTargetAtTime(0,ctx.currentTime,.25);setTimeout(()=>{clearInterval(timer);ctx.close();ctx=null},400)}sound.classList.remove("is-on");sound.setAttribute("aria-pressed","false");sound.querySelector(".sound-label").textContent="SOUND OFF"}sound?.addEventListener("click",()=>sound.classList.contains("is-on")?stopSound():startSound())});