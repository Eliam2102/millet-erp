# Hook PreToolUse para Bash: valida auto-merge y operaciones cross-branch.
# Bloquea con exit 2 + mensaje en stderr si la operacion no es segura.
# Pass-through (exit 0) si todo OK.
#
# Configurado en .claude/settings.json como PreToolUse hook para Bash.

$ErrorActionPreference = 'Continue'

function Deny([string]$msg) {
    [Console]::Error.WriteLine($msg)
    exit 2
}

# Leer JSON de stdin
$rawInput = [Console]::In.ReadToEnd()
if (-not $rawInput) { exit 0 }

try {
    $payload = $rawInput | ConvertFrom-Json
} catch {
    # Si no parsea, no bloquear
    exit 0
}

$command = $payload.tool_input.command
if (-not $command) { exit 0 }

# Branch actual
$branch = ''
try {
    $branch = (git branch --show-current 2>$null)
    if ($branch) { $branch = $branch.Trim() }
} catch {
    $branch = ''
}

$isAutoBranch = $branch -match '^(compras/oc(-uf)?-|admin/|cxp/|cxp-fe/|almacen/|almacen-fe/|facturacion/|facturacion-fe/|cxc/|cxc-fe/|tesoreria/|tesoreria-fe/|centros-costo/|centros-costo-fe/)'

# REGLA 1: si es comando de merge a main, validar branch + CI
if ($command -match '^gh pr merge') {
    if (-not $isAutoBranch) {
        Deny "Auto-merge solo aplica a branches compras/oc-*, compras/oc-uf-*, admin/*, cxp/*, cxp-fe/*, almacen/*, almacen-fe/*, facturacion/*, facturacion-fe/*, cxc/*, cxc-fe/*, tesoreria/*, tesoreria-fe/*, centros-costo/* y centros-costo-fe/*. Branch actual: '$branch'. Si quieres mergear manualmente, pide permiso explicito al usuario antes de ejecutar este comando."    }

    # Validar que el branch no este atras de origin/main (otro PR pudo mergear despues del ultimo rebase)
    git fetch origin main --quiet 2>$null | Out-Null
    $behind = 0
    try {
        $behind = [int](git rev-list --count 'HEAD..origin/main' 2>$null)
    } catch {
        $behind = 0
    }
    if ($behind -gt 0) {
        Deny "El branch '$branch' esta $behind commit(s) atras de origin/main. Rebasea primero: git fetch origin; git rebase origin/main; git push --force-with-lease origin $branch. Espera a que CI vuelva a correr en verde y reintenta el merge."
    }

    # Validar que CI este verde
    $checksJson = ''
    try {
        $checksJson = (gh pr checks --json state,name,bucket 2>$null) -join "`n"
    } catch {
        Deny "No se pudo consultar 'gh pr checks'. Verifica que el PR existe (gh pr view) y que gh esta autenticado. Detalle: $_"
    }

    if (-not $checksJson -or $checksJson.Trim() -eq '') {
        Deny "'gh pr checks' no devolvio resultados. El PR puede no existir todavia o no tiene checks configurados. Verifica con 'gh pr view'."
    }

    try {
        $checks = $checksJson | ConvertFrom-Json
    } catch {
        Deny "No se pudo parsear la respuesta de 'gh pr checks'. Output crudo: $checksJson"
    }

    if (-not $checks -or $checks.Count -eq 0) {
        Deny "El PR no tiene checks configurados (lista vacia). Auto-merge bloqueado por seguridad. Confirma con el usuario antes de mergear manualmente."
    }

    # Buckets posibles: pass, fail, pending, skipping, cancel
    $blocking = @($checks | Where-Object { $_.bucket -in 'fail', 'pending', 'cancel' })

    if ($blocking.Count -gt 0) {
        $names = ($blocking | ForEach-Object { "$($_.name) [$($_.bucket)]" }) -join ', '
        Deny "CI no esta verde. Checks bloqueando: $names. Espera a que termine (gh pr checks --watch) o investiga la falla (gh run view) antes de mergear."
    }

    # All clear
    exit 0
}

# REGLA 2: bloquear push directo a main (defensa adicional al settings.deny)
if ($command -match '^git push.*\bmain\b' -and $command -notmatch 'origin (compras|admin|cxp|cxp-fe|almacen|almacen-fe|facturacion|facturacion-fe|cxc|cxc-fe|tesoreria|tesoreria-fe|centros-costo|centros-costo-fe)/') {

    Deny "Push directo a main bloqueado. Usa un branch feature compras/oc-*, compras/oc-uf-*, admin/*, cxp/*, cxp-fe/*, almacen/*, almacen-fe/*, facturacion/*, facturacion-fe/*, cxc/*, cxc-fe/*, tesoreria/*, tesoreria-fe/*, centros-costo/* o centros-costo-fe/* y abre PR."
 }

# Resto pass-through
exit 0
