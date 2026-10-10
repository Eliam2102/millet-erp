using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Catalogos.Domain;
using Millet.Contabilidad.Domain;
using Millet.Contabilidad.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Npgsql;

namespace Millet.Contabilidad.Application.Catalogo;

public sealed record OrigenCuentaDto(string Fuente, string CodigoOrigen);

public sealed record CuentaResponse(
    Guid Id, string Codigo, string Nombre, Guid? PadreId, int Nivel, NaturalezaCuenta? Naturaleza, TipoCuenta? Tipo,
    string Estatus, bool Activa, CuentaControl CuentaControl, string? CodigoAgrupador, string? GrupoReporte,
    bool PendienteValidacion, int Version, ClaseCuenta Clase, Guid? RubroId,
    bool? Usada = null, IReadOnlyList<OrigenCuentaDto>? Origenes = null, bool NoAfectableManual = false, Guid? SolicitudId = null)
{
    public static CuentaResponse De(CuentaContable c, bool? usada = null, IReadOnlyList<OrigenCuentaDto>? origenes = null) => new(
        c.Id, c.Codigo, c.Nombre, c.PadreId, c.Nivel, c.Naturaleza, c.Tipo, c.Estatus.ToString(), c.Activa,
        c.CuentaControl, c.CodigoAgrupador, c.GrupoReporte, c.PendienteValidacion, c.Version, c.Clase, c.RubroId, usada, origenes, c.NoAfectableManual);
}

/// <summary>Reglas R1–R9 que dependen del árbol y de la configuración (compartidas por crear/editar/baja).</summary>
internal sealed class PoliticaCatalogo(ContabilidadDbContext db, FormatoCatalogo formato)
{
    public CatalogoOpciones Opciones => formato.Opciones;

    public string NormalizarCodigo(string codigo)
    {
        var c = FormatoCatalogo.Codigo(codigo);
        var motivo = c is null ? "el código está vacío" : formato.MotivoCodigoInvalido(c);
        return motivo is null ? c! : throw new BusinessRuleException("CONTAB_CUENTA_CODIGO_INVALIDO", $"Código inválido: {motivo}.");
    }

    public bool Deriva => Opciones.Tipo.DerivarPorJerarquia;

    /// <summary>
    /// R3: el padre existe (de esta empresa, por el filtro) y está activo. Con tipo derivado (P20) un padre afectable se acepta
    /// (se convierte con <see cref="ConvertirPadreSiAfectableAsync"/>); sin derivación sigue rechazándose.
    /// </summary>
    public async Task<CuentaContable?> ValidarPadreAsync(Guid? padreId, CancellationToken cancellationToken)
    {
        if (padreId is null) return null;
        var padre = await db.Cuentas.AsNoTracking().FirstOrDefaultAsync(c => c.Id == padreId, cancellationToken)
            ?? throw new BusinessRuleException("CONTAB_CUENTA_PADRE_INVALIDO", "La cuenta padre no existe.");
        if (!padre.Activa)
            throw new BusinessRuleException("CONTAB_CUENTA_PADRE_INVALIDO", $"La cuenta padre {padre.Codigo} está inactiva.");
        if (padre.EsRubro)
            throw new BusinessRuleException("CONTAB_CUENTA_PADRE_INVALIDO",
                $"{padre.Codigo} es un rubro de reporte: agrupa cuentas de nivel 1, pero no tiene cuentas debajo en el árbol. Elija una cuenta como padre.");
        if (!Deriva && padre.Tipo == TipoCuenta.Afectable)
            throw new BusinessRuleException("CONTAB_CUENTA_PADRE_NO_ES_TITULO", $"La cuenta padre {padre.Codigo} es afectable; solo un título puede tener hijas.");
        return padre;
    }

    /// <summary>
    /// P20: dar una hija a una cuenta afectable la convierte en acumulativa, salvo que ya tenga movimientos o sea colectiva.
    /// Una afectable no tiene hijas (R4), así que el subárbol con uso a revisar es ella misma.
    /// </summary>
    public async Task ConvertirPadreSiAfectableAsync(CuentaContable? padre, CancellationToken cancellationToken)
    {
        if (!Deriva || padre is null || padre.Tipo == TipoCuenta.Titulo) return;
        if (padre.CuentaControl != CuentaControl.Ninguna)
            throw new BusinessRuleException("CONTAB_CUENTA_CONTROL_SOLO_AFECTABLE",
                $"La cuenta {padre.Codigo} es colectiva y debe seguir recibiendo los movimientos de su módulo: no puede tener cuentas debajo. Quite primero la marca de cuenta colectiva.");
        if (await db.Usos.AnyAsync(u => u.CuentaId == padre.Id, cancellationToken))
            throw new BusinessRuleException("CONTAB_CUENTA_CAMBIO_BLOQUEADO_POR_USO",
                $"La cuenta padre {padre.Codigo} ya tiene movimientos: no puede pasar a acumular para recibir cuentas debajo, porque sus saldos quedarían en una cuenta que ya no recibe movimientos. "
                + "Cree la cuenta en otra rama o use el procedimiento de impacto (reclasificación aprobada por Contabilidad).");
        var rastreado = db.Cuentas.Local.FirstOrDefault(c => c.Id == padre.Id)
            ?? await db.Cuentas.FirstAsync(c => c.Id == padre.Id, cancellationToken);
        rastreado.ConvertirEnAcumulativa();
    }

