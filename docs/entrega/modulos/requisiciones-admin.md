# Requisiciones — Ficha de administración

## Permisos canónicos

Claves exactas de `Identidad.Domain.PermisosCanonicos` y a qué rol operativo se
sugieren (no hay roles seed por módulo: solo `super-admin` tiene todo y
`auditor` los `.leer`; los roles operativos se arman a mano en Identidad):

| Permiso | Rol sugerido |
|---|---|
| `compras.requisiciones.leer` | Todos los usuarios del módulo |
| `compras.requisiciones.crear`, `.editar`, `.eliminar` | Solicitante |
| `compras.requisiciones.seleccionar-requisitante` | Asistentes que capturan a nombre de otro |
| `compras.requisiciones.autorizar-nivel1` | Jefe de departamento / Jefe de Almacén |
| `compras.requisiciones.autorizar-nivel2` | Autorizador N2 (Dirección/Gerencia) |
| `compras.requisiciones.rechazar` | Ambos niveles de aprobador |
| `compras.requisiciones.cancelar` | Solicitante senior / Comprador |
| `compras.requisiciones.cerrar-manual` | Jefe de Almacén / Almacenista (ADR-0043) |
| `compras.requisiciones.editar-de-otros-usuarios`, `.ver-todos-departamentos` | Perfiles de supervisión |
| `compras.aprobadores.administrar` | Administrador del módulo Compras |

## Configuración previa obligatoria

Lo que la verificación e2e (2026-07-15) confirmó que debe existir **antes** de
operar:

1. **Matriz de aprobadores por departamento** — en `/compras/admin/aprobadores`:
   designar JefeDpto, JefeAlmacen y AutorizadorN2 por departamento, con
   vigencias. Sin aprobador designado, la RQ transmitida no tiene quién la firme.
2. **Umbrales de monto por departamento** (tabla
   `umbrales_aprobacion_departamento`): definen cuándo se exige Nivel 2.
3. **La sucursal debe tener departamentos ligados.** Hallazgo vigente: crear una
   RQ con una sucursal sin departamentos devuelve **500** (debería ser 4xx) —
   validar el alta de sucursales en Administración antes de habilitar usuarios.
4. **Catálogo de motivos de rechazo** activo (se comparte con OC).
5. **Naturaleza del artículo** correcta en el catálogo (Crítico/Riesgo/
   Estándar/Servicio): gobierna qué nivel firma.
6. Setting `compras.settings.auto_generar_oc_al_autorizar` (default **false** =
   el comprador convierte manualmente).

## Eventos que publica / consume

| Dirección | Evento | Con quién | Efecto operativo |
|---|---|---|---|
| Publica | `SaldoNoSurtidoEvent` (informativo) | Compras/OC | Saldos no cubiertos con stock quedan disponibles para OC; no reabre autorización |
| Consume | `OrdenCompraCanceladaEvent` | Compras/OC | Libera RQs comprometidas en la OC cancelada |
| Consume | `OrdenCompraCerradaEvent` | Compras/OC | Marca el cubrimiento de las RQs de la OC |
| Interno | Reserva/liberación de stock al autorizar/cancelar | Almacén (puerto de lectura) | La bifurcación stock-aware es síncrona a la autorización |

## Monitoreo y troubleshooting

- **Reservas vencidas:** el TTL de reservas (default 14 días) las libera vía
  job; si un artículo aparece "sin disponible" sin razón, revisar reservas
  activas de RQs viejas.
- **RQ autorizada que no generó saldo de compra:** revisar el log de la
  bifurcación (`BIFURCACION_FALLO` en Application Insights) — la autorización
  falla completa si la bifurcación truena.
- **Workers**: el `OutboxPublisherWorker` de Compras publica los eventos; si las
  RQs no se liberan al cancelar OCs, revisar dead-letters de
  `compras-events`/`compras-subscription` en Service Bus.
- La RQ que nunca sale de `EnSurtido` suele deberse a que la OC asociada no
  cierra — ver "Monitoreo y troubleshooting" de
  [ordenes-compra-admin.md](ordenes-compra-admin.md).

## Límites conocidos vigentes

- **Sucursal sin departamentos → error 500** al crear RQ (pendiente convertir
  a 4xx).
- El cubrimiento final de RQs depende de `OrdenCompraCerradaEvent`; desde
  #613/#614 las OCs **cierran automáticamente** al completar
  Recepción+Facturación+Pago y la RQ se cubre sola. Si una RQ no sale de
  `EnSurtido`, revisar el cierre de la OC (ficha de OC).
