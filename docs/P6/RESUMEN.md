> **Estado vigente, adenda 4 (09-oct):** inicio limpio en `fa4c0df`, con fusión y correcciones anteriores ya registradas. Se aisló el periodo contable del fixture P6, se corrigió la limpieza parcial y de roles ADM-09, y bootstrap recorre todas las páginas sin reducir sus aserciones. Ver [adenda4-integracion-09oct.md](adenda4-integracion-09oct.md) para causas, archivos, resultados y pendientes. Build 0 errores/advertencias; 3,277 unitarias y 2,078 pruebas Vitest aprobadas; tipos/lint sin errores. Último gate externo aportado: API **947 aprobadas / 118 fallidas / 1,065 total**. El verde PostgreSQL posterior está **Por confirmar**: el sandbox deniega el socket Docker. Los apartados siguientes conservan sus cortes históricos; no acreditan aceptación Millet ni describen el estado actual de Git.

# Resumen de P6 — auditoría 9-oct-2026

**Estado:** continuación del 9-oct: correcciones de la adenda, implementación local y compilación/unitarias en verde. La corrida PostgreSQL aportada por Claude fue **861 aprobadas / 105 fallidas / 966 total**; el resultado después de estas correcciones está **Por confirmar**. Integración PostgreSQL, Entra/Graph real y aceptación en vivo Millet: **Por confirmar**. No constituye cierre de las 30 funciones del proyecto ni aceptación del cliente.

Worktree: `millet_erp-P6-ADM`. Rama: `fix/P6-acceso-y-sucursal`, basada en `main`/`4bf1690`. No se hicieron commits, push, despliegues ni cambios en `infra/` o `.env*`. No se levantaron servidores.

## Qué cambió

1. **F1-ADM-01:** el administrador organizacional pierde los permisos de crear/desactivar empresas, incluso concesiones históricas del bootstrap. El cargador reserva ambos al rol activo `super-admin`, también frente a overrides. Sucursales muestra la razón social de la sesión. La pantalla Mi empresa ya tenía pruebas de ausencia de alta/desactivación; se conservan y se agrega cobertura P6 del encabezado.
2. **F1-ADM-02:** el token validado aporta `groups`; `hasgroups`/`_claim_names.groups` activa consulta Graph. Se sustituye la pertenencia persistida en cada inicio real y se invalida la caché del usuario. Los roles manuales se conservan y los grupos no crean acceso a empresas no asignadas. Desactivar un rol o cambiar sus permisos invalida usuarios directos y por grupo en todas sus empresas. Se mantiene la prioridad de denegaciones. El evento de cambio de permisos se publica antes de SaveChanges (ADR-0009).
3. **F1-ADM-03:** `GET /api/v1/admin/auditoria/exportar` genera CSV UTF-8 con BOM, todos los filtros y permiso `admin.auditoria.leer`; exporta todas las páginas dentro de una instantánea RepeatableRead y registra la exportación. Se protege el escape CSV y las fórmulas al abrirlo en Excel. Auditoría incorpora el botón Exportar y descarga autenticada.
4. **F1-ADM-04:** estado activo de formas SAT mediante command/validator, permiso `catalogos.formas-pago.gestionar`, idempotencia y auditoría. Listado administrativo incluye inactivas; la pantalla ofrece interruptor solo con permiso. Cobro de mostrador, liquidación de ruta y movimientos de caja consultan el mismo catálogo activo que Facturación. Sus selectores de Caja también leen el catálogo activo y bloquean selecciones que se desactiven durante la sesión; se eliminaron las opciones fijas de captura.
5. **F1-ADM-11:** propietarios genéricos para requisición y factura proveedor, permisos independientes de ver/subir/baja y alcance de sucursal según operación. Se reutiliza la plataforma de adjuntos y su UI. Tipos de documento: soporte opcional, sin inventar obligaciones documentales Millet. Póliza sigue fuera hasta su motor.
6. **F1-ADM-12:** guardas y filtros antes de paginación/totales en RQ, OC, CxP, CxC y Tesorería. Se agregan puertos de lectura de relaciones entre documentos. Incluye PDF, árbol documental, evidencias, CFDI y movimientos/estados de cuenta TC relacionados. En módulos paralelos se conserva la lógica de firmas, saldo, cancelación, aplicación y conciliación; los cambios son de alcance y metadata necesaria para filtrar.

**Inventario:** [162 rutas y reglas por familia](inventario-sucursales.md). **Archivos:** [manifest completo de archivos cambiados](archivos-cambiados.md). **Adenda:** [causa y corrección de los fallos](fallos-integracion-09oct.md).

## Decisiones y diferencias contra la ficha

