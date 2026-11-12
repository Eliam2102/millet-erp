using Microsoft.EntityFrameworkCore;
using Millet.CuentasPorCobrar.Application.Cobranza;
using Millet.CuentasPorCobrar.Domain.Cartera;
using Millet.CuentasPorCobrar.Domain.Cobranza;
using Millet.CuentasPorCobrar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorCobrar.UnitTests.Cobranza;

/// <summary>M-345: una promesa se compara contra los pagos aplicados a la factura en la fecha prometida.</summary>
public sealed class CumplimientoPromesaTests
{
    private static readonly Guid EmpresaId = Guid.NewGuid();
    private static readonly DateOnly Promesa = new(2026, 11, 1);
    private static readonly DateOnly Prometida = new(2026, 11, 10);

    private static ResultadoPromesa Evaluar(DateOnly hoy, params PagoAplicado[] pagos) =>
        CumplimientoPromesa.Evaluar(5_000m, Prometida, Promesa, pagos, hoy);

    [Fact]
    public void Una_promesa_de_5000_con_pago_de_3000_queda_parcial()
    {
        var r = Evaluar(Prometida.AddDays(1), new PagoAplicado(new DateOnly(2026, 11, 8), 3_000m));

        r.Estado.Should().Be(EstadoPromesa.Parcial);
        r.Pagado.Should().Be(3_000m);
        r.Faltante.Should().Be(2_000m);
    }

    [Fact]
    public void El_pago_completo_cumple_la_promesa_aunque_se_haga_en_pagos()
    {
        var r = Evaluar(Prometida.AddDays(1),
            new PagoAplicado(new DateOnly(2026, 11, 3), 2_000m), new PagoAplicado(Prometida, 3_000m));

        r.Estado.Should().Be(EstadoPromesa.Cumplida);
        r.Faltante.Should().Be(0m);
    }

    [Fact]
    public void Sin_pagos_despues_de_la_fecha_queda_incumplida()
    {
        Evaluar(Prometida.AddDays(1)).Estado.Should().Be(EstadoPromesa.Incumplida);
    }

    [Fact]
    public void Antes_de_la_fecha_prometida_sigue_vigente_y_si_ya_se_pago_todo_queda_cumplida()
    {
        Evaluar(Prometida, new PagoAplicado(Promesa, 1_000m)).Estado.Should().Be(EstadoPromesa.Vigente);
        Evaluar(Prometida.AddDays(-3), new PagoAplicado(Promesa, 5_000m)).Estado.Should().Be(EstadoPromesa.Cumplida);
    }

    [Fact]
    public void Los_pagos_anteriores_a_la_promesa_o_posteriores_a_la_fecha_no_cuentan()
    {
        var r = Evaluar(Prometida.AddDays(5),
            new PagoAplicado(Promesa.AddDays(-1), 5_000m), new PagoAplicado(Prometida.AddDays(1), 5_000m));

        r.Estado.Should().Be(EstadoPromesa.Incumplida);
        r.Pagado.Should().Be(0m);
    }

    [Fact]
    public async Task La_consulta_toma_solo_los_pagos_vigentes_de_esa_factura()
    {
        await using var db = Db();
        var factura = Factura(db);
        var otra = Factura(db);
        var ahora = new DateTimeOffset(2026, 11, 12, 12, 0, 0, TimeSpan.Zero);
        db.MovimientosCartera.AddRange(
            Pago(factura, 3_000m, new DateTimeOffset(2026, 11, 8, 12, 0, 0, TimeSpan.Zero)),
            Pago(factura, 1_000m, new DateTimeOffset(2026, 11, 9, 12, 0, 0, TimeSpan.Zero), revertido: true),
            Pago(otra, 5_000m, new DateTimeOffset(2026, 11, 8, 12, 0, 0, TimeSpan.Zero)));
        await db.SaveChangesAsync();

        var r = await new EvaluarPromesaFacturaHandler(db, new Reloj(ahora))
            .Handle(new EvaluarPromesaFacturaQuery(factura.Id, 5_000m, Prometida, Promesa), CancellationToken.None);

        r.Estado.Should().Be(EstadoPromesa.Parcial);
        r.Pagado.Should().Be(3_000m, "el pago revertido y el de otra factura no cuentan");
    }

    [Fact]
    public async Task La_consulta_rechaza_monto_cero_fechas_invertidas_y_factura_inexistente()
    {
        await using var db = Db();
        var factura = Factura(db);
        await db.SaveChangesAsync();
        var h = new EvaluarPromesaFacturaHandler(db, new Reloj(DateTimeOffset.UtcNow));

        (await FluentActions.Awaiting(() => h.Handle(new(factura.Id, 0m, Prometida, Promesa), default))
            .Should().ThrowAsync<BusinessRuleException>()).Where(e => e.Code == "PRO_MONTO_INVALIDO");
        (await FluentActions.Awaiting(() => h.Handle(new(factura.Id, 100m, Promesa, Prometida), default))
            .Should().ThrowAsync<BusinessRuleException>()).Where(e => e.Code == "PRO_FECHAS_INVALIDAS");
        await FluentActions.Awaiting(() => h.Handle(new(Guid.NewGuid(), 100m, Prometida, Promesa), default))
            .Should().ThrowAsync<EntityNotFoundException>();
    }

    private static FacturaCartera Factura(CuentasPorCobrarDbContext db)
    {
        var f = FacturaCartera.Crear(EmpresaId, Guid.NewGuid(), Guid.NewGuid(), "AAA010101AAA", "Cliente",
            Guid.NewGuid().ToString(), "FV-1", 10_000m, "MXN", "PPD",
            new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero));
        db.FacturasCartera.Add(f);
        return f;
    }

    private static MovimientoCartera Pago(FacturaCartera f, decimal importe, DateTimeOffset fecha, bool revertido = false)
    {
        var m = new MovimientoCartera(EmpresaId, f.Id, TipoMovimientoCartera.Pago, Guid.NewGuid(), importe, fecha);
        if (revertido) m.Revertir(fecha.AddHours(1));
        return m;
    }

    private static CuentasPorCobrarDbContext Db() =>
        new(new DbContextOptionsBuilder<CuentasPorCobrarDbContext>().UseInMemoryDatabase($"cxc_m345_{Guid.NewGuid()}").Options,
            new Empresa(EmpresaId));

    private sealed class Reloj(DateTimeOffset ahora) : IClock
    {
        public DateTimeOffset UtcNow => ahora;
    }

    private sealed class Empresa(Guid id) : ICurrentEmpresaContext
    {
        public Guid? Current => id;
        public bool IsBypassed => false;
        public IDisposable Bypass() => new Noop();
        private sealed class Noop : IDisposable { public void Dispose() { } }
    }
}