    /// <summary>P24: el rubro indicado existe (de esta empresa) y es un rubro.</summary>
    public async Task<Guid?> ValidarRubroAsync(Guid? rubroId, CancellationToken cancellationToken)
    {
        if (rubroId is null) return null;
        var r = await db.Cuentas.AsNoTracking().FirstOrDefaultAsync(c => c.Id == rubroId, cancellationToken);
        return r is { EsRubro: true } ? r.Id
            : throw new BusinessRuleException("CONTAB_CUENTA_RUBRO_INVALIDO", "El rubro indicado no existe o no es un rubro de reporte.");
    }

    public void ValidarNivelYHerencia(int nivel, NaturalezaCuenta? naturaleza, CuentaContable? padre)
    {
        if (nivel > Opciones.NivelMaximo)
            throw new BusinessRuleException("CONTAB_CUENTA_NIVEL_EXCEDIDO", $"El nivel {nivel} excede el máximo configurado ({Opciones.NivelMaximo}).");
        if (Opciones.HerenciaNaturaleza && padre?.Naturaleza is { } pn && naturaleza is { } n && pn != n)
            throw new BusinessRuleException("CONTAB_CUENTA_NATURALEZA_INVALIDA", "La naturaleza no es coherente con la del padre (HerenciaNaturaleza activa).");
    }

    /// <summary>Alta manual: el código debe pertenecer a la rama del padre (opción 2; configurable, encendida por defecto).</summary>
    public void ValidarRama(string codigo, CuentaContable? padre)
    {
        if (padre is null || !Opciones.Jerarquia.ExigirCodigoEnRamaDelPadre || formato.EstaEnRama(codigo, padre.Codigo)) return;
        var sugerido = formato.PrefijoSignificativo(padre.Codigo);
        throw new BusinessRuleException("CONTAB_CUENTA_CODIGO_FUERA_DE_RAMA",
            $"El código {codigo} no corresponde a la cuenta padre {padre.Codigo}: debe empezar con «{sugerido}» y agregar un solo nivel.");
    }

    /// <summary>Cuenta de control listada en configuración: el catálogo marca el tipo (§20.1 regla 1).</summary>
    public CuentaControl ResolverControl(string codigo, CuentaControl solicitado)
    {
        var cfg = Opciones.CuentasControl.FirstOrDefault(c => FormatoCatalogo.Codigo(c.Codigo) == codigo);
        if (cfg is null) return solicitado;
        var tipo = Enum.Parse<CuentaControl>(cfg.Tipo);
        return solicitado is not CuentaControl.Ninguna && solicitado != tipo
            ? throw new BusinessRuleException("CONTAB_CUENTA_CONTROL_CONFLICTO", "El tipo de control contradice CuentasControl de la configuración.")
            : tipo;
    }

    public static bool EsViolacionUnica(DbUpdateException e, string indice) =>
        e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg && pg.ConstraintName == indice;
}

// ─── Crear ───────────────────────────────────────────────────────────────────

public sealed record CrearCuentaCommand(
    string Codigo, string Nombre, Guid? PadreId, NaturalezaCuenta? Naturaleza, TipoCuenta? Tipo,
    CuentaControl CuentaControl, string? CodigoAgrupador, string? GrupoReporte,
    ClaseCuenta Clase = ClaseCuenta.Cuenta, Guid? RubroId = null, bool NoAfectableManual = false) : IRequest<CuentaResponse>;

public sealed class CrearCuentaValidator : AbstractValidator<CrearCuentaCommand>
{
    public CrearCuentaValidator()
    {
        RuleFor(c => c.Codigo).NotEmpty().MaximumLength(30);
        RuleFor(c => c.Nombre).NotEmpty().MaximumLength(254);
        RuleFor(c => c.CodigoAgrupador).MaximumLength(30);
        RuleFor(c => c.GrupoReporte).MaximumLength(60);
    }
}

