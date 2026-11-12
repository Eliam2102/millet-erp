# Implementation Plan: Cierre N-15 sin acumulados obsoletos ni ampliación de permisos

**Branch**: `fix/n15-ce-conciliacion` | **Date**: 2026-11-11 | **Spec**: [spec.md](spec.md)

**Input**: `specs/001-n15-cierre-ce/spec.md`; evidencia QA de CE-IMP-2026-000004.

## Summary

Usar Almacén como fuente canónica del recibido mediante el puerto público existente y
RecibidoPorLineaOc. El evento activa la conciliación, no dicta el último acumulado.
Compartir distribución y candados con la recuperación durante revisión/cierres normales.
Reconocer al responsable el permiso efectivo CE para gestionar todas las sucursales,
además de asociación/alcance corporativo existentes. No conceder permisos ni crear endpoints/UI.

## Technical Context

**Language/Version**: C#/.NET10.0.100, TypeScript sólo si un consumidor requiere ajuste (no previsto).
**Primary Dependencies**: EF Core/Npgsql, MediatR, xUnit y puertos existentes. Sin dependencias nuevas.
**Storage**: PostgreSQL; ningún cambio de esquema ni migración previsto.
**Testing**: regresión de dominio, adapter/permiso, API CE en PostgreSQL aislado y QA autenticado.
**Target Platform**: App Service QA Staging, portal actual; producción fuera de alcance.
**Project Type**: monolito modular/web.
**Performance Goals**: una lectura de neto por OC distinta por conciliación; sin barridos globales.
**Constraints**: mantener separación de módulos, empresa/sucursal, If-Match, idempotencia,
historia y bloqueos por faltantes reales. No usar máximo histórico ni reloj/GUID como autoridad.
**Scale/Scope**: tres OC/dos guías/seis recepciones en el ensayo; hasta100 OC existentes por operación.

## Constitution Check

Antes y después del diseño: PASS. Fuentes identificadas, regresiones primero, puerto del módulo
dueño del dato, controles sin cambios, sin dependencia/migración/endpoint nuevo. QA y aceptación
separadas. Archivos de pruebas/resultados fuera del repositorio. No hay excepción de complejidad.

## Project Structure

### Documentation (this feature)
```text
specs/001-n15-cierre-ce/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/conciliacion.md
├── checklists/requirements.md
└── tasks.md
```

### Source Code (repository root)
```text
backend/src/Almacen/Application/PublicPorts/IRecepcionesImportacionReadPort.cs
backend/src/Almacen/Infrastructure/PublicAdapters/RecepcionesImportacionReadAdapter.cs
backend/src/ComercioExterior/Application/RecepcionCe.cs
backend/src/ComercioExterior/Application/ConciliacionRecepcionServicio.cs
backend/src/ComercioExterior/Application/ExpedienteHandlers.cs
backend/src/ComercioExterior/Domain/ExpedienteRecepcion.cs
backend/src/ComercioExterior/Domain/ExpedienteImportacion.cs
backend/src/ComercioExterior/Infrastructure/DependencyInjection.cs
backend/src/ComercioExterior/Infrastructure/PublicAdapters/CatalogosCeReadAdapter.cs
backend/tests/{ComercioExterior.UnitTests,Almacen.UnitTests,Api.UnitTests,Api.IntegrationTests}/
```

**Structure Decision**: conservar capas y registros de DI existentes. El servicio compartido
reúne la distribución actual; sólo Almacén calcula netos de su esquema.

## Diseño de ejecución

1. Conservar validación del payload de evento (identidades, fecha, acumulado único/no negativo).
2. Tomar transacción/candados FOR UPDATE de los expedientes afectados, ordenados por ID,
   antes de consultar el neto canónico. Distribuir por arribo/creación/ID como ahora.
3. Excluir cancelados; reservar lo de cerrados sin modificarlos. Dato faltante no equivale a cero.
4. Registrar ID de eventos reales; misma cantidad no reabre/re-historia una incidencia ni invalida
   revisión. Duplicado no duplica efectos. Cantidad menor canónica sigue siendo válida.
5. En revisión y cierre, después de permiso/alcance/versión, reconciliar partidas que ya tengan
   recepción proyectada usando el mismo servicio; no crear incidentes al abrir operaciones sin
   recepción ni inventar eventos. Sin éxito de la acción no afirmar reparación persistida.
6. Validar responsable activo con rol vigente en la empresa de la sucursal: asociación/corporativo
   existente O permiso efectivo CE de gestión global del responsable, no del actor. Usar
   IPermissionLoader, respetando rol inactivo, grupo y denegación explícita.
7. Probar localmente, revisar diff y publicar dentro de la autorización vigente; exigir CI verde
   antes de integrar. Preparar pase QA concreto preservando los cambios entrantes de develop.
8. En QA comprobar permiso exacto de Eliam, recuperar el mismo expediente con una acción normal,
   registrar hitos con evidencia ficticia propia, revisar documentos y cerrar/aprobar. No repetir
   recepciones ni modificar originales, catálogos o concesiones de acceso. Mantener N-15 abierta
   si falta cualquiera de estos resultados.

## Complexity Tracking

Sin violaciones. Se descartan tabla de watermarks, nuevas colas, nuevos permisos y API de reparación.
