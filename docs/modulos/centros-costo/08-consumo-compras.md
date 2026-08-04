# Consumo del CC-Máquina en la cadena de compras — Fase E

> Diseño del consumo del centro de costo de máquina (Dim3) en los documentos
> de compras. Decisión de fondo y semánticas: **ADR-0050**. Este doc aterriza
> el ADR en los cuatro documentos, la mecánica de autorización y el desglose
> de PRs. El espejo del frontend vive en `05-frontend-diseno.md` §4.3.

## 0. Cómo leer

- **Alcance acotado** (indicación del superior): solo el flujo de **compras**.
- **Cuatro documentos** en alcance: RQ, OC, entrada (recepción), salida.
- **Fuera, diferido**: nota de crédito y **entrada manual** (verificado en
  Fase 1 que hoy no existe entrada manual — toda entrada es contra OC).
- **Etiqueta en documentos**: "CC-Máquina". **Display**: `clave — nombre`, sin
  jerarquía (CC-G4 cerrado; la jerarquía es de los reportes de la Fase F).

## 1. El modelo de consumo por documento

Las tres semánticas del ADR-0050, aterrizadas. `CC` = CC-Máquina (Dim3).

| Documento | Semántica | Quién captura (permiso) | Selector | Origen del dato |
|---|---|---|---|---|
| **RQ** | Elegido-filtrado | Requisitante (`compras.requisiciones.crear`) | **Filtrado por alcance** | Captura del requisitante (dueño) |
| **OC — línea desde RQ** | Heredado-solo lectura | — | ninguno | Copia de la línea de RQ |
| **OC — línea manual** (FOC11) | Elegido-abierto-proxy | Comprador (`compras.ordenes.crear-sin-rq`) | **Abierto** (todas activas) | Captura del comprador (proxy) |
| **Entrada** (recepción) | Heredado-solo lectura | — | ninguno | Copia de la línea de OC |
| **Salida con RQ** | Heredado-solo lectura | — | ninguno | Copia de la línea de RQ |
| **Salida por vale** | Elegido-abierto-proxy | Almacenista (`almacen.salidas.por-vale`) | **Abierto** (todas activas) | Captura del almacenista (proxy) |

**Obligatorio** en todo origen de captura (RQ, línea manual OC, vale). Aguas
abajo el heredado siempre trae valor **si el origen post-cutover lo trae**;
las RQ/OC previas al cutover pueden bajar en null (ver §6, bloqueo de arranque).

## 2. Estado del campo hoy (mapa Fase 1, con `archivo:línea`)

| Documento | Dominio / columna | FE | Estado hoy |
|---|---|---|---|
| RQ | `LineaRequisicion.CentroCostoId` (`Guid?`), `LineaRequisicionConfiguration.cs:98`, col `compras.requisicion_lineas.centro_costo_id` | `LineaInlineForm.tsx:443-447` (**`<Input>` texto GUID**), schema `linea.ts:57` | Columna viva, capturable como texto libre → el **picker la sustituye** |
| OC | `LineaOrdenCompra` — **sin** `CentroCostoId`; col inexistente | — | **Columna nueva** + propagación (hoy los handlers `…DesdeRequisicion…` no copian nada) |
| Entrada | `LineaMovimiento.CentroCostoId` (`Guid?`), col `almacen.lineas_movimiento.centro_costo_id` | — | **Columna muerta**: recepción no la puebla → heredar de OC |
| Salida | `LineaMovimiento.CentroCostoId` (misma col) | `nueva-salida-helpers.ts:72` (prefill FE desde RQ), schema `salida.ts:27,72` | Viva y prefilled; falta herencia **autoritativa en backend** + rama vale |

Datos que fundamentan el cut (millet_dev): OC = **36 líneas desde RQ / 33
manuales de 69** (~48% manual); salida = **86 con RQ / 1 vale**; línea de
salida = 51/91 con `LineaRqId` (el resto cae al fallback header+artículo);
`MaquinaDestinoId` (cabecera salida) = **0/87 poblado**, sin FK → vestigial.

## 3. Mecánica de autorización — el backend abre el selector

Dos caminos de **captura** (selector) y uno de **display** (resolución de
nombre). Ninguno lo decide el FE.

