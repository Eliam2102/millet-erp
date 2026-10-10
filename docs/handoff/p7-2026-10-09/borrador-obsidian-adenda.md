# Borrador de actualización · P7 · adenda 09-oct-2026

No aplicado a Obsidian: la bóveda está fuera de las raíces de escritura de esta sesión. Conservar el informe anterior como histórico.

Destino: fichas F1-COM-03/04/12, F1-ALM-07/10, ADM-04/08; Bitácora y estado de construcción de los módulos 03/04/01.

- Base local comprobada: P7 guardado por Claude en `893f096`; main incorporado por `cb76942`. Sin modificaciones pendientes al comenzar esta continuación.
- Se corrigió la validación de periodo antes de conversión en ambas recepciones; el ciclo de vida de conexiones EF/Npgsql al apartar; la conversión que cambiaba capitalización del código de unidad; y la preparación de OC sin motivo para probar la guarda del handler sin violar el CHECK.
- Se mantuvo y reforzó el caso de rollback persistido en un scope independiente. Nuevas pruebas locales detectaron los errores en rojo y pasaron tras restaurar las correcciones.
- Build: 0 errores/advertencias. Unitarias: 1,780 aprobadas. Frontend: tipos verdes, lint 0 errores/10 advertencias, Vitest 358 archivos/2,072 pruebas verdes.
- Gate PostgreSQL: **Por confirmar**; este sandbox deniega el socket de Docker. Las siete pruebas fallidas del reporte de Claude no se ejecutaron aquí. Siguiente acción: Claude corre el gate completo y rojo/verde PostgreSQL.
- ADM-08 continúa pendiente por conflicto de regla con ADR-0050, no por ausencia de datos. Se solicitó resolver herencia departamento/máquina opcional frente a Dim3 obligatoria independiente.
- Correo V08, equivalencia real V49, confirmación D3 y aceptación Millet siguen **Por confirmar**. Sin envíos, tareas externas, despliegues, commit ni push.

Fuente: `millet_erp-P7-RES/docs/handoff/p7-2026-10-09/REVISION-ADENDA.md` y evidencias asociadas. Registrar esta verificación como construcción/prueba local; no como aceptación ni integración PostgreSQL aprobada.
