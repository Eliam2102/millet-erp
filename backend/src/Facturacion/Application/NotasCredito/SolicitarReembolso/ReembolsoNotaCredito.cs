using Millet.Facturacion.Application.Integration;
using Millet.Facturacion.Domain.NotasCredito;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.Application.NotasCredito.SolicitarReembolso;

/// <summary>
/// Pide a Tesorería el reembolso en dinero de una NC timbrada: marca la NC y publica
/// <c>facturacion.nota-credito.reembolso-solicitado.v1</c> por Outbox (ADR-0009) en la misma transacción.
/// Lo usan la solicitud manual y, desde F6.3, la emisión o autorización con «Devolver dinero al cliente» al quedar timbrada.
/// </summary>
internal static class ReembolsoNotaCredito
{
    public const string PermisoSolicitar = "facturacion.notas-credito.reembolso-solicitar";

    /// <summary>Programar el reembolso al emitir o autorizar mueve dinero: exige el mismo permiso que la solicitud manual.</summary>
    public static async Task ExigirPermisoAsync(ICurrentUserPermissions permisos, CancellationToken ct)
    {
        if (!await permisos.TieneAsync(PermisoSolicitar, ct))
            throw new ForbiddenException("NC_REEMBOLSO_SIN_PERMISO",
                "No tienes permiso para solicitar a Tesorería el reembolso en dinero de una nota de crédito.");
    }

    public static async Task SolicitarAsync(
        FacturacionDbContext db, IIntegrationEventPublisher eventos, NotaCredito nc, decimal importe, string motivo,
        Guid solicitadoPor, DateTimeOffset ahora, CancellationToken ct)
    {
        var clienteId = await ClienteContableFacturacion.ResolverAsync(db, nc, ct)
            ?? throw new BusinessRuleException("NC_REEMBOLSO_SIN_CLIENTE",
                "No se pudo identificar al cliente de la nota de crédito; revisa su factura o anticipo de origen.");

        nc.SolicitarReembolso(importe, ahora);

        await eventos.PublishAsync(new NotaCreditoReembolsoSolicitadoIntegrationEvent(
            EmpresaId: nc.EmpresaId, OcurridoEn: ahora, NotaCreditoId: nc.Id, Folio: nc.Folio, Uuid: nc.Uuid,
            ClienteId: clienteId, ReceptorRfc: nc.ReceptorRfc, Importe: importe,
            TotalNotaCredito: nc.Total, Moneda: nc.Moneda, SucursalId: nc.SucursalId,
            Motivo: motivo.Trim(), SolicitadoPor: solicitadoPor,
            DestinoBanco: nc.ReembolsoBanco, DestinoClabe: nc.ReembolsoClabe, DestinoTitular: nc.ReembolsoTitular), ct);
    }

    /// <summary>
    /// F6.3: si la NC quedó timbrada con el reembolso programado, lo pide por el total. Se llama justo después de timbrar
    /// (emisión, autorización o reintento); sin reembolso programado no hace nada (F6.3-b).
    /// </summary>
    public static async Task SolicitarProgramadoSiAplicaAsync(
        FacturacionDbContext db, IIntegrationEventPublisher eventos, NotaCredito nc, Guid? usuarioId,
        DateTimeOffset ahora, CancellationToken ct)
    {
        if (!nc.DebeSolicitarReembolsoProgramado) return;
        // La NC ya está timbrada: aquí no se puede fallar. Sin cliente identificable (la emisión lo valida antes de timbrar)
        // el reembolso queda programado y se pide a mano desde la nota.
        if (await ClienteContableFacturacion.ResolverAsync(db, nc, ct) is null) return;
        await SolicitarAsync(db, eventos, nc, nc.Total, $"Devolución de dinero de la nota de crédito {nc.Folio}",
            usuarioId ?? nc.UsuarioEmisorId ?? Guid.Empty, ahora, ct);
    }

    /// <summary>Antes de timbrar con «Devolver dinero al cliente»: el cliente debe poder identificarse por su factura.</summary>
    public static async Task ExigirClienteAsync(FacturacionDbContext db, Domain.Comprobantes.Comprobante origen, CancellationToken ct)
    {
        if (await ClienteContableFacturacion.ResolverAsync(db, origen, ct) is null)
            throw new BusinessRuleException("NC_REEMBOLSO_SIN_CLIENTE",
                "No se pudo identificar al cliente de la factura; sin cliente no se puede devolver el dinero.");
    }
}
