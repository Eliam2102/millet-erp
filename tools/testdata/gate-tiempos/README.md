# Ejemplo ficticio de tiempos del gate

Todos los nombres, tiempos y resultados de `fases.tsv` y del TRX son ficticios.
No son evidencia de ejecución o aprobación del ERP Millet.

Desde la raíz del worktree:

```bash
python3 tools/gate-tiempos.py tools/testdata/gate-tiempos
python3 tools/gate-tiempos.py tools/testdata/gate-tiempos --fallas
PYTHONDONTWRITEBYTECODE=1 python3 tools/testdata/gate-tiempos/verificar.py
```

Se esperan 2 aprobadas, 2 fallidas y 1 omitida. Compras acumula 6 s en 2 pruebas;
Almacén acumula 2 s en 3 pruebas. Las fallidas se ordenan Almacén, Compras.
