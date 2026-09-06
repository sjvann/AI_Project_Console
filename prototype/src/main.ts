import "./styles.css";
import { subscribe } from "./store";
import { bind, layout, renderPage } from "./views";

const app = document.getElementById("app")!;

function render() {
  app.innerHTML = layout(renderPage());
  bind(app);
}

window.addEventListener("hashchange", render);
subscribe(render);
if (!location.hash) location.hash = "#/";
else render();
