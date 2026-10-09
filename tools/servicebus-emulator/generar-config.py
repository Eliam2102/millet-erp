#!/usr/bin/env python3
"""Genera Config.json del emulador de Service Bus a partir de infra/modules/servicebus.bicep.

Así el emulador local tiene exactamente los mismos temas, suscripciones y
filtros SQL que Azure. Si cambia el Bicep, se vuelve a correr este script.
Uso: python3 tools/servicebus-emulator/generar-config.py
"""
import json
import re
from pathlib import Path

raiz = Path(__file__).resolve().parents[2]
bicep = (raiz / "infra/modules/servicebus.bicep").read_text(encoding="utf-8")
salida = Path(__file__).resolve().parent / "Config.json"

patron = re.compile(
    r"resource\s+(\w+)\s+'Microsoft\.ServiceBus/namespaces/([\w/]+)@[^']+'\s*=\s*\{(.*?)\n\}",
    re.S,
)
temas, subs, reglas = {}, {}, {}
for var, tipo, cuerpo in patron.findall(bicep):
    padre = re.search(r"parent:\s*(\w+)", cuerpo)
    nombre = re.search(r"name:\s*'([^']+)'", cuerpo)
    if tipo == "topics":
        temas[var] = {"Name": nombre.group(1), "Subs": []}
    elif tipo == "topics/subscriptions":
        subs[var] = {"Name": nombre.group(1), "Padre": padre.group(1), "Reglas": []}
    elif tipo == "topics/subscriptions/rules":
        sql = re.search(r"sqlExpression:\s*'((?:[^'\\]|\\.)*)'", cuerpo)
        reglas[var] = {
            "Name": nombre.group(1),
            "Padre": padre.group(1),
            "Sql": sql.group(1).replace("\\'", "'") if sql else None,
        }

for r in reglas.values():
    if r["Sql"]:
        subs[r["Padre"]]["Reglas"].append(r)
for s in subs.values():
    temas[s["Padre"]]["Subs"].append(s)

# El emulador exige al menos una suscripción por tema (Azure no). A los temas
# sin consumidor (p. ej. admin-events) se les agrega una suscripción de
# relleno que solo existe en local; nadie la lee.
for t in temas.values():
    if not t["Subs"]:
        t["Subs"].append({"Name": "sin-consumidor-local", "Reglas": []})

props_tema = {
    "DefaultMessageTimeToLive": "PT1H",
    "DuplicateDetectionHistoryTimeWindow": "PT20S",
    "RequiresDuplicateDetection": False,
}
props_sub = {
    "DeadLetteringOnMessageExpiration": False,
    "DefaultMessageTimeToLive": "PT1H",
    "LockDuration": "PT1M",
    "MaxDeliveryCount": 10,
    "ForwardDeadLetteredMessagesTo": "",
    "ForwardTo": "",
    "RequiresSession": False,
}
config = {
    "UserConfig": {
        "Namespaces": [
            {
                "Name": "sbemulatorns",
                "Queues": [],
                "Topics": [
                    {
                        "Name": t["Name"],
                        "Properties": props_tema,
                        "Subscriptions": [
                            {
                                "Name": s["Name"],
                                "Properties": props_sub,
                                "Rules": [
                                    {
                                        "Name": r["Name"],
                                        "Properties": {
                                            "FilterType": "Sql",
                                            "SqlFilter": {"SqlExpression": r["Sql"]},
                                        },
                                    }
                                    for r in s["Reglas"]
                                ],
                            }
                            for s in t["Subs"]
                        ],
                    }
                    for t in temas.values()
                ],
            }
        ],
        "Logging": {"Type": "File"},
    }
}
salida.write_text(json.dumps(config, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
print(f"{len(temas)} temas, {len(subs)} suscripciones, {sum(len(s['Reglas']) for s in subs.values())} filtros -> {salida.relative_to(raiz)}")
for t in temas.values():
    print(f"  {t['Name']}: {', '.join(s['Name'] for s in t['Subs']) or '(sin suscripciones)'}")
