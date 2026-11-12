# Implementation Plan: entregar servicios CE con lo ya construido

**Branch**: `fix/ce-servicios-circuito` | **Date**:2026-11-11 | **Spec**: [spec.md](spec.md)
**Estado**: ejecución local autorizada por Eliam. Paquete reutilizado sobre develop; gates en curso, sin publicación ni pase QA de este circuito todavía.

## Summary
Reutilizar correccionesCE-02/CE-03 y deltaCE-01 ya preparados en COMEX_AF_listo_pruebas. Combinar con develop954a52 preservando los8solapamientos; completar pruebas del circuito real y de vigencia de firma tras sustitución; integrar sólo tras gate/CI y probar en QA con perfiles reales autorizados. No volver a pedir proceso/documentos ya recibidos.

## Technical Context
**Language/Version**: C#/.NET10, TypeScript/React vigentes en repo.
**Primary Dependencies**: MediatR, EFCore/Npgsql, puertos y RBAC existentes; ninguna nueva.
**Storage**: PostgreSQL;3migraciones aditivas preparadas Compras/CxP/Identidad, indicador false en históricos; permisos sin asignaciones automáticas.
**Testing**: xUnit, gateHTTP/PostgreSQL, Vitest y navegaciónQA. Reusar fixtureEscenarioCE/AF y fixtures compartidos.
**Target Platform**: local aislado y QA AppService/SWA identificados; no producción.
**Project Type**: monolito modular/web.
**Performance Goals**: consultas de saldo/pagos por empresa y conjuntoIDs; sin barridos globales ni worker nuevo.
**Constraints**: anticipo y pedimento separados; control de versión/idempotencia, personas distintas, límites empresa/sucursal, historia y reversas. Conservar lectura accionable/autorización masiva reciente.
**Scale/Scope**: servicio ordinarioCE de una factura porOC y cierreIMPO; regresiones en módulos conectados. No AF nuevo ni reaperturaP25.

## Constitution Check
I fuentes/autoridad: PASS, correo y decisiones diferenciados de código/QA.
II mínimo/fronteras: PASS, reutilización puertos del dueño; no dependencia/nuevo subsistema.
III regresiones primero: PASS en plan; dos pruebas de flujo y riesgoN2 antes de cambiar lógica.
IV seguridad: PASS en plan; no permisos silenciosos ni borradohistóricos; negativos obligatorios.
V evidencia: PASS en plan; entrega exige gate/CI/QA/conformidad separadas. Sin aceptación anticipada.
Post-design: mismas condiciones; ninguna excepción de complejidad. Fallo en gate detiene integración, no se elimina prueba.

## Project Structure
Documentación: spec.md, research.md, data-model.md, contracts/flujo-servicio.md, quickstart.md, tasks.md, checklists/requirements.md.
Código a reutilizar y revisar:
- Compras/Application/Oc/{Autorizar,ListarPendientesAutorizacion,CrearOrdenCompraVacia,CrearOrdenCompraDesdeRequisicion,DuplicarOrdenCompra,Rechazar}, FirmasServicioCeGuard; Domain/Oc/OrdenCompra; PublicPorts/IFacturaServicioCeReadPort; migración/snapshot.
- CuentasPorPagar/Application/FacturaProveedor/{CapturarFacturaConOc,AutorizarFactura,Elegibilidad,Queries}; Domain/FacturaProveedor; PublicPorts/IFacturaGastoReadPort/IPagosFacturaCeReadPort; Infrastructure/PublicAdapters/FacturaGastoReadAdapter y migración/snapshot.
- ComercioExterior/Application/CierreServiciosCeServicio, ExpedienteServicio/Handlers/FinanzasServicio; RecepcionCe y listener para delta de reversas.
- Api/Infrastructure/Adapters/{FacturaServicioCeReadAdapter,PagosFacturaCeReadAdapter}, Program; Identidad permisos/migración; infra/modules/servicebus.bicep.
- frontend/features/compras/ordenes y features/cxp; lib/auth/permission-codes.ts; conservar UI actualCE.
Rutas anteriores relativas a backend/src o frontend/src según prefijo de módulo. Pruebas detalladas en tasks.md.

## Fases y puertas de salida
1. **Integración local**: conservar origen por huella, aplicar tres vías en rama nueva, revisar8solapamientos/3migraciones/registroDI. Sin cambiar worktree del paquete original. Salida: diffs explicados, aplicacióncompila, modelos/migracionescoherentes.
2. **Completar lo verificable de VILO**: circuito entero porHTTP, sustitución trasN2, pagos/reversas y cierres. Si falla una guarda, corregir raíz en servicio compartido. Sin retocar terceros para simular éxito.
3. **Gate**: unitariasCompras/CxP/CE/Api/Tesorería y UI tocada; HTTP dirigidoCE+CxP+Compras. Al tocar compartidos/migraciones, gate completoCI obligatorio; pruebas rojas no se desactivan. Registrar casos/corridas/reintentosfuera del repo.
4. **Publicación/pase**: preparar commit/PR concreto y aprobación específica del paquete nuevo si corresponde. CIverde antesmerge. Refrescar API/portal/historialQA, preservar lotes ajenos, respaldo y paseaditivo, verificación de versión/health. Roles/cuentasreales sólo con autorización y configuración revisable.
5. **Prueba y aceptación**: perfilesCE/CxP/Comercial/Tesorería; unservicio y una deuda, impago bloqueado, pagototal permite, reversa vuelveabloquear. Crédito/anticipo/pedimento separados. Acta con evidenciaporpaso. La junta valida un caso existente; si el nuevo circuito no está enQA se declara explícito y no se ofrece demo terminada.

## Dependencias que no son falta de programación general
Cuentas fletes/almacenajes/otros y cuentaanticipo: consultar catálogo/respuestas antes de pedir. Confirmada201.07.00.00. Rolesnominales/suplencia yM151 puntuales. PACdepruebas es requisito sólo deQ-CEfiscal. Originales5recibidos. OTR se coordina con tareaexistente; no repetir revalidación/crear retorno.
No se promete hora de terminación antes de estimar pase y disponibilidad de los aprobadores. Responsabilidad de integrar/probar código:VILO; configuración/validación de área se asigna concretamente.

## Complexity Tracking
Sin violaciones. No tabla de workflow paralela, bypass de autorización, servicio de pago nuevo ni migraciónmasiva de históricos.
