# Requisiciones — Ficha de usuario

## Qué hace el módulo

Gestiona la solicitud interna de materiales y servicios: cualquier área captura
lo que necesita, el flujo de autorización lo aprueba según departamento, monto y
naturaleza del artículo, y al autorizarse el sistema decide solo qué se surte
del stock existente (reserva + salida de almacén) y qué se manda a comprar
(saldo de compra → OC). **No** genera órdenes de compra automáticamente por
default (el comprador convierte), no cotiza, no controla presupuesto y no
maneja caja chica.

## Quién lo usa

| Rol | Qué hace aquí |
|---|---|
| Solicitante / Requisitante | Crea y transmite requisiciones (puede crearse a nombre de otro con permiso de delegación) |
| Jefe de departamento (Autorizador N1) | Aprueba las RQs de su departamento |
| Jefe de Almacén | Cubre el nivel 1 de artículos de naturaleza Estándar; cierre manual de RQs no surtidas |
| Autorizador N2 (Dirección/Gerencia) | Aprueba cuando el monto rebasa el umbral del departamento o la naturaleza es crítica |
| Comprador | Convierte RQs autorizadas en OCs |

## Operaciones principales

| Operación | Pantalla | Pasos resumidos |
|---|---|---|
| Crear requisición | `/compras/requisiciones` → botón **"Nueva requisición"** | Capturar cabecera, agregar líneas con el form inline, **"Crear requisición"** → queda en Borrador |
| Transmitir a autorización | `/compras/requisiciones/$id` → **"Transmitir"** | Pasa a EnAutorizacion; ya no es editable |
| Autorizar / rechazar | `/compras/pendientes` ("Pendientes de autorización") | **"Aprobar Nivel 1"** → (si aplica) **"Aprobar Nivel 2"**; **"Rechazar"** exige motivo del catálogo |
| Convertir a OC | Detalle de RQ autorizada → **"Convertir a OC"** | Abre el Sheet "Nueva OC" 1:1 con el saldo de compra prellenado |
| Cancelar / eliminar / cerrar manual | Detalle → **"Cancelar"** / **"Eliminar"** / **"Cerrar"** | Eliminar solo pre-autorización; cancelar post-autorización libera reservas; cierre manual para RQs que ya no se surtirán |
| Historial de compras de un artículo | `/compras/articulos/$id/historial-compras` | Consulta de precios y proveedores anteriores |

## Flujos internos de control

- **Delegación de requisitante**: con el permiso de "seleccionar requisitante",
  un asistente captura la RQ a nombre de otro usuario; el requisitante queda
  como responsable del flujo.
- **Reservas de stock**: al autorizar, lo cubierto con stock queda **reservado**
  (no decrementa el físico; reduce el disponible). Las reservas caducan por TTL
  (default 14 días) o se liberan al cancelar/rechazar la RQ.
- **RQs de sistema (reabasto)**: el motor de reorden de Almacén genera
  borradores de RQ automáticamente cuando un artículo cae bajo su punto de
  reorden; siguen el flujo normal de autorización — ver
  [P8](../pipelines/p8-almacen-reabasto-cierre.md).
- **Administración de aprobadores**: la designación de JefeDpto / JefeAlmacen /
  AutorizadorN2 por departamento vive en `/compras/admin/aprobadores` — detalle
  en la [ficha de administración](requisiciones-admin.md).

## Ejemplos con folios reales

- **RQ `MID2026-000040`** — recorrió el ciclo completo hasta pago y REPP:
  ver guion [P1 — Flujo feliz insumos](../pipelines/p1-flujo-feliz-insumos.md).
- Las salidas `M-SAL2026-000004` (surtido contra RQ) y `M-SAL2026-000005`
  (vale urgente que se regulariza con una RQ posterior) muestran los dos caminos
  por los que una RQ termina en entrega de material:
  [P6 — Flujos internos de Almacén](../pipelines/p6-flujos-internos-almacen.md).

## Errores comunes (en lenguaje de negocio)

- **"La requisición debe tener al menos una línea para enviarse a autorización."**
  — se intentó transmitir vacía.
- **"Nivel2 requiere que primero exista Nivel1."** — el segundo aprobador se
  adelantó; primero firma el jefe del departamento.
- **"El artículo '{clave}' está Inactivo y no puede usarse en líneas nuevas."**
  — pedir a Datos Maestros reactivar o sustituir el artículo.
- **"El motivo '{clave}' requiere texto libre que describa la razón."** — al
  rechazar con motivo "OTRO" hay que escribir la explicación.
- **"La RQ ya está comprometida en la OC '{folio}'. No puede asignarse a otra."**
  — una RQ solo vive en una OC activa; si la OC se canceló en borrador, la RQ
  se libera sola.
- La pantalla no deja editar una RQ transmitida: es intencional — se rechaza y
  se corrige, o se cancela y se recaptura.
