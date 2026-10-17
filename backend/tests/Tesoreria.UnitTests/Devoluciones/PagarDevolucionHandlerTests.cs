using Microsoft.EntityFrameworkCore;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.Tesoreria.Application.Devoluciones;
using Millet.Tesoreria.Domain.Cuentas;
using Millet.Tesoreria.Domain.Devoluciones;
using Millet.Tesoreria.Domain.Movimientos;
using Millet.Tesoreria.Domain.Ports;
using Millet.Tesoreria.Infrastructure.Persistence;

namespace Millet.Tesoreria.UnitTests.Devoluciones;

public sealed class PagarDevolucionHandlerTests
{
    private static readonly Guid Empresa = Guid.NewGuid();
    private static readonly Guid Solicitante = Guid.NewGuid();
    private static readonly Guid Cliente = Guid.NewGuid();
    private static readonly DateTimeOffset Ahora = new(2026, 10, 17, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Fecha = new(2026, 10, 17);
    private static readonly CuentaDestinoDevolucion Destino = new("BBVA", "032180000118359719", "Cliente SA");

    private static TesoreriaDbContext Db() => new(new DbContextOptionsBuilder<TesoreriaDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, new EmpresaContext());

    private static PagarDevolucionClienteHandler Handler(TesoreriaDbContext db, bool abierto = true) =>
        new(db, new EmpresaContext(), new User(Guid.NewGuid()), new Periodo(abierto), new Clock());

    private static async Task<(MovimientoBancario, DevolucionCliente)> Deposito(TesoreriaDbContext db, decimal importe = 1_000m)
    {
        var cuenta = new CuentaBancaria(Empresa, "BBVA", "1234567890", null, "MXN");
        var m = MovimientoBancario.RegistrarIngreso(Empresa, cuenta, 1_000m, Fecha, null, null, null, null, Solicitante, Ahora);
        var d = DevolucionCliente.SolicitarPorDepositoErroneo(Empresa, m, Cliente, importe, 1_000m, "depósito erróneo", Destino, Solicitante, Ahora);
        d.Autorizar(Guid.NewGuid(), Ahora);
        db.CuentasBancarias.Add(cuenta); db.MovimientosBancarios.Add(m); db.DevolucionesCliente.Add(d);
        await db.SaveChangesAsync();
        return (m, d);
    }

    private static async Task<(CuentaBancaria, DevolucionCliente)> Nc(TesoreriaDbContext db, string monedaCuenta, bool autorizada = true)
    {
        var cuenta = new CuentaBancaria(Empresa, "BBVA", "1234567890", null, monedaCuenta);
        var d = DevolucionCliente.SolicitarPorNotaCredito(Empresa, Guid.NewGuid(), "NC-1", null, Cliente, 300m, 500m, "MXN", null, "reembolso", null, Ahora);
        d.Editar(Cliente, 300m, 500m, "reembolso", Destino);
        if (autorizada) d.Autorizar(Guid.NewGuid(), Ahora);
        db.CuentasBancarias.Add(cuenta); db.DevolucionesCliente.Add(d);
        await db.SaveChangesAsync();
        return (cuenta, d);
    }

    [Fact]
    public async Task Pagar_deposito_crea_contramovimiento_y_marca_pagada_y_deposito_aplicado()
    {
        await using var db = Db(); var (m, d) = await Deposito(db);
        var r = await Handler(db).Handle(new(d.Id, Fecha, null, null, true, d.Version), default);
        r.Estado.Should().Be(EstadoDevolucionCliente.Pagada);
        var egreso = await db.MovimientosBancarios.SingleAsync(x => x.ContramovimientoDe == m.Id);
        r.MovimientoPagoId.Should().Be(egreso.Id);
        egreso.Sentido.Should().Be(SentidoMovimiento.Egreso);
        (await db.MovimientosBancarios.FirstAsync(x => x.Id == m.Id)).EstadoAplicacion.Should().Be(EstadoAplicacionMovimiento.Aplicado);
    }

    [Fact]
    public async Task Pagar_dos_veces_lanza_y_no_duplica_egreso()
    {
        await using var db = Db(); var (_, d) = await Deposito(db);
        var v = d.Version;
        await Handler(db).Handle(new(d.Id, Fecha, null, null, true, v), default);
        var act = () => Handler(db).Handle(new(d.Id, Fecha, null, null, true, d.Version), default);
        await act.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "DEV_NO_AUTORIZADA");
        (await db.MovimientosBancarios.CountAsync(x => x.Sentido == SentidoMovimiento.Egreso)).Should().Be(1);
    }

    [Fact]
    public async Task Periodo_cerrado_lanza_sin_egreso()
    {
        await using var db = Db(); var (_, d) = await Deposito(db);
        var act = () => Handler(db, abierto: false).Handle(new(d.Id, Fecha, null, null, true, d.Version), default);
        await act.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "MOV_PERIODO_CERRADO");
        (await db.MovimientosBancarios.CountAsync(x => x.Sentido == SentidoMovimiento.Egreso)).Should().Be(0);
    }

    [Fact]
    public async Task Nc_sin_cuenta_lanza()
    {
        await using var db = Db(); var (_, d) = await Nc(db, "MXN");
        var act = () => Handler(db).Handle(new(d.Id, Fecha, null, null, true, d.Version), default);
        await act.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "DEV_CUENTA_REQUERIDA");
    }

    [Fact]
    public async Task Nc_con_cuenta_en_otra_moneda_lanza()
    {
        await using var db = Db(); var (c, d) = await Nc(db, "USD");
        var act = () => Handler(db).Handle(new(d.Id, Fecha, c.Id, null, true, d.Version), default);
        await act.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "DEV_CUENTA_OTRA_MONEDA");
    }

    private sealed class EmpresaContext : ICurrentEmpresaContext
    { public Guid? Current => Empresa; public bool IsBypassed => false; public IDisposable Bypass() => new Noop(); private sealed class Noop : IDisposable { public void Dispose() { } } }
    private sealed class User(Guid id) : ICurrentUserContext { public Guid? UserId => id; public string? UserName => "Prueba"; }
    private sealed class Clock : IClock { public DateTimeOffset UtcNow => Ahora; }
    private sealed class Periodo(bool abierto) : IPeriodoContablePort { public Task<bool> EstaAbiertoAsync(int año, int mes, CancellationToken ct) => Task.FromResult(abierto); }
}
