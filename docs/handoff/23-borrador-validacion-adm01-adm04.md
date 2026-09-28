# Borrador interno de validación · F1-ADM-01 a F1-ADM-04

Este documento acompaña la implementación local. No constituye aceptación de Vidrios Millet ni autoriza cargar datos fiscales productivos.

## Lo que se puede revisar ahora

| Módulo | Recorrido reproducible | Evidencia técnica local | Para cerrar con Millet |
|---|---|---|---|
| ADM-01 · empresas y sucursales | Alta, consulta, cambio y separación por empresa | Pruebas de integración de Administración y revisión de navegación local | Confirmar empresas y sucursales reales, sus responsables y sus claves externas. |
| ADM-02 · usuarios y permisos | Inicio de sesión simulado, asignación y revocación de rol, pantalla y API restringidas | Pruebas de identidad, autorización y auditoría | Probar Microsoft Entra con usuarios reales y obtener aprobación de la matriz de roles. |
| ADM-03 · auditoría | Consultar altas, cambios, accesos, cambios de empresa y detalle antes/después | Pruebas de API, partición posterior a octubre de 2026 y recorrido visible | Acordar conservación, acceso de auditoría y prueba del flujo real de Entra. |
| ADM-04 · catálogos compartidos | Monedas, unidades, formas de pago SAT de consulta e impuestos de referencia | Pruebas de catálogo, duplicados, vigencias, permisos e interfaz | Validar catálogos vigentes, tasas, fuente, vigencias y quién autoriza cambios. |

## Propuesta VILO para validar con Millet: referencias de impuestos

El ERP conservaría un catálogo compartido por **clave, tipo (traslado o retención), factor (tasa, cuota o exento), valor, inicio y fin de vigencia, estado y fuente**. La consulta de una operación ofrecería sólo registros activos y vigentes en su fecha. Para mantener trazabilidad, una clave que cambia de tasa se cerraría en una vigencia y se daría de alta en otra; se rechazarían vigencias superpuestas para la misma clave, tipo y factor. La creación o modificación requeriría el permiso de administración de catálogos compartidos y dejaría registro en la bitácora.

La propuesta se apoya en la necesidad documentada de ADM-04 de compartir catálogos entre procesos y en el comportamiento existente de formas de pago SAT y datos fiscales de empresa. **El catálogo no calcula ni sustituye todavía las reglas fiscales de facturación.** Se agregó una consulta opcional para elegir una referencia vigente de IVA al configurar la empresa; el valor manual anterior permanece disponible. No se precargaron tasas como datos aprobados de Millet.

Millet debe confirmar puntualmente:

1. La lista de impuestos que usa en ventas, compras, anticipos, devoluciones, notas de crédito y comercio exterior, con clave fiscal, tipo, factor, tasa o cuota, fechas de vigencia y ejemplos de CFDI.
2. Quién entrega la fuente oficial, quién propone una modificación, quién la aprueba y quién la captura; incluir el tratamiento de cambios SAT y correcciones retroactivas.
3. Si la selección del catálogo debe convertirse en regla obligatoria o automática en cada proceso fiscal. Hasta esa decisión, el cálculo fiscal actual sigue vigente.
4. La fecha de corte y el tratamiento de documentos históricos cuando una referencia se desactiva.

**Criterio de validación con Millet:** Fiscal y Contabilidad aprueban una tabla de referencias y ejecutan casos reales de factura, nota de crédito y cancelación; TI confirma el responsable de mantenimiento. Antes de ello, el estado es **Por confirmar**.

## Límites del cierre local

La implementación y las pruebas locales comprueban el código de esta rama. La bitácora de una empresa sólo devuelve eventos atribuidos a ella; los eventos sin empresa se reservan para una futura vista de seguridad transversal, cuya autorización deberá definirse aparte. Falta validar permisos de creación de particiones en la infraestructura de destino, ejecutar el flujo real de Entra y obtener UAT de Millet con datos oficiales. Esas validaciones no se sustituyen por el inicio de sesión simulado ni por registros de prueba.
