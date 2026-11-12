# Research: evaluación por versión y reutilización

## Cortes comprobados
- develop remoto954a52c7a72e63183c3565437e2ae609383be307, fetch11-nov; checkout aislado de ese SHA.
- QA API61388a15a6c26070d7cadb050ffa5c567d8928dc; portal2e577b384481d73409090300e31201d12b708819/apiBase61388. Consultas HTTP de metadatos, no nuevo recorrido funcional.
- Paquete local `millet_erp-COMEX-AF-listo`, baseb847c377e más74archivos no comprometidos. Manifiesto74/74 comprobado por SHA256; parche4816c5919f244aba8c220625e98abbfc7699adabcd9b368ac10f828a28311477 coincide. No confundir base con SHA de su código nuevo.
- Evidencia fuera del repo: `/Users/eliamcv/Documents/Vidrios millet/outputs/Evaluacion_CE_servicios_2026-11-11/` y paquete `outputs/COMEX_AF_listo_pruebas/`.

## Decisiones
1. **Reutilizar paquete local**, no crear tercera implementación. CE-02 agrega indicador, permisos por nivel, N1 habilita captura, N2 exige pasivo, factura completa única y liberación sin comprobación duplicada. CE-03 consulta saldo y pagos vigentes y revalida cierre. Alternativa descartada: modificar de cero handlers de develop.
2. **Integrar sobre develop fresco con combinación de tres vías.** Comprobación `git apply --cached --3way --check` en índice temporal devolvió0; no se aplicó al checkout. Hay8archivos solapados con cambios recientes: Program, listado pendientes OC, snapshotsCompras/CxP/Identidad, permisosbackend/frontend y bicep. La aplicación limpia no prueba corrección semántica: conservar lectura accionable y nuevas entidades/migraciones. No copiar snapshots antiguos completos.
3. **Pruebas por interfaces antes de declarar listo.** FirmasServiciosHttpTests crea el pasivo directamente entre firmas; CierreServiciosHttpTests prepara factura y usa notas de crédito para reducir saldo. Cubren piezas, pero no toda la cadenaN1→capturaHTTP→N2→liberación→pagoTesorería→cierre. Añadir ensayo completo sin reemplazar acciones de negocio por escrituras de preparación.
4. **Vigencia de autorización como riesgo a comprobar.** El adapter actual verifica existencia de facturaCE no cancelada; no identifica explícitamente facturaId/version enN2. Tras cancelación puede haber recaptura. Probar que no se libere una factura nueva sólo por firmas anteriores. No se declara defecto reproducido todavía; usar guardas y auditoría existentes para la solución mínima si falla.
5. **No pedir otra vez regla de servicios.** CorreoLaura y confirmaciones ya permiten construir. Personas/roles y cuentas definitivas son configuración con responsable. MatrizM151 y aceptación se precisan, no bloquean todoCE.
6. **Conservar amplitud real de CE fuera del parche:** Q-CE/PAC real; originalesEXPO/IN/A1/Aéreo/AF; anticipo/póliza/cuentas; OTR ya tiene otra línea de trabajo y evidencia. Referenciar tareas existentes y no revertir avances de otros chats.

## Dictamen
Desarrollado parcialmente en paquete local, no entregado como circuito listo en develop/QA. Dos brechas funcionales en el código integrado, correcciones locales reutilizables, dos huecos de demostración del flujo y riesgo de vigenciaN2 por probar. No es falta de respuesta Millet. La junta conserva utilidad como validación operativa del caso y configuración/aceptación, no para repetir levantamiento ni cubrir errores propios.

## Evidencia anterior que no se amplía
Paquete reporta4,475unitarias/19omisiones preexistentes,181casosAPI consolidados entre corridas y461UI. Es histórico de esa composición; no nueva corrida en954a52 ni CI remoto. N-15 cerró su muestraQA específica; no certificó estos servicios. Prueba de compatibilidad actual no equivale a tests/backend funcional aprobados.
