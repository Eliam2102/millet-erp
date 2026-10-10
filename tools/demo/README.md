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

## 6. Antes de presentar

- **Clientes y productos de A+W:** en Clientes → Sincronizar, elige «Copia de demo» y pulsa «Iniciar sincronización». Repite en Productos A+W.
- **Cuentas de demo:** la primera vez que cada cuenta entra, se crea sin perfil. Su perfil y sus sucursales se asignan con `Seed:DemoSesion:Usuarios`; al reiniciar la API, cada cuenta queda con su papel.
- **Pedidos de A+W en Facturación:** las sucursales necesitan su clave de A+W (`CIRCUITO`, `CANCUN`), según el LEEME del paquete.

## Si algo falla

- **«Docker no está corriendo»:** abre OrbStack o Docker Desktop.
- **El puerto 5432 está ocupado:** cambia `PG_PORT` en `.env.demo.local` (por ejemplo a 5434) y vuelve a correr `preparar`.
- **La pantalla dice que tu cuenta no tiene empresa ni roles:** revisa `Auth__InitialAdminEntraOid` y reinicia la API.
