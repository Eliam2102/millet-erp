using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Millet.DatosMaestros.Application.Clientes;
using Millet.DatosMaestros.Application.ProductosAw;
using Millet.Facturacion.Domain.Ports;
using Millet.Integraciones.Aw.Application.Pedidos;

namespace Millet.Integraciones.Aw.Infrastructure.Pedidos;

/// <summary>
/// Adapter REAL de <see cref="IMasterProvisioningPort"/> (ADR-0048 D6;
/// cierra PLATFORM-TODO(&lt;MasterProvisioningAw&gt;)). Bridge del único caso
/// de alta automática del master desde una vista (§3.bis.6, solo origen
/// A+W): lee la vista (readers de esta sub-área) y delega el alta a los
/// commands de DatosMaestros vía MediatR — cero escritura directa al
/// esquema de otro módulo.
///
/// <para>
/// Vive en Integraciones.Aw por grafo de dependencias (documentado en el
/// ADR): Compartido no puede referenciar Facturación (ciclo), así que "el
/// puerto lo expone DatosMaestros" se materializa como "DatosMaestros
/// ejecuta el alta; el bridge lo hospeda Integraciones.Aw" — este proyecto
/// ya referencia a ambos.
/// </para>
///
/// <para>
/// Devuelve <c>null</c> si la vista no trae el registro (la matriz de
/// ingesta cae a la bandeja con ClienteNoExiste/ArticuloNoExiste). El
/// upsert es idempotente por referencia externa — reintentos y carreras
/// entre líneas del mismo pedido devuelven el registro existente.
/// </para>
/// </summary>
public sealed class AwMasterProvisioningAdapter : IMasterProvisioningPort
{
    private readonly IAwClientesReader _clientes;
    private readonly IAwArticulosReader _articulos;
    private readonly ISender _sender;
    private readonly AwPedidosOptions _options;
    private readonly ILogger<AwMasterProvisioningAdapter> _logger;

