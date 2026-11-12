using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Contabilidad.Domain;
using Millet.Contabilidad.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Contabilidad.Application.Iva;

// ─── M-311 · papel de trabajo del IVA del mes: foto, acuse y diferencias fiscales ────────────────────────────────────────────────────

public sealed record AcuseIvaResponse(Guid Id, string Nombre, string TipoContenido, string Sha256, DateTimeOffset AdjuntadoEn);
public sealed record DiferenciaFiscalIvaResponse(Guid Id, Guid SaldoIvaDocumentoId, string? Folio, string? Uuid, decimal IvaFoto, decimal IvaActual,
    decimal Diferencia, string Causa, string Estado, Guid? AjustePolizaId, DateTimeOffset DetectadaEn, DateTimeOffset? CerradaEn);
public sealed record PapelTrabajoIvaResponse(Guid FotoId, int Anio, int Mes, Guid PolizaId, string FolioPoliza, DateTimeOffset CreadaEn,
    ResumenLiquidacionIva Resumen, IReadOnlyList<CuadreRolIva> Cuadre, IReadOnlyList<DocumentoLiquidacionIva> Documentos, AcuseIvaResponse? Acuse,
    IReadOnlyList<DiferenciaFiscalIvaResponse> Diferencias, int DiferenciasPendientes);

public sealed record PapelTrabajoIvaQuery(int Anio, int Mes) : IRequest<PapelTrabajoIvaResponse>;
/// <summary>Compara la foto del mes con lo que el mes muestra hoy y registra (o actualiza) las diferencias pendientes.</summary>
public sealed record DetectarDiferenciasIvaCommand(int Anio, int Mes) : IRequest<PapelTrabajoIvaResponse>;
public sealed record AdjuntarAcuseIvaCommand(int Anio, int Mes, string Nombre, string TipoContenido, byte[] Contenido) : IRequest<AcuseIvaResponse>;
public sealed record LigarAjusteDiferenciaIvaCommand(Guid DiferenciaId, Guid AjustePolizaId) : IRequest<DiferenciaFiscalIvaResponse>;

public sealed class PapelTrabajoIvaQueryValidator : AbstractValidator<PapelTrabajoIvaQuery>
{
    public PapelTrabajoIvaQueryValidator() { RuleFor(q => q.Anio).InclusiveBetween(2000, 2100); RuleFor(q => q.Mes).InclusiveBetween(1, 12); }
}

