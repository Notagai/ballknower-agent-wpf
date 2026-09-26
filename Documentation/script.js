// Small progressive enhancement: mark the current page in navigation and
// reveal a subtle active state for the section currently being read.
document.addEventListener("DOMContentLoaded", () => {
  const links = [...document.querySelectorAll(".docs-sidebar a[href^='#']")];
  const sections = links.map(link => document.querySelector(link.getAttribute("href"))).filter(Boolean);
  if (!("IntersectionObserver" in window) || !sections.length) return;

  const observer = new IntersectionObserver(entries => {
    for (const entry of entries) {
      if (!entry.isIntersecting) continue;
      links.forEach(link => {
        const active = link.getAttribute("href") === `#${entry.target.id}`;
        link.classList.toggle("section-active", active);
        if (active) link.setAttribute("aria-current", "location");
        else link.removeAttribute("aria-current");
      });
    }
  }, { rootMargin: "-15% 0px -70% 0px" });

  sections.forEach(section => observer.observe(section));
});

// Reveal sections as they enter view; keep content visible if motion is reduced.
const reduceMotion = window.matchMedia("(prefers-reduced-motion: reduce)").matches;
if (!reduceMotion && "IntersectionObserver" in window) {
  const revealTargets = document.querySelectorAll(".feature-card, .quote-panel, .bottom-cta, .section-heading");
  revealTargets.forEach(el => el.classList.add("reveal-ready"));
  const revealObserver = new IntersectionObserver(entries => {
    entries.forEach(entry => {
      if (entry.isIntersecting) {
        entry.target.classList.add("is-revealed");
        revealObserver.unobserve(entry.target);
      }
    });
  }, { threshold: 0.12 });
  revealTargets.forEach(el => revealObserver.observe(el));
}


// Desktop horizontal story: translate vertical wheel input into sideways scrolling.
// Touch, trackpad horizontal gestures, and the mobile stacked layout remain native.
document.addEventListener("DOMContentLoaded", () => {
  const world = document.querySelector(".side-world");
  if (!world) return;
  const desktop = window.matchMedia("(min-width: 801px)");
  world.addEventListener("wheel", event => {
    if (!desktop.matches || Math.abs(event.deltaY) <= Math.abs(event.deltaX)) return;
    if (world.scrollWidth <= world.clientWidth) return;
    event.preventDefault();
    world.scrollBy({ left: event.deltaY, behavior: "auto" });
  }, { passive: false });
});
