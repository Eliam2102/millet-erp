using Microsoft.EntityFrameworkCore;
using Millet.Almacen.Domain.Apartados;
using Millet.Almacen.UnitTests.TestSupport;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Almacen.UnitTests.P7;

public sealed class LiberarApartadoTests
{
    [Fact]
    public async Task DEMO_Completo_revalida_existencia_real_sin_crear_apartado_parcial()
    {
        await using var f = new P1Fixture();
        var sucursal = (await f.Db.Almacenes.SingleAsync()).SucursalId;
        var servicio = P7Support.Apartados(f.Db);
        Assert.Equal(0, await servicio.ApartarAsync(P1Fixture.EmpresaId, sucursal, f.DocumentoId, f.LineaId, f.ArticuloId, 101, default, soloSiCompleto: true));
        Assert.Empty(await f.Db.ApartadosRequisicion.ToListAsync());
        Assert.Equal(100, await servicio.ApartarAsync(P1Fixture.EmpresaId, sucursal, Guid.NewGuid(), Guid.NewGuid(), f.ArticuloId, 101, default));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(11)]
    public void DEMO_Rechaza_cantidad_invalida_sin_mutar(decimal cantidad)
    {
        var a = new ApartadoRequisicion(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 10);
        Assert.Throws<BusinessRuleException>(() => a.Liberar(cantidad));
        Assert.Equal(10, a.Pendiente);
    }

    [Fact]
    public async Task DEMO_Liberacion_parcial_registra_evidencia_y_ofrece_stock_a_otra_RQ()
    {
        await using var f = new P1Fixture();
        var sucursal = (await f.Db.Almacenes.SingleAsync()).SucursalId;
        var servicio = P7Support.Apartados(f.Db);
        await servicio.ApartarAsync(P1Fixture.EmpresaId, sucursal, f.DocumentoId, f.LineaId, f.ArticuloId, 100, default);
        var actor = Guid.NewGuid();
        var fecha = DateTimeOffset.UtcNow;
        await servicio.LiberarLineaAsync(f.DocumentoId, f.LineaId, 3, "DEMO cambio de prioridad", actor, fecha, default);
        Assert.Equal(97, (await f.Db.ApartadosRequisicion.SingleAsync()).Pendiente);
        var registro = await f.Db.LiberacionesApartado.SingleAsync();
        Assert.Equal((actor, fecha, 3m, "DEMO cambio de prioridad"), (registro.ActorId, registro.Fecha, registro.Cantidad, registro.Motivo));
        Assert.Equal(3, await servicio.ApartarAsync(P1Fixture.EmpresaId, sucursal, Guid.NewGuid(), Guid.NewGuid(), f.ArticuloId, 10, default));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task DEMO_Motivo_obligatorio_no_libera(string motivo)
    {
        await using var f = new P1Fixture();
        var sucursal = (await f.Db.Almacenes.SingleAsync()).SucursalId;
        var servicio = P7Support.Apartados(f.Db);
        await servicio.ApartarAsync(P1Fixture.EmpresaId, sucursal, f.DocumentoId, f.LineaId, f.ArticuloId, 10, default);
        await Assert.ThrowsAsync<BusinessRuleException>(() => servicio.LiberarLineaAsync(f.DocumentoId, f.LineaId, 3, motivo, Guid.NewGuid(), DateTimeOffset.UtcNow, default));
        Assert.Equal(10, (await f.Db.ApartadosRequisicion.SingleAsync()).Pendiente);
        Assert.Empty(await f.Db.LiberacionesApartado.ToListAsync());
    }
}
