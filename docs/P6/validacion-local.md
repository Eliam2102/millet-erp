# Validación local · continuación P6 · 9-oct-2026

Código verificado sin commit en `fix/P6-acceso-y-sucursal`, desde `4bf1690`.

| Check | Resultado de esta continuación |
|---|---|
| `cd backend && dotnet build Millet.sln --no-restore -m:1 /nr:false --nologo` | exit 0; 0 errores; 0 advertencias; 27.64 s en la última corrida |
| Unitarias de Api, Identidad, Compartido, Compras, Administración, Facturación, CxP, CxC, Tesorería, SharedKernel y Catálogos | 2140 aprobadas; 0 fallidas; 0 omitidas; runner xUnit en proceso |
| `npx tsc --noEmit -p tsconfig.json` | exit 0 |
| `npx tsc --noEmit -p tsconfig.app.json` | exit 0 |
| `npm run -s typecheck:test` | exit 0 |
| `npm run -s lint` | exit 0; 0 errores; 10 advertencias preexistentes de react-hooks |
| `npx vitest run` | exit 0; 342 archivos; 1995 pruebas aprobadas; 85.28 s |
| EF `migrations has-pending-model-changes`, Identidad y Compartido, `--no-build` | exit 0; No changes have been made to the model since the last migration |
| `git diff --check` | exit 0 |

Build/runner usan `DOTNET_CLI_HOME=/tmp/p6-dotnet`; build con `MSBUILDDISABLENODEREUSE=1`. No se instalaron paquetes nuevos. [Salida del runner por módulo](evidencia-unitarias.txt).

VSTest estándar se intentó y abortó: `SocketException (13): Permission denied` al abrir su canal. Se utilizó el runner oficial xUnit disponible en los paquetes del entorno, sin cambiar los tests. Su ruta base se corrigió al directorio compilado de cada proyecto para que las pruebas existentes que leen fuentes encuentren el repositorio; después se repitieron los once módulos y todos pasaron.

Docker devuelve `permission denied` al acceder al socket del daemon. Las pruebas PostgreSQL están compiladas, pero no se ejecutaron en esta continuación; la nueva corrida de `tools/validate-integration-isolated.sh` corresponde a Claude. El 861/966 y los 105 fallos son la corrida previa aportada por la adenda; no se sustituyen por el verde local.

Vitest informa limitaciones de jsdom para canvas/navegación; no hay fallos de tests. No se realizó una comprobación visual con navegador ni operación contra Entra/Graph/blob reales. No se aplicaron migraciones, ni se levantaron servidores, ni se fusionó o publicó código. `infra/` y `.env*` no cambiaron.
