# Auditoría técnica · 35 funcionalidades marcadas como existentes

## Lectura correcta

Esta auditoría contrasta la afirmación de la matriz con el repositorio en el commit `aac30b25651ccdee641299bfd20aadb03a0d7a00`. La solución completa compila y sus pruebas automatizadas pasan, pero eso no acredita por sí solo cada caso funcional ni la aceptación de Millet.

Estados usados:

- **Verificable local:** se localizaron pantalla/ruta, backend y pruebas relacionadas; todavía requiere ejecutar el caso con evidencia y UAT.
- **Parcial o condicionado:** existe una parte reutilizable, simulada o dependiente de un servicio/dato externo; la afirmación “ya existe y funciona” es demasiado fuerte.
- **No localizado:** no se encontró implementación dedicada suficiente para sostener el estado de la matriz.

## Resultado ejecutivo

- Verificables localmente: **23**.
- Parciales, reutilizados o condicionados: **10**.
- No localizados: **2**.
- Aceptados en UAT: **0**.

Por lo tanto, las 35 deben conservar resultado operativo `Pendiente`. La matriz no debe usarse para prometer que todas están listas sin trabajo.

## Evidencia por funcionalidad

| ID | Estado comprobado | Evidencia principal | Qué falta para cerrar |
|---|---|---|---|
| F1-ADM-01 | Verificable local | `frontend/src/routes/_app/admin/empresas/`, componentes de empresas/sucursales/departamentos/puestos, `EmpresasEndpoints.cs`, `DepartamentosEndpoints.cs`, `PuestosEndpoints.cs`, pruebas de dominio de Administración | Caso de dos empresas, evidencia de no mezcla y UAT de Dirección/Administración |
| F1-ADM-03 | Verificable local | `frontend/src/routes/_app/admin/auditoria/index.tsx`, `AuditoriaEndpoints.cs`, `AuditoriaPage.smoke.test.tsx` | Ejecutar alta/cambio/autorización/acceso y comprobar antes/después, usuario y fecha |
| F1-ADM-10 | Verificable local | `Sidebar.tsx`, `SidebarNav.tsx`, `AppLauncherModal.tsx`, `nav.test.ts`, `permission-codes.test.ts`; menú comprobado visualmente después del login local | Matriz final de permisos y pruebas con roles reales/negativas |
| F1-ADM-11 | Parcial o condicionado | `AdjuntosManager.tsx` y pruebas; almacenamientos específicos en Compras, Almacén, CxP y A+W | No se localizó un servicio universal que pruebe metadatos y permisos para todos los registros; definir cobertura por módulo |
| F1-COM-01 | Verificable local | rutas de requisiciones, `RequisicionesEndpoints.cs`, `ObtenerRequisicionPorIdHandlerTests.cs`, pruebas de cobertura y endpoints | Recorrido con requisición real/simulada, autorizaciones y saldo por comprar |
| F1-COM-02 | No localizado | Sólo se localizaron adjuntos de tipo cotización en OC y consultas de cotización A+W; no una entidad/pantalla para registrar y comparar cotizaciones de proveedores | Diseñar o identificar el módulo real; corregir estado y estimación antes de asignar |
| F1-COM-03 | Verificable local | `useCrearOrdenCompraDesdeRequisicion.ts`, `OrdenCompraDesdeRqTests.cs`, compromiso/cubrimiento y endpoints de OC | Caso de saldo suficiente y rechazo por excedente con evidencia |
| F1-COM-04 | Verificable local | `useCrearOrdenCompraVacia.ts`, `CrearOrdenCompraVacia*`, autorización y evidencias de excepción | Caso sin requisición con motivo, autorizador y negativa de liberación incompleta |
| F1-COM-05 | Verificable local | `OrdenCompraAutorizarTests.cs`, `TwoLevelAuthEndpointsTests.cs`, `DobleFirmaDialog.tsx` | Ejecutar montos con y sin segunda firma usando matriz vigente de Millet |
| F1-COM-10 | Parcial o condicionado | `QuestPdfOrdenCompraGeneratorTests.cs`, `OrdenCompraPdf`, `TabPdf.tsx` | Se comprobó generación/consulta de PDF; no se localizó entrega real por correo al proveedor ni acuse de envío |
| F1-ALM-01 | Verificable local | rutas de almacenes/ubicaciones/asignaciones, `AlmacenCatalogoEndpoints.cs`, pruebas de agregados y asignación | Ejecutar catálogos vigentes e intento con ubicación/artículo inactivo |
| F1-ALM-02 | Verificable local | rutas de recepciones, `RecepcionesEndpoints.cs`, `RegistrarRecepcionValidatorTests.cs`, listener de factura proveedor | Recorrido OC–recepción–factura con datos representativos |
| F1-ALM-03 | Verificable local | `PackingListUpload.tsx`, `RegistrarRecepcionPackingListValidatorTests.cs`, `CfdiRecibidoIngresadoHandlerTests.cs` | Probar asociación posterior de factura sin duplicar inventario |
| F1-ALM-06 | Verificable local | rutas de salidas, `SalidasEndpoints.cs`, `RegistrarSalidaConRequisicionCcTests.cs` | Caso nominal y rechazo por saldo autorizado insuficiente |
| F1-ALM-08 | Verificable local | rutas de devoluciones, endpoints internos/proveedor y pruebas de devolución | Probar reversa de existencia y trazabilidad al documento origen |
| F1-ALM-09 | Verificable local | rutas de inventarios/captura/aprobación, `ConteosEndpoints.cs`, `AprobacionEndpoints.cs`, `ConteoInventarioTests.cs` | Conteo con diferencia, autorización y movimiento de ajuste |
| F1-ALM-10 | Verificable local | ruta `frontend/src/routes/_app/almacen/reorden.tsx`, `AlmacenReordenEndpoints.cs`, `ConfiguracionReordenAggregateTests.cs`, `ReordenWorkerToggleTests.cs` | Parámetros reales, consumo representativo y revisión del cálculo con Almacén |
| F1-MP-04 | Parcial o condicionado | Reutiliza recepción/packing list de Almacén | No se probó una clasificación específica de materia prima ni su contrato A+W; confirmar que la reutilización cubre reglas de material directo |
| F1-CXP-03 | Verificable local | `CapturarFacturaSheet.tsx`, `CapturarSinOcTests.cs`, autorización de factura y evidencias | Caso de servicio/gasto sin OC, clasificación, autorización y generación del pasivo |
| F1-CXP-04 | Verificable local | `NuevaComprobacionAduanalesSheet.tsx`, `ComprobacionGastosAduanalesTests.cs`, comandos de comprobación aduanal | Ligar expediente real y validar distribución contra la regla de Comercio Exterior |
| F1-CXP-06 | No localizado | Se localizaron reportes de vencimiento y evento de pasivo hacia Tesorería, pero no una propuesta de pagos seleccionable en CxP | Definir si pertenece a Tesorería o falta construir; corregir estado y dueño |
| F1-CXP-07 | Verificable local | páginas/endpoints de anticipos y notas de crédito, `AplicarNcYAnticipoTests.cs`, agregados correspondientes | Ejecutar aplicación parcial/total, compensación y rechazo de saldo negativo |
| F1-FAC-02 | Parcial o condicionado | emisión CFDI, worker de timbrado, adapter real FiscalAPI, pruebas de `CfdiEmisionBuilder` y pantallas de factura | Requiere sandbox/credenciales/certificados de Millet; no se ejecutó timbrado real ni se obtuvo UUID/XML/PDF externo |
| F1-FAC-04 | Verificable local | `NcRanuraTests.cs`, `NcRanuraEmisor.cs`, campos de ranura en pedido y migración | Caso funcional acordado con pedido A+W y evidencia de cuándo aplica/no aplica |
| F1-FAC-05 | Verificable local | dominio de Caja/Cobro, regla MXN, formas SAT y pruebas de `CobrosMostradorTests.cs` | Ejecutar contado nominal y rechazos de moneda/forma inválidas en UI |
| F1-FAC-06 | Parcial o condicionado | dominio y emisión de REPP, `ReppTests.cs`, pantallas REPP y evento desde pago confirmado | El cálculo local está cubierto; timbrado/UUID real depende de PAC y datos fiscales |
| F1-FAC-07 | Parcial o condicionado | dominio `ComplementoCce`, `CceTests.cs`, selector de Incoterm y configuración CFDI | Falta prueba real con datos de Comercio Exterior, catálogos, certificado y PAC |
| F1-CXC-01 | Verificable local | cartera/estado de cuenta, `AntiguedadSaldosTests.cs`, `EstadoCuentaClienteTests.cs`, `CarteraPage.smoke.test.tsx` | Conciliar el detalle con saldo real de un cliente de Millet |
| F1-CXC-02 | Parcial o condicionado | Cobro de mostrador en Facturación, eventos hacia cartera y modelos de aplicación | No se comprobó un recorrido único cobro–aplicación–remanente desde CxC con integración real |
| F1-CXC-03 | Parcial o condicionado | `PropuestaAplicacionPago`, pruebas de propuesta y depósitos/confirmación en Tesorería | Falta demostrar prioridad completa y conservación operativa del depósito no identificado de extremo a extremo |
| F1-CXC-05 | Parcial o condicionado | consulta de anticipos, eventos de nota de crédito y pantallas de aplicaciones | No se localizó evidencia suficiente de un flujo unificado de anticipos, NC y compensaciones con todos los límites del criterio |
| F1-TES-01 | Verificable local | cuentas bancarias, `CuentasEndpoints.cs`, `CuentaBancariaTests.cs`, pantalla de cuentas | Caso de dos empresas y rechazo de cuenta ajena/inactiva |
| F1-TES-02 | Verificable local | movimientos/depósitos/transferencias, endpoints y `MovimientoBancarioTests.cs` | Ejecutar afectación de saldo y referencia con datos de prueba controlados |
| F1-TES-03 | Verificable local | `PagosEndpoints.cs`, `PagosCommands.cs`, `PagoProveedorDomainTests.cs`, pantalla de pagos | Pago parcial/total contra pasivos elegibles y evento de confirmación |
| F1-TES-08 | Parcial o condicionado | `RegistrarReppRecibidoCommand`, `ReppRecibidoTests.cs`, bandeja de REPP pendientes | El código registra un REPP emitido por el proveedor; la matriz dice “emitir REPP de proveedor”. Corregir la redacción/alcance antes de cerrar |

## Correcciones que deben reflejarse en planeación

1. `F1-COM-02` y `F1-CXP-06` no deben conservar “Ya existe y funciona” sin localizar una implementación adicional.
2. `F1-TES-08` necesita corregir el verbo y la responsabilidad fiscal: Millet registra/valida el REPP recibido del proveedor; no lo emite por cuenta del proveedor.
3. `F1-COM-10` debe separar generación de PDF de envío/acuse real por correo.
4. `F1-ADM-11` debe acotarse por módulos que realmente implementan adjuntos.
5. Las funciones fiscales sólo pueden quedar “listas para UAT” después de una prueba real contra el sandbox/servicio autorizado del PAC.
6. Los flujos cruzados CxC–Facturación–Tesorería requieren casos integrales; pruebas unitarias por módulo no bastan.

## Próximo paso operativo

Ejecutar `17-plan-validacion-35-existentes.md`, que asigna responsable, recorrido y bloqueo de cierre para cada ID. Los hallazgos deben reflejarse en cada ficha y tarea: `Lista para comenzar`, `Puede comenzar con datos ficticios`, `Bloqueada por Millet` o `Requiere decisión interna`. Ninguna debe pasar directamente a `Aceptada`.
