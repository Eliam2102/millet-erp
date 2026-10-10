using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Application.Recepciones;
using Millet.Almacen.Application.Salidas;
using Millet.Almacen.UnitTests.TestSupport;
namespace Millet.Almacen.UnitTests.Recepciones;

public sealed class ADM08CentroCostoHeredadoTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Entrada_copia_centro_del_departamento_de_oc_sin_maquina(bool packing)
    {
        await using var f = new P1Fixture();
        var ccDepartamento = Guid.NewGuid();
        var oc = (await f.Oc.ObtenerAsync(f.DocumentoId, default))!;
        var port = new P1Fixture.OcPort(oc with { Lineas = [oc.Lineas[0] with { CentroCostoId = ccDepartamento }] });
        RegistrarRecepcionLineaInput[] lineas = [new(f.ArticuloId, f.LineaId, 2, null, null, f.BinId)];
        var conversion = new P7Support.Conversion();
        if (packing) await new RegistrarRecepcionConPackingListHandler(f.Db, port, new P1Fixture.Articulos(), f.Events,
            f.Context, f.Context, f.Guard, new PeriodoContableStub(true), conversion)
            .Handle(new(f.DocumentoId, P1Fixture.Fecha, "DEMO-ADM08.pdf", null, lineas), default);
        else await new RegistrarRecepcionConFacturaHandler(f.Db, port, new P1Fixture.Articulos(), f.Events,
            f.Context, f.Context, f.Guard, new PeriodoContableStub(true), conversion)
            .Handle(new(f.DocumentoId, P1Fixture.Fecha, Guid.NewGuid(), null, null, lineas), default);
        var linea = (await f.Db.Movimientos.Include(x => x.Lineas).SingleAsync()).Lineas.Single();
        Assert.Equal(ccDepartamento, linea.CentroCostoId);
        Assert.Equal(f.LineaId, linea.LineaOcId);
        Assert.Single(f.Events.Items);
    }
    [Fact]
    public async Task Salida_copia_departamento_de_rq_e_ignora_centro_enviado_por_el_caller()
    {
        await using var f = new P1Fixture(); var centroDepartamento = Guid.NewGuid();
        var rq = (await f.Rq.ObtenerAsync(f.DocumentoId, default))!;
        var port = new P1Fixture.RqPort(rq with { Lineas = [rq.Lineas[0] with { CentroCostoId = centroDepartamento }] });
        await new RegistrarSalidaConRequisicionHandler(f.Db, port, f.Events, f.Context, f.Context, f.Guard,
            new PeriodoContableStub(true), new P7Support.Conversion(), P7Support.Apartados(f.Db))
            .Handle(new(f.DocumentoId, P1Fixture.Fecha, null, null,
                [new(f.ArticuloId, f.LineaId, 1, Guid.NewGuid(), null, null, null, f.BinId)]), default);
        var linea = (await f.Db.Movimientos.Include(x => x.Lineas).SingleAsync()).Lineas.Single();
        Assert.Equal(centroDepartamento, linea.CentroCostoId);
    }
}
