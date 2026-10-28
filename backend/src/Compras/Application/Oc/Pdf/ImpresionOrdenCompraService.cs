using System.Collections.Immutable;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Compras.Domain;
using Millet.Compras.Domain.Oc;
using Millet.Compras.Domain.Ports.Catalogos;
using Millet.Compras.Domain.Ports.DatosMaestros;
using Millet.Compras.Domain.Ports.Identidad;
using Millet.Compras.Domain.Ports.Pdf;
using Millet.Compras.Infrastructure;
using Millet.DatosMaestros.Domain;
using Millet.Documentos;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Blob;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compras.Application.Oc.Pdf;

/// <summary>
/// Arma el modelo de impresión de una OC (FMT-2) con los datos vigentes de empresa, proveedor,
/// sucursal, catálogos y firmas, y lo manda al generador. La OC se imprime siempre al momento:
/// el PDF guardado al autorizar es solo una foto de ese instante. Todo dato faltante se omite
/// (un proveedor sin domicilio no impide el PDF).
/// </summary>
public class ImpresionOrdenCompraService(
    ComprasDbContext db, CompartidoDbContext compartido, ICurrentEmpresaContext empresaContext,
    IBlobStoragePort blobs, IArticuloReadPort articulos, IUsuarioReadPort usuarios,
    ICatalogoSatReadPort catalogoSat, IGenerarPdfOrdenCompraPort generador,
    ILogger<ImpresionOrdenCompraService> logger)
{
    /// <summary>1 = ORIGINAL; 2 en adelante = COPIA (n-1) con la fecha de impresión.</summary>
    public static string Marca(int impresion, DateOnly hoy) => impresion <= 1
        ? "ORIGINAL"
        : $"COPIA {impresion - 1} · {hoy.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)}";

    // virtual: las pruebas del envío lo sustituyen sin montar los catálogos.
    public virtual async Task<PdfOrdenCompraGenerado> RenderizarAsync(Guid ordenCompraId, string? marca, CancellationToken cancellationToken) =>
        await generador.GenerarAsync(await ConstruirAsync(ordenCompraId, cancellationToken), marca, cancellationToken);

    public async Task<OrdenCompraImpresion> ConstruirAsync(Guid ordenCompraId, CancellationToken cancellationToken)
    {
        var oc = await db.OrdenesCompra.AsNoTracking().Include(o => o.Lineas).Include(o => o.Autorizaciones)
            .FirstOrDefaultAsync(o => o.Id == ordenCompraId, cancellationToken)
            ?? throw new EntityNotFoundException("ORDEN_COMPRA_NO_ENCONTRADA", "No se encontró la orden de compra.");
        var totales = oc.CalcularTotales();

        var rqIds = oc.Lineas.Where(l => l.RequisicionId.HasValue).Select(l => l.RequisicionId!.Value).Distinct().ToArray();
        var motivos = await db.Requisiciones.AsNoTracking()
            .Where(r => rqIds.Contains(r.Id) && r.Descripcion != null).Select(r => r.Descripcion!).Distinct()
            .ToListAsync(cancellationToken);
        if (motivos.Count == 0 && !string.IsNullOrWhiteSpace(oc.MotivoSinRequisicion)) motivos.Add(oc.MotivoSinRequisicion);
        // R7: solo el motivo de la requisición; las observaciones de la OC son internas y no van al proveedor.
        var motivo = string.Join("\n", motivos).Trim();

        var datosArticulos = await articulos.ObtenerPorIdsAsync(
            oc.Lineas.Select(l => l.ArticuloId).Distinct().ToArray(), cancellationToken);
        // R6: se agrupa solo lo idéntico para no perder comentarios ni mezclar precios.
        var renglones = oc.Lineas.OrderBy(l => l.Posicion)
            .GroupBy(l => (l.ArticuloId, l.ProductoAwVarianteId, l.TextoAdicional, l.PrecioUnitario, l.UnidadMedida))
            .Select(g =>
            {
                var l = g.First();
                var art = datosArticulos.GetValueOrDefault(l.ArticuloId);
                var subtotal = g.Sum(x => x.SubtotalLinea);
                var descripcion = new List<string> { art?.Nombre ?? "" };
                if (l.AnchoMm is { } ancho && l.AltoMm is { } alto) descripcion.Add($"{ancho:0.##} × {alto:0.##} mm");
                if (!string.IsNullOrWhiteSpace(l.DescripcionExtendida)) descripcion.Add(l.DescripcionExtendida);
                var iva = g.Sum(x => x.IvaImporte);
                return new RenglonImpresion(art?.Clave ?? "", string.Join("\n", descripcion.Where(x => x.Length > 0)),
                    l.TextoAdicional, g.Sum(x => x.Cantidad), l.UnidadMedida, l.PrecioUnitario,
                    subtotal == 0m ? 0m : Math.Round(iva / subtotal * 100m, 2), subtotal);
            }).ToImmutableArray();

        var autorizadas = oc.Autorizaciones
            .Where(a => a.Ciclo == oc.CicloAutorizacion && a.Resultado == ResultadoAutorizacionOc.Autorizado).ToList();
        var n1 = autorizadas.FirstOrDefault(a => a.Nivel == NivelAutorizacion.Nivel1);
        var n2 = autorizadas.FirstOrDefault(a => a.Nivel == NivelAutorizacion.Nivel2);
        // Con solo N1, ese aprobador firma como Autorizó y Revisó queda vacío.
        var (reviso, autorizo) = n2 is null ? (null, n1) : (n1, n2);
        var nombres = await usuarios.ObtenerNombresAsync(
            new[] { oc.CompradorTitularId, reviso?.UsuarioId, autorizo?.UsuarioId }
                .OfType<Guid>().Distinct().ToArray(), cancellationToken);
        FirmaImpresion Firma(AutorizacionOC? a) => a is null ? FirmaImpresion.Vacia
            : new(nombres.GetValueOrDefault(a.UsuarioId), FechaContable.HoyLocal(a.FechaHora));

        using var bypass = empresaContext.Bypass();
        var empresa = await compartido.Empresas.AsNoTracking().FirstOrDefaultAsync(e => e.Id == oc.EmpresaId, cancellationToken);
        var prov = await compartido.Proveedores.AsNoTracking().FirstOrDefaultAsync(p => p.Id == oc.ProveedorId, cancellationToken);
        var sucursal = await compartido.Sucursales.AsNoTracking().FirstOrDefaultAsync(s => s.Id == oc.SucursalDestinoId, cancellationToken);
        var empleado = await compartido.Empleados.AsNoTracking()
            .FirstOrDefaultAsync(e => e.EmpresaId == oc.EmpresaId && e.UsuarioId == oc.CompradorTitularId, cancellationToken);
        var condicion = await compartido.CondicionesPago.AsNoTracking()
            .Where(c => c.Id == oc.CondicionesPagoId).Select(c => c.Nombre).FirstOrDefaultAsync(cancellationToken);
        var extranjero = prov?.Origen == OrigenProveedor.Extranjero;
        string? incoterm = null;
        if (extranjero && oc.InfoImportIncotermId is { } incotermId)
            incoterm = await compartido.Incoterms.AsNoTracking().Where(i => i.Id == incotermId)
                .Select(i => i.Codigo + " · " + i.Nombre).FirstOrDefaultAsync(cancellationToken);

        var uso = oc.UsoCfdiClave is { Length: > 0 } u ? Clave(u, (await catalogoSat.ObtenerUsoCfdiAsync(u, cancellationToken))?.Descripcion) : null;
        var forma = oc.FormaPagoClave is { Length: > 0 } f ? Clave(f, (await catalogoSat.ObtenerFormaPagoAsync(f, cancellationToken))?.Descripcion) : null;
        var metodo = oc.MetodoPago switch
        {
            "PUE" => "PUE · Pago en una sola exhibición",
            "PPD" => "PPD · Pago en parcialidades o diferido",
            var m => m,
        };

        return new OrdenCompraImpresion
        {
            Folio = oc.Folio.Valor,
            Moneda = oc.Moneda,
            FechaDocumento = oc.FechaDocumento,
            FechaEntrega = oc.FechaEntregaEsperada,
            Referencia = oc.ReferenciaProveedor,
            Empresa = new EmpresaEncabezado(empresa?.RazonSocial ?? "", empresa?.NombreComercial, empresa?.Rfc ?? "",
                empresa?.RegimenFiscal ?? "",
                empresa is null ? [] : Domicilio(empresa.Calle, empresa.NumeroExterior, empresa.NumeroInterior,
                    empresa.Colonia, empresa.Ciudad, empresa.Municipio, empresa.Estado, empresa.CodigoPostal, empresa.Pais),
                await LogoAsync(empresa?.LogoBlobRef, cancellationToken)),
            Proveedor = new ProveedorImpresion(prov?.Clave ?? "", prov?.RazonSocial ?? "", prov?.Rfc ?? "",
                prov is null ? [] : Domicilio(prov.Calle, prov.NumeroExterior, prov.NumeroInterior, prov.Colonia,
                    prov.Ciudad, prov.Municipio, prov.Estado, prov.CodigoPostal, prov.Pais),
                extranjero ? prov!.TaxId : null),
            Contacto = new ContactoImpresion(
                empleado?.Nombre ?? nombres.GetValueOrDefault(oc.CompradorTitularId),
                empleado is null ? null : string.Join(" Ext. ", new[] { empleado.Telefono, empleado.Extension }
                    .Where(x => !string.IsNullOrWhiteSpace(x))),
                empleado?.Email ?? empleado?.EmailContacto),
            EntregaNombre = sucursal?.Nombre ?? "",
            EntregaLineas = sucursal is null ? [] : Domicilio(sucursal.Calle, sucursal.NumeroExterior, sucursal.NumeroInterior,
                sucursal.Colonia, sucursal.Ciudad, sucursal.Municipio, sucursal.Estado, sucursal.CodigoPostal, sucursal.Pais),
            UsoCfdi = uso,
            MetodoPago = metodo,
            FormaPago = forma,
            CondicionPago = condicion,
            Incoterm = incoterm,
            Renglones = renglones,
            Subtotal = totales.SubtotalAntesDescuento,
            Descuento = totales.DescuentoGlobalAplicado,
            OtrosCargos = oc.GastosAdicionales,
            BaseGravable = totales.BaseGravable,
            Impuesto = totales.IvaTotal,
            Retenciones = totales.RetencionIsrTotal,
            Redondeo = oc.Redondeo,
            Total = totales.TotalAPagar,
            Motivo = motivo,
            Elaboro = new(empleado?.Nombre ?? nombres.GetValueOrDefault(oc.CompradorTitularId), oc.FechaDocumento),
            Reviso = Firma(reviso),
            Autorizo = Firma(autorizo),
        };
    }

    private static string Clave(string clave, string? descripcion) =>
        string.IsNullOrWhiteSpace(descripcion) ? clave : $"{clave} · {descripcion}";

    /// <summary>Líneas de domicilio al estilo del formato de Millet; omite lo que no se capturó.</summary>
    private static ImmutableArray<string> Domicilio(string? calle, string? exterior, string? interior, string? colonia,
        string? ciudad, string? municipio, string? estado, string? cp, string? pais)
    {
        string? Unir(string sep, params string?[] partes) => string.Join(sep, partes.Where(p => !string.IsNullOrWhiteSpace(p)));
        var interiorTxt = string.IsNullOrWhiteSpace(interior) ? null : "Int. " + interior;
        return new[]
        {
            Unir(" ", calle, exterior, interiorTxt), colonia, Unir(", ", ciudad, municipio),
            Unir(", ", cp, estado), pais,
        }.Where(l => !string.IsNullOrWhiteSpace(l)).Select(l => l!).ToImmutableArray();
    }

    private async Task<byte[]?> LogoAsync(string? blobRef, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(blobRef)) return null;
        try
        {
            await using var stream = await blobs.ObtenerStreamAsync(blobRef, cancellationToken);
            using var memoria = new MemoryStream();
            await stream.CopyToAsync(memoria, cancellationToken);
            return memoria.ToArray();
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            // Sin logo se imprime el nombre comercial; el PDF no debe fallar por la imagen.
            logger.LogWarning(ex, "No se pudo leer el logo de la empresa para el PDF de la OC.");
            return null;
        }
    }
}
