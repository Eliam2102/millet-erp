using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Integraciones.Fiscal.Domain;
using Millet.Integraciones.Fiscal.Domain.Certificados;
using Millet.Integraciones.Fiscal.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.Integraciones.Fiscal.Application.Certificados;

/// <summary>
/// Estado del CSD de la empresa actual para el aviso en el ERP (F6.9): estado,
/// vencimiento y días restantes. Sin datos sensibles.
/// </summary>
public sealed record EstadoCsdEmpresaQuery : IRequest<EstadoCsdEmpresaResponse>;

/// <param name="Estado">NoConfigurado, Vigente, ProximoAVencer, Vencido o Revocado.</param>
public sealed record EstadoCsdEmpresaResponse(
    string Estado,
    string? NumeroSerie,
    DateTimeOffset? VenceEn,
    int? DiasRestantes,
    bool BloqueaTimbrado);

/// <summary>Historial de CSD de una empresa (F6.9), del más reciente al más antiguo.</summary>
public sealed record ListarCertificadosCsdQuery(Guid EmpresaId) : IRequest<IReadOnlyList<CertificadoCsdItem>>;

public sealed record CertificadoCsdItem(
    Guid Id,
    string NumeroSerie,
    string? Rfc,
    DateTimeOffset VigenteDesde,
    DateTimeOffset VenceEn,
    string Estado,
    DateTimeOffset RegistradoEn,
    string? RegistradoPor,
    DateTimeOffset? FinalizadoEn,
    string? FinalizadoPor,
    string? MotivoRevocacion,
    IReadOnlyList<int> AlertasEnviadas);

public sealed class EstadoCsdEmpresaHandler(IntegracionesFiscalDbContext db, ICurrentEmpresaContext empresa, IClock clock)
    : IRequestHandler<EstadoCsdEmpresaQuery, EstadoCsdEmpresaResponse>
{
    public async Task<EstadoCsdEmpresaResponse> Handle(EstadoCsdEmpresaQuery query, CancellationToken cancellationToken)
    {
        var sinCsd = new EstadoCsdEmpresaResponse("NoConfigurado", null, null, null, false);
        if (empresa.Current is not Guid empresaId) return sinCsd;

        var config = await db.ConfiguracionesPac.AsNoTracking()
            .FirstOrDefaultAsync(c => c.EmpresaId == empresaId && c.Proveedor == ProveedorPac.FiscalApi && c.Activo, cancellationToken);
        var ahora = clock.UtcNow;
        if (config?.ObtenerEstadoCsd(ahora) is not { } estado) return sinCsd;

        var numeroSerie = await db.CertificadosCsd.AsNoTracking()
            .Where(c => c.EmpresaId == empresaId && c.Estado != EstadoCertificadoCsd.Sustituido)
            .OrderByDescending(c => c.RegistradoEn)
            .Select(c => c.NumeroSerie)
            .FirstOrDefaultAsync(cancellationToken);
        var dias = (int)Math.Floor((config.CsdNotAfter!.Value - ahora).TotalDays);
        return new EstadoCsdEmpresaResponse(
            estado.ToString(), numeroSerie, config.CsdNotAfter, dias,
            BloqueaTimbrado: estado is EstadoCsd.Vencido or EstadoCsd.Revocado);
    }
}

public sealed class ListarCertificadosCsdHandler(IntegracionesFiscalDbContext db)
    : IRequestHandler<ListarCertificadosCsdQuery, IReadOnlyList<CertificadoCsdItem>>
{
    public async Task<IReadOnlyList<CertificadoCsdItem>> Handle(ListarCertificadosCsdQuery query, CancellationToken cancellationToken)
    {
        var certificados = await db.CertificadosCsd.AsNoTracking()
            .Where(c => c.EmpresaId == query.EmpresaId)
            .OrderByDescending(c => c.RegistradoEn)
            .ToListAsync(cancellationToken);
        var ids = certificados.Select(c => c.Id).ToList();
        var alertas = await db.AlertasCsd.AsNoTracking()
            .Where(a => ids.Contains(a.CertificadoCsdId))
            .Select(a => new { a.CertificadoCsdId, a.UmbralDias })
            .ToListAsync(cancellationToken);

        return certificados.Select(c => new CertificadoCsdItem(
            c.Id, c.NumeroSerie, c.Rfc, c.VigenteDesde, c.VenceEn, c.Estado.ToString(), c.RegistradoEn, c.RegistradoPor,
            c.FinalizadoEn, c.FinalizadoPor, c.MotivoRevocacion,
            alertas.Where(a => a.CertificadoCsdId == c.Id).Select(a => a.UmbralDias).OrderDescending().ToList())).ToList();
    }
}
