# Replicar la demo del 12-oct en otra laptop (Maestros y A+W, Compras, Almacén, CxP y Tesorería)

> Para Eliam. Con esto levantas **exactamente el estado** en que Geovany
> ensayó la demo: mismo código, mismos usuarios y roles, mismos datos. Se apoya
> en el procedimiento oficial de `tools/demo/README.md` y le agrega lo que ese
> README no cubre: la rama con los arreglos, el respaldo de la base y las
> cuentas y datos que el recorrido necesita.
>
> Los pasos de pantalla, campo por campo, están en `docs/operacion/demo-guia/`
> (`00-maestros.md`, `01-compras.md`, `02-almacen.md`, `03-cxp-tesoreria.md`).

## 0. Qué necesitas recibir de Geovany (no va en GitHub)

| Pieza | Para qué | Cómo se pasa |
|---|---|---|
| `base-practica.dump` (1 MB) | La base de la demo completa: usuarios, roles, requisición/OC/recepción/factura/pago de ejemplo | Por un canal privado (AirDrop, USB, gestor de archivos). **No lo subas a GitHub**: trae correos y OID del equipo y datos de la copia de A+W |
| Valores de Entra (`TenantId`, `ClientId`, `Audience`, secreto) | Inicio de sesión con Microsoft | Gestor de contraseñas. Ver `tools/demo/README.md`, sección 3 |
| Tu «Id. de objeto» de Microsoft | Variable `Auth__InitialAdminEntraOid`: te vuelve super administrador | Portal de Azure → Microsoft Entra ID → Usuarios |
| `CFDI DEMO-7001.xml` (opcional) | Solo si quieres repetir la captura de factura con ese mismo CFDI | Está en `.verification/demo-tour/cxp-tesoreria/` de la laptop de Geovany |

Huella del respaldo para comprobar que llegó íntegro:
`sha256 9b59a1232ca265c5de6240694ed97a2f5660ebfe59d876cb6a30afcc14c4d5b4`
(`shasum -a 256 base-practica.dump`).

## 1. Requisitos (una sola vez)

Docker (OrbStack o Docker Desktop abierto), .NET SDK 10, Node 22+, python3 y
Git con acceso al repo. Detalle en `tools/demo/README.md`, sección 1.

## 2. Baja la rama correcta

