# Instrucciones para agentes (Codex y otros)

Antes de cambiar código, lee `CLAUDE.md` (contexto y convenciones del monorepo); para UI, también `frontend/AGENTS.md`.

Pruebas de integración y PR: sigue `docs/qa/gate-integracion.md`.
- Pruebas nuevas de API: `[Collection(ApiCompartida.Nombre)]`, no `IClassFixture<WebApplicationFactory<Program>>`.
- Todo `WithWebHostBuilder` se cierra con `await using`; su cliente, con `using`.
- Comercio Exterior y Activos Fijos: `ExpedientesHttpTests.Escenario.CrearAsync()`.
- No dejes en el repositorio resúmenes (`RESUMEN_*.md`), logs, TRX ni carpetas `outputs/`: tu resumen va en tu respuesta o fuera del repositorio. El CI rechaza el PR si los encuentra.
