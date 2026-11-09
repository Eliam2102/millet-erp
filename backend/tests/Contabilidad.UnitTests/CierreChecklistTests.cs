using Millet.Contabilidad.Application.Cierre;
using Millet.Contabilidad.Application.PublicPorts;
using Millet.Contabilidad.Domain;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Contabilidad.UnitTests;

/// <summary>
/// CON-12 (86e3jvvme): checklist de cierre (CA10.9 y CON-12-a) y cierre anual (CA10.13). Datos FIX; checklist propuesto P10.4 (R17)
/// mientras Millet no mande el suyo.
/// </summary>
public class CierreChecklistTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 2, 3, 18, 0, 0, TimeSpan.Zero);
    private static readonly ContextoVerificacionCierre Enero = new(Guid.NewGuid(), 2026, 1, new(2026, 1, 1), new(2026, 1, 31));

    private static PasoCierreContable Paso(string clave = PasosCierre.RepConciliados, bool obligatorio = true, TipoPasoCierre tipo = TipoPasoCierre.Automatico,
        AlcancePasoCierre alcance = AlcancePasoCierre.Mensual) =>
        new(Guid.NewGuid(), clave, "REP conciliados", "", tipo, alcance, 20, obligatorio);

    private static MarcaPasoCierre Marca(TipoMarcaPasoCierre tipo, string usuario = "Laura Contadora General") =>
        new(Guid.NewGuid(), Enero.PeriodoId, PasosCierre.RepConciliados, tipo, "FIX el proveedor manda el REP la próxima semana", Guid.NewGuid(), usuario, Ahora);

    // ─── CA10.9: el paso pendiente bloquea y dice qué falta ─────────────────

    [Fact]
    public void CA10_9_REP_sin_conciliar_bloquea_el_cierre_y_dice_que_falta()
    {
        var pendiente = ResultadoVerificacionPaso.Pendiente("1 documento(s) PPD pagados o cobrados sin REP.", ["Cobro sin REP emitido · FV 1203"]);

        var r = EvaluadorChecklistCierre.Combinar(Paso(), pendiente, "/contabilidad/reportes/liquidacion-iva", null, null);

        r.Estado.Should().Be(EstadoPasoChecklist.Pendiente);
        r.Bloquea.Should().BeTrue();
        r.Resumen.Should().Contain("sin REP");
        r.Detalle.Should().ContainSingle().Which.Should().Contain("FV 1203");
        r.Ruta.Should().Be("/contabilidad/reportes/liquidacion-iva");
    }

    [Fact]
    public void Un_paso_opcional_pendiente_se_avisa_pero_no_bloquea()
    {
        var r = EvaluadorChecklistCierre.Combinar(Paso(obligatorio: false), ResultadoVerificacionPaso.Pendiente("falta"), null, null, null);
        (r.Estado, r.Bloquea).Should().Be((EstadoPasoChecklist.Pendiente, false));
    }

    [Theory]
    [InlineData(EstadoVerificacionPaso.Cumple, EstadoPasoChecklist.Cumple)]
    [InlineData(EstadoVerificacionPaso.NoAplica, EstadoPasoChecklist.NoAplica)]
    public void Cumplido_o_sin_movimiento_no_bloquea(EstadoVerificacionPaso verificado, EstadoPasoChecklist esperado)
    {
        var r = EvaluadorChecklistCierre.Combinar(Paso(), new ResultadoVerificacionPaso(verificado, "ok", []), null, null, null);
        (r.Estado, r.Bloquea, r.Marca).Should().Be((esperado, false, (MarcaPasoResponse?)null));
    }

    // ─── CON-12-a: excepción autorizada con quién la autorizó ───────────────

    [Fact]
    public void CON12a_la_excepcion_autorizada_deja_cerrar_y_queda_con_quien_la_autorizo()
    {
        var excepcion = Marca(TipoMarcaPasoCierre.Excepcion);

        var r = EvaluadorChecklistCierre.Combinar(Paso(), ResultadoVerificacionPaso.Pendiente("sin REP"), null, excepcion, null);

        r.Estado.Should().Be(EstadoPasoChecklist.Excepcion);
        r.Bloquea.Should().BeFalse();
        r.Marca!.UsuarioNombre.Should().Be("Laura Contadora General");
        r.Marca.Motivo.Should().Contain("REP");
        r.Marca.RegistradaEn.Should().Be(Ahora);
    }

    [Fact]
    public void Si_el_paso_ya_se_cumple_la_excepcion_no_se_muestra()
    {
        var r = EvaluadorChecklistCierre.Combinar(Paso(), ResultadoVerificacionPaso.Cumple("ok"), null, Marca(TipoMarcaPasoCierre.Excepcion), null);
        (r.Estado, r.Marca).Should().Be((EstadoPasoChecklist.Cumple, (MarcaPasoResponse?)null));
    }

    [Fact]
    public void La_excepcion_exige_motivo()
    {
        var crear = () => new MarcaPasoCierre(Guid.NewGuid(), Guid.NewGuid(), PasosCierre.RepConciliados, TipoMarcaPasoCierre.Excepcion, "corto", null, "x", Ahora);
        crear.Should().Throw<BusinessRuleException>().Which.Code.Should().Be("CONTAB_CIERRE_MOTIVO_INVALIDO");
    }

    [Fact]
    public void Una_marca_retirada_no_se_retira_dos_veces()
    {
        var m = Marca(TipoMarcaPasoCierre.Excepcion);
        m.Retirar("Laura", Ahora);
        m.Vigente.Should().BeFalse();
        m.Invoking(x => x.Retirar("Laura", Ahora)).Should().Throw<ConflictException>();
    }

    // ─── Pasos manuales (nómina contra CFDI) ────────────────────────────────

    [Fact]
    public void El_paso_manual_queda_pendiente_hasta_confirmarlo_con_nota()
    {
        var paso = Paso(PasosCierre.NominaVsCfdi, tipo: TipoPasoCierre.Manual);

        EvaluadorChecklistCierre.Combinar(paso, EvaluadorChecklistCierre.Manual(null), null, null, null).Bloquea.Should().BeTrue();

        var confirmacion = Marca(TipoMarcaPasoCierre.Confirmacion, "Ana Auxiliar");
        var r = EvaluadorChecklistCierre.Combinar(paso, EvaluadorChecklistCierre.Manual(confirmacion), null, null, confirmacion);
        (r.Estado, r.Bloquea).Should().Be((EstadoPasoChecklist.Confirmado, false));
        r.Resumen.Should().StartWith("Confirmado por Ana Auxiliar");
    }

    [Fact]
    public void Los_pasos_mensuales_aplican_a_1_a_12_y_los_anuales_solo_al_13()
    {
        var mensual = Paso();
        var anual = Paso(PasosCierre.CierreAnual, alcance: AlcancePasoCierre.Anual);
        Enumerable.Range(1, 12).Should().OnlyContain(n => mensual.AplicaA(n) && !anual.AplicaA(n));
        (mensual.AplicaA(13), anual.AplicaA(13)).Should().Be((false, true));
    }

    [Fact]
    public void Configurar_un_paso_valida_nombre_y_orden()
    {
        var paso = Paso();
        paso.Configurar("Conciliación de REP", "Desc", 5, false, false);
        (paso.Nombre, paso.Orden, paso.Obligatorio, paso.Activo).Should().Be(("Conciliación de REP", 5, false, false));
        paso.Invoking(p => p.Configurar(" ", null, 1, true, true)).Should().Throw<BusinessRuleException>();
        paso.Invoking(p => p.Configurar("x", null, -1, true, true)).Should().Throw<BusinessRuleException>();
    }

    // ─── Verificadores con datos de otros módulos ───────────────────────────

    private sealed class Tesoreria(params CuentaBancariaSinConciliar[] pendientes) : IConciliacionBancariaCierreReadPort
    {
        public Task<IReadOnlyList<CuentaBancariaSinConciliar>> PendientesAsync(int anio, int mes, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<CuentaBancariaSinConciliar>>(pendientes);
    }

    [Fact]
    public async Task La_conciliacion_bancaria_lista_las_cuentas_sin_conciliar()
    {
        var r = await new VerificadorConciliacionBancaria(new Tesoreria(new CuentaBancariaSinConciliar(Guid.NewGuid(), "BBVA 0123 (MXN)", "conciliación abierta", 3)))
            .VerificarAsync(Enero, default);
        r.Estado.Should().Be(EstadoVerificacionPaso.Pendiente);
        r.Detalle.Should().ContainSingle().Which.Should().Be("BBVA 0123 (MXN): conciliación abierta · 3 movimiento(s) sin conciliar");

        (await new VerificadorConciliacionBancaria(new Tesoreria()).VerificarAsync(Enero, default)).Estado.Should().Be(EstadoVerificacionPaso.Cumple);
    }

    private sealed class Almacen(bool cerrado) : IInventarioCierreReadPort
    {
        public Task<bool> MesCerradoAsync(int anio, int mes, CancellationToken ct) => Task.FromResult(cerrado);
    }

    [Theory]
    [InlineData(true, EstadoVerificacionPaso.Cumple)]
    [InlineData(false, EstadoVerificacionPaso.Pendiente)]
    public async Task El_inventario_debe_estar_cerrado_en_almacen(bool cerrado, EstadoVerificacionPaso esperado) =>
        (await new VerificadorInventarioCerrado(new Almacen(cerrado)).VerificarAsync(Enero, default)).Estado.Should().Be(esperado);

    private sealed class Activos(bool hay, bool ejecutada) : IDepreciacionCierreReadPort
    {
        public Task<EstadoDepreciacionMes> EstadoAsync(int anio, int mes, CancellationToken ct) => Task.FromResult(new EstadoDepreciacionMes(hay, ejecutada));
    }

    [Theory]
    [InlineData(true, true, EstadoVerificacionPaso.Cumple)]
    [InlineData(true, false, EstadoVerificacionPaso.Pendiente)]
    [InlineData(false, false, EstadoVerificacionPaso.NoAplica)]
    public async Task La_depreciacion_del_mes_debe_estar_aplicada_si_hay_activos(bool hay, bool ejecutada, EstadoVerificacionPaso esperado) =>
        (await new VerificadorDepreciacion(new Activos(hay, ejecutada)).VerificarAsync(Enero, default)).Estado.Should().Be(esperado);

    private sealed class Cxp(bool ejecutada, int facturas) : IRevaluacionProveedoresCierreReadPort
    {
        public Task<EstadoRevaluacionProveedores> EstadoAsync(int anio, int mes, CancellationToken ct) =>
            Task.FromResult(new EstadoRevaluacionProveedores(ejecutada, facturas));
    }

    [Theory]
    [InlineData(true, 0, EstadoVerificacionPaso.Cumple)]
    [InlineData(false, 4, EstadoVerificacionPaso.Pendiente)]
    [InlineData(false, 0, EstadoVerificacionPaso.NoAplica)]
    public async Task La_revaluacion_de_proveedores_se_exige_solo_con_facturas_en_moneda_extranjera(bool ejecutada, int facturas, EstadoVerificacionPaso esperado) =>
        (await new VerificadorRevaluacionProveedores(new Cxp(ejecutada, facturas)).VerificarAsync(Enero, default)).Estado.Should().Be(esperado);

    [Fact]
    public void Si_la_revaluacion_no_se_puede_calcular_el_resumen_dice_la_causa()
    {
        const string sinTc = "Falta el tipo de cambio de USD del 30/11/2026 en el catálogo (K10.6). Cárgalo y vuelve a correr la revaluación.";
        VerificadorRevaluacionBancosClientes.SinCalculo([sinTc]).Should().Be($"No se pudo calcular la revaluación: {sinTc}");
        VerificadorRevaluacionBancosClientes.SinCalculo([sinTc, "Otro aviso"]).Should().Contain("USD del 30/11/2026").And.EndWith("(y 1 aviso(s) más).");
    }

    private sealed class Auxiliares(bool cuadra) : IConciliacionAuxiliaresResumenReadPort
    {
        public Task<ResumenConciliacionAuxiliares> ObtenerAsync(DateOnly corte, CancellationToken ct) => Task.FromResult(new ResumenConciliacionAuxiliares(corte, cuadra,
            [new("Clientes", "Cuadra", true, 0m), new("Proveedores", cuadra ? "Cuadra" : "Con diferencias", cuadra, cuadra ? 0m : 1_250.50m)]));
    }

    [Fact]
    public async Task Los_auxiliares_se_concilian_al_ultimo_dia_del_periodo()
    {
        var r = await new VerificadorAuxiliares(new Auxiliares(false)).VerificarAsync(Enero, default);
        r.Estado.Should().Be(EstadoVerificacionPaso.Pendiente);
        r.Detalle.Should().ContainSingle().Which.Should().Be("Proveedores: Con diferencias · diferencia 1,250.50");
        (await new VerificadorAuxiliares(new Auxiliares(true)).VerificarAsync(Enero, default)).Estado.Should().Be(EstadoVerificacionPaso.Cumple);
    }

    [Fact]
    public void El_detalle_se_corta_en_20_renglones()
    {
        var r = ResultadoVerificacionPaso.Pendiente("muchos", Enumerable.Range(1, 50).Select(i => $"doc {i}"));
        r.Detalle.Should().HaveCount(ResultadoVerificacionPaso.MaximoDetalle);
    }

    // ─── CA10.13: cierre anual ──────────────────────────────────────────────

    [Theory]
    [InlineData("Estado de Pérdidas y Ganancias (Resultado)", "401.01.00.00", true)]
    [InlineData("Estado de Posicion Financiera (Balance)", "305.01.00.00", false)]
    [InlineData("Notas a los Estados Financieros", "801.00.00.00", false)]
    [InlineData(null, "601.84.00.00", true)]
    [InlineData(null, "FIX-701.01.00.00", true)]
    [InlineData(null, "201.01.00.00", false)]
    [InlineData(null, "801.00.00.00", false)]
    public void Cuentas_de_resultados_por_grupo_de_reporte_o_rubro_400_a_799(string? grupo, string codigo, bool esperado) =>
        CalculadoraCierreAnual.EsResultados(grupo, codigo).Should().Be(esperado);

    [Fact]
    public void CA10_13_la_poliza_de_cierre_deja_resultados_en_cero_contra_resultado_del_ejercicio()
    {
        Guid ventas = Guid.NewGuid(), costo = Guid.NewGuid(), gastos = Guid.NewGuid(), resultado = Guid.NewGuid();
        Guid merida = Guid.NewGuid(), cancun = Guid.NewGuid();
        var saldos = new List<SaldoResultadoEjercicio>
        {
            new(ventas, "401.01", merida, null, null, null, -1_000_000m), // ingreso: saldo acreedor
            new(costo, "501.01", merida, null, null, null, 600_000m),
            new(gastos, "601.01", merida, Guid.NewGuid(), null, null, 150_000m),
            new(ventas, "401.01", cancun, null, null, null, -100_000m),
            new(gastos, "601.01", cancun, null, null, null, 180_000m), // Cancún dejó pérdida
        };

        var partidas = CalculadoraCierreAnual.Partidas(saldos, 2026, perdida: false, _ => resultado);

        partidas.Sum(p => p.Cargo).Should().Be(partidas.Sum(p => p.Abono), "la póliza cuadra");
        foreach (var s in saldos)
        {
            var p = partidas.Single(x => x.CuentaId == s.CuentaId && x.SucursalId == s.SucursalId && x.Dim1Id == s.Dim1Id);
            (s.Saldo + p.Cargo - p.Abono).Should().Be(0m, $"{s.CuentaCodigo} queda en cero");
        }
        var contra = partidas.Where(p => p.CuentaId == resultado).ToList();
        contra.Should().HaveCount(2);
        contra.Single(p => p.SucursalId == merida).Abono.Should().Be(250_000m, "utilidad de Mérida al haber de resultado del ejercicio");
        contra.Single(p => p.SucursalId == cancun).Cargo.Should().Be(80_000m, "la pérdida de Cancún va al debe");
        (contra.Sum(p => p.Abono) - contra.Sum(p => p.Cargo)).Should().Be(170_000m, "utilidad neta del ejercicio");
    }
}