- **Selector filtrado (RQ)** — `GET /dim3/buscar` → `BuscarDim3Query`, typeahead
  top-N filtrado por `AlcanceDim3Evaluator`. Ya existe. Autorización: permiso de
  lectura del selector.
- **Selector abierto (proxy: vale + línea manual OC)** — misma query **sin** el
  filtro de alcance (typeahead sobre todas las activas), detrás de un endpoint
  gateado por el permiso de la población proxy:
  - Vale → endpoint bajo Almacén, `RequireAuthorization(almacen.salidas.por-vale)`.
  - Línea manual OC → endpoint bajo Compras, `RequireAuthorization(compras.ordenes.crear-sin-rq)`.
  Un requisitante que llame estos endpoints → **403**. La query abierta **no**
  usa el evaluador → CentrosCosto sigue dependency-free.
- **Display heredado (OC, entrada, salida, y re-display de RQ por otro usuario)**
  — **read-port batch** `IDim3ReadPort` (nuevo, ADR-0042): `{ids} → {clave,
  nombre, activa}`, **sin filtro de alcance**, **incluye inactivas** (ADR-0049).
  Adaptador público en CentrosCosto, consumido por Compras y Almacén.

> Precedente exacto: `esAlcanceTotal` (FE-PR3) se resolvió en el endpoint vía
> `IPermissionLoader`; aquí el gate también es del endpoint. Y ya existe
> `centros_costo.dim3.leer-todos` como permiso que bypassa alcance (global) —
> el proxy es el mismo principio, pero **contextual** (por endpoint), no global.

### Verificación de roles (Card 2) — con un hueco a reportar

Holders reales en millet_dev (roles del sistema):

- `almacen.salidas.por-vale` → **Almacenista** (+ SuperAdmin).
- `compras.ordenes.crear-sin-rq` → **Encargado de Compras** (+ SuperAdmin).
- `compras.requisiciones.crear` → **Requisitante de Compras** (+ SuperAdmin).

**Hueco**: el superior nombró "3 roles de almacén" (almacenista, jefe de
almacén, encargado); el catálogo tiene **un solo rol de almacén: Almacenista**.
No existen "Jefe de almacén" ni "Encargado de almacén" (el "Encargado"/"Jefe"
del catálogo son de **Compras**). El mecanismo no cambia — quien tenga
`por-vale` ve todo —, pero **cuando se creen esos roles hay que otorgarles
`por-vale`**. No es trabajo de la Fase E; es una nota para Identidad.

## 4. El vale y su regularización

- El vale (salida sin RQ) es la única salida **elegida**; el resto hereda.
- El CC-Máquina **no se sobreescribe al regularizar** (ADR-0050). Gasto contado
  una vez en la salida; la RQ regularizadora lleva su propia CC-Máquina sin
  movimiento; divergencia en papel aceptada, sin doble conteo.
- Correcto por construcción: `RegularizarVale` solo toca `RqRegularizadoraId` +
  `PendienteRegularizacion`; nunca las líneas. El `RegularizacionValeSlaWorker`
  solo notifica, no regulariza (lo hace el `RegularizarSalidaPorValeCommand`).

## 5. La línea manual de OC — dato de menor confianza

- Es proxy (comprador, `crear-sin-rq`), misma mecánica que el vale: selector
  abierto. Obligatoria en el flujo cerrado: PR3 entregó el picker abierto sin
  obligar y el **flip** a `NotNull` se desprendió a **PR3.1** (§7), **ya
  activo** — `LINEA_OC_MANUAL_CENTRO_COSTO_REQUERIDO`.
- **Menor confianza** (ADR-0050): en dev, las 33 manuales traen
  `departamento_solicitante_id` (33/33) pero **cero** máquina y **cero** texto
  libre; solo 2 departamentos distintos. Un departamento agrupa muchas máquinas
  → **no hay cómo derivarla**; el comprador la elige de ~361 a mano.
- **El hueco baja**: la entrada hereda de la línea de OC; con la manual poblada
  a mano, la entrada hereda ese valor (de menor confianza), no null. En el
  trace de dev, 6/6 líneas de entrada que trazan a OC vienen de líneas
  manuales — de ahí la insistencia en no dejarlas null.

