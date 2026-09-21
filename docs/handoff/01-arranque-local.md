# Arranque local reproducible

## Prerrequisitos

- Git.
- .NET SDK 9.x.
- Node.js 22.x y npm.
- Docker Desktop con Compose.
- Bash en macOS/Linux/WSL/Git Bash o PowerShell 5.1+ en Windows.

## Ruta automatizada recomendada

macOS, Linux, WSL o Git Bash:

```bash
./tools/setup-dev.sh
```

Windows PowerShell:

```powershell
.\tools\setup-dev.ps1
```

Ambas rutas levantan PostgreSQL, restauran y compilan el backend, aplican el
mismo manifiesto canónico de 12 migraciones e instalan el frontend. Ninguna
requiere Azure ni Entra ID real para el arranque local.

## 1. Clonar y comprobar rama

```bash
git clone https://github.com/Eliam2102/millet-erp.git
cd millet-erp
git status
```

La rama base es `main`. Al corte del 20/09/2026 el repositorio fue observado
como público; la visibilidad objetivo y los permisos de escritura deben
confirmarse antes del onboarding. Tener lectura no autoriza commits directos a
`main`.

## 2. Levantar PostgreSQL manualmente

Esta sección se usa cuando no se ejecutó el script automatizado o se requiere
controlar el puerto manualmente.

El puerto predeterminado es `5432`. Si está ocupado, usa otro sin editar archivos versionados:

```bash
POSTGRES_PORT=5434 POSTGRES_VOLUME_NAME=millet-dev-pg-data-5434 \
  docker compose -f docker-compose.dev.yml up -d
```

Para el puerto alterno, exporta la conexión del backend en esa terminal:

```bash
export ConnectionStrings__Postgres='Host=localhost;Port=5434;Database=millet_dev;Username=pgadmin;Password=pgadmin;Include Error Detail=true'
```

En PowerShell:

```powershell
$env:POSTGRES_PORT = '5434'
$env:POSTGRES_VOLUME_NAME = 'millet-dev-pg-data-5434'
docker compose -f docker-compose.dev.yml up -d
$env:ConnectionStrings__Postgres = 'Host=localhost;Port=5434;Database=millet_dev;Username=pgadmin;Password=pgadmin;Include Error Detail=true'
```

Estos valores son exclusivamente locales. No reutilizarlos fuera de desarrollo.

## 3. Restaurar y compilar backend

```bash
cd backend
dotnet tool restore
dotnet restore Millet.sln
dotnet build Millet.sln --nologo
```

## 4. Aplicar migraciones

Desde `backend/`, aplicar en el orden siguiente:

```bash
contexts=(
  'CompartidoDbContext|src/Compartido/Millet.Compartido.csproj'
  'CoreDbContext|src/SharedKernel/Millet.SharedKernel.csproj'
  'IdentidadDbContext|src/Identidad/Millet.Identidad.csproj'
  'ComprasDbContext|src/Compras/Millet.Compras.csproj'
  'IntegracionesAwDbContext|src/Integraciones.Aw/Millet.Integraciones.Aw.csproj'
  'IntegracionesFiscalDbContext|src/Integraciones.Fiscal/Millet.Integraciones.Fiscal.csproj'
  'AlmacenDbContext|src/Almacen/Millet.Almacen.csproj'
  'CuentasPorPagarDbContext|src/CuentasPorPagar/Millet.CuentasPorPagar.csproj'
  'FacturacionDbContext|src/Facturacion/Millet.Facturacion.csproj'
  'CuentasPorCobrarDbContext|src/CuentasPorCobrar/Millet.CuentasPorCobrar.csproj'
  'TesoreriaDbContext|src/Tesoreria/Millet.Tesoreria.csproj'
  'CentrosCostoDbContext|src/CentrosCosto/Millet.CentrosCosto.csproj'
)

for item in "${contexts[@]}"; do
  IFS='|' read -r context project <<< "$item"
  dotnet ef database update --context "$context" --project "$project" \
    --startup-project src/Api/Millet.Api.csproj
done
```

En Windows puede utilizarse `tools/setup-dev.ps1`; antes de confiar en su resultado, verificar que reporte los 12 contextos anteriores.

## 5. Instalar frontend

```bash
cd ../frontend
npm install --no-audit --no-fund
npm run build
```

## 6. Ejecutar

Terminal de API:

```bash
cd backend/src/Api
dotnet watch run
```

Terminal de frontend:

```bash
cd frontend
npm run dev
```

Abrir `http://localhost:5173`. En desarrollo local se usa el selector de usuario fake; esto no valida Entra ID real.

El puerto `5173` no es intercambiable en el arranque estándar: la política CORS de desarrollo permite `http://localhost:5173`. Si se usa otro puerto, la pantalla puede abrir pero el login devolverá `Failed to fetch` hasta que el origen se configure explícitamente en la API.

## 7. Verificación mínima

```bash
cd backend
dotnet test Millet.sln --nologo

cd ../frontend
npm test
npm run build
```

La instalación usa `npm install` porque el lockfile contiene variantes de
plataforma y el CI actual resuelve los binarios del sistema operativo en cada
ejecución. Si esto cambia, debe actualizarse de forma coordinada aquí, en el
README y en `.github/workflows/validate-app.yml`.

Las pruebas de integración requieren una base local limpia, con los 12 contextos ya migrados, y la variable `ConnectionStrings__Postgres` apuntando al puerto correcto. Deben ejecutarse en configuración **Debug**, porque `/api/dev/fake-login` está protegido por `#if DEBUG`; en Release las pruebas que autentican por esa ruta reciben 404. Para el gate integral compartido, ejecutar los proyectos en serie (`dotnet test Millet.sln -m:1`): varios proyectos de integración paralelos sobre la misma base pueden contaminar sus datos. Un error `28P01 password authentication failed` normalmente indica que se conectó a otra instancia PostgreSQL, no que una prueba funcional haya fallado.

Para evitar colisiones de puerto y residuos de una base previa, desde la raíz puede ejecutarse:

```bash
./tools/validate-integration-isolated.sh
```

El gate local portable, sin las integraciones de PostgreSQL, es:

```bash
./tools/validate-local.sh
```

En Windows PowerShell:

```powershell
.\tools\validate-local.ps1
```

El script crea un PostgreSQL temporal en un puerto libre, aplica los 12 contextos, ejecuta las tres suites en serie y elimina el contenedor al terminar.

## 8. Detener

```bash
docker compose -f docker-compose.dev.yml down
```

`down -v` elimina la base local. Usarlo sólo cuando se quiera reconstruir el entorno desde cero y se haya confirmado que el volumen es exclusivamente de desarrollo.
