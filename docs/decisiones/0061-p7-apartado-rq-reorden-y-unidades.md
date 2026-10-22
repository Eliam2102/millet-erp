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

## Adenda · 22-oct-2026 · Liberación autorizada y modo de surtido

Solicitud de Millet de la sesión del 19-oct, según el alcance recibido para
86e3tferc. La requisición guarda `SoloApartarSiCompleto` al crearse: por omisión
`false` (surtido parcial, incluida la información histórica). Con `true`, cada
partida se aparta completa o en cero; no exige disponibilidad de toda la RQ.
Las partidas del mismo artículo consumen disponibilidad sucesivamente; una
partida que no alcanza no retiene stock que pueda cubrir otra. Matriz, bandeja,
vista previa y autorización comparten esa regla. Servicios y activos fijos
conservan su tratamiento. El parámetro global de apartado sigue vigente.

El permiso `compras.requisiciones.liberar-apartado` permite liberar una cantidad
positiva de una partida de una RQ autorizada o en surtido, hasta su apartado
pendiente, con motivo obligatorio (máximo 500 caracteres). El endpoint valida
además empresa y alcance de sucursal; sin permiso responde 403. El catálogo de
permisos incorpora la acción, sin concederla automáticamente a roles operativos.

La UI y el comando expresan la cantidad en la unidad de la partida; el puerto de
conversión la lleva a inventario. Almacén guarda cada liberación en
`liberaciones_apartado`, con cantidad en unidad de inventario, RQ, partida,
usuario autenticado, fecha UTC del servidor y motivo. La entidad es auditable.
La reducción y su registro se guardan juntos, dentro de la transacción compartida
y el bloqueo por sucursal/artículo del ADR original. Se exige Idempotency-Key.
La cantidad no puede superar el pendiente al ejecutar, incluso si la pantalla
estaba desactualizada. La existencia física no cambia: disminuye el apartado y
la disponibilidad aumenta inmediatamente para otra RQ.

Liberar no recalcula el cubrimiento original ni crea compras u órdenes nuevas;
la sección «Existencia apartada» consulta el pendiente real. Tampoco cancela ni
cierra la RQ. No cambia condiciones de proveedor, cotizaciones, OC ni CxP.
Las migraciones son aditivas; aplicar y validar en PostgreSQL corresponde al
revisor. Validación local y límites: `CODEX_REPORTE.md`.
