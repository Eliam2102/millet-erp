using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Millet.Integraciones.Aw.Application.Pedidos;
using Millet.Facturacion.Domain.Ports;
using Millet.Integraciones.Aw.Application.Retorno;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Integraciones.Aw.UnitTests.Retorno;

public sealed class AwRetornoDocumentoServiceTests
{
    private sealed class PuertoFalso : IAwRetornoPort
    {
        public List<AwRetornoDocumento> Agregados { get; } = [];
        public bool Apagado { get; init; }
        public Task<int?> AgregarDocumentoAsync(AwRetornoDocumento d, CancellationToken ct)
        {
            if (Apagado) throw new BusinessRuleException("AW_ORIGEN_SIN_CONFIGURAR", "apagado");
            Agregados.Add(d);
            return Task.FromResult<int?>(Agregados.Count);
        }
        public List<AwRetornoAcuse> Acuses { get; init; } = [];
        public List<(string Pedido, long Version, string Folio, IReadOnlyList<AwPagoGrupo> Grupos, bool Exigir)> Pagos { get; } = [];
        public Task<int> AgregarPagosAsync(string numeroPedido, long versionPedido, string folio,
            IReadOnlyList<AwPagoGrupo> grupos, bool exigirDocumento, CancellationToken ct)
        {
            if (Apagado) throw new BusinessRuleException("AW_ORIGEN_SIN_CONFIGURAR", "apagado");
            Pagos.Add((numeroPedido, versionPedido, folio, grupos, exigirDocumento));
            return Task.FromResult(grupos.Count);
        }
        public List<(string Pedido, long Version, short Estatus, byte? Origen, string? Usuario)> Estatus { get; } = [];
        public Task<int?> AgregarEstatusAsync(string numeroPedido, long versionPedido, short estatus, byte? origen, string? usuarioLibera, CancellationToken ct)
        {
            if (Apagado) throw new BusinessRuleException("AW_ORIGEN_SIN_CONFIGURAR", "apagado");
            Estatus.Add((numeroPedido, versionPedido, estatus, origen, usuarioLibera));
            return Task.FromResult<int?>(Estatus.Count);
        }
        public List<(string Pedido, decimal Saldo)> Saldos { get; } = [];
        public Task<int?> AgregarSaldoAsync(string numeroPedido, long versionPedido, decimal saldo, CancellationToken ct)
        {
            Saldos.Add((numeroPedido, saldo));
            return Task.FromResult<int?>(Saldos.Count);
        }
        public Task<IReadOnlyList<AwRetornoAcuse>> LeerAcusesPorAtenderAsync(CancellationToken ct) =>
            Apagado ? throw new BusinessRuleException("AW_ORIGEN_SIN_CONFIGURAR", "apagado")
                    : Task.FromResult<IReadOnlyList<AwRetornoAcuse>>(Acuses);
    }

    private sealed class VersionFalsa : IAwVersionPedidoReadPort
    {
        public Dictionary<string, long> PorPedido { get; } = [];
        public Task<long?> VersionAplicadaAsync(string numeroPedido, CancellationToken ct) =>
            Task.FromResult(PorPedido.TryGetValue(numeroPedido, out var v) ? (long?)v : null);
    }

    private sealed class PagosFalsos : IAwPagosReadPort
    {
        public List<PagosFacturaAw> Facturas { get; } = [];
        public Task<IReadOnlyList<PagosFacturaAw>> PorComprobanteAsync(Guid comprobanteId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<PagosFacturaAw>>(Facturas);
        public Task<PagosFacturaAw?> PorFolioAsync(string numeroPedidoAw, string folio, CancellationToken ct) =>
            Task.FromResult(Facturas.FirstOrDefault(f => f.NumeroPedidoAw == numeroPedidoAw && f.Folio == folio));
    }

    private static AwRetornoDocumentoService Servicio(PuertoFalso p, VersionFalsa? v = null, PagosFalsos? pg = null,
        AwPedidosOptions? o = null) =>
        new(p, v ?? new VersionFalsa(), pg ?? new PagosFalsos(), Options.Create(o ?? new AwPedidosOptions()),
            NullLogger<AwRetornoDocumentoService>.Instance);

