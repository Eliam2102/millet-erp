using MediatR;
using Millet.CuentasPorCobrar.Domain.Ports.DatosMaestros;
using Millet.CuentasPorCobrar.Domain.Ports.Facturacion;
using Millet.CuentasPorCobrar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorCobrar.Application.Reportes.EstadoCuenta;

/// <summary>
/// Pagos que bloquean el estado de cuenta del cliente (C7.8, CA7.14): la
/// pantalla los muestra en lugar del reporte para que el usuario sepa qué
/// REP falta emitir o reflejar.
/// </summary>
public sealed record PendientesEstadoCuentaQuery(Guid ClienteId)
    : IRequest<PendientesEstadoCuentaResponse>, IDocumentoScopedQuery
{
    public string PermisoTodasSucursales => "cuentas_por_cobrar.cartera.leer-todas-sucursales";
    public string TipoDocumento => "cliente_cartera";
    public IReadOnlyList<Guid>? SucursalesPermitidas { get; set; }
    public IReadOnlyList<Guid>? DocumentosPermitidos { get; set; }
}

public sealed class PendientesEstadoCuentaHandler(
    CuentasPorCobrarDbContext db, IReppPendientesReadPort? repp = null, IClienteReadPort? clientes = null)
    : IRequestHandler<PendientesEstadoCuentaQuery, PendientesEstadoCuentaResponse>
{
    public async Task<PendientesEstadoCuentaResponse> Handle(PendientesEstadoCuentaQuery query, CancellationToken cancellationToken)
    {
        EstadoCuentaAlcance.Verificar(query.ClienteId, query.DocumentosPermitidos);
        var evaluacion = await new EstadoCuentaBloqueo(db, repp).EvaluarAsync(query.ClienteId, cancellationToken);
        // Correo del maestro de clientes para prellenar el destinatario del envío.
        var cliente = clientes is null ? null : await clientes.ObtenerAsync(query.ClienteId, cancellationToken);
        return evaluacion with { EmailCliente = string.IsNullOrWhiteSpace(cliente?.Email) ? null : cliente.Email.Trim() };
    }
}

/// <summary>
/// Alcance por sucursal (ADR-0051) de las operaciones del estado de cuenta
/// que actúan sobre el cliente completo: el usuario operativo debe cubrir
/// todas las sucursales de la cartera del cliente ("cliente_cartera").
/// </summary>
internal static class EstadoCuentaAlcance
{
    public static void Verificar(Guid clienteId, IReadOnlyList<Guid>? clientesPermitidos)
    {
        if (clientesPermitidos is not null && !clientesPermitidos.Contains(clienteId))
            throw new ForbiddenException("SUCURSAL_FUERA_DE_ALCANCE",
                "El cliente tiene cartera en sucursales fuera de tu alcance.");
    }
}
