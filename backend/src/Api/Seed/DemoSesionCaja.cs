using Microsoft.EntityFrameworkCore;
using Millet.Administracion.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.Facturacion.Application.Cajas.Sesiones;
using Millet.Facturacion.Domain.Cajas;
using Millet.Facturacion.Domain.Comprobantes;
using Millet.Facturacion.Domain.Facturas;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Identidad.Infrastructure;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Domain;

namespace Millet.Api.Seed;

/// <summary>
/// FIX-12: caja DEMO de Conkal (sucursal MID) con un día ya cerrado para presentar el corte
/// de caja por cajero y punto de venta sin depender de cobrar en vivo. La
/// sesión es del día anterior a la preparación (día local de la sucursal) y
/// queda Cerrada; así no bloquea abrir la sesión de hoy (§5.2).
///
/// <para>
/// Las facturas son ficticias: folio reservado de la serie de facturas (<c>FA-000001</c>…), avanzando su consecutivo como una factura real para que la primera emisión real no repita folio, sin XML ni PDF y con
/// un UUID simulado (no pasan por el PAC). No publica eventos de integración,
/// así que CxC no las ve en cartera y Tesorería no proyecta el depósito
/// esperado de esta sesión; para mostrar ese recorrido se cobra en vivo.
/// </para>
/// </summary>
public sealed partial class DemoSesionSeedHostedService
{
    public const string CajaDemoNombre = "Caja Mostrador Conkal";
    public static readonly Guid CajeraDemoId = Id("DEMO-USUARIO-CAJERA-MID");

    private const decimal FondoDemo = 2_000m;
    private const decimal RetiroDemo = 1_500m;

    // Facturas PUE de mostrador cobradas en el día: clave de cliente, artículo,
    // cantidad, precio unitario y formas de pago (clave SAT, importe con IVA, voucher/folio).
    private static readonly (string Cliente, string Articulo, decimal Cantidad, decimal Precio, TimeOnly Hora,
        (string Forma, decimal Importe, string? Referencia)[] Pagos)[] CobrosDemo =
    [
        ("DEMO-CLI-KIJ", "Vidrio claro 6 mm", 2, 500m, new(10, 15), [("01", 1_160m, null)]),
        ("DEMO-CLI-NORMAL", "Vidrio templado 10 mm", 1, 2_000m, new(11, 40), [("04", 2_320m, "DEMO-VOUCHER-0417")]),
        ("DEMO-CLI-XIA", "Espejo 4 mm", 1, 500m, new(13, 5), [("03", 580m, "DEMO-FOLIO-INGRESOS-0031")]),
        ("DEMO-CLI-RANURA", "Cancel de baño", 1, 1_000m, new(16, 30), [("01", 500m, null), ("28", 660m, "DEMO-VOUCHER-0422")]),
    ];