## 6. Bloqueo de arranque y rollout (Card 1)

La obligatoriedad en RQ bloquea a todo requisitante **sin alcance** el día 1.
Secuencia decidida: **picker primero, obligatorio después**, sin flag.

1. Se entrega el picker + display (PR2) **sin** obligar.
2. Se siembra alcance a los requisitantes (runbook operativo, fuera de código).
3. Un cambio mínimo activa la obligatoriedad cuando ya hay alcance.

Obligatoriedad **a nivel app** (validator) primero; un `NOT NULL` en BD solo
tras backfill (un `NOT NULL` directo truena con las filas viejas en null). El
heredado aguas abajo **no** es obligatorio por sí mismo: hereda lo que el
origen traiga (siempre presente para orígenes post-cutover).

## 7. Desglose de PRs (el cut, justificado)

Cinco PRs: uno de infraestructura compartida + uno por documento. Orden lineal
**PR1 → PR2 → PR3 → PR4 → PR5** siguiendo la cadena de herencia.

### PR1 — Toolkit de consumo CeCo (infra compartida, sin cablear documentos)

- **Backend**: read-port batch `IDim3ReadPort` (`{ids} → {clave, nombre,
  activa}`, sin alcance, incluye inactivas), adaptador público en CentrosCosto.
  Query del **selector abierto** (BuscarDim3 sin el filtro de alcance).
- **Frontend**: componente `Dim3Picker` (combobox sobre el endpoint del
  selector, con modo filtrado/abierto según el documento) + hook
  `useCcMaquinaLabel` (resuelve `clave — nombre` vía el read-port para displays
  heredados).
- **Cut**: todo display heredado (OC, entrada, salida) y el re-display de RQ
  por otro usuario dependen del read-port; ambas capturas elegidas dependen del
  picker. Aislarlo evita repetir la infra en 4 PRs y encapsula el "ver ≠ elegir"
  en un solo lugar. Sin valor de cara al usuario aún; desbloquea los 4 documentos.

### PR2 — RQ (origen elegido-filtrado), sin obligatoriedad

- FE: `<Input>` de GUID → `Dim3Picker` **filtrado por alcance** (`/dim3/buscar`),
  en sub-fila propia siempre-visible. Prellenado si el alcance resuelve a
  **exactamente 1** máquina (F1 Card 2: FE con `/dim3/buscar?limit=2`).
- Backend: enriquece `LineaResponse` con `CentroCostoClave/Nombre` vía
  **`IDim3ReadPort`** (primera cableada real del read-port de PR1). Display
  `clave — nombre` en el detalle; "No catalogado" para ids irresolubles.
- **Campo OPCIONAL** — schema `nullish` y validators **intactos**. La
  obligatoriedad se **desprende a PR2.1** (F1 Card 1: picker primero,
  obligatorio después, sin ventana de bloqueo).
- **Dep**: PR1 (picker + read-port).

### PR2.1 — RQ: flip a obligatorio (ACTIVO)

- **Obligatorio en RQ desde PR2.1.** `linea.ts` → `centroCostoId` requerido
  (`idLike`, empty string bloquea al submit) + `RuleFor(c => c.CentroCostoId).NotNull()`
  con código `LINEA_RQ_CENTRO_COSTO_REQUERIDO` en los 2 validators
  (Agregar/Actualizar). El validador da HTTP 400 (no 422). `NOT NULL` de BD
  solo tras backfill (no en PR2.1).