    private static PagosFalsos UnaFactura(params PagoGrupoAw[] grupos)
    {
        var pg = new PagosFalsos();
        pg.Facturas.Add(new PagosFacturaAw("900101", "F-1", true, grupos));
        return pg;
    }

    [Fact]
    public async Task Factura_de_pedido_AW_arma_la_fila_Documento_con_pedido_version_folio_uuid_e_importe()
    {
        var p = new PuertoFalso();
        await Servicio(p).RegistrarFacturaAsync(new("UUID-1", 1160m, "F-9", "900101", 4), default);
        p.Agregados.Should().ContainSingle().Which.Should().Be(
            new AwRetornoDocumento("900101", "FacturaFinal", 4, "F-9", "UUID-1", 1160m));
    }

    [Fact]
    public async Task Nota_de_credito_de_A_W_arma_la_fila_NotaCredito_con_el_folio_fiscal()
    {
        var p = new PuertoFalso();
        await Servicio(p).RegistrarNotaCreditoAsync(new("UUID-NC", 1500m, "NC-7", "900101", 4), default);
        p.Agregados.Should().ContainSingle().Which.Should().Be(
            new AwRetornoDocumento("900101", "NotaCredito", 4, "NC-7", "UUID-NC", 1500m));
    }

    [Fact]
    public async Task Nota_de_credito_del_ERP_sin_pedido_de_A_W_no_escribe_nada()
    {
        var p = new PuertoFalso();
        (await Servicio(p).RegistrarNotaCreditoAsync(new("UUID-NC", 1500m, "NC-7"), default)).Should().BeNull();
        p.Agregados.Should().BeEmpty();
    }

    [Fact]
    public async Task Factura_de_pedido_no_AW_no_escribe_nada()
    {
        var p = new PuertoFalso();
        (await Servicio(p).RegistrarFacturaAsync(new("UUID-1", 1160m, "F-9"), default)).Should().BeNull();
        p.Agregados.Should().BeEmpty();
    }

    [Fact]
    public async Task Con_el_canal_sin_configurar_no_falla_ni_escribe()
    {
        var p = new PuertoFalso { Apagado = true };
        (await Servicio(p).RegistrarFacturaAsync(new("UUID-1", 1160m, "F-9", "900101", 4), default)).Should().BeNull();
        (await Servicio(p).ConciliarAcusesAsync(default)).Should().Be(new AwRetornoConciliacion(0, 0, 0));
    }

    [Fact]
    public async Task El_115_se_manda_con_la_version_aplicada_y_sin_version_se_omite()
    {
        var p = new PuertoFalso(); var v = new VersionFalsa();
        (await Servicio(p, v).RegistrarFacturadoEntregadoAsync("900101", default)).Should().BeNull();
        v.PorPedido["900101"] = 4;
        await Servicio(p, v).RegistrarFacturadoEntregadoAsync("900101", default);
        p.Estatus.Should().ContainSingle().Which.Should().Be(("900101", 4L, (short)115, (byte?)null, (string?)null));
    }

    [Fact]
    public async Task La_liberacion_manual_va_como_70_con_origen_manual_y_usuario_y_sin_usuario_no_se_manda()
    {
        var p = new PuertoFalso(); var v = new VersionFalsa(); v.PorPedido["900101"] = 2;
        (await Servicio(p, v).RegistrarLiberacionManualAsync("900101", " ", default)).Should().BeNull();
        await Servicio(p, v).RegistrarLiberacionManualAsync("900101", "cobranza.mx", default);
        p.Estatus.Should().ContainSingle().Which.Should().Be(("900101", 2L, (short)70, (byte?)2, (string?)"cobranza.mx"));
    }

    [Fact]
    public async Task El_saldo_se_manda_como_cifra_de_control_con_la_version_aplicada()
    {
        var p = new PuertoFalso(); var v = new VersionFalsa(); v.PorPedido["900101"] = 2;
        await Servicio(p, v).RegistrarSaldoControlAsync("900101", 350m, default);
        p.Saldos.Should().ContainSingle().Which.Should().Be(("900101", 350m));
    }

    private static AwRetornoAcuse Estatus70(string motivo, int intentos = 3) => new("900101", 5, 2, null, null, null, null,
        DateTimeOffset.UtcNow, 2, motivo, intentos, AwRetornoAcuse.TipoEstatusFila, EstatusDestino: 70, Origen: 2, UsuarioLibera: "cobranza.mx");

