# P7 · Cancelación tras crear OC · Adenda 2 · 09-oct-2026

La preparación de la prueba reutilizaba una requisición obsoleta después de
crear la OC mediante HTTP. Se corrigió el ciclo de vida de los scopes de la
prueba. **El verde PostgreSQL de los dos casos reportados sigue Por confirmar**:
el sandbox no permite acceder al socket Docker. No se modificó producción.

## Causa comprobada en código

- Base local: `6b0e610`, rama `fix/P7-resto-compras-almacen`; worktree limpio
  al comenzar. Los cambios anteriores ya estaban guardados por Claude.
- `P7RestoComprasAlmacenTests` autorizaba la RQ con el `ComprasDbContext`
  del scope de preparación y conservaba ese agregado rastreado.
- El POST `/ordenes/desde-requisicion` ejecuta
  `CrearOrdenCompraDesdeRequisicionHandler` en otro scope: llama a
  `rq.ComprometerEnOc(oc.Id)` y guarda la RQ.
- `MetadataSaveChangesInterceptor` aumenta `Requisicion.Version` al guardar
  una modificación. `BaseDbContext` configura esa propiedad como token de
  concurrencia. Por eso la instancia de preparación queda con una versión
  anterior y `ComprometidaEnOcId == null`.
- Cancelar con aquel contexto obtiene su instancia rastreada anterior;
  su UPDATE usa la versión vieja y afecta cero filas. La rama habilitada y
  la deshabilitada comparten exactamente este paso HTTP; el apartado no es
  quien modifica esa versión.
- Producción registra los DbContext con `AddDbContext` (scoped) y el endpoint
  de cancelación envía el comando por MediatR en el scope de su petición.
  No comparte el contexto de una petición anterior de creación de OC.

La evidencia de código identifica un defecto de preparación de prueba, no
justifica eliminar la concurrencia optimista ni recargar silenciosamente
agregados dentro del handler. La comprobación final contra PostgreSQL debe
confirmar este diagnóstico con las aserciones nuevas.

## Cambio acotado

En `backend/tests/Api.IntegrationTests/Compras/Oc/P7RestoComprasAlmacenTests.cs`:

- Se comprueba explícitamente que el POST guardó el compromiso con la OC y
  aumentó la versión frente a la instancia retenida por la preparación.
- Cancelación y cierre manual usan cada uno un scope nuevo con sus propios
  contextos, mediator y servicio de transacción de apartados.
- Otro scope verifica los datos persistidos: `Cancelada`, versión posterior
  al compromiso, `CerradaSinSurtir`, apartados pendientes cero, físico 2 y
  disponibilidad 2 por el puerto real de Compras.
- Se conserva sin cambios la rama de fallo que comprueba rollback desde
  otro scope: RQ en autorización, sin firmas/apartados/OC y físico intacto.
  También se conservan los tres casos on/off/fallo, OC por 8, segunda RQ sin
  reutilizar las 2 apartadas y limpieza de datos DEMO en `finally`.

## Validación de esta continuación

- `dotnet build backend/Millet.sln --no-restore -m:1`: exit 0,
  0 errores, 0 advertencias; incluye las pruebas de integración modificadas.
- Unitarias completas: Compras 565; Almacén 316; Compartido 94;
  CxP 427; Tesorería 120; SharedKernel 258. **Total 1,780 aprobadas**,
  cero fallos y cero omitidas, incluidas las unitarias existentes de P1/P2.
- VSTest no puede abrir su socket local (`SocketException (13)`). Se usó
  el runner real `XunitFrontController.RunAll` en proceso, sin TCP y sin
  paralelismo, ya conservado en `evidencia-adenda/runner-xunit.cs`.
- `npx tsc --noEmit -p tsconfig.json` y `npm run -s typecheck:test`: exit 0.
- `npm run -s lint`: exit 0, 0 errores y 10 advertencias existentes.
- `npx vitest run --maxWorkers=4`: exit 0, 358 archivos y 2,072 pruebas
  aprobadas; 122.31 s. Conserva avisos de jsdom sobre canvas/navegación.
- `git diff --check`: exit 0.
- `bash tools/validate-integration-isolated.sh`: exit 1 por acceso denegado
  a `/Users/eliamcv/.orbstack/run/docker.sock`; no ejecutó PostgreSQL ni las
  suites. No se afirma 953/953 ni un rojo/verde de integración local.

Evidencias de esta corrida: [build](evidencia-adenda-2/build.txt),
[unitarias](evidencia-adenda-2/unitarias.txt),
[TypeScript](evidencia-adenda-2/tsc.txt),
[tipos de pruebas](evidencia-adenda-2/typecheck-test.txt),
[lint](evidencia-adenda-2/lint.txt), [Vitest](evidencia-adenda-2/vitest.txt),
[límite de VSTest](evidencia-adenda-2/vstest.txt) y
[límite del gate](evidencia-adenda-2/gate.txt).

## Contraste de P7 y pendientes

Se conservaron los puntos ya existentes: apartado configurable y ADR-0061;
cotización obligatoria; árbol recursivo y proveedor de aplicaciones de pago;
aviso de vales en Inicio; reorden por existencia más pedido vivo contra el
punto, hasta máximo o cantidad fija; conversión y captura de unidades;
obra heredada RQ → OC → factura. No se añadió migración ni `ParametroGlobal`.
No se tocaron flujos P4, frontend, `infra/`, `.env*` ni servidores.

**ADM-08 continúa pendiente:** ADR-0050 exige Dim3 por línea e independencia
del departamento, mientras la ficha pide herencia y caso sin máquina. Se
solicitó a Eliam resolver esa regla; V49 por sí sola no impide usar DEMO.
V08/remitente, equivalencias reales V49, ensayo visual, confirmación de D3
con Millet y aceptación siguen Por confirmar.

Siguiente acción: Claude ejecuta el gate completo, sin filtro, y conserva
conteos y rojo/verde PostgreSQL. Deben pasar los dos casos reportados,
la rama de rollback y las regresiones P1/P2. P7 no se declara cerrado.

No se hizo commit ni push, conforme a la regla común explícita de la ficha.
Mensaje propuesto:

```text
test(p7): aislar cancelación y cierre de requisiciones tras crear la OC
```