Las correcciones de esta demo están en la rama `fix/demo-12oct-correcciones`,
que sale de `main` (ya con tu PR #94 de alcance corporativo y el #95 de OC):

```bash
git clone https://github.com/Eliam2102/millet-erp.git   # si no lo tienes
cd millet-erp
git fetch origin
git checkout fix/demo-12oct-correcciones
git pull
```

Qué trae esa rama respecto de `main` (commits separados por tema; **no toca el
backend de producto**, solo pruebas):
1. **Oculta "Cuentas de acceso"** del menú de Admin (la gestión de usuarios vive en
   Empleados → Roles y accesos; las rutas `/admin/usuarios` siguen por URL directa).
2. **Pruebas estabilizadas** (mocks de Vitest, aislamiento de pruebas de
   integración que dependían del orden o del entorno).
3. **Arreglos de la demo** (cada uno con su prueba):
   - El panel "Capturar factura desde OC" hace scroll y el texto rojo "Selecciona la
     línea de OC…" ya no aparece antes de enviar.
   - La OC no ofrece firmar a quien la creó ni a quien ya firmó N1; los sub-estados
     muestran el avance real (adiós "~50 %" fijo); la cabecera fija de OC y de
     requisición ya no tapa el primer bloque (medido en navegador: la fila
     "Recepción parcial (20 %)" queda visible).
   - Historial y Autorización de la OC en palabras ("Creó la firma de autorización");
     la trazabilidad dice "En surtido"; Saldos muestra "ALM-MID-G · INSUMOS".
   - El alta de proveedor ya no avisa de "posible duplicado" salvo que el backend lo
     detecte; el botón flotante de la palmera (devtools de React Query) viene apagado
     (se enciende con `VITE_QUERY_DEVTOOLS=true`).
4. **Documentación** de la demo, el archivo de usuarios y permisos
   (`demo-usuarios-permisos.md` / `.csv`) y el verificador
   `tools/demo/verificar-usuarios-demo.sh`.

**Acceso a todas las sucursales ("nivel todo").** Ya está en `main` (tu alcance
corporativo, PR #94): en el alta de empleado, una sucursal vacía crea al usuario
como **corporativo**, y en Empleados → Roles y accesos (o en el detalle de la cuenta)
la casilla **"Usuario corporativo de Millet"** lo cambia después; requiere el permiso
`admin.empleados.gestionar-todas-sucursales`. Esta rama no agrega otro mecanismo.

## 3. Configura lo privado

```bash
cp tools/demo/.env.demo.example tools/demo/.env.demo.local
```

Llena `tools/demo/.env.demo.local` con los valores de la sección 0 (ver la tabla
de variables en `tools/demo/README.md`). El archivo está ignorado por Git.

## 4. Prepara el ambiente

```bash
tools/demo/demo.sh preparar
```

Crea PostgreSQL en Docker (contenedor `millet-dev-postgres`, usuario `pgadmin`),
la base `millet_demo` con todas las migraciones, la copia de A+W de demo y las
dependencias del frontend. Tarda unos minutos.

## 5. Restaura la base con el estado del ensayo

Deja el archivo `base-practica.dump` en `~/.millet-demo/` (créalo si no existe) y corre:

```bash
mkdir -p ~/.millet-demo && cp base-practica.dump ~/.millet-demo/
tools/demo/restablecer-demo.sh
```

Reemplaza `millet_demo` por la base de Geovany en unos segundos (solo opera sobre bases
`millet_demo*`, nunca sobre dev) y muestra los conteos antes y después. La copia ya trae
las migraciones de esta rama (incluida `AlcanceCorporativoUsuario`). Si algún día tu código
es más nuevo que la copia, usa `tools/demo/restablecer-demo.sh --migrar`.

Debe dar: `usuarios 8`, `requisiciones 9`, `ordenes 9`, `facturas 7`, `proveedores 20`,
`articulos 13` y `clientes 80`. **Reinicia la API** (`tools/demo/demo.sh api`) y recarga el
navegador: la API guarda permisos en memoria.

Después verifica **usuarios, roles, sucursales y permisos** con el verificador
(compara contra `docs/operacion/demo-usuarios-permisos.csv`; debe terminar en `OK`):

```bash
tools/demo/verificar-usuarios-demo.sh
```

Si prefieres **no** usar el respaldo y reconstruir a mano lo que se cambió
en la base, mira el apéndice A.

## 6. Arranca (tres terminales)

```bash
tools/demo/demo.sh eventos   # emulador de Service Bus (la 1.ª vez baja ~750 MB)
tools/demo/demo.sh api
tools/demo/demo.sh web
tools/demo/demo.sh estado    # todo debe decir "ok"
```

**Sin el emulador de eventos no se actualiza "Recibido / Por surtir" en Compras
ni llega la recepción a CxP ni el pasivo a Tesorería.** Es lo primero que hay
que revisar si algo "no se mueve".

Abre `http://localhost:5173` y entra con «Continuar con Microsoft».

## 7. Cuentas que deben existir

Cada una debe ser una **cuenta Microsoft real** del tenant (hay MFA). Con el
respaldo ya están creadas en la base:

| Persona | Correo | Rol | Papel |
|---|---|---|---|
| Geovany | `geovanny@uzieltzaboutlook.onmicrosoft.com` | `super-admin` | Presenta; crea requisición y OC, recibe, captura factura, paga |
| fio | `fio@uzieltzaboutlook.onmicrosoft.com` | `DEMO-ROL-Jefe Compras` | Firma Nivel 1 |
| Mariana | `mariana.ortega@uzieltzaboutlook.onmicrosoft.com` | `DEMO-ROL-Dirección` | Firma Nivel 2 |

- Quien crea la OC **no puede firmarla** y N1 y N2 deben ser personas
  distintas. Por eso Geovany crea, fio firma N1 y Mariana N2.
- fio y Mariana tienen las sucursales **MID, MTY y QRO**. Sin asociación a
  sucursal o sin los permisos `*-todas-sucursales` no ven las requisiciones y
  órdenes de esa sucursal.
- Un usuario tiene **un solo rol por empresa**. Los permisos de una sesión
  abierta tardan hasta 5 minutos en refrescarse.
- **Tu propia cuenta** (Eliam): no existe en la base de Geovany. Con
  `Auth__InitialAdminEntraOid` = tu Id. de objeto, la API te hace super
  administrador al arrancar. Si ves "tu cuenta no tiene empresa ni roles",
  revisa esa variable y reinicia la API.

## 8. Verificación rápida (10 minutos, antes de las 9:00)

Con Geovany:
1. `/compras/ordenes` → abre **OC-MID2026-000007**: estado **Autorizada**; en
   la línea 1 (ART-CRT-01) **Recibido 6.00 y Por surtir 4.00**.
2. La trazabilidad de esa OC muestra RQ → OC → recepción **M-ENT2026-000011** →
   factura **DEMO-7001** → pago.
3. `/cxp/facturas` → **DEMO-7001** en estado **Pagada**.
4. `/admin/datos-maestros/proveedores` → `TOUR-PROV-OK` **Activo** con "Expediente
   completo" y `DEMO-PROV-REV` **En revisión** con su banner de documentos faltantes.
5. `/admin/datos-maestros/clientes` → Sincronizar → origen **Copia de demo**: el
   diálogo debe listar las ejecuciones previas (la API ya trae las variables de A+W
   por `demo.sh api`).
6. Crea una requisición nueva (paso 2 de `01-compras.md`) para comprobar que
   el formulario y los selectores cargan sin avisos rojos.

Con fio y con Mariana: entra en `/compras/ordenes/pendientes-autorizacion` y en
`/compras/ordenes`: **no debe salir ningún aviso rojo** de permiso.

## 9. Para presentar: ¿se reutilizan los documentos del ensayo?

El respaldo trae el ejemplo ya terminado (RQ MID2026-010008, OC
OC-MID2026-000007, recepción M-ENT2026-000011, factura DEMO-7001, pago
SPEI-DEMO-0001). Sirve para **mostrar el resultado**. Para hacerlo **en vivo**
crea documentos nuevos siguiendo las guías campo por campo; las cosas que
cambian al repetir:

- Requisición: usar el artículo crítico `ART-CRT-01` para que pida **dos
  firmas** (no hay umbral sembrado ni pantalla para editarlo); centro de costo
  de **MID + Ingeniería** (solo ING y CAL tienen equivalencia).
- Cotización: cualquier PDF.
- Recepción: UUID fiscal manual **distinto** al del ensayo
  (`6F1D2A9E-4B7C-4E58-9A31-0D5C8B7E1F42` ya está usado).
- Factura: UUID y folio nuevos (se deduplica por UUID): el CFDI
  `DEMO-7001.xml` ya está usado; edita su `UUID` y su `Folio` si lo reutilizas.
- Pago: referencia nueva (el ensayo usó `SPEI-DEMO-0001`).
- Maestros: el ensayo dejó `TOUR-ART-001`, `TOUR-PROV-OK` (validado) y
  `TOUR-PROV-NO` (rechazado). En vivo usa **claves nuevas** (p. ej. `TOUR-ART-002`,
  `TOUR-PROV-OK2`), un RFC de 12 caracteres con formato válido distinto, y 5 PDF
  de prueba cualquiera. **No toques `DEMO-PROV-REV`**: se deja en revisión para
  mostrar el bloqueo y subirle en vivo los 2 documentos que le faltan.
- Sincronización A+W: dará **0 creados** porque la copia de demo ya está
  sincronizada; los 1–2 errores (`NAME1 vacío`, `descripcion vacía`,
  `UNIDAD_SIN_EQUIVALENCIA`) sirven para explicar causa y acción sugerida. La
  medida `1800x2600` del guion no existe en la copia: usa `1830x2440` o `2440x3660`.

## 10. Si algo falla

| Síntoma | Causa y arreglo |
|---|---|
| Aviso rojo "Requiere el permiso X" | Falta ese permiso en el rol de esa persona. Ver `docs/operacion/demo-geovany-usuarios-roles-y-datos.md` |
| "Recibido / Por surtir" no cambia | El emulador de eventos no está arriba: `tools/demo/demo.sh eventos` y `demo.sh estado`; espera ~1 min |
| `ENTRADA_A_UBICACION_UNICA` / `ENTRADA_SIN_ASIGNACION` | Falta una ubicación real y la asignación artículo → rack. Ver `02-almacen.md` |
| `PERIODO_CONTABLE_NO_ADMITE` / `CXP_PERIODO_CERRADO` | No hay periodo contable abierto para esa fecha. Abrir 2026-10 en Contabilidad |
| `PASIVO_NO_PAGABLE_CXP` (elegible 0) o `PAGO_EXCEDE_ELEGIBLE` (importe mayor a lo elegible) | Aún no llega la recepción a CxP (segundos) o falta recibir. Esperar y recargar |
| Reportes Cartera/Antigüedad de CxP dan 422 `CXP_HISTORICO_POR_CONFIRMAR` | La factura `MOD-1002` está pagada sin movimiento. Filtra por proveedor `DEMO-PROV-ACT` |
| El puerto 5432 está ocupado | Cambia `PG_PORT` en `.env.demo.local` y repite `preparar` |
| El PDF de la cotización/OC no se ve | Ábrelo en un navegador normal; en pruebas automáticas no hay visor de PDF |

## Practicar la presentación y volver a empezar

Cuantas veces quieras, sin perder el estado de ensayo:

| Quiero… | Comando |
|---|---|
| Volver al estado de ensayo (borra lo que hiciste practicando) | `tools/demo/restablecer-demo.sh` y luego reiniciar la API |
| Ver cuántos registros hay ahora y cuándo se hizo la copia | `tools/demo/restablecer-demo.sh --estado` |
| Guardar **el estado actual** como nueva base de práctica | `tools/demo/restablecer-demo.sh --guardar` (deja `base-practica.dump.anterior`) |
| Comprobar usuarios, roles y permisos | `tools/demo/verificar-usuarios-demo.sh` |

Qué trae el estado de ensayo: el ejemplo terminado (requisición `MID2026-010008`, OC
`OC-MID2026-000007` autorizada, recepción 6 de 10, factura `DEMO-7001` pagada) y los
prerrequisitos (usuarios fio y Mariana con sus roles, periodo contable abierto, rack con
asignaciones, cuenta bancaria `DEMO-CTA-MXN`, `DEMO-PROV-REV` en revisión con 3 de 5
documentos). Para practicar en vivo **crea documentos nuevos** (sección 9) y restablece al
terminar. La API debe estar apagada o reiniciarse después de restablecer.

## Apéndice A. Reconstruir a mano lo que cambió en la base (sin respaldo)

Es lo que Geovany ejecutó sobre `millet_demo` antes del ensayo. Solo hace falta
si no usas el respaldo. Reemplaza los `id` por los de tu base.

```sql
begin;
-- 1) Las pantallas de OC cargan la lista de usuarios: sin este permiso sale el aviso rojo.
insert into identidad.rol_permisos (id,rol_id,permiso_id,version,created_at,updated_at,created_by)
select gen_random_uuid(), r.id, p.id, 1, now(), now(), 'seed-demo'
from identidad.roles r cross join identidad.permisos p
where r.codigo in ('DEMO-ROL-Jefe Compras','DEMO-ROL-Dirección') and p.codigo='identidad.usuarios.leer'
  and not exists (select 1 from identidad.rol_permisos x where x.rol_id=r.id and x.permiso_id=p.id and x.deleted_at is null);

-- 2) fio: de Contabilidad a Jefe de Compras (N1).
update identidad.usuario_empresa_roles
   set rol_id = (select id from identidad.roles where codigo='DEMO-ROL-Jefe Compras'),
       updated_at = now(), updated_by = 'seed-demo'
 where usuario_id = (select id from identidad.usuarios where email like 'fio@%') and deleted_at is null;

-- 3) Mariana: usuario nuevo con rol Dirección y las sucursales de fio.
--    El entra_oid es su "Id. de objeto" de Microsoft Entra (portal de Azure).
do $$
declare nid uuid := gen_random_uuid();
        fio uuid := (select id from identidad.usuarios where email like 'fio@%');
begin
  insert into identidad.usuarios (id,entra_oid,email,nombre,activo,version,created_at,updated_at,created_by,es_cuenta_tecnica,estado_acceso)
  values (nid,'84d84897-6167-4c89-b9e2-7ca9a096a67e','mariana.ortega@uzieltzaboutlook.onmicrosoft.com','Mariana Ortega Valdés',true,1,now(),now(),'seed-demo',false,0);
  insert into identidad.usuario_empresa_roles (id,usuario_id,empresa_id,rol_id,version,created_at,updated_at,created_by)
  select gen_random_uuid(), nid, uer.empresa_id, (select id from identidad.roles where codigo='DEMO-ROL-Dirección'), 1, now(), now(), 'seed-demo'
  from identidad.usuario_empresa_roles uer where uer.usuario_id = fio and uer.deleted_at is null;
  insert into identidad.usuario_sucursales (id,usuario_id,sucursal_id,empresa_id,estatus,version,created_at,updated_at,created_by)
  select gen_random_uuid(), nid, us.sucursal_id, us.empresa_id, us.estatus, 1, now(), now(), 'seed-demo'
  from identidad.usuario_sucursales us where us.usuario_id = fio and us.deleted_at is null;
end $$;
commit;
```

Además, por pantalla y como Geovany: una **ubicación real (rack)** en el
sub-almacén de MID con una **asignación activa** de `ART-CRT-01` y `ART-EST-01`
a ese rack; **datos bancarios** del proveedor `DEMO-PROV-ACT` (banco, CLABE y
beneficiario); una **cuenta bancaria MXN** en Tesorería (`DEMO-CTA-MXN`); y el
**periodo contable 2026-10 abierto**. Todo eso ya viene en el respaldo.

## Apéndice B. Estado de todos los problemas encontrados en el recorrido
Las capturas de esta guía se tomaron con el código de `main`, **antes** de los
arreglos. Los marcados "Corregido" ya están en la rama
`fix/demo-12oct-correcciones`.

| # | Problema | Estado |
|---|---|---|
| 1 | Panel "Capturar factura desde OC" sin scroll: el botón quedaba fuera de pantalla a 1440×900 | **Corregido** (medido en navegador: botón en y=850, clicable) |
| 2 | A quien creó la OC se le ofrecía "Aprobar Nivel 1/2"; tras firmar N1 se ofrecía N2 | **Corregido**: botones deshabilitados con motivo |
| 3 | Sub-estados de la OC con "~50 %" fijo y sin dato real | **Corregido**: Recepción/Facturación muestran el avance real; Pago solo el estado |
| 4 | Barra fija tapaba la fila "Recepción" de la OC y la primera fila del detalle de requisición | **Corregido** (medido: cabecera 76–127 px, fila en 167–183 px) |
| 5 | Botón flotante de isla con palmera tapaba botones | **Corregido**: era el devtools de React Query; ahora apagado por defecto (`VITE_QUERY_DEVTOOLS=true` lo enciende) |
| 6 | Estado en crudo "EnSurtido" en la trazabilidad | **Corregido**: se muestra "En surtido" |
| 7 | Hora del evento "Creado" corrida 4 horas | **No era error**: la OC se creó a las 00:09:59 (hora de Mérida) y la pantalla dice 12:09:59 a.m. |
| 8 | fio ve "Aprobar Nivel 2" habilitado tras su N1 **en la requisición** | **No es error**: la requisición no tiene regla de segregación en el dominio; solo la OC la tiene |
| 9 | "Couldn't load plugin" en el PDF de la cotización y de la OC | **Por verificar**: probablemente el navegador sin interfaz no trae visor de PDF; abrirlo una vez en uno normal |
| 10 | Reportes Cartera y Antigüedad de CxP: 422 `CXP_HISTORICO_POR_CONFIRMAR` sin filtrar por proveedor | **Pendiente (dato)**: la factura `MOD-1002` está pagada sin movimiento. Filtrar por `DEMO-PROV-ACT` |
| 11 | Historial y Autorización de la OC con textos técnicos ("Creó AutorizacionOC…", `EnAutorizacionJefeCompras`) | **Corregido**: "Creó la firma de autorización", "Se creó la orden de compra"; el estado sale en palabras (sin tilde); el id crudo queda solo en el tooltip |
| 12 | Texto rojo "Selecciona la línea de OC…" visible tras elegir la línea, en la captura de factura | **Corregido**: se validaba al elegir la OC, antes de enviar; ahora solo tras intentar enviar y desaparece al elegir la línea |
| 13 | El selector de CFDI de la recepción solo trae RFC genérico, ninguno del proveedor | **Pendiente (dato)**: usar el folio fiscal manual |
| 14 | Saldos de Almacén: "INSUMOS" sin el almacén y `DEMO-ART-PIEZA` repetido 3 veces | **Corregido**: ahora "ALM-MID-G · INSUMOS" |
| 15 | Pestaña Líneas de la OC muy ancha a 1440 px (precio, subtotal e IVA fuera de vista) | **Pendiente** (cosmético): usar zoom 90 % o desplazarse |
| 16 | Tabla de líneas de la requisición sin columna de centro de costo; "Servicio" preseleccionado como clasificación | **Pendiente** (cosmético): el CeCo se ve en el formulario de la línea |
| 17 | "Eliminar" visible en una requisición ajena en autorización | **Pendiente**: el backend decide si lo permite; no pulsarlo |
| 18 | Tesorería muestra el folio sin serie (`7001`), el pago sin folio propio y conceptos "(ejemplo por validar)" | **Pendiente** (cosmético/dato) |
| 19 | `DEMO-CTA-MXN` con saldo negativo; deja pagar sin fondos | **Pendiente (dato)** |
| 20 | `DEMO-PROV-REV` bloqueado: faltan 2 de 5 documentos | **Esperado**: sirve para mostrar el bloqueo, no se puede usar en una OC |
| 21 | Con elegible 0, Tesorería responde `PASIVO_NO_PAGABLE_CXP` (no `PAGO_EXCEDE_ELEGIBLE`) | **Corregido en la guía** |
| 22 | Maestros: el aviso de "posible duplicado / RFC genérico" salía en **toda** alta de proveedor | **Corregido** (el aviso solo sale si el backend detecta duplicado o RFC genérico) |
| 23 | Maestros: contadores de la sincronización de productos inconsistentes (Errores 1 pero 2 listados; "Pendientes 1" sin explicación) | **Pendiente** (cosmético) |
| 24 | Maestros: texto interno "Por validar con Millet (V37, Eliam 10-oct-2026)" en las clasificaciones de clientes | **Pendiente** (cosmético; no abrirlo en la demo) |
| 25 | Maestros: la bitácora muestra enums e ids crudos ("EnRevision → Activo", "ProductoAw 01a12c5c") | **Pendiente** (cosmético) |
| 26 | Maestros: cliente AW-1 con condición de pago `<indf>` y moneda `PESOSMX` crudas | **Pendiente (dato)** de la copia de A+W; mapeos pendientes de Millet |
| 27 | Maestros: la medida `1800x2600` del guion no existe en la copia (existen `1830x2440` y `2440x3660`) | **Dato**: usar esas medidas en la demo |
| 28 | Maestros: la sincronización da "0 creados" y 1–2 errores | **Esperado**: ya estaba sincronizado; los errores (`NAME1 vacío`, `descripcion vacía`, `UNIDAD_SIN_EQUIVALENCIA`) sirven para explicar causa y acción sugerida |

Los toasts verdes que tapan unos segundos la barra superior no son un error.