public sealed class CrearCuentaHandler(ContabilidadDbContext db, FormatoCatalogo formato, SolicitudesCatalogo solicitudes)
    : IRequestHandler<CrearCuentaCommand, CuentaResponse>
{
    private readonly PoliticaCatalogo _p = new(db, formato);

    public Task<CuentaResponse> Handle(CrearCuentaCommand request, CancellationToken cancellationToken) =>
        solicitudes.PrepararCuentaAsync(request, () => AplicarAsync(request, cancellationToken), cancellationToken);

    internal async Task<CuentaResponse> AplicarAsync(CrearCuentaCommand request, CancellationToken cancellationToken, Guid? cuentaId = null)
    {
        var codigo = _p.NormalizarCodigo(request.Codigo);
        // R1 (incluye inactivas, P9): el índice único es la autoridad; esto solo mejora el mensaje.
        if (await db.Cuentas.AsNoTracking().AnyAsync(c => c.Codigo == codigo, cancellationToken))
            throw new ConflictException("CONTAB_CUENTA_CODIGO_DUPLICADO", $"Ya existe una cuenta con el código '{codigo}'.");
        var padre = await _p.ValidarPadreAsync(request.PadreId, cancellationToken);
        var nivel = (padre?.Nivel ?? 0) + 1;
        _p.ValidarNivelYHerencia(nivel, request.Naturaleza, padre);
        _p.ValidarRama(codigo, padre);
        // P19: con derivación el tipo enviado se ignora (una cuenta nueva no tiene hijas).
        var tipo = _p.Deriva ? CuentaContable.DerivarTipo(nivel, tieneHijas: false) : request.Tipo;
        await _p.ConvertirPadreSiAfectableAsync(padre, cancellationToken);

        var cuenta = new CuentaContable(cuentaId ?? Guid.CreateVersion7(), codigo, request.Nombre.Trim(), padre?.Id, nivel,
            request.Naturaleza, tipo, _p.ResolverControl(codigo, request.CuentaControl),
            FormatoCatalogo.Texto(request.CodigoAgrupador), FormatoCatalogo.Texto(request.GrupoReporte), request.Clase, request.NoAfectableManual);
        cuenta.AsignarRubro(await _p.ValidarRubroAsync(request.RubroId, cancellationToken));
        db.Cuentas.Add(cuenta);
        return CuentaResponse.De(cuenta);
    }
}

// ─── Editar (el código es inmutable) ─────────────────────────────────────────

public sealed record EditarCuentaCommand(
    Guid Id, int VersionEsperada, string Nombre, Guid? PadreId, NaturalezaCuenta? Naturaleza, TipoCuenta? Tipo,
    CuentaControl CuentaControl, string? CodigoAgrupador, string? GrupoReporte, Guid? RubroId = null, bool NoAfectableManual = false) : IRequest<CuentaResponse>;

public sealed class EditarCuentaValidator : AbstractValidator<EditarCuentaCommand>
{
    public EditarCuentaValidator()
    {
        RuleFor(c => c.Id).NotEqual(Guid.Empty);
        RuleFor(c => c.Nombre).NotEmpty().MaximumLength(254);
        RuleFor(c => c.CodigoAgrupador).MaximumLength(30);
        RuleFor(c => c.GrupoReporte).MaximumLength(60);
    }
}

