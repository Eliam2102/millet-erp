# ADR-0061 · P7: apartado de RQ, reorden y conversión de unidades

Fecha: 09-oct-2026. Estado: aceptado para construcción por Eliam (D3, D4, D6, D11, D16). Confirmación con Millet: **Por confirmar**, sesión del 12-oct.

## Contexto y autoridad

Módulo 03, Matriz P1 y CA3.4: al autorizar una RQ, la existencia se aparta para su salida y sólo el faltante se compra. La decisión D3 de Eliam reemplaza la decisión de ADR-0047 de no reservar al autorizar. Permanecen la jerarquía física de almacenes, los bins y la actualización de saldos por trigger de aquel ADR.

## Decisión

- Almacén conserva apartados por línea de RQ y sucursal, expresados en la unidad del artículo. La disponibilidad de la sucursal descuenta los apartados pendientes. No se decrementa el físico hasta la salida.
- La autorización comparte transacción PostgreSQL con Almacén. Un bloqueo transaccional por sucursal/artículo serializa autorización y salidas. Un fallo revierte autorización, apartado y outbox.
- Las salidas sólo consumen existencia libre más los apartados de su RQ; una salida por vale no consume apartados ajenos. Cancelar, cerrar manualmente o cerrar automáticamente por entrega total libera lo pendiente. Surtir consume el apartado de la línea.
- `ComprasSettings.ApartarExistenciaAlAutorizar` se activa por defecto. Su cambio afecta autorizaciones futuras. Puede apagarse desde Administración → Compras → configuración genérica; no elimina apartados existentes. No se siembra `ParametroGlobal` ni se ocupan sus IDs.
- D4: cotización obligatoria para toda OC. `CotizacionExcepcionada` se conserva únicamente como dato histórico compatible con clientes anteriores; no permite omitir la cotización.
- D6: evaluar existencia física + pedido vivo; disparar sólo cuando sea menor o igual al punto de reorden. Reponer hasta máximo o cantidad fija positiva. La demanda manual sin almacén explícito se cuenta una vez por sucursal; a nivel almacén se toma como cobertura conservadora de esa sucursal, sin inventar un destino.
- D16 / P154: recepción y salida convierten con la razón de `FactorABase`, verifican dimensión y decimales, conservan cantidad/unidad capturada, y registran la cantidad en la unidad de inventario del artículo. Los eventos a Compras conservan la unidad de la línea de OC/RQ. El catálogo de equivalencias debe validarse con Millet; no se adivinan factores.
- D11: obra opcional en texto (no se encontró catálogo de obras en el maestro), RQ → OC → pasivo. No consolidar RQ con obras distintas en una sola cabecera; corregir la selección antes de continuar.

## Alcance pendiente

ADM-08 no se modifica mientras se concilia el conflicto con ADR-0050, que exige Dim3/máquina por línea. V49 y V08 continúan **Por confirmar**. El aviso de vale se presenta en Inicio de Almacén; el correo requiere remitente y un servicio de envío configurado.

## Validación

Ver el informe local de P7 para las pruebas ejecutadas y las pendientes. Las pruebas PostgreSQL deben correr en aislamiento mediante `tools/validate-integration-isolated.sh`, incluyendo rojo/verde. No equivale a aceptación de Millet ni despliegue.

## Adenda 22-oct-2026 · Liberar apartado y surtido completo (86e3tferc)

Origen: sesión con Millet del 19-oct-2026 (Jorge, 01:08:04–01:11:28). Confirmación con Millet: **por validar**.

**Surtido parcial o completo.** La requisición lleva el indicador `SoloApartarSiCompleto`, que elige quien la solicita; por omisión es surtido parcial. Queda congelado al transmitir la requisición, igual que `Obra`. Se decide por línea: una línea con el indicador activo solo aparta si la existencia libre cubre la cantidad pendiente completa; si no, no aparta nada de esa línea. La regla se aplica de la misma forma en la división estimada (matriz, bandeja y vista previa) y de forma atómica al apartar en la autorización. El parámetro global `ApartarExistenciaAlAutorizar` sigue prevaleciendo: si está apagado no se aparta nada, sin importar el indicador.

**Liberar apartado.** Se puede liberar el apartado por línea y por cantidad, con el permiso `compras.requisiciones.liberar-apartado` y un motivo obligatorio. La operación corre dentro de la transacción compartida con Almacén, bajo el bloqueo de saldo de OC de Compras y el bloqueo asesor por sucursal/artículo de Almacén, y nunca libera más de lo pendiente. Quien libera responde siempre si lo liberado se manda a compra (decisión del owner, 22-oct):
- **Sí:** la cobertura se recalcula; la cantidad pasa de almacén a saldo de compra y la ve el comprador al generar la OC. Si la requisición ya está en una OC, no se permite (`RQ_COMPROMETIDA_EN_OC`): solo se libera sin compra o se captura otra requisición.
- **No:** la cantidad sale de almacén y queda como `cant_sin_compra` de la línea. Ya no cuenta para el cierre por entrega; si con eso no falta nada por entregar, la requisición se cierra en la misma transacción.

No emite evento de integración nuevo; el cierre usa el `RequisicionCerradaEvent` existente. La liberación queda en el histórico de la requisición (tipo `ApartadoLiberado`: quién, cuándo, cantidad, destino y motivo). El permiso se siembra sin asignarlo a ningún rol de sistema; se asigna en Administración a los roles de Compras.

**Superficie técnica.**
- `GET /api/v1/compras/requisiciones/{id}/apartados` consulta el apartado por línea.
- `POST /api/v1/compras/requisiciones/{id}/lineas/{lineaId}/liberar-apartado` libera cantidad con motivo.
- Almacén registra la liberación en las columnas `cantidad_liberada` y `motivo_liberacion` del apartado; Compras, en `cant_liberada_apartado`, `cant_sin_compra` y `motivo_ultima_liberacion` de la línea.
- `PATCH /api/v1/compras/requisiciones/{id}` edita el surtido junto con el resto de la cabecera; en pantalla se edita desde el detalle mientras la requisición está en Borrador.
- La pantalla de detalle de la requisición muestra el apartado por línea y, con el permiso, la acción de liberar.
