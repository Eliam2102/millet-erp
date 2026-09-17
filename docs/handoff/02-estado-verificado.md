# Estado técnico verificado

**Corte:** 16 de septiembre de 2026  
**Commit base local:** `f0f50eb549caa304c6161c0896ed70c6a7ad8a69`  
**Rama:** `main`  
**Remoto:** no configurado al momento del corte

## Evidencia ejecutada

- .NET SDK local disponible: `10.0.401`; el proyecto requiere oficialmente .NET 9. Para esta auditoría se usó `DOTNET_ROLL_FORWARD=Major` sólo como compatibilidad local, no como requisito recomendado de onboarding.
- Backend: compilación completa aprobada, **0 advertencias y 0 errores**.
- Backend: **2,768 pruebas aprobadas y 0 fallidas**: 2,279 unitarias y 489 de integración (A+W 7, Compras 120 y API 362) contra una base local aislada.
- Frontend: **278 archivos y 1,567 pruebas aprobadas**.
- Frontend: compilación de producción aprobada; conserva advertencias por chunks mayores a 500 kB.
- Docker Compose: configuración válida.
- Migraciones: los **12 DbContexts** quedaron aplicados y al día en PostgreSQL local aislado en el puerto `5434`.
- API: `/health/live` y `/health/ready` respondieron HTTP 200.
- Acceso local: `POST /api/dev/fake-login` respondió HTTP 200 y la interfaz permitió entrar como `Super Admin (Dev)` hasta mostrar el tablero y los módulos.

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
