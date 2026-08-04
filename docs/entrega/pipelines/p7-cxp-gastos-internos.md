# P7 — Gastos internos de CxP: comprobaciones, viáticos y tarjetas de crédito

> ✅ **Los tres flujos tienen evidencia e2e en dev**: comprobaciones (caja
> chica y aduanales) y viáticos el 2026-07-16, tarjetas de crédito el
> 2026-07-17 — vía API real con CFDIs de prueba cargados por
> `/cfdis/cargar`. IDs de evidencia en la sección "Cómo verificar el
> resultado".
>
> Los hallazgos de las verificaciones (P7-H1..H5 y TC-B1..B4, sección
> "Hallazgos") están corregidos salvo lo marcado explícitamente.

Tres flujos de control interno de CxP que no pasan por OC ni por la triada:
comprobación de gastos (caja chica y aduanales), viáticos de empleados y
tarjetas de crédito empresariales.

## Actores y permisos

| Flujo | Actor | Permisos requeridos |
|---|---|---|
| Comprobaciones | Auxiliar de CxP captura; aprobadores por tipo | `cuentas_por_pagar.comprobaciones.capturar`, `.aprobar-nivel1` (Jefe / Comercio Exterior), `.aprobar-nivel2` (Dirección de Finanzas) |
| Viáticos | Empleado, Jefe directo, DF, Auxiliar | `cuentas_por_pagar.viaticos.solicitar`, `.autorizar-jefe`, `.autorizar-df`, `.marcar-pagado`, `.capturar-comprobacion`, `.liberar` |
| Tarjetas de crédito | Auxiliar de CxP / Responsable | `cuentas_por_pagar.tc.registrar-movimiento`, `.cerrar-estado-cuenta`, `.administrar`, `.disputar`, `.leer` |

Desde #626/#630, **empleado, jefe directo y responsable son Empleados del
catálogo de Administración** (`/admin/empleados`), no usuarios de Identidad.
Para firmar como jefe directo, el usuario autenticado debe estar vinculado a
su empleado (campo "Usuario del sistema" del catálogo); si no lo está, el
API responde `VIA_USUARIO_SIN_EMPLEADO`.

## Flujo 1 — Comprobación de gastos (caja chica y aduanales)

**Objetivo:** documentar gastos sin OC (reembolsos de caja chica, gastos
aduanales) agrupando sus CFDIs en una comprobación con aprobación por niveles.
Ciclo real: `Borrador → PorRevisar → Autorizada → Aplicada` (+ `Rechazada`
terminal y `AutorizadaNivel1` intermedio solo de aduanales).

