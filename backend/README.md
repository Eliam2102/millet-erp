# Backend - Millet ERP

API en .NET 9 que implementa el back-office del ERP. Monolito modular con
arquitectura hexagonal y CQRS por módulo, comunicación entre módulos por
eventos asíncronos vía Azure Service Bus.

Para entender la arquitectura completa antes de tocar código, lee
[`docs/arquitectura.md`](../docs/arquitectura.md) (15-20 min). Las decisiones
fundacionales viven en [`docs/decisiones/`](../docs/decisiones/).

## Estructura

```
backend/
├── Millet.sln
├── Directory.Build.props          ← settings comunes a todos los proyectos
├── Directory.Packages.props       ← Central Package Management (versiones)
├── nuget.config
├── src/
│   ├── Api/                       ← ASP.NET host único, hosts todos los módulos
│   ├── SharedKernel/              ← primitivas transversales (BaseEntity, Money, IClock, …)
│   │   ├── Domain/
│   │   ├── Application/
│   │   └── Infrastructure/
│   └── Identidad/                 ← módulo (esqueleto vacío en Fase 1)
│       ├── Domain/
│       ├── Application/
│       └── Infrastructure/
└── tests/
    ├── SharedKernel.UnitTests/
    ├── Identidad.UnitTests/
    └── Api.IntegrationTests/
```

La modularidad es por **carpetas y namespaces**, no por proyectos `.csproj`
separados por módulo. Un solo `Api.csproj` referencia todos los módulos. Esto
mantiene los tiempos de build razonables sin perder el aislamiento, que se
garantiza por convención (no joins SQL cross-schema, comunicación por eventos)
y no por límites de proyecto.

`SharedKernel` es el "shared kernel" en sentido DDD: tipos transversales
(`BaseEntity`, `Money`, `IClock`, excepciones de dominio, value objects)
que todos los módulos consumen. Su nombre evita la palabra reservada
`Shared` de VB.NET (CA1716).

## Requisitos

- **.NET 9 SDK** ([descarga](https://dotnet.microsoft.com/download/dotnet/9.0))
- PostgreSQL 16 (para correr la app contra una BD real; en Fase 1 todavía no se usa)

## Comandos comunes

Restaurar paquetes y compilar:
```powershell
dotnet build backend/Millet.sln
```

Correr todos los tests:
```powershell
dotnet test backend/Millet.sln
```

Correr la API localmente (Hello world placeholder en Fase 1):
```powershell
dotnet run --project backend/src/Api/Millet.Api.csproj
```

La API arranca por defecto en `http://localhost:5000` (puerto puede variar
según `Properties/launchSettings.json`). En el browser o con curl:
```
curl http://localhost:5000/
# → Hello World!
```

## Estado actual (Fase 1)

Esta fase es **scaffolding y fundación técnica**, no implementa módulos de
negocio. Lo que existe:

- ✅ Estructura de proyectos, solución, Central Package Management
- ✅ Hello world endpoint
- ⬜ `BaseEntity`, `IClock`, `Money`, excepciones de dominio (PR 2)
- ⬜ `BaseDbContext`, interceptors, esquemas iniciales (PR 3)
- ⬜ `ProblemDetails`, Serilog, OTel Distro, `/health` (PR 4)
- ⬜ Bicep para App Service, Service Bus, SignalR (PR 5)
- ⬜ Frontend setup (PR 6)

Lo que **no** está en Fase 1: módulos de negocio (Comercial, Fiscal, etc.),
tablas de identidad reales, MediatR, Outbox publisher, Hubs SignalR concretos,
componentes de presentación del frontend, login real con Entra ID. Cada uno
llega en una fase posterior.
