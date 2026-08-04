## Por qué

<!-- 1-3 frases. El qué se ve en el diff; aquí va el porqué del cambio. -->

## Cambios principales

- 

## ADRs tocados

<!-- Lista ADRs nuevos, reemplazados o cuya implementación avanza con este PR.
     Si no hay, escribir "ninguno". -->

- 

## Checklist

- [ ] Branch con prefijo `feature/`, `fix/`, `chore/` o `docs/`.
- [ ] Commits en español formato convencional (`tipo(scope): descripción`).
- [ ] `validate all (CI mirror)` (VS Code) o equivalente local pasa
      (`dotnet build`, `dotnet test`, `npm run build`, `npm run lint`).
- [ ] Si toca `infra/`: `az deployment sub what-if` revisado y output
      pegado en este PR o linkeado.
- [ ] Si toca contratos del API: tipos del frontend regenerados (cuando
      exista codegen, ver ADR-0017).
- [ ] Si toca un ADR existente: marcado como `Reemplazado por ADR-XXXX` y
      ADR nuevo agregado en `docs/decisiones/`.
- [ ] Sin secretos en commits ni en archivos de configuración.

## Notas de despliegue

<!-- Migraciones nuevas, app settings nuevos, secretos nuevos en Key Vault,
     cambios manuales en Entra (app registrations, redirect URIs).
     Si no aplica, escribir "ninguna". -->

- 
