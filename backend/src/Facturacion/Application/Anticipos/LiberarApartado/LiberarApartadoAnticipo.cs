using System.Text.Json;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Domain.Anticipos;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.NotasCredito;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.Application.Anticipos.LiberarApartado;

/// <summary>
/// U-4 (86e3yzemw · flujo 3): libera con motivo el importe apartado (vinculado sin NC) de un anticipo con una
/// factura cuando no corresponde. Reúsa la liberación de U-1 (<see cref="Anticipo.LiberarVinculacion"/>) y deja
/// constancia con usuario y motivo. Si la NC de amortización de esa factura sigue pendiente de timbrar, primero
/// se descarta la NC (el descarte ya libera el apartado).
/// </summary>
public sealed record LiberarApartadoAnticipoCommand(Guid AnticipoId, Guid FacturaVentaId, string Motivo) : IRequest<LiberacionApartadoResponse>;

public sealed record LiberacionApartadoResponse(
    Guid AnticipoId, Guid FacturaVentaId, decimal ImporteLiberado, string Motivo, string LiberadoPorNombre, DateTimeOffset LiberadoEn,
    decimal Saldo, decimal SaldoDisponible);

public sealed class LiberarApartadoAnticipoValidator : AbstractValidator<LiberarApartadoAnticipoCommand>
{
    public LiberarApartadoAnticipoValidator()
    {
        RuleFor(x => x.AnticipoId).NotEmpty();
        RuleFor(x => x.FacturaVentaId).NotEmpty();
        RuleFor(x => x.Motivo).NotEmpty()
            .Must(m => m.Trim().Length is >= Anticipo.MotivoLiberacionMin and <= Anticipo.MotivoLiberacionMax)
            .WithMessage($"El motivo debe tener de {Anticipo.MotivoLiberacionMin} a {Anticipo.MotivoLiberacionMax} caracteres.");
    }
}

public sealed class LiberarApartadoAnticipoHandler(
    FacturacionDbContext db,
    ICurrentUserContext usuario,
    ICurrentEmpresaContext empresa,
    IUsuariosReadPort usuarios,
    IAuditLogWriter audit,
    IClock clock) : IRequestHandler<LiberarApartadoAnticipoCommand, LiberacionApartadoResponse>
{
    public async Task<LiberacionApartadoResponse> Handle(LiberarApartadoAnticipoCommand r, CancellationToken cancellationToken)
    {
        var usuarioId = usuario.UserId ?? throw new ForbiddenException("USUARIO_REQUERIDO", "No se pudo identificar al usuario.");
        var anticipo = await db.Anticipos.Include(a => a.Vinculaciones).Include(a => a.Liberaciones)
            .FirstOrDefaultAsync(a => a.Id == r.AnticipoId, cancellationToken)
            ?? throw new EntityNotFoundException("ANTICIPO_NO_ENCONTRADO", "No existe el anticipo solicitado.");

        if (await NcPendiente.ExisteAsync(db, r.AnticipoId, r.FacturaVentaId, cancellationToken))
            throw new BusinessRuleException("ANTICIPO_APARTADO_CON_NC_PENDIENTE",
                "La nota de crédito de amortización de esa factura sigue pendiente de timbrar: descártela y el apartado se libera solo.");

        var nombres = await usuarios.ObtenerNombresAsync([usuarioId], cancellationToken);
        var nombre = nombres.TryGetValue(usuarioId, out var n) ? n : usuario.UserName ?? "Usuario";
        var liberacion = anticipo.LiberarApartado(r.FacturaVentaId, r.Motivo, usuarioId, nombre, clock.UtcNow);
        await db.SaveChangesAsync(cancellationToken);

        var folio = await db.FacturasVenta.AsNoTracking().Where(f => f.Id == r.FacturaVentaId).Select(f => f.Folio).FirstOrDefaultAsync(cancellationToken);
        await audit.RegistrarAsync(
            operacion: "anticipo.apartado-liberado", modulo: "Facturacion", entidad: "Anticipo",
            entidadId: anticipo.Id, aggregateRootId: anticipo.Id,
            actorNombre: nombre, actorTipo: "usuario", actorEmail: usuario.Email,
            entidadEtiqueta: anticipo.PedidoOrigenRef is { } p ? $"Anticipo · pedido {p}" : "Anticipo",
            resumen: $"Se liberaron {liberacion.Importe:N2} {anticipo.Moneda} apartados para la factura {folio ?? "(sin folio)"}: {liberacion.Motivo}",
            usuarioId: usuarioId, empresaId: empresa.Current,
            cambios: JsonSerializer.Serialize(new { liberacion.FacturaVentaId, liberacion.Importe, liberacion.Motivo }),
            cancellationToken: cancellationToken);

        return new LiberacionApartadoResponse(anticipo.Id, liberacion.FacturaVentaId, liberacion.Importe, liberacion.Motivo,
            liberacion.LiberadoPorNombre, liberacion.LiberadoEn, anticipo.Saldo, anticipo.SaldoDisponible);
    }
}

