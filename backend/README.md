# Backend · Millet ERP

API ASP.NET Core en .NET 10. La solución `Millet.sln` contiene el host `Api`,
los proyectos de cada módulo y sus proyectos de pruebas. Los módulos usan
CQRS con MediatR y PostgreSQL con esquemas propios; el código compartido vive
en `SharedKernel` y `Compartido`. El código de negocio ya está implementado
en varios módulos: este repositorio dejó de ser un esqueleto inicial.

La integración de código y sus pruebas no sustituyen la validación operativa
de Millet. Consultar la [base consolidada](../docs/handoff/29-base-consolidada-adm08-net10-2026-09-29.md)
para conocer qué se comprobó y qué depende de servicios externos.

## Requisitos

- SDK .NET 10 compatible con `../global.json`: mínimo 10.0.100 y avance permitido `latestMinor`.
- PostgreSQL local y Docker para el conjunto de pruebas de integración desechable.
- Variables locales según las plantillas del repositorio y el [arranque local](../docs/handoff/01-arranque-local.md).
- Para el acceso manual: configuración Microsoft Entra del entorno autorizado y clave de firma JWT local. Las credenciales se inyectan por variables o secretos locales.

## Compilación y pruebas

Desde la raíz del repositorio:

```bash
dotnet restore backend/Millet.sln
dotnet build backend/Millet.sln --configuration Release --no-restore
./tools/validate-integration-isolated.sh
```

El último comando crea PostgreSQL temporal, aplica los 12 contextos definidos
en `tools/migration-contexts.txt` y ejecuta las tres suites de integración.
El contenedor se elimina al terminar. Las suites se niegan a ejecutarse
contra una base de desarrollo normal.

Las pruebas unitarias se ejecutan por proyecto de `backend/tests/*.UnitTests/`.
No usar `dotnet test` sobre toda la solución contra una base de desarrollo:
incluye suites que requieren el entorno aislado anterior.

## Arranque local

Desde la raíz, con las variables del entorno cargadas:

```bash
dotnet run --project backend/src/Api/Millet.Api.csproj
```

El puerto efectivo es el indicado por `Now listening on`; el perfil de
`Properties/launchSettings.json` puede definirlo. Verificar `/health/live`
y `/health/ready` en ese puerto. El ingreso manual usa Microsoft Entra.
Los adaptadores y trabajadores de A+W, correo y Azure requieren su propia
configuración; un health local satisfactorio no acredita esas integraciones.

## Migraciones de datos

Antes de actualizar una base con información, conservar una copia y probar
la actualización allí. `ProveedorRfcUnico` detiene la migración si encuentra
RFC no genéricos duplicados. `ReconciliacionCatalogoM1` conserva los IDs
históricos y crea IDs nuevos para cuatro equipos que cambian de padre;
su reversa exige conciliación manual de referencias.

Ver [impacto histórico de ADM-08](../docs/modulos/centros-costo/12-adm08-impacto-historico-m1.md).
