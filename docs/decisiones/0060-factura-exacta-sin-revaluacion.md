# 0060 · Factura exacta por línea, sin revaluación por precio

Fecha: 9-oct-2026. Decisión de negocio: D18/D19 de Eliam (ficha P3), con fuente Millet 8-oct.

La conciliación contra el total de la OC rechazaba entregas parciales y confundía diferencias fiscales con diferencias de precio. El evento de diferencia de precio emitía una valoración contable en Almacén; el código anterior no creaba un movimiento físico de ajuste.

Se concilian cantidades pendientes y precio por línea. Se acumula el valor absoluto de las diferencias para impedir compensaciones entre líneas. Se conserva la lectura de tolerancia por proveedor del handler (G1.13 no se modifica). La base considera descuentos pactados; impuestos y retenciones se verifican contra el XML ligado, sin imponer el IVA de la OC. El rechazo cancela el pasivo, avisa a Compras y deja el CFDI por procesar. La factura cancelada no se puede autorizar.

Las NC adjuntas se leen del almacenamiento fiscal: egreso del mismo proveedor y moneda, relación 01 a la factura. Su base se asigna explícitamente a líneas de OC y su total se aplica al pasivo en el mismo SaveChanges. Un rechazo no consume ninguno de los CFDI. La cantidad facturada se mantiene: una NC de precio no devuelve mercancía ni libera cantidad de OC.

CxP deja de publicar `cuentas_por_pagar.factura.diferencia-precio-detectada.v1`. Almacén conserva el consumidor para descartar y marcar como procesados eventos antiguos sin emitir `EntradaInventarioValoradaIntegrationEvent`, sin movimientos y sin modificar costos. Se conserva todo el historial. Esta decisión sustituye la regla anterior de variación de precio en CLAUDE.md y los levantamientos de la tríada.

G1.4 asigna las recepciones a las facturas vivas por fecha de creación del sistema (UUID como desempate), por línea, evitando habilitar dos veces la misma mercancía. Elegible base = mín(facturado, recibido disponible) × precio pactado. **Supuesto de implementación por confirmar:** el neto fiscal (después de NC) se prorratea según la base recibida; anticipos y pagos reducen lo disponible. El último tramo recibido libera el redondeo. El supuesto funcional V20 sigue pendiente de validación de Laura el 16-oct.

El detalle y la comprobación previa al pago consultan las cantidades de Compras, autoridad que también descuenta devoluciones. El consumidor de recepción usa además su proyección local para publicar el elegible sin depender del orden de consumo entre módulos. Durante ese desfase, la comprobación de pago puede retener temporalmente más hasta que Compras procese el evento; nunca se habilita pago por una proyección local antigua. La bandeja sigue recibiendo `pasivo.autorizado-para-pago.v1`, con `SaldoPendiente` limitado al elegible. NC aplicadas vuelven a publicar el elegible, sin repetir la autorización contable.

Tesorería comprueba el límite acumulado vigente por un puerto de lectura (adaptador en API → query CQRS de CxP) y descuenta sus pagos firmes, incluidos los aún no proyectados en CxP. El pago que excede el elegible se rechaza con el máximo permitido, sin cambiar silenciosamente el monto de una transferencia. Capturas de la misma OC se serializan mediante advisory lock transaccional; los pagos manuales también se serializan. Todos los eventos se publican antes de SaveChanges (ADR-0009).

No se requieren migraciones: se reutilizan facturas, líneas, notas de crédito, CFDI y proyecciones existentes. No se cambian flujos de P1/P2, corridas T9.1, parámetros G1.13 ni pantallas de aplicación posterior de P4.
