# 02 — Muestra y mapeo del catálogo contable (F1-CON-01)

> Todos los datos de este documento son **ficticios** (`FIX-*`). No hay cuentas, códigos ni nombres reales de Millet.
> Del archivo real recibido solo se documentan **estructura y estadísticas** (plan §20.10); el archivo vive fuera del repositorio.

## 1. Muestra ficticia (formato canónico)

```
codigo;nombre;naturaleza;tipo_cuenta;codigo_origen
FIX-100.00.00.00;FIX Raiz;Deudora;Titulo;O-1
FIX-100.10.00.00;FIX Hoja 1;Deudora;Afectable;O-2
FIX-100.20.00.00;FIX Hoja 2;Acreedora;Afectable;O-3
```

Padre deducido por segmentos del código (`Jerarquia.Modo = PorSegmentos`, P15): el padre es el código con el último segmento numérico
distinto de cero puesto a ceros (ancho preservado). Los segmentos no numéricos (`FIX`) son etiquetas y no cuentan.
Resultado: 1 raíz (nivel 1) y 2 hijas (nivel 2).

## 2. Mapeo de la hoja base provisional («Plan de cuentas-VILO», P16)

| Cabecera de la hoja | Campo del catálogo | Tratamiento |
|---|---|---|
| Número | `codigo` | obligatorio; texto; validado por `Codigo.Patron` / longitud |
| Cuenta | `nombre` | obligatorio |
| Código agrupador SAT | `codigo_agrupador` | opcional (varchar 30) |
| Nivel Contable | (no se guarda) | se **valida** contra el nivel derivado; discrepancia = advertencia `CONTAB_IMPORT_NIVEL_DISCREPANTE` |
| Nivel de cuenta SAT | — | no se mapea; advertencia `CONTAB_IMPORT_COLUMNA_SIN_MAPEO` |
| Tipo (categoría tipo SAP B1) | — | **no** se mapea a naturaleza ni a título/afectable; advertencia `CONTAB_IMPORT_COLUMNA_SIN_MAPEO` |
| (ausentes) naturaleza, título/afectable, código padre, cuenta de control, código de origen | `naturaleza`, `tipo`, jerarquía, `cuenta_control`, origen | no se suponen: `naturaleza`/`tipo` quedan en `NULL` = **pendiente de validación**; el padre se deduce por segmentos; control `Ninguna` |

La columna canónica de título/afectable se llama `tipo_cuenta` (alias `titulo_afectable`, `afectabilidad`, `account_type`) para no chocar con el `Tipo` de la hoja.

## 3. Estadísticas de la hoja base recibida (sin contenido)

849 filas, 729 con código y 120 vacías; códigos de forma `999.99.99.99` (717) y `999.99.99.999` (12, formato distinto de ancho fijo: son cuentas de depreciación acumulada con un cero adicional en el último segmento; con la jerarquía por segmentos **no** quedan huérfanas: 1 raíz y 11 con padre existente. Propuesta del TL, pendiente de Contabilidad: homologar a `999.99.99.99` en una copia de trabajo guardando el código original en `codigo_origen`; 0 colisiones);
niveles contables 1–4 (118/404/151/56); 8 filas con nivel contable distinto de los segmentos distintos de cero; 1 huérfana por padre inferido;
178 con hijas y 551 hojas; `Tipo` con 14 categorías y 4 vacías; agrupador SAT en 552 filas; 0 duplicados, 0 espacios sobrantes, 0 caracteres corruptos.
Las otras dos hojas («Plan de cuentas (5)», «Catalogo») tienen formatos de código distintos; cuál es la oficial está pendiente (ver `04-evidencia-f1-con-01.md`).

## 4. Estado del criterio «mapeo documentado»

Documentados: el formato del mapeo, la muestra ficticia y el mapeo provisional de la hoja base. **Pendiente**: el mapeo real y las equivalencias
SAP↔ERP (Contabilidad indicó que no quiere usar el catálogo actual de SAP; puede ser innecesario o solo para saldos iniciales).
