using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Documentos;
using Millet.Facturacion.Application.Cajas.Sesiones;
using Millet.Facturacion.Domain.Cajas;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.Application.Cajas.Vales;

/// <summary>
/// PDF del Vale de Caja de un retiro (86e3ww6tw). Lo descarga el cajero de la
/// sesión o quien supervisa, administra o liquida cajas (mismo criterio que el
/// corte de caja). Se genera siempre desde el retiro guardado: reimprimir da el
/// mismo folio y los mismos datos con la fecha de impresión del momento.
/// </summary>
public sealed record ValeCajaPdfQuery(Guid MovimientoId) : IRequest<PdfFacturaGenerado>;

public sealed class ValeCajaPdfHandler(
    FacturacionDbContext db,
    ICurrentUserContext user,
    ICurrentUserPermissions permisos,
    IUsuariosReadPort usuarios,
    ISucursalesReadPort sucursales,
    IEmpresaFiscalReadPort empresas,
    IDatosImpresionCfdiReadPort datosImpresion,
    IGenerarValeCajaPdfPort generador,
    IClock clock) : IRequestHandler<ValeCajaPdfQuery, PdfFacturaGenerado>
{
    private const string Supervisar = "facturacion.caja.supervisar";
    private const string Administrar = "facturacion.caja.administrar";
    private const string Liquidar = "facturacion.caja.liquidar";

    public async Task<PdfFacturaGenerado> Handle(ValeCajaPdfQuery query, CancellationToken cancellationToken)
    {
        var movimiento = await db.CajaMovimientos.AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == query.MovimientoId, cancellationToken)
            ?? throw new EntityNotFoundException("MOVIMIENTO_NO_ENCONTRADO", $"No existe el movimiento '{query.MovimientoId}'.");
        if (movimiento.Tipo != TipoCajaMovimiento.Retiro || movimiento.FolioVale is null)
            throw new BusinessRuleException("VALE_SOLO_RETIRO", "Solo un retiro de caja tiene Vale de Caja.");

        var sesion = await db.CajaSesiones.AsNoTracking()
            .FirstAsync(s => s.Id == movimiento.CajaSesionId, cancellationToken);
        await ExigirAccesoAsync(sesion, cancellationToken);

        var nombres = await usuarios.ObtenerNombresAsync([sesion.ResponsableUsuarioId], cancellationToken);
        var cajero = nombres.GetValueOrDefault(sesion.ResponsableUsuarioId) ?? sesion.ResponsableUsuarioId.ToString();
        var zona = await sucursales.ObtenerZonaHorariaAsync(sesion.SucursalId, cancellationToken);
        var empresa = await empresas.ObtenerAsync(movimiento.EmpresaId, cancellationToken);
        var datos = await datosImpresion.ObtenerAsync(movimiento.EmpresaId, cancellationToken);

        var modelo = ValeCajaPdfModelBuilder.Desde(
            movimiento, cajero, empresa?.RazonSocial ?? datos.NombreComercial ?? string.Empty, datos.Logo,
            datos.FormasPago,
            DateOnly.FromDateTime(DiaOperacion.ALocal(movimiento.CreatedAt, zona).DateTime),
            DiaOperacion.HoyLocal(clock.UtcNow, zona));
        return generador.Generar(modelo);
    }

    private async Task ExigirAccesoAsync(CajaSesion sesion, CancellationToken cancellationToken)
    {
        if (user.UserId is Guid usuarioId && usuarioId == sesion.ResponsableUsuarioId)
            return;
        if (await permisos.TieneAsync(Supervisar, cancellationToken)
            || await permisos.TieneAsync(Administrar, cancellationToken)
            || await permisos.TieneAsync(Liquidar, cancellationToken))
            return;

        throw new ForbiddenException(
            "VALE_PERMISO_INSUFICIENTE",
            "Solo el cajero de la sesión o quien supervisa, administra o liquida cajas puede imprimir el vale.");
    }
}

/// <summary>Arma el <see cref="ValeCajaPdfModel"/> desde el retiro guardado (86e3ww6tw).</summary>
public static class ValeCajaPdfModelBuilder
{
    public const string FormaEfectivo = "01";
    public const string FormaCheque = "02";

    public static ValeCajaPdfModel Desde(
        CajaMovimiento retiro,
        string cajero,
        string empresaNombre,
        byte[]? logo,
        IReadOnlyDictionary<string, string> formasPago,
        DateOnly fechaExpedicion,
        DateOnly fechaImpresion)
    {
        if (retiro.Tipo != TipoCajaMovimiento.Retiro || retiro.FolioVale is not int folio)
            throw new BusinessRuleException("VALE_SOLO_RETIRO", "Solo un retiro de caja tiene Vale de Caja.");

        // El retiro se guarda negativo; el vale muestra lo entregado.
        var importe = Math.Abs(retiro.Importe);
        var (forma, otro) = retiro.FormaPago switch
        {
            FormaEfectivo => (FormaPagoVale.Efectivo, (string?)null),
            FormaCheque => (FormaPagoVale.Cheque, null),
            var clave => (FormaPagoVale.Otro,
                formasPago.TryGetValue(clave, out var descripcion) ? $"{clave} - {descripcion}" : clave),
        };

        return new ValeCajaPdfModel(
            folio, fechaExpedicion, cajero, importe,
            ImporteConLetra.Espanol(importe, retiro.Moneda), retiro.Descripcion,
            forma, otro, fechaImpresion, empresaNombre, logo,
            $"vale-de-caja-{folio}.pdf");
    }
}
