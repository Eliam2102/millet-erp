# Borrador de PR · ADM-03 y ADM-04 sobre ADM-01/02

## Problema y resultado

La base integrada de empresas, usuarios y permisos aún no registraba todos los accesos ni podía garantizar escrituras de auditoría después de octubre de 2026. El catálogo compartido de ADM-04 tampoco tenía referencias de impuestos. Este cambio registra accesos y cambios de empresa, prepara particiones futuras, muestra los detalles de auditoría y agrega referencias fiscales con vigencia, protección por permisos y consulta desde la configuración de empresa.

## Cambio verificable

- ADM-01/02: regresión de organización, identidad y catálogos sobre dos empresas y distintos permisos.
- ADM-03: accesos permitidos y denegados sin conservar tokens ni contraseñas; auditoría con usuario, empresa, fecha local, entidad, acción y valores anteriores/nuevos; particiones mensuales automáticas.
- ADM-04: referencias de impuestos con clave, tipo, factor, valor, vigencia, estado y fuente; sin tasas reales precargadas. Formas de pago SAT conservan mantenimiento controlado y consulta. Monedas y unidades continúan desde los catálogos existentes.
- Propuesta de validación funcional en `docs/handoff/23-borrador-validacion-adm01-adm04.md`.

## Verificación local

- 285 pruebas de integración de Administración, Identidad y Catálogos: aprobadas.
- 383 pruebas unitarias de los cuatro proyectos pertinentes: aprobadas.
- 6 pruebas de interfaz de impuestos y auditoría: aprobadas.
- Compilación del frontend y análisis estático de archivos cambiados: aprobados.
- Recorrido visual local: inicio de sesión simulado, navegación a impuestos, formulario de alta, consulta de auditoría y detalle de acceso.
- Migración aplicada en una base local aislada y escritura de auditoría posterior a octubre de 2026 comprobada.

## Límites que debe ver el revisor

- La prueba real de Microsoft Entra, los catálogos vigentes, los valores fiscales y la UAT de Millet siguen **Por confirmar**.
- El servicio requiere permiso `CREATE` sobre el esquema `core` para preparar particiones futuras en la infraestructura de destino; verificarlo antes de desplegar.
- La referencia fiscal todavía no sustituye el cálculo fiscal existente. La opción en datos de empresa es consultiva y mantiene la captura anterior.
