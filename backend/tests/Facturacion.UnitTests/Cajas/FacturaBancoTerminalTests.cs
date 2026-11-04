using Microsoft.EntityFrameworkCore;
using Millet.Facturacion.Application.Cajas.Terminal;
using Millet.Facturacion.Application.Integration;
using Millet.Facturacion.Domain.Cajas.Terminal;
using Millet.Facturacion.Domain.Ports;
using Millet.Facturacion.Infrastructure.Persistence;
using Millet.Facturacion.UnitTests.TestDoubles;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Facturacion.UnitTests.Cajas;

/// <summary>
/// 86e3xt2q8 · Parte B: factura mensual del banco ligada a los cargos diarios de la
/// terminal. Datos FICTICIOS: 30 cargos de octubre con comisión 200 e IVA 32 cada uno.
/// </summary>
public sealed class FacturaBancoTerminalTests
{
    private static readonly Guid Empresa = Guid.NewGuid();
    private static readonly DateTimeOffset Ahora = new(2026, 11, 3, 18, 0, 0, TimeSpan.Zero);
    private const string RfcBanco = "BAN010101AAA";
    private readonly FakeIntegrationEventPublisher _eventos = new();
    private readonly FakeCfdisBanco _cfdis = new();

    [Fact]
    public async Task Criterio_4_treinta_cargos_que_suman_6960_quedan_con_el_folio_fiscal_y_el_iva_pasa_a_pagado()
    {
        using var db = NewDb();
        await SembrarCargosAsync(db, 2026, 10, 30);
        var cfdi = _cfdis.Agregar(6_000m, 960m, 6_960m);

        var r = await Handler(db).Handle(new LigarFacturaBancoTerminalCommand(2026, 10, cfdi.Id), default);

        r.Cargos.Should().Be(30);
        r.Total.Should().Be(6_960m);
        var cargos = await db.LiquidacionesTerminal.ToListAsync();
        cargos.Should().HaveCount(30).And.OnlyContain(c => c.FacturaBancoId == r.Id && c.UuidFacturaBanco == cfdi.Uuid);
        var evento = _eventos.Publicados.OfType<TerminalFacturaBancoLigadaIntegrationEvent>().Single();
        evento.EventType.Should().Be("facturacion.terminal.factura-banco-ligada.v1");
        evento.IvaComision.Should().Be(960m, "el IVA de las comisiones pasa de pendiente a pagado");
        evento.Comision.Should().Be(6_000m);
        evento.UuidCfdi.Should().Be(cfdi.Uuid);
        evento.Cargos.Should().HaveCount(30);
    }

