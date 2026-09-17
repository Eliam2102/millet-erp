#!/usr/bin/env python3
"""Genera el inventario trazable de Fase 1 desde la matriz y el plan de olas.

Uso:
  python tools/generar-inventario-fase1.py MATRIZ.xlsx OLAS.xlsx

Requiere openpyxl. Los resultados se escriben en docs/handoff/.
"""

from __future__ import annotations

import csv
import sys
from collections import Counter
from datetime import date, datetime
from pathlib import Path

from openpyxl import load_workbook


ROOT = Path(__file__).resolve().parents[1]
OUT_DIR = ROOT / "docs" / "handoff"

MODULE_PATHS = {
    "Administración y usuarios": (
        ["backend/src/Administracion", "backend/src/Identidad", "backend/src/Catalogos", "backend/src/DatosMaestros", "backend/src/Compartido"],
        ["frontend/src/features/catalogos", "frontend/src/routes"],
        ["backend/tests/Administracion.UnitTests", "backend/tests/Identidad.UnitTests", "backend/tests/Catalogos.UnitTests", "backend/tests/Api.IntegrationTests"],
    ),
    "Compras": (["backend/src/Compras"], ["frontend/src/features/compras"], ["backend/tests/Compras.UnitTests", "backend/tests/Compras.IntegrationTests"]),
    "Almacén de insumos": (["backend/src/Almacen"], ["frontend/src/features/almacen"], ["backend/tests/Almacen.UnitTests", "backend/tests/Api.IntegrationTests"]),
    "Materia prima": (["backend/src/Almacen", "backend/src/Integraciones.Aw"], ["frontend/src/features/almacen"], ["backend/tests/Almacen.UnitTests", "backend/tests/Integraciones.Aw.UnitTests", "backend/tests/Integraciones.Aw.IntegrationTests"]),
    "Cuentas por pagar": (["backend/src/CuentasPorPagar"], ["frontend/src/features/cxp"], ["backend/tests/CuentasPorPagar.UnitTests", "backend/tests/Api.IntegrationTests"]),
    "Comercio exterior": ([], [], ["backend/tests/Api.IntegrationTests"]),
    "Facturación y fiscal": (["backend/src/Facturacion", "backend/src/Integraciones.Fiscal"], ["frontend/src/features/facturacion", "frontend/src/features/integraciones-fiscal"], ["backend/tests/Facturacion.UnitTests", "backend/tests/Integraciones.Fiscal.UnitTests", "backend/tests/Api.IntegrationTests"]),
    "Crédito y cobranza": (["backend/src/CuentasPorCobrar"], ["frontend/src/features/cxc"], ["backend/tests/CuentasPorCobrar.UnitTests", "backend/tests/Api.IntegrationTests"]),
    "Producto terminado": (["backend/src/Almacen", "backend/src/Integraciones.Aw"], ["frontend/src/features/almacen"], ["backend/tests/Almacen.UnitTests", "backend/tests/Integraciones.Aw.UnitTests", "backend/tests/Integraciones.Aw.IntegrationTests"]),
    "Tesorería": (["backend/src/Tesoreria"], ["frontend/src/features/tesoreria"], ["backend/tests/Tesoreria.UnitTests", "backend/tests/Api.IntegrationTests"]),
    "Contabilidad": (["backend/src/CentrosCosto", "backend/src/Compartido"], ["frontend/src/features/centros-costo"], ["backend/tests/Api.IntegrationTests"]),
    "Activos fijos": ([], [], ["backend/tests/Api.IntegrationTests"]),
}


def value(v):
    if isinstance(v, (datetime, date)):
        return v.isoformat()
    if v is None:
        return ""
    return str(v)


def existing(paths):
    return [p for p in paths if (ROOT / p).exists()]


def rows_from_sheet(path: Path, sheet: str, header_row: int):
    ws = load_workbook(path, data_only=True)[sheet]
    headers = [c.value for c in ws[header_row]]
    return [dict(zip(headers, row)) for row in ws.iter_rows(min_row=header_row + 1, values_only=True)]


