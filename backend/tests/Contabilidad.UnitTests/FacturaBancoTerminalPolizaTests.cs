using System.Text.Json;
using System.Text.RegularExpressions;
using Millet.Contabilidad.Application.Motor;
using Millet.Contabilidad.Domain;
using N = Millet.Contabilidad.Application.Motor.NormalizadorEventos;

namespace Millet.Contabilidad.UnitTests;

/// <summary>
/// 86e3zcbjx · póliza de la factura mensual del banco por las comisiones de la terminal: lectura de
/// <c>facturacion.terminal.factura-banco-ligada.v1</c> (payload como el outbox, PascalCase), cuadre de la plantilla sembrada con el
/// centavo a favor o en contra y oyente de Facturación. Datos FICTICIOS (30 cargos de octubre: comisión 6,000 e IVA 960).
/// </summary>
public class FacturaBancoTerminalPolizaTests
{
    private static readonly Guid Empresa = Guid.NewGuid(), Factura = Guid.NewGuid(), Sucursal = Guid.NewGuid();
    private static readonly DateTimeOffset Ocurrido = new(2026, 11, 4, 18, 0, 0, TimeSpan.Zero);

    private static string Evento(decimal comisionFactura, decimal ivaFactura, decimal redondeo, bool conImportesFactura = true) =>
        JsonSerializer.Serialize(conImportesFactura
            ? (object)new
            {
                EventType = N.FacturaBancoTerminalLigada, EmpresaId = Empresa, OcurridoEn = Ocurrido, FacturaBancoId = Factura,
                CfdiRecibidoId = Guid.NewGuid(), UuidCfdi = "AAAAAAAA-0000-0000-0000-000000000001", RfcEmisor = "BAN010101AAA",
                Anio = 2026, Mes = 10, Comision = 6_000m, IvaComision = 960m, Total = 6_960m,
                Cargos = new[] { new { LiquidacionId = Guid.NewGuid(), FolioLiquidacion = "LIQ-1", Fecha = new DateOnly(2026, 10, 1), Comision = 200m, IvaComision = 32m } },
                ComisionFactura = comisionFactura, IvaFactura = ivaFactura, Redondeo = redondeo, SucursalId = Sucursal,
            }
            : new
            {
                EventType = N.FacturaBancoTerminalLigada, EmpresaId = Empresa, OcurridoEn = Ocurrido, FacturaBancoId = Factura,
                CfdiRecibidoId = Guid.NewGuid(), UuidCfdi = "AAAAAAAA-0000-0000-0000-000000000001", RfcEmisor = "BAN010101AAA",
                Anio = 2026, Mes = 10, Comision = 6_000m, IvaComision = 960m, Total = 6_960m, Cargos = Array.Empty<object>(),
            });

    private static EventoContable Normalizar(string payload) => N.Normalizar(N.FacturaBancoTerminalLigada, Guid.NewGuid(), payload);

    [Fact]
    public void Se_normaliza_con_los_importes_del_cfdi_lo_que_cargo_el_banco_y_el_ultimo_dia_del_mes()
    {
        var e = Normalizar(Evento(6_000.01m, 960m, -0.01m));

        e.Tipo.Should().Be(N.FacturaBancoTerminalLigada);
        e.EmpresaId.Should().Be(Empresa);
        e.SucursalId.Should().Be(Sucursal);
        e.FechaContable.Should().Be(new DateOnly(2026, 10, 31), "la comisión es gasto del mes en que el banco la cobró");
        e.Documento.Should().Match<DocumentoOrigenPoliza>(d => d.Modulo == "facturacion" && d.Tipo == "factura_banco_terminal"
            && d.Id == Factura && d.Folio == "2026-10" && d.Uuid == "AAAAAAAA-0000-0000-0000-000000000001");
        e.Importes["Comision"].Should().Be(6_000.01m);
        e.Importes["Iva"].Should().Be(960m);
        e.Importes["Total"].Should().Be(6_960m);
        e.Importes["Redondeo"].Should().Be(-0.01m);
        N.SinMovimiento(e).Should().BeFalse();
    }

    [Fact]
    public void Un_evento_anterior_sin_importes_del_cfdi_usa_los_de_los_cargos_y_sin_redondeo()
    {
        var e = Normalizar(Evento(0m, 0m, 0m, conImportesFactura: false));

        e.Importes["Comision"].Should().Be(6_000m);
        e.Importes["Iva"].Should().Be(960m);
        e.Importes["Redondeo"].Should().Be(0m);
        e.SucursalId.Should().BeNull("sin sucursal el motor lo deja en la bandeja como dato faltante");
    }

