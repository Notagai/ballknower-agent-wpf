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


// Desktop horizontal story: translate ordinary vertical mouse-wheel movement
// into continuous horizontal travel. Ease toward the wheel target, but never
// snap or settle to a slide; users may stop anywhere between panels.
document.addEventListener("DOMContentLoaded", () => {
  const world = document.querySelector(".side-world");
  if (!world) return;
  const desktop = window.matchMedia("(min-width: 801px)");
  const reduceMotion = window.matchMedia("(prefers-reduced-motion: reduce)");
  let target = world.scrollLeft;
  let frame = 0;

  function animateTowardTarget() {
    const distance = target - world.scrollLeft;
    if (Math.abs(distance) < 0.5) {
      world.scrollLeft = target;
      frame = 0;
      return;
    }
    world.scrollLeft += distance * (reduceMotion.matches ? 1 : 0.12);
    frame = requestAnimationFrame(animateTowardTarget);
  }

  document.addEventListener("wheel", event => {
    if (!desktop.matches || !world.contains(event.target)) return;
    if (Math.abs(event.deltaY) <= Math.abs(event.deltaX)) return;
    if (world.scrollWidth <= world.clientWidth) return;
    event.preventDefault();
    target = Math.max(0, Math.min(
      target + event.deltaY * 0.9,
      world.scrollWidth - world.clientWidth
    ));
    if (!frame) frame = requestAnimationFrame(animateTowardTarget);
  }, { passive: false });

  world.addEventListener("scroll", () => {
    if (!frame) target = world.scrollLeft;
  }, { passive: true });
  window.addEventListener("resize", () => {
    target = world.scrollLeft;
    if (frame) cancelAnimationFrame(frame);
    frame = 0;
  });
});


// Give the home panels a lightweight scroll-linked depth effect. This stays
// JS-driven rather than forcing layout work, and is disabled for reduced motion.
document.addEventListener("DOMContentLoaded", () => {
  const world = document.querySelector(".side-world");
  if (!world || window.matchMedia("(prefers-reduced-motion: reduce)").matches) return;
  const panels = [...world.querySelectorAll(".world-panel")];
  let ticking = false;
  const updateDepth = () => {
    ticking = false;
    const center = world.scrollLeft + world.clientWidth / 2;
    panels.forEach(panel => {
      const panelCenter = panel.offsetLeft + panel.offsetWidth / 2;
      const distance = (panelCenter - center) / Math.max(world.clientWidth, 1);
      panel.style.setProperty("--scroll-progress", Math.max(-1, Math.min(1, distance)));
    });
  };
  world.addEventListener("scroll", () => {
    if (!ticking) {
      ticking = true;
      requestAnimationFrame(updateDepth);
    }
  }, { passive: true });
  window.addEventListener("resize", updateDepth);
  updateDepth();
});

/* Fade between the site's Home / About / Documentation tabs. */
document.addEventListener("DOMContentLoaded", () => {
  const reduce = window.matchMedia("(prefers-reduced-motion: reduce)").matches;
  if (reduce) return;
  document.querySelectorAll('.site-header .nav a[href], a.button[href]').forEach(link => {
    const url = new URL(link.href, location.href);
    if (url.origin !== location.origin || !/\\.html$/.test(url.pathname) || url.pathname === location.pathname) return;
    link.addEventListener("click", event => {
      if (event.metaKey || event.ctrlKey || event.shiftKey || event.altKey) return;
      event.preventDefault();
      document.body.classList.add("page-leaving");
      setTimeout(() => location.href = url.href, 150);
    });
  });
});
