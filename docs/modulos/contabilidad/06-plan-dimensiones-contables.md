# Plan — F1-CON-02 Administrar dimensiones contables (`Millet.Contabilidad`)

> **Versión:** 0.2 (implementado con datos de prueba; ver §15) · **Fecha:** 2026-10-04
> **Tarea:** F1-CON-02 · **Responsable:** Uziel · **Rama:** `feature/F1-CON-02-dimensiones-contables`
> **Estado:** IMPLEMENTADO en la rama con datos base de prueba (`FIX-`). Pendiente: insumos de Millet (§12) y PR.
> **Fuente funcional:** ficha F1-CON-02 (planeación de ejecución 28-sep, ajustada). Estimación de la ficha: 6 h. Inicio 2-oct-2026, vence 5-oct-2026.
> **Dependencias:** F1-ADM-08 (centros de costo, `Millet.CentrosCosto`) y F1-CON-01 (catálogo contable, mergeado en `main` por PR #30).

---

## 0. Cómo leer este documento

§1 contexto y alcance · §2 hallazgos (lo que YA existe) · §3 decisiones de diseño (D1–D9) ·
§4 modelo de datos · §5 reglas de validación · §6 API · §7 permisos y sucursal · §8 UI ·
§9 migraciones · §10 pruebas · §11 fases y esfuerzo · §12 lo que se necesita de Millet ·
§13 dependencias de plataforma (ADR-0031) · §14 riesgos.

Todas las reglas, tipos de documento y movimientos de este plan son **de prueba** (prefijo
`FIX-`) y **no representan política oficial de Contabilidad**.

---

## 1. Contexto, alcance y anti-alcance

### Resultado esperado (ficha)
Reglas configurables **cuenta × tipo de documento × dimensión** con estado
**obligatorio / opcional / no aplicable**, vigencia y validación en API. La pantalla explica
qué dimensión falta o qué combinación no es válida.

### Alcance (en esta tarea)
- Entidad `ReglaDimension` con vigencia (desde/hasta), persistencia en el esquema
  `contabilidad`, comandos y consultas (MediatR) con el patrón hexagonal vigente.
- Catálogo mínimo de **tipos de documento contable** (ver D2).
- **Servicio de validación** de un movimiento (cuenta + tipo de documento + fecha + sucursal +
  dimensiones capturadas): dimensión obligatoria ausente, dimensión no aplicable capturada,
  centro inexistente o inactivo, centro fuera de la sucursal del movimiento, jerarquía
  Dim1→Dim2→Dim3 incongruente.
- Puerto público `IDimensionContableValidacionPort` para que Pólizas/CxP/Compras lo consuman
  después (sin cablear consumidores en esta tarea).
- Registro mínimo de **movimientos de prueba confirmados** para demostrar la conservación
  histórica (ver D7).
- Relación **centro ↔ sucursal** en `Millet.CentrosCosto` (ver D6) y puerto de validación de
  centros publicado por su dueño.
- Pantalla para configurar reglas y panel para probar un movimiento con mensajes claros
  (extiende el panel «Probar movimiento» de CON-01).
- Permisos canónicos nuevos y alcance por sucursal (ADR-0051).

### Anti-alcance (NO se hace)
- Pólizas y asientos reales (siguen diferidos desde CON-01).
- Cablear la validación en Compras, Almacén, CxP o Facturación (cada consumidor es su propia
  tarea; ADM-08 ya registró la brecha de validación al guardar en Compras).
- Crear otro catálogo de centros de costo: se reutilizan Dim1/Dim2/Dim3 de ADM-08 y sus IDs.
- Cargar reglas definitivas de Contabilidad ni presentar las reglas de prueba como política.
- Conciliar los datos M1 de ADM-08 (renombres, bajas, 4 NumEQ reubicados): sigue en ADM-08.
- Dimensiones distintas a los centros de costo (proyecto, línea de negocio, etc.).

---

## 2. Hallazgos: lo que YA existe (verificado contra `main` `9ecbcd2`, 2026-10-04)

### 2.1 Centros de costo (`Millet.CentrosCosto`, ADM-08)
- Jerarquía **Dim1 → Dim2 (CeCo) → Dim3 (NumEQ / máquina)**, cada nivel con `Clave`, `Nombre`
  y `Estatus` (`EstatusCatalogo`); baja lógica en cascada; padre inmutable.
- **Catálogo global**: sin `EmpresaId` y **sin relación con sucursales** (ADM-08 §Objetivo:
  "no se relacionará automáticamente con sucursales o departamentos"). El `ISucursalReadPort`
  del modelo anterior se eliminó (CECO-PR4).
- Alcance de selección **por usuario**: `Asignacion` (usuario → Dim3) + permiso
  `centros_costo.dim3.leer-todos`, evaluado por `IAlcanceDim3Evaluator`.
- Puerto público `IDim3ReadPort` (batch, incluye inactivas, sin alcance: "ver ≠ elegir",
  ADR-0050). No resuelve Dim1/Dim2 ni sucursal.
- Brecha abierta de ADM-08 (#3): ningún consumidor valida al guardar que el centro exista,
  esté activo y esté en alcance. El puerto de validación de esta tarea la cubre del lado del
  dueño (los consumidores se cablean aparte).

### 2.2 Catálogo contable (`Millet.Contabilidad`, CON-01)
- `CuentaContable` con `EmpresaId` (ADR-0011), sin sucursal; jerarquía por segmentos;
  `tipo` derivado (acumula / afectable, P19); rubros (P24); colectivas (P23).
- `ICuentaContableReadPort.ValidarParaMovimientoAsync(cuenta, origen)` y endpoint
  `POST /api/v1/contabilidad/cuentas/validar-movimiento` (panel «Probar movimiento»).
- Tabla `cuentas_contables_uso` (marca "cuenta con movimientos"). **No se escribirá** desde los
  movimientos de prueba: volvería "usadas" cuentas reales y bloquearía cambios (P20).
- Permisos `contabilidad.catalogo.leer|administrar|importar` (solo SuperAdmin).

### 2.3 Tipos de documento
- Solo existe `TipoDocumentoSerie` (Administración): `OrdenCompra, Cfdi, NotaCredito, Poliza,
  FacturaAnticipo`. Es para **folios**, no cubre factura de proveedor, entrada/salida de
  almacén, pagos ni asiento manual, y su `short` está fijo por ABI. No se reutiliza (D2).

### 2.4 Sucursal
- `SucursalScopeGuard.VerificarAsync` + `IUsuarioSucursalReadPort` (ADR-0051) ya existen y se
  reutilizan para "el usuario puede operar en la sucursal del movimiento".

---

## 3. Decisiones de diseño (propuestas; confirmar antes de codificar)

| ID | Decisión propuesta | Alternativa | Por qué |
|---|---|---|---|
| **D1** | **Dimensiones = niveles de CentrosCosto**: enum `DimensionContable { Dim1 = 1, Dim2 = 2, Dim3 = 3 }` (extensible al final). El movimiento captura el nivel más fino que tenga; los niveles superiores **se derivan** del árbol (Dim3 implica su Dim2 y Dim1). | Dimensiones libres configurables | La ficha prohíbe otro catálogo; Dim1–Dim3 ya son las dimensiones de Millet (CeCo/NumEQ). |
| **D2** | **Catálogo propio `tipos_documento_contable`** (clave, nombre, activo) en `contabilidad`, sembrado solo con tipos `FIX-` de prueba. | Reusar `TipoDocumentoSerie` | Ese enum es de folios, tiene 5 valores y no cubre los documentos contables. |
| **D3** | La regla aplica a una **cuenta o a una rama** (título que hereda a sus descendientes) y a un **tipo de documento o a todos** (`null`). Resolución: gana la más específica (cuenta exacta > ancestro más cercano; tipo específico > todos). | Solo cuentas afectables y tipo obligatorio | Con ~729 cuentas, configurar hoja por hoja es impráctico; Contabilidad suele pensar en rangos/rubros. |
| **D4** | **Vigencia** `vigente_desde` (obligatoria) / `vigente_hasta` (nula = abierta), por fecha contable. Sin traslape para la misma llave (empresa, cuenta, tipo, dimensión). **Cambiar la política = cerrar la vigencia + crear regla nueva**; una regla ya iniciada no se edita (solo se cierra). Las futuras sí se editan. | Edición con historial de auditoría | La historia queda en las filas; el movimiento referencia la regla exacta que lo validó. |
| **D5** | **Sin regla aplicable = opcional** (no bloquea). Configurable `Contabilidad:Dimensiones:SinReglaEs`. | Sin regla = rechazo | Con reglas de prueba, bloquear todo por defecto impediría operar. |
| **D6** | **Relación CeCo (Dim2) ↔ sucursal N:M** en `contabilidad.centros_costo_sucursal` (ver §15: se movió a Contabilidad). Dim3 hereda la de su Dim2. Interruptor `Contabilidad:Dimensiones:ExigirSucursalDelCentro` (encendido). Un centro **sin sucursales** se rechaza con "el centro no está asignado a ninguna sucursal". | Usar solo las asignaciones usuario→Dim3 | La ficha pide "centro de otra sucursal": hace falta saber a qué sucursal pertenece cada centro, y hoy ese dato no existe. |
| **D7** | **Movimiento de prueba confirmado**: tabla `movimientos_dimension_prueba` con el snapshot (cuenta, tipo, fecha, sucursal, dims, `regla_id`s aplicadas, resultado). `PLATFORM-TODO(<Polizas>)`: se retira cuando existan las pólizas. | No persistir nada | Sin un movimiento registrado no se puede probar la "conservación histórica" que pide la ficha. |
| **D8** | Permisos `contabilidad.dimensiones.leer`, `contabilidad.dimensiones.administrar`, `contabilidad.movimientos.validar` y bypass `contabilidad.movimientos.gestionar-todas-sucursales` (ADR-0041/0051). Solo SuperAdmin hasta que Millet diga qué roles los reciben. | Reusar `contabilidad.catalogo.administrar` | La ficha pide "permiso de configuración contable" separado del catálogo. |
| **D9** | **Contabilidad declara `ICentroCostoContabilidadPort`** (batch: existe, activo en la cadena, Dim1/Dim2 derivados) y CentrosCosto lo implementa (ver §15). Contabilidad no lee el esquema `centros_costo`. | Contabilidad lee tablas de CeCo | Regla de oro: cero acceso directo a tablas de otro módulo (patrón `IDim3ReadPort`). |

---

## 4. Modelo de datos

### 4.1 `contabilidad.tipos_documento_contable`
`id`, `empresa_id`, `clave` (único por empresa, ≤ 20), `nombre`, `activo`, auditoría, `version`.

### 4.2 `contabilidad.reglas_dimension`
| Columna | Tipo | Notas |
|---|---|---|
| `id` | uuid | v7 |
| `empresa_id` | uuid | ADR-0011, filtro global |
| `cuenta_id` | uuid | FK `cuentas_contables`; cuenta o rama (D3) |
| `tipo_documento_id` | uuid nulo | FK; nulo = todos los tipos |
| `dimension` | smallint | `DimensionContable` |
| `requerimiento` | smallint | `Obligatorio = 1, Opcional = 2, NoAplica = 3` |
| `vigente_desde` | date | obligatoria |
| `vigente_hasta` | date nulo | nula = abierta; `>= vigente_desde` |
| `es_prueba` | bool | marca las reglas `FIX-` (evidencia de la ficha) |
| `nota` | varchar(500) nulo | justificación |
| auditoría + `version` | | ETag/If-Match (ADR-0012) |

Índices: `(empresa_id, cuenta_id, tipo_documento_id, dimension, vigente_desde)` único;
restricción de **no traslape** con `EXCLUDE USING gist` sobre `daterange(vigente_desde,
vigente_hasta, '[]')` (requiere `btree_gist`; si la extensión no está en Azure Flexible
Server se valida en el handler dentro de transacción con bloqueo).

### 4.3 `contabilidad.movimientos_dimension_prueba` (D7)
`id`, `empresa_id`, `sucursal_id`, `cuenta_id`, `tipo_documento_id`, `fecha_contable`,
`dim1_id`, `dim2_id`, `dim3_id`, `reglas_aplicadas` (jsonb: `regla_id`, dimensión,
requerimiento), `confirmado_por`, `confirmado_en`. Solo inserción; nunca se recalcula.

### 4.4 `centros_costo.dim2_sucursales` (D6, módulo CentrosCosto)
`dim2_id`, `sucursal_id`, auditoría. PK compuesta. Sin FK cruzada a `compartido.sucursales`
(otro esquema); se valida contra `ISucursalReadPort`/lectura del módulo dueño.

---

## 5. Reglas de validación de un movimiento

Entrada: `cuentaId`, `tipoDocumentoId`, `fechaContable`, `sucursalId`, `dim1Id?`, `dim2Id?`, `dim3Id?`.

1. Cuenta válida para movimiento (`ICuentaContableReadPort`, CON-01).
2. Tipo de documento existe y está activo.
3. El usuario puede operar en `sucursalId` (`SucursalScopeGuard`; bypass por permiso).
4. Centros capturados: existen, **activos en toda la cadena** y **congruentes** (la Dim3
   pertenece a la Dim2 indicada, etc.) — vía `ICentroCostoValidacionPort`.
5. Centro de la sucursal del movimiento (D6).
6. Reglas **vigentes a `fechaContable`** para la cuenta (o su ancestro más cercano) y el tipo
   (o "todos"); por dimensión gana la más específica.
   - `Obligatorio` sin valor (directo o derivado) ⇒ error.
   - `NoAplica` con valor capturado **directamente** ⇒ error (los derivados no cuentan).
7. Respuesta con **todos** los errores (no solo el primero) y las reglas aplicadas.

Códigos nuevos (se documentan en `03-contrato-api.md`), mensajes en lenguaje de usuario sin
claves internas (lección de CON-01):

| Código | Mensaje de ejemplo |
|---|---|
| `CONTAB_DIM_OBLIGATORIA_FALTANTE` | «Para la cuenta 5101 en "Factura de proveedor" falta el **Centro de costo (Dim2)**.» |
| `CONTAB_DIM_NO_APLICA` | «La cuenta 1101 no lleva **Máquina (Dim3)** en este tipo de documento; quítala.» |
| `CONTAB_DIM_CENTRO_INACTIVO` | «El centro 30VM00 está dado de baja y no se puede usar en movimientos nuevos.» |
| `CONTAB_DIM_CENTRO_OTRA_SUCURSAL` | «El centro 30VT00 no pertenece a la sucursal Mérida.» |
| `CONTAB_DIM_CENTRO_SIN_SUCURSAL` | «El centro X no está asignado a ninguna sucursal.» |
| `CONTAB_DIM_JERARQUIA_INCONGRUENTE` | «La máquina VU056 no pertenece al centro 20PR01.» |
| `CONTAB_REGLA_VIGENCIA_TRASLAPADA` | «Ya hay una regla vigente del … al … para esta combinación.» |
| `CONTAB_REGLA_INICIADA_NO_EDITABLE` | «Esta regla ya está en vigor; ciérrala y crea una nueva.» |
| `CONTAB_REGLA_INCONSISTENTE` | «Dim2 "no aplica" contradice Dim3 "obligatoria" en la misma combinación.» |

---

## 6. API (`/api/v1/contabilidad`, ADR-0021)

| Método y ruta | Permiso | Notas |
|---|---|---|
| `GET /tipos-documento` | `dimensiones.leer` | |
| `POST /tipos-documento`, `PUT /tipos-documento/{id}` | `dimensiones.administrar` | Idempotency-Key, ETag |
| `GET /reglas-dimension?cuentaId&tipoDocumentoId&vigentesA&incluirPrueba` | `dimensiones.leer` | Paginado |
| `GET /reglas-dimension/{id}` | `dimensiones.leer` | ETag |
| `POST /reglas-dimension` | `dimensiones.administrar` | Idempotency-Key |
| `PUT /reglas-dimension/{id}` | `dimensiones.administrar` | Solo reglas futuras (D4); If-Match |
| `POST /reglas-dimension/{id}/cerrar` | `dimensiones.administrar` | Fija `vigente_hasta` |
| `GET /reglas-dimension/efectivas?cuentaId&tipoDocumentoId&fecha` | `dimensiones.leer` | Matriz resuelta para la UI de captura |
| `POST /movimientos/validar` | `movimientos.validar` | 200 con `{ valido, errores[], reglasAplicadas[] }`; no persiste |
| `POST /movimientos-prueba` | `movimientos.validar` | Valida y persiste (D7); 422 Problem Details si no es válido |
| `GET /movimientos-prueba`, `GET /movimientos-prueba/{id}` | `dimensiones.leer` | Muestra la regla con la que se validó |

En CentrosCosto: `GET|PUT /api/v1/centros-costo/dim2/{id}/sucursales` (permiso de
`centros_costo.catalogo.administrar`, ya existente).

---

## 7. Permisos y alcance por sucursal

- Nuevos en `PermisosCanonicos` + seed (migración Identidad) + espejo en
  `frontend/src/lib/auth/permission-codes.ts` (D8).
- Validación por **empresa** (filtro global ADR-0011) y por **sucursal** del movimiento
  (`SucursalScopeGuard`); el selector de centros en la pantalla de captura solo ofrece centros
  activos de la sucursal elegida **y** dentro del alcance del usuario (`IAlcanceDim3Evaluator`).
- La API repite la validación (no se confía en el selector).

---

## 8. UI (`frontend/src/features/contabilidad/`, design system Millet)

Leer antes `design-system/DESIGN.md`, `FIGMA.md`, `README.md`, `frontend/AGENTS.md` y
`frontend/docs/patrones-compras.md`.

1. **Reglas de dimensión** (`/contabilidad/dimensiones`): bandeja P2 filtrable por cuenta,
   tipo y "vigentes a fecha"; insignia «PRUEBA» en reglas `es_prueba`; sheet «Nueva regla»;
   acción «Cerrar vigencia»; historial de vigencias por combinación.
2. **Matriz efectiva**: al elegir cuenta + tipo + fecha, tabla Dim1/Dim2/Dim3 →
   Obligatorio/Opcional/No aplica con la regla de origen (propia o heredada de la rama).
3. **Probar movimiento** (extiende el panel de CON-01): selector de sucursal, tipo, fecha y
   centros; muestra cada error junto al campo que lo causa («Falta Centro de costo (Dim2)»).
4. **Movimientos de prueba**: lista y detalle con la regla que aplicó al confirmarse.
5. **CentrosCosto → configuración de CeCo**: pestaña "Sucursales" en el detalle de Dim2.

---

## 9. Migraciones

| Contexto | Migración |
|---|---|
| Contabilidad | `ContabilidadDimensionesReglas` (3 tablas, `btree_gist` si aplica) |
| Contabilidad | `SeedTiposDocumentoPrueba` (solo `FIX-`; o seed por endpoint en la evidencia) |
| CentrosCosto | `CentrosCostoDim2Sucursales` |
| Identidad | `SeedPermisosContabilidadDimensiones` |

Tras cada `pull` de `main`: correr migraciones de todos los contextos de
`tools/migration-contexts.txt` (lección de CON-01).

---

## 10. Pruebas (casos de la ficha + negativos)

| # | Caso | Nivel |
|---|---|---|
| T1 | Cuenta + tipo + dimensión obligatoria capturada ⇒ acepta | Unit + HTTP |
| T2 | Dimensión obligatoria ausente ⇒ 422 que **nombra** la dimensión faltante | Unit + HTTP |
| T3 | Centro inactivo (Dim3 o su Dim2/Dim1) ⇒ rechazo en movimiento nuevo | HTTP |
| T4 | Centro de otra sucursal ⇒ no aparece en el selector **y** la API lo rechaza | HTTP + Vitest |
| T5 | Conservación histórica: movimiento confirmado con regla A; se cierra A y se crea B más estricta; el movimiento sigue consultable mostrando A | HTTP |
| T6 | `NoAplica` capturada ⇒ rechazo; derivada por jerarquía ⇒ acepta | Unit |
| T7 | Herencia: regla en la rama aplica a la hoja; regla de la hoja gana | Unit |
| T8 | Traslape de vigencias ⇒ 409/422; editar regla iniciada ⇒ rechazo | HTTP |
| T9 | Validación a fecha pasada usa la regla vigente en esa fecha | Unit |
| T10 | Sin permiso `dimensiones.administrar` ⇒ 403; sin sesión ⇒ 401 | HTTP |
| T11 | Usuario sin la sucursal y sin bypass ⇒ 403; con bypass ⇒ acepta | HTTP |
| T12 | Jerarquía incongruente (Dim3 de otra Dim2) ⇒ rechazo | Unit |
| T13 | Sin regla aplicable ⇒ opcional (D5) | Unit |

Integración siempre en BD desechable (`tools/validate-integration-isolated.sh`), nunca en
`millet_dev` (CON-01 dejó residuos `contab-test-…`).

---

## 11. Fases, esfuerzo y commits

| Fase | Contenido | Estimado |
|---|---|---|
| F0 | Confirmar D1–D9 con el TL | 0.5 h |
| F1 | Dominio + persistencia + migraciones (Contabilidad, Identidad) | 2.0 h |
| F2 | CentrosCosto: `dim2_sucursales` + `ICentroCostoValidacionPort` | 1.5 h |
| F3 | Servicio de validación + puerto público + endpoints | 2.5 h |
| F4 | UI (reglas, matriz, probar, movimientos de prueba, pestaña sucursales) | 3.0 h |
| F5 | Pruebas T1–T13 + regresión CON-01/ADM-08 | 2.5 h |
| F6 | Evidencia, `03-contrato-api.md`, ADR, PR | 1.0 h |
| | **Total** | **≈ 13 h** |

> ⚠️ **Reestimación:** la ficha da 6 h y vence el **5-oct-2026**. El alcance completo
> (incluida la relación centro↔sucursal, que hoy no existe) cabe en ~13 h. Para 6 h habría que
> dejar fuera D3 (herencia por rama), la UI de movimientos de prueba y la pestaña de
> sucursales en CeCo (sembrarlas por seed `FIX-`). Decidir con el dueño antes de iniciar.

Commits propuestos (sin push ni commit hasta aprobación):
1. `docs(contabilidad): plan de F1-CON-02 dimensiones contables`
2. `feat(identidad): permisos canónicos de dimensiones contables (F1-CON-02)`
3. `feat(centros-costo): relación centro-sucursal y puerto de validación de centros (F1-CON-02)`
4. `feat(contabilidad): reglas de dimensión con vigencia y validación de movimientos (F1-CON-02)`
5. `test(contabilidad): casos nominales y negativos de dimensiones (F1-CON-02)`
6. `feat(contabilidad): pantalla de reglas de dimensión y prueba de movimientos (F1-CON-02)`
7. `docs(contabilidad): evidencia de F1-CON-02`

ADR a escribir: **0057 — Reglas de dimensión contable con vigencia y relación centro↔sucursal**.

---

## 12. Lo que se necesita de Millet (Contabilidad)

| # | Insumo | ¿Bloquea el cierre técnico? | Formato sugerido |
|---|---|---|---|
| M1 | **Relación CeCo ↔ sucursal** (qué centros usa cada sucursal; cuáles son corporativos/compartidos) | No para el cierre técnico (se prueba con datos `FIX-`); **sí** para usar la validación con datos reales | Excel: `ClaveCeCo` · `Sucursal(es)` · `Corporativo (S/N)` |
| M2 | **Lista de tipos de documento / tipos de póliza** que usa Contabilidad (equivalentes a los de SAP B1: factura de proveedor, pago, entrada de mercancía, asiento manual, …) | No | Excel: `Clave` · `Nombre` · `Equivalente SAP` |
| M3 | **Matriz de reglas de dimensión**: por cuenta o rango × tipo de documento, qué dimensión es obligatoria/opcional/no aplica y desde cuándo | No (la ficha dice "por confirmar"); sí para producción | Excel: `Cuenta o rango` · `Tipo doc` · `Dim1` · `Dim2` · `Dim3` · `Vigente desde` |
| M4 | 2–3 **pólizas reales de ejemplo** (pueden ir sin montos) para validar los mensajes | No | PDF o Excel |
| M5 | **Roles** que reciben `dimensiones.administrar` / `movimientos.validar` | No (queda SuperAdmin) | Respuesta escrita |
| M6 | Decisiones pendientes de ADM-08 (renombres M1, 4 NumEQ reubicados, vigencias) | No para CON-02; afecta los datos de centros | Ya solicitado en ADM-08 |

Regla vigente de ADM-08: **no se activa obligatoriedad por cuenta o documento sin aprobación
expresa de Millet**. Por eso todas las reglas de esta tarea se cargan como `es_prueba = true`.

---

## 13. Dependencias de plataforma pendientes (ADR-0031)

| Pieza | Ticket | Stub en uso | Cómo se wirea |
|---|---|---|---|
| Pólizas contables | `<Polizas>` | `movimientos_dimension_prueba` (D7) | La póliza llama a `IDimensionContableValidacionPort` al confirmar y guarda los `regla_id`; se retira la tabla de prueba |
| Consumidores (Compras, Almacén, CxP) | `<DimensionesConsumidores>` | Ninguno cableado | Cada módulo define su puerto y un adaptador delgado (patrón `<ContabilidadCuentasConsumidores>`) |

---

## 14. Riesgos

- **Sucursal sin dato fuente:** si Millet no entrega M1, la regla "centro de otra sucursal" solo
  se demuestra con datos de prueba; el interruptor D6 permite apagarla en producción.
- **`btree_gist`** puede no estar habilitada en Flexible Server ⇒ alternativa en handler.
- **Datos M1 de ADM-08 sin conciliar:** las claves de centros pueden cambiar; las reglas
  referencian IDs (no claves), así que sobreviven a renombres.
- **Calendario:** 6 h / vence 5-oct vs ≈ 13 h estimadas (§11).

---

## 15. Cómo quedó implementado (v0.2, 2026-10-04)

Se construyó el alcance completo con **datos base de prueba**; cuando lleguen los insumos de §12 se cargan como datos/configuración,
sin cambiar código. Contrato: `07-contrato-api-dimensiones.md`. Decisión: ADR-0057.

Ajustes respecto de v0.1:

- **D6 en Contabilidad, no en CentrosCosto.** La tabla es `contabilidad.centros_costo_sucursal`: es el alcance *contable* del centro y
  así no se contradice la decisión de ADM-08 de no relacionar su catálogo con sucursales. La UI está en Contabilidad → Dimensiones →
  «Centros por sucursal» (no en una pestaña de CeCo).
- **D9 con puertos de consumidor.** Contabilidad declara `ICentroCostoContabilidadPort` e `ISucursalContabilidadPort`; los implementan
  `CentrosCosto.Infrastructure.PublicAdapters.ContabilidadCentroCostoAdapter` y `Compartido.Infrastructure.PublicAdapters.SucursalContabilidadAdapter`
  (cableados en `Program.cs`). Contabilidad no referencia esos módulos; ellos referencian a Contabilidad (sin ciclos).
- **Sin `CONTAB_REGLA_INCONSISTENTE`.** Como "no aplica" solo rechaza lo capturado directamente, «Dim2 no aplica + Dim3 obligatoria» es
  satisfacible (se captura solo la Dim3). No hace falta validar consistencia al crear.
- **Sin retroactividad por defecto** (`CONTAB_REGLA_VIGENCIA_RETROACTIVA`), configurable con `PermitirVigenciaRetroactiva` para la carga real.
- **Sin `btree_gist`**: el no-traslape se valida en el handler bajo `pg_advisory_xact_lock`; índice único `(empresa, cuenta, tipo, dimensión, desde)`
  con `NULLS NOT DISTINCT`.
- Endpoint adicional `GET /sucursales` (todas las de la empresa) para configurar centros sin depender del alcance del usuario.
- Rótulos de dimensión: los del catálogo de Centros de Costo («Dimensión 1/2/3», helper `etiquetaNivel`), también en los mensajes del backend.

Datos de prueba locales: `tools/datos-prueba-f1-con-02.sql` (idempotente, marcado `created_by = 'seed-f1-con-02-prueba'`, con bloque de retiro).

