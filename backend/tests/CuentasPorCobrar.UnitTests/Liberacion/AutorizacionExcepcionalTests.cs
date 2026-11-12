using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Millet.CuentasPorCobrar.Application.Autorizaciones;
using Millet.CuentasPorCobrar.Domain.Liberacion;
using Millet.CuentasPorCobrar.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorCobrar.UnitTests.Liberacion;

/// <summary>
/// M-239: la solicitud de excepción de crédito registra monto y condición; arriba del umbral queda pendiente hasta la
/// segunda firma (Dirección); la vigencia sale de configuración y el monto autorizado limita lo que se libera.
/// </summary>
public sealed class AutorizacionExcepcionalTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 11, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid EmpresaId = Guid.NewGuid();
    private static readonly Guid Credito = Guid.NewGuid();      // Crédito y Cobranza: solicita y da la primera firma
    private static readonly Guid Beneficiario = Guid.NewGuid();
    private static readonly Guid Direccion = Guid.NewGuid();

    private static AutorizacionCredito Excepcion(
        decimal monto = 80_000m, short firmas = 2, bool deDireccion = false, TimeSpan? vigencia = null, TimeSpan? maxima = null) =>
        AutorizacionCredito.CrearExcepcional(EmpresaId, Credito, Beneficiario, "Pago en tránsito", "6023900", Ahora,
            vigencia ?? TimeSpan.FromHours(24), maxima ?? TimeSpan.FromHours(24),
            CondicionExcepcionCredito.CoberturaParcial, monto, "MXN", firmas, deDireccion);

    // ---------------------------------------------------------------- dominio

    [Fact]
    public void Arriba_del_umbral_queda_pendiente_hasta_la_segunda_firma_y_no_se_puede_consumir()
    {
        var a = Excepcion();

        a.Estado.Should().Be(EstadoAutorizacionCredito.PendienteSegundaFirma);
        (a.Condicion, a.MontoAutorizado, a.Moneda, a.FirmasRequeridas)
            .Should().Be((CondicionExcepcionCredito.CoberturaParcial, 80_000m, "MXN", (short)2));

        var consumir = () => a.Consumir(Guid.NewGuid(), Beneficiario, Ahora, "6023900");
        consumir.Should().Throw<BusinessRuleException>().Where(e => e.Code == "AC_PENDIENTE_SEGUNDA_FIRMA");
        a.Estado.Should().Be(EstadoAutorizacionCredito.PendienteSegundaFirma);
    }

    [Fact]
    public void Ni_quien_solicita_ni_quien_dio_la_primera_firma_puede_dar_la_segunda()
    {
        var a = Excepcion();

        // Quien solicita es quien da la primera firma (Crédito y Cobranza).
        var mismo = () => a.Firmar(Credito, Ahora);
        mismo.Should().Throw<BusinessRuleException>().Where(e => e.Code == "AC_FIRMA_MISMA_PERSONA");

        // El beneficiario tampoco firma su propia excepción.
        var beneficiario = () => a.Firmar(Beneficiario, Ahora);
        beneficiario.Should().Throw<BusinessRuleException>().Where(e => e.Code == "AC_FIRMA_BENEFICIARIO");

        a.Estado.Should().Be(EstadoAutorizacionCredito.PendienteSegundaFirma);
        a.SegundaFirmaPorId.Should().BeNull();
    }

    [Fact]
    public void La_segunda_firma_de_otra_persona_la_deja_autorizada_y_ya_se_consume()
    {
        var a = Excepcion();

        a.Firmar(Direccion, Ahora.AddHours(1));

        (a.Estado, a.AutorizadaPorDireccion, a.SegundaFirmaPorId, a.SegundaFirmaEn)
            .Should().Be((EstadoAutorizacionCredito.Autorizada, true, (Guid?)Direccion, (DateTimeOffset?)Ahora.AddHours(1)));
        a.Consumir(Guid.NewGuid(), Beneficiario, Ahora.AddHours(2), "6023900");
        a.Estado.Should().Be(EstadoAutorizacionCredito.Usada);
    }

    [Fact]
    public void La_segunda_firma_solo_vale_dentro_de_la_vigencia_de_la_solicitud_y_solo_si_esta_pendiente()
    {
        var vencida = () => Excepcion().Firmar(Direccion, Ahora.AddHours(25));
        vencida.Should().Throw<BusinessRuleException>().Where(e => e.Code == "AC_VENCIDA");

        var firmada = Excepcion();
        firmada.Firmar(Direccion, Ahora);
        var otraVez = () => firmada.Firmar(Guid.NewGuid(), Ahora);
        otraVez.Should().Throw<BusinessRuleException>().Where(e => e.Code == "AC_NO_PENDIENTE_FIRMA");

        var unaFirma = () => Excepcion(monto: 10_000m, firmas: 1).Firmar(Direccion, Ahora);
        unaFirma.Should().Throw<BusinessRuleException>().Where(e => e.Code == "AC_NO_PENDIENTE_FIRMA");
    }

    [Fact]
    public void Hasta_el_umbral_basta_la_firma_de_Credito_y_si_la_otorga_Direccion_queda_firmada_completa()
    {
        Excepcion(monto: 10_000m, firmas: 1).Estado.Should().Be(EstadoAutorizacionCredito.Autorizada);

        var deDireccion = Excepcion(monto: 80_000m, firmas: 2, deDireccion: true);
        (deDireccion.Estado, deDireccion.AutorizadaPorDireccion).Should().Be((EstadoAutorizacionCredito.Autorizada, true));
    }

    [Fact]
    public void Una_excepcion_pendiente_se_puede_cancelar()
    {
        var a = Excepcion();
        a.Cancelar();
        a.Estado.Should().Be(EstadoAutorizacionCredito.Cancelada);
    }

    [Fact]
    public void La_vigencia_maxima_sale_de_configuracion()
    {
        Excepcion(vigencia: TimeSpan.FromHours(48), maxima: TimeSpan.FromHours(72)).VigenteHasta.Should().Be(Ahora.AddHours(48));

        var sobrepasa = () => Excepcion(vigencia: TimeSpan.FromHours(25)); // máxima por omisión: 24 h
        sobrepasa.Should().Throw<BusinessRuleException>().Where(e => e.Code == "AC_VIGENCIA_INVALIDA");

        var recortada = () => Excepcion(vigencia: TimeSpan.FromHours(24), maxima: TimeSpan.FromHours(12));
        recortada.Should().Throw<BusinessRuleException>().Where(e => e.Code == "AC_VIGENCIA_INVALIDA");
    }

    [Fact]
    public void El_monto_autorizado_limita_lo_que_se_libera()
    {
        var a = Excepcion(monto: 60_000m, firmas: 2, deDireccion: true);

        a.ExigirMonto(60_000m, "MXN"); // igual al tope: pasa
        var excede = () => a.ExigirMonto(60_000.01m, "MXN");
        excede.Should().Throw<BusinessRuleException>().Where(e => e.Code == "AC_MONTO_EXCEDE");
        var moneda = () => a.ExigirMonto(100m, "USD");
        moneda.Should().Throw<BusinessRuleException>().Where(e => e.Code == "AC_MONEDA_DISTINTA");
    }

    [Fact]
    public void Supuesto_el_legado_sin_monto_ni_condicion_queda_sin_tope()
    {
        var legado = AutorizacionCredito.Crear(EmpresaId, Credito, Beneficiario, "Cliente", "6023900", Ahora, TimeSpan.FromHours(24));

        legado.ExigirMonto(9_999_999m, "USD");
        legado.ExigirCondicion(CondicionExcepcionCredito.BloqueoPorVencimiento);
        (legado.Estado, legado.FirmasRequeridas).Should().Be((EstadoAutorizacionCredito.Autorizada, (short)1));
    }

    [Theory]
    [InlineData(0, "MXN", (short)1, "AC_MONTO_INVALIDO")]
    [InlineData(100, "PESOS", (short)1, "AC_MONEDA_INVALIDA")]
    [InlineData(100, "MXN", (short)3, "AC_FIRMAS_INVALIDAS")]
    public void Solicitud_invalida_se_rechaza(decimal monto, string moneda, short firmas, string codigo)
    {
        var act = () => AutorizacionCredito.CrearExcepcional(EmpresaId, Credito, Beneficiario, "Motivo", "6023900", Ahora,
            TimeSpan.FromHours(1), TimeSpan.FromHours(24), CondicionExcepcionCredito.SerieNuncaLibera, monto, moneda, firmas, false);
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == codigo);
    }

    [Fact]
    public void Firmas_por_monto_sin_reglas_aplican_M085_y_con_reglas_gana_la_mas_exigente()
    {
        ReglaFirmaExcepcionCredito.FirmasPara([], "MXN", 50_000m).Should().Be(1, "hasta el tope basta Crédito y Cobranza");
        ReglaFirmaExcepcionCredito.FirmasPara([], "MXN", 50_000.01m).Should().Be(2, "arriba del tope firma también Dirección");
        ReglaFirmaExcepcionCredito.FirmasPara([], "USD", 9_999_999m).Should().Be(1, "el tope solo aplica a pesos");

        var reglas = new[]
        {
            ReglaFirmaExcepcionCredito.Crear(EmpresaId, 10_000m, 2),
            ReglaFirmaExcepcionCredito.Crear(EmpresaId, 200_000m, 2),
        };
        ReglaFirmaExcepcionCredito.FirmasPara(reglas, "MXN", 10_000m).Should().Be(1);
        ReglaFirmaExcepcionCredito.FirmasPara(reglas, "MXN", 10_000.01m).Should().Be(2);
    }

    // ---------------------------------------------------------------- handlers

    private sealed class Permisos(bool direccion) : ICurrentUserPermissions
    {
        public ValueTask<bool> TieneAsync(string permiso, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(direccion && permiso == Millet.Identidad.Domain.PermisosCanonicos.CuentasPorCobrarLiberacionOverrideDireccion);
    }

    private sealed class Beneficiarios : Millet.CuentasPorCobrar.Domain.Ports.DatosMaestros.IBeneficiarioCreditoReadPort
    {
        public Task<bool> PuedeDecidirAsync(Guid usuario, Guid empresa, CancellationToken ct) => Task.FromResult(true);
        public Task<IReadOnlyList<Millet.CuentasPorCobrar.Domain.Ports.DatosMaestros.BeneficiarioCreditoDto>> ListarAsync(Guid empresa, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<Millet.CuentasPorCobrar.Domain.Ports.DatosMaestros.BeneficiarioCreditoDto>>([]);
    }

    private sealed class Empresa(Guid id) : ICurrentEmpresaContext
    {
        public Guid? Current => id;
        public bool IsBypassed => false;
        public IDisposable Bypass() => new NoopDisposable();

        private sealed class NoopDisposable : IDisposable
        {
            public void Dispose() { }
        }
    }

    private sealed class Usuario(Guid id) : ICurrentUserContext
    {
        public Guid? UserId => id;
        public string? UserName => "test";
    }

    private sealed class Reloj : IClock
    {
        public DateTimeOffset UtcNow => Ahora;
    }

    private static CuentasPorCobrarDbContext Db() =>
        new(new DbContextOptionsBuilder<CuentasPorCobrarDbContext>().UseInMemoryDatabase($"cxc_m239_{Guid.NewGuid()}").Options,
            new Empresa(EmpresaId));

    private static CrearAutorizacionCreditoHandler Crear(
        CuentasPorCobrarDbContext db, bool direccion = false, ExcepcionCreditoOptions? opciones = null) =>
        new(db, new Empresa(EmpresaId), new Usuario(Credito), new Reloj(), new Beneficiarios(), new Permisos(direccion),
            Options.Create(opciones ?? new ExcepcionCreditoOptions()));

    private static CrearAutorizacionCreditoCommand Comando(decimal monto, int? horas = null, string moneda = "MXN") =>
        new(Beneficiario, "Pago en tránsito", "6023900", CondicionExcepcionCredito.CoberturaParcial, monto, moneda, horas);

    [Fact]
    public async Task Solicitar_arriba_del_umbral_de_M085_queda_pendiente_y_Direccion_la_firma()
    {
        await using var db = Db();

        var res = await Crear(db).Handle(Comando(80_000m), default);

        (res.Estado, res.FirmasRequeridas, res.MontoAutorizado, res.Condicion)
            .Should().Be((EstadoAutorizacionCredito.PendienteSegundaFirma, 2, (decimal?)80_000m, (CondicionExcepcionCredito?)CondicionExcepcionCredito.CoberturaParcial));

        var firmar = new FirmarAutorizacionCreditoHandler(db, new Usuario(Direccion), new Reloj());
        var firmada = await firmar.Handle(new FirmarAutorizacionCreditoCommand(res.Id, res.Version), default);
        (firmada.Estado, firmada.SegundaFirmaPorId, firmada.AutorizadaPorDireccion)
            .Should().Be((EstadoAutorizacionCredito.Autorizada, (Guid?)Direccion, true));
    }

    [Fact]
    public async Task Solicitar_hasta_el_umbral_queda_autorizada_con_una_firma_y_Direccion_que_solicita_queda_completa()
    {
        await using var db = Db();

        (await Crear(db).Handle(Comando(50_000m), default)).Estado.Should().Be(EstadoAutorizacionCredito.Autorizada);

        await using var db2 = Db();
        var deDireccion = await Crear(db2, direccion: true).Handle(Comando(80_000m), default);
        (deDireccion.Estado, deDireccion.AutorizadaPorDireccion).Should().Be((EstadoAutorizacionCredito.Autorizada, true));
    }

    [Fact]
    public async Task La_firma_exige_la_version_vigente_y_otra_persona_que_quien_solicito()
    {
        await using var db = Db();
        var res = await Crear(db).Handle(Comando(80_000m), default);

        var versionVieja = () => new FirmarAutorizacionCreditoHandler(db, new Usuario(Direccion), new Reloj())
            .Handle(new FirmarAutorizacionCreditoCommand(res.Id, res.Version + 5), default);
        await versionVieja.Should().ThrowAsync<ConcurrencyException>();

        var quienSolicito = () => new FirmarAutorizacionCreditoHandler(db, new Usuario(Credito), new Reloj())
            .Handle(new FirmarAutorizacionCreditoCommand(res.Id, res.Version), default);
        await quienSolicito.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "AC_FIRMA_MISMA_PERSONA");
    }

    [Fact]
    public async Task La_vigencia_por_omision_y_la_maxima_salen_de_configuracion()
    {
        await using var db = Db();
        var opciones = new ExcepcionCreditoOptions { VigenciaPorOmisionHoras = 12, VigenciaMaximaHoras = 48 };

        var porOmision = await Crear(db, opciones: opciones).Handle(Comando(1_000m), default);
        porOmision.VigenteHasta.Should().Be(Ahora.AddHours(12));

        var larga = await Crear(db, opciones: opciones).Handle(Comando(1_000m, horas: 48), default);
        larga.VigenteHasta.Should().Be(Ahora.AddHours(48));

        var sobrepasa = () => Crear(db, opciones: opciones).Handle(Comando(1_000m, horas: 49), default);
        await sobrepasa.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "AC_VIGENCIA_INVALIDA");
    }

    [Fact]
    public async Task Las_firmas_salen_de_la_tabla_por_monto_y_sin_reglas_se_aplica_M085()
    {
        await using var db = Db();
        var config = new ConfiguracionExcepcionCreditoHandler(db, new Empresa(EmpresaId));

        var inicial = await config.Handle(new ConfiguracionExcepcionCreditoQuery(), default);
        (inicial.ReglasPorOmision, inicial.Reglas.Single().UmbralMxn, inicial.VigenciaPorOmisionHoras).Should().Be((true, 50_000m, 24));

        await config.Handle(new GuardarReglasFirmaExcepcionCreditoCommand([new(10_000m, 2)]), default);
        (await Crear(db).Handle(Comando(20_000m), default)).FirmasRequeridas.Should().Be(2);
        (await Crear(db).Handle(Comando(9_000m), default)).FirmasRequeridas.Should().Be(1);

        var duplicada = () => config.Handle(new GuardarReglasFirmaExcepcionCreditoCommand([new(1m, 2), new(1m, 1)]), default);
        await duplicada.Should().ThrowAsync<BusinessRuleException>().Where(e => e.Code == "AC_REGLA_DUPLICADA");

        await config.Handle(new GuardarReglasFirmaExcepcionCreditoCommand([]), default);
        (await config.Handle(new ConfiguracionExcepcionCreditoQuery(), default)).ReglasPorOmision.Should().BeTrue();
    }
}
