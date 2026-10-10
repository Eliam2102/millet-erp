# 09 — C1.0: cuentas de control propuestas y carga del catálogo de Laura

Fecha: 06-oct-2026. Ficha C1.0 (completa CON-01). Sin cambios de código: configuración, datos y evidencia.

## 1. Qué faltaba de CON-01

El PR #30 dejó provisionales la naturaleza, las cuentas de control, los permisos sin rol y la casilla «no afectable por asiento manual». La casilla corresponde a K10.1. Con el archivo de Contabilidad del 2-oct («Catalogo de cuenta propuesta Millet.xlsx», hoja «Plan de cuentas»):

- **Naturaleza:** viene en 685 de 739 códigos. **No se infiere** la de los demás (criterio acordado el 2-oct). Las 51 cuentas de orden (801–808…) quedan como pendiente de validación. Las 3 agrupaciones sin naturaleza son rubros o títulos y no cuentan como pendientes.
- **Fila B47 (`104.01.00.01`):** su padre por segmentos (`104.01.00.00`) no existe, y eso rechaza todo el archivo (`PuedeAplicar = false`). Se resuelve en el archivo de carga con `codigo_padre = 104.00.00.00`, padre explícito propuesto (nivel 2 declarado por Contabilidad). El original no se modifica.
- **Agrupadores SAT de las filas 589 y 590:** Excel los guardó como `601.41999999999996` y `601.42999999999995`. En el archivo de carga van como `601.42` y `601.43`, el valor que se presenta.
- **Familia 171** (12 códigos de 13 caracteres): se conserva literal y el importador la resuelve. Su naturaleza «Deudora» se conserva tal como llegó; la validación contable queda pendiente (depreciación acumulada).

## 2. Cuentas de control · PROPUESTA de VILO, pendiente de Contabilidad

Laura definió las colectivas como «CLIENTES, DEUDORES, PROVEEDORES, ACREEDORES, etc.». Todas son de nivel 2, sin hijas y afectables (verificado contra el archivo).

| Tipo | Códigos |
|---|---|
| Clientes | 105.01.00.00, 105.02.00.00 |
| Proveedores | 201.01.00.00, 201.02.00.00, 201.03.00.00, 201.04.00.00 |
| Deudores | 107.01.00.00, 107.02.00.00, 107.05.00.00 |
| Acreedores | 205.02.00.00 a 205.06.00.00; 251.02.00.00 a 251.06.00.00 |

**Fuera por ahora:** los anticipos de clientes (206.x) y de proveedores (120.x). Se pregunta a Contabilidad si entran en el «etc.».

**Por qué no va en `appsettings.json`:** las pruebas de integración corren con ese archivo. Una prueba exige la lista vacía (`CatalogoHttpTests`, configuración de formato) y otra agrega su propia cuenta en la posición 0 (`ImportacionHttpTests`). La propuesta se aplica por ambiente:

```json
{ "Contabilidad": { "Catalogo": { "CuentasControl": [
  { "Codigo": "105.01.00.00", "Tipo": "Clientes" },
  { "Codigo": "105.02.00.00", "Tipo": "Clientes" },
  { "Codigo": "201.01.00.00", "Tipo": "Proveedores" },
  { "Codigo": "201.02.00.00", "Tipo": "Proveedores" },
  { "Codigo": "201.03.00.00", "Tipo": "Proveedores" },
  { "Codigo": "201.04.00.00", "Tipo": "Proveedores" },
  { "Codigo": "107.01.00.00", "Tipo": "Deudores" },
  { "Codigo": "107.02.00.00", "Tipo": "Deudores" },
  { "Codigo": "107.05.00.00", "Tipo": "Deudores" },
  { "Codigo": "205.02.00.00", "Tipo": "Acreedores" },
  { "Codigo": "205.03.00.00", "Tipo": "Acreedores" },
  { "Codigo": "205.04.00.00", "Tipo": "Acreedores" },
  { "Codigo": "205.05.00.00", "Tipo": "Acreedores" },
  { "Codigo": "205.06.00.00", "Tipo": "Acreedores" },
  { "Codigo": "251.02.00.00", "Tipo": "Acreedores" },
  { "Codigo": "251.03.00.00", "Tipo": "Acreedores" },
  { "Codigo": "251.04.00.00", "Tipo": "Acreedores" },
  { "Codigo": "251.05.00.00", "Tipo": "Acreedores" },
  { "Codigo": "251.06.00.00", "Tipo": "Acreedores" }
] } } }
```

- **En local:** variables `Contabilidad__Catalogo__CuentasControl__{i}__Codigo` y `__Tipo` (archivo `.env` fuera del repositorio), o `dotnet user-secrets`.
- **En el ambiente demo:** app settings del App Service (G1.0).
- **Importante:** la lista se aplica **al importar**. Hay que configurarla antes de cargar el catálogo; si cambia después, se vuelve a importar.

## 3. Rol de Contabilidad (datos, sin código)

En la pantalla de Roles: crear «Contabilidad» con `contabilidad.catalogo.leer`, `.administrar` e `.importar`, más los permisos de dimensiones contables, y asignarlo al usuario DEMO. Qué personas lo reciben se define con Millet (P7).

## 4. Evidencia (06-oct-2026)

Base PostgreSQL **temporal** (puerto 55433, eliminada al terminar; `millet_dev` no se tocó), las 13 migraciones de `tools/migration-contexts.txt` y el API de `main` 4933872 con la propuesta del §2:

| Prueba | Resultado |
|---|---|
| Vista previa del archivo de carga | 746 filas leídas, **739 por crear, 0 errores**; 51 advertencias de naturaleza pendiente y 7 títulos omitidos; `puedeAplicar = true` |
| Aplicar | 201; 739 creadas |
| `GET /cuentas?pendientes=true` | **51** (cuentas de orden) — C1.0-a |
| 105.00.00.00 / 105.01.00.00 | nivel 1 «Titulo» / nivel 2 «Afectable» con control «Clientes» — CA10.1 |
| Validar movimiento 105.01.00.00 `Manual` | `ControlSoloAuxiliar` — **CA10.2**; con `AuxiliarCxC`: válido |
| Validar movimiento 201.01.00.00 `Manual` / `AuxiliarCxP` | `ControlSoloAuxiliar` / válido |
| Validar movimiento 105.00.00.00 `Manual` | `Titulo` (acumula) |
| Validar movimiento 801.01.00.00 `Manual` | `PendienteValidacion` — C1.0-a |
| Validar movimiento 601.85.42.00 `Manual` | válido (gasto afectable); agrupador `601.42` |
| Usuario sin permisos, vista previa | 403 `PERMISO_FALTANTE` — C1.0-b |
| `Millet.Contabilidad.UnitTests` | 136/136 |
| `Api.IntegrationTests` filtro Contabilidad | 52/52 |

El archivo de carga y el `.env` de la propuesta quedan fuera del repositorio (datos reales, plan §20.9).
