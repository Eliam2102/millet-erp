using Microsoft.EntityFrameworkCore;
using Millet.Api.Web;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.Tesoreria.Application.Reportes;
using Millet.Tesoreria.Domain.Cuentas;
using Millet.Tesoreria.Domain.Movimientos;
using Millet.Tesoreria.Infrastructure;
using Millet.Tesoreria.Infrastructure.Persistence;

namespace Millet.Api.UnitTests;

public sealed class TesoreriaReportesSucursalP6Tests
{
    [Fact]
    public async Task Reportes_conservan_saldos_P5_sin_exponer_cuentas_ajenas_mixtas_o_sin_origen()
    {
        var empresa = new Empresa();
        using var db = new TesoreriaDbContext(new DbContextOptionsBuilder<TesoreriaDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, empresa);
        var propia = Guid.NewGuid(); var ajena = Guid.NewGuid();
        var facturaPropia = Guid.NewGuid(); var facturaAjena = Guid.NewGuid();
        var puerto = new Origenes([new(facturaPropia, [propia]), new(facturaAjena, [ajena])]);
        var cuentas = Enumerable.Range(0, 4).Select(i => new CuentaBancaria(empresa.Current!.Value,
            $"Banco ficticio P6 {i}", $"1234567{i}", null, "MXN")).ToArray();
        foreach (var cuenta in cuentas) cuenta.RegistrarSaldoInicial(1000m, new(2026, 9, 30), "Saldo ficticio P6");
        void Agregar(int cuenta, Guid? factura, int dia)
        {
            var movimiento = MovimientoBancario.RegistrarPagoProveedor(empresa.Current!.Value, cuentas[cuenta], Guid.NewGuid(),
                100m, new(2026, 10, dia), "Prueba P6", null, Guid.NewGuid(), Ahora);
            db.MovimientosBancarios.Add(movimiento);
            if (factura is Guid f) db.AplicacionesPagoProveedor.Add(new(movimiento.Id, f, Guid.NewGuid(), 100m, Ahora));
        }
        db.CuentasBancarias.AddRange(cuentas);
        Agregar(0, facturaPropia, 1); Agregar(0, facturaPropia, 9);
        Agregar(1, facturaAjena, 9);
        Agregar(2, facturaPropia, 9); Agregar(2, facturaAjena, 9);
        Agregar(3, null, 9);
        await db.SaveChangesAsync();
        var adapter = new TesoreriaSucursalReadAdapter(db, puerto, puerto, puerto);
        var documentos = await adapter.ListarAsync("cuenta_bancaria", CancellationToken.None);
        var permitidos = documentos.Where(x => DocumentoSucursalScope.Permitido(x, [propia])).Select(x => x.Id).ToArray();
        Assert.Equal(new[] { cuentas[0].Id }, permitidos);
        Assert.Empty(documentos.Single(x => x.Id == cuentas[3].Id).Sucursales);
        var flujo = new FlujoEfectivoReporteHandler(db, new Reloj());
        var query = new FlujoEfectivoReporteQuery(new(2026, 10, 8), new(2026, 10, 10)) { DocumentosPermitidos = permitidos };
        var reporte = await flujo.Handle(query, CancellationToken.None);
        Assert.Equal(900m, reporte.Filas.Single(x => (string?)x["tipo"] == "saldoInicial")["neto"]);
        Assert.Equal(800m, reporte.Filas.Single(x => (string?)x["tipo"] == "saldoFinal")["neto"]);
        Assert.All(reporte.Filas.Where(x => (string?)x["tipo"] != "total"), x => Assert.Equal(cuentas[0].Id, x["cuentaId"]));
        var corporativo = await flujo.Handle(query with { DocumentosPermitidos = null }, CancellationToken.None);
        Assert.Equal(4, corporativo.Filas.Count(x => (string?)x["tipo"] == "saldoInicial"));
        var auxiliar = new AuxiliarBancosReporteHandler(db, new Reloj());
        var auxQuery = new AuxiliarBancosReporteQuery(cuentas[0].Id, new(2026, 10, 8), new(2026, 10, 10)) { DocumentosPermitidos = permitidos };
        Assert.Equal(800m, (await auxiliar.Handle(auxQuery, CancellationToken.None)).Totales!["saldo"]);
        await Assert.ThrowsAsync<EntityNotFoundException>(() => auxiliar.Handle(auxQuery with { CuentaBancariaId = cuentas[1].Id }, CancellationToken.None));
        Assert.Equal(900m, (await auxiliar.Handle(auxQuery with { CuentaBancariaId = cuentas[1].Id, DocumentosPermitidos = null }, CancellationToken.None)).Totales!["saldo"]);
    }
    private static readonly DateTimeOffset Ahora = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);
    private sealed class Reloj : IClock { public DateTimeOffset UtcNow => Ahora; }
    private sealed class Empresa : ICurrentEmpresaContext
    {
        public Guid? Current { get; } = Guid.NewGuid(); public bool IsBypassed => true;
        public IDisposable Bypass() => throw new NotSupportedException();
    }
    private sealed class Origenes(IReadOnlyList<DocumentoSucursales> facturas) : ICxpSucursalReadPort, ICxcSucursalReadPort, IFacturacionSucursalReadPort
    {
        public Task<IReadOnlyList<DocumentoSucursales>> ListarAsync(string tipo, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<DocumentoSucursales>>(tipo == "factura_proveedor" ? facturas : []);
    }
}
