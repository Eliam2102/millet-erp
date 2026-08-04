# Kickoff — Módulo Facturación (`Millet.Facturacion`)

> Punto de entrada para quien implementa el módulo. Lee primero
> [`00-levantamiento.md`](00-levantamiento.md) y [`01-diseno.md`](01-diseno.md).

---

## 0. Contexto rápido del módulo

Emisión de **CFDI 4.0** del lado emitido: mostrador, maquila/anticipos,
reparto, obras, exportación (CCE), Carta Porte, activos fijos, administrativa.
Más anticipos, NCs, REPP y cancelación SAT 4.0.

**Tres orígenes de pedido:** A+W, Planta Pintura, captura manual. (Salidas
**no** origina pedidos: aporta el pedimento.) **No timbra directo:** consume
`IFiscalApiClient` de `Integraciones.Fiscal` (stub hasta su fase 2). ERP es
master único de Cliente/Producto (clientes nacen de A+W por auto-provisión).

---

## 1. Convenciones operativas

- Ramas `facturacion/...` (backend) y `facturacion-fe/...` (frontend) —
  patrón de **auto-mode** (hook `validate-auto-merge.ps1`); nunca directo a
  `main`.
- PRs consolidados (memoria [feedback_pr_granularidad]); cada PR deja
  `/health/ready` verde.
- DbContext nuevo → tocar `Program.cs` + `deploy-app-dev.yml` en el **mismo PR**
  (memoria [feedback_dbcontext_nuevo_checklist]).
- Permisos `facturacion.*` → migración en `IdentidadDbContext` (memoria
  [feedback_permisos_canonicos_migration]).
- Stubs con `PLATFORM-TODO(<id>)` (ADR-0031); tabla en §13 del diseño.
- Idioma: negocio/comentarios en español, código en inglés.
- No commitear sin permiso del owner.

---

## 2. Plan secuencial

Sigue el [`02-plan-implementacion.md`](02-plan-implementacion.md) (F0–F12) y el
[`03-pr-breakdown.md`](03-pr-breakdown.md). Resumen:

1. **F0** Foundation → **F1** walking skeleton (captura manual + emisión stub).
2. **F2** repo CFDI común + PDF → **F3** ingesta A+W (cola) + provisión master.
3. **F4** anticipos → **F5** NC/cancelación → **F6** REPP.
4. **F7** exportación/CCE/pedimento → **F8** Carta Porte → **F9** activos.
5. **F10** eventos + write-back → **F11** reportes → **F12** timbrado real.

**Todo contra el stub de timbrado hasta F12** (no esperar a `Integraciones.Fiscal`).

---

## 3. Resumen de puntos de sincronización

- **`Integraciones.Fiscal` fase 2** (timbrado real): wireup en F12.
- **`Integraciones.Origenes`** (Planta Pintura + Salidas): módulo nuevo,
  consumo en F7/F10.
- **DatosMaestros** (fiscal + `origen` + provisioning): consumo en F3.
- **CxP** (repo CFDI común): coordinación en F2-PR1.
- **Contabilidad/Tesorería**: stubs hasta que existan.

---

## 4. Qué publicas que otros necesitan

- Eventos al Outbox: `FacturaVentaTimbradaEvent`, `FacturaAnticipoTimbradaEvent`,
  `NotaCreditoTimbradaEvent`, `ReciboPagoTimbradoEvent`, `ComprobanteCanceladoEvent`
  (Contabilidad, CxC).
- Write-back a A+W: claim + `estado_facturacion` + UUID (tabla-puente).
- `IFacturacionCfdiReadPort` para Obras/CxC (CFDIs por obra/cliente).
- **El esquema de `aw_solicitud_pedido` lo defines tú** (D18) y lo pasas al
  equipo de A+W.

---

## 5. Decisiones clave que no debes romper

- **Pedido ≠ factura**; `Facturado` es estado del pedido; ≤1 factura vigente;
  re-facturable tras cancelar; historial N comprobantes.
- **No existe "parcialmente facturado"** (D19): unidades discretas.
- **La factura manda** (D20): Modif/Cancel de A+W solo sin CFDI.
- **Claim persiste** (nunca se borra) + doble candado `ingesta_control`.
- **Pedimento condicional** (compuerta por factura, nunca global).
- **NC de amortización atómica** con el timbre de la factura final.
- **Series configurables** (`Compartido.Series`, seed FANT).

---

## 6. Catálogos / pendientes del área (pre-go-live)

- Contrato de columnas de la vista de datos del pedido A+W.
- Sample fiscal: anticipo + factura final + NC amortización (validar XML).
- Catálogo de estatus A+W (15/69/70/115).
- Secuencia pedimento vs. timbre (confirmar con sample).
- Códigos contables reales (reemplazar `TBD-*`).

---

## 7. Primer comando

Empieza por **F0-PR1** según el `03-pr-breakdown.md` §Fase 0 (csproj + smoke).

---

## 8. Si algo se sale del plan

Reporta al owner (Eduardo Paredes) y actualiza el doc correspondiente +
memoria. Las decisiones cerradas (D1–D20) viven en el `00-levantamiento.md`
§15; cualquier cambio se versiona ahí.
