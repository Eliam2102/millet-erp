#!/usr/bin/env python3
"""Resume los tiempos del gate y sus TRX; --fallas imprime sólo nombres fallidos."""

import argparse
import csv
import xml.etree.ElementTree as ET
from collections import defaultdict
from pathlib import Path


def segundos(duracion):
    """TRX expresa duration como [días.]horas:minutos:segundos."""
    horas, minutos, segundos_texto = duracion.split(":")
    dias = 0
    if "." in horas:
        dias_texto, horas = horas.split(".")
        dias = int(dias_texto)
    return dias * 86400 + int(horas) * 3600 + int(minutos) * 60 + float(segundos_texto)


def leer_trx(ruta):
    raiz = ET.parse(ruta).getroot()
    # ElementTree acepta también los TRX que usan el namespace habitual de VSTest.
    ns = {"t": raiz.tag.split("}")[0][1:] if "}" in raiz.tag else ""}
    nombres = {}
    for prueba in raiz.findall("t:TestDefinitions/t:UnitTest", ns):
        metodo = prueba.find("t:TestMethod", ns)
        if metodo is None:
            raise ValueError(f"{ruta.name}: UnitTest sin TestMethod")
        clase = metodo.attrib["className"]
        nombres[prueba.attrib["id"]] = (clase, f"{clase}.{metodo.attrib['name']}")
    resultados = []
    for prueba in raiz.findall("t:Results/t:UnitTestResult", ns):
        clase, nombre = nombres[prueba.attrib["testId"]]
        resultados.append((clase, nombre, prueba.attrib["outcome"],
                           segundos(prueba.get("duration", "00:00:00"))))
    return resultados


def informe(directorio, solo_fallas=False):
    archivos = sorted(directorio.glob("*.trx"))
    proyectos = [(ruta.stem, leer_trx(ruta)) for ruta in archivos]
    if solo_fallas:
        for nombre in sorted({nombre for _, pruebas in proyectos
                              for _, nombre, resultado, _ in pruebas if resultado == "Failed"}):
            print(nombre)
        return

    with (directorio / "fases.tsv").open(newline="", encoding="utf-8") as archivo:
        lector = csv.DictReader(archivo, delimiter="\t")
        if lector.fieldnames != ["fase", "segundos", "resultado"]:
            raise ValueError("fases.tsv debe tener las columnas fase, segundos y resultado")
        fases = [(fila["fase"], int(fila["segundos"]), fila["resultado"]) for fila in lector]
    totales = [duracion for fase, duracion, _ in fases if fase == "total"]
    if len(totales) != 1:
        raise ValueError("fases.tsv debe contener exactamente una fila total")
    total = totales[0]
    print("Fase\tSegundos\t% del total\tResultado")
    for fase, duracion, resultado in sorted(fases, key=lambda fila: (-fila[1], fila[0])):
        porcentaje = duracion / total * 100 if total else 0
        print(f"{fase}\t{duracion}\t{porcentaje:.1f}%\t{resultado}")

    if not archivos:
        print("\nNo hay archivos TRX; la ejecución pudo terminar antes de las pruebas.")
        return

    clases = defaultdict(list)
    print("\nProyecto\tAprobadas\tFallidas\tOmitidas")
    for proyecto, pruebas in proyectos:
        conteos = defaultdict(int)
        for clase, _, resultado, duracion in pruebas:
            conteos[resultado] += 1
            clases[clase].append(duracion)
        omitidas = conteos["NotExecuted"] + conteos["Skipped"]
        print(f"{proyecto}\t{conteos['Passed']}\t{conteos['Failed']}\t{omitidas}")

    print("\nLas 25 clases más lentas\nClase\tSegundos\tPruebas\tPromedio (s)")
    for clase, duraciones in sorted(clases.items(), key=lambda fila: (-sum(fila[1]), fila[0]))[:25]:
        duracion = sum(duraciones)
        print(f"{clase}\t{duracion:.3f}\t{len(duraciones)}\t{duracion / len(duraciones):.3f}")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("directorio", type=Path, help="Directorio con fases.tsv y archivos TRX")
    parser.add_argument("--fallas", action="store_true", help="Sólo FullyQualifiedName fallidas, ordenadas")
    args = parser.parse_args()
    try:
        informe(args.directorio, args.fallas)
    except (OSError, ValueError, KeyError, ET.ParseError) as error:
        parser.exit(1, f"Error al leer los tiempos del gate: {error}\n")


if __name__ == "__main__":
    main()
