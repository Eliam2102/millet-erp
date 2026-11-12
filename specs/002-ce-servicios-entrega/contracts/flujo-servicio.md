# Contract: flujo visible sin API paralela

## Operación normal
Usar los endpoints vigentes de Compras OC, CxP CapturarFacturaConOc y autorizar/liberar, Tesorería pagos/aplicaciones, CE gastos/cierres. Fuentes de rutas: Api/Endpoints/Compras y CuentasPorPagar/FacturasEndpoints.cs. No añadir endpoint universal para saltar N1/captura/N2.

| Paso | Precondición | Salida verificable | Rechazo a probar |
|---|---|---|---|
| Clasificar OCservicio | Actor/sucursal habilitados, partidas de servicio | IndicadorCE explícito y dos niveles exigidos | Material ordinario no entra al caso especial |
| N1CE | PermisoOC+facultadCEN1, actor distinto capturista según regla | Firma vigente, permite capturaCxP | Sin facultad/autoautorización |
| Capturar factura | N1vigente, proveedor/moneda/cantidades/CFDI válidos | Una deuda registrada, aún no liberada | Duplicada/incompleta/otraempresa/discrepancia |
| N2Comercial | Pasivo vigente, facultadCEN2, otra persona | Firma sobre operación/documento vigentes | Sin pasivo/sin facultad/misma persona |
| LiberarCxP | N2vigente y factura correspondiente | Elegible/programable enTesorería | Sin N2 o firma anterior a sustitución no verificada |
| Pagar/aplicar | Pasivo habilitado, banco/moneda/saldo válidos | Movimiento y aplicación al mismo origen | Reintento sin duplicado, exceso/reversa |
| CerrarIMPO | Datos/documentos/mercancía/demáscierres y servicios saldados | Fecha/actor yGerencia final | Impago/parcial/reversa/proyección/facturaausente |

## HTTP y UI
Conservar If-Match, idempotencia yProblemDetails. Error de pagos usa el contrato local CE_SERVICIOS_PENDIENTES con referencia/motivo. Acciones de UI vigentes: indicadorOCservicio, etiquetasN1/N2, Enviar aTesorería y cierresCE. No basta ocultar botón: directoHTTP debe rechazar. Bandeja/avisos reutilizan helper `Compras/Application/Listar/PendientesAutorizacionLectura.cs` para que visibilidad y facultades coincidan.

## Límites de lectura
Los puertos son del dueño del dato: Compras consulta existencia/vigencia de factura víaadapterApi; CE consulta saldo víaCxP; CxP contrasta pagos conTesorería. No SQL de escritura cruzada. EXPO no hereda reglaIMPO de serviciospagados. Ante falta de lectura no declarar saldo0.