internal static class NcPendiente
{
    private static readonly EstadoTimbrado[] Cerradas =
        [EstadoTimbrado.Timbrado, EstadoTimbrado.Cancelado, EstadoTimbrado.Descartada, EstadoTimbrado.CancelacionPendiente];

    public static Task<bool> ExisteAsync(FacturacionDbContext db, Guid anticipoId, Guid facturaVentaId, CancellationToken cancellationToken) =>
        db.NotasCredito.AsNoTracking().AnyAsync(nc => nc.Motivo == MotivoNotaCredito.Amortizacion
            && nc.AnticipoOrigenId == anticipoId && nc.FacturaRelacionadaId == facturaVentaId
            && !Cerradas.Contains(nc.Estado), cancellationToken);
}

/// <summary>Apartados vigentes (vinculados sin NC) y liberaciones hechas, para la pantalla del anticipo.</summary>
public sealed record ObtenerApartadosAnticipoQuery(Guid AnticipoId) : IRequest<ApartadosAnticipoResponse>;

public sealed record ApartadoAnticipoDto(Guid FacturaVentaId, string? FacturaFolio, decimal Importe, DateTimeOffset ApartadoEn, bool NcPendiente);
public sealed record LiberacionAnticipoDto(Guid FacturaVentaId, string? FacturaFolio, decimal Importe, string Motivo, string LiberadoPorNombre, DateTimeOffset LiberadoEn);
public sealed record ApartadosAnticipoResponse(
    Guid AnticipoId, string Moneda, decimal Saldo, decimal SaldoDisponible,
    IReadOnlyList<ApartadoAnticipoDto> Apartados, IReadOnlyList<LiberacionAnticipoDto> Liberaciones);

public sealed class ObtenerApartadosAnticipoHandler(FacturacionDbContext db) : IRequestHandler<ObtenerApartadosAnticipoQuery, ApartadosAnticipoResponse>
{
    public async Task<ApartadosAnticipoResponse> Handle(ObtenerApartadosAnticipoQuery r, CancellationToken cancellationToken)
    {
        var a = await db.Anticipos.AsNoTracking().Include(x => x.Vinculaciones).Include(x => x.Liberaciones)
            .FirstOrDefaultAsync(x => x.Id == r.AnticipoId, cancellationToken)
            ?? throw new EntityNotFoundException("ANTICIPO_NO_ENCONTRADO", "No existe el anticipo solicitado.");
        var apartados = a.Vinculaciones.Where(v => v.NcAmortizacionId is null).ToList();
        var facturas = apartados.Select(v => v.FacturaVentaId).Concat(a.Liberaciones.Select(l => l.FacturaVentaId)).Distinct().ToList();
        var folios = await db.FacturasVenta.AsNoTracking().Where(f => facturas.Contains(f.Id)).ToDictionaryAsync(f => f.Id, f => f.Folio, cancellationToken);
        var conNcPendiente = new HashSet<Guid>();
        foreach (var v in apartados)
            if (await NcPendiente.ExisteAsync(db, a.Id, v.FacturaVentaId, cancellationToken))
                conNcPendiente.Add(v.FacturaVentaId);
        return new ApartadosAnticipoResponse(a.Id, a.Moneda, a.Saldo, a.SaldoDisponible,
            apartados.OrderBy(v => v.CreadoEn)
                .Select(v => new ApartadoAnticipoDto(v.FacturaVentaId, folios.GetValueOrDefault(v.FacturaVentaId), v.Importe, v.CreadoEn, conNcPendiente.Contains(v.FacturaVentaId)))
                .ToList(),
            a.Liberaciones.OrderByDescending(l => l.LiberadoEn)
                .Select(l => new LiberacionAnticipoDto(l.FacturaVentaId, folios.GetValueOrDefault(l.FacturaVentaId), l.Importe, l.Motivo, l.LiberadoPorNombre, l.LiberadoEn))
                .ToList());
    }
}
