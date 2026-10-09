using Millet.CuentasPorPagar.Application.FacturaProveedor.EditarCabecera;
using Millet.CuentasPorPagar.Application.FacturaProveedor.AplicarAnticipo;
using Millet.CuentasPorPagar.Application.FacturaProveedor.AplicarNotaCredito;
using Millet.CuentasPorPagar.Domain.AnticipoProveedor;
using Millet.CuentasPorPagar.Domain.NotaCreditoProveedor;
using Millet.CuentasPorPagar.Domain.NotaCargo;
using Millet.SharedKernel.Application.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Millet.CuentasPorPagar.UnitTests.P8;

public sealed class PeriodosP8Tests
{
    [Theory]
    [InlineData("captura", false)] [InlineData("captura", true)]
    [InlineData("autorizacion", false)] [InlineData("autorizacion", true)]
    [InlineData("anticipo", false)] [InlineData("anticipo", true)]
    [InlineData("nc", false)] [InlineData("nc", true)]
    [InlineData("cargo", false)] [InlineData("cargo", true)]
    [InlineData("cancelacion", false)] [InlineData("cancelacion", true)]
    [InlineData("fecha", false)] [InlineData("fecha", true)]
    public async Task Cada_operacion_verifica_periodo_antes_de_persistir(string operacion, bool cerrado)
    {
        await using var a = new P8Fixture();
        var f = a.Nueva(); a.Db.FacturasProveedor.Add(f);
        var fecha = DateOnly.FromDateTime(f.FechaContabilizacion.UtcDateTime);
        if (operacion != "captura") await a.Db.SaveChangesAsync();
        switch (operacion)
        {
            case "autorizacion": f.Autorizar(null, a.UtcNow); break;
            case "anticipo": fecha = new(2026, 10, 9); f.AplicarAnticipo(100, fecha); break;
            case "nc": fecha = new(2026, 10, 9); f.AplicarNotaCredito(100, fecha); break;
            case "cargo": fecha = new(2026, 10, 9); f.AplicarNotaCargo(100, Guid.NewGuid(), fecha); break;
            case "cancelacion": f.Cancelar(Domain.FacturaProveedor.MotivoCancelacion.ErrorCaptura, "FIX prueba", null, a.UtcNow); break;
            case "fecha": fecha = new(2026, 10, 1); f.EditarCabeceraPreAutorizacion("FIX", null, new(2026, 10, 15), new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero)); break;
        }
        if (cerrado) a.Cerradas.Add(fecha);
        Func<Task> guardar = async () => await a.Db.SaveChangesAsync();
        if (cerrado) await guardar.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "CXP_PERIODO_CERRADO");
        else await guardar.Should().NotThrowAsync();
        if (cerrado)
        {
            a.Db.ChangeTracker.Clear();
            var persistida = await a.Db.FacturasProveedor.FirstOrDefaultAsync();
            if (operacion == "captura") persistida.Should().BeNull();
            else { persistida!.Estado.Should().Be(Domain.FacturaProveedor.EstadoPasivo.Capturada); persistida.Movimientos.Should().BeEmpty(); }
        }
    }
    [Fact]
    public async Task No_se_puede_mover_factura_desde_periodo_cerrado_a_abierto()
    {
        await using var a = new P8Fixture(); var f = a.Nueva(); a.Db.Add(f); await a.Db.SaveChangesAsync();
        a.Cerradas.Add(new(2026, 9, 1));
        var h = new EditarCabeceraFacturaHandler(a.Db);
        Func<Task> editar = async () => await h.Handle(new(f.Id, f.Version, "FIX", null, new(2026, 10, 15), a.UtcNow), default);
        await editar.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "CXP_PERIODO_CERRADO");
    }
    [Theory]
    [InlineData(false, false)] [InlineData(false, true)]
    [InlineData(true, false)] [InlineData(true, true)]
    public async Task Editar_cabecera_con_la_misma_fecha_verifica_periodo(bool cerrado, bool cambiarFolio)
    {
        await using var a = new P8Fixture();
        var f = a.Nueva(); a.Db.Add(f); await a.Db.SaveChangesAsync();
        if (cerrado) a.Cerradas.Add(DateOnly.FromDateTime(f.FechaContabilizacion.UtcDateTime));
        var folio = cambiarFolio ? "FIX-P8-EDITADA" : f.FolioProveedor;
        var h = new EditarCabeceraFacturaHandler(a.Db);
        Func<Task> editar = async () => await h.Handle(new(f.Id, f.Version, folio, f.SerieProveedor,
            f.FechaVencimiento, f.FechaContabilizacion), default);
        if (cerrado)
            await editar.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "CXP_PERIODO_CERRADO");
        else await editar.Should().NotThrowAsync();
        a.Db.ChangeTracker.Clear();
        var persistida = await a.Db.FacturasProveedor.SingleAsync();
        persistida.FolioProveedor.Should().Be(cerrado ? "FIX-P8" : folio);
        persistida.Movimientos.Should().BeEmpty();
    }
    [Theory]
    [InlineData("anticipo", false)] [InlineData("anticipo", true)]
    [InlineData("nc", false)] [InlineData("nc", true)]
    [InlineData("cargo", false)] [InlineData("cargo", true)]
    public async Task Captura_documentos_auxiliares_verifica_periodo(string documento, bool cerrado)
    {
        await using var a = new P8Fixture(); if (cerrado) a.Cerradas.Add(new(2026, 10, 9));
        if (documento == "anticipo") a.Db.Add(global::Millet.CuentasPorPagar.Domain.AnticipoProveedor.AnticipoProveedor.Capturar(a.Current!.Value, null, Guid.NewGuid().ToString(), a.Proveedor, "FANT", "FIX", a.UtcNow, "MXN", null, 100, null, null, a.UtcNow));
        if (documento == "nc") a.Db.Add(global::Millet.CuentasPorPagar.Domain.NotaCreditoProveedor.NotaCreditoProveedor.Capturar(a.Current!.Value, null, Guid.NewGuid().ToString(), a.Proveedor, "FIX", null, a.UtcNow, "MXN", null, 100, 0, 0, 100, TipoNotaCredito.Descuento, TipoRelacionCfdi.NotaCredito, Guid.NewGuid().ToString(), null, null, a.UtcNow));
        if (documento == "cargo") a.Db.Add(global::Millet.CuentasPorPagar.Domain.NotaCargo.NotaCargo.Crear(a.Current!.Value, FolioInternoNotaCargo.FromAnioSecuencial(2026, 1), a.Proveedor, a.Sucursal, "FIX prueba", null, 100, "MXN", null, null, null, null, a.UtcNow));
        Func<Task> guardar = async () => await a.Db.SaveChangesAsync();
        if (cerrado) await guardar.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "CXP_PERIODO_CERRADO");
        else await guardar.Should().NotThrowAsync();
    }
}
