using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Contabilidad.Application.Dimensiones;
using Millet.Contabilidad.Domain;
using Millet.Contabilidad.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Contabilidad.Application.Polizas;

public sealed record DocumentoOrigenRequest(string Modulo, string Tipo, Guid Id, string? Folio, string? Uuid);

/// <summary>
/// Encabezado y partidas de una póliza manual. <c>PeriodoNumero</c> solo se indica para el 13 de ajustes (fecha 31-dic); los
/// demás periodos salen de la fecha. <c>ReclasificaPolizaId</c> (K10.4): la póliza autorizada que esta reclasifica; queda ligada.
/// </summary>
public sealed record PolizaManualRequest(
    Guid TipoOperacionId, DateOnly Fecha, string Concepto, IReadOnlyList<PartidaRequest> Partidas,
    int? PeriodoNumero = null, DocumentoOrigenRequest? DocumentoOrigen = null, Guid? ReclasificaPolizaId = null);

public sealed class PartidaRequestValidator : AbstractValidator<PartidaRequest>
{
    public PartidaRequestValidator()
    {
        RuleFor(p => p.CuentaId).NotEqual(Guid.Empty).WithMessage("Elija la cuenta contable.");
        RuleFor(p => p.SucursalId).NotEqual(Guid.Empty).WithMessage("Elija la sucursal.");
        RuleFor(p => p.Concepto).MaximumLength(PolizaPartida.ConceptoMaximo);
        RuleFor(p => p.Proyecto).MaximumLength(40);
        RuleFor(p => p.Cargo).GreaterThanOrEqualTo(0m).WithMessage("El cargo no puede ser negativo.");
        RuleFor(p => p.Abono).GreaterThanOrEqualTo(0m).WithMessage("El abono no puede ser negativo.");
        RuleFor(p => p).Must(p => (p.Cargo != 0m) != (p.Abono != 0m)).WithName("cargo")
            .WithMessage("Capture un cargo o un abono (solo uno, distinto de cero).");
    }
}

public sealed class PolizaManualRequestValidator : AbstractValidator<PolizaManualRequest>
{
    public PolizaManualRequestValidator()
    {
        RuleFor(p => p.TipoOperacionId).NotEqual(Guid.Empty).WithMessage("Elija el tipo de operación.");
        RuleFor(p => p.Fecha).NotEqual(default(DateOnly)).WithMessage("Indique la fecha.");
        RuleFor(p => p.Concepto).NotEmpty().MaximumLength(Poliza.ConceptoMaximo);
        RuleFor(p => p.PeriodoNumero).Must(n => n is null or PeriodoContable.NumeroAjuste)
            .WithMessage("Solo el periodo 13 de ajustes se indica; los demás salen de la fecha.");
        RuleFor(p => p.Partidas).NotNull().Must(p => p.Count is >= 2 and <= 500)
            .WithMessage("La póliza lleva de 2 a 500 partidas.");
        RuleForEach(p => p.Partidas).SetValidator(new PartidaRequestValidator());
    }
}

/// <summary>Captura una póliza manual en borrador (C1.3). Valida cuentas, dimensiones y periodo; el cuadre se exige al autorizar.</summary>
public sealed record CrearPolizaCommand(PolizaManualRequest Poliza) : IRequest<PolizaResponse>;

public sealed class CrearPolizaValidator : AbstractValidator<CrearPolizaCommand>
{
    public CrearPolizaValidator() => RuleFor(c => c.Poliza).SetValidator(new PolizaManualRequestValidator());
}

