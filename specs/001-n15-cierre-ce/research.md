# Research · N-15

## D1 · Fuente de cantidades
Decision: extender IRecepcionesImportacionReadPort para consultar neto por línea mediante
RecibidoPorLineaOc.CalcularAsync, usando Compras sólo como metadatos de unidad/artículo.
Rationale: CE hoy reemplaza acumulados según entrega; una proyección eventual de Compras tampoco
es autoridad. El helper existente excluye entradas canceladas, resta devoluciones válidas,
considera AF anulados y convierte unidades/redondea4. No sumar metadatos de pedimento/guía.
Alternatives: max(cantidad) rompe disminuciones; timestamp/GUID tiene empates y exige migración;
retransmitir eventos o cambiar DB en QA oculta el defecto.

## D2 · Concurrencia y recuperación
Decision: candados CE antes de leer fuente; servicio común en listener y revisión/cierres normales.
Rationale: snapshot leído antes de esperar un candado puede quedar obsoleto. Mantener orden de
reparto y consumo previo de cerrados. Recuperación sin nuevo endpoint, sweeper ni evento ficticio.
Alcance explícito: no se añaden suscripciones de cancelación/devolución; el neto se vuelve a
comprobar en conciliación/cierre, por lo que una proyección puede esperar hasta ese paso.
No se guarda reparación cuando la acción completa devuelve422.

## D3 · Responsable
Decision: usar asociación/corporativo existentes O permiso efectivo
ComercioExteriorDocumentosGestionarTodasSucursales del responsable en la empresa correcta.
Rationale: ADR-0051 y ExpedienteServicio ya reconocen bypass del recurso; el adapter de
responsables no lo hace. Es una inconsistencia de validación, no prueba de que deba otorgarse acceso.
Alternatives: dar corporativo/sucursal a Eliam cambia su alcance; usar permiso del actor permite
responsables indebidos; modificar EstaAsociadoAsync altera todos los módulos. Se descartan.

## Fuentes y nivel de certeza
Investigación de dos agentes de sólo lectura, comprobada contra código en6ab88808b:
RecepcionCe.cs, ExpedienteRecepcion.cs, RecibidoPorLineaOc.cs, CatalogosCeReadAdapter.cs,
UsuarioSucursalReadAdapter.cs, PermissionLoader.cs y ADR-0051. La ejecución local y QA aún
están pendientes: este documento describe decisiones técnicas, no un pase verificado.
