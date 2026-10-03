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
const panels=[...document.querySelectorAll(".feature-panel")];
let ticking=false;
function paintMotion(){
 if(reduce)return;
 const vh=innerHeight;
 scenes.forEach((scene,i)=>{
  const r=scene.getBoundingClientRect(),p=Math.max(-1,Math.min(1,(vh/2-(r.top+r.height/2))/vh));
  scene.style.setProperty("--scene-progress",p);
  if(i===1){scene.querySelector(".story-title")?.style.setProperty("transform",`translateY(${p*-45}px) scale(${1-Math.abs(p)*.06})`);scene.querySelector(".story-copy")?.style.setProperty("transform",`translateY(${p*55}px)`)}
 });
 panels.forEach((panel,i)=>{
  const r=panel.getBoundingClientRect(),enter=Math.max(-1,Math.min(1,(vh-r.top)/(vh*.82))),leave=Math.max(-1,Math.min(1,(r.bottom)/(vh*.82)));
  const centered=Math.max(0,Math.min(1,1-Math.abs((r.top+r.height/2-vh/2)/(vh*.62))));
  const phase=Math.max(-1,Math.min(1,(r.top+r.height*.5-vh*.5)/(vh*.72)));
  panel.style.setProperty("--focus",centered);
  if(i===0){
   const flip=Math.max(-1,Math.min(1,-phase));
   panel.style.transform=`translate3d(${-phase*70}px,0,${-Math.abs(phase)*650}px) rotateY(${flip*180}deg) scale(${1-Math.abs(phase)*.28})`;
  }else if(i===1){
   const wheel=Math.max(-1,Math.min(1,-phase));
   panel.style.transform=`translate3d(${wheel*85}px,0,${-Math.abs(wheel)*700}px) rotateZ(${wheel*360}deg) scale(${1-Math.abs(wheel)*.3})`;
  }else{
   const morph=Math.max(-1,Math.min(1,-phase));
   panel.style.transform=`translate3d(${morph*35}px,0,${-Math.abs(morph)*800}px) rotateX(${morph*150}deg) rotateY(${morph*-35}deg) scale(${1-Math.abs(morph)*.34})`;
  }
  panel.style.opacity=String(.42+.58*centered);
  panel.style.filter=`brightness(${.62+.38*centered}) saturate(${.7+.3*centered}) blur(${(1-centered)*1.2}px)`;
 });
 ticking=false;
}
addEventListener("scroll",()=>{if(!ticking){requestAnimationFrame(paintMotion);ticking=true}},{passive:true});addEventListener("resize",paintMotion);paintMotion();

const canvas=document.querySelector("#basketball-canvas"),stage=document.querySelector(".ball-stage");
if(canvas&&stage&&window.THREE&&!reduce){
 const T=window.THREE,renderer=new T.WebGLRenderer({canvas,alpha:true,antialias:true});
 const scene=new T.Scene(),camera=new T.PerspectiveCamera(34,1,.1,100);camera.position.set(0,0,4.2);
 scene.add(new T.HemisphereLight(0xffd8b8,0x17100d,2.1));
 const key=new T.DirectionalLight(0xffffff,3.2);key.position.set(-3,4,5);scene.add(key);
 const rim=new T.PointLight(0xff6b35,12,7);rim.position.set(3,-1,3);scene.add(rim);
 const texCanvas=document.createElement("canvas");texCanvas.width=1024;texCanvas.height=1024;const ctx2=texCanvas.getContext("2d");
 const grad=ctx2.createRadialGradient(310,240,20,700,700,800);grad.addColorStop(0,"#ff9a61");grad.addColorStop(.42,"#d95132");grad.addColorStop(1,"#651c18");ctx2.fillStyle=grad;ctx2.fillRect(0,0,1024,1024);
 ctx2.strokeStyle="#171313";ctx2.lineWidth=34;ctx2.lineCap="round";
 const curves=[[-100,170,390,30,650,260,1120,150],[-160,800,210,500,790,560,1120,850],[180,-120,250,280,520,470,1160,540],[720,-100,610,280,520,520,390,1120],[100,950,300,720,610,680,950,1050]];
 curves.forEach(c=>{ctx2.beginPath();ctx2.moveTo(c[0],c[1]);ctx2.bezierCurveTo(c[2],c[3],c[4],c[5],c[6],c[7]);ctx2.stroke()});
 const texture=new T.CanvasTexture(texCanvas);texture.anisotropy=renderer.capabilities.getMaxAnisotropy();
 const ball=new T.Mesh(new T.SphereGeometry(1.48,96,64),new T.MeshStandardMaterial({map:texture,roughness:.72,metalness:.02}));scene.add(ball);
 let rx=-.16,ry=-.38,down=false,lastX=0,lastY=0;
 const draw=()=>{ball.rotation.x=rx;ball.rotation.y=ry;const w=stage.clientWidth||500,h=stage.clientHeight||500;renderer.setSize(w,h,false);camera.aspect=w/h;camera.updateProjectionMatrix();renderer.render(scene,camera);requestAnimationFrame(draw)};draw();
 stage.addEventListener("pointerdown",e=>{down=true;lastX=e.clientX;lastY=e.clientY;stage.setPointerCapture(e.pointerId);stage.classList.add("is-dragging")});
 stage.addEventListener("pointermove",e=>{if(!down)return;ry+=(e.clientX-lastX)*.009;rx+=(e.clientY-lastY)*.009;rx=Math.max(-1.35,Math.min(1.35,rx));lastX=e.clientX;lastY=e.clientY});
 const release=()=>{down=false;stage.classList.remove("is-dragging")};stage.addEventListener("pointerup",release);stage.addEventListener("pointercancel",release);
 stage.addEventListener("wheel",e=>{e.preventDefault();ry+=e.deltaY*.006},{passive:false});
}
const sound=document.querySelector(".sound-toggle");let ctx,master,timer;function startSound(){ctx=ctx||new (window.AudioContext||window.webkitAudioContext)();master=master||ctx.createGain();master.gain.value=.045;master.connect(ctx.destination);const o1=ctx.createOscillator(),o2=ctx.createOscillator(),g=ctx.createGain();o1.type="sine";o2.type="triangle";o1.frequency.value=55;o2.frequency.value=82.41;g.gain.value=.16;o1.connect(g);o2.connect(g);g.connect(master);o1.start();o2.start();timer=setInterval(()=>{o1.frequency.setTargetAtTime([55,65.41,73.42,61.74][Math.floor(Math.random()*4)],ctx.currentTime,.8);o2.frequency.setTargetAtTime([82.41,98,110][Math.floor(Math.random()*3)],ctx.currentTime,1.1)},2200);sound.classList.add("is-on");sound.setAttribute("aria-pressed","true");sound.querySelector(".sound-label").textContent="SOUND ON"}function stopSound(){if(ctx){master.gain.setTargetAtTime(0,ctx.currentTime,.25);setTimeout(()=>{clearInterval(timer);ctx.close();ctx=null},400)}sound.classList.remove("is-on");sound.setAttribute("aria-pressed","false");sound.querySelector(".sound-label").textContent="SOUND OFF"}sound?.addEventListener("click",()=>sound.classList.contains("is-on")?stopSound():startSound())});