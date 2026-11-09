using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Millet.Compras.Infrastructure;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.CuentasPorPagar.Application.Cfdi.IngresarCfdi;
using Millet.CuentasPorPagar.Domain.Cfdi;
using Millet.CuentasPorPagar.Domain.Ports.DatosMaestros;
using Millet.CuentasPorPagar.Infrastructure.GeneradorPrueba;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorPagar.Application.Cfdi.SimularCfdi;

/// <summary>
/// Handler de <see cref="GenerarEIngresarCfdiPruebaCommand"/>.
/// Genera e ingresa un comprobante CFDI 4.0 sintético en base a los datos de una Orden de Compra.
/// Protegido contra ejecución en ambientes de producción e idempotente por (EmpresaId, Emisor, Folio, Estado=PorProcesar).
/// </summary>
public sealed class GenerarEIngresarCfdiPruebaHandler : IRequestHandler<GenerarEIngresarCfdiPruebaCommand, GenerarEIngresarCfdiPruebaResponse>
{
    private readonly ComprasDbContext _comprasDb;
    private readonly CuentasPorPagarDbContext _cxpDb;
    private readonly CompartidoDbContext _compartidoDb;
    private readonly IProveedorReadPort _proveedores;
    private readonly IGeneradorCfdiPruebaService _generador;
    private readonly ISender _mediator;
    private readonly IHostEnvironment _environment;
    private readonly ICurrentEmpresaContext? _empresaContext;

    public GenerarEIngresarCfdiPruebaHandler(
        ComprasDbContext comprasDb,
        CuentasPorPagarDbContext cxpDb,
        CompartidoDbContext compartidoDb,
        IProveedorReadPort proveedores,
        IGeneradorCfdiPruebaService generador,
        ISender mediator,
        IHostEnvironment environment,
        ICurrentEmpresaContext? empresaContext = null)
    {
        _comprasDb = comprasDb;
        _cxpDb = cxpDb;
        _compartidoDb = compartidoDb;
        _proveedores = proveedores;
        _generador = generador;
        _mediator = mediator;
        _environment = environment;
        _empresaContext = empresaContext;
    }

    public async Task<GenerarEIngresarCfdiPruebaResponse> Handle(
        GenerarEIngresarCfdiPruebaCommand request,
        CancellationToken cancellationToken)
    {
        if (_environment.IsProduction())
        {
            throw new InvalidOperationException("La simulación de CFDI no está permitida en entorno de producción.");
        }

        using var _ = _empresaContext?.Bypass();

        var oc = _comprasDb.OrdenesCompra.Local.FirstOrDefault(o => o.Id == request.OrdenCompraId);
        if (oc is null)
        {
            var existe = await _comprasDb.OrdenesCompra.AnyAsync(o => o.Id == request.OrdenCompraId, cancellationToken);
            if (!existe)
            {
                throw new EntityNotFoundException("ORDEN_COMPRA_NO_ENCONTRADA", $"No se encontró la orden de compra '{request.OrdenCompraId}'.");
            }

            oc = await _comprasDb.OrdenesCompra
                .Include(o => o.Lineas)
                .FirstOrDefaultAsync(o => o.Id == request.OrdenCompraId, cancellationToken);
        }
        else if (oc.Lineas.Count == 0 && _comprasDb.Database.ProviderName != "Microsoft.EntityFrameworkCore.InMemory")
        {
            if (!_comprasDb.Entry(oc).Collection(o => o.Lineas).IsLoaded)
            {
                await _comprasDb.Entry(oc).Collection(o => o.Lineas).LoadAsync(cancellationToken);
            }
        }

        if (oc is null)
        {
            throw new EntityNotFoundException("ORDEN_COMPRA_NO_ENCONTRADA", $"No se encontró la orden de compra '{request.OrdenCompraId}'.");
        }

        var proveedor = await _proveedores.ObtenerAsync(oc.ProveedorId, cancellationToken)
            ?? throw new EntityNotFoundException("PROVEEDOR_NO_ENCONTRADO", $"No se encontró el proveedor '{oc.ProveedorId}'.");

        var empresa = await _compartidoDb.Empresas
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == oc.EmpresaId, cancellationToken)
            ?? throw new EntityNotFoundException("EMPRESA_NO_ENCONTRADA", $"No se encontró la empresa '{oc.EmpresaId}'.");