    [Theory]
    [InlineData(6_000.01, 960, -0.01)] // la factura trae un centavo más: abono a redondeo
    [InlineData(5_999.99, 960, 0.01)]  // un centavo menos: cargo a redondeo
    [InlineData(6_000, 960, 0)]        // al centavo: sin partida de redondeo
    public void C2_la_plantilla_sembrada_cuadra_comision_e_iva_pagado_contra_el_banco(double comision, double iva, double redondeo)
    {
        var e = Normalizar(Evento((decimal)comision, (decimal)iva, (decimal)redondeo));
        var partidas = Partidas(PlantillaSembrada(), e);

        partidas.Sum(p => p.Cargo).Should().Be(partidas.Sum(p => p.Abono)).And.Be(6_960m + Math.Max(0m, -(decimal)redondeo));
        partidas.Should().ContainSingle(p => p.Rol == "COMISIONES_BANCARIAS").Which.Cargo.Should().Be((decimal)comision);
        partidas.Should().ContainSingle(p => p.Rol == "IVA_ACREDITABLE_PAGADO").Which.Cargo.Should().Be((decimal)iva);
        partidas.Should().ContainSingle(p => p.Rol == "BANCO_TERMINAL_TARJETA").Which.Abono.Should().Be(6_960m);
        var partidaRedondeo = partidas.Where(p => p.Rol == "REDONDEO").ToList();
        if (redondeo == 0) partidaRedondeo.Should().BeEmpty();
        else partidaRedondeo.Single().Should().Be(("REDONDEO", Math.Max(0m, (decimal)redondeo), Math.Max(0m, -(decimal)redondeo)));
    }

    [Fact]
    public void La_migracion_siembra_los_roles_nuevos_sin_cuenta_y_reusa_comisiones_e_iva_pagado()
    {
        var m = Migracion();
        m.Should().Contain("'BANCO_TERMINAL_TARJETA'").And.Contain("'REDONDEO'").And.Contain("703.21.00.00");
        PlantillaSembrada().Select(l => l.Rol).Should()
            .Equal("COMISIONES_BANCARIAS", "IVA_ACREDITABLE_PAGADO", "REDONDEO", "BANCO_TERMINAL_TARJETA");
    }

    [Fact]
    public void El_oyente_de_Facturacion_contabiliza_la_factura_del_banco()
    {
        var w = new Millet.Contabilidad.Infrastructure.Workers.FacturacionContabilidadListenerWorker(null, null!,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<Millet.Contabilidad.Infrastructure.Workers.FacturacionContabilidadListenerWorker>.Instance);

        w.TiposContables.Should().Contain(N.FacturaBancoTerminalLigada);
    }

    /// <summary>Mismo cálculo de lados que el motor (<c>MotorPolizas.ArmarAsync</c>): un importe negativo invierte el lado; cero no genera partida.</summary>
    private static List<(string Rol, decimal Cargo, decimal Abono)> Partidas(IEnumerable<LineaPlantillaContable> plantilla, EventoContable e)
    {
        var partidas = new List<(string, decimal, decimal)>();
        foreach (var l in plantilla.OrderBy(x => x.Orden))
        {
            var importe = e.Importes[l.Campo]!.Value;
            if (importe == 0m) continue;
            var cargo = l.Lado == LadoPartida.Cargo ? importe : 0m;
            var abono = l.Lado == LadoPartida.Abono ? importe : 0m;
            if (importe < 0) (cargo, abono) = (-abono, -cargo);
            partidas.Add((l.Rol, cargo, abono));
        }
        return partidas;
    }

    private static string Migracion()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "backend", "src"))) dir = dir.Parent;
        dir.Should().NotBeNull("las pruebas corren dentro del repositorio");
        return File.ReadAllText(Directory.GetFiles(Path.Combine(dir!.FullName, "backend", "src", "Contabilidad", "Infrastructure", "Persistence", "Migrations"),
            "*_TerminalPolizaFacturaBanco.cs").Single());
    }

    private static readonly JsonSerializerOptions OpcionesPlantilla = new() { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };

    private static List<LineaPlantillaContable> PlantillaSembrada()
    {
        var m = Regex.Match(Migracion(), @"\('facturacion\.terminal\.factura-banco-ligada\.v1', 'AS', '[^']*', '[^']*', '(?<lineas>\[[^']*\])'\)");
        m.Success.Should().BeTrue();
        return JsonSerializer.Deserialize<List<LineaPlantillaContable>>(m.Groups["lineas"].Value, OpcionesPlantilla)!;
    }
}
