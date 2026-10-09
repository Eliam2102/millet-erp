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
