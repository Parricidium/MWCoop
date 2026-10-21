// Miroir GitHub de MWCoop -- Worker Cloudflare (gratuit), 21/10/2026.
// Des joueurs (Russie surtout) ne joignent pas GitHub : le lanceur redemande alors la meme adresse a travers ce
// miroir, sous la forme  https://<ce worker>/https://api.github.com/...  (meme convention que les proxys gh-proxy).
// Seuls les depots de MWCoop sont servis (liste ci-dessous) ; les reponses de l'API sont gardees 5 minutes en cache
// (l'API de GitHub limite a 60 demandes par heure et par adresse : tous les joueurs passent par l'adresse du worker).
//
// Mise en place (5 minutes) : dash.cloudflare.com > Workers & Pages > Create > Worker > nom « mwcoop » > Deploy,
// puis Edit code : coller ce fichier, Deploy. L'adresse est  https://mwcoop.<ton-sous-domaine>.workers.dev/
// -> a mettre dans kMirrors (launcher.cpp) ou, pour les joueurs, dans mwcoop-lanceur.ini : [Lanceur] Miroirs=https://mwcoop.xxx.workers.dev/

const ALLOW = [
  "https://api.github.com/repos/Parricidium/",
  "https://github.com/Parricidium/",
  "https://raw.githubusercontent.com/Parricidium/",
  "https://objects.githubusercontent.com/",
  "https://release-assets.githubusercontent.com/",
  "https://codeload.github.com/Parricidium/",
  "https://api.github.com/repos/piotrulos/MSCModLoader/",
  "https://github.com/piotrulos/MSCModLoader/",
];

export default {
  async fetch(request) {
    const u = new URL(request.url);
    const target = u.pathname.slice(1) + u.search;   // "https://api.github.com/..." apres le premier "/"
    if (request.method !== "GET" && request.method !== "HEAD")
      return new Response("GET only", { status: 405 });
    if (!ALLOW.some(p => target.startsWith(p)))
      return new Response("MWCoop mirror: not allowed", { status: 403 });

    const isApi = target.startsWith("https://api.github.com/");
    const cache = caches.default;
    const key = new Request(target, { method: "GET" });
    if (isApi) {
      const hit = await cache.match(key);
      if (hit) return hit;
    }
    const headers = new Headers();
    headers.set("User-Agent", "MWCoop-Mirror");
    const acc = request.headers.get("Accept");
    if (acc) headers.set("Accept", acc);
    const r = await fetch(target, { method: request.method, headers, redirect: "follow" });
    const out = new Response(r.body, { status: r.status, headers: r.headers });
    out.headers.delete("set-cookie");
    out.headers.set("Access-Control-Allow-Origin", "*");
    if (isApi && r.status === 200) {
      out.headers.set("Cache-Control", "public, max-age=300");
      await cache.put(key, out.clone());
    }
    return out;
  },
};
