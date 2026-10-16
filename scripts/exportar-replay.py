"""Exporta um trecho do history.db do Meu Clima como fixture de replay (sem IDs de aparelho).

Uso:
  python -I scripts/exportar-replay.py "2026-10-15 20:20" "2026-10-16 08:55" saida.csv

Lê só o aparelho do PC (pcDeviceId do settings.json). O CSV traz hora local, estado lido e a
temperatura de fora mais recente. Nenhum ID, cidade ou credencial vai para a saída.
"""
import csv
import datetime as dt
import json
import os
import sqlite3
import sys


def main():
    if len(sys.argv) != 4:
        sys.exit(__doc__)
    de, ate, saida = sys.argv[1:]
    app = os.path.join(os.environ["APPDATA"], "MeuClima")
    pc = json.load(open(os.path.join(app, "settings.json"), encoding="utf-8"))["pcDeviceId"]
    db = sqlite3.connect("file:" + os.path.join(app, "history.db").replace("\\", "/") + "?mode=ro", uri=True)
    t0 = int(dt.datetime.strptime(de, "%Y-%m-%d %H:%M").timestamp())
    t1 = int(dt.datetime.strptime(ate, "%Y-%m-%d %H:%M").timestamp())
    fora = db.execute("select ts, temp from fora where ts <= ? order by ts", (t1,)).fetchall()
    linhas = db.execute(
        "select ts, conectado, ligado, modo, ventilacao, alvo, ambiente from leitura "
        "where aparelho = ? and ts between ? and ? order by ts", (pc, t0, t1)).fetchall()
    with open(saida, "w", newline="", encoding="utf-8") as f:
        w = csv.writer(f)
        w.writerow(["hora", "conectado", "ligado", "modo", "ventilacao", "alvo", "ambiente", "fora"])
        for ts, con, lig, modo, vent, alvo, amb in linhas:
            ultimo = next((t for s, t in reversed(fora) if s <= ts), None)
            hora = dt.datetime.fromtimestamp(ts).astimezone().isoformat(timespec="seconds")
            w.writerow([hora, con, lig, modo, vent, "" if alvo is None else alvo, "" if amb is None else amb,
                        "" if ultimo is None else ultimo])
    print(f"{len(linhas)} leituras em {saida}")


main()