public sealed class CrearPolizaHandler(
    ContabilidadDbContext db, RegistroPolizas registro, AlcanceSucursalContable alcance, ICurrentUserContext usuario, LecturaPolizas lectura)
    : IRequestHandler<CrearPolizaCommand, PolizaResponse>
{
    public async Task<PolizaResponse> Handle(CrearPolizaCommand request, CancellationToken cancellationToken)
    {
        var r = request.Poliza;
        foreach (var s in r.Partidas.Select(p => p.SucursalId).Distinct()) await alcance.VerificarAsync(s, cancellationToken);
        Poliza? poliza = null;
        await registro.EnTransaccionAsync(async ct =>
        {
            var tipo = await registro.TipoAsync(r.TipoOperacionId, ct);
            var (anio, numero) = RegistroPolizas.Periodo(r.Fecha, r.PeriodoNumero);
            await registro.VerificarPeriodoAsync(anio, numero, OrigenPoliza.Manual, ct);
            var partidas = await registro.ValidarPartidasAsync(r.Partidas, tipo.Id, r.Fecha, OrigenMovimiento.Manual, ct, aplicarAlcanceCentros: true);
            poliza = Poliza.CrearManual(Guid.CreateVersion7(), tipo, r.Fecha, anio, numero, r.Concepto, Documento(r.DocumentoOrigen),
                partidas.Partidas, usuario.UserId, PoliticaPolizas.Usuario(usuario));
            if (r.ReclasificaPolizaId is { } reclasificada)
            {
                var original = await db.Polizas.AsNoTracking().FirstOrDefaultAsync(x => x.Id == reclasificada, ct)
                    ?? throw new EntityNotFoundException("CONTAB_POLIZA_NO_ENCONTRADA", $"No existe la póliza a reclasificar '{reclasificada}'.");
                poliza.LigarReclasificacion(original);
            }
            db.Polizas.Add(poliza);
            await db.SaveChangesAsync(ct);
        }, cancellationToken);
        return await lectura.ObtenerAsync(poliza!.Id, cancellationToken);
    }

    internal static DocumentoOrigenPoliza? Documento(DocumentoOrigenRequest? d) =>
        d is null ? null : new(d.Modulo, d.Tipo, d.Id, d.Folio, d.Uuid);
}

/// <summary>Edita el borrador (encabezado y partidas completas). La autorizada no se edita (C1.3-b).</summary>
public sealed record EditarPolizaCommand(Guid Id, int VersionEsperada, PolizaManualRequest Poliza) : IRequest<PolizaResponse>;

public sealed class EditarPolizaValidator : AbstractValidator<EditarPolizaCommand>
{
    public EditarPolizaValidator() => RuleFor(c => c.Poliza).SetValidator(new PolizaManualRequestValidator());
}

public sealed class EditarPolizaHandler(
    ContabilidadDbContext db, RegistroPolizas registro, AlcanceSucursalContable alcance, ICurrentUserContext usuario, LecturaPolizas lectura)
    : IRequestHandler<EditarPolizaCommand, PolizaResponse>
{
    public async Task<PolizaResponse> Handle(EditarPolizaCommand request, CancellationToken cancellationToken)
    {
        var r = request.Poliza;
        foreach (var s in r.Partidas.Select(p => p.SucursalId).Distinct()) await alcance.VerificarAsync(s, cancellationToken);
        await registro.EnTransaccionAsync(async ct =>
        {
            var poliza = await PoliticaPolizas.CargarAsync(db, request.Id, request.VersionEsperada, ct);
            poliza.ExigirBorrador("editar");
            if (usuario.UserId is null || usuario.UserId != poliza.CapturadaPorId)
                throw new BusinessRuleException("CONTAB_POLIZA_EDITA_SOLO_QUIEN_CAPTURA",
                    "Solo quien capturó el borrador puede editarlo. Si tiene un error, regréselo a quien lo capturó o descártelo y capture otro.");
            if (r.TipoOperacionId != poliza.TipoOperacionId)
                throw new BusinessRuleException("CONTAB_POLIZA_TIPO_INMUTABLE", "El tipo de operación no se cambia: descarte el borrador y capture otro.");
            var (anio, numero) = RegistroPolizas.Periodo(r.Fecha, r.PeriodoNumero);
            await registro.VerificarPeriodoAsync(anio, numero, OrigenPoliza.Manual, ct);
            var partidas = await registro.ValidarPartidasAsync(r.Partidas, poliza.TipoOperacionId, r.Fecha, OrigenMovimiento.Manual, ct, aplicarAlcanceCentros: true);
            db.PolizasPartidas.RemoveRange(poliza.Partidas);
            poliza.Editar(r.Fecha, anio, numero, r.Concepto, partidas.Partidas, usuario.UserId);
            db.PolizasPartidas.AddRange(poliza.Partidas);
            await db.SaveChangesAsync(ct);
        }, cancellationToken);
        return await lectura.ObtenerAsync(request.Id, cancellationToken);
    }
}

