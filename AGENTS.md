# Instrucciones para agentes (Codex y otros)

Antes de cambiar código, lee `CLAUDE.md` (contexto y convenciones del monorepo); para UI, también `frontend/AGENTS.md`.

Pruebas de integración y PR: sigue `docs/qa/gate-integracion.md`.
- Base de pruebas (una vez): `docker compose -f docker-compose.test.yml up -d`; luego corre sólo el módulo que tocaste con `./tools/validate-integration-isolated.sh --shared-db --filter "FullyQualifiedName~Millet.Api.IntegrationTests.<Módulo>"`. La suite completa la corre el CI.
- Pruebas nuevas de API: `[Collection(ApiCompartida.Nombre)]`, no `IClassFixture<WebApplicationFactory<Program>>`.
- Todo `WithWebHostBuilder` se cierra con `await using`; su cliente, con `using`.
- Comercio Exterior y Activos Fijos: `ExpedientesHttpTests.Escenario.CrearAsync()`.
- No dejes en el repositorio resúmenes (`RESUMEN_*.md`), logs, TRX ni carpetas `outputs/`: tu resumen va en tu respuesta o fuera del repositorio. El CI rechaza el PR si los encuentra.

## Validación de entradas

- Al crear o cambiar una entrada, contrasta los límites y reglas del proceso con la UI y el servidor. Cubre la solicitud directa, la carga masiva y el portal si escriben el mismo dato.
- Conserva la consulta de registros históricos. Aplica las reglas nuevas al dato que se crea o modifica. Reutiliza los validadores y `ProblemDetails` y prueba el rechazo antes de persistir.
- En documentos, comprueba el contenido con el validador del tipo antes de guardar. Si el trámite exige revisión documental, registra su resultado por versión, con actor, fecha y motivo. La validez del formato y la aprobación del documento son estados distintos.

## Base operativa Millet
Referencia única: `/Users/eliamcv/Documents/vilo/BASE DE CONCIMIENTO DE VIDRIOS/Obsidian ERP Millet`. Consultar Inicio, Cobertura y fichas pertinentes; registrar evidencias de cierre sin duplicar la bóveda.