- CSV satisface la opción XLSX o CSV sin añadir paquetes; usa las consultas existentes, paginación estable y filtros completos.
- Se reutilizaron guardas, plataforma de adjuntos, auditoría EF y adapter SAT activo que ya existían. El texto de Facturación se ajustó para explicar la desactivación.
- Lectura y escritura corporativas de documentos usan permisos separados. Se conserva el bypass territorial previo de los adjuntos de OC por instrucción expresa de la adenda, manteniendo su permiso de operación independiente. Para un documento de varias sucursales se exige acceso a todas.
- Un origen no verificable no se asigna a la sucursal del usuario: se oculta de listados operativos y requiere corporativo por ID. Se documenta en el inventario qué documentos iniciales/globales entran en esta regla.
- Las asignaciones Entra son una pertenencia vigente por usuario, no duplicaciones permanentes en UsuarioEmpresaRol. Así salir del grupo no borra un rol manual legítimo.
- La decisión D15 de esta ficha autoriza construir Entra. El identificador D15 encontrado en la bóveda corresponde a otro asunto; se aplicó la instrucción explícita del 09-oct.
- Las pruebas usan dos sucursales existentes del seed y datos ficticios etiquetados. Los fixtures P6 limpian cuentas/tarjetas/roles de prueba; la forma 02 vuelve al estado original. La repetición con usuarios reales Cancún/Circuito corresponde a la aceptación en vivo.
- Se agregaron factories de diseño de EF para generar/verificar migraciones sin arrancar API/workers ni cargar credenciales. No abren conexión al generar el modelo.

## Migraciones preparadas

- `20261009170214_P6GruposEntraYAlcanceSucursal`: tabla de pertenencia a grupos y 17 permisos deterministas.
- `20261009170252_P6AdjuntosRequisicionYFacturaProveedor`: dos tipos de soporte opcionales.

Ambas incluyen Designer y Snapshot. `dotnet ef migrations has-pending-model-changes` devolvió **No changes have been made to the model since the last migration** para Identidad y Compartido. No se aplicaron contra una base de datos en este sandbox. La adenda de Claude acredita una corrida previa del gate con PostgreSQL, con los fallos descritos; no acredita el verde posterior.

## Pruebas nuevas

- `GruposEntraP6Tests`: unión de roles, salida del grupo, conservación de rol manual, rol inactivo y exclusividad de super-admin.
- `EntraGruposClaimsP6Tests`: claims directos, overage, eliminación de duplicados y marca malformada.
- `OrganizacionP6Tests`: CA1.2 departamento no asignado bloquea y asignado permite; CA1.6 jefe sin usuario no firma y vinculado sí autoriza el paso de jefe.
- `FormasPagoP6Tests`: CA1.9 alta y cambio de SAT auditados con actor/empresa, en catálogo aislado; CA1.10 catálogo real rechaza 02 inactiva y acepta activa.
- `BitacoraCsvP6Tests`: escape, comillas y protección de fórmulas.
- `AdjuntosP6Tests`: permiso y sucursal para ver/subir/baja de ambos propietarios.
- `SucursalScopeP6Tests`: propio/ajeno, corporativo y documento de varias sucursales; filtro previo al handler.
- `CxpDocumentosRelacionadosP6Tests`: CFDI y TC heredan sucursal de factura; filtro anterior a total/paginación; corporativo conserva ambos.
- `P6AccesoTests` (PostgreSQL): 403 de empresa, login directo/overage, salida del grupo y desactivación inmediata sin renovar JWT.
- `AuditoriaEndpointsTests` (PostgreSQL): CSV de 205 filas filtradas, BOM, download y registro de exportación.
- `P6FormasPagoTests` (PostgreSQL): desactivar 02 → listado de activas la oculta, admin la incluye, Caja/caja-movimiento/Facturación rechazan, bitácora registra, reactivar permite catálogo.
- `P6SucursalEndpointsTests` (PostgreSQL): casos por ruta de lectura/escritura/alta, listados propios/corporativos y permisos de adjuntos, incluyendo CFDI/TC y referencias de cuerpo. Camino válido: modificación propia/corporativa RQ persiste.
- `DemoSesionSeedTests` (PostgreSQL): se agregó login del usuario DEMO Compras y aserciones HTTP: RQ/OC MID y listado permitidos, OC MTY bloqueada y excluida. No se cambiaron roles ni se concedió bypass a la demo. Los pedidos facturables del seed usan MID; CxP/Tesorería se siembran inicialmente con catálogos, sin facturas/movimientos documentales. La operación de documentos creados durante el ensayo se valida con el gate/sesión.
- Frontend: las funciones puras de Caja prueban ocultación de 02 inactiva, selección desactivada, reactivación y ausencia de catálogo. `RegistrarCobroCard.p6.test.tsx` prueba el componente real: opciones activas, cobro válido y bloqueo tras desactivación. Exportación conserva filtros; interruptor SAT habilita/refresca y manda Idempotency-Key; sucursales usa razón social de sesión y no ofrece alta de empresa. Se mantienen los smoke tests y la prueba previa de MiEmpresaPage.

