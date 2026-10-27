# Pruebas de integración y PR limpios

Regla vigente desde el 27-oct-2026 para todo el equipo y para los agentes (Claude, Codex). Un PR a `develop` se integra sólo si las pruebas de integración están en verde y no trae archivos de trabajo. El CI lo comprueba solo; esta guía explica qué hacer para no llegar rojo.

## 1. Preparar tu máquina (una sola vez)

Igual en Linux y macOS. Necesitas Docker (en Linux, tu usuario en el grupo `docker` o usar `sudo`) y el SDK de .NET de `global.json`.

```bash
docker compose -f docker-compose.test.yml up -d
```

Así queda encendido el PostgreSQL de pruebas `millet-erp-postgres-test-1` en el puerto 5435. Se vuelve a encender solo al reiniciar Docker. No toca tu Postgres de desarrollo.

## 2. Qué corre y cuándo

| Momento | Qué corres | Cuánto tarda |
|---|---|---|
| Mientras programas | Tus pruebas unitarias y **sólo las de integración de tu módulo**: `./tools/validate-integration-isolated.sh --shared-db --filter "FullyQualifiedName~Millet.Api.IntegrationTests.<Módulo>"` | segundos a pocos minutos |
| Si tocaste algo compartido (seeds, migraciones, `Program.cs`, auth, outbox, fixtures) y quieres saberlo antes que el CI | El gate completo: `./tools/validate-integration-isolated.sh --shared-db` | depende de tu máquina (≈ 20 min en una laptop de desarrollo) |
| En cada PR con cambios en `backend/` | Nada: el CI corre el gate completo en 3 partes en paralelo (check **«Integration backend»**) | ≈ 15 min, sin que nadie espere |

- `<Módulo>` es la carpeta de `backend/tests/Api.IntegrationTests` (por ejemplo `Tesoreria`, `ComercioExterior`). Para Compras: `--filter "FullyQualifiedName~Millet.Compras.IntegrationTests"`.
- `--shared-db` usa el PostgreSQL de pruebas del paso 1. Sin la bandera, el script levanta uno nuevo y lo borra al terminar.
- Varias corridas pueden compartir el PostgreSQL de pruebas: cada una crea y borra sólo sus bases (`millet_gate_<pid>` y sus clones CE). Aun así, en una laptop conviene una sola corrida **completa** a la vez por memoria.
- **Rojo = no se integra.** Si el check sale en rojo, se corrige ese mismo día. Si una falla no es tuya, avisa en el PR con el nombre de la prueba; **no** la desactives ni la marques como omitida para pasar.

## 3. Calendario del sprint

| Cuándo | Qué pasa |
|---|---|
| Lunes a viernes 12:00 | Cada cambio se prueba al subir su PR. Verde → entra a `develop`. Rojo → se corrige ese mismo día. |
| **Viernes 12:00 · corte** | Último momento para tener el PR arriba y en verde. Lo que no esté no entra al sprint. |
| Viernes en la tarde | `develop` → `main`. Se corrige sólo lo que falle al juntar todo, sin dejar nada abierto. |

## 4. Medir

```bash
MILLET_GATE_TIEMPOS_DIR=/ruta/fuera/del/repo ./tools/validate-integration-isolated.sh --shared-db
python3 tools/gate-tiempos.py /ruta/fuera/del/repo            # fases, conteos y clases más lentas
python3 tools/gate-tiempos.py /ruta/fuera/del/repo --fallas   # lista para comparar con diff
```

Usa un directorio **fuera del repositorio**: los TRX y `fases.tsv` no se suben.

## 5. Cómo escribir una prueba de integración de API

1. **Por omisión, usa el servidor compartido:** `[Collection(ApiCompartida.Nombre)]` y recibe `WebApplicationFactory<Program>` en el constructor. **No** uses `IClassFixture<WebApplicationFactory<Program>>`: enciende un servidor por clase y cuesta ≈ 3 s cada vez.
2. **Todo servidor hijo se cierra.** Si necesitas cambiar servicios con `WithWebHostBuilder`, guárdalo con `await using var f = _factory.WithWebHostBuilder(...)` y su cliente con `using`. Si el hijo es de la clase, ciérralo en el `Dispose`/`DisposeAsync` de su fixture. Un hijo sin cerrar queda encendido toda la corrida, agota las conexiones de PostgreSQL (`53300 too many clients`) y hace fallar pruebas ajenas.
3. **No cambies variables de entorno** ni configuración global en una clase compartida. Si de verdad lo necesitas (pruebas de arranque, seed o autenticación real), usa tu propia fábrica y documenta por qué en el `<summary>` de la clase.
4. **Si todas las pruebas de una clase usan la misma configuración**, crea el hijo una vez por clase (fixture propio) y reinicia sus dobles al inicio de cada prueba.
5. **Comercio Exterior y Activos Fijos:** usa `ExpedientesHttpTests.Escenario.CrearAsync()`. Clona una plantilla ya migrada; no crees ni migres bases propias por prueba.
6. Las pruebas corren en serie y comparten la base del gate: crea tus datos con identificadores únicos y no dependas de conteos globales.

## 6. Qué no entra en un PR (basura)

El check **«Archivos de trabajo»** rechaza el PR si agrega o modifica:

- resúmenes y bitácoras de agentes: `RESUMEN_*.md`, `RESUMEN-*.md`, `NOTAS_*.md`;
- carpetas de salida y logs: `outputs/`, `logs/`, `TestResults/`, `*.log`, `*.trx`, `nohup.out`;
- restos de merge y del sistema: `*.orig`, `*.rej`, `.DS_Store`;
- archivos de sesión: `.claude/scheduled_tasks.lock`, `.claude/worktrees/`.

La única excepción son los ejemplos versionados a propósito en `tools/testdata/`.

Además, aunque el CI no lo detecte:

- un PR = una tarea de ClickUp; nada de cambios ajenos a la tarea «de pasada»;
- sin código comentado, `Console.WriteLine` de depuración ni pruebas desactivadas (`Skip`) sin motivo escrito en la prueba;
- la evidencia (capturas, conteos, corrida del gate) va en la descripción del PR, no como archivo en el repositorio.

## 7. Por qué

El 27-oct-2026, el gate completo tardaba 41 min y el CI no lo corría. Por eso llegaban a `develop` PRs con pruebas de integración rotas; por ejemplo, el #233 tuvo que arreglar pruebas que ya fallaban. Con el servidor compartido, la plantilla CE/AF y el cierre de servidores hijo, bajó a ≈ 21 min sin fallas (PR #237). Con eso se pudo meter al CI en paralelo, donde tarda ≈ 15 min.
