# Instrucciones para agentes (Codex y otros)

Antes de cambiar código, lee `CLAUDE.md` (contexto y convenciones del monorepo); para UI, también `frontend/AGENTS.md`.

Pruebas de integración y PR: sigue `docs/qa/gate-integracion.md`.
- Base de pruebas (una vez): `docker compose -f docker-compose.test.yml up -d`; luego corre sólo el módulo que tocaste con `./tools/validate-integration-isolated.sh --shared-db --filter "FullyQualifiedName~Millet.Api.IntegrationTests.<Módulo>"`. La suite completa la corre el CI.
- Pruebas nuevas de API: `[Collection(ApiCompartida.Nombre)]`, no `IClassFixture<WebApplicationFactory<Program>>`.
- Todo `WithWebHostBuilder` se cierra con `await using`; su cliente, con `using`.
- Comercio Exterior y Activos Fijos: `ExpedientesHttpTests.Escenario.CrearAsync()`.
- No dejes en el repositorio resúmenes (`RESUMEN_*.md`), logs, TRX ni carpetas `outputs/`: tu resumen va en tu respuesta o fuera del repositorio. El CI rechaza el PR si los encuentra.