public sealed class PapelTrabajoIvaHandlers(ContabilidadDbContext db, IMediator mediator, ICurrentUserContext usuario, IClock clock)
    : IRequestHandler<PapelTrabajoIvaQuery, PapelTrabajoIvaResponse>,
      IRequestHandler<DetectarDiferenciasIvaCommand, PapelTrabajoIvaResponse>,
      IRequestHandler<AdjuntarAcuseIvaCommand, AcuseIvaResponse>,
      IRequestHandler<LigarAjusteDiferenciaIvaCommand, DiferenciaFiscalIvaResponse>
{
    public Task<PapelTrabajoIvaResponse> Handle(PapelTrabajoIvaQuery q, CancellationToken cancellationToken) => ArmarAsync(q.Anio, q.Mes, cancellationToken);

    public async Task<PapelTrabajoIvaResponse> Handle(DetectarDiferenciasIvaCommand c, CancellationToken cancellationToken)
    {
        var ct = cancellationToken;
        var foto = await FotoAsync(c.Anio, c.Mes, ct);
        var viva = await mediator.Send(new LiquidacionIvaQuery(c.Anio, c.Mes), ct);
        var cambios = DetectorDiferenciasIva.Comparar(foto.Detalle(), viva.Documentos);
        var existentes = await db.DiferenciasFiscalesIva.Where(d => d.FotoId == foto.Id).ToListAsync(ct);
        foreach (var cambio in cambios)
        {
            var previa = existentes.FirstOrDefault(d => d.SaldoIvaDocumentoId == cambio.SaldoId);
            if (previa is null) db.DiferenciasFiscalesIva.Add(DiferenciaFiscalIva.Detectar(Guid.Empty, foto.Id, cambio, clock.UtcNow));
            else previa.Actualizar(cambio);
        }
        await db.SaveChangesAsync(ct);
        return await ArmarAsync(c.Anio, c.Mes, ct);
    }

    public async Task<AcuseIvaResponse> Handle(AdjuntarAcuseIvaCommand c, CancellationToken cancellationToken)
    {
        var ct = cancellationToken;
        var foto = await FotoAsync(c.Anio, c.Mes, ct);
        if (await db.LiquidacionesIvaAcuse.AnyAsync(a => a.FotoId == foto.Id, ct))
            throw new ConflictException("IVA_ACUSE_EXISTE", "La liquidación de este mes ya tiene su acuse; no se reemplaza.");
        var acuse = LiquidacionIvaAcuse.Crear(Guid.Empty, foto.Id, c.Nombre, c.TipoContenido, c.Contenido, Usuario(), clock.UtcNow);
        db.LiquidacionesIvaAcuse.Add(acuse);
        await db.SaveChangesAsync(ct);
        return Acuse(acuse);
    }

    public async Task<DiferenciaFiscalIvaResponse> Handle(LigarAjusteDiferenciaIvaCommand c, CancellationToken cancellationToken)
    {
        var ct = cancellationToken;
        var diferencia = await db.DiferenciasFiscalesIva.FirstOrDefaultAsync(d => d.Id == c.DiferenciaId, ct)
            ?? throw new EntityNotFoundException("CONTAB_IVA_DIFERENCIA_NO_ENCONTRADA", "La diferencia fiscal de IVA no existe.");
        if (!await db.Polizas.AsNoTracking().AnyAsync(p => p.Id == c.AjustePolizaId && p.Estado != EstadoPoliza.Anulada, ct))
            throw new BusinessRuleException("IVA_DIFERENCIA_AJUSTE", "La póliza de ajuste no existe o está anulada.");
        diferencia.LigarAjuste(c.AjustePolizaId, Usuario(), clock.UtcNow);
        await db.SaveChangesAsync(ct);
        return Diferencia(diferencia);
    }

    private Guid Usuario() => usuario.UserId ?? throw new BusinessRuleException("IVA_SIN_USUARIO", "No se identificó al usuario.");

    private async Task<LiquidacionIvaFoto> FotoAsync(int anio, int mes, CancellationToken ct)
    {
        // Si el mes se liquidó más de una vez (póliza anulada y regenerada) vale la foto de la póliza vigente más reciente.
        var fotos = await db.LiquidacionesIvaFoto.AsNoTracking().Where(f => f.Anio == anio && f.Mes == mes).OrderByDescending(f => f.CreadaEn).ToListAsync(ct);
        foreach (var f in fotos)
            if (await db.Polizas.AsNoTracking().AnyAsync(p => p.Id == f.PolizaId && p.Estado != EstadoPoliza.Anulada, ct)) return f;
        throw new EntityNotFoundException("CONTAB_IVA_SIN_FOTO", $"El mes {mes:D2}/{anio} no tiene liquidación de IVA con póliza vigente.");
    }

    private async Task<PapelTrabajoIvaResponse> ArmarAsync(int anio, int mes, CancellationToken ct)
    {
        var foto = await FotoAsync(anio, mes, ct);
        var acuse = await db.LiquidacionesIvaAcuse.AsNoTracking().FirstOrDefaultAsync(a => a.FotoId == foto.Id, ct);
        var diferencias = (await db.DiferenciasFiscalesIva.AsNoTracking().Where(d => d.FotoId == foto.Id).OrderBy(d => d.DetectadaEn).ToListAsync(ct))
            .Select(Diferencia).ToList();
        return new(foto.Id, foto.Anio, foto.Mes, foto.PolizaId, foto.FolioPoliza, foto.CreadaEn, foto.Resumen(), foto.Cuadre(), foto.Detalle(),
            acuse is null ? null : Acuse(acuse), diferencias, diferencias.Count(d => d.Estado == nameof(EstadoDiferenciaFiscalIva.Pendiente)));
    }

    private static AcuseIvaResponse Acuse(LiquidacionIvaAcuse a) => new(a.Id, a.Nombre, a.TipoContenido, a.Sha256, a.AdjuntadoEn);
    private static DiferenciaFiscalIvaResponse Diferencia(DiferenciaFiscalIva d) => new(d.Id, d.SaldoIvaDocumentoId, d.Folio, d.Uuid, d.IvaFoto, d.IvaActual,
        d.Diferencia, d.Causa, d.Estado.ToString(), d.AjustePolizaId, d.DetectadaEn, d.CerradaEn);
}
