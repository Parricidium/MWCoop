// Essais de l'interface dans un navigateur ordinaire (sans EditeurCarte.exe) : le programme est simule. Carte : /carte/<jeu>/
// (serveur d'essai). ?msc=0 : My Summer Car introuvable ; ?carte=0 : pas de carte preparee.
export function start({ onMessage }) {
  const q = new URLSearchParams(location.search);
  let mscFound = q.get("msc") !== "0", mwcFound = q.get("mwc") === "1", carte = q.get("carte") !== "0";
  const etat = () => ({ type: "etat", langue: q.get("langue") || "fr", cache: "C:\\Users\\Joueur\\Documents\\My Seasons Car\\Carte", ouvrir: q.get("ouvrir") || "", jeux: [
    mscFound ? { id: "msc", nom: "My Summer Car", exe: "mysummercar.exe", trouve: true, dossier: "D:\\SteamLibrary\\steamapps\\common\\My Summer Car", source: "steam", version: "v.250908-04", lanceur: true,
                 carte: carte ? { date: "19/10/2026 21:51", taille: 1.2e9 } : undefined }
             : { id: "msc", nom: "My Summer Car", exe: "mysummercar.exe", trouve: false },
    mwcFound ? { id: "mwc", nom: "My Winter Car", exe: "mywintercar.exe", trouve: true, dossier: "E:\\Jeux\\My Winter Car", source: "choisi", version: "v.260917-03", lanceur: true,
                 carte: carte ? { date: "19/10/2026 22:26", taille: 9.6e8 } : undefined }
             : { id: "mwc", nom: "My Winter Car", exe: "mywintercar.exe", trouve: false },
  ] });
  const reply = m => setTimeout(() => onMessage(m), 60);
  window.essaiSend = m => {
    if (m.type === "pret" || m.type === "reessayer") reply(etat());
    else if (m.type === "choisirExe") reply({ type: "verification", jeu: m.jeu, chemin: "E:\\Jeux\\My Winter Car\\mywintercar.exe", dossier: "E:\\Jeux\\My Winter Car", exe: true, data: true, ok: true, version: "v.260917-03", lanceur: true });
    else if (m.type === "verifier") { const ok = /winter|summer/i.test(m.chemin); reply({ type: "verification", jeu: m.jeu, chemin: m.chemin, dossier: "", exe: ok, data: ok, ok, version: ok ? "v.260917-03" : "", lanceur: ok }); }
    else if (m.type === "valider") { if (m.jeu === "mwc") mwcFound = true; else mscFound = true; reply(etat()); }
    else if (m.type === "preparer") {
      const steps = [["lancement", .01], ["chargement", .02], ["attente", .03], ["sol", .1], ["routes", .3], ["batiments", .5], ["vegetation", .75], ["textures", .85], ["cache", .97]];
      steps.forEach(([e, a], i) => setTimeout(() => onMessage({ type: "prep", jeu: m.jeu, etape: e, avance: a, texte: "", fini: false }), 700 * (i + 1)));
      setTimeout(() => { carte = true; onMessage({ type: "prep", jeu: m.jeu, etape: "fini", avance: 1, texte: "", fini: true }); }, 700 * (steps.length + 1));
    }
    else if (m.type === "annuler") reply({ type: "prep", jeu: "msc", etape: "annule", avance: 0, fini: true, erreur: "annule" });
  };
  window.essaiSend({ type: "pret" });
}
