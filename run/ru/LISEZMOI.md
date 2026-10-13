# Traduction russe (13/10/2026)

Lanceur (`T(L"fr", L"en")`) et menus du mod (`Lang.T("fr", "en")`) : en russe, le texte anglais est cherché dans une table
générée ; absent, l'anglais reste. Les textes écrits avec la police du jeu (GUIinteraction, GUIsubtitle : pas de
cyrillique) passent par `Lang.TG` et restent en anglais.

- `ru_launcher.tsv`, `ru_mod.tsv` : anglais tel qu'écrit dans la source `<TAB>` russe (mêmes échappements, mêmes `%d`/`{0}`).
- `python -X utf8 -I ru_gen.py` : écrit `launcher/ru.inc` et `src/MWCoop/UI/LangRu.cs` (vérifie les formats).
- `ru_extract.py` puis `ru_extract2.py` : listent les textes de la source (`ru_src_*.tsv`) pour trouver ceux à traduire.
- `ru_brief.md` : consignes et glossaire de la traduction.
