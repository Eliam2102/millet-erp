# Go-Live Checklist — Módulo Almacén

> **Audience:** Eduardo Paredes (owner) + Carlos Burgos (Jefe Almacén).
>
> **Estado:** v1 (F9-PR1, 2026-05-23).

---

## 0. Cómo usar este checklist

Cada ítem tiene `[ ]` para marcar al validar. Mientras haya items abiertos, el módulo NO está listo para producción.

---

## 1. Datos maestros (pre-requisitos)

- [ ] Catálogo de Almacenes y Sub-almacenes cargado desde SAP (script F1-PR3 + verificación manual). Cada sub-almacén tiene su tipo correcto (Insumos / MaterialesDirectos / MaterialEnRevision / Transitorio).
- [ ] Sub-almacén especial `MATERIAL_EN_REVISION` (MAT-REV) creado por sucursal (A15).
- [ ] Catálogo de Artículos migrado con criterio "sin movimiento en 2 años → no migrar" (cuidado §7.1).
- [ ] Cada artículo tiene: UM válida, sub-almacén default (cuando aplique), costo unitario inicial.
- [ ] Reporte de "artículos a archivar" validado por Carlos Burgos.
- [ ] Reporte de "artículos incompletos" cerrado en cero (todos completos).

## 2. Saldos iniciales

- [ ] Saldos iniciales aplicados como movimientos `AjustePositivo` con flag `es_saldo_inicial=true` (auditoría trazable; cuidado §7.3).
- [ ] Suma de saldos por sub-almacén coincide con SAP al día del cut-over.
- [ ] Costo promedio inicial = costo unitario de SAP por artículo.

## 3. Periodos cerrados

- [ ] Todos los meses anteriores al go-live están en `almacen.periodos_cerrados` (script de seed F1-PR3 + verificación).
- [ ] El periodo del mes del go-live está abierto.

## 4. Permisos y roles

- [ ] Permisos canónicos `almacen.*` seedeados (30 permisos, namespace `00000008-*`).
- [ ] Roles operativos asignados a usuarios reales:
  - Almacenista (Insumos)
  - Almacenista (Materiales directos)
  - Supervisor de Insumos
  - Jefe Almacén (Carlos Burgos)
  - Auditor Externo (con vigencia documentada)
- [ ] Pruebas RBAC en dev: usuarios con cada rol ven solo lo que corresponde (captura sin sesgo A6, salida-leer-propias vs leer-todas).

## 5. Infraestructura

- [ ] Migraciones EF Core de Almacén aplicadas en `prod` (build de release con `dotnet ef database update --context AlmacenDbContext`).
- [ ] Outbox `almacen.integration_events_outbox` operativo: `OutboxPublisherWorker<AlmacenDbContext>` registra eventos publicados a Service Bus.
- [ ] Subscription `almacen-subscription` creada en topic `cuentas-por-pagar-events` (Bicep o manualmente).
- [ ] `CxpEventListenerWorker` levantado (logs muestran "iniciado").
- [ ] Health check `/health/ready` retorna 200 con `AlmacenDbContext` en `MigrationsHealthCheckOptions.ContextTypes`.
- [ ] Service Bus connection string en Key Vault.

## 6. Validación end-to-end

Ejecutar el ciclo completo con un usuario super-admin:

- [ ] **Recepción Variante A**: POST `/api/v1/almacen/recepciones` con OC + CFDI → 201, saldo actualiza, evento en outbox y `cuentas-por-pagar-events` topic (CxP recibe).
- [ ] **Recepción Variante B**: POST `/api/v1/almacen/recepciones/packing-list` → 201 con `factura_pendiente=true`. Luego CxP factura → listener Almacén concilia.
- [ ] **Salida normal**: POST `/api/v1/almacen/salidas` con RQ aprobada → 201, saldo decrementa.
- [ ] **Salida con reserva**: reservar primero (`POST /reservas`), luego salida → consume reserva, `cantidad_reservada` baja.
- [ ] **Salida con vale**: POST `/api/v1/almacen/salidas/vale` → 201 con `pendiente_regularizacion=true`. Worker SLA notifica a los 2 días.
- [ ] **Devolución interna**: POST `/api/v1/almacen/devoluciones-internas` con `estado_material=Integro` → saldo restituido al costo de salida (A9).
- [ ] **Devolución interna a MAT-REV**: con `estado_material=Danado` → debe ir a sub-almacén `MaterialEnRevision`.
- [ ] **Devolución a proveedor**: ciclo Iniciar → Evidencia → Solicitar → Autorizar → Registrar → (CxP emite NC fiscal CFDI 03) → ConciliadaConNcFiscal.
- [ ] **Inventario rotativo**: Crear → Iniciar → Capturar → Enviar a conciliación → Aprobar → Aplicar. Movimientos `AjustePositivo`/`AjusteNegativo` generados.
- [ ] **Inventario anual**: bloqueo de salidas activo durante captura.
- [ ] **Cierre de mes**: POST `/api/v1/almacen/cierre-mes` con todos los movimientos del mes firmados → 200. Movimiento posterior con fecha del mes cerrado → 422.
- [ ] **Reportes**: ALFAK + MP-CNK devuelven datos coherentes con saldos vigentes.

## 7. Performance

- [ ] Bandeja `/api/v1/almacen/movimientos` con 5000 movs sintéticos: latencia p95 < 500ms.
- [ ] Saldo materializado con 100 inserts concurrentes: 0 errores, 0 saldos negativos, sin race conditions.
- [ ] 2 reservas paralelas a stock 100 cada una pidiendo 80: una gana (Exitosa=true), la otra falla (`Exitosa=false`, `CantidadDisponible=20`).
- [ ] Reporte ALFAK con periodo de 6 meses: respuesta < 5s.

## 8. Plan de fallback

- [ ] Procedimiento documentado para revertir el módulo Almacén en caso de bug crítico:
  - Restaurar el App Service a la versión pre-deploy.
  - Migraciones EF Core de Almacén son aditivas (no destructivas); la rollback es revertir el código que las consume.
  - Los datos capturados en Almacén pueden re-cargarse a SAP via export manual (script preparado).
- [ ] Backup automático de Postgres habilitado y validado (point-in-time recovery a 7 días).

## 9. Comunicación

- [ ] Anuncio al equipo de Almacén con 1 semana de antelación: fecha del go-live, capacitación, soporte directo.
- [ ] Capacitación con Carlos Burgos + equipo (~3h) cubriendo:
  - Captura de recepciones (Variantes A y B).
  - Captura de salidas (con RQ y por vale).
  - Inventario físico con captura sin sesgo (A6).
  - Devolución a proveedor (sub-flujo 8.B).
- [ ] Canal de soporte directo (Teams/Slack) durante las primeras 2 semanas post-go-live.

## 10. Firmas

- [ ] **Eduardo Paredes** (owner) — firmado: ____________ fecha: ____________
- [ ] **Carlos Burgos** (Jefe Almacén) — firmado: ____________ fecha: ____________

---

## Rev.

- **2026-05-23 — v1** — Checklist inicial.