/// <summary>Descarta un borrador: no tiene número, así que no deja hueco en el consecutivo.</summary>
public sealed record DescartarPolizaCommand(Guid Id, int VersionEsperada) : IRequest;

public sealed class DescartarPolizaHandler(ContabilidadDbContext db, AlcanceSucursalContable alcance) : IRequestHandler<DescartarPolizaCommand>
{
    public async Task Handle(DescartarPolizaCommand request, CancellationToken cancellationToken)
    {
        var poliza = await PoliticaPolizas.CargarAsync(db, request.Id, request.VersionEsperada, cancellationToken);
        foreach (var s in poliza.Partidas.Select(p => p.SucursalId).Distinct()) await alcance.VerificarAsync(s, cancellationToken);
        poliza.ExigirBorrador("descartar");
        db.Polizas.Remove(poliza);
        await db.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>
/// Autoriza el borrador (C1.3): vuelve a validar periodo, cuentas y dimensiones (pudieron cambiar desde la captura), exige cuadre
/// (C1.3-a) y que no lo autorice quien lo capturó (C1.3-c); asigna el número del tipo (K10.9) y registra el uso de cuentas y reglas.
/// </summary>
public sealed record AutorizarPolizaCommand(Guid Id, int VersionEsperada) : IRequest<PolizaResponse>;

public sealed class AutorizarPolizaHandler(
    ContabilidadDbContext db, RegistroPolizas registro, AlcanceSucursalContable alcance, ICurrentUserContext usuario, IClock clock,
    LecturaPolizas lectura)
    : IRequestHandler<AutorizarPolizaCommand, PolizaResponse>
{
    public async Task<PolizaResponse> Handle(AutorizarPolizaCommand request, CancellationToken cancellationToken)
    {
        await registro.EnTransaccionAsync(async ct =>
        {
            var poliza = await PoliticaPolizas.CargarAsync(db, request.Id, request.VersionEsperada, ct);
            foreach (var s in poliza.Partidas.Select(p => p.SucursalId).Distinct()) await alcance.VerificarAsync(s, ct);
            poliza.ExigirBorrador("autorizar");
            await registro.VerificarPeriodoAsync(poliza.PeriodoAnio, poliza.PeriodoNumero, poliza.Origen, ct);
            var partidas = await registro.ValidarPartidasAsync(
                [.. poliza.Partidas.OrderBy(p => p.Linea).Select(PoliticaPolizas.Request)], poliza.TipoOperacionId, poliza.Fecha, OrigenMovimiento.Manual, ct);
            poliza.ValidarCuadre();
            var numero = await registro.ReservarNumeroAsync(poliza.TipoOperacionId, poliza.PeriodoAnio, ct);
            poliza.Autorizar(numero, usuario.UserId, PoliticaPolizas.Usuario(usuario), clock.UtcNow);
            await db.SaveChangesAsync(ct);
            await registro.RegistrarUsosAsync(poliza, partidas.ReglasAplicadas, ct);
        }, cancellationToken);
        return await lectura.ObtenerAsync(request.Id, cancellationToken);
    }
}

/// <summary>
/// Anula una póliza autorizada (K10.3): nace otra del mismo tipo con las mismas partidas en negativo, concepto «Anulación de
/// TT-000123», ligada a la original, que queda anulada. Motivo obligatorio. Si el periodo de la original ya no admite pólizas, la
/// anulación va al periodo abierto indicado (<c>PeriodoAnio</c>/<c>PeriodoNumero</c>). Devuelve la póliza de anulación.
/// </summary>
public sealed record AnularPolizaCommand(Guid Id, int VersionEsperada, string Motivo, int? PeriodoAnio = null, int? PeriodoNumero = null)
    : IRequest<PolizaResponse>;

public sealed class AnularPolizaValidator : AbstractValidator<AnularPolizaCommand>
{
    public AnularPolizaValidator()
    {
        RuleFor(c => c.Motivo).Must(m => m is not null && m.Trim().Length is >= Poliza.MotivoMinimo and <= Poliza.MotivoMaximo)
            .WithMessage($"Indique el motivo (entre {Poliza.MotivoMinimo} y {Poliza.MotivoMaximo} caracteres).");
        RuleFor(c => c).Must(c => (c.PeriodoAnio is null) == (c.PeriodoNumero is null)).WithName("periodoNumero")
            .WithMessage("Indique el año y el número del periodo destino, o ninguno.");
        RuleFor(c => c.PeriodoNumero).InclusiveBetween(1, PeriodoContable.NumeroAjuste).When(c => c.PeriodoNumero is not null);
    }
}

public sealed class AnularPolizaHandler(
    ContabilidadDbContext db, RegistroPolizas registro, AlcanceSucursalContable alcance, ICurrentUserContext usuario, IClock clock,
    LecturaPolizas lectura)
    : IRequestHandler<AnularPolizaCommand, PolizaResponse>
{
    public async Task<PolizaResponse> Handle(AnularPolizaCommand request, CancellationToken cancellationToken)
    {
        Poliza? anulacion = null;
        await registro.EnTransaccionAsync(async ct =>
        {
            var original = await PoliticaPolizas.CargarAsync(db, request.Id, request.VersionEsperada, ct);
            foreach (var s in original.Partidas.Select(p => p.SucursalId).Distinct()) await alcance.VerificarAsync(s, ct);
            var destino = request.PeriodoAnio is { } a && request.PeriodoNumero is { } n ? (a, n) : ((int, int)?)null;
            anulacion = await registro.AnularAsync(original, request.Motivo, destino, FechaContable.HoyLocal(clock.UtcNow),
                usuario.UserId, PoliticaPolizas.Usuario(usuario), clock.UtcNow, ct);
            await PoliticaPolizas.GuardarAnulacionAsync(db, ct);
        }, cancellationToken);
        return await lectura.ObtenerAsync(anulacion!.Id, cancellationToken);
    }
}

internal static class PoliticaPolizas
{
    public static string Usuario(ICurrentUserContext usuario) => usuario.UserName ?? "system";

    public static async Task<Poliza> CargarAsync(ContabilidadDbContext db, Guid id, int versionEsperada, CancellationToken ct)
    {
        var p = await db.Polizas.Include(x => x.Partidas).FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new EntityNotFoundException("CONTAB_POLIZA_NO_ENCONTRADA", $"No existe la póliza '{id}'.");
        if (p.Version != versionEsperada) throw new ConcurrencyException(nameof(Poliza), id);
        return p;
    }

    public static PartidaRequest Request(PolizaPartida p) => new(p.CuentaId, p.Concepto, p.Cargo, p.Abono, p.SucursalId,
        p.Dim1Id, p.Dim2Id, p.Dim3Id, p.Proyecto, p.ClienteId, p.ProveedorId, p.CuentaBancariaId, p.Moneda, p.TipoCambio, p.ImporteOriginal);

    /// <summary>El índice único de la póliza anulada convierte una segunda anulación concurrente en 409 (K10.3-b).</summary>
    public static async Task GuardarAnulacionAsync(ContabilidadDbContext db, CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException e) when (e.InnerException is Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.UniqueViolation, ConstraintName: "ux_polizas_anulada" })
        {
            throw new ConflictException("CONTAB_POLIZA_YA_ANULADA", "La póliza ya fue anulada por otra operación.");
        }
    }
}
