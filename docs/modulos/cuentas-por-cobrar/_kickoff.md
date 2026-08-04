# Kickoff — Módulo Cuentas por Cobrar (`Millet.CuentasPorCobrar`)

> Guía operativa para la sesión que implemente el módulo. Leer junto con
> [`00-levantamiento.md`](00-levantamiento.md) (fuente funcional, decisiones
> CXC-1..7 y validación contra código §9) y [`03-pr-breakdown.md`](03-pr-breakdown.md).

---

## 0. Contexto rápido

CxC posee el ciclo del cobrable (crédito, liberación, cobranza, cartera,
propuesta de aplicación, estados de cuenta); **no emite CFDI** — consume los
eventos de Facturación. Espejo de CxP del lado del ingreso. Autoridad funcional:
Néstor Prida.

## 1. Convenciones operativas

### Auto-mode y branches
- Ramas backend `cxc/pr{N}-{slug}`, frontend `cxc-fe/pr{N}-{slug}`.
- ⚠️ `cxc/*` y `cxc-fe/*` **aún no están en la allowlist del hook
  `validate-auto-merge`**: o Eduardo los da de alta antes de arrancar (mismo
  tratamiento que `cxp/*`), o los merges van manuales.
- Sesiones concurrentes en el checkout: trabajo multi-PR en worktree bajo
  `.claude/worktrees/`; verificar rama en la misma cadena antes de commitear.

### Técnicas (no negociables)
- Esquema `cuentas_por_cobrar`; permisos `cuentas_por_cobrar.{recurso}.{accion}`
  (3 segmentos exactos), GUIDs `0000000a-*`; migration en `IdentidadDbContext`
  con cada permiso.
- DbContext nuevo (PR-1): `Program.cs` + `MigrationsHealthCheckOptions` +
  `deploy-app-dev.yml` **en el mismo PR**.
- Enums persistidos: `HasCheckConstraint` + migration + mirror FE.
- App settings nuevos → `appservice.bicep` en el mismo PR.
- Topics/subscriptions → Bicep, `what-if` antes de aplicar.
- Cero SELECT a tablas de otro módulo: eventos o `I*ReadPort`.
- Stubs con `PLATFORM-TODO(<id>)` (tabla en [`01-diseno.md`](01-diseno.md) §13).

### Después de cada PR mergeado
Actualizar memoria de progreso + `Rev.` del doc tocado si cambió una decisión.

## 2. Plan secuencial

**Bloque A — Fundaciones (independiente):** PR-1 → PR-2.

### 🛑 STOP #1 — antes de PR-3
PR-3 **toca el módulo Facturación** (promover `AnticipoSaldoDetalle` de
`Facturacion.Application.Anticipos.Queries` a contrato público +
adapter de `IFacturacionAnticiposReadPort`). Verificar que no haya trabajo en
curso de Facturación en conflicto (otra sesión suele operar ese módulo) y
decidir con Eduardo el **backfill** de `factura_cartera` para facturas timbradas
antes del deploy (04-cuidados §3.1).

**Bloque B — Cartera core:** PR-3 (listener + proyección + read port) → PR-6
(antigüedad + estado de cuenta).

**Bloque C — Operación de crédito (paralelo a B tras PR-2):** PR-4 (liberación;
gate suave: series nacionales confirmadas por Prida) → PR-5 (cobranza) → PR-8
(alertas).

**Bloque D — Aplicación de pagos:** PR-7 (tras PR-3). La confirmación es manual
interina (A2) hasta que exista el emisor de `PagoClienteConfirmadoEvent`.

### 🛑 STOP #2 — antes de PR-9
Bloqueado por contrato con equipo A+W (G1/G6/G-writeback). No arrancar sin la
definición de la tabla/columna en `MILLET_INTEGRACION`. El script on-prem se
re-corre a mano en SER-DATA (SQL Server 2016 RTM, stub+`ALTER`).

## 3. Puntos de sincronización

| Con | Qué | Cuándo |
|---|---|---|
| Facturación (código) | Promover `AnticipoSaldoDetalle` + adapter | PR-3 |
| Prida | Series de folio nacionales, plazos, alcance migración SAP | antes de PR-4 (gate suave) |
| Fiscal | Tolerancia < $50 USD sin CFDI | antes de PR-7 |
| Ingresos/Tesorería | Owner del `PagoClienteConfirmadoEvent` | post-MVP (A2 cubre) |
| Equipo A+W | G1 (en firme), G6 (estado_origen), contrato write-back | PR-9 |

## 4. Qué publica CxC que otros esperan

`DecisionLiberacionEmitidaEvent` (write-back PR-9),
`PropuestaAplicacionPagoCreadaEvent` (Ingresos),
`AlertaCarteraGeneradaEvent` (Notificaciones, cuando exista el motor).
Ninguno es prerequisito de otro módulo en curso: CxC no bloquea a nadie.

## 5. Primer comando

```bash
git checkout main && git pull
git checkout -b cxc/pr1-foundation
# Empieza por CXC-PR1 según 03-pr-breakdown
```

## 6. Si algo se sale del plan

Registrar la desviación en el doc correspondiente (`Rev.`), no improvisar
decisiones estructurales: CXC-1..7 están aprobadas por Eduardo y cambiarlas
requiere su confirmación explícita.