    private static async Task SembrarCajaAsync(IServiceProvider sp, CancellationToken ct)
    {
        var db = sp.GetRequiredService<FacturacionDbContext>();
        var maestros = sp.GetRequiredService<CompartidoDbContext>();
        var mid = await maestros.Sucursales.SingleAsync(s => s.EmpresaId == EmpresaId && s.Clave == "MID", ct);

        // Cajeros: la cajera técnica y los usuarios DEMO de Facturación que aún
        // no estén en ninguna caja (los que entran por primera vez después).
        // Nunca se retiran cajeros: se respeta lo que el administrador edite.
        var rolFacturacion = Id("DEMO-ROL-Facturación");
        var cajerosFacturacion = await sp.GetRequiredService<IdentidadDbContext>().UsuarioEmpresaRoles
            .Where(a => a.EmpresaId == EmpresaId && a.RolId == rolFacturacion)
            .Select(a => a.UsuarioId).ToListAsync(ct);
        var yaRelacionados = await db.Cajas.SelectMany(c => c.Usuarios).Select(u => u.UsuarioId).ToListAsync(ct);

        var caja = await db.Cajas.Include(c => c.Sucursales).Include(c => c.Usuarios)
            .SingleOrDefaultAsync(c => c.EmpresaId == EmpresaId && (c.Nombre == CajaDemoNombre || c.Nombre == "DEMO Caja Mostrador Conkal"), ct);
        if (caja is null)
        {
            caja = Caja.Crear(EmpresaId, CajaDemoNombre, "Mostrador de Conkal, un cajero por sesión.");
            caja.ReemplazarSucursales([mid.Id]);
            db.Cajas.Add(caja);
        }
        if (caja.Nombre == "DEMO Caja Mostrador Conkal") caja.Actualizar(CajaDemoNombre, "Mostrador de Conkal, un cajero por sesión.");
        var nuevos = cajerosFacturacion.Append(CajeraDemoId).Where(u => !yaRelacionados.Contains(u)).ToList();
        if (nuevos.Count > 0)
            caja.ReemplazarUsuarios([.. caja.Usuarios.Select(u => u.UsuarioId), .. nuevos]);
        await db.SaveChangesAsync(ct);

        if (await db.CajaSesiones.AnyAsync(s => s.CajaId == caja.Id && s.ResponsableUsuarioId == CajeraDemoId, ct))
            return;

        var empresa = await maestros.Empresas.SingleAsync(e => e.Id == EmpresaId, ct);
        var emisor = new DatosFiscalesEmisor(empresa.Rfc, empresa.RazonSocial, empresa.RegimenFiscal, empresa.CodigoPostal ?? "42501");
        var dia = DiaOperacion.HoyLocal(sp.GetRequiredService<IClock>().UtcNow, mid.ZonaHoraria).AddDays(-1);
        DateTimeOffset A(TimeOnly hora) => LocalAUtc(dia, hora, mid.ZonaHoraria);

        var sesion = CajaSesion.Abrir(EmpresaId, caja.Id, mid.Id, CajeraDemoId, FondoDemo, dia, A(new(9, 0)), null);
        var movimientos = new List<(CajaMovimiento Movimiento, DateTimeOffset Fecha)>
        {
            (CajaMovimiento.Crear(EmpresaId, sesion.Id, TipoCajaMovimiento.FondoApertura, CajaSesion.FormaPagoEfectivo,
                FondoDemo, CajeraDemoId, "Fondo de apertura"), A(new(9, 0))),
            (CajaMovimiento.Crear(EmpresaId, sesion.Id, TipoCajaMovimiento.Retiro, CajaSesion.FormaPagoEfectivo,
                -RetiroDemo, CajeraDemoId, "Retiro parcial entregado a caja general"), A(new(15, 0))),
        };
        // Los folios salen del consecutivo de la serie de facturas (igual que ReservarFolioCommand):
        // si se escribieran fijos, la primera factura real repetiría FA-000001 (ix_comprobante_sucursal_folio).
        var serieId = Id("DEMO-F");
        var serie = await maestros.Series.IgnoreQueryFilters().SingleAsync(s => s.Id == serieId, ct);
        var secuencia = await maestros.SecuenciasFolio.IgnoreQueryFilters()
            .SingleOrDefaultAsync(s => s.SerieId == serieId && s.PeriodoClave == string.Empty, ct);
        if (secuencia is null)
        {
            secuencia = new SecuenciaFolio(Guid.CreateVersion7(), serieId, string.Empty, serie.FolioInicial - 1);
            maestros.SecuenciasFolio.Add(secuencia);
        }
        var facturas = new List<FacturaVenta>();
        var cobros = new List<CobroMostrador>();
        for (var i = 0; i < CobrosDemo.Length; i++)
        {
            var c = CobrosDemo[i];
            var cliente = ClientesSat.Single(x => x.Clave == c.Cliente);
            var receptor = new DatosFiscalesReceptor(cliente.Rfc, cliente.Nombre, "601", cliente.Cp, "G03", "MEX", false);
            var fecha = A(c.Hora);
            var numero = secuencia.ReservarSiguiente();
            var factura = FacturaVenta.CrearBorrador(EmpresaId, $"{serie.Prefijo}{serie.Sufijo}-{numero:D6}", numero, mid.Id, caja.Id, CajeraDemoId,
                receptor, emisor, "PUE", c.Pagos[0].Forma, "MXN", null, dia.Year, dia.Month, 1,
                ComportamientoFiscal.MostradorInmediato, null, null, null, false);
            factura.AgregarLinea(null, "30171700", c.Articulo, "H87", c.Cantidad, c.Precio, 0, "02", .16m, null, null);
            factura.RecalcularTotales();
            factura.MarcarTimbradoEnProceso();
            // UUID simulado: la factura no pasó por el PAC (no tiene XML ni PDF).
            factura.MarcarTimbrado(Id($"DEMO-CAJA-UUID-{i + 1}").ToString(), null, null, null, fecha, null, null);
            if (factura.Total != c.Pagos.Sum(p => p.Importe))
                throw new InvalidOperationException($"DEMO: el cobro {factura.Folio} no cubre el total de la factura.");

            var cobro = CobroMostrador.Registrar(EmpresaId, sesion.Id, mid.Id, factura.CanalVentaId, factura.Id,
                OrigenCobroMostrador.Mostrador, fecha, CajeraDemoId,
                [.. c.Pagos.Select(p => (p.Forma, p.Importe, p.Referencia, (string?)null, (string?)null))]);
            facturas.Add(factura);
            cobros.Add(cobro);
            movimientos.AddRange(cobro.FormasPago.Select(f => (CajaMovimiento.Crear(EmpresaId, sesion.Id,
                TipoCajaMovimiento.CobroCliente, f.FormaPago, f.Importe, CajeraDemoId, $"Cobro {factura.Folio}",
                f.Referencia, cobro.Id), fecha)));
        }

        // Mismo cálculo que el arqueo real: esperado por forma = suma de movimientos.
        var esperado = movimientos.GroupBy(m => m.Movimiento.FormaPago)
            .ToDictionary(g => g.Key, g => g.Sum(m => m.Movimiento.Importe));
        sesion.IniciarArqueo(esperado);
        sesion.Cerrar(esperado[CajaSesion.FormaPagoEfectivo], "Cierre del día: arqueo cuadrado, efectivo entregado a caja general.",
            cierreExtemporaneo: false, A(new(18, 10)));

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        db.FacturasVenta.AddRange(facturas);
        db.CajaSesiones.Add(sesion);
        db.CobrosMostrador.AddRange(cobros);
        db.CajaMovimientos.AddRange(movimientos.Select(m => m.Movimiento));
        await db.SaveChangesAsync(ct);
        await maestros.SaveChangesAsync(ct);
        // El corte ordena y fecha los movimientos por CreatedAt, que el interceptor
        // fija al momento de guardar: se lleva cada uno a su hora del día DEMO.
        foreach (var (movimiento, fecha) in movimientos)
            await db.CajaMovimientos.Where(m => m.Id == movimiento.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.CreatedAt, fecha), ct);
        foreach (var factura in facturas)
            await db.Comprobantes.Where(f => f.Id == factura.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(f => f.CreatedAt, factura.FechaTimbrado!.Value), ct);
        await tx.CommitAsync(ct);
    }

    private static DateTimeOffset LocalAUtc(DateOnly dia, TimeOnly hora, string? zonaHoraria)
    {
        var tz = TimeZoneInfo.FindSystemTimeZoneById(string.IsNullOrWhiteSpace(zonaHoraria) ? "America/Merida" : zonaHoraria);
        var local = dia.ToDateTime(hora, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, tz.GetUtcOffset(local)).ToUniversalTime();
    }
}
