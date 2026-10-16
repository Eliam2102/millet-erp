# 13 — Pólizas: captura, autorización, numeración por tipo y anulación (F1-CON-04)

Entregas: **C1.3** (póliza y partidas, captura manual y autorización), **K10.9** (tipo de dos letras + consecutivo de 6
dígitos por tipo) y **K10.3** (anulación con las mismas partidas en negativo). Planos: «Módulo 10 · Contabilidad» (R5, R6,
R7, R10, R23, P10.3) y «Plano C1 · Motor contable» (§2, §3, §5.2, §6, §9). La «póliza de reversa» del plano C1 §2 queda
sustituida por la anulación en negativo (§9).

## 1. Reglas de Millet que se implementan tal cual

| Regla | Fuente | Dónde |
|---|---|---|
| Cada operación se identifica con dos letras de tipo + consecutivo de 6 dígitos | Cuestionario 08, C-02 (R5) | `Poliza.Folio`, `ConsecutivoPoliza` |
| Los códigos son los de SAP (TT, PC, RF, RC, AN, PR, PP, DP, EM, SM, IM, AC, AS) | C-02 «las claves son las mismas que el punto 4»; P10.3 | Migración `F1Con04PolizasNumeracionAnulacion` |
| Si hay error: reversa con los mismos movimientos en negativo y el texto «Anulación de xxxx» | C-06 (R10) | `Poliza.Anular` |
| Cancelaciones y reversas exigen motivo | Matriz de validación, RNF-05 | `Poliza.ValidarMotivo` |
| Prepara el Analista, aprueba el Supervisor; bitácora | R23 | `Poliza.Autorizar`, auditoría central |
| Documento soporte dentro del ERP | R7 | `PolizaAdjuntoPropietario` (G1.2) |
| El periodo 13 solo admite ajustes manuales | C-14, CA10.12 | `RegistroPolizas.VerificarPeriodoAsync` |
| 25,000 pólizas y 130,000 partidas al mes | R6 | Índices (§5) |

## 2. Supuestos (propuestas VILO por validar)

Ninguna fuente define estos casos; se construye con el supuesto y se ajusta si Millet o Eliam deciden otra cosa.

| # | Supuesto | Valida | Dónde cambiarlo |
|---|---|---|---|
| D1 | La anulación lleva el **mismo tipo** que la original y toma su siguiente consecutivo (anular TT-000123 genera TT-000124) | Laura Cerón (sesión contable 16-oct) | `RegistroPolizas.AnularAsync` |
| D2 | **Un solo catálogo**: el tipo de documento contable (K10.2) es también el tipo de operación (K10.9), con su tipo de póliza del SAT | Eliam | `TipoDocumentoContable` |
| D3 | El número se asigna **al autorizar** (el borrador no consume folio y descartarlo no deja hueco). La automática lo toma al nacer, porque nace autorizada | Eliam | `AutorizarPolizaHandler` |
| D4 | La anulación **nace autorizada** por quien anula, con el permiso `contabilidad.polizas.anular` y motivo | Laura Cerón | `Poliza.Anular` |
| D5 | Con el periodo original abierto, la anulación cae en la **misma fecha y periodo**; con el periodo cerrado, en el periodo abierto que se indique (hoy si cae en él; si no, su primer día) | Eliam | `RegistroPolizas.AnularAsync`, `FechaEnPeriodo` |
| D6 | Cargo y abono en columnas separadas; los **negativos solo existen en una anulación**. La balanza cuenta autorizadas, anuladas y sus anulaciones (suman cero) | Eliam | `PolizaPartida`, `SaldosPeriodoHandler` |
| V39 | El consecutivo se **reinicia cada año** (el del periodo de la póliza) | Laura Cerón (V39) | `ConsecutivoPoliza` (llave empresa, tipo, año) |
| — | **AN** aparece en SAP para el anticipo de clientes y el de proveedores: se precarga uno solo, como diario | Laura Cerón (V39) | Catálogo de tipos |

Contraste descartado: el libro «Layout Numeración avanzada» usa AS, AM, AC, PP y RC con otro significado y 8 dígitos; es
una propuesta de folios de **documentos**, no de pólizas, y no se mezcla con K10.9.

## 3. Modelo

- `TipoDocumentoContable` (+ `tipo_sat`): clave de dos letras para los tipos reales; los de prueba (`FIX-*`) conservan la
  clave libre. Un tipo inactivo no admite pólizas nuevas; anular una existente sí procede.
- `Poliza`: tipo (id + snapshot de la clave y del tipo SAT), número (null en borrador), fecha, periodo (año, número),
  concepto, origen (`Manual`/`Automatica`), estado (`Borrador`/`Autorizada`/`Anulada`), documento de origen (módulo, tipo,
  id, folio, UUID), llave de idempotencia (automática), `poliza_anulada_id` (en la anulación) y `anulada_por_poliza_id`
  (en la original), motivo, quién capturó, autorizó y anuló, totales.
