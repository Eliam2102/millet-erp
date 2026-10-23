using System.Text.Json.Serialization;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Inicio;

namespace Millet.Facturacion.Application.Reportes.CierreDia;

/// <summary>
/// Consulta al SAT el estatus de los CFDI timbrados que muestra el monitor de
/// un día y sucursal (F6.7) y guarda el resultado en cada comprobante. Se
/// limita a <see cref="MaximoPorConsulta"/> por llamada (cuota del PAC); un
/// error en un CFDI no detiene a los demás.
/// </summary>
public sealed record VerificarEstatusSatCommand(Guid SucursalId, DateOnly Dia)
    : IRequest<VerificarEstatusSatResponse>, ISucursalSeleccionadaQuery
{
    public const int MaximoPorConsulta = 200;

    public string PermisoTodasSucursales => CierreDia.PermisoTodasSucursales;
    [JsonIgnore] public IReadOnlyList<Guid>? SucursalesPermitidas { get; set; }
    Guid? ISucursalSeleccionadaQuery.SucursalId => SucursalId;
}

public sealed record VerificarEstatusSatResponse(
    string Fuente,
    int Consultados,
    int Vigentes,
    int Cancelados,
    int NoEncontrados,
    int Errores,
    int Pendientes);

public sealed class VerificarEstatusSatHandler(
    FacturacionDbContext db,
    ISucursalesReadPort sucursales,
    IConsultaEstatusCfdiPort estatusSat,
    IClock clock,
    ILogger<VerificarEstatusSatHandler> logger) : IRequestHandler<VerificarEstatusSatCommand, VerificarEstatusSatResponse>
{
    public async Task<VerificarEstatusSatResponse> Handle(VerificarEstatusSatCommand command, CancellationToken cancellationToken)
    {
        CierreDia.ExigirSucursal(command.SucursalesPermitidas, command.SucursalId);

        var zona = await sucursales.ObtenerZonaHorariaAsync(command.SucursalId, cancellationToken);
        var (desde, hasta) = CierreDia.Ventana(command.Dia, zona);

        // Mismo universo que el monitor, solo los que tienen timbre; primero los
        // nunca verificados y luego los de consulta más vieja.
        var ids = (await MonitorCfdiHandler.ComprobantesDelDiaAsync(
                db, command.SucursalId, command.Dia, null, desde, hasta, cancellationToken))
            .Where(c => c.Uuid is not null)
            .OrderBy(c => c.EstatusSatConsultadoEn ?? DateTimeOffset.MinValue)
            .Select(c => c.Id)
            .ToList();
        var lote = ids.Take(VerificarEstatusSatCommand.MaximoPorConsulta).ToList();

        var comprobantes = await db.Comprobantes.Where(c => lote.Contains(c.Id)).ToListAsync(cancellationToken);
        int vigentes = 0, cancelados = 0, noEncontrados = 0, errores = 0;
        foreach (var c in comprobantes)
        {
            try
            {
                var r = await estatusSat.ConsultarAsync(new ConsultaEstatusCfdi(
                    c.EmpresaId, c.Uuid!, c.RfcEmisor, c.ReceptorRfc, c.Total,
                    c.SelloCfdi is { Length: >= 8 } sello ? sello[^8..] : null, c.Estado), cancellationToken);
                c.RegistrarEstatusSat(r.EstatusSat, r.EsCancelable, clock.UtcNow);
                switch (r.EstatusSat)
                {
                    case EstatusSat.Vigente: vigentes++; break;
                    case EstatusSat.Cancelado: cancelados++; break;
                    default: noEncontrados++; break;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                errores++;
                logger.LogWarning(ex, "[F6.7] No se pudo consultar el estatus SAT del comprobante {ComprobanteId}.", c.Id);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        return new VerificarEstatusSatResponse(
            estatusSat.Fuente, comprobantes.Count, vigentes, cancelados, noEncontrados, errores,
            Math.Max(0, ids.Count - lote.Count));
    }
}
