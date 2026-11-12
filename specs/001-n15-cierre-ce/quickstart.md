# Quickstart · Validación del cierre N-15

1. Partir de develop actualizado, rama aislada y git status limpio; conservar referencia al SHA.
2. Ejecutar regresiones dirigidas antes de corregir, guardar salida roja fuera del repositorio.
3. Preparar PostgreSQL con docker compose -f docker-compose.test.yml up -d.
4. Unitarias CE, Almacén y Api.UnitTests dirigidas; luego
   ./tools/validate-integration-isolated.sh --shared-db --filter "FullyQualifiedName~Millet.Api.IntegrationTests.ComercioExterior".
5. Probar cantidades: cronológico/invertido/empatado/repetido, disminución por anulación o
   devolución, varias guías/expedientes y cerrados/cancelados. Datos faltantes/negativos rechazan.
6. Probar responsable operativo, corporativo, bypass de gestión, lectura sola, otro usuario/empresa,
   rol inactivo y override denegado. Verificar el permiso del responsable, no sólo del actor.
7. Probar por HTTP la reparación histórica CE1/Almacén2 con cierre normal, sin nuevo evento,
   sin duplicar historial. Mantener If-Match y pruebas de permisos/alcance.
8. Revisar diff, modelos/migraciones y CI. Guardar resultados fuera del repo y publicar evidencia.
9. QA: comprobar /api/version, salud y permiso efectivo; usar CE-IMP-2026-000004 con sus OC6/7/8
   y recepciones7..12 ya existentes. No crearlas otra vez. Registrar hitos con soportes ficticios,
   revisar, cerrar las tres dimensiones y aprobar. Verificar seis filas IVA y ausencia de faltante.
10. Capturar estado final y resultados; actualizar N-15 y bóveda con límites. Cerrar sólo al
    contar con esa evidencia y distinguir la aceptación fiscal/productiva de esta prueba de QA.
