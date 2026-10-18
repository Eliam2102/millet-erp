# Demo local en otra Mac

Sirve para levantar la demo del ERP en cualquier Mac (por ejemplo, la M1 de presentación) solo con lo que está en `main`. Se entra únicamente con cuenta de Microsoft: no hay acceso de prueba.

## 1. Una sola vez: instala

- **Docker:** OrbStack o Docker Desktop, abierto.
- **.NET SDK 10:** `dotnet --list-sdks` debe mostrar una versión 10.x.
- **Node 22 o superior** y **python3**.
- **Git** con acceso al repositorio.

## 2. Baja el código

```bash
git clone https://github.com/Eliam2102/millet-erp.git
cd millet-erp
```

Si ya lo tienes clonado: `git checkout main && git pull`.

## 3. Configura lo privado (no está en GitHub)

```bash
cp tools/demo/.env.demo.example tools/demo/.env.demo.local
```

Llena `tools/demo/.env.demo.local` con los mismos valores de la Mac donde ya funciona. Cópialos tú, a mano o desde tu gestor de contraseñas; no van por chat ni por correo:

| Variable | Dónde está en la Mac que ya funciona |
|---|---|
| `Auth__EntraId__TenantId`, `ClientId`, `Audience` | El archivo de arranque de la API de la demo (variables `Auth__…`) |
| `Auth__EntraId__ClientSecret` | `cd backend/src/Api && dotnet user-secrets list` |
| `VITE_ENTRA_*`, `VITE_API_AUDIENCE` | `frontend/.env.development.local` |
| `Auth__InitialAdminEntraOid` | Tu «Id. de objeto» en Microsoft Entra (portal.azure.com) |
| `IntegracionesFiscal__Sdk__*` (opcional, timbrado de pruebas) | `dotnet user-secrets list` |

El inicio de sesión de Microsoft ya acepta `http://localhost:5173`, así que funciona igual en cualquier Mac.

## 4. Prepara la demo

```bash
tools/demo/demo.sh preparar
```

Con los datos de Geovany (clientes, productos y proveedores con lo sensible alterado), pasa la ruta del paquete. No lo subas al repositorio y bórralo al terminar:

```bash
tools/demo/demo.sh preparar ~/Downloads/paquete-demo-real.tar.gz
```

## 5. Arranca (tres terminales)

```bash
tools/demo/demo.sh eventos
```

```bash
tools/demo/demo.sh api
```

```bash
tools/demo/demo.sh web
```

`eventos` levanta el emulador de Service Bus, para que los módulos se pasen los eventos: la recepción llega a Cuentas por pagar, el pasivo a Tesorería, etc. La primera vez descarga unos 750 MB y pide aceptar la licencia de desarrollo de Microsoft.

Revisa que todo responda:

```bash
tools/demo/demo.sh estado
```

Abre http://localhost:5173 y entra con «Continuar con Microsoft».

Para abrir dos sesiones simultáneas, usa **una cuenta por perfil de Chrome**; para las tres cuentas del recorrido, abre tres perfiles. Las pestañas del mismo perfil funcionan con `sessionStorage` separado para la aplicación, pero comparten la cookie de Microsoft y pueden reutilizar la misma cuenta de Entra.

El pie del menú de usuario muestra `Versión 5ee7270 · Desarrollo` (ejemplo). `GET /api/version`, anónimo, devuelve commit, fecha de compilación UTC y ambiente. `demo.sh api` incorpora el commit con `-p:SourceRevisionId=$(git rev-parse --short HEAD)` al compilar. Si la API de versión falla, el menú sigue operativo. Un cambio local sin commit no se distingue por este identificador.

## 6. Antes de presentar