public sealed class EditarCuentaHandler(ContabilidadDbContext db, FormatoCatalogo formato, SolicitudesCatalogo solicitudes)
    : IRequestHandler<EditarCuentaCommand, CuentaResponse>
{
    private readonly PoliticaCatalogo _p = new(db, formato);

    public Task<CuentaResponse> Handle(EditarCuentaCommand request, CancellationToken cancellationToken) =>
        solicitudes.PrepararCuentaAsync(request, () => AplicarAsync(request, cancellationToken), cancellationToken);

    internal async Task<CuentaResponse> AplicarAsync(EditarCuentaCommand request, CancellationToken cancellationToken)
    {
        var cuenta = await db.Cuentas.FirstOrDefaultAsync(c => c.Id == request.Id, cancellationToken)
            ?? throw new EntityNotFoundException("CONTAB_CUENTA_NO_ENCONTRADA", $"No existe la cuenta '{request.Id}'.");
        if (cuenta.Version != request.VersionEsperada) throw new ConcurrencyException(nameof(CuentaContable), request.Id);

        var todas = await db.Cuentas.ToListAsync(cancellationToken); // el catálogo de una empresa cabe en memoria (≈ miles)
        var descendientes = Descendientes(todas, cuenta.Id);

        // R2: sin ciclos; R3/R4/R5 sobre el nuevo padre.
        if (request.PadreId == cuenta.Id || descendientes.Any(d => d.Id == request.PadreId))
            throw new BusinessRuleException("CONTAB_CUENTA_CICLO", "El padre indicado es la propia cuenta o uno de sus descendientes.");
        var mismoPadre = request.PadreId == cuenta.PadreId;
        var padre = mismoPadre && request.PadreId is not null
            ? todas.First(c => c.Id == request.PadreId) : await _p.ValidarPadreAsync(request.PadreId, cancellationToken);
        var nivel = (padre?.Nivel ?? 0) + 1;
        var tieneHijas = todas.Any(c => c.PadreId == cuenta.Id);
        // P19: con derivación el tipo enviado se ignora; manda la jerarquía.
        var tipo = _p.Deriva ? CuentaContable.DerivarTipo(nivel, tieneHijas) : request.Tipo;

        // R8: campos sensibles bloqueados si la cuenta o un descendiente ya tiene uso.
        var sensible = cuenta.PadreId != request.PadreId || cuenta.Naturaleza != request.Naturaleza || cuenta.Tipo != tipo;
        if (sensible)
        {
            var ids = descendientes.Select(d => d.Id).Append(cuenta.Id).ToList();
            if (await db.Usos.AnyAsync(u => ids.Contains(u.CuentaId), cancellationToken))
            {
                var campo = cuenta.PadreId != request.PadreId ? "el padre" : cuenta.Naturaleza != request.Naturaleza ? "la naturaleza" : "el tipo";
                throw new BusinessRuleException("CONTAB_CUENTA_CAMBIO_BLOQUEADO_POR_USO",
                    $"La cuenta {cuenta.Codigo} ya tiene movimientos; cambiar {campo} alteraría la interpretación de saldos históricos. "
                    + "Use el procedimiento de impacto (reclasificación aprobada por Contabilidad) o cree una cuenta nueva y desactive esta.");
            }
        }

        if (tipo == TipoCuenta.Afectable && tieneHijas)
            throw new BusinessRuleException("CONTAB_CUENTA_AFECTABLE_CON_HIJAS", "Una cuenta con hijas no puede ser afectable.");
        var profundidad = descendientes.Count == 0 ? 0 : descendientes.Max(d => d.Nivel - cuenta.Nivel);
        _p.ValidarNivelYHerencia(nivel + profundidad, request.Naturaleza, padre);
        if (!mismoPadre) await _p.ConvertirPadreSiAfectableAsync(padre, cancellationToken);

        var delta = nivel - cuenta.Nivel;
        cuenta.Editar(request.Nombre.Trim(), request.PadreId, nivel, request.Naturaleza, tipo, _p.ResolverControl(cuenta.Codigo, request.CuentaControl),
            FormatoCatalogo.Texto(request.CodigoAgrupador), FormatoCatalogo.Texto(request.GrupoReporte), request.NoAfectableManual);
        cuenta.AsignarRubro(await _p.ValidarRubroAsync(request.RubroId, cancellationToken));
        foreach (var d in descendientes) d.FijarNivel(d.Nivel + delta);
        return CuentaResponse.De(cuenta);
    }

    internal static List<CuentaContable> Descendientes(List<CuentaContable> todas, Guid raiz)
    {
        var res = new List<CuentaContable>();
        var cola = new Queue<Guid>([raiz]);
        while (cola.TryDequeue(out var id))
            foreach (var h in todas.Where(c => c.PadreId == id)) { res.Add(h); cola.Enqueue(h.Id); }
        return res;
    }
}

// ─── Desactivar / reactivar (baja lógica; R7, R9) ────────────────────────────

public sealed record DesactivarCuentaCommand(Guid Id, int VersionEsperada) : IRequest<CuentaResponse>;

public sealed class DesactivarCuentaHandler(ContabilidadDbContext db, SolicitudesCatalogo solicitudes) : IRequestHandler<DesactivarCuentaCommand, CuentaResponse>
{
    public Task<CuentaResponse> Handle(DesactivarCuentaCommand request, CancellationToken cancellationToken) =>
        solicitudes.PrepararCuentaAsync(request, () => AplicarAsync(request, cancellationToken), cancellationToken);

