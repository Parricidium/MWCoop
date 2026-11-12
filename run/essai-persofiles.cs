// Essai de src\MWCoop\Sync\PersoFiles.cs sans Unity ni jeu (images perso par jeu, 12/11) :
//   csc -out:essai-persofiles.exe run\essai-persofiles.cs src\MWCoop\Sync\PersoFiles.cs
//   essai-persofiles.exe <dossier vide jetable>
using System;
using System.IO;
using MWCoop;

static class EssaiPersoFiles
{
    static int bad;
    static void Say(string l, bool good) { Console.WriteLine(l + (good ? " ok" : " ECHEC")); if (!good) bad++; }

    static int Main(string[] a)
    {
        if (a.Length < 1 || Directory.Exists(a[0]) && Directory.GetFileSystemEntries(a[0]).Length > 0) { Console.WriteLine("refuse : un dossier vide jetable"); return 3; }
        string r = a[0];
        Directory.CreateDirectory(r);
        string[] names = { "visage", "haut", "pantalon" };
        File.WriteAllText(Path.Combine(r, "haut.jpg"), "chemise");
        File.WriteAllText(Path.Combine(r, "pantalon.jpg"), "ancien");
        File.WriteAllText(PersoFiles.Image(r, "pantalon", "msc"), "deja");

        Say("noms : haut-msc.jpg", Path.GetFileName(PersoFiles.Image(r, "haut", PersoFiles.GameId(true))) == "haut-msc.jpg");
        Say("noms : gabarit-haut-mwc.png, haut-jeu-mwc.png, gabarits-mwc.version",
            Path.GetFileName(PersoFiles.Template(r, "haut", "mwc")) == "gabarit-haut-mwc.png" && Path.GetFileName(PersoFiles.GameTexture(r, "haut", "mwc")) == "haut-jeu-mwc.png" && Path.GetFileName(PersoFiles.TemplateTag(r, "mwc")) == "gabarits-mwc.version");

        Say("reprise MWC : 2 images", PersoFiles.Migrate(r, "mwc", names) == 2);
        Say("reprise MWC : haut-mwc = l'ancien", File.ReadAllText(PersoFiles.Image(r, "haut", "mwc")) == "chemise");
        Say("reprise MWC : pas de visage invente", !File.Exists(PersoFiles.Image(r, "visage", "mwc")));
        Say("reprise MWC : une seule fois", PersoFiles.Migrate(r, "mwc", names) == -1);
        Say("reprise MSC : 1 image, pantalon-msc deja la garde", PersoFiles.Migrate(r, "msc", names) == 1 && File.ReadAllText(PersoFiles.Image(r, "pantalon", "msc")) == "deja");

        // manteau sur MWC : la chemise de MSC reste
        File.WriteAllText(PersoFiles.Image(r, "haut", "mwc"), "manteau");
        Say("manteau MWC, chemise MSC", File.ReadAllText(PersoFiles.Image(r, "haut", "msc")) == "chemise" && File.ReadAllText(PersoFiles.Image(r, "haut", "mwc")) == "manteau");
        // retire sur MWC : pas de reprise de l'ancien (marqueur)
        File.Delete(PersoFiles.Image(r, "haut", "mwc"));
        Say("retrait MWC : l'ancien ne revient pas", PersoFiles.Migrate(r, "mwc", names) == -1 && !File.Exists(PersoFiles.Image(r, "haut", "mwc")));
        Say("l'ancien haut.jpg reste", File.ReadAllText(Path.Combine(r, "haut.jpg")) == "chemise");

        // ancien fichier pris : pas de marqueur, reprise au passage suivant
        string r2 = Path.Combine(r, "pris");
        Directory.CreateDirectory(r2);
        File.WriteAllText(Path.Combine(r2, "visage.jpg"), "visage");
        using (new FileStream(Path.Combine(r2, "visage.jpg"), FileMode.Open, FileAccess.Read, FileShare.None))
            Say("ancien pris : pas repris, pas de marqueur", PersoFiles.Migrate(r2, "msc", names) == 0 && !File.Exists(Path.Combine(r2, "perso-msc.migre")));
        Say("ancien libre : repris ensuite", PersoFiles.Migrate(r2, "msc", names) == 1 && File.Exists(Path.Combine(r2, "perso-msc.migre")));

        Console.WriteLine(bad == 0 ? "TOUT OK" : "RESULTAT : ECHEC");
        return bad == 0 ? 0 : 1;
    }
}