        var folioCfdi = oc.Folio.Valor;
        var rfcEmisor = RfcMexicano.Parse(proveedor.Rfc);

        // Verificación de idempotencia: si ya existe un CFDI PorProcesar para esta OC con mismo emisor y folio, devolverlo
        var cfdiExistente = await _cxpDb.CfdisRecibidos
            .AsNoTracking()
            .FirstOrDefaultAsync(c =>
                c.EmpresaId == oc.EmpresaId &&
                c.RfcEmisor == rfcEmisor &&
                c.Folio == folioCfdi &&
                c.Estado == EstadoCfdiRecibido.PorProcesar,
                cancellationToken);

        if (cfdiExistente is not null)
        {
            return new GenerarEIngresarCfdiPruebaResponse(
                cfdiExistente.Id,
                cfdiExistente.UuidCfdi.Valor,
                cfdiExistente.Folio ?? folioCfdi,
                cfdiExistente.Total);
        }

        // Enriquecer datos de artículos de las líneas
        var articuloIds = oc.Lineas.Select(l => l.ArticuloId).Distinct().ToList();
        var articulos = await _compartidoDb.Articulos
            .AsNoTracking()
            .Where(a => articuloIds.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, cancellationToken);

        var lineasCfdi = new List<LineaCfdiPrueba>();

        foreach (var l in oc.Lineas.OrderBy(x => x.Posicion))
        {
            decimal cantidad;
            if (request.CantidadesPorLineaId is not null && request.CantidadesPorLineaId.TryGetValue(l.Id, out var cantLinea))
            {
                cantidad = cantLinea;
            }
            else if (request.CantidadesPorPosicion is not null && request.CantidadesPorPosicion.TryGetValue(l.Posicion, out var cantEspecifica))
            {
                cantidad = cantEspecifica;
            }
            else
            {
                cantidad = Math.Max(0m, l.Cantidad - l.CantidadFacturada);
            }

            if (cantidad <= 0m)
            {
                continue;
            }

            articulos.TryGetValue(l.ArticuloId, out var art);
            var clave = art?.Clave;
            var nombre = art?.Nombre ?? l.DescripcionExtendida ?? $"Partida {l.Posicion}";

            var importeBruto = decimal.Round(cantidad * l.PrecioUnitario, 2, MidpointRounding.AwayFromZero);
            var baseGravable = l.Cantidad > 0m
                ? Math.Min(importeBruto, decimal.Round(cantidad * l.SubtotalLinea / l.Cantidad, 2, MidpointRounding.AwayFromZero))
                : importeBruto;
            var descuento = decimal.Round(importeBruto - baseGravable, 2, MidpointRounding.AwayFromZero);

            var tasaIva = l.SubtotalLinea > 0m && l.IvaImporte > 0m
                ? decimal.Round(l.IvaImporte / l.SubtotalLinea, 2, MidpointRounding.AwayFromZero)
                : (l.IvaImporte == 0m ? 0m : 0.16m);

            var retencionIsr = l.RetencionIsr.HasValue && l.RetencionIsr.Value > 0m && l.Cantidad > 0m
                ? decimal.Round((l.RetencionIsr.Value * cantidad) / l.Cantidad, 2, MidpointRounding.AwayFromZero)
                : 0m;

            decimal? tasaRetencionIsr = retencionIsr > 0m && baseGravable > 0m
                ? decimal.Round(retencionIsr / baseGravable, 6, MidpointRounding.AwayFromZero)
                : null;

            lineasCfdi.Add(new LineaCfdiPrueba(
                Posicion: l.Posicion,
                Cantidad: cantidad,
                PrecioUnitario: l.PrecioUnitario,
                ArticuloClave: clave,
                ArticuloNombre: nombre,
                DescripcionExtendida: l.DescripcionExtendida,
                ClaveProdServ: "01010101",
                ClaveUnidad: "H87",
                UnidadMedida: !string.IsNullOrWhiteSpace(l.UnidadMedida) ? l.UnidadMedida : "PZA",
                Descuento: descuento,
                TasaIva: tasaIva,
                RetencionIsr: retencionIsr,
                TasaRetencionIsr: tasaRetencionIsr));
        }