def main():
    if len(sys.argv) != 3:
        raise SystemExit("Uso: generar-inventario-fase1.py MATRIZ.xlsx OLAS.xlsx")

    matrix_path = Path(sys.argv[1]).resolve()
    waves_path = Path(sys.argv[2]).resolve()
    matrix = [r for r in rows_from_sheet(matrix_path, "Matriz con tiempos", 4) if r.get("Fase") == "Fase 1"]
    tasks = {r.get("ID matriz"): r for r in rows_from_sheet(waves_path, "Tareas ClickUp v3", 5) if r.get("ID matriz")}

    if len(matrix) != 139:
        raise SystemExit(f"Se esperaban 139 funcionalidades de Fase 1 y se encontraron {len(matrix)}")

    headers = [
        "ID", "Ola", "Módulo", "Funcionalidad", "Estado matriz", "Qué falta según matriz",
        "Horas base", "Responsable propuesto en plan", "Responsable nominal", "Inicio objetivo", "Fin objetivo",
        "Dependencias", "Criterio de aceptación", "Backend localizado", "Frontend localizado", "Pruebas localizadas",
        "Estado de evidencia técnica", "Evidencia requerida para cierre", "Resultado QA/UAT",
    ]
    output_rows = []
    for row in matrix:
        task = tasks.get(row["ID"], {})
        backend, frontend, tests = MODULE_PATHS.get(row["Módulo"], ([], [], []))
        backend_found, frontend_found, tests_found = existing(backend), existing(frontend), existing(tests)
        status = row.get("Estado")
        if status == "Ya existe y funciona":
            if backend_found or frontend_found:
                evidence_state = "Código candidato localizado; falta ejecutar el caso, documentar evidencia, regresión y UAT"
            else:
                evidence_state = "Riesgo: matriz indica existente, pero no se localizó módulo dedicado; requiere trazabilidad manual"
        elif status == "Existe a medias":
            evidence_state = "Base de código candidata localizada; falta confirmar brecha e implementar/probar"
        else:
            evidence_state = "Construcción pendiente; rutas son puntos de integración, no evidencia de funcionalidad terminada"

        proposed = task.get("Responsable principal propuesto") or "Por confirmar"
        nominal = "Geovany y Uzziel" if proposed == "Dev 1 y Dev 2" else "Por confirmar: mapear Dev 1/Dev 2 a Geovany/Uzziel"
        output_rows.append({
            "ID": row["ID"],
            "Ola": task.get("Ola v3") or "Por confirmar",
            "Módulo": row["Módulo"],
            "Funcionalidad": row["Funcionalidad y acciones incluidas"],
            "Estado matriz": status,
            "Qué falta según matriz": row.get("Si existe a medias: qué falta") or "",
            "Horas base": row.get("Horas totales") or 0,
            "Responsable propuesto en plan": proposed,
            "Responsable nominal": nominal,
            "Inicio objetivo": task.get("Inicio objetivo") or "Por confirmar",
            "Fin objetivo": task.get("Fin objetivo") or "Por confirmar",
            "Dependencias": row.get("De qué depende") or "",
            "Criterio de aceptación": row.get("Criterio de aceptación") or "",
            "Backend localizado": "; ".join(backend_found) or "Sin módulo dedicado localizado",
            "Frontend localizado": "; ".join(frontend_found) or "Sin feature dedicado localizado",
            "Pruebas localizadas": "; ".join(tests_found) or "Sin proyecto de pruebas dedicado localizado",
            "Estado de evidencia técnica": evidence_state,
            "Evidencia requerida para cierre": "Caso reproducible; dato usado; captura o log; pruebas automáticas aplicables; resultado de regresión; aprobación UAT del área",
            "Resultado QA/UAT": "Pendiente",
        })

    OUT_DIR.mkdir(parents=True, exist_ok=True)
    csv_path = OUT_DIR / "inventario-funcional-fase1.csv"
    with csv_path.open("w", newline="", encoding="utf-8-sig") as f:
        writer = csv.DictWriter(f, fieldnames=headers)
        writer.writeheader()
        writer.writerows(output_rows)

    statuses = Counter(r["Estado matriz"] for r in output_rows)
    waves = Counter(r["Ola"] for r in output_rows)
    no_backend = [r["ID"] for r in output_rows if r["Backend localizado"].startswith("Sin módulo")]
    existing_rows = [r for r in output_rows if r["Estado matriz"] == "Ya existe y funciona"]
    summary_path = OUT_DIR / "08-inventario-funcional-fase1.md"
    with summary_path.open("w", encoding="utf-8") as f:
        f.write("# Inventario funcional trazable · Fase 1\n\n")
        f.write("Este inventario concilia la matriz definitiva con el plan de olas y con la estructura real del repositorio. ")
        f.write("La presencia de una carpeta o prueba indica un punto de entrada, no acredita por sí sola que la funcionalidad esté terminada.\n\n")
        f.write("## Control de integridad\n\n")
        f.write(f"- Funcionalidades Fase 1: **{len(output_rows)}**.\n")
        f.write(f"- Horas funcionales base: **{sum(float(r['Horas base']) for r in output_rows):g} h**.\n")
        f.write(f"- Estados: {', '.join(f'{k}: {v}' for k, v in statuses.items())}.\n")
        f.write(f"- Marcadas como existentes y con 0 h: **{len(existing_rows)}**; las 35 conservan QA, regresión, evidencia y UAT pendientes.\n")
        f.write(f"- Sin módulo backend dedicado localizado: **{len(no_backend)}** ({', '.join(no_backend)}).\n")
        f.write("- Archivo operativo completo: [inventario-funcional-fase1.csv](inventario-funcional-fase1.csv).\n\n")
        f.write("## Distribución por ola\n\n")
        for wave, count in sorted(waves.items()):
            f.write(f"- {wave}: {count} funcionalidades.\n")
        f.write("\n## Regla para las 35 existentes\n\n")
        f.write("Cada fila debe pasar por levantamiento local, ejecución del caso, evidencia, regresión y presentación en UAT. ")
        f.write("Hasta entonces su resultado permanece `Pendiente`; no se considera cerrada por tener 0 horas de construcción.\n\n")
        f.write("## Riesgos detectados por estructura\n\n")
        f.write("- Comercio Exterior y Activos Fijos no tienen módulo dedicado localizado en el repositorio actual.\n")
        f.write("- Contabilidad sólo tiene puntos parciales en Centros de Costo/Compartido; no se localizó un módulo contable dedicado.\n")
        f.write("- Las rutas candidatas deben sustituirse por archivo/endpoint/pantalla exactos durante la toma de cada tarea.\n")
        f.write("- La asignación singular sigue expresada como Dev 1/Dev 2; debe mapearse nominalmente antes de publicar en ClickUp/Notion.\n\n")
        f.write("## Criterio de cierre por fila\n\n")
        f.write("No puede pasar a terminado sin: PR revisado, pruebas aplicables, migración/configuración documentada, recorrido reproducible, evidencia, regresión y aceptación funcional cuando corresponda.\n")

    print(csv_path)
    print(summary_path)


if __name__ == "__main__":
    main()
