using Microsoft.EntityFrameworkCore;
using Millet.Api.Web;
using Millet.CuentasPorPagar.Application.Cfdi.ListarCfdis;
using Millet.CuentasPorPagar.Application.TarjetaCredito.Movimientos;
using Millet.CuentasPorPagar.Domain.Cfdi;
using Millet.CuentasPorPagar.Domain.FacturaProveedor;
using Millet.CuentasPorPagar.Domain.TarjetaCredito;
using Millet.CuentasPorPagar.Infrastructure;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;

namespace Millet.Api.UnitTests;

public sealed class CxpDocumentosRelacionadosP6Tests
{
    [Fact]
    public async Task Cfdi_y_movimiento_TC_heredan_sucursal_de_factura_y_filtran_antes_de_totalizar()
    {
        var empresa = new Empresa();
        using var db = new CuentasPorPagarDbContext(new DbContextOptionsBuilder<CuentasPorPagarDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, empresa);
        var propia = Guid.NewGuid(); var ajena = Guid.NewGuid(); var ahora = DateTimeOffset.UtcNow;
        foreach (var sucursal in new[] { propia, ajena })
        {
            var cfdi = CfdiRecibido.Ingresar(empresa.Current!.Value, UuidCfdi.Parse(Guid.NewGuid().ToString()),
                RfcMexicano.Parse("PRO010101AAA"), RfcMexicano.Parse("MIL010101AAA"), TipoCfdi.Ingreso,
                "P6", null, ahora, 116m, 100m, 16m, 0m, "MXN", null, CanalOrigenCfdi.CargaManual, ahora, "blob-prueba", null, "hash-prueba");
            var proveedor = Guid.NewGuid();
            var factura = FacturaProveedor.CapturarSinOc(empresa.Current.Value, cfdi.Id, cfdi.UuidCfdi.Valor,
                proveedor, sucursal, "P6", null, ahora, ahora, DateOnly.FromDateTime(ahora.DateTime), "MXN", null,
                100m, 0m, 16m, 0m, 116m, "Prueba P6", ahora);
            var tarjeta = Tarjeta.Crear(empresa.Current.Value, "Emisora de prueba", "AMEX_MX", NumeroTarjetaEnmascarado.FromUltimosCuatro("1234"),
                "Tarjeta de prueba", Guid.NewGuid(), proveedor, 10000m, "MXN", 15, 20, new DateOnly(2026, 1, 1));
            var movimiento = MovimientoTarjetaCredito.CapturarCompraConCfdi(empresa.Current.Value, tarjeta, tarjeta.TitularId,
                DateOnly.FromDateTime(ahora.DateTime), 116m, "MXN", null, "Prueba P6", null, cfdi.Id, factura.Id, proveedor, "Prueba P6");
            db.AddRange(cfdi, factura, tarjeta, movimiento);
        }
        await db.SaveChangesAsync();
        var puerto = new CxpSucursalReadAdapter(db, new Compras());
        var cfdis = await puerto.ListarAsync("cfdi_recibido", CancellationToken.None);
        var movimientos = await puerto.ListarAsync("movimiento_tc", CancellationToken.None);
        Assert.Equal(2, cfdis.Count); Assert.Equal(2, movimientos.Count);
        var cfdisPermitidos = cfdis.Where(x => DocumentoSucursalScope.Permitido(x, [propia])).Select(x => x.Id).ToArray();
        var movimientosPermitidos = movimientos.Where(x => DocumentoSucursalScope.Permitido(x, [propia])).Select(x => x.Id).ToArray();
        var listaCfdis = await new ListarCfdisHandler(db).Handle(new() { DocumentosPermitidos = cfdisPermitidos }, CancellationToken.None);
        var listaTc = await new ListarMovimientosTcHandler(db).Handle(new() { DocumentosPermitidos = movimientosPermitidos }, CancellationToken.None);
        Assert.Equal(1, listaCfdis.Total); Assert.Single(listaCfdis.Items);
        Assert.Equal(1, listaTc.Total); Assert.Single(listaTc.Items);
        var listaCorporativa = await new ListarCfdisHandler(db).Handle(new(), CancellationToken.None);
        Assert.Equal(2, listaCorporativa.Total);
    }
    private sealed class Empresa : ICurrentEmpresaContext
    {
        public Guid? Current { get; } = Guid.NewGuid(); public bool IsBypassed => true;
        public IDisposable Bypass() => throw new NotSupportedException();
    }
    private sealed class Compras : IComprasSucursalReadPort
    {
        public Task<IReadOnlyList<DocumentoSucursales>> ListarAsync(string tipo, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<DocumentoSucursales>>([]);
    }
}