## Resultados locales exactos

Build de solución: **0 errores, 0 advertencias**, incluidos proyectos de integración. Comando utilizado: `cd backend && DOTNET_CLI_HOME=/tmp/p6-dotnet MSBUILDDISABLENODEREUSE=1 dotnet build Millet.sln --no-restore -m:1 /nr:false --nologo`. Restore previo con caché local NuGet; no cambios de paquetes.

VSTest no pudo abrir su socket de comunicación (`SocketException: Permission denied`). Las unitarias se ejecutaron con el runner oficial xUnit de los paquetes existentes, en el mismo proceso, con `AppContext.BaseDirectory` en el directorio compilado de cada proyecto. El primer intento del runner produjo cuatro errores de ruta en tests de auditoría de Compras; se corrigió el runner temporal y la repetición completa quedó en verde, sin alterar esas pruebas. **2140 aprobadas; 0 fallidas; 0 omitidas**:

| Módulo | Aprobadas | Fallidas | Omitidas |
|---|---:|---:|---:|
| Api | 30 | 0 | 0 |
| Identidad | 113 | 0 | 0 |
| Compartido | 83 | 0 | 0 |
| Compras | 540 | 0 | 0 |
| Administración | 140 | 0 | 0 |
| Facturación | 397 | 0 | 0 |
| CxP | 357 | 0 | 0 |
| CxC | 114 | 0 | 0 |
| Tesorería | 106 | 0 | 0 |
| SharedKernel | 251 | 0 | 0 |
| Catálogos | 9 | 0 | 0 |

Frontend final:

| Comando | Resultado |
|---|---|
| `npx tsc --noEmit -p tsconfig.json` | exit 0 |
| `npx tsc --noEmit -p tsconfig.app.json` | exit 0 |
| `npm run -s typecheck:test` | exit 0 |
| `npm run -s lint` | exit 0; 0 errores, 10 advertencias preexistentes de react-hooks en componentes ajenos al cambio |
| `npx vitest run` | exit 0; 342 archivos y 1,995 pruebas aprobadas |
| `git diff --check` | exit 0 |

## Qué no se verificó y relevo

1. PostgreSQL: `docker info` recibe **permission denied** al conectar con el socket Docker de este sandbox. La suite de integración está compilada, pero su inicializador exige una conexión PostgreSQL desechable y aborta sin ella. El intento del runner de integración terminó en errores de inicialización, sin verificar aserciones HTTP. No equivale a un rojo funcional ni a un verde de integración. La comprobación de Docker en esta continuación vuelve a dar permission denied. No se ejecutó el script completo ni se alteró esa protección. Los 105 fallos de la corrida externa se diagnosticaron y corrigieron en el código/fixtures, pero sus aserciones PostgreSQL no se ejecutaron aquí.
2. Entra/Graph real: configuración de groups, consentimiento de Graph y credencial de aplicación válidos en el tenant están **Por confirmar**. Los tests usan tokens/puerto simulados y no prueban el proveedor real. El adapter falla en español si no puede consultar grupos; no utiliza endpoints entregados en `_claim_sources`. Referencia del protocolo: [Microsoft: grupos y overage en tokens](https://learn.microsoft.com/security/zero-trust/develop/configure-tokens-group-claims-app-roles).
3. Adjuntos: no se verificó carga/descarga contra almacenamiento real ni revisión visual con servidor/browser; componentes y autorización tienen cobertura local. La disponibilidad y validación real del archivo se revisan en integración/aceptación.
4. Aceptación Millet: cambios/migraciones no desplegados ni fusionados; no se afirma cierre funcional en vivo.

**Siguiente acción:** Claude debe correr `./tools/validate-integration-isolated.sh` completo, sin filtro, desde este mismo worktree y reportar el rojo/verde. Corregir cualquier rojo antes de aplicar migraciones o fusionar. Después, sesión Millet: usuario operativo de Cancún frente a documentos Circuito y rol corporativo; login/grupo/rol; exportación filtrada; desactivar/reactivar 02; adjuntos RQ/factura.

Se conservó `P6DesignTimeDbContexts.cs` leyendo `ConnectionStrings__Postgres`, como pidió Claude. Se borró el archivo temporal `P6-fallos-integracion.txt` después de documentar las causas. La actualización de la bóveda está preparada en [actualizacion-boveda.md](actualizacion-boveda.md); no se aplicó fuera de este worktree.

Mensaje de commit propuesto (no ejecutado):

```text
fix(administracion): completar acceso y separación por sucursal de P6
```