- `PolizaPartida`: cuenta (id + snapshot del código), concepto, cargo y abono en MXN, moneda, tipo de cambio, importe
  original, sucursal, Dim1–Dim3 efectivos (los superiores derivados por jerarquía), proyecto y auxiliares (cliente,
  proveedor, cuenta bancaria). Una partida tiene un solo lado.
- `ConsecutivoPoliza`: último número por empresa, tipo y año.

## 4. Concurrencia y consistencia

- Toda escritura de pólizas corre en una transacción con los candados de periodos y de reglas en modo **compartido**: las
  pólizas no se bloquean entre sí, pero un cierre de periodo o un cambio de reglas (exclusivos) espera a que terminen.
- El consecutivo se avanza con `INSERT … ON CONFLICT DO UPDATE … RETURNING` en la misma transacción: la fila queda
  bloqueada hasta el commit (sin duplicados) y un rollback devuelve el número (sin huecos). Un tipo nuevo crea su fila
  con el primer uso.
- Índices únicos de respaldo: folio por (empresa, tipo, año, número); llave de idempotencia; `poliza_anulada_id` (una
  segunda anulación concurrente responde 409).
- Restricciones en BD: la póliza no borrador tiene número y cuadra; cada partida tiene un solo lado.

## 5. Índices y paginación (C1.3 §7, propuesta para Eliam)

- Bandeja: `ix_polizas_periodo_fecha (empresa, año, periodo, fecha, id)` y `ix_polizas_estado_fecha`; paginación por
  offset con límite máximo de 200 y orden estable (fecha, id). Si el volumen lo exige, se pasa a cursor por (fecha, id)
  sin cambiar los índices.
- Balanza y auxiliares (C1.7): `ix_polizas_partidas_cuenta (empresa, cuenta)` y la llave por póliza.
- Motor: `ix_polizas_documento_origen` para anular por documento.

## 6. Contrato HTTP (`/api/v1/contabilidad/polizas`)

| Método y ruta | Permiso | Respuesta |
|---|---|---|
| `GET /` (`anio, numero, tipoOperacionId, estado, origen, q, offset, limit`) | `polizas.leer` | 200 página; `q` busca por folio (`TT-000123` o `TT-123`) o en el concepto |
| `GET /{id}` | `polizas.leer` | 200 + ETag; 404 |
| `GET /saldos?anio&numero&cuentaId?` | `polizas.leer` | 200 cargos, abonos y saldo del periodo por cuenta (sin borradores) |
| `POST /` | `polizas.capturar` | 201 borrador; 400 por partida (`partidas[i].campo`); 422 periodo |
| `PUT /{id}` (If-Match) | `polizas.capturar` | 200; 422 si no es borrador |
| `DELETE /{id}` (If-Match) | `polizas.capturar` | 204; 422 si no es borrador |
| `POST /{id}/autorizar` (If-Match) | `polizas.autorizar` | 200 con folio; 422 `CONTAB_POLIZA_NO_CUADRA`, `CONTAB_POLIZA_AUTORIZA_QUIEN_CAPTURA` |
| `POST /{id}/anular` (If-Match) `{motivo, periodoAnio?, periodoNumero?}` | `polizas.anular` | 201 con la póliza de anulación; 409 `CONTAB_POLIZA_YA_ANULADA`; 422 `CONTAB_POLIZA_ANULACION_REQUIERE_PERIODO` / `…_PERIODO_ORIGINAL_ABIERTO` |
| `…/{id}/adjuntos` | ver `leer`; subir y baja `capturar` | Contrato genérico de adjuntos; solo en borrador |

Las mutaciones exigen `Idempotency-Key`. Catálogo de tipos: `/api/v1/contabilidad/tipos-documento` acepta y devuelve
`tipoSat` (`Ingreso`, `Egreso`, `Diario`).

## 7. Puerto del motor contable

`IPolizaContablePort` (Contabilidad): `RegistrarAutomaticaAsync` (nace autorizada y numerada, idempotente por llave) y
`AnularPorDocumentoOrigenAsync` (aplica la misma anulación en negativo a las pólizas del documento cancelado, idempotente).
Con el periodo cerrado lanza el rechazo: la bandeja de rechazos y la elección de periodo son de C1.5.

## 8. Dependencias de plataforma pendientes

| Pieza | Ticket | NoOp en uso | Cómo se wirea |
|---|---|---|---|
| Listeners del motor (p. ej. `facturacion.comprobante.cancelado.v1`, F6.5) | `<MotorContable>` (C1.5) | Ninguno: el puerto existe y nadie lo llama aún | El listener llama `IPolizaContablePort.AnularPorDocumentoOrigenAsync` |
| Plantillas con su tipo (K10.9 §2) | C1.4 | — | La plantilla guarda la clave de dos letras y la pasa en `SolicitudPolizaAutomatica.TipoClave` |
| Balanza completa (saldos iniciales, arrastre) | C1.7 | `GET /polizas/saldos` (solo movimientos del periodo) | C1.7 reutiliza el mismo criterio de estados |
| Movimientos de prueba | `<Polizas>` | `MovimientoDimensionPrueba` sigue vivo | Retirarlo cuando Contabilidad confirme que ya no lo usa |
