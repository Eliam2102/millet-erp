#!/usr/bin/env python3
"""Comprobaciones del lector con datos ficticios; no ejecuta el gate ni Docker."""

import contextlib
import importlib.util
import io
import tempfile
import unittest
from pathlib import Path

EJEMPLO = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location("gate_tiempos", EJEMPLO.parents[1] / "gate-tiempos.py")
gate = importlib.util.module_from_spec(spec)
spec.loader.exec_module(gate)


def salida(directorio, fallas=False):
    buffer = io.StringIO()
    with contextlib.redirect_stdout(buffer):
        gate.informe(directorio, fallas)
    return buffer.getvalue()


class TiemposTest(unittest.TestCase):
    def test_ejemplo(self):
        texto = salida(EJEMPLO)
        self.assertIn("Millet.Ejemplo.IntegrationTests\t2\t2\t1", texto)
        self.assertIn("Ejemplo.Compras\t6.000\t2\t3.000", texto)
        self.assertIn("Ejemplo.Almacen\t2.000\t3\t0.667", texto)
        self.assertLess(texto.index("total\t30\t100.0%"), texto.index("build\t8\t26.7%"))
        self.assertEqual(salida(EJEMPLO, True), "Ejemplo.Almacen.Falla\nEjemplo.Compras.Falla\n")

    def test_duracion(self):
        self.assertEqual(gate.segundos("1.02:03:04.5"), 93784.5)
        self.assertEqual(gate.segundos("00:00:00"), 0)

    def test_sin_trx_y_total_cero(self):
        with tempfile.TemporaryDirectory() as temporal:
            ruta = Path(temporal)
            (ruta / "fases.tsv").write_text("fase\tsegundos\tresultado\nbuild\t0\t7\ntotal\t0\t7\n")
            self.assertIn("build\t0\t0.0%\t7", salida(ruta))
            self.assertIn("No hay archivos TRX", salida(ruta))
            self.assertEqual(salida(ruta, True), "")

    def test_trx_sin_namespace_y_limite_25(self):
        with tempfile.TemporaryDirectory() as temporal:
            ruta = Path(temporal)
            (ruta / "fases.tsv").write_text("fase\tsegundos\tresultado\ntotal\t30\tok\n")
            definiciones = "".join(f'<UnitTest id="{i}"><TestMethod className="Clase{i}" name="Prueba" /></UnitTest>' for i in range(26))
            resultados = "".join(f'<UnitTestResult testId="{i}" outcome="Passed" duration="00:00:{i:02}" />' for i in range(26))
            (ruta / "Ejemplo.trx").write_text(f'<TestRun><TestDefinitions>{definiciones}</TestDefinitions><Results>{resultados}</Results></TestRun>')
            clases = [fila for fila in salida(ruta).splitlines() if fila.startswith("Clase") and not fila.startswith("Clase\t")]
            self.assertEqual(len(clases), 25)
            self.assertTrue(clases[0].startswith("Clase25\t"))
            self.assertFalse(any(fila.startswith("Clase0\t") for fila in clases))

    def test_total_ausente(self):
        with tempfile.TemporaryDirectory() as temporal:
            ruta = Path(temporal)
            (ruta / "fases.tsv").write_text("fase\tsegundos\tresultado\nbuild\t3\tok\n")
            with self.assertRaisesRegex(ValueError, "exactamente una fila total"):
                salida(ruta)


if __name__ == "__main__":
    unittest.main()