    [Fact]
    public async Task Criterio_5_la_misma_factura_por_7000_muestra_diferencia_de_40_y_no_liga()
    {
        using var db = NewDb();
        await SembrarCargosAsync(db, 2026, 10, 30);
        var cfdi = _cfdis.Agregar(6_034.48m, 965.52m, 7_000m);

        var cuadre = await Handler(db).Handle(new CuadreFacturaBancoQuery(2026, 10, cfdi.Id), default);
        cuadre.Cuadra.Should().BeFalse();
        cuadre.TotalCargos.Should().Be(6_960m);
        cuadre.DiferenciaTotal.Should().Be(40m);

        var ligar = () => Handler(db).Handle(new LigarFacturaBancoTerminalCommand(2026, 10, cfdi.Id), default);
        (await ligar.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "TERMINAL_FACTURA_NO_CUADRA"))
            .Which.Message.Should().Contain("40.00");
        (await db.LiquidacionesTerminal.AnyAsync(c => c.FacturaBancoId != null)).Should().BeFalse();
        (await db.FacturasBancoTerminal.AnyAsync()).Should().BeFalse();
        _eventos.Publicados.Should().BeEmpty();
    }

    [Fact]
    public async Task El_total_cuadra_pero_el_iva_no_entonces_no_liga()
    {
        using var db = NewDb();
        await SembrarCargosAsync(db, 2026, 10, 30);
        var cfdi = _cfdis.Agregar(6_010m, 950m, 6_960m);

        var cuadre = await Handler(db).Handle(new CuadreFacturaBancoQuery(2026, 10, cfdi.Id), default);

        cuadre.DiferenciaTotal.Should().Be(0m);
        cuadre.DiferenciaIva.Should().Be(-10m);
        cuadre.Cuadra.Should().BeFalse();
    }

    [Fact]
    public async Task Solo_suma_los_cargos_del_mes_sin_factura_y_la_misma_factura_no_se_liga_dos_veces()
    {
        using var db = NewDb();
        await SembrarCargosAsync(db, 2026, 10, 2);
        await SembrarCargosAsync(db, 2026, 11, 1, prefijo: "NOV");
        var octubre = _cfdis.Agregar(400m, 64m, 464m);

        var sinCfdi = await Handler(db).Handle(new CuadreFacturaBancoQuery(2026, 10, null), default);
        sinCfdi.Cargos.Should().Be(2, "el cargo de noviembre no entra en octubre");
        sinCfdi.TotalCargos.Should().Be(464m);

        await Handler(db).Handle(new LigarFacturaBancoTerminalCommand(2026, 10, octubre.Id), default);

        var otraVez = () => Handler(db).Handle(new LigarFacturaBancoTerminalCommand(2026, 10, octubre.Id), default);
        await otraVez.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "TERMINAL_FACTURA_YA_LIGADA");
        var otra = _cfdis.Agregar(400m, 64m, 464m);
        var sinCargos = () => Handler(db).Handle(new LigarFacturaBancoTerminalCommand(2026, 10, otra.Id), default);
        await sinCargos.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "TERMINAL_MES_SIN_CARGOS");
        (await Handler(db).Handle(new CuadreFacturaBancoQuery(2026, 10, null), default)).Cargos.Should().Be(0);

        var candidatos = await Handler(db).Handle(new CfdisBancoCandidatosQuery(2026, 10, null), default);
        candidatos.Select(c => c.Id).Should().Equal([otra.Id], "la factura ya ligada deja de ofrecerse");
        (await Handler(db).Handle(new FacturasBancoTerminalQuery(), default)).Should().ContainSingle()
            .Which.CfdiRecibidoId.Should().Be(octubre.Id);
    }

    [Fact]
    public async Task Rechaza_un_cfdi_que_no_es_de_ingreso()
    {
        using var db = NewDb();
        await SembrarCargosAsync(db, 2026, 10, 1);
        var egreso = _cfdis.Agregar(200m, 32m, 232m, tipo: "Egreso");

        var act = () => Handler(db).Handle(new LigarFacturaBancoTerminalCommand(2026, 10, egreso.Id), default);

        await act.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "TERMINAL_FACTURA_NO_INGRESO");
    }

    private FacturaBancoTerminalHandlers Handler(FacturacionDbContext db) =>
        new(db, _cfdis, new FakeEmpresaContext(Empresa), new FakeUserContext(Guid.NewGuid()), new FakeClock(Ahora), _eventos);

    private static FacturacionDbContext NewDb() =>
        new(new DbContextOptionsBuilder<FacturacionDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            new FakeEmpresaContext(Empresa));

    /// <summary>Un cargo diario (liquidación de una venta de 10,000: comisión 200, IVA 32, neto 9,768) por día.</summary>
    private static async Task SembrarCargosAsync(FacturacionDbContext db, int anio, int mes, int dias, string prefijo = "OCT")
    {
        var carga = CargaTerminal.Iniciar(Empresa, "prueba.xls", $"HUELLA-{prefijo}", new DateOnly(anio, mes, 1), Guid.NewGuid(), Ahora);
        db.CargasTerminal.Add(carga);
        for (var d = 1; d <= dias; d++)
        {
            var t = TransaccionTerminal.Registrar(Empresa, carga.Id, new RenglonTerminal(
                $"{prefijo}-{d:00}", new DateTime(anio, mes, d, 12, 0, 0), 10_000m, "MXN", "4321", $"{d:000000}", "Aprobada", true,
                200m, 32m, 9_768m, $"LIQ-{prefijo}-{d:00}"));
            t.Emparejar(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
            db.TransaccionesTerminal.Add(t);
            db.LiquidacionesTerminal.Add(LiquidacionTerminal.Crear(Empresa, carga.Id, t.FolioLiquidacion!, new DateOnly(anio, mes, d), [t]));
        }
        await db.SaveChangesAsync();
    }

    private sealed class FakeCfdisBanco : ICfdiRecibidoBancoReadPort
    {
        private readonly List<CfdiRecibidoBanco> _cfdis = [];

        public CfdiRecibidoBanco Agregar(decimal subtotal, decimal iva, decimal total, string tipo = "Ingreso")
        {
            var c = new CfdiRecibidoBanco(Guid.NewGuid(), Guid.NewGuid().ToString().ToUpperInvariant(), RfcBanco, "A",
                $"{_cfdis.Count + 1}", new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero), subtotal, iva, total, "MXN", tipo, "PorProcesar");
            _cfdis.Add(c);
            return c;
        }

        public Task<IReadOnlyList<CfdiRecibidoBanco>> ListarParaMesAsync(int anio, int mes, string? rfcEmisor, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<CfdiRecibidoBanco>>(_cfdis.Where(c => rfcEmisor is null || c.RfcEmisor == rfcEmisor).ToList());

        public Task<CfdiRecibidoBanco?> ObtenerAsync(Guid cfdiRecibidoId, CancellationToken cancellationToken) =>
            Task.FromResult(_cfdis.FirstOrDefault(c => c.Id == cfdiRecibidoId));
    }
}
