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