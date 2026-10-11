# Borrador local para la base de conocimiento de Millet

Fecha: 11 de octubre de 2026. Fuente: ficha PRU-B y decisiones de Eliam en esta conversación; código y pruebas del worktree `millet_erp-PRU-B` en la rama `fix/PRU-B-observaciones-prueba-uziel`. Resumen y archivos: `PRU-B-observaciones-uziel-10oct.md`.

Pendiente aplicar a la bóveda: este sandbox no permite escribir fuera del worktree. No se consultaron de nuevo los estados externos de ClickUp/GitHub ni se afirma publicación, merge o aceptación.

## Hechos para incorporar conservando el historial

- **Identidad / asignación de roles:** el modelo actual permite varios roles por usuario y empresa. La frase de rol único en CLAUDE.md debe quedar relacionada como documentación desactualizada, sin promoverla a una regla vigente. La ficha de Eliam autoriza conservar la cardinalidad del dominio.
- **Cuentas por pagar / notas de cargo:** implementada localmente la segregación de quien captura frente a quien autoriza y el botón en detalle. No promover a integración ni aceptación: unitarias y HTTP pendientes de ejecución.
- **Cuentas por cobrar / autorizaciones de crédito:** selector y validación de beneficiarios basados en permiso efectivo de liberación de Crédito y Cobranza. Conserva vigencia, un solo uso y prohibición de autoconsumo existentes.
- **Datos maestros / proveedores:** acción local Activo → En revisión, con motivo y permiso de validación de Cuentas por pagar. La bitácora guarda el motivo; los handlers actuales de Compras rechazan el proveedor en revisión.
- **Revisión por área:** retirado el stub de identificadores; nuevo adapter a departamentos activos reales con filtro de empresa. Pruebas escritas y compiladas; ejecución pendiente.
- **Departamento y centro de costo:** detectada asignación DEMO por posición. Se corrige solo el seed identificable; se preservan equivalencias mantenidas. Sin coincidencia única del catálogo queda Por confirmar, con Contabilidad como responsable de validar la equivalencia real.
- **Facturación:** adapter envía el folio ERP al PAC; convierte fecha de timbrado local a UTC y UI muestra México. No hubo timbrado real ni migración de fechas anteriores. Los folios consumidos permanecen; el handler actual de reintento usa el folio guardado. PRU-A debe comprobarlo en su integración.
- **Datos DEMO:** reglas fiscales de vidrio en M2 con MTK y puestos faltantes por sucursal/departamento. Configuración/seed preparados; no se ejecutaron contra una base en operación.

## Registro de validación para Bitácora / cobertura

- Backend: solución y proyectos de pruebas compilados, 0 errores y 0 advertencias.
- Frontend: tipos de producción y pruebas correctos; lint 0 errores, 10 advertencias existentes; 381 archivos y 2,158 pruebas de Vitest en verde.
- Backend unitario: VSTest anulado al intentar abrir socket local, `SocketException (13): Permission denied`. No registrar como verde ni como falla funcional.
- Integración: pruebas HTTP y seed escritas/compiladas; PostgreSQL aislado, rojo/verde y ejecución completa por Claude **Por confirmar**.
- No commit, push, despliegue, login simulado ni aceptación por Millet. PRU-A se mantiene como paquete separado.

Siguiente acción: Claude ejecuta unitarias y `tools/validate-integration-isolated.sh`; después Millet repite la prueba mediante autenticación real y compara folio/hora contra el XML del PAC. Actualizar cobertura únicamente con los resultados que se verifiquen.
