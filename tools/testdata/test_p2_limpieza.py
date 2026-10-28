"""Comprueba la limpieza de P2 con un doble de docker, sin ejecutar el gate."""
from pathlib import Path
import subprocess
import tempfile
import unittest

SCRIPT = (Path(__file__).resolve().parents[1] / "validate-integration-isolated.sh").read_text()
FUNCIONES = SCRIPT[SCRIPT.index("borrar_base_gate() {"):SCRIPT.index("trap cleanup")]
DOBLE = r'''
set -euo pipefail
pg_container=prueba
pg_user=prueba
pg_db=millet_gate_123
shared_db=true
tiempos_listos=false
audit_container=contenedor_prueba
audit_log_dir="$directorio/temporal"
mkdir "$audit_log_dir"
docker() {
  printf '%s\n' "$*" >> "$directorio/llamadas"
  if [[ "$*" == *"-At -c SELECT"* ]]; then
    printf '%s\n' ce_tpl_millet_gate_123 ce_integration_abc
  fi
}
'''


class LimpiezaP2Tests(unittest.TestCase):
    def ejecutar(self, accion):
        with tempfile.TemporaryDirectory() as carpeta:
            subprocess.run(["bash", "-c", 'directorio="$1"\n' + DOBLE + FUNCIONES + accion,
                            "p2", carpeta], check=True)
            return (Path(carpeta) / "llamadas").read_text()

    def test_compartida_quita_template_antes_de_borrar_y_limpia_clones(self):
        llamadas = self.ejecutar("cleanup\n")
        for nombre in ("ce_tpl_millet_gate_123", "ce_integration_abc", "millet_gate_123"):
            alterar = f'ALTER DATABASE "{nombre}" IS_TEMPLATE false'
            borrar = f'DROP DATABASE IF EXISTS "{nombre}" WITH (FORCE)'
            self.assertLess(llamadas.index(alterar), llamadas.index(borrar))
        self.assertNotIn("stop", llamadas)

    def test_sin_compartir_solo_detiene_contenedor(self):
        self.assertEqual(self.ejecutar("shared_db=false\ncleanup\n"), "stop contenedor_prueba\n")

    def test_identificador_con_comillas_se_escapa(self):
        llamadas = self.ejecutar("borrar_base_gate 'ce_integration_a\"b'\n")
        self.assertIn('DROP DATABASE IF EXISTS "ce_integration_a""b" WITH (FORCE)', llamadas)

    def test_corrida_muerta_limpia_su_plantilla(self):
        inicio = SCRIPT.index("  for stale in ")
        final = SCRIPT.index("  docker exec", SCRIPT.index("  done", inicio))
        bucle = SCRIPT[inicio:final]
        # PID imposible en esta prueba; no se consulta ningún proceso ajeno.
        llamadas = self.ejecutar('docker() { printf "%s\\n" "$*" >> "$directorio/llamadas"; '
                                 'if [[ "$*" == *"-At -c SELECT"* ]]; then echo millet_gate_999999999; fi; }\n'
                                 + bucle)
        self.assertIn('DROP DATABASE IF EXISTS "ce_tpl_millet_gate_999999999" WITH (FORCE)', llamadas)
        self.assertIn('DROP DATABASE IF EXISTS "millet_gate_999999999" WITH (FORCE)', llamadas)


if __name__ == "__main__":
    unittest.main()
