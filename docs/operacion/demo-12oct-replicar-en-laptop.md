# Replicar la demo del 12-oct en otra laptop (Compras, Almacén, CxP y Tesorería)

> Para Eliam. Con esto levantas **exactamente el estado** en que Geovany
> ensayó la demo: mismo código, mismos usuarios y roles, mismos datos. Se apoya
> en el procedimiento oficial de `tools/demo/README.md` y le agrega lo que ese
> README no cubre: la rama con los arreglos, el respaldo de la base y las
> cuentas y datos que el recorrido necesita.
>
> Los pasos de pantalla, campo por campo, están en `docs/operacion/demo-guia/`
> (`01-compras.md`, `02-almacen.md`, `03-cxp-tesoreria.md`).

## 0. Qué necesitas recibir de Geovany (no va en GitHub)

| Pieza | Para qué | Cómo se pasa |
|---|---|---|
| `millet_demo-2026-10-12.dump` (1 MB) | La base de la demo completa: usuarios, roles, requisición/OC/recepción/factura/pago de ejemplo | Por un canal privado (AirDrop, USB, gestor de archivos). **No lo subas a GitHub**: trae correos y OID del equipo y datos de la copia de A+W |
| Valores de Entra (`TenantId`, `ClientId`, `Audience`, secreto) | Inicio de sesión con Microsoft | Gestor de contraseñas. Ver `tools/demo/README.md`, sección 3 |
| Tu «Id. de objeto» de Microsoft | Variable `Auth__InitialAdminEntraOid`: te vuelve super administrador | Portal de Azure → Microsoft Entra ID → Usuarios |
| `CFDI DEMO-7001.xml` (opcional) | Solo si quieres repetir la captura de factura con ese mismo CFDI | Está en `.verification/demo-tour/cxp-tesoreria/` de la laptop de Geovany |

Huella del respaldo para comprobar que llegó íntegro:
`sha256 a2d5cbc4730588de34eed00fb6e13813e1a56254c9c0a7b72446ff98dec69b67`
(`shasum -a 256 millet_demo-2026-10-12.dump`).

## 1. Requisitos (una sola vez)

Docker (OrbStack o Docker Desktop abierto), .NET SDK 10, Node 22+, python3 y
Git con acceso al repo. Detalle en `tools/demo/README.md`, sección 1.

## 2. Baja la rama correcta

Los arreglos de esta demo están en la rama
`feature/acceso-todas-sucursales-ui`, no en `main`:

```bash
git clone https://github.com/Eliam2102/millet-erp.git   # si no lo tienes
cd millet-erp
git fetch origin
git checkout feature/acceso-todas-sucursales-ui
git pull
```

Qué trae esa rama respecto de `main` (commits separados por tema):
1. **Acceso a todas las sucursales** desde el alta de empleado y "Dar acceso",
   con el interruptor en el panel de permisos personalizados, y la sección
   "Cuentas de acceso" oculta del menú de Admin.
2. **Pruebas estabilizadas** (mocks de Vitest, aislamiento de pruebas de
   integración).
3. **Arreglos de la demo**: el panel "Capturar factura desde OC" ya hace
   scroll; la OC no ofrece firmar a quien la creó o ya firmó N1; el avance de
   recepción deja de decir "~50 %" fijo; y la cabecera fija de los detalles de
   OC y de requisición ya no tapa el primer bloque (se verificó midiendo en el
   navegador: la fila "Recepción parcial (20 %)" queda visible).
4. **Documentación** de la demo.

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

Esto **reemplaza** `millet_demo` por la de Geovany. Haz esto con la API
**detenida**:

```bash
# desde la carpeta donde dejaste el .dump
docker exec millet-dev-postgres psql -U pgadmin -d postgres \
  -c "DROP DATABASE IF EXISTS millet_demo WITH (FORCE)"
docker exec millet-dev-postgres psql -U pgadmin -d postgres \
  -c "CREATE DATABASE millet_demo"
docker exec -i millet-dev-postgres pg_restore -U pgadmin -d millet_demo \
  --no-owner --no-privileges < millet_demo-2026-10-12.dump
```

Comprueba que quedó completa:

```bash
docker exec millet-dev-postgres psql -U pgadmin -d millet_demo -Atc "
select 'usuarios', count(*) from identidad.usuarios union all
select 'requisiciones', count(*) from compras.requisiciones union all
select 'ordenes', count(*) from compras.ordenes_compra union all
select 'facturas', count(*) from cuentas_por_pagar.facturas_proveedor union all
select 'ubicaciones', count(*) from almacen.ubicaciones"
```

Debe dar: `usuarios 8`, `requisiciones 9`, `ordenes 9`, `facturas 7`,
`ubicaciones 15`.

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
4. Crea una requisición nueva (paso 2 de `01-compras.md`) para comprobar que
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
