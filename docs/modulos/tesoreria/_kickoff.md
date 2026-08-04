# Kickoff — Módulo Tesorería / Bancos (`Millet.Tesoreria`)

> Guía operativa para la sesión que implemente el módulo. Leer junto con
> [`00-levantamiento.md`](00-levantamiento.md) (fuente funcional, TES-1..9,
> gaps T-G1..T-G11) y [`03-pr-breakdown.md`](03-pr-breakdown.md).

---

## 0. Contexto rápido

Tesorería posee el hecho bancario en ambos sentidos (absorbe "Ingresos",
TES-9): cuentas propias, movimientos, ejecución de pagos a proveedor,
confirmación de cobros, pago a cuenta, conciliación y REPP recibido.
**No decide qué pagar** (CxP), **no emite CFDI** (Facturación), **no
postea GL** (Contabilidad futura/SAP). Autoridad funcional: Javier
Humberto Canche Chan. El contrato con CxP está congelado desde F9-PR1 —
este módulo es la contraparte, no el diseñador.

## 1. Convenciones operativas

### Auto-mode y branches
- Ramas backend `tesoreria/pr{N}-{slug}`, frontend `tesoreria-fe/pr{N}-{slug}`.
- ✅ `tesoreria/*` y `tesoreria-fe/*` están en la allowlist del hook
  `validate-auto-merge` (alta 2026-07-14): auto-mode N2 — auto hasta merge
  con CI verde + rama al día con main.
- Sesiones concurrentes en el checkout: trabajo multi-PR en worktree bajo
  `.claude/worktrees/`; verificar rama antes de commitear.

### Técnicas (no negociables)
- Esquema `tesoreria`; permisos `tesoreria.{recurso}.{accion}` (3
  segmentos exactos); GUIDs del siguiente bloque libre en
  `PermisosCanonicos` (CxC usó `0000000a-*`) + migration de Identidad.
- DbContext nuevo (PR-1): `Program.cs` + `MigrationsHealthCheckOptions` +
  `deploy-app-dev.yml` **en el mismo PR**.
- Enums persistidos: `HasCheckConstraint` + migration + mirror FE.
- App settings nuevos → `appservice.bicep` en el mismo PR.
- Topics/subscriptions → Bicep, `what-if` antes de aplicar.
- Cero SELECT a tablas de otro módulo: eventos o `I*ReadPort`.
- Idempotency-Key = UUID v4 puro por submit (sin sufijos).
- Stubs con `PLATFORM-TODO(<id>)` (tabla en [`01-diseno.md`](01-diseno.md) §13).
- **Los payloads de los 4 eventos `tesoreria.*.v1` se copian de
  `ContratosEspejo.cs:97-141` de CxP, no se redactan de memoria.**

### Después de cada PR mergeado
Actualizar memoria de progreso + `Rev.` del doc tocado si cambió una decisión.

## 2. Plan secuencial

**Bloque A — Fundaciones:** PR-1 → PR-2 → PR-3.

### 🛑 STOP #1 — en PR-3
Dos decisiones con Eduardo antes de mergear: (1) **backfill** de pasivos
autorizados previos a la subscription ([`04-cuidados-infra.md`](04-cuidados-infra.md)
§3.1); (2) el `IProveedorBancoReadPort` **toca DatosMaestros** — verificar
que no haya trabajo en curso de Admin/DatosMaestros en conflicto.

**Bloque B — Contrato congelado:** PR-4 (pago end-to-end). Test de
contrato contra los records espejo de CxP obligatorio.

### 🛑 STOP #2 — antes de PR-5
Gate T-G4 (matriz de corridas: umbrales y suplencias de Javier) +
verificar si la matriz de Compras tiene contrato público reutilizable o
va stub `<MatrizAutorizacionCompartida>`.

**Bloque C — post-PR-4 (paralelizables):** PR-6 (pago a cuenta) → PR-8
(REPP; gate T-G11) → PR-10 (reportes).

### 🛑 STOP #3 — antes de PR-7
PR-7 **requiere PR gemelo en el módulo Facturación** (listener de
`tesoreria-events` que invoca `EmitirReppCommand`). Otra sesión suele
operar Facturación: coordinar con Eduardo el orden y cerrar juntos el
payload final de `pago-cliente.confirmado.v1`. Validar también T-G7
(rechazo de propuesta) contra el agregado de CxC.

**Bloque D — Conciliación:** PR-9 tras T-G2 (bancos/formatos de Javier)
y T-G8 (saldos iniciales).

## 3. Gates funcionales pendientes (no arrancar el PR sin esto)

| Gate | Bloquea | Owner |
|---|---|---|
| T-G2 bancos/formatos de extracto | PR-9 | Javier |
| T-G4 matriz de corridas | PR-5 | Javier |
| T-G8 saldos iniciales | PR-9 | Eduardo/Javier |
| T-G11 `MetodoPago` en pasivo | PR-8 (versión completa) | Eduardo/CxP |
| PR gemelo Facturación | PR-7 (ciclo completo) | Eduardo |

PR-1..PR-4, PR-6 y PR-10 **no tienen gate**: son arrancables hoy.