1. En `/cxp/comprobaciones`, crear con el Sheet correspondiente:
   **"Nueva comprobación — Caja chica"** o **"Nueva comprobación — Aduanales"**.
   - **Caja chica**: cada línea/CFDI genera una `FacturaProveedor` sin OC al
     crear. Una línea puede **vincular un CFDI del repositorio** (picker por
     RFC o carga del XML, #623): el sistema valida que exista, que siga
     `PorProcesar` y que el UUID coincida, y al crear lo marca
     `ConvertidoEnPasivo` copiando su `MetodoPago` a la factura (#622). El
     UUID de la línea puede omitirse si viene vinculada (se toma del CFDI).
   - **Aduanales**: no genera facturas; agrupa facturas **con OC ya
     capturadas** (`CapturarFacturaConOcCommand`) del mismo proveedor
     (agencia aduanal). `NumeroPedimento` obligatorio.
2. Enviar a revisión y aprobar:
   - **Caja chica**: aprueba el responsable de la sucursal (firma única) y
     después se aplica.
   - **Aduanales**: **doble firma obligatoria** — primero el Responsable de
     Comercio Exterior (nivel 1), después Dirección de Finanzas (nivel 2,
     autoriza las facturas ligadas). El segundo firmante **no puede ser la
     misma persona** (`COMP_NIVEL2_MISMO_USUARIO`, verificado).
3. **Aplicada** cierra el ciclo de captura y **acumula el monto en el
   saldo por reponer** de la (sucursal, destino) — el Sheet pide el
   destino con el selector **"Reponer a"** (cuenta de la sucursal o
   responsable de la caja). Cuando el saldo alcanza el **mínimo
   configurado** para la sucursal se emite automáticamente la
   **reposición** hacia la bandeja de Tesorería (beneficiario interno);
   sin mínimo configurado la emisión es inmediata. Saldos, mínimo por
   sucursal y **corte manual** ("Emitir ahora") viven en
   `/cxp/admin/reposiciones` (doc 12, GI-PR1/PR4). Las facturas por CFDI
   quedan en `Capturada` solo para gasto/IVA/DIOT: **no son pagables**
   (`FACTURA_GASTO_INTERNO_NO_AUTORIZABLE` — el proveedor ya cobró en
   efectivo; pagar es reponer la caja).
   📸 Captura pendiente: Sheet "Nueva comprobación — Aduanales".

**Errores verificados:** `COMP_CFDI_NO_ENCONTRADO` · `COMP_CFDI_YA_PROCESADO`
("El CFDI ... ya no está PorProcesar") · `COMP_CFDI_UUID_NO_COINCIDE` ·
`COMP_LINEA_CFDI_DUPLICADO` (mismo CFDI en dos líneas) ·
`COMP_ADUANALES_REQUIERE_DOBLE_FIRMA` (aduanales por el endpoint de firma
única) · `COMP_NIVEL2_MISMO_USUARIO` · `COMP_FACTURA_SIN_OC` (aduanales con
factura sin OC) · `COMP_FACTURA_YA_LIGADA` · `NumeroPedimento` obligatorio
(400 de validación). No verificados pero en código: `COMP_SIN_LINEAS`,
`COMP_LINEA_UUID_DUPLICADO`, `COMP_LINEA_FACTURA_DUPLICADA`,
`COMP_FACTURA_PROVEEDOR_MISMATCH`, `COMP_FACTURA_ESTADO_INVALIDO`.

## Flujo 2 — Viáticos

**Objetivo:** préstamo de viáticos a un empleado con política por puesto y
destino. **No** es un anticipo a proveedor (no lleva CFDI FANT); es un pasivo
de préstamo al empleado que se comprueba al regreso.

Estados: `Solicitada → AutorizadaPorJefe → (RequiereDireccionFinanzas →
AutorizadaCompleta) → Anticipada → ComprobacionCapturada → Liquidada`
(+ `Rechazada` antes del anticipo).

1. **Solicitar.** El empleado captura en `/cxp/viaticos` → **"Nueva solicitud
   de viáticos"**: empleado y jefe directo (selectores del catálogo de
   Administración, #630), destino, fechas, monto. Se valida contra la
   política (`politicas_viaticos`: monto máximo por día y días máximos según
   puesto y tipo de destino); el tope se calcula por días
   (`montoMaxDia × días`) y queda como snapshot en la solicitud.
2. **Autorizar.** El **jefe directo** autoriza — el sistema resuelve al
   empleado del usuario autenticado (#631) y debe coincidir con el jefe de
   la solicitud (`VIA_JEFE_NO_AUTORIZADO` si no). Si la solicitud **excede
   la política** (monto o días), exige justificación al solicitar
   (`VIA_JUSTIFICACION_REQUERIDA`) y tras la firma del jefe pasa a
   **Dirección de Finanzas** (`RequiereDireccionFinanzas`); DF debe ser
   persona distinta al jefe (`VIA_DF_MISMO_QUE_JEFE`, verificado).
3. **Pagar.** Al quedar autorizada, el **préstamo aparece solo en la
   bandeja de pasivos de Tesorería** ("Préstamo de viáticos", beneficiario
   empleado, vence en la fecha de salida). Cuando Tesorería registra el
   pago, la solicitud pasa a `Anticipada` **automáticamente** (GI-PR3).
   El botón **marcar pagado** queda como fallback manual para efectivo de
   ventanilla (Q2); ambos caminos exigen solicitud autorizada
   (`VIA_NO_ANTICIPABLE`).
4. **Comprobar.** Al regreso, el empleado captura su comprobación
   (**"Comprobar viáticos"**): líneas con CFDI del repositorio (validado
   `PorProcesar` + UUID, **sin consumirse todavía** — la captura es
   reemplazable, #622) y/o tickets no fiscales (que no pueden referenciar
   CFDI). El **Auxiliar de CxP la libera**: genera una `FacturaProveedor`
   sin OC por cada línea fiscal (no pagable — el empleado ya pagó con el
   anticipo), marca los CFDIs `ConvertidoEnPasivo` copiando `MetodoPago`,
   y calcula la **diferencia de liquidación** que cruza sola a Tesorería
   (GI-PR4): **positiva** = pasivo de reembolso al empleado en la
   bandeja; **negativa** = expectativa de depósito (`VIATICOS XXXXXXXX`)
   conciliable cuando el empleado devuelve la diferencia en el banco.

**Errores verificados:** `VIA_JUSTIFICACION_REQUERIDA` ·
`VIA_JEFE_NO_AUTORIZADO` · `VIA_DF_MISMO_QUE_JEFE` · `VIA_NO_ANTICIPABLE` ·
`VIA_CFDI_NO_ENCONTRADO` · `VIA_CFDI_YA_PROCESADO` ·
`VIA_CFDI_UUID_NO_COINCIDE` · `VIA_LINEA_CFDI_DUPLICADO` · ticket no fiscal
con CFDI vinculado (400 de validación).

**Configuración previa:** catálogo de **Puestos y Empleados** en
`/admin/puestos` y `/admin/empleados` (el jefe firma solo si su empleado
tiene "Usuario del sistema" asignado), `politicas_viaticos` en
`/cxp/admin/politicas-viaticos` (administra RH + Dirección; sin política
para puesto+destino la solicitud se rechaza con `VIA_POLITICA_NO_DEFINIDA`)
y aprobadores con límite en `/cxp/admin/aprobadores`.

## Flujo 3 — Tarjetas de crédito empresariales

> ✅ **Verificado e2e en dev (2026-07-17)** vía API real — tarjeta,
> movimientos con/sin CFDI, disputa, refund y ciclo completo de estado de
> cuenta hasta el pasivo del banco en Tesorería. La verificación encontró
> 4 hallazgos (TC-B1..B4), corregidos en #648.

**Objetivo:** controlar el gasto con TC corporativa. La regla central: al
registrar el movimiento de TC, la deuda con el proveedor del gasto queda
saldada (la factura del CFDI nace autorizada y pagada, solo para
gasto/IVA/DIOT) y **el pasivo se transfiere al banco emisor**; al corte se
paga como una factura del banco.

Ciclos: movimiento `Registrado → ConciliadoConEstadoCuenta → PagadoAlBanco`
(+ `EnDisputa`); estado de cuenta
`EnConciliacion → Conciliado → Cerrado → PagadoBanco`.

1. **Administrar tarjetas.** `/cxp/tc/tarjetas` → "Nueva tarjeta": emisora,
   **perfil de parser del banco** (p. ej. `AMEX_MX`), titular (empleado del
   catálogo), banco emisor (proveedor), límite, día de corte y de pago
   (permiso `.administrar`).
2. **Registrar movimientos.** `/cxp/tc/movimientos` → "Nuevo movimiento TC"
   con su CFDI (cada CFDI se registra individual para gasto/IVA/DIOT; el
   CFDI vinculado se valida `PorProcesar` y queda `ConvertidoEnPasivo`,
   #648) o sin CFDI (ticket). Un mismo CFDI no puede registrarse dos veces
   (`TC_MOV_FACTURA_DUPLICADA`, por el UUID efectivo del CFDI).
3. **Conciliar el estado de cuenta.** `/cxp/tc/estados-cuenta` → "Nuevo
   estado de cuenta TC" + **subir el archivo del banco** (Excel según el
   perfil de la tarjeta; debe incluir la **fila `TOTAL`/`SALDO` sin fecha**
   con el total declarado — sin ella, `EC_SIN_TOTAL_DECLARADO`). La
   conciliación automática matchea líneas ↔ movimientos (scoring con
   tolerancias internas de $0.50 / 2%); "Marcar conciliado" exige
   diferencia ≤ $0.01 contra el total declarado. Un archivo equivocado se
   recarga mientras no haya matches (`EC_RECARGA_CON_MATCHES` si ya los
   hay).
4. **Cerrar el corte.** **"Cerrar estado de cuenta"** genera la **factura
   agregada contra el banco** (`Capturada`), que sigue el flujo normal:
   autorizarla publica el pasivo y aparece en la bandeja de Tesorería como
   cualquier proveedor.
5. **Disputas y refunds.** "Disputar movimiento TC" marca el cargo en
   disputa (se excluye del cierre); "Registrar refund" captura la
   devolución del banco (no puede exceder el cargo original —
   `TC_REFUND_EXCEDE_ORIGINAL`).
   📸 Captura pendiente: estado de cuenta TC en conciliación.

**Errores verificados:** `TC_MOV_FACTURA_DUPLICADA` (post #648) ·
`TC_REFUND_EXCEDE_ORIGINAL` · `EC_DIFERENCIA_NO_CERO` ·
`EC_SIN_TOTAL_DECLARADO` (#648) · `EC_RECARGA_CON_MATCHES` (#648) ·
`EC_ARCHIVO_DUPLICADO` (mismo archivo por hash en otra tarjeta/corte).

## Hallazgos de la verificación (2026-07-16)

- **P7-H1 — ✅ CORREGIDO. El rechazo de una comprobación ahora compensa.**
  Rechazar una caja chica cancela las facturas generadas (con
  `FacturaProveedorCanceladaEvent` a Contabilidad) y regresa los CFDIs
  vinculados a `PorProcesar` para re-capturarlos en una comprobación
  corregida; rechazar una de aduanales libera las facturas agrupadas
  (dejan de bloquear `COMP_FACTURA_YA_LIGADA`). Los checks de UUID
  duplicado ignoran facturas `Cancelada`.
- **P7-H2 — ✅ CORREGIDO. Aduanales nivel 2 notifica a Tesorería.** La firma
  N2 publica `FacturaProveedorAutorizadaDomainEvent` por cada factura que
  autoriza → salen `pasivo.autorizado-para-pago.v1` (bandeja de Tesorería)
  y `factura.autorizada.v1` (Compras/Contabilidad).
- **P7-H3 — ✅ RESUELTO (GI-PR1..PR4, doc 12 v1.0).** El pasivo real de
  los gastos internos ya está modelado end-to-end: **reposición de caja
  chica** acumulada por (sucursal, destino) con mínimo configurable y
  corte manual (`/cxp/admin/reposiciones`), **préstamo de viáticos** con
  ida y vuelta automática (pago en Tesorería → `Anticipada`), y
  **liquidación** con reembolso al empleado o expectativa de depósito.
  Ver [`docs/modulos/cuentas-por-pagar/12-pasivos-internos-tesoreria.md`](../../modulos/cuentas-por-pagar/12-pasivos-internos-tesoreria.md)
  (PRs #640, #641, #642, #643, #645, #646). El **candado anti doble-pago**
  sigue vigente: las facturas por CFDI de caja chica/viáticos no son
  autorizables (`FACTURA_GASTO_INTERNO_NO_AUTORIZABLE`).
- **P7-H4 — ✅ CORREGIDO.** Las facturas de la liquidación de viáticos toman
  la **sucursal del empleado** (catálogo de Administración) y las líneas
  fiscales incompletas ya no se saltan: se validan al capturar (proveedor +
  CFDI/UUID obligatorios) y liberar rechaza con
  `VIA_LINEA_FISCAL_INCOMPLETA` si quedó alguna.
- **P7-H5 — ✅ CORREGIDO.** Solicitar viáticos valida que empleado y jefe
  existan **activos** en el catálogo (`VIA_EMPLEADO_NO_EXISTE` /
  `VIA_JEFE_NO_EXISTE`) y que el puesto de la solicitud sea el del empleado
  (`VIA_PUESTO_NO_COINCIDE`).

**Hallazgos del pipeline TC (2026-07-17) — corregidos en #648:**

- **TC-B1 — ✅. El mismo CFDI se registraba dos veces.** El dup-check solo
  corría cuando el UUID venía explícito; el vínculo por `cfdiRecibidoId`
  (el camino del picker) lo saltaba. Ahora deduplica por el UUID efectivo
  del CFDI y valida existencia/estado (`TC_CFDI_NO_ENCONTRADO` /
  `TC_CFDI_YA_PROCESADO` / `TC_CFDI_UUID_NO_COINCIDE` — patrón #622).
- **TC-B2 — ✅. El CFDI vinculado quedaba `PorProcesar` para siempre.**
  Ahora se marca `ConvertidoEnPasivo` apuntando a la factura y copia su
  `MetodoPago`.
- **TC-B3 — ✅. Conciliar sin total declarado daba un error críptico.**
  Ahora responde `EC_SIN_TOTAL_DECLARADO` explicando que falta la fila
  TOTAL/SALDO del archivo.
- **TC-B4 — ✅. Re-subir el archivo del banco tronaba con 500** (23505
  sobre `ux_linea_archivo_posicion`). Ahora la recarga reemplaza las
  líneas mientras no haya matches (`EC_RECARGA_CON_MATCHES` si ya los
  hay).

## Cómo verificar el resultado

| Qué | Dónde | Qué esperar |
|---|---|---|
| Comprobación aplicada | `/cxp/comprobaciones` | Estado `Aplicada`, con la cadena de firmas |
| Saldo por reponer / reposición | `/cxp/admin/reposiciones` | El monto aplicado acumula el saldo; al superar el mínimo, la reposición aparece en "emitidas" y como pasivo interno en Tesorería |
| Préstamo de viáticos | Bandeja de pasivos de Tesorería | "Préstamo de viáticos" con beneficiario empleado; al pagarlo, la solicitud pasa sola a `Anticipada` |
| Viático cerrado | `/cxp/viaticos` | Comprobación liberada por el Auxiliar; solicitud `Liquidada` con diferencia calculada — reembolso en bandeja (positiva) o expectativa de depósito `VIATICOS XXXXXXXX` (negativa) |
| CFDI consumido | `/cxp/cfdis` | El CFDI vinculado pasa de `PorProcesar` a `ConvertidoEnPasivo` apuntando a la factura generada |
| Corte de TC | `/cxp/tc/estados-cuenta` y `/cxp/facturas` | Estado de cuenta `Cerrado` y pasivo agregado contra el banco en el ciclo normal |
| Reportes | `/cxp/reportes/tc`, `/cxp/reportes/tc-pendientes`, `/cxp/reportes/diot` | Movimientos conciliados y pendientes; IVA por CFDI en DIOT |

**Evidencia e2e en dev (2026-07-16, datos "prueba Claude P7"):**

- Viáticos `019f6c97-7042-...f0e8`: Solicitada → jefe → pagado → comprobación
  (CFDI $1,160 PUE + ticket $340, capturada 2 veces) → **Liquidada**,
  diferencia −$1,500; factura generada `019f6c99-5861-...a96c` con UUID
  backfilled y `MetodoPago` copiado. Exceso: `019f6c9a-1621-...e82a`
  (tope ×2 días, `RequiereDireccionFinanzas`, segregación DF verificada).
- Caja chica `019f6c9e-5db3-...c5c5` ($928, línea vinculada PPD + línea con
  UUID manual): **Aplicada**; CFDI `D0B1295E-...` → `ConvertidoEnPasivo`.
  Caso rechazo (P7-H1): `019f6c9f-4f49-...e854`, factura huérfana $232
  cancelada a mano.
- Aduanales `019f6ca1-4847-...0755` (pedimento 26-43-3821-6000124, factura
  P2-001 con OC): doble firma exigida, N1 firmada, N2 mismo-usuario
  bloqueada; rechazada — P2-001 quedó ligada (P7-H1).

**Evidencia e2e de pasivos internos (2026-07-16/17, GI-PR1..PR4):**

- **Reposición acumulada**: mínimo $1,000 en Planta México Centro;
  comprobación de $580 acumuló sin emitir (visible en `/saldos`); la
  segunda de $522 disparó la reposición `019f6d14-2964-...2f6b` de
  **$1,102** (2 comprobaciones, beneficiario = cuenta de sucursal) con el
  pasivo publicado a Tesorería.
- **Préstamo ida y vuelta**: solicitud `019f6d40-14e2-...cff8` ($1,800) —
  al firmar el jefe apareció en la bandeja de Tesorería como "Préstamo de
  viáticos"; pago `SPEI-GI-PR3-V7` → la solicitud pasó sola a
  `Anticipada` (~30 s, evento `pago-prestamo-viaticos.aplicado.v1`).
- **Liquidación negativa**: la misma solicitud liberada con gasto de
  $1,392 (diferencia −$408) generó la expectativa de depósito
  `VIATICOS 019F6D40` por **$408** en Tesorería (conciliable RN-6).
  Incidente detectado y corregido en el camino: el check de origen del
  depósito no admitía viáticos (#646).

**Evidencia e2e de TC (2026-07-17, datos "prueba Claude P7"):**

- Tarjeta `019f6dc8-4a4a-...0b05` (AMEX **** 0001, perfil `AMEX_MX`,
  titular EMP-001, banco P000002): 2 movimientos con CFDI ($580 y
  $1,160 — sus facturas nacen autorizadas y pagadas, solo gasto/IVA/DIOT)
  + 1 sin CFDI ($232).
- **Disputa y refund**: movimiento de $232 marcado `EnDisputa`; refund de
  $500 rechazado (`TC_REFUND_EXCEDE_ORIGINAL`) y refund válido de $232
  registrado (`019f6dce-bf8c`).
- **Corte cerrado**: estado de cuenta `019f6dcf-f28c-...957b` — archivo
  AMEX con fila TOTAL, conciliación automática 1/1, diferencia $0.00,
  `Conciliado` → `Cerrado` con factura contra el banco `019f6dd0-0209`
  por $464; autorizada → **pasivo del banco en la bandeja de Tesorería**.
- Los hallazgos TC-B1..B4 se reprodujeron en vivo antes del fix #648
  (CFDI duplicado aceptado con 201, CFDIs `05F58577`/`15D3DABD` sin
  consumir, recarga con 500 en `019f6dca-416b`); los estados de cuenta
  EC1/EC2 de esa reproducción quedaron como datos de prueba en dev.