    public AwMasterProvisioningAdapter(
        IAwClientesReader clientes,
        IAwArticulosReader articulos,
        ISender sender,
        IOptions<AwPedidosOptions> options,
        ILogger<AwMasterProvisioningAdapter> logger)
    {
        _clientes = clientes;
        _articulos = articulos;
        _sender = sender;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<ClienteFiscalLectura?> EnsureClienteDesdeAwAsync(
        string clienteRef, CancellationToken cancellationToken)
    {
        var vista = await _clientes.LeerClienteAsync(clienteRef, cancellationToken);
        if (vista is null || string.IsNullOrWhiteSpace(vista.RazonSocial))
        {
            _logger.LogWarning(
                "[AwMasterProvisioning] cliente '{Ref}' no está en vw_erp_cliente (o sin razón social) → null",
                clienteRef);
            return null;
        }

        // Receptor extranjero (CCE): solo se puebla si el país de A+W es una
        // clave ISO-3 válida y ≠ MEX (cliente de exportación). Así no metemos
        // domicilios MX en campos de "extranjero" ni rompemos la validación
        // si A+W manda el país en formato libre (p.ej. "MEXICO").
        var paisExt = PaisIso3(vista.Pais);
        var esExtranjero = paisExt is not null && paisExt != "MEX";

        var r = await _sender.Send(
            new ProvisionarClienteDesdeAwCommand(
                ReferenciaExterna: vista.ClienteRef,
                RazonSocial: vista.RazonSocial,
                Rfc: vista.Rfc,
                Telefono: vista.Telefono,
                // KU_KUNDEN.PLZ sí trae CP (validado con datos reales
                // 2026-07-09) — se pasa como prefill del CP fiscal solo si
                // son 5 dígitos exactos; basura de captura de A+W → null.
                CodigoPostalFiscal: CpValido(vista.Cp),
                // Defaults fiscales del cliente: por ahora solo lo que la
                // vista aporta; uso CFDI/forma/método por pedido viajan en el
                // snapshot y el operador completa defaults en la UI (G12).
                UsoCfdiDefault: null,
                FormaPagoDefault: null,
                MetodoPagoDefault: null,
                MonedaDefault: null,
                NumRegIdTrib: vista.NumRegIdTrib,
                PaisResidencia: esExtranjero ? paisExt : null,
                DomicilioExtranjeroCalle: esExtranjero ? Limitar(vista.Calle, 200) : null,
                DomicilioExtranjeroEstado: esExtranjero ? Limitar(vista.Estado, 100) : null,
                DomicilioExtranjeroCodigoPostal: esExtranjero ? Limitar(vista.Cp, 12) : null),
            cancellationToken);

        if (r.Creado)
        {
            _logger.LogInformation(
                "[AwMasterProvisioning] cliente '{Ref}' auto-provisionado ({ClienteId}); fiscales completos: {Completos}",
                clienteRef, r.ClienteId,
                r.Rfc is not null && r.RegimenFiscal is not null && r.CodigoPostalFiscal is not null);
        }

        return new ClienteFiscalLectura(
            r.ClienteId, r.Rfc, r.RazonSocial, r.RegimenFiscal,
            r.CodigoPostalFiscal, r.UsoCfdiDefault, r.FormaPagoDefault,
            r.MetodoPagoDefault, r.MonedaDefault, r.EsGenerico);
    }

    private static string? CpValido(string? cp) =>
        cp is { Length: 5 } && cp.All(char.IsAsciiDigit) ? cp : null;

    /// <summary>País ISO 3166-1 alfa-3 en mayúsculas, o null si A+W lo manda en formato libre.</summary>
    private static string? PaisIso3(string? pais) =>
        pais is not null && System.Text.RegularExpressions.Regex.IsMatch(pais.Trim(), "^[A-Za-z]{3}$")
            ? pais.Trim().ToUpperInvariant()
            : null;

    /// <summary>Recorta a un máximo (defensivo contra longitudes de A+W que romperían el alta).</summary>
    private static string? Limitar(string? s, int max) =>
        string.IsNullOrWhiteSpace(s) ? null : (s.Length > max ? s[..max] : s);

    public async Task<ProductoFiscalLectura?> EnsureArticuloDesdeAwAsync(
        string articuloRef, CancellationToken cancellationToken)
    {
        var vista = await _articulos.LeerArticuloAsync(articuloRef, cancellationToken);
        if (vista is null || string.IsNullOrWhiteSpace(vista.Descripcion))
        {
            _logger.LogWarning(
                "[AwMasterProvisioning] artículo '{Ref}' no está en vw_erp_articulo (o sin descripción) → null",
                articuloRef);
            return null;
        }

        // MapeoUnidadSat propone la clave unidad SAT desde la unidad A+W
        // (M2→MTK, PZA→H87, …); sin mapeo queda null → la completa el operador.
        _options.MapeoUnidadSat.TryGetValue(vista.UnidadMedida, out var claveUnidadSugerida);

        var r = await _sender.Send(
            new ProvisionarProductoAwCommand(
                ReferenciaExterna: vista.ProductoRef,
                Descripcion: vista.Descripcion,
                UnidadMedida: vista.UnidadMedida,
                ClaveUnidadSatSugerida: claveUnidadSugerida,
                // Contrato "como si A+W lo mandara": hoy null (la vista aún no
                // los trae); cuando A+W los agregue viajan al alta del master.
                FraccionArancelaria: vista.FraccionArancelaria,
                PesoUnitarioKg: vista.PesoUnitarioKg),
            cancellationToken);

        if (r.Creado)
        {
            _logger.LogInformation(
                "[AwMasterProvisioning] producto '{Ref}' auto-provisionado ({ProductoId}); claves SAT completas: {Completas}",
                articuloRef, r.ProductoId,
                r.ClaveProdServSat is not null && r.ClaveUnidadSat is not null);
        }

        return new ProductoFiscalLectura(
            r.ProductoId, r.Descripcion, r.ClaveProdServSat, r.ClaveUnidadSat,
            r.ObjetoImp, r.TasaIvaTraslado, r.TasaRetencionIva,
            r.TasaRetencionIsr, r.Origen);
    }
}
