r"""Index du vidage complet du jeu (ref\GAME-complet.txt, fait par [Test] Vidage=2) pour chercher hors jeu.

    python run\ref-index.py ref\<version>

Ecrit dans ce dossier :
  automates.tsv   ligne, chemin de l'objet, automate, actif ou non, etats
  variables.tsv   variable -> ou elle est utilisee (automate, etat, action) ; G = globale
  evenements.tsv  evenement -> qui l'envoie (action) et qui y reagit (transition)
Les numeros de ligne renvoient a GAME-complet.txt (rg -n, puis lire autour).
"""
import re
import sys
from collections import defaultdict
from pathlib import Path

OBJ = re.compile(r"^( *)(\[off\] )?(.*?)  @\(")
FSM = re.compile(r"^ *\* FSM '(.*)' etat=(.*)$")
STATE = re.compile(r"^ *(.+?) :( .*)?$")
VAR = re.compile(r"\{([^{}=]+)\}=")
EVT = re.compile(r"evt:([^,;)\]]+)")
TRANS = re.compile(r" ([^ ]+(?: [^ >-]+)*?)->")


def main():
    d = Path(sys.argv[1] if len(sys.argv) > 1 else "ref")
    src = d / "GAME-complet.txt"
    glob_file = d / "globals.txt"
    globals_ = set()
    if glob_file.exists():
        for line in glob_file.read_text(encoding="utf-8", errors="replace").splitlines():
            m = re.match(r"\s*\w+\s+\{?([^}=]+)\}?=", line)
            if m:
                globals_.add(m.group(1).strip())
    stack = []            # (profondeur, nom, actif)
    fsms = []
    uses = defaultdict(list)
    sent = defaultdict(list)
    handled = defaultdict(list)
    cur = None            # [ligne, chemin, automate, actif, etats]
    state = None
    # newline="\n" : du texte du jeu contient des \r seuls, qui decaleraient les numeros de ligne (rg, sed, Read).
    with src.open(encoding="utf-8", errors="replace", newline="\n") as f:
        for no, line in enumerate(f, 1):
            line = line.rstrip("\n")
            m = OBJ.match(line)
            if m and not line.lstrip().startswith(("*", "#")):
                depth = len(m.group(1)) // 2
                del stack[depth:]
                stack.append((m.group(3), m.group(2) is None))
                cur = state = None
                continue
            m = FSM.match(line)
            if m:
                path = "/".join(n for n, _ in stack)
                active = all(a for _, a in stack)
                cur = [no, path, m.group(1), "actif" if active else "inactif", []]
                fsms.append(cur)
                state = None
                continue
            if cur is None:
                continue
            body = line.strip()
            indent = len(line) - len(line.lstrip())
            obj_indent = (len(stack) - 1) * 2
            where = f"{cur[1]} :: {cur[2]}"
            if body.startswith("var "):
                continue
            if body.startswith("global "):
                for e in TRANS.findall(" " + body[7:]):
                    handled[e.strip()].append((no, where, "(global)"))
                continue
            if indent == obj_indent + 6:
                m = STATE.match(line)
                if m:
                    state = m.group(1).strip()
                    cur[4].append(state)
                    rest = m.group(2) or ""
                    if "{" in rest:      # vidage sans parametres : actions sur la meme ligne
                        rest = rest.split("  {")[0]
                    for e in TRANS.findall(rest):
                        handled[e.strip()].append((no, where, state))
                continue
            if indent >= obj_indent + 10 and state:
                action = body.split("(", 1)[0]
                for v in set(VAR.findall(body)):
                    uses[v].append((no, where, state, action))
                for e in set(EVT.findall(body)):
                    sent[e.strip()].append((no, where, state, action))

    with (d / "automates.tsv").open("w", encoding="utf-8") as o:
        o.write("ligne\tchemin\tautomate\tactif\tetats\n")
        for no, path, name, act, states in fsms:
            o.write(f"{no}\t{path}\t{name}\t{act}\t{' | '.join(states)}\n")
    with (d / "variables.tsv").open("w", encoding="utf-8") as o:
        o.write("variable\tG\tligne\tautomate\tetat\taction\n")
        for v in sorted(uses):
            for no, where, st, act in uses[v]:
                o.write(f"{v}\t{'G' if v in globals_ else ''}\t{no}\t{where}\t{st}\t{act}\n")
    with (d / "evenements.tsv").open("w", encoding="utf-8") as o:
        o.write("evenement\tsens\tligne\tautomate\tetat\taction\n")
        for e in sorted(set(sent) | set(handled)):
            for no, where, st, act in sent.get(e, []):
                o.write(f"{e}\tenvoi\t{no}\t{where}\t{st}\t{act}\n")
            for no, where, st in handled.get(e, []):
                o.write(f"{e}\treaction\t{no}\t{where}\t{st}\t\n")
    print(f"{len(fsms)} automates, {len(uses)} variables ({len(globals_ & set(uses))} globales), "
          f"{len(set(sent) | set(handled))} evenements -> {d}")


if __name__ == "__main__":
    main()