    [Fact]
    public async Task Un_70_a_un_pedido_ya_liberado_es_nada_que_hacer_y_no_cuenta_como_detenida()
    {
        var p = new PuertoFalso { Acuses = [Estatus70("Nada que hacer: el pedido ya estaba liberado")] };
        (await Servicio(p).ConciliarAcusesAsync(default)).Should().Be(new AwRetornoConciliacion(0, 0, 0));
    }

    [Fact]
    public async Task Un_70_rechazado_por_otra_causa_sigue_contando_como_detenida()
    {
        var p = new PuertoFalso { Acuses = [Estatus70("Estatus del pedido inválido")] };
        (await Servicio(p).ConciliarAcusesAsync(default)).Should().Be(new AwRetornoConciliacion(0, 0, 1));
    }

    [Fact]
    public async Task Un_estatus_desfasado_se_reemplaza_con_la_version_nueva_y_el_mismo_origen_y_usuario()
    {
        var acuse = Estatus70("versión mayor", 1) with { Resultado = 3 };
        var p = new PuertoFalso { Acuses = [acuse] };
        var v = new VersionFalsa(); v.PorPedido["900101"] = 3;
        (await Servicio(p, v).ConciliarAcusesAsync(default)).Should().Be(new AwRetornoConciliacion(1, 0, 0));
        p.Estatus.Should().ContainSingle().Which.Should().Be(("900101", 3L, (short)70, (byte?)2, (string?)"cobranza.mx"));
    }

    [Fact]
    public async Task Sin_equivalencia_de_forma_de_pago_avisa_en_el_log_ademas_de_fallar()
    {
        var log = new LogEspia();
        var v = new VersionFalsa(); v.PorPedido["900101"] = 1;
        var o = new AwPedidosOptions(); o.MapeoFormaPagoAw["01"] = "EFECTIVO";
        var s = new AwRetornoDocumentoService(new PuertoFalso(), v, UnaFactura(new PagoGrupoAw("28", "MXN", 10m)), Options.Create(o), log);
        await FluentActions.Awaiting(() => s.RegistrarPagosAsync(Guid.NewGuid(), default)).Should().ThrowAsync<BusinessRuleException>();
        log.Avisos.Should().ContainSingle().Which.Should().Contain("«28»");
    }

    private sealed class LogEspia : Microsoft.Extensions.Logging.ILogger<AwRetornoDocumentoService>
    {
        public List<string> Avisos { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;
        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == Microsoft.Extensions.Logging.LogLevel.Warning) Avisos.Add(formatter(state, exception));
        }
    }

    [Fact]
    public async Task La_moneda_de_pesos_va_como_PESOSMX_de_A_W_y_la_forma_como_su_BEZ_completo()
    {
        var p = new PuertoFalso(); var v = new VersionFalsa(); v.PorPedido["900101"] = 1;
        var o = new AwPedidosOptions();
        o.MapeoFormaPagoAw["04"] = "TARJETA CREDITO"; o.MapeoMonedaAw["MXN"] = "PESOSMX";
        await Servicio(p, v, UnaFactura(new PagoGrupoAw("04", "MXN", 80m)), o).RegistrarPagosAsync(Guid.NewGuid(), default);
        p.Pagos.Single().Grupos.Should().Equal(new AwPagoGrupo("TARJETA CREDITO", "PESOSMX", 80m));
    }

    private static AwRetornoAcuse PagoDesfasado(long version) => new("900101", 3, version, null, "F-1", null, 500m,
        DateTimeOffset.UtcNow, 3, "versión mayor", 1, AwRetornoAcuse.TipoPagoFila, "03", "MXN");

    [Fact]
    public async Task Un_pago_desfasado_espera_mientras_el_ERP_no_tenga_una_version_mayor()
    {
        var p = new PuertoFalso { Acuses = [PagoDesfasado(2)] };
        var v = new VersionFalsa(); v.PorPedido["900101"] = 2;
        (await Servicio(p, v, UnaFactura(new PagoGrupoAw("03", "MXN", 500m))).ConciliarAcusesAsync(default))
            .Should().Be(new AwRetornoConciliacion(0, 1, 0));
        p.Pagos.Should().BeEmpty();
        p.Agregados.Should().BeEmpty();
    }

