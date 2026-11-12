# Quickstart: demostrar preparación de servicios CE

No ejecutar sobreQA/producción hasta preparar/revisar el pase concreto. No nueva autorización de administrador para leer el ERP. Usar datos ficticios identificados, perfiles aprobados y las fuentes recibidas.

## Validación local tras integrar
1. Reconfirmar SHAorigen y manifiesto74/74; ramaaislada, no modificar `millet_erp-COMEX-AF-listo`.
2. Unificar aplicación de tresvías,8solapamientos y3migraciones; verificar modelos y compilación.
3. Reutilizar suites dirigidas:
   - Compras.UnitTests: OrdenCompraAutorizarTests yFirmasServicioCeGuardTests.
   - CuentasPorPagar.UnitTests: P3FacturaConOcTests, AplicarNcYAnticipoTests yElegibilidad.
   - ComercioExterior.UnitTests: CierreServiciosCeTests y regresionesP20/P25.
   - Api.UnitTests: adapters/saldos por empresa; Tesoreria.UnitTests: pagos/reversas pertinentes.
   - Vitest: compras/ordenes ycxp/FacturaDetallePage.p3.test.tsx; tipos/lint/frontendbuild.
4. Iniciar sóloPostgreSQLdepruebas si no estádisponible: `docker compose -f docker-compose.test.yml up -d`.
5. `./tools/validate-integration-isolated.sh --shared-db --filter "FullyQualifiedName~Millet.Api.IntegrationTests.ComercioExterior"`; luego gate deCompras yCxP pertinentes, usando nombres reales. Salidas/TRXfuera del repo. CIejecuta integracióncompleta3partes.
6. No presentar181casosdevariascorridasanteriores como corridaactualcompleta. Registrar nombre,estado,fecha/SHA por ejecución.

## Ensayo por interfaces que falta
Servicio ficticio100MXN antes de impuestos, OCseparada de mercancía. N1porCE → capturafacturaHTTP → N2porComercialdistinto → liberaciónCxPHTTP → pagoTesoreríaHTTP → proyección → cierreCEHTTP. Sembrar catálogos/usuarios es preparación; insertar factura/aplicación para sustituir esasacciones invalida la demostración del circuito.
Negativos: capturaantesN1; N2sinpasivo; misma persona; duplicado; incompleta; cancelaciónsustitucióntrasN2; impago/parcial/reversapendiente; facturaotraempresa. Tras total, exactamenteunpasivo y gasto, políza/cierrecoherentes. Pago40+76 (factura100+IVA16) y reversa40 debenmostrar saldos esperados, con código de moneda y sin segundo pago por proyección.

## Junta yQA
Si el paquete no está publicado, anunciarlo explícitamente: sesión de validación delcaso/configuración, no demo funcional terminada. Llevar el flujo ya contestado porLaura, capturas reales de lo que sí existe y lista de tareasVILO.
Tras pase: registrar versionesAPI/portal, rolesCE/CxP/Comercial/Tesorería, empresa/sucursal, OC/factura/expediente, firma/pago/póliza y resultado porpaso. Ensayar crédito, anticipoexcepcional y pago separado delpedimento. ConservarN15 como evidenciaespecífica; crear prueba nueva para servicios sin alterar el expediente cerrado.
Salida: acta con esperado/obtenido, fuente/versión, evidencia y responsable; aceptaciónMilletseparada. No usar cuentas/transferenciasreales ni timbreproductivo.
