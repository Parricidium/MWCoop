namespace MWCoop
{
    // Ajouts de MWCoop au jeu (ce qui n'existe pas dans le jeu de base) : camera drone (F8), sacs de couchage, boutique
    // OSUUSKAUPPA... Un seul interrupteur dans le lanceur (Options du jeu > Coop > Partie : « Desactiver les ajouts COOP »,
    // [Coop] SansAjouts=1 ; demande de Menor, 12/10 : « un jeu 100 % vanilla », et de JD : un bouton pour tous, presents
    // et a venir). Tout nouvel ajout de ce genre doit consulter Ajouts.On.
    public static class Ajouts
    {
        public static bool On { get { return Config.GetInt("Coop", "SansAjouts", 0) == 0; } }
    }
}