        if (lineasCfdi.Count == 0)
        {
            throw new BusinessRuleException("OC_SIN_LINEAS_FACTURABLES", "No hay partidas pendientes por facturar en la orden de compra.");
        }

        var cpReceptor = !string.IsNullOrWhiteSpace(empresa.CodigoPostal) ? empresa.CodigoPostal.Trim() : "00000";
        var regimenReceptor = !string.IsNullOrWhiteSpace(empresa.RegimenFiscal) ? empresa.RegimenFiscal.Trim() : "601";
        var usoCfdi = !string.IsNullOrWhiteSpace(oc.UsoCfdiClave) ? oc.UsoCfdiClave.Trim() : "G03";
        var metodoPago = !string.IsNullOrWhiteSpace(oc.MetodoPago) ? oc.MetodoPago.Trim() : "PPD";
        var formaPago = !string.IsNullOrWhiteSpace(oc.FormaPagoClave)
            ? oc.FormaPagoClave.Trim()
            : (metodoPago == "PUE" ? "03" : "99");

        var datos = new DatosGeneracionCfdiPrueba(
            Emisor: new EmisorCfdiPrueba(proveedor.Rfc, proveedor.RazonSocial, "601"),
            Receptor: new ReceptorCfdiPrueba(empresa.Rfc, empresa.RazonSocial, cpReceptor, regimenReceptor, usoCfdi),
            Lineas: lineasCfdi,
            Folio: folioCfdi,
            Serie: "PRUEBA",
            Moneda: !string.IsNullOrWhiteSpace(oc.Moneda) ? oc.Moneda.Trim() : "MXN",
            TipoCambio: oc.TipoCambio,
            MetodoPago: metodoPago,
            FormaPago: formaPago,
            UsoCfdi: usoCfdi,
            LugarExpedicion: cpReceptor);

        using var xmlStream = _generador.GenerarXmlStream(datos);

        var ingresarCmd = new IngresarCfdiCommand(
            Xml: xmlStream,
            Pdf: null,
            Canal: CanalOrigenCfdi.Mailbox,
            EmpresaIdOverride: oc.EmpresaId);

        var resp = await _mediator.Send(ingresarCmd, cancellationToken);

        var cfdi = await _cxpDb.CfdisRecibidos
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == resp.Id, cancellationToken);

        var subtotal = decimal.Round(lineasCfdi.Sum(l => decimal.Round(l.Cantidad * l.PrecioUnitario, 2, MidpointRounding.AwayFromZero)), 2, MidpointRounding.AwayFromZero);
        var descTotal = decimal.Round(lineasCfdi.Sum(l => l.Descuento), 2, MidpointRounding.AwayFromZero);
        var ivaTotal = decimal.Round(lineasCfdi.Sum(l => decimal.Round((decimal.Round(l.Cantidad * l.PrecioUnitario, 2, MidpointRounding.AwayFromZero) - l.Descuento) * l.TasaIva, 2, MidpointRounding.AwayFromZero)), 2, MidpointRounding.AwayFromZero);
        var retTotal = decimal.Round(lineasCfdi.Sum(l => l.RetencionIsr), 2, MidpointRounding.AwayFromZero);
        var totalCalculado = decimal.Round(subtotal - descTotal + ivaTotal - retTotal, 2, MidpointRounding.AwayFromZero);

        return new GenerarEIngresarCfdiPruebaResponse(
            resp.Id,
            resp.UuidCfdi,
            cfdi?.Folio ?? folioCfdi,
            cfdi?.Total ?? totalCalculado);
    }
}
