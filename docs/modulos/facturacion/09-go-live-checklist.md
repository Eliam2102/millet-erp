# 09 — Go-live checklist (Facturación)

> **Proyecto:** ERP Millet — Módulo 3 (Facturación CFDI 4.0).
> **Versión:** 1.0 — Fecha: 2026-05-30. Owner: Eduardo Paredes.
> El go-live **real** depende del cierre de `Integraciones.Fiscal` fase 2
> (timbrado real). Hasta entonces, go-live limitado a flujos sin timbre real.

---

## 1. Pre go-live — T-7 días

- [ ] `Integraciones.Fiscal` fase 2 cerrada (timbrado/cancelación reales con
      complementos) y validada en sandbox FiscalAPI. **Bloqueante.**
- [ ] CSD del/los RFC emisor(es) cargado(s) en Key Vault (`ICsdProvider` real).
- [ ] Master de **Cliente** con datos fiscales (RFC, régimen, CP, defaults)
      cargado/migrado; auto-provisión A+W operativa o carga inicial hecha.
- [ ] Master de **Producto** con clave SAT, clave unidad, retención, `origen`.
- [ ] Series sembradas (`Compartido.Series`) por sucursal-tipo + `FANT`.
- [ ] Catálogos SAT vigentes (`Millet.Catalogos` sincronizado).
- [ ] Hybrid Connection a `SER-DATA` establecida; tabla-puente
      `aw_solicitud_pedido` creada con el esquema del ERP; A+W la llena.
- [ ] Contrato de la vista de datos del pedido A+W acordado y probado.
- [ ] Permisos `facturacion.*` asignados a roles (Cajero, Caja general,
      Contador General, CxC).
- [ ] `ConceptoContable` sembrado (placeholders `TBD-*`).
- [ ] Workers verdes en staging: `AwSolicitudesWorker`, `TimbradoPendienteWorker`,
      `CancelacionSatPollerWorker`, `EnvioCfdiCorreoWorker`, `PedimentoSalidasWorker`,
      `WriteBackResultadoWorker`, `OutboxPublisherWorker<FacturacionDbContext>`.

---

## 2. Pre go-live — T-1 día

- [ ] Workflow build-and-test verde en `main`.
- [ ] `/health/ready` verde con migraciones de `facturacion` aplicadas en prod.
- [ ] Deploy a prod revisado (what-if antes de create; segundo par de ojos).
- [ ] FiscalAPI apuntando a **producción** (variable de ambiente), no sandbox.
- [ ] Backup/restore de `facturacion` probado.
- [ ] Plan de rollback listo (§8).

---

## 3. Día del corte (T-0)

- [ ] Habilitar el módulo para un piloto (1 sucursal/caja) antes del rollout
      total.
- [ ] Emitir una factura nominal real de prueba (monto bajo) → validar UUID,
      XML, PDF bilingüe, envío de correo.
- [ ] Emitir un anticipo + factura final + NC de amortización → validar cadena
      07 y restitución de saldo al cancelar.
- [ ] Probar ingesta A+W: Alta → factura; Modificación → refresh; Cancelación
      sobre `Facturado` → alerta manual.
- [ ] Probar cancelación SAT 4.0 (motivo, sustituto) y re-facturación.

---

## 4. Smoke tests funcionales post go-live

- [ ] Mostrador: cargar pedido, cobro multi-forma, emitir, NC bonificación.
- [ ] Maquila: anticipo → saldo → factura final → NC amortización (atómica).
- [ ] Reparto: forma 99 + liquidación de ruta + REPP.
- [ ] Exportación: CCE + IVA 0% + pedimento (compuerta condicional).
- [ ] Carta Porte: tramo T/I + "siguiente tramo".
- [ ] Activo fijo: autorización Contador General + asientos de baja.
- [ ] Captura manual: pedido con líneas inline → factura.

---

## 5. Smoke técnico post go-live

- [ ] Eventos publicados al Outbox y consumidos (Contabilidad/CxC stubs).
- [ ] Write-back a A+W (claim + estatus 115/70) reflejado.
- [ ] Sin timbres perdidos (validación local previa efectiva).
- [ ] Métricas y alertas activas (§3 runbook).
- [ ] Sin PAN completo / CSD en logs.

---

## 6. Validación post go-live — T+7 días

- [ ] 0 pedidos duplicados / doble-facturados.
- [ ] Backlog de cola A+W bajo control.
- [ ] Conciliación de CFDIs emitidos (descarga masiva) vs. emitidos por el ERP.
- [ ] Revisión de excepciones de ingesta resueltas.

---

## 7. Validación post go-live — T+30 días

- [ ] Cierre de mes: candado de período cerrado operó (sin emisión en período
      cerrado).
- [ ] Reportes (Liquidación de caja, Estados de anticipo) cuadran.
- [ ] Inventario de `PLATFORM-TODO` revisado; cierres pendientes planificados.

---

## 8. Rollback

- Feature flag / deshabilitar el módulo en el shell (caja vuelve a SAP/proceso
  previo).
- Los CFDIs ya timbrados son inmutables y válidos ante el SAT (no se revierten).
- Pausar workers de ingesta/write-back para no escribir a A+W.
- Restaurar `facturacion` desde backup solo si hay corrupción de datos (los
  CFDIs emitidos no se borran).

---

## 9. Sign-off

| Rol | Nombre | Fecha | Firma |
|---|---|---|---|
| Owner del módulo | Eduardo Paredes | | |
| Contador General | | | |
| Responsable de caja | | | |
| Owner técnico | | | |

---

## Rev.

| Versión | Fecha | Cambios |
|---|---|---|
| 1.0 | 2026-05-30 | Checklist inicial. |