    internal async Task<CuentaResponse> AplicarAsync(DesactivarCuentaCommand request, CancellationToken cancellationToken)
    {
        var cuenta = await db.Cuentas.FirstOrDefaultAsync(c => c.Id == request.Id, cancellationToken)
            ?? throw new EntityNotFoundException("CONTAB_CUENTA_NO_ENCONTRADA", $"No existe la cuenta '{request.Id}'.");
        if (cuenta.Version != request.VersionEsperada) throw new ConcurrencyException(nameof(CuentaContable), request.Id);
        if (!cuenta.Activa) return CuentaResponse.De(cuenta);
        if (await db.Cuentas.AnyAsync(c => c.PadreId == cuenta.Id && c.Estatus == EstatusCatalogo.Activo, cancellationToken))
            throw new BusinessRuleException("CONTAB_CUENTA_BAJA_CON_HIJAS_ACTIVAS",
                $"La cuenta {cuenta.Codigo} tiene hijas activas; desactívelas primero (no hay baja en cascada).");
        cuenta.Desactivar();
        return CuentaResponse.De(cuenta);
    }
}

public sealed record ReactivarCuentaCommand(Guid Id, int VersionEsperada) : IRequest<CuentaResponse>;

public sealed class ReactivarCuentaHandler(ContabilidadDbContext db, SolicitudesCatalogo solicitudes) : IRequestHandler<ReactivarCuentaCommand, CuentaResponse>
{
    public Task<CuentaResponse> Handle(ReactivarCuentaCommand request, CancellationToken cancellationToken) =>
        solicitudes.PrepararCuentaAsync(request, () => AplicarAsync(request, cancellationToken), cancellationToken);

    internal async Task<CuentaResponse> AplicarAsync(ReactivarCuentaCommand request, CancellationToken cancellationToken)
    {
        var cuenta = await db.Cuentas.FirstOrDefaultAsync(c => c.Id == request.Id, cancellationToken)
            ?? throw new EntityNotFoundException("CONTAB_CUENTA_NO_ENCONTRADA", $"No existe la cuenta '{request.Id}'.");
        if (cuenta.Version != request.VersionEsperada) throw new ConcurrencyException(nameof(CuentaContable), request.Id);
        if (cuenta.Activa) return CuentaResponse.De(cuenta);
        if (cuenta.PadreId is { } pid && !await db.Cuentas.AnyAsync(c => c.Id == pid && c.Estatus == EstatusCatalogo.Activo, cancellationToken))
            throw new BusinessRuleException("CONTAB_CUENTA_PADRE_INVALIDO", "No se puede reactivar: la cuenta padre está inactiva.");
        cuenta.Reactivar();
        return CuentaResponse.De(cuenta);
    }
}

// ─── Registrar uso (P4): solo Contabilidad; sin endpoint HTTP ────────────────

/// <summary>
/// Marca una cuenta como usada (R8). Lo invocará el módulo de pólizas/asientos al afectar; los
/// consumidores nunca escriben en <c>cuentas_contables_uso</c>. Idempotente por (cuenta, consumidor, referencia).
/// </summary>
// PLATFORM-TODO(<ContabilidadAsientos>): el módulo de pólizas debe invocar este comando al afectar una cuenta.
public sealed record RegistrarUsoCuentaCommand(Guid CuentaId, string Consumidor, string? Referencia) : IRequest<bool>;

public sealed class RegistrarUsoCuentaValidator : AbstractValidator<RegistrarUsoCuentaCommand>
{
    public RegistrarUsoCuentaValidator()
    {
        RuleFor(c => c.CuentaId).NotEqual(Guid.Empty);
        RuleFor(c => c.Consumidor).NotEmpty().MaximumLength(40);
        RuleFor(c => c.Referencia).MaximumLength(100);
    }
}

public sealed class RegistrarUsoCuentaHandler(ContabilidadDbContext db, IClock clock)
    : IRequestHandler<RegistrarUsoCuentaCommand, bool>
{
    /// <returns><c>true</c> si se registró un uso nuevo; <c>false</c> si ya existía.</returns>
    public async Task<bool> Handle(RegistrarUsoCuentaCommand request, CancellationToken cancellationToken)
    {
        if (!await db.Cuentas.AnyAsync(c => c.Id == request.CuentaId, cancellationToken))
            throw new EntityNotFoundException("CONTAB_CUENTA_NO_ENCONTRADA", $"No existe la cuenta '{request.CuentaId}'.");
        if (await db.Usos.AnyAsync(u => u.CuentaId == request.CuentaId && u.Consumidor == request.Consumidor && u.Referencia == request.Referencia, cancellationToken))
            return false;
        db.Usos.Add(new CuentaContableUso(Guid.CreateVersion7(), request.CuentaId, request.Consumidor, clock.UtcNow, request.Referencia));
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }
}
