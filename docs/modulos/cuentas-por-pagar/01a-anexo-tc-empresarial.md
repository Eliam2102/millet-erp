# Anexo técnico — Tarjetas de Crédito Empresariales (sub-módulo de CxP)

> **Construido sobre:**
> - [00-levantamiento.md](00-levantamiento.md) §4.8, §4.9, §7.4.3, §13.1 puntos 4 y 6.
> - [01-diseno.md](01-diseno.md) §3 asunciones A6, A15, A20.
>
> **Estado:** propuesta de diseño técnico v1 para revisión con el owner. Cierra el §13.3 punto 18 del levantamiento de CxP ("Modelo TC Empresarial — sub-módulo más complejo del nuevo alcance; conviene un anexo técnico dedicado antes de PR breakdown").
>
> **Fecha:** 2026-05-22.

---

## 0. Por qué este anexo existe

El sub-módulo de Tarjetas de Crédito Empresariales (TC) es el más complejo del alcance de CxP porque combina varios patrones que el resto del módulo no toca:

1. **Doble pasivo lógico** que se desfasa en el tiempo: el proveedor del gasto queda saldado al instante (la tarjeta pagó), pero el pasivo agregado contra el banco emisor se crea en la fecha de corte y se paga días/semanas después.
2. **Conciliación contra un sistema externo** (el portal del banco) por archivo Excel/CSV, con perfiles de parser por banco emisor y tolerancia a variaciones de formato.
3. **Movimientos con y sin CFDI** que se tratan distinto fiscalmente (DIOT, IVA acreditable).
4. **Multimoneda activa** (Amex factura en USD muchos cargos; el banco consolida en MXN al TC del corte).
5. **Casos límite frecuentes**: refunds del banco, intereses, comisiones por divisa, anualidades, fraudes detectados al conciliar.
6. **Modelo de responsable** distinto: titular fijo por tarjeta (decisión #4 de §13.1) pero N empleados autorizados pueden usar la tarjeta. La trazabilidad cae sobre el empleado que hizo el cargo, la responsabilidad sobre el titular.

Documentar este sub-módulo aparte permite que la captura del 01-diseno se mantenga proporcional (CxP es grande, TC es 1/8 del módulo en superficie pero 1/3 en complejidad).

---

## 1. Cómo leer este documento

- `[Decidido]` — fijado por §13.1 del levantamiento (decisiones cerradas el 2026-05-22) o por ADR.
- `[Asunción técnica]` — propuesta del diseñador para detalle no resuelto explícitamente con el área. Listado en §15.
- `[Diferido]` — vNext o post-MVP.

---

## 2. Decisiones cerradas que enmarcan este anexo

| # | Decisión | Origen |
|---|---|---|
| **D1** | **Varias TC corporativas con titular fijo por tarjeta.** No por sucursal. Catálogo local en CxP. | §13.1 punto 4 del levantamiento |
| **D2** | **Pasivo con el banco, no con cada proveedor.** Cada cargo registra "qué se compró y a quién" para gasto/IVA/DIOT, pero el pasivo agregado es contra el banco emisor (proveedor especial). | §7.4.3 del levantamiento |
| **D3** | **Cada CFDI individual registrado para DIOT.** Movimientos con CFDI generan `FacturaProveedor` que se marca `Pagada` automáticamente al registrarse. | §7.4.3 del levantamiento |
| **D4** | **Movimientos sin CFDI no generan `FacturaProveedor`.** Afectan gasto directo (sin IVA acreditable, o con IVA si es ticket simplificado). | A6 del 01-diseno |
| **D5** | **Conciliación por carga manual de Excel/CSV del portal del banco.** Parser configurable por perfil de banco. Integración API del banco a vNext. | §13.1 punto 6 del levantamiento |
| **D6** | **Match automático por `fecha + monto + merchant`** (fuzzy). Movimientos no conciliados quedan en bandeja. | §7.4.3 del levantamiento |
| **D7** | **Autorización del pago al banco: Dirección de Finanzas.** El movimiento individual no requiere autorización adicional (el banco ya pagó). | §8.1 del levantamiento |
| **D8** | **Naming canónico de eventos:** `{Agregado}{Verbo}Event` (alineado con Compras-OC §8.6). | ADR/convención cross-módulo |

Decisiones nuevas introducidas en este anexo (§15 las lista como `[Asunción técnica]` pendientes de confirmar):

- **D9 [Asunción]:** los movimientos en USD se almacenan en USD (monto original) + MXN (al TC del día del cargo, snapshot). La diferencia cambiaria entre captura y pago al banco se contabiliza como `DIFERENCIA_CAMBIARIA_BANCO_TC`.
- **D10 [Asunción]:** un empleado distinto al titular que usa la TC se modela como `usuario_que_uso = empleado_id` en el movimiento. El titular sigue siendo el responsable de la conciliación y la firma.
- **D11 [Asunción]:** refunds bancarios (líneas negativas en el estado de cuenta) se modelan como `MovimientoTarjetaCredito` con `tipo = Refund` y referencia al movimiento original. Reducen el pasivo del estado de cuenta.
- **D12 [Asunción]:** intereses, comisiones por divisa, anualidades del banco se modelan como `MovimientoTarjetaCredito` con `tipo = GastoFinanciero` y proveedor implícito = el banco. Se capturan al conciliar (no se conocen antes del corte).
- **D13 [Asunción]:** captura retroactiva al conciliar: si una línea del banco no tiene movimiento capturado, el Auxiliar puede capturar el movimiento desde la pantalla de conciliación con flag `captura_retroactiva=true` (para auditoría).
- **D14 [Asunción]:** TC bloqueada por robo/extravío → estado `Bloqueada`. Los movimientos pendientes (capturados pero no en estado de cuenta cerrado) se siguen pagando al banco normalmente; los disputados se manejan vía refunds que el banco aplique.

---

## 3. Modelo del dominio detallado

### 3.1 Agregados

| Agregado | Por qué es raíz | Esquema |
|---|---|---|
| `Tarjeta` | Master local; ciclo independiente (alta/bloqueo/baja). Sus atributos pueden cambiar (titular, límite) sin afectar movimientos históricos. | `cuentas_por_pagar.tarjetas_credito` |
| `MovimientoTarjetaCredito` | Unidad atómica fiscal y operativa. Cada cargo individual. Se concilia, refunda, se paga. | `cuentas_por_pagar.movimientos_tarjeta_credito` |
| `EstadoCuentaTC` | Cierre periódico que agrupa N movimientos + las líneas crudas del archivo del banco. Genera el pasivo contra el banco. | `cuentas_por_pagar.estados_cuenta_tc` |

`FacturaProveedor` no es agregado nuevo de TC — ya existe en el módulo. Se reutiliza con dos usos distintos en este sub-módulo:

- **Factura del proveedor del gasto** (cuando el cargo tiene CFDI): se crea normal vía `FacturaProveedorRegistradaEvent` con `pagador = BANCO_TC_X` y se marca `Pagada` inmediatamente.
- **Factura agregada del banco** (al cerrar el estado de cuenta): se crea apuntando al `Proveedor` especial `BancoEmisorTC` con el monto total del estado de cuenta. Esta es la que Tesorería paga.

### 3.2 Value Objects

| VO | Propósito | Validaciones |
|---|---|---|
| `NumeroTarjetaEnmascarado` | `**** **** **** 1234` (solo últimos 4) | Formato fijo. Nunca se guarda el PAN completo (PCI-DSS-ish: aunque el sistema no procesa pagos, no almacenar PAN reduce riesgo). |
| `MerchantName` | Texto libre del cargo ("STARBUCKS CDMX", "AMAZON.COM.MX") | Normalización al cargar el archivo (UPPER, trim, colapsar espacios) para mejorar match. |
| `MonedaCargo` | ISO 4217 (`MXN`, `USD`) | Restringido a las monedas que el banco soporta para esa tarjeta. |
| `TasaConversionMxn` | Decimal con 4 decimales | Snapshot al momento de la captura del movimiento. |
| `PerfilParserBanco` | Identifica el formato del Excel/CSV de un banco | Enum: `AMEX_MX`, `BANAMEX`, `BBVA_MX`, … (extensible por seed). |
| `EstadoMovimientoTc` | Enum del ciclo de vida del movimiento | `Registrado` / `ConciliadoConEstadoCuenta` / `EnDisputa` / `Reversado` / `PagadoAlBanco` |
| `TipoMovimientoTc` | Naturaleza del cargo | `CompraConCfdi` / `CompraSinCfdi` / `Refund` / `GastoFinanciero` / `Anualidad` / `ComisionDivisa` |
| `EstadoCuentaTcStatus` | Ciclo de vida del estado de cuenta | `EnConciliacion` / `Conciliado` / `Cerrado` / `PagadoBanco` |

### 3.3 Invariantes

- `Tarjeta.estado = Bloqueada` no impide capturar movimientos históricos pero **sí** impide registrar nuevos con `fecha_movimiento > tarjeta.fecha_bloqueo`.
- `MovimientoTarjetaCredito.tarjeta_id` debe estar `Activa` o `Bloqueada` al momento del cargo (`fecha_movimiento` dentro de vigencia).
- `MovimientoTarjetaCredito.tipo = CompraConCfdi` requiere FK no-null a `CfdiRecibido` y a `FacturaProveedor`.
- `MovimientoTarjetaCredito.tipo = Refund` requiere FK no-null a un `movimiento_original_id` con la misma tarjeta y monto ≤ |monto del original|.
- `EstadoCuentaTC.total = sum(movimientos.monto_mxn) + sum(refunds.monto_mxn negativo) + sum(gastos_financieros.monto_mxn)`. La igualdad se verifica al cerrar; discrepancia bloquea el cierre.
- `EstadoCuentaTC.estado = Cerrado` requiere que **todos los movimientos del periodo estén conciliados o explicados** (excepción: movimientos en disputa pueden quedar fuera del cierre con bandera `excluido_por_disputa`).
- `EstadoCuentaTC` solo se puede pagar al banco si todos sus movimientos están `ConciliadoConEstadoCuenta` (no en disputa, no reversados pendientes).
- **Inmutabilidad:** un `MovimientoTarjetaCredito` en estado `ConciliadoConEstadoCuenta` o posterior **no se edita**. Corrección por movimiento de ajuste o refund.

### 3.4 Distinción `titular` vs `usuario_que_uso`

Decisión D10:

- `Tarjeta.titular_id` — empleado responsable de la tarjeta (firma el estado de cuenta, autoriza el pago al banco). Asignación duradera.
- `MovimientoTarjetaCredito.usuario_que_uso_id` — empleado que realizó el cargo. Puede ser el titular u otro empleado autorizado. Captura por evento.

Implicación: la pantalla del titular muestra **todos los movimientos de su tarjeta** (incluidos los hechos por otros usuarios autorizados). La pantalla de un empleado autorizado muestra **solo los movimientos que él hizo**.

---

## 4. Esquema PostgreSQL

### 4.1 Tablas

```sql
-- Master local: tarjetas corporativas
CREATE TABLE cuentas_por_pagar.tarjetas_credito (
    id                          uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    emisora                     text NOT NULL,                    -- 'Amex', 'Banamex'
    perfil_parser               text NOT NULL,                    -- FK lógica a perfiles_parser_banco.codigo
    numero_enmascarado          text NOT NULL,                    -- '**** **** **** 1234'
    nombre_alias                text NOT NULL,                    -- 'Amex Corporativa Dirección'
    titular_id                  uuid NOT NULL,                    -- FK Empleado (DatosMaestros)
    banco_proveedor_id          uuid NOT NULL,                    -- FK Proveedor especial (BancoEmisorTC)
    limite_credito_mxn          numeric(14,2) NOT NULL,
    moneda_default              text NOT NULL DEFAULT 'MXN',
    dia_corte                   smallint NOT NULL,                -- 1-31; si el banco usa fin de mes = 31
    dia_limite_pago             smallint NOT NULL,                -- offset desde corte
    estado                      text NOT NULL,                    -- 'Activa', 'Bloqueada', 'Cancelada'
    fecha_bloqueo               date,                             -- NULL si Activa
    motivo_bloqueo              text,
    vigencia_desde              date NOT NULL,
    vigencia_hasta              date,                             -- NULL si vigente
    -- Auditoría + versión
    version                     int NOT NULL DEFAULT 0,
    created_at                  timestamptz NOT NULL DEFAULT now(),
    created_by                  uuid NOT NULL,
    updated_at                  timestamptz NOT NULL DEFAULT now(),
    updated_by                  uuid NOT NULL,
    soft_deleted_at             timestamptz,
    CONSTRAINT ck_estado_valido CHECK (estado IN ('Activa', 'Bloqueada', 'Cancelada')),
    CONSTRAINT ck_dia_corte CHECK (dia_corte BETWEEN 1 AND 31),
    CONSTRAINT ck_bloqueo_consistente CHECK (
        (estado = 'Bloqueada' AND fecha_bloqueo IS NOT NULL) OR
        (estado != 'Bloqueada')
    )
);

-- Usuarios autorizados por tarjeta (N empleados pueden usar una TC)
CREATE TABLE cuentas_por_pagar.tarjeta_usuarios_autorizados (
    tarjeta_id                  uuid NOT NULL REFERENCES cuentas_por_pagar.tarjetas_credito(id),
    empleado_id                 uuid NOT NULL,                    -- FK Empleado
    vigencia_desde              date NOT NULL,
    vigencia_hasta              date,
    monto_max_mensual_mxn       numeric(14,2),                    -- NULL = sin tope individual (el de la tarjeta aplica)
    PRIMARY KEY (tarjeta_id, empleado_id, vigencia_desde)
);

-- Movimientos individuales (cada cargo)
CREATE TABLE cuentas_por_pagar.movimientos_tarjeta_credito (
    id                          uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tarjeta_id                  uuid NOT NULL REFERENCES cuentas_por_pagar.tarjetas_credito(id),
    usuario_que_uso_id          uuid NOT NULL,                    -- FK Empleado
    fecha_movimiento            date NOT NULL,                    -- fecha del cargo según ticket/CFDI
    fecha_aplicacion_banco      date,                             -- fecha que aparece en el estado de cuenta (NULL hasta conciliar)
    tipo                        text NOT NULL,                    -- 'CompraConCfdi', 'CompraSinCfdi', 'Refund', 'GastoFinanciero', 'Anualidad', 'ComisionDivisa'
    estado                      text NOT NULL,                    -- 'Registrado', 'ConciliadoConEstadoCuenta', 'EnDisputa', 'Reversado', 'PagadoAlBanco'
    -- Importes
    monto_original              numeric(14,2) NOT NULL,
    moneda_original             text NOT NULL,                    -- 'MXN', 'USD'
    tipo_cambio_captura         numeric(10,4),                    -- snapshot TC al capturar (NULL si moneda = MXN)
    monto_mxn                   numeric(14,2) NOT NULL,           -- monto en MXN (igual al original si MXN, o convertido si USD)
    -- Comercio
    merchant_raw                text NOT NULL,                    -- nombre del comercio según ticket o CFDI
    merchant_normalizado        text NOT NULL,                    -- UPPER+trim+colapsar espacios (para match)
    descripcion_libre           text,
    -- Vinculaciones fiscales
    cfdi_recibido_id            uuid,                             -- FK CfdiRecibido (solo si tipo = CompraConCfdi)
    factura_proveedor_id        uuid,                             -- FK FacturaProveedor (solo si tipo = CompraConCfdi)
    proveedor_id                uuid,                             -- FK Proveedor (solo si tipo = CompraConCfdi; redundante con factura para queries rápidas)
    concepto_contable           text NOT NULL,
    -- Refunds
    movimiento_original_id      uuid REFERENCES cuentas_por_pagar.movimientos_tarjeta_credito(id), -- solo si tipo = Refund
    -- Conciliación
    estado_cuenta_tc_id         uuid,                             -- FK estado_cuenta_tc (populated al conciliar)
    estado_cuenta_tc_linea_id   uuid,                             -- FK línea cruda del banco que matcheó
    captura_retroactiva         boolean NOT NULL DEFAULT false,   -- D13: capturado desde la pantalla de conciliación
    -- Adjuntos
    ticket_blob_ref             text,                             -- ruta blob al ticket si no hay CFDI
    -- Disputas
    en_disputa                  boolean NOT NULL DEFAULT false,
    motivo_disputa              text,
    fecha_inicio_disputa        date,
    -- Auditoría + versión
    version                     int NOT NULL DEFAULT 0,
    created_at                  timestamptz NOT NULL DEFAULT now(),
    created_by                  uuid NOT NULL,
    updated_at                  timestamptz NOT NULL DEFAULT now(),
    updated_by                  uuid NOT NULL,
    soft_deleted_at             timestamptz,
    -- Constraints
    CONSTRAINT ck_tipo_valido CHECK (tipo IN ('CompraConCfdi','CompraSinCfdi','Refund','GastoFinanciero','Anualidad','ComisionDivisa')),
    CONSTRAINT ck_estado_valido CHECK (estado IN ('Registrado','ConciliadoConEstadoCuenta','EnDisputa','Reversado','PagadoAlBanco')),
    CONSTRAINT ck_cfdi_consistente CHECK (
        (tipo = 'CompraConCfdi' AND cfdi_recibido_id IS NOT NULL AND factura_proveedor_id IS NOT NULL) OR
        (tipo != 'CompraConCfdi' AND cfdi_recibido_id IS NULL)
    ),
    CONSTRAINT ck_refund_consistente CHECK (
        (tipo = 'Refund' AND movimiento_original_id IS NOT NULL) OR
        (tipo != 'Refund')
    ),
    CONSTRAINT ck_moneda_tc_consistente CHECK (
        (moneda_original = 'MXN' AND tipo_cambio_captura IS NULL) OR
        (moneda_original != 'MXN' AND tipo_cambio_captura IS NOT NULL)
    )
);

-- Estados de cuenta periódicos
CREATE TABLE cuentas_por_pagar.estados_cuenta_tc (
    id                          uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    tarjeta_id                  uuid NOT NULL REFERENCES cuentas_por_pagar.tarjetas_credito(id),
    periodo_desde               date NOT NULL,
    periodo_hasta               date NOT NULL,
    fecha_corte                 date NOT NULL,
    fecha_limite_pago           date NOT NULL,
    archivo_banco_blob_ref      text,                             -- ruta blob al Excel/CSV subido
    archivo_banco_hash          text,                             -- SHA-256 para deduplicar uploads
    archivo_banco_cargado_at    timestamptz,
    archivo_banco_cargado_by    uuid,
    perfil_parser_usado         text,                             -- perfil con el que se parseó
    total_banco_mxn             numeric(14,2),                    -- total declarado por el banco en el estado de cuenta
    total_conciliado_mxn        numeric(14,2),                    -- total de movimientos conciliados
    diferencia_mxn              numeric(14,2),                    -- total_banco - total_conciliado; debe ser 0 para Cerrado
    estado                      text NOT NULL,                    -- 'EnConciliacion','Conciliado','Cerrado','PagadoBanco'
    factura_proveedor_id        uuid,                             -- FK FacturaProveedor (banco) generada al Cerrar
    -- Diferencia cambiaria entre captura y corte
    diferencia_cambiaria_mxn    numeric(14,2),
    -- Auditoría
    version                     int NOT NULL DEFAULT 0,
    created_at                  timestamptz NOT NULL DEFAULT now(),
    created_by                  uuid NOT NULL,
    updated_at                  timestamptz NOT NULL DEFAULT now(),
    updated_by                  uuid NOT NULL,
    soft_deleted_at             timestamptz,
    CONSTRAINT ck_estado_valido CHECK (estado IN ('EnConciliacion','Conciliado','Cerrado','PagadoBanco')),
    CONSTRAINT ck_periodo_coherente CHECK (periodo_desde <= periodo_hasta),
    CONSTRAINT ux_periodo_tarjeta UNIQUE (tarjeta_id, periodo_desde, periodo_hasta)
);

-- Líneas crudas del archivo del banco (lo que el parser extrajo)
CREATE TABLE cuentas_por_pagar.estado_cuenta_tc_lineas_banco (
    id                          uuid PRIMARY KEY DEFAULT gen_random_uuid(),
    estado_cuenta_tc_id         uuid NOT NULL REFERENCES cuentas_por_pagar.estados_cuenta_tc(id),
    posicion_archivo            int NOT NULL,                     -- número de fila del Excel/CSV
    fecha_aplicacion            date NOT NULL,
    monto                       numeric(14,2) NOT NULL,
    moneda                      text NOT NULL,
    monto_mxn                   numeric(14,2) NOT NULL,           -- según el TC del corte del banco
    merchant_raw                text NOT NULL,
    merchant_normalizado        text NOT NULL,
    referencia_banco            text,                             -- "Authorization Code", "ID Tx", según banco
    tipo_segun_banco            text,                             -- 'Compra', 'Refund', 'Interes', 'Anualidad', 'Comision'
    movimiento_tc_id            uuid REFERENCES cuentas_por_pagar.movimientos_tarjeta_credito(id), -- match
    estado_match                text NOT NULL,                    -- 'Pendiente','Matched','NoConciliado','CapturaRetroactiva'
    score_match                 numeric(5,2),                     -- score del fuzzy match (0-100)
    -- Auditoría
    created_at                  timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT ck_estado_match_valido CHECK (estado_match IN ('Pendiente','Matched','NoConciliado','CapturaRetroactiva')),
    CONSTRAINT ux_linea_archivo UNIQUE (estado_cuenta_tc_id, posicion_archivo)
);

-- Perfiles de parser por banco (seed)
CREATE TABLE cuentas_por_pagar.perfiles_parser_banco (
    codigo                      text PRIMARY KEY,                 -- 'AMEX_MX', 'BANAMEX', etc.
    nombre                      text NOT NULL,
    formato_archivo             text NOT NULL,                    -- 'XLSX', 'CSV', 'CSV_TAB'
    encoding                    text NOT NULL DEFAULT 'UTF-8',    -- 'UTF-8', 'WINDOWS-1252' (común en bancos MX)
    fila_inicio_datos           int NOT NULL DEFAULT 1,           -- skip de headers
    columna_fecha               text NOT NULL,                    -- 'A', 'B', o nombre de columna
    formato_fecha               text NOT NULL,                    -- 'yyyy-MM-dd', 'dd/MM/yyyy'
    columna_monto               text NOT NULL,
    columna_moneda              text,                             -- si NULL, se asume `moneda_default` de la tarjeta
    columna_merchant            text NOT NULL,
    columna_referencia          text,
    columna_tipo                text,                             -- columna con 'Compra'/'Refund'/etc; si NULL, se infiere por signo de monto
    regla_signo_refund          text NOT NULL DEFAULT 'NegativoEsRefund', -- 'NegativoEsRefund', 'PositivoEsRefund', 'PorColumnaTipo'
    locale_montos               text NOT NULL DEFAULT 'es-MX',    -- separador decimal (coma o punto)
    activo                      boolean NOT NULL DEFAULT true
);
```

### 4.2 Índices críticos

```sql
-- Bandeja "Mis movimientos de TC" del titular
CREATE INDEX ix_movtc_titular_estado
  ON cuentas_por_pagar.movimientos_tarjeta_credito (tarjeta_id, estado, fecha_movimiento DESC);

-- Movimientos por usuario que los hizo
CREATE INDEX ix_movtc_usuario
  ON cuentas_por_pagar.movimientos_tarjeta_credito (usuario_que_uso_id, fecha_movimiento DESC);

-- Match por (fecha + monto) durante conciliación
CREATE INDEX ix_movtc_para_match
  ON cuentas_por_pagar.movimientos_tarjeta_credito (tarjeta_id, fecha_movimiento, monto_mxn)
  WHERE estado IN ('Registrado','ConciliadoConEstadoCuenta');

-- Búsqueda por merchant normalizado (trigram para fuzzy)
CREATE EXTENSION IF NOT EXISTS pg_trgm;
CREATE INDEX ix_movtc_merchant_trgm
  ON cuentas_por_pagar.movimientos_tarjeta_credito USING gin (merchant_normalizado gin_trgm_ops);

-- Bandeja de movimientos sin conciliar
CREATE INDEX ix_movtc_pendientes
  ON cuentas_por_pagar.movimientos_tarjeta_credito (tarjeta_id, fecha_movimiento)
  WHERE estado_cuenta_tc_id IS NULL AND estado != 'Reversado';

-- Líneas del banco no conciliadas
CREATE INDEX ix_lineas_banco_no_conciliadas
  ON cuentas_por_pagar.estado_cuenta_tc_lineas_banco (estado_cuenta_tc_id, estado_match)
  WHERE estado_match IN ('Pendiente','NoConciliado');
```

---

## 5. Flujos paso a paso

### 5.1 Flujo A — Captura del movimiento (con CFDI)

**Disparador:** el empleado hace una compra con la TC; el comercio emite CFDI a nombre de Millet con el RFC correspondiente.

1. El CFDI llega al ERP por canal estándar (descarga SAT, mailbox) y se ingesta como `CfdiRecibido`.
2. En la bandeja del titular o Auxiliar de CxP aparece sugerencia "Este CFDI parece pago con TC porque la forma de pago = 04 (Tarjeta de crédito) y el monto coincide con un cargo pendiente". `[Asunción técnica D-A]`: usar el campo `FormaPago = "04"` del CFDI como heurística primaria.
3. Usuario abre la captura → selecciona la tarjeta usada (entre las que tiene autorizadas).
4. Sistema crea:
   - `FacturaProveedor` con `proveedor_id = emisor del CFDI`, `estado = Pagada` (porque la TC ya pagó), `forma_pago = 04`, `pagador = banco_de_la_tarjeta`.
   - `MovimientoTarjetaCredito` con `tipo = CompraConCfdi`, FK al `CfdiRecibido` y al `FacturaProveedor`, `estado = Registrado`.
5. Emite `FacturaProveedorRegistradaEvent` (Compras suscribe normalmente) y `FacturaProveedorAutorizadaEvent` (porque queda `Pagada` directo) en el mismo commit.
6. Contabilidad recibe los asientos: cargo a `GASTO_*` / abono a `BANCO_TC_X` (no al `PROVEEDOR_*` como sería normal — la diferencia clave de TC).

### 5.2 Flujo B — Captura del movimiento (sin CFDI)

**Disparador:** el empleado hace una compra con la TC; solo hay ticket no fiscal o CFDI simplificado (gasolina, peajes, comida no facturada).

1. Empleado abre la pantalla "Mis movimientos de TC" → "Nuevo movimiento sin CFDI".
2. Captura: tarjeta, fecha, monto, moneda, merchant (texto libre), concepto contable, foto del ticket (adjunto).
3. Sistema crea solo `MovimientoTarjetaCredito` con `tipo = CompraSinCfdi`. **No** crea `FacturaProveedor` ni afecta `Proveedor`.
4. Contabilidad: cargo a `GASTO_*` (sin IVA acreditable; o con IVA al 16% si el ticket es CFDI simplificado y la política lo permite — `[Pendiente — Finanzas]`) / abono a `BANCO_TC_X`.
5. **No aparece en DIOT** (no hay CFDI). Si el área lo necesitara para DIOT, debe insistir al proveedor que emita CFDI.

### 5.3 Flujo C — Cierre del estado de cuenta y conciliación

**Disparador:** llega la fecha de corte (`tarjeta.dia_corte` del mes); el titular o Auxiliar de CxP descarga el estado de cuenta del portal del banco.

1. Auxiliar entra a `/cxp/tc/{tarjeta_id}/estado-cuenta/nuevo`.
2. Selecciona periodo (sistema sugiere el último corte; ajustable).
3. Sube el archivo (Excel o CSV) descargado del portal del banco.
4. Sistema:
   - Calcula SHA-256 del archivo → deduplica si ya fue subido (rechaza con mensaje "Este archivo ya se procesó en el estado de cuenta X").
   - Identifica `perfil_parser` a partir de la tarjeta.
   - Llama `IEstadoCuentaTcParserPort.parse(archivo, perfil)` que devuelve `List<LineaBanco>` parseada.
   - Crea `EstadoCuentaTC` con `estado = EnConciliacion`, líneas insertadas en `estado_cuenta_tc_lineas_banco`.
5. Sistema corre **algoritmo de match automático** (§7) sobre cada línea contra movimientos `Registrado` de la misma tarjeta dentro de una ventana ± 3 días.
6. UI muestra dashboard de conciliación:
   - Total líneas archivo: N
   - Conciliadas automáticamente: M (score ≥ 90)
   - Sugerencias de match (score 60-89): K (requiere confirmación humana)
   - Sin sugerencia (score < 60 o sin candidato): N - M - K (requiere captura retroactiva o explicación)
7. Auxiliar revisa las sugerencias, confirma matches, captura retroactivamente los movimientos faltantes (con flag `captura_retroactiva = true`), y clasifica los movimientos del banco que no son compras (intereses, comisiones, anualidades, refunds) creando movimientos con `tipo = GastoFinanciero / Anualidad / ComisionDivisa / Refund`.
8. Cuando `total_conciliado = total_banco` (diferencia 0) y todos los movimientos están atados a una línea del banco o explicados, el Auxiliar puede pasar `estado = Conciliado`.
9. Titular de la tarjeta revisa (UI le muestra resumen) y aprueba → `estado = Cerrado`.

### 5.4 Flujo D — Generación del pasivo agregado contra el banco

**Disparador:** un `EstadoCuentaTC` pasa a `Cerrado`.

1. Sistema crea automáticamente una `FacturaProveedor` especial:
   - `proveedor_id = tarjeta.banco_proveedor_id` (proveedor especial `BancoEmisorTC`).
   - `total = estado_cuenta_tc.total_banco_mxn`.
   - `fecha_vencimiento = estado_cuenta_tc.fecha_limite_pago`.
   - `estado = Capturada`.
   - Sin OC asociada (factura directa).
   - `comprobacion_gastos_id = null`, pero con FK inversa `estado_cuenta_tc_id` para trazabilidad.
2. La factura entra al flujo de autorización normal: Dirección de Finanzas firma (D7).
3. Al pasar a `Autorizada`, emite `PasivoAutorizadoParaPagoEvent` → Tesorería lo recibe normalmente.
4. Tesorería paga al banco en la fecha límite.
5. Al recibir `PagoFacturaProveedorEvent` desde Tesorería:
   - `FacturaProveedor.estado = Pagada`.
   - `EstadoCuentaTC.estado = PagadoBanco`.
   - Todos los `MovimientoTarjetaCredito.estado = PagadoAlBanco` en batch.

### 5.5 Flujo E — Refund / contracargo

**Disparador:** un proveedor cancela una compra y devuelve dinero a la tarjeta. Aparece como línea negativa en el siguiente estado de cuenta del banco.

1. Durante la conciliación (§5.3), la línea del banco con monto negativo se identifica.
2. Sistema sugiere automáticamente el movimiento original a refundar (match por monto absoluto + merchant + ventana de 90 días).
3. Auxiliar confirma el match.
4. Sistema crea `MovimientoTarjetaCredito` con `tipo = Refund`, `movimiento_original_id = movimiento_a_refundar.id`, monto negativo (o positivo con flag `es_refund` — el signo se elige al implementar; D11).
5. Si el movimiento original tenía CFDI → se espera que el proveedor emita una NC fiscal (relación CFDI tipo 01). Cuando llegue, se vincula al `FacturaProveedor` original y reduce su saldo (aunque ya estaba `Pagada`, esto deja consistente la contabilidad: cargo a `BANCO_TC_X` / abono a `GASTO_*`).
6. Si el movimiento original era sin CFDI → solo se contabiliza el refund directo: cargo a `BANCO_TC_X` / abono a `GASTO_*`.
7. El refund reduce el `total_banco_mxn` del estado de cuenta donde aparece (no del estado de cuenta original).

---

## 6. Parser de archivos del banco

### 6.1 Arquitectura

Patrón: **Strategy** por perfil de banco. La interfaz `IEstadoCuentaTcParserPort` toma `(archivo_blob, perfil_codigo)` y devuelve `ParseResult { lineas[], errores[], totalDeclarado? }`.

Cada perfil es un record en `perfiles_parser_banco` con metadatos del formato (columnas, fecha, encoding, signo de refund, etc.). El parser único lee el perfil y aplica las reglas — **no hay clases C# distintas por banco** (no rompe abierto/cerrado: agregar banco = agregar seed).

**Excepción:** si un banco tiene un formato realmente exótico (PDF en lugar de Excel, JSON con estructura no tabular, etc.), se agrega una clase parser específica que implementa `IEstadoCuentaTcParserPort` y se registra en DI con el código del banco como key.

### 6.2 Casos que el parser maneja

| Caso | Comportamiento |
|---|---|
| **Headers en filas 1-3** | `fila_inicio_datos = 4` en el perfil. |
| **Filas de subtotal o saldo final** | Detectar por columnas vacías o palabras clave (`SALDO`, `TOTAL`); excluir del resultado pero capturar `total_declarado` para verificar consistencia. |
| **Filas con multilínea (descripción larga)** | Concatenar líneas sin fecha al merchant de la línea previa. |
| **Decimales con coma o punto** | `locale_montos` del perfil. |
| **Encoding Windows-1252** | Común en bancos MX. Detectar BOM o usar `encoding` del perfil. |
| **Líneas en moneda extranjera mezcladas con MXN** | Mantener ambas columnas (monto original + monto MXN convertido por el banco). |
| **Refund identificado por signo o por columna `tipo`** | `regla_signo_refund` del perfil. |
| **Anualidad/intereses/comisiones** | Detectar por palabras clave en `merchant` (`INTERESES`, `ANUALIDAD`, `COMISION`); marcar `tipo_segun_banco`. El Auxiliar confirma al conciliar. |
| **Fila duplicada** | Detectar por (`fecha + monto + merchant + referencia`); el parser advierte pero no descarta — el Auxiliar decide. |

### 6.3 Validaciones del parser

- Archivo > 50 MB: rechazado (configurable).
- Suma de montos parseados debe coincidir con `total_declarado` del estado de cuenta ± $0.01 (rounding). Si difiere, se acepta pero con advertencia visible.
- Cada línea con fecha fuera del periodo declarado por el usuario al subir el archivo → se acepta pero el sistema sugiere ampliar el periodo o excluir.
- Encoding ambiguo → se intenta detectar con BOM; si falla, se aplica el del perfil.

### 6.4 Perfiles de seed (MVP)

| Código | Banco | Estado |
|---|---|---|
| `AMEX_MX` | American Express México | MVP — coincide con la TC declarada |
| `BANAMEX` | `[Diferido]` hasta confirmar si Millet usa | post-MVP |
| `BBVA_MX` | `[Diferido]` hasta confirmar | post-MVP |

`[Pendiente — área]`: confirmar qué bancos emisores Millet usa hoy para sus TC corporativas (§13.1 punto 4 cerró que son varias TC, pero no especificó bancos).

---

## 7. Algoritmo de conciliación automática

### 7.1 Entrada

Para cada `linea_banco` del estado de cuenta:
- Lista de `movimientos_tc` de la misma tarjeta, en estado `Registrado`, con `fecha_movimiento` en ventana `[linea_banco.fecha_aplicacion - 3 días, linea_banco.fecha_aplicacion + 3 días]`.

### 7.2 Score

```
score = 0
if abs(mov.monto_mxn - linea.monto_mxn) < 0.50:                  score += 50
elif abs(mov.monto_mxn - linea.monto_mxn) / linea.monto_mxn < 0.02: score += 30
else:                                                              score += 0

if mov.fecha_movimiento == linea.fecha_aplicacion:                score += 25
elif abs(mov.fecha_movimiento - linea.fecha_aplicacion) <= 1 día: score += 15
elif abs(mov.fecha_movimiento - linea.fecha_aplicacion) <= 3 días: score += 5

similarity = trigram_similarity(mov.merchant_normalizado, linea.merchant_normalizado)
if similarity > 0.80:                                             score += 25
elif similarity > 0.50:                                           score += 10
else:                                                             score += 0

# Total: 0 - 100
```

### 7.3 Decisión

| Score | Acción |
|---|---|
| `≥ 90` | Match automático: `estado_match = Matched`, vincular movimiento al estado de cuenta, mover movimiento a `ConciliadoConEstadoCuenta`. |
| `60-89` | Sugerencia: queda en `estado_match = Pendiente` con `score_match` registrado; UI muestra para confirmación humana. |
| `< 60` | Sin sugerencia: `estado_match = NoConciliado`. Auxiliar decide si capturar retroactivamente (D13) o si es gasto financiero / refund / anualidad. |

### 7.4 Reglas adicionales

- **Un movimiento solo puede matchear a una línea del banco** (1:1). Si dos líneas del banco proponen al mismo movimiento, el sistema escoge la de mayor score y deja la otra `NoConciliado`.
- **Refunds (líneas negativas)** se matchean contra movimientos con monto **positivo** original (no contra otros refunds). Ventana extendida a 90 días.
- **Líneas tipo `INTERES`, `ANUALIDAD`, `COMISION`** (detectadas por merchant) **no buscan match**: se proponen como movimiento nuevo con tipo correspondiente.

### 7.5 Casos límite

- **Cargo pagado con TC pero CFDI emitido con fecha diferente** (común con servicios mensuales). El score por fecha cae; queda como sugerencia. Auxiliar valida.
- **Dos cargos en el mismo día por el mismo monto** (compras simultáneas en distintos merchants): se evita confundirlos gracias al merchant; si el merchant también coincide, el sistema muestra ambos como sugerencias y obliga a confirmación humana.
- **TC en USD con conversión banco distinta a la calculada al capturar**: el monto_mxn del movimiento puede diferir del monto_mxn de la línea del banco. El score por monto cae; queda como sugerencia. Al confirmar el match, el sistema **actualiza el `monto_mxn` del movimiento al valor del banco** y registra la diferencia en `estado_cuenta_tc.diferencia_cambiaria_mxn` (D9).

---

## 8. Casos especiales fiscales y operativos

### 8.1 Multimoneda (Amex USD)

`[Asunción D9]`: dos campos en el movimiento — `monto_original` (USD por ejemplo) + `monto_mxn` (snapshot al TC del día de captura). Al conciliar, si el banco aplicó otro TC, se actualiza `monto_mxn` al valor del banco y la diferencia se contabiliza:

- Cargo a `DIFERENCIA_CAMBIARIA_BANCO_TC` si el banco aplicó TC más alto (Millet pagó más).
- Abono a `DIFERENCIA_CAMBIARIA_BANCO_TC` si el banco aplicó TC más bajo (Millet pagó menos).

Esto preserva consistencia entre lo que se cargó al gasto (TC del día) y lo que efectivamente se pagó al banco (TC del corte).

### 8.2 Comisión por divisa

Cuando Amex compra USD, suele cobrar 2-3% sobre el monto convertido como "Comisión por divisa". Esto aparece como **línea separada** en el estado de cuenta. Se modela como `MovimientoTarjetaCredito` con `tipo = ComisionDivisa`, sin proveedor de gasto (es comisión del banco), con concepto contable `GASTO_FINANCIERO_COMISION_TC`.

### 8.3 Anualidad de la tarjeta

Cargo anual del banco. Mismo patrón: `tipo = Anualidad`, concepto contable `GASTO_FINANCIERO_ANUALIDAD_TC`. No aparece en DIOT (no hay CFDI; o si el banco emite CFDI por la anualidad, se captura con `tipo = CompraConCfdi` excepcionalmente).

### 8.4 Intereses moratorios

Si por algún error de pago aparecen intereses, se capturan como `tipo = GastoFinanciero`, concepto `GASTO_FINANCIERO_INTERES_TC`. **Bandera visible** en el dashboard del titular: "esta tarjeta generó intereses en el corte X" → alerta para investigar la causa.

### 8.5 Disputa con el banco

Si el titular o el Auxiliar detectan un cargo desconocido o fraudulento al conciliar:

1. Marca el movimiento (o la línea del banco) como `EnDisputa = true` con `motivo_disputa`.
2. El cargo **se excluye del cierre del estado de cuenta**: el `total_conciliado_mxn` no lo cuenta y la `FacturaProveedor` del banco se genera por el total ajustado (menos el cargo en disputa).
3. La gestión externa (comunicación con el banco, reclamo formal) **no vive en el ERP**. Solo el estado.
4. Cuando se resuelve (el banco aplica refund o la disputa se rechaza), el cargo en disputa se resuelve manualmente: el Auxiliar marca `en_disputa = false` y o lo vincula a un refund que vino en un estado de cuenta posterior, o lo concilia como movimiento legítimo.

### 8.6 Pago anticipado de la TC

Si el titular paga al banco antes de la fecha límite (por estrategia financiera), el evento `PagoFacturaProveedorEvent` llega con `fecha_pago < fecha_limite`. El sistema lo acepta sin cambios — el saldo simplemente queda en cero antes de tiempo.

### 8.7 TC robada o extraviada (D14)

1. Titular reporta al sistema → `Tarjeta.estado = Bloqueada`, `fecha_bloqueo = hoy`.
2. Movimientos con `fecha_movimiento > fecha_bloqueo` se bloquean (no se pueden capturar).
3. Los movimientos pendientes anteriores siguen su curso normal hasta el siguiente estado de cuenta.
4. Cargos fraudulentos post-bloqueo que aparezcan en el estado de cuenta se manejan vía §8.5 (disputa).
5. Si la TC se reemplaza con nuevo número, **se crea una `Tarjeta` nueva** (no se reusa la entidad): preserva historial y trazabilidad.

### 8.8 Cambio de titular

Cuando un titular deja la empresa o cambia de rol:

1. RH actualiza `Tarjeta.titular_id` con vigencia desde la fecha de cambio.
2. Movimientos históricos preservan referencia al titular original (snapshot semántico vía `usuario_que_uso_id`, no hay referencia textual al "titular" en el movimiento porque siempre se deriva de la tarjeta).
3. El nuevo titular asume responsabilidad de los siguientes cortes.

---

## 9. Eventos publicados/suscritos

### 9.1 Eventos publicados (TC-específicos, además de los del 01-diseno §8.1)

| Evento | Trigger | Consumidor | Payload |
|---|---|---|---|
| `MovimientoTarjetaCreditoRegistradoEvent` | Captura de un movimiento (flujos A y B) | Contabilidad | `movimiento_id`, `tarjeta_id`, `usuario_que_uso_id`, `tipo`, `monto_mxn`, `fecha`, `concepto_contable` |
| `EstadoCuentaTcCerradoEvent` | `EstadoCuentaTC.estado` pasa a `Cerrado` | Tesorería (vía la `FacturaProveedor` generada con `PasivoAutorizadoParaPagoEvent`), Contabilidad | `estado_cuenta_tc_id`, `tarjeta_id`, `total_banco_mxn`, `factura_proveedor_id`, `diferencia_cambiaria_mxn` |
| `MovimientoTarjetaCreditoEnDisputaEvent` | `en_disputa = true` | Notificaciones (alerta titular + Dirección de Finanzas) | `movimiento_id`, `motivo_disputa`, `monto_mxn`, `titular_id` |
| `TarjetaCreditoBloqueadaEvent` | `Tarjeta.estado = Bloqueada` | Notificaciones (titular + Dirección), Contabilidad (informativo) | `tarjeta_id`, `fecha_bloqueo`, `motivo_bloqueo` |

### 9.2 Eventos suscritos (TC-específicos)

| Evento | Origen | Acción |
|---|---|---|
| `PagoFacturaProveedorEvent` | Tesorería | Si la `FacturaProveedor` pagada es de un banco (proveedor `BancoEmisorTC`), trae el `EstadoCuentaTC` asociado a `estado = PagadoBanco` y todos sus movimientos a `PagadoAlBanco`. |

---

## 10. Endpoints HTTP

Patrón estándar del módulo (ADR-0020, ADR-0021, ADR-0012). Prefijo `/api/v1/cuentas-por-pagar/tc/...`.

### 10.1 Tarjetas

```
GET    /tc/tarjetas                          — listar tarjetas que el usuario puede ver (titulares = sus tarjetas; admin = todas)
GET    /tc/tarjetas/{id}                     — detalle
POST   /tc/tarjetas                          — alta de tarjeta (permiso .tc.tarjetas.alta)
PATCH  /tc/tarjetas/{id}                     — modificar atributos no críticos (alias, límite)
POST   /tc/tarjetas/{id}/bloquear            — body: {motivo}
POST   /tc/tarjetas/{id}/reactivar           — si bloqueo fue temporal
POST   /tc/tarjetas/{id}/usuarios-autorizados — body: {empleado_id, vigencia_desde, vigencia_hasta?, monto_max_mensual?}
DELETE /tc/tarjetas/{id}/usuarios-autorizados/{empleado_id}
```

### 10.2 Movimientos

```
GET    /tc/movimientos                       — bandeja, filtros por tarjeta/usuario/estado/periodo
GET    /tc/movimientos/{id}                  — detalle
POST   /tc/movimientos                       — capturar (flujos A y B; el body determina tipo)
PATCH  /tc/movimientos/{id}                  — solo si estado = Registrado (pre-conciliación)
POST   /tc/movimientos/{id}/disputar         — body: {motivo}
POST   /tc/movimientos/{id}/resolver-disputa — body: {resolucion: 'refund_recibido' | 'cargo_legitimo', evidencias}
```

### 10.3 Estados de cuenta

```
GET    /tc/estados-cuenta                          — listar
GET    /tc/estados-cuenta/{id}                     — detalle con bandeja de conciliación
POST   /tc/estados-cuenta                          — crear vacío para un periodo
POST   /tc/estados-cuenta/{id}/cargar-archivo      — multipart upload del Excel/CSV
POST   /tc/estados-cuenta/{id}/conciliar-automatico — corre algoritmo §7
PATCH  /tc/estados-cuenta/{id}/lineas/{linea_id}/confirmar-match  — confirma sugerencia
PATCH  /tc/estados-cuenta/{id}/lineas/{linea_id}/capturar-retroactivo — body: {tipo, concepto_contable, ...}
POST   /tc/estados-cuenta/{id}/cerrar              — pasa a `Conciliado` → `Cerrado` → dispara generación de FacturaProveedor del banco
```

### 10.4 Perfiles de parser

```
GET    /tc/perfiles-parser                  — listar (read-only en MVP; CRUD desde Admin en vNext)
GET    /tc/perfiles-parser/{codigo}
```

---

## 11. RBAC

Patrón ADR-0007. Permisos canónicos:

```
cuentas_por_pagar.tc.tarjetas.read
cuentas_por_pagar.tc.tarjetas.alta            // alta de TC corporativa (Admin)
cuentas_por_pagar.tc.tarjetas.bloquear        // titular o Admin
cuentas_por_pagar.tc.tarjetas.usuarios.alta   // titular o Admin

cuentas_por_pagar.tc.movimientos.read.propios // empleado: ve solo sus movimientos
cuentas_por_pagar.tc.movimientos.read.tarjeta // titular: ve todos los movimientos de sus tarjetas
cuentas_por_pagar.tc.movimientos.read.todos   // Admin/Auxiliar CxP: ve todos
cuentas_por_pagar.tc.movimientos.capturar.propios // empleado captura sus propios cargos
cuentas_por_pagar.tc.movimientos.capturar.cualquiera // Auxiliar de CxP
cuentas_por_pagar.tc.movimientos.disputar     // titular o Auxiliar

cuentas_por_pagar.tc.estados_cuenta.cargar    // titular o Auxiliar
cuentas_por_pagar.tc.estados_cuenta.conciliar // Auxiliar
cuentas_por_pagar.tc.estados_cuenta.cerrar    // titular + Auxiliar (ambos firman; o solo Auxiliar con autorización vía evidencias §8.2)

cuentas_por_pagar.tc.perfiles_parser.read
cuentas_por_pagar.tc.perfiles_parser.crud     // Admin (vNext)
```

Roles operativos asignados:

| Rol | Permisos TC |
|---|---|
| **EmpleadoConTcAutorizada** | `read.propios`, `capturar.propios` |
| **TitularTc** | `read.tarjeta`, `capturar.propios`, `disputar`, `estados_cuenta.cargar`, `estados_cuenta.cerrar` |
| **AuxiliarCxp** | `read.todos`, `capturar.cualquiera`, `disputar`, `estados_cuenta.cargar/conciliar/cerrar` |
| **AdminCxp** | Todos + `tarjetas.alta`, `tarjetas.usuarios.alta`, `perfiles_parser.crud` |
| **DireccionFinanzas** | `read.todos` + autoriza la `FacturaProveedor` del banco (vía permisos generales de autorización de facturas) |

---

## 12. Frontend — UX

### 12.1 Rutas

```
/cxp/tc                                    — landing del sub-módulo
/cxp/tc/tarjetas                           — bandeja tarjetas (admin)
/cxp/tc/tarjetas/$id                       — detalle de una tarjeta
/cxp/tc/movimientos                        — bandeja "Mis movimientos" o "Movimientos de mis tarjetas" (según rol)
/cxp/tc/movimientos/$id                    — detalle
/cxp/tc/estados-cuenta                     — bandeja de estados de cuenta abiertos/cerrados
/cxp/tc/estados-cuenta/$id                 — detalle + conciliación
/cxp/tc/estados-cuenta/$id/conciliacion    — vista dedicada de conciliación (full-screen)
```

### 12.2 Bandeja "Mis movimientos de TC"

Patrón P2 (bandeja filtrada server-side) según [`patrones-compras.md`](../../../frontend/docs/patrones-compras.md):

- Filtros: tarjeta, periodo, estado, tipo, conciliación.
- Columnas: fecha, merchant, monto MXN, tarjeta, estado, conciliado.
- Quick Create: "Nuevo movimiento sin CFDI" (sheet desde la derecha).
- Acción contextual por fila: abrir detalle, disputar.

### 12.3 Pantalla de conciliación

**Vista dual** (split horizontal):

- Izquierda: **líneas del banco** (parseadas del archivo), agrupadas por estado_match (Matched / Sugerencias / NoConciliado).
- Derecha: **movimientos capturados** que no están conciliados, ordenados por fecha.
- Centro: **dashboard de progreso** (total banco, conciliado, diferencia, % cumplido).

Acciones:

- **Click en línea del banco → click en movimiento sugerido**: confirma match.
- **Click en línea del banco no conciliada → "Capturar retroactivo"**: abre sheet pre-llenado con datos del banco.
- **Click en movimiento sin línea del banco → "Marcar como disputa"**: si pasaron 30+ días del corte y no aparece en el banco.
- **Drag-and-drop** opcional para asociar línea ↔ movimiento (puede ser un nice-to-have).

### 12.4 Topbar

- Selector global de tarjeta activa (chip en el topbar; si el usuario tiene solo una, no aparece).
- Acceso rápido a "Capturar movimiento sin CFDI" desde el Quick Create.

### 12.5 Notificaciones

- Titular: notificación cuando se cierra un estado de cuenta de sus tarjetas (requiere su revisión y firma) — vía email + bandeja.
- Auxiliar CxP: notificación cuando un estado de cuenta lleva 5 días en `EnConciliacion` sin progreso.

---

## 13. Migración inicial

`[Pendiente — área]`: confirmar si hay movimientos históricos de TC en SAP que necesiten migrarse.

Posibilidades:

1. **Sí, todos**: importar últimos 12 meses de movimientos con `captura_retroactiva = true`. Genera N `FacturaProveedor` (las que tenían CFDI). Sin generar `EstadoCuentaTC` (los pasivos al banco ya fueron pagados). Solo trazabilidad histórica.
2. **Sí, solo el periodo abierto**: importar movimientos del corte actual no cerrado. Permite que el primer cierre en el ERP refleje todo el corte.
3. **No**: arrancar limpio. Solo movimientos posteriores al go-live. Las facturas pagadas con TC antes del go-live quedan en SAP histórico.

Mi recomendación `[Asunción técnica]`: **opción 2** (solo periodo abierto). Importar histórico genera complicaciones (CFDIs que no están en el repositorio del ERP, conciliaciones contra estados de cuenta ya cerrados sin archivo del banco) que no aportan valor operativo.

---

## 14. Riesgos técnicos

| Riesgo | Probabilidad | Impacto | Mitigación |
|---|---|---|---|
| **Variaciones de formato del Excel del banco** entre cortes (banco cambia headers) | Media | Alto | Perfiles versionados; tests con corpus de archivos reales; alerta clara al Auxiliar cuando el parser detecte un header inesperado. |
| **Match falso positivo** entre dos cargos en mismo día, mismo monto, mismo merchant | Baja | Medio | Score ≥ 90 requiere también referencia bancaria coincidente cuando esté disponible; UI permite deshacer match. |
| **Diferencia cambiaria material** entre captura y corte (USD volátil) | Media | Bajo | Snapshot del TC del día de captura + reconciliación al cierre + contabilización en cuenta dedicada. Conversación con Contabilidad sobre umbral material. |
| **Movimiento sin CFDI sigue sin CFDI por meses** y el área lo necesita para DIOT | Media | Medio | Reporte mensual de "Movimientos sin CFDI" para que el responsable persiga al proveedor. Decisión del área sobre política de "fecha máxima sin CFDI". |
| **Duplicación de carga de archivo del banco** | Media | Bajo | Hash SHA-256 del archivo; rechazo en upload. |
| **Disputa abierta indefinidamente** que distorsiona reportes | Baja | Bajo | Alerta cuando una disputa tiene 60+ días sin resolución; escalamiento a Dirección. |
| **Fraude detectado tarde** (cargos falsos no atrapados por meses) | Baja | Alto | Conciliación mensual obligatoria + comparación contra histórico de merchant frecuente + alerta a titular ante merchant nuevo > umbral. |
| **TC con N usuarios autorizados confunde responsabilidades** | Media | Bajo | Trazabilidad clara: cada movimiento captura `usuario_que_uso_id`; reportes por usuario. |

---

## 15. Pendientes

### 15.1 Asunciones técnicas a confirmar

| # | Asunción | Pregunta abierta |
|---|---|---|
| D9 | Diferencia cambiaria → cuenta dedicada | Confirmar con Contabilidad el concepto contable exacto (`DIFERENCIA_CAMBIARIA_BANCO_TC` propuesto). |
| D10 | `usuario_que_uso_id` distinto de titular | Confirmar con el área que esto se quiere modelar (vs todo cae sobre el titular sin distinción). |
| D11 | Refund como movimiento con signo negativo | Confirmar convención: signo negativo en `monto_mxn` o flag `es_refund`. |
| D12 | Intereses/comisiones/anualidades como movimiento con tipo dedicado | Confirmar conceptos contables. |
| D13 | Captura retroactiva con flag | Confirmar que es auditable y no requiere autorización adicional. |
| D14 | Reemplazo de TC = nueva entidad | Confirmar con Dirección que esto está bien (vs preservar continuidad). |

### 15.2 Pendientes del área

1. Lista exacta de tarjetas operativas: cuántas, emisor por cada una, titulares actuales, límites, días de corte.
2. Política de "movimiento sin CFDI" — ¿se permite siempre, o hay un máximo mensual?
3. Política de disputa — ¿cuándo escala la disputa de "el titular gestiona" a "Legal interviene"?
4. ¿Hay tarjetas adicionales en otras monedas (USD nativas) o todas son MXN con cargos eventuales en USD?
5. Migración histórica (§13).

### 15.3 Pendientes técnicos

- Cierre del perfil de parser `AMEX_MX` con un archivo real de muestra para tests.
- Definir umbral material de `diferencia_cambiaria_mxn` que dispara revisión (vs auto-aplicar).
- Confirmar con Compras-OC si una `FacturaProveedor` con `proveedor.tipo = BancoEmisorTC` tiene algún caso especial en el sub-estado `Facturacion` de OC (probablemente no — no tiene OC asociada).

### 15.4 Diferidos a vNext

- Integración directa con API del banco (Amex Open Banking u otro).
- CRUD de perfiles de parser desde UI (MVP es seed).
- Lectura automática de CFDIs de Amex que el banco emite por las anualidades.
- Dashboard analítico de gasto por TC + por usuario + por concepto.
- Política de bonificaciones (cashback, puntos) — actualmente fuera de scope.

---

## Rev.

- **2026-05-22 — v1 (Draft)** — Anexo técnico inicial cubriendo: modelo de dominio detallado, esquema PG completo con índices, 5 flujos paso a paso (captura con/sin CFDI, cierre de estado de cuenta, generación de pasivo, refund), parser configurable por perfil de banco, algoritmo de conciliación automática con score, 8 casos especiales fiscales/operativos, eventos TC-específicos, endpoints, RBAC, frontend UX, migración inicial. Asunciones técnicas D9-D14 listadas en §15 para confirmación con el área y Contabilidad.
