# Estado técnico verificado

- **Corte:** 16 de septiembre de 2026
- **Commit original auditado:** `f0f50eb549caa304c6161c0896ed70c6a7ad8a69`
- **Última revalidación:** `e6f506210bb1a345d7bde0d575d63df11151ee84`
- **Rama:** `main`
- **Remoto:** `https://github.com/Eliam2102/millet-erp` (privado)

## Evidencia ejecutada

- .NET SDK global disponible: `10.0.401`; el proyecto requiere oficialmente .NET 9. La revalidación se ejecutó con SDK `9.0.318` aislado en una carpeta temporal, sin modificar la instalación global. El onboarding debe instalar .NET 9 de forma normal.
- Backend: compilación completa aprobada, **0 advertencias y 0 errores**.
- Backend: **2,768 pruebas aprobadas y 0 fallidas**: 2,279 unitarias y 489 de integración (A+W 7, Compras 120 y API 362). La última revalidación utilizó una base migrada limpia y una copia independiente por proyecto de integración.
- Frontend: **278 archivos y 1,567 pruebas aprobadas**.
- Frontend: compilación de producción aprobada; conserva advertencias por chunks mayores a 500 kB.
- Docker Compose: configuración válida.
- Migraciones: los **12 DbContexts** quedaron aplicados y al día en PostgreSQL local aislado en el puerto `5434`.
- API: `/health/live` y `/health/ready` respondieron HTTP 200.
- Acceso local: `POST /api/dev/fake-login` respondió HTTP 200 y la interfaz permitió entrar como `Super Admin (Dev)` hasta mostrar el tablero y los módulos.
- Publicación: el commit `96de6044a7e3e0693af300ddfaba6b96c29ae543` se clonó desde el repositorio privado en una carpeta temporal limpia; Compose validó, backend compiló y el gate completo pasó después de crear/migrar una base exclusiva y ejecutar los proyectos en serie.

## Qué demuestra y qué no

Demuestra que el código unitario verificado y el frontend pueden compilar/probarse en esta copia. No demuestra:

- Integración real con A+W.
- Integración real con PAC/FiscalAPI.
- Inicio de sesión real con Entra ID.
- Migración real desde SAP.
- Despliegue en QA o producción.
- Aceptación funcional de Millet.
- Cobertura total del código por existir muchas pruebas.

## Riesgos técnicos inmediatos

1. `tools/setup-dev.ps1` fue alineado con los 12 contextos; cualquier módulo nuevo debe agregarse también al health check, al script y al pipeline de migraciones.
2. El puerto PostgreSQL fijo puede chocar con otras instalaciones; usar las variables documentadas.
3. Existen archivos `appsettings` versionados para desarrollo y servicios on-prem. Las API keys observadas están vacías, pero antes de publicar debe ejecutarse una revisión final del historial y de todos los archivos rastreados.
4. Algunos identificadores de aplicaciones de desarrollo están documentados. El repositorio debe ser privado.
5. Las advertencias de pruebas frontend (`act(...)`, handlers MSW y canvas) no hicieron fallar el gate, pero deben registrarse como deuda técnica y no ocultarse.
6. `npm audit` reporta 6 hallazgos: 5 moderados y 1 alto. El alto corresponde a `nanoid` transitivo; `vitest` afecta herramientas de prueba y `exceljs/uuid` requiere evaluar compatibilidad antes de cambiar versión. No se aplicó una corrección automática potencialmente disruptiva.
7. Los workflows de despliegue quedaron en disparo manual para que publicar el repositorio no modifique Azure sin autorización y sin validar OIDC/ambientes.
8. GitHub rechazó la protección automática de `main` con HTTP 403 porque el plan actual no habilita esa función en repositorios privados. No se hizo público el código. Hasta habilitar el plan o moverlo a una organización con la función disponible, la revisión por PR es un control de proceso, no una restricción técnica.
9. Una ejecución sin `ConnectionStrings__Postgres` apuntó por error a otro PostgreSQL que ocupaba `5432` y devolvió `28P01`. Una base clonada con residuos produjo dos falsos negativos por outbox/catálogos. El procedimiento comprobado es: puerto explícito + base limpia + 12 migraciones + una base independiente por proyecto de integración, o ejecución serial con reinicio comprobado. Esto debe automatizarse en CI.
10. El gate completo acredita la salud automatizada del repositorio, no la ejecución individual de las 35 funciones heredadas. El recorrido y la evidencia exigidos están en `17-plan-validacion-35-existentes.md`.
