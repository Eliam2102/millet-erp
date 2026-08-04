# Knowledge base files

Archivos del repo que se suben al Claude Project como knowledge base.
`bundle.ps1` empaqueta exactamente esta lista; mantenerlos sincronizados.

## Núcleo (siempre)

- `README.md`
- `CLAUDE.md`
- `CONTRIBUTING.md`
- `docs/README.md`
- `docs/arquitectura.md`
- `docs/onboarding-dev-seed-users.md`
- `docs/decisiones/README.md`
- `docs/decisiones/template.md`
- Todos los ADRs aceptados o propuestos: `docs/decisiones/00NN-*.md`
  (`bundle.ps1` los descubre por glob).

## Operación

- `infra/README.md`
- `backend/README.md`
- `frontend/README.md`
- `tools/README.md`

## Levantamientos por módulo (cuando existan)

- `docs/levantamientos/*.md`
  Aún vacío en Phase 1 (solo el README de la carpeta). El bundle los incluye
  cuando aparezcan.

## NO subir

- Nada bajo `bin/`, `obj/`, `node_modules/`, `dist/`.
- `package-lock.json`, `*.csproj`, `*.bicep`, `*.tsx`, etc. — el código vivo
  no va a la KB; va al repo. La KB es para documentación y decisiones.
- Migraciones EF auto-generadas (`backend/src/**/Migrations/**`).
- Archivos `.env*` o cualquier archivo con secretos (no debería haberlos en
  el repo, pero por si acaso).
- `routeTree.gen.ts` y otros archivos generados.