- **Sin esperar sembrado masivo de alcance** (decisión del owner): la
  configuración se dispara **por uso real** — si al capturar una RQ el picker
  filtrado sale vacío, el capturista reporta al admin de CentrosCosto, que le
  asigna CCs en `/centros-costo/asignaciones` (Fase C, FE-PR3 #661); el
  capturista refresca y continúa. Config por demanda, no big-bang.

### PR3 — OC (heredado desde RQ + elegido-abierto manual), sin obligatoriedad

- Migración: `orden_compra_lineas.centro_costo_id` (uuid nullable).
- Línea **desde RQ**: propagación en `CrearOrdenCompraDesdeRequisicionHandler`
  y `AgregarLineaDesdeRequisicionHandler` (hoy no copian). Heredado, solo lectura
  — decisión F1 (Victor): hereda **y se BLOQUEA** (Opción 1), guarda de dominio
  `LINEA_OC_CC_HEREDADO_INMUTABLE` en `ActualizarLineaOc`.
- Línea **manual** (`AgregarLineaManualOcCommand`, `crear-sin-rq`): `Dim3Picker`
  **abierto** (proxy, §5) contra el endpoint gateado
  `/api/v1/compras/ordenes/dim3/buscar` (`BuscarDim3AbiertoQuery`, sin filtro de
  alcance). El backend abre el selector, no el FE.
- Display `clave — nombre` (heredado solo lectura / capturado) vía `IDim3ReadPort`.
- **Campo OPCIONAL en PR3** — schemas `nullish`, validators intactos. La
  obligatoriedad de la línea manual se **desprendió a PR3.1** (mismo criterio
  que RQ/PR2.1: picker primero, obligatorio después, sin ventana de bloqueo)
  y **ya está activa** — ver PR3.1 abajo.
- **Dep**: PR1 (picker abierto + read-port), PR2 (RQ ya puebla el origen).

### PR3.1 — OC: flip a obligatorio en la línea manual (ACTIVO)

- **Obligatorio en la línea manual desde PR3.1.** Validator de
  `AgregarLineaManualOcCommand` → `RuleFor(c => c.CentroCostoId).NotNull()` con
  código `LINEA_OC_MANUAL_CENTRO_COSTO_REQUERIDO` (HTTP 400, no 422, igual que
  PR2.1). Solo aplica a la línea **manual**: la heredada toma lo que traiga la
  RQ, que ya lo obliga desde PR2.1, y aquí es read-only
  (`LINEA_OC_CC_HEREDADO_INMUTABLE`).
- **Sin esperar sembrado masivo** (mismo modelo que PR2.1): el comprador que
  intente guardar sin CC recibe el 400; el admin le asigna CCs en
  `/centros-costo/asignaciones` si aplica, y continúa. Config por demanda, no
  big-bang. Nota: el picker de OC es **abierto** (proxy), así que el comprador
  ve todo el catálogo — el alcance no lo bloquea, solo tiene que elegir.
- **El PATCH no se rompe.** `ActualizarLineaOcCommand` es PATCH parcial
  (`null` = *no tocar*), así que la obligatoriedad **no** se implementó como
  regla de input — eso habría obligado a reenviar el CC en cada edición
  parcial. Vive en el dominio como invariante de **POST-ESTADO**
  (`LineaOrdenCompra.ActualizarEstructural`): si tras aplicar el patch la línea
  es manual y queda sin CC, lanza `LINEA_OC_MANUAL_CENTRO_COSTO_REQUERIDO`
  (422, consistente con la guarda vecina). Efecto neto: cambiar solo el precio
  de una línea que ya trae CC sigue funcionando, y editar una línea manual
  **legada** (anterior a PR3.1, con CC en null) obliga a completarlo en esa
  misma edición — backfill por demanda.
- **FE: la exigencia es condicional.** `crearAgregarLineaManualSchema` toma un
  segundo parámetro `ccRequerido`, y `LineaInlineFormOc` pasa `!lineaDesdeRq`.
  Razón: ese schema resuelve el form en los dos modos, y en la línea heredada
  el campo se pinta **read-only** — si fuera requerido siempre, una línea
  heredada legada con CC null quedaría imposible de guardar (requerida pero no
  capturable). El tipo inferido no cambia en ninguna rama.
- **Duplicar OC copia el CC** (`DuplicarOrdenCompraHandler`). Hueco heredado de
  PR3 que PR3.1 vuelve material: duplicar convierte **todas** las líneas en
  manuales y las crea por dominio, sin pasar por el comando validado — sin
  copiar el CC, cada OC duplicada nacía con líneas manuales en null, saltándose
  el obligatorio y perdiendo el dato del origen en silencio. La línea origen
  heredada se copia como manual y su CC pasa a ser editable, que es lo correcto:
  ya no cuelga de una RQ.
- `NOT NULL` de BD solo tras backfill (no en PR3.1).

### PR4 — Entrada / recepción (heredado-solo lectura)

- Poblar la columna muerta `almacen.lineas_movimiento.centro_costo_id` en
  `RegistrarRecepcionConFacturaCommand` y `…ConPackingListCommand`, heredando de
  la línea de OC vía `oc_linea_id` (fallback por artículo, mismo patrón que ya
  usan para la unidad). Display solo lectura.
- **Dep**: PR3 (OC poblada).

### PR5 — Salida (heredado con RQ + elegido-abierto vale) + muere MaquinaDestinoId

- Con RQ: herencia **autoritativa en backend** (antes solo prefill FE) vía
  `LineaRqId`, fallback `RqId` de cabecera + artículo. Solo lectura en variante A.
- Vale: `Dim3Picker` **abierto** (gateado por `por-vale`). **Campo OPCIONAL**
  en PR5 (schema `nullish`) — **sin PR5.1**: el almacenista siempre elige
  explícitamente al capturar el vale y la salida heredada no captura (hereda),
  así que no hay validator "flippeable" que desprender (mismo razonamiento que
  PR4). El endpoint abierto es `/api/v1/almacen/salidas/dim3/buscar`.
- **Muere `MaquinaDestinoId`**: fuera del `NuevaSalidaSheet`, del command/schema,
  y `DROP COLUMN` (0 datos, sin FK). Era de **cabecera** (`MovimientoInventario`,
  1 por salida); el CC-Máquina es **por línea** — no había move 1:1 sensato.
- **Read-port en Almacén**: Almacén **no** referencia CentrosCosto (cerraría un
  ciclo vía Compartido: `CentrosCosto→Compartido→Almacen`). Se usa el patrón
  `IComprasOcReadPort`: puerto propio `Almacen.Domain.Ports.ICentroCostoReadPort`
  + NoOp stub + adapter bridge en `Compras.Infrastructure` (delega en
  `CentrosCosto.IDim3ReadPort`) wireado en `Program.cs`. **No** hay arista de
  proyecto Almacén→CentrosCosto.
- **Dep**: PR1 (picker abierto + read-port). **No** depende de PR3/PR4.

### Grafo de dependencias

```
PR1 (toolkit) ──┬── PR2 (RQ) ──── PR3 (OC) ──── PR4 (entrada)
                │      └ PR2.1 (flip)  └ PR3.1 (flip)
                └── PR5 (salida)   [solo depende de PR1]
```

Los `.1` (flips a obligatorio) cuelgan de su PR base y se mergean **después**
de sembrar alcance; no están en el camino crítico de la herencia.

PR5 podría ir en paralelo tras PR1; el orden lineal mantiene la narrativa de
herencia (RQ es el origen) y evita rebases.

## 8. Consecuencias aceptadas / riesgos

- Captura por proxy = **menor confianza** (ADR-0050 §consecuencia). Un reporte
  de Fase F que no cuadre en el vale o en la línea manual **no es bug**.
  Mitigación: revisar/reasignar en el consumo.
- Vale regularizado: **dos CC-Máquina posibles** (vale ≠ RQ regularizadora), sin
  doble conteo. Divergencia en papel aceptada.
- Obligatoriedad gated por el sembrado de alcance (bloqueo de arranque).
- Roles de almacén: solo Almacenista existe hoy; los otros dos, al crearse,
  necesitan `por-vale` (nota para Identidad, §3).

## Rev.

| Versión | Fecha | Cambios |
|---|---|---|
| 0.1 | 2026-07-18 | Primera versión. Aterriza ADR-0050: 3 semánticas por documento, mecánica de autorización (selector abierto gateado por permiso proxy, read-port sin filtro), vale + línea manual OC como un solo concepto proxy, verificación de roles (hueco: solo Almacenista existe), bloqueo de arranque, desglose de 5 PRs con cut y grafo de dependencias, CC-G4 cerrado. |
| 0.2 | 2026-07-21 | **PR3.1 ACTIVO** (§5, §7): CC-Máquina obligatorio en la línea manual de OC. Documentados los dos matices que el molde de PR2.1 no cubría — invariante de **post-estado** en dominio para no romper el PATCH parcial, y exigencia **condicional** en el schema FE para no dejar sin salida a la línea heredada legada con CC null. |