- **Clientes y productos de A+W:** en Clientes → Sincronizar, elige «Copia de demo» y pulsa «Iniciar sincronización». Repite en Productos A+W.
- **Cuentas de demo:** la primera vez que cada cuenta entra, se crea sin perfil. `Seed:DemoSesion:Usuarios` configura un `Rol` y una lista de `Sucursales` por correo (por ejemplo, `Usuarios__1__Rol='Jefe Compras y CxP'` y `Usuarios__1__Sucursales__0=MID`). Hay **un solo rol por cuenta y empresa**, conforme a [ADR-0053](../../docs/decisiones/0053-permisos-personalizados-por-usuario.md) y [ADR-0051](../../docs/decisiones/0051-segmentacion-de-datos-por-sucursal.md). Roles válidos: Compras, Jefe Compras, Dirección, Jefe Compras y CxP, Dirección y Tesorería, CxP, Tesorería, Facturación, Contabilidad, DAF y Administrador. Compras exige solo MID. Los roles combinados reúnen los permisos de sus dos perfiles; no ganan acceso a todas las sucursales ni el nivel de firma ajeno.
- **Reasignación opcional:** por omisión `Seed:DemoSesion:ReemplazarAsignaciones=false`: solo asigna un rol cuando la cuenta no tiene ninguno y sucursales cuando no tiene asignaciones. Con `true`, solo en Development + `Habilitado=true`, deja exactamente una asignación de rol, la configurada, y reemplaza las sucursales activas de cada correo listado en la empresa DEMO; registra antes → después. Si una cuenta tiene `super-admin` en la empresa DEMO o es el administrador inicial (`Auth:InitialAdminEntraOid`), falla sin cambiar sus asignaciones: «el administrador queda fuera de las cuentas del ensayo». Nunca quita `super-admin`. Conserva excepciones individuales y permisos editados de los roles. Volver a `false` tras configurar y cerrar/abrir sesión para renovar permisos.
- **Compra a pago con personas distintas:** usar tres cuentas operativas de Microsoft y el administrador inicial aparte. A: Compras (incluye Almacén), MID; B: Jefe Compras y CxP, MID; C: Dirección y Tesorería, MID. Cada cuenta recibe un solo rol. A captura y recibe; B firma N1, liga factura y concilia; C firma N2 y registra pago. Ensayar los rechazos de autoautorización y doble firma con las reglas actuales de OC. El bloque comentado en `.env.demo.example` usa solo correos ficticios; las cuentas técnicas sembradas no inician sesión. Conservar la razón social editada y los permisos no acredita este recorrido integrado.
- **Pedidos de A+W en Facturación:** las sucursales necesitan su clave de A+W (`CIRCUITO`, `CANCUN`), según el LEEME del paquete.

## Si algo falla

- **No llega el correo de acceso:** la configuración local lo captura en Mailpit, no en Outlook/Gmail. `preparar` levanta el buzón; si ya preparaste la demo, ejecuta `tools/demo/demo.sh correos`, abre http://localhost:8025 y pulsa «Reenviar acceso» en el ERP. El destinatario es el correo de contacto del empleado. Para entrega real se requiere `Entra__Proveedor=Graph`, `Entra__Correo__Proveedor=Graph`, un buzón remitente en `Auth__EntraId__SenderEmail` y permiso `Mail.Send` de la aplicación en Microsoft Graph.
- **«Docker no está corriendo»:** abre OrbStack o Docker Desktop.
- **El puerto 5432 está ocupado:** cambia `PG_PORT` en `.env.demo.local` (por ejemplo a 5434) y vuelve a correr `preparar`.
- **La pantalla dice que tu cuenta no tiene empresa ni roles:** revisa `Auth__InitialAdminEntraOid` y reinicia la API.

## Configuración incluida para PRU-A

La función `api` de `demo.sh` exporta estos valores públicos por omisión. Un valor definido por el operador conserva precedencia; no contienen contraseñas ni llaves:

```sh
IntegracionesAw__Clientes__LecturaHabilitada=true
IntegracionesAw__Clientes__AplicacionHabilitada=true
IntegracionesAw__Productos__OrigenHabilitado=true
IntegracionesAw__Clientes__MapeoMoneda__PESOSMX=MXN
IntegracionesAw__Clientes__MapeoMoneda__USD=USD
IntegracionesAw__Clientes__MapeoMoneda__Euro=EUR
Facturacion__ReppAutomatico__SucursalClave=MID
```

La copia creada por `seed-aw-copy.sql` usa `PESOSMX`, `USD` y `Euro`. Si el paquete recibido usa otras claves, agrega su equivalencia ISO. Las banderas habilitan lectura y aplicación de clientes y el origen de productos; la sincronización se inicia desde las pantallas correspondientes.

El nombre histórico de la opción `ReppAutomatico` solo configura la sucursal emisora del cobro bancario. **El REP se emite manualmente desde Pendientes de REP** (decisión U1.1, #52). Confirmar un depósito crea el pendiente; no timbra. CXC-07 se documenta como comportamiento acordado.

En la bandeja, «Emitir seleccionados» informa si falta la sucursal emisora. Corrige la configuración antes de reintentar. La fecha de cierre de septiembre se registra con el instante real del sembrado. Las fechas fijas de operaciones ficticias conservan el escenario de demo.

`.env.demo.example` trae el mismo bloque comentado; solo hace falta descomentarlo para cambiar un valor.