    [Fact]
    public async Task Un_pago_desfasado_se_recalcula_de_Facturacion_con_la_version_nueva()
    {
        var p = new PuertoFalso { Acuses = [PagoDesfasado(2)] };
        var v = new VersionFalsa(); v.PorPedido["900101"] = 3;
        (await Servicio(p, v, UnaFactura(new PagoGrupoAw("03", "MXN", 700m))).ConciliarAcusesAsync(default))
            .Should().Be(new AwRetornoConciliacion(1, 0, 0));
        p.Pagos.Should().ContainSingle().Which.Should().BeEquivalentTo(
            (Pedido: "900101", Version: 3L, Folio: "F-1", Grupos: new[] { new AwPagoGrupo("03", "MXN", 700m) }, Exigir: false));
    }

    [Fact]
    public async Task Los_pagos_de_la_factura_viajan_con_la_version_del_pedido_y_exigen_su_documento()
    {
        var p = new PuertoFalso();
        var v = new VersionFalsa(); v.PorPedido["900101"] = 4;
        (await Servicio(p, v, UnaFactura(new PagoGrupoAw("01", "MXN", 300m), new PagoGrupoAw("03", "USD", 20m))).RegistrarPagosAsync(Guid.NewGuid(), default))
            .Should().Be(2);
        var fila = p.Pagos.Should().ContainSingle().Subject;
        (fila.Pedido, fila.Version, fila.Folio, fila.Exigir).Should().Be(("900101", 4L, "F-1", true));
        fila.Grupos.Should().Equal(new AwPagoGrupo("01", "MXN", 300m), new AwPagoGrupo("03", "USD", 20m));
    }

    [Fact]
    public async Task Sin_version_aplicada_del_pedido_el_pago_se_omite()
    {
        var p = new PuertoFalso();
        (await Servicio(p, pg: UnaFactura(new PagoGrupoAw("01", "MXN", 300m))).RegistrarPagosAsync(Guid.NewGuid(), default)).Should().Be(0);
        p.Pagos.Should().BeEmpty();
    }

    [Fact]
    public async Task Con_equivalencia_configurada_se_mandan_los_valores_de_A_W_y_se_suman_los_que_coinciden()
    {
        var p = new PuertoFalso();
        var v = new VersionFalsa(); v.PorPedido["900101"] = 1;
        var o = new AwPedidosOptions();
        o.MapeoFormaPagoAw["01"] = "EFECTIVO"; o.MapeoFormaPagoAw["28"] = "TARJETA"; o.MapeoFormaPagoAw["04"] = "TARJETA";
        o.MapeoMonedaAw["MXN"] = "MN";
        await Servicio(p, v, UnaFactura(new PagoGrupoAw("01", "MXN", 100m), new PagoGrupoAw("04", "MXN", 50m), new PagoGrupoAw("28", "MXN", 25m)), o)
            .RegistrarPagosAsync(Guid.NewGuid(), default);
        p.Pagos.Single().Grupos.Should().BeEquivalentTo(new[] { new AwPagoGrupo("EFECTIVO", "MN", 100m), new AwPagoGrupo("TARJETA", "MN", 75m) });
    }

    [Fact]
    public async Task Una_forma_sin_equivalencia_no_se_manda_y_falla_para_reintentar()
    {
        var p = new PuertoFalso();
        var v = new VersionFalsa(); v.PorPedido["900101"] = 1;
        var o = new AwPedidosOptions(); o.MapeoFormaPagoAw["01"] = "EFECTIVO";
        await FluentActions.Awaiting(() => Servicio(p, v, UnaFactura(new PagoGrupoAw("03", "MXN", 100m)), o).RegistrarPagosAsync(Guid.NewGuid(), default))
            .Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "AW_RETORNO_MAPEO_PAGO");
        p.Pagos.Should().BeEmpty();
    }

    [Fact]
    public async Task Con_el_canal_sin_configurar_los_pagos_no_fallan_ni_escriben()
    {
        var p = new PuertoFalso { Apagado = true };
        var v = new VersionFalsa(); v.PorPedido["900101"] = 1;
        (await Servicio(p, v, UnaFactura(new PagoGrupoAw("01", "MXN", 100m))).RegistrarPagosAsync(Guid.NewGuid(), default)).Should().Be(0);
    }
}
