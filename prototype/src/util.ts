export function esc(s: string) {
  return s.replace(/[&<>"']/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c]!));
}

export function navLink(href: string, label: string, on: boolean, badge?: number) {
  const b = badge && badge > 0 ? ` <span class="badge">${badge}</span>` : "";
  return `<a href="#${href}" class="${on ? "is-on" : ""}">${esc(label)}${b}</a>`;
}
