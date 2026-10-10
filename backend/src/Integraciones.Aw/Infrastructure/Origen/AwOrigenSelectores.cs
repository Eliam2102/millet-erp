using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Millet.Facturacion.Domain.Ports;
using Millet.Integraciones.Aw.Application.Clientes;
using Millet.Integraciones.Aw.Application.Pedidos;
using Millet.Integraciones.Aw.Application.Productos;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Integraciones.Aw.Infrastructure.Origen;

/// <summary>Selectores por puerto. Los adaptadores se resuelven perezosamente: Demo jamás abre SQL Server.</summary>
public sealed class AwOrigenSelectores(IServiceProvider services, AwOrigenSesion sesion) :
    IAwClientesOrigen, IAwProductosOrigen, IAwSolicitudesReader, IAwClientesReader, IAwArticulosReader,
    IAwWriteBackPort, IMasterProvisioningPort
{
    private async Task<T> ResolverAsync<T>(CancellationToken ct) where T : class
    {
        var key = await sesion.EsDemoAsync(ct) ? "Demo" : "Real";
        var adaptador = services.GetKeyedService<T>(key);
        if (adaptador is not null) return adaptador;
        if (key == "Real" && typeof(T) == typeof(IAwClientesOrigen))
            throw new AwClientesSyncException("origen_sin_configurar",
                $"Origen '{services.GetRequiredService<IOptions<AwClientesOptions>>().Value.Origen}' sin adaptador: falta ConnectionStrings:{AwClientesOptions.ConnectionStringName}.");
        throw new BusinessRuleException("AW_ORIGEN_SIN_CONFIGURAR",
                "El origen real de A+W no tiene adaptador configurado para esta operación. Revise la configuración del área.");
    }

    public async Task<AwClienteOrigenFila?> LeerPorReferenciaAsync(string referencia, CancellationToken ct)
    {
        sesion.Reiniciar();
        var fila = await (await ResolverAsync<IAwClientesOrigen>(ct)).LeerPorReferenciaAsync(referencia, ct);
        return fila is not null && !await sesion.EsDemoAsync(ct) ? fila with { AplicarFiscalesDeOrigen = false } : fila;
    }
    public async Task<AwClientesPagina> LeerPaginaAsync(string? cursor, int tamano, CancellationToken ct)
    {
        if (cursor is null) sesion.Reiniciar();
        var pagina = await (await ResolverAsync<IAwClientesOrigen>(ct)).LeerPaginaAsync(cursor, tamano, ct);
        return await sesion.EsDemoAsync(ct) ? pagina : pagina with
        {
            Filas = pagina.Filas.Select(f => f with { AplicarFiscalesDeOrigen = false }).ToArray(),
        };
    }
    async Task<AwProductoOrigenFila?> IAwProductosOrigen.LeerPorReferenciaAsync(string referencia, CancellationToken ct)
    {
        sesion.Reiniciar();
        return await (await ResolverAsync<IAwProductosOrigen>(ct)).LeerPorReferenciaAsync(referencia, ct);
    }
    async Task<AwProductosPagina> IAwProductosOrigen.LeerPaginaAsync(string? cursor, int tamano, CancellationToken ct)
    {
        if (cursor is null) sesion.Reiniciar();
        return await (await ResolverAsync<IAwProductosOrigen>(ct)).LeerPaginaAsync(cursor, tamano, ct);
    }
    public async Task<IReadOnlyList<SolicitudAw>> LeerPendientesAsync(int max, CancellationToken cancellationToken)
    {
        sesion.Reiniciar();
        return await (await ResolverAsync<IAwSolicitudesReader>(cancellationToken)).LeerPendientesAsync(max, cancellationToken);
    }
    public async Task<LecturaPedidoAw> LeerDatosPedidoAsync(string numeroPedido, CancellationToken cancellationToken) =>
        await (await ResolverAsync<IAwSolicitudesReader>(cancellationToken)).LeerDatosPedidoAsync(numeroPedido, cancellationToken);
    public async Task<AwClienteMaster?> LeerClienteAsync(string clienteRef, CancellationToken cancellationToken) =>
        await (await ResolverAsync<IAwClientesReader>(cancellationToken)).LeerClienteAsync(clienteRef, cancellationToken);
    public async Task<AwArticuloMaster?> LeerArticuloAsync(string articuloRef, CancellationToken cancellationToken) =>
        await (await ResolverAsync<IAwArticulosReader>(cancellationToken)).LeerArticuloAsync(articuloRef, cancellationToken);
    public async Task EscribirResultadoAsync(AwWriteBack writeBack, CancellationToken cancellationToken) =>
        await (await ResolverAsync<IAwWriteBackPort>(cancellationToken)).EscribirResultadoAsync(writeBack, cancellationToken);
    public async Task<ClienteFiscalLectura?> EnsureClienteDesdeAwAsync(string clienteRef, CancellationToken cancellationToken) =>
        await (await ResolverAsync<IMasterProvisioningPort>(cancellationToken)).EnsureClienteDesdeAwAsync(clienteRef, cancellationToken);
    public async Task<ProductoFiscalLectura?> EnsureArticuloDesdeAwAsync(string articuloRef, CancellationToken cancellationToken) =>
        await (await ResolverAsync<IMasterProvisioningPort>(cancellationToken)).EnsureArticuloDesdeAwAsync(articuloRef, cancellationToken);
}
