#!/usr/bin/env bash

set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$script_dir/.." && pwd)"

cd "$repo_root/backend"
dotnet restore Millet.sln
dotnet build Millet.sln --configuration Release --no-restore --nologo

for project in tests/*UnitTests/*.csproj; do
  dotnet test "$project" \
    --configuration Release \
    --no-build \
    --no-restore \
    --nologo
done

cd "$repo_root/frontend"
npm install --ignore-scripts --no-audit --no-fund
npm run lint
npm run build
npm test

printf '\nGate local aprobado: backend unitario + frontend lint/build/tests.\n'
printf 'Para integración con PostgreSQL aislado: ./tools/validate-integration-isolated.sh\n'
