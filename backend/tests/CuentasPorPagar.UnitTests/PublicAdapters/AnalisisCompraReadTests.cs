using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorPagar.Domain.FacturaProveedor;
using Millet.CuentasPorPagar.Infrastructure.Persistence;
using Millet.CuentasPorPagar.Infrastructure.PublicAdapters;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Factura = Millet.CuentasPorPagar.Domain.FacturaProveedor.FacturaProveedor;

namespace Millet.CuentasPorPagar.UnitTests.PublicAdapters;

public sealed class AnalisisCompraReadTests
{
    [Fact]
    public async Task DEMO_Respeta_corte_pagos_posteriores_y_empresa()
    {
        var contexto = new Empresa();
        await using var db = new CuentasPorPagarDbContext(new DbContextOptionsBuilder<CuentasPorPagarDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, contexto, new P8.PeriodoAbiertoStub(), new P8.PeriodoAbiertoStub());
        var fecha = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var oc = Guid.NewGuid();
        var factura = Factura.CapturarConOc(contexto.Current!.Value, null, null, Guid.NewGuid(), Guid.NewGuid(),
            "DEMO ficticio", null, fecha, fecha, new(2026, 2, 1), "MXN", null, 100, 0, 0, 0, 100,
            oc, null, Tolerancia.MontoAbsoluto(1), 0, 0, fecha);
        factura.AgregarLinea(Guid.NewGuid(), null, "DEMO ficticio", 10, "H87", "PZA", 10, 100, null, Guid.NewGuid(), null);
        factura.Autorizar(Guid.NewGuid(), fecha);
        factura.RegistrarPago(30, fecha.AddDays(1), "DEMO pago");
        factura.RegistrarPago(20, fecha.AddMonths(3), "DEMO pago posterior");
        db.FacturasProveedor.Add(factura); await db.SaveChangesAsync();
        var lector = new CxpFacturasTrazabilidadProvider(db, contexto);
        var r = Assert.Single(await lector.LeerAnalisisAsync(contexto.Current.Value, [oc], new(2026, 3, 31), default));
        Assert.Equal(70, r.Saldo); Assert.Equal(10, Assert.Single(r.Lineas).Cantidad);
        Assert.Empty(await lector.LeerAnalisisAsync(Guid.NewGuid(), [oc], new(2026, 3, 31), default));
        Assert.Empty(await lector.LeerAnalisisAsync(contexto.Current.Value, [oc], new(2025, 12, 31), default));
        db.Entry(factura).Property(f => f.ImportePagado).CurrentValue = 60;
        await db.SaveChangesAsync();
        var ex = await Assert.ThrowsAsync<BusinessRuleException>(() => lector.LeerAnalisisAsync(contexto.Current.Value, [oc], new(2026, 3, 31), default));
        Assert.Equal("CXP_HISTORICO_POR_CONFIRMAR", ex.Code);
    }
    private sealed class Empresa : ICurrentEmpresaContext
    {
        public Guid? Current { get; } = Guid.NewGuid();
        public bool IsBypassed => false;
        public IDisposable Bypass() => throw new NotSupportedException();
    }
}
