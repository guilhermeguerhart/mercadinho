// Service worker do Mercado Gerência: guarda as telas do app para abrir rápido (e para os sites que geram o .apk
// reconhecerem o app). Os dados do mercado sempre vêm da internet, nunca do cache.
const VERSAO = "mercado-gerencia-3";
const ARQUIVOS = ["./", "./index.html", "./estilo.css", "./app.js", "./config.js", "./estoque.js", "./pedidos.js", "./trava.js", "./manifest.webmanifest", "./icones/icone-192.png", "./icones/icone-512.png"];

self.addEventListener("install", (e) => {
  e.waitUntil(caches.open(VERSAO).then((c) => c.addAll(ARQUIVOS)).then(() => self.skipWaiting()));
});

self.addEventListener("activate", (e) => {
  e.waitUntil(caches.keys().then((nomes) => Promise.all(nomes.filter((n) => n !== VERSAO).map((n) => caches.delete(n)))).then(() => self.clients.claim()));
});

self.addEventListener("fetch", (e) => {
  const url = new URL(e.request.url);
  // Só as telas do próprio app: primeiro a internet (versão nova), sem internet o que está guardado
  if (e.request.method !== "GET" || url.origin !== self.location.origin) return;
  e.respondWith(
    fetch(e.request)
      .then((r) => { const copia = r.clone(); caches.open(VERSAO).then((c) => c.put(e.request, copia)); return r; })
      .catch(() => caches.match(e.request).then((r) => r ?? caches.match("./index.html"))),
  );
});
