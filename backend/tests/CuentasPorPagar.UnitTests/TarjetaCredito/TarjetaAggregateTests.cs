using Millet.CuentasPorPagar.Domain.TarjetaCredito;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.CuentasPorPagar.UnitTests.TarjetaCredito;

public sealed class TarjetaAggregateTests
{
    private static Tarjeta Crear(
        decimal limite = 100000m,
        short diaCorte = 15,
        short diaLimitePago = 20,
        DateOnly? vigenciaDesde = null) =>
        Tarjeta.Crear(
            empresaId: Guid.NewGuid(),
            emisora: "Amex",
            perfilParser: "AMEX_MX",
            numero: NumeroTarjetaEnmascarado.FromUltimosCuatro("1234"),
            nombreAlias: "Amex Corporativa",
            titularId: Guid.NewGuid(),
            bancoProveedorId: Guid.NewGuid(),
            limiteCreditoMxn: limite,
            monedaDefault: "MXN",
            diaCorte: diaCorte,
            diaLimitePago: diaLimitePago,
            vigenciaDesde: vigenciaDesde ?? new DateOnly(2026, 1, 1));

    [Fact]
    public void Crear_acepta_parametros_validos()
    {
        var t = Crear();
        t.Estado.Should().Be(EstadoTarjeta.Activa);
        t.Numero.Valor.Should().Be("**** **** **** 1234");
        t.LimiteCreditoMxn.Should().Be(100000m);
    }

    [Fact]
    public void Crear_rechaza_dia_corte_invalido()
    {
        var act = () => Crear(diaCorte: 32);
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "TC_DIA_CORTE_INVALIDO");
    }

    [Fact]
    public void Crear_rechaza_limite_no_positivo()
    {
        var act = () => Crear(limite: 0m);
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "TC_LIMITE_INVALIDO");
    }

    [Fact]
    public void Bloquear_desde_Activa_OK()
    {
        var t = Crear();
        t.Bloquear("Robo de tarjeta", new DateOnly(2026, 5, 15));
        t.Estado.Should().Be(EstadoTarjeta.Bloqueada);
        t.FechaBloqueo.Should().Be(new DateOnly(2026, 5, 15));
        t.MotivoBloqueo.Should().Be("Robo de tarjeta");
    }

    [Fact]
    public void Bloquear_sin_motivo_rechaza()
    {
        var t = Crear();
        var act = () => t.Bloquear("  ", new DateOnly(2026, 5, 15));
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "TC_MOTIVO_BLOQUEO_VACIO");
    }

    [Fact]
    public void Reactivar_solo_desde_Bloqueada()
    {
        var t = Crear();
        var act = () => t.Reactivar();
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "TC_NO_REACTIVABLE");
    }

    [Fact]
    public void Reactivar_desde_Bloqueada_OK()
    {
        var t = Crear();
        t.Bloquear("test", new DateOnly(2026, 5, 15));
        t.Reactivar();
        t.Estado.Should().Be(EstadoTarjeta.Activa);
        t.FechaBloqueo.Should().BeNull();
        t.MotivoBloqueo.Should().BeNull();
    }

    [Fact]
    public void Cancelar_es_terminal()
    {
        var t = Crear();
        t.Cancelar(new DateOnly(2026, 12, 31));
        t.Estado.Should().Be(EstadoTarjeta.Cancelada);

        var act = () => t.Cancelar(new DateOnly(2026, 12, 31));
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "TC_YA_CANCELADA");
    }

    [Fact]
    public void AsegurarPuedeAceptarCargo_bloqueada_acepta_cargo_anterior()
    {
        var t = Crear(vigenciaDesde: new DateOnly(2026, 1, 1));
        t.Bloquear("test", new DateOnly(2026, 5, 15));

        var actAnterior = () => t.AsegurarPuedeAceptarCargo(new DateOnly(2026, 5, 10));
        actAnterior.Should().NotThrow();

        var actPosterior = () => t.AsegurarPuedeAceptarCargo(new DateOnly(2026, 6, 1));
        actPosterior.Should().Throw<BusinessRuleException>().Where(e => e.Code == "TC_BLOQUEADA_FECHA_POSTERIOR");
    }

    [Fact]
    public void AsegurarPuedeAceptarCargo_cancelada_rechaza()
    {
        var t = Crear();
        t.Cancelar(new DateOnly(2026, 12, 31));
        var act = () => t.AsegurarPuedeAceptarCargo(new DateOnly(2026, 5, 1));
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "TC_CANCELADA_SIN_MOVIMIENTOS");
    }

    [Fact]
    public void AsegurarPuedeAceptarCargo_fuera_de_vigencia_rechaza()
    {
        var t = Crear(vigenciaDesde: new DateOnly(2026, 1, 1));
        var act = () => t.AsegurarPuedeAceptarCargo(new DateOnly(2025, 12, 31));
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "TC_FUERA_DE_VIGENCIA");
    }

    [Fact]
    public void AgregarUsuarioAutorizado_OK_y_evita_duplicado_vigente()
    {
        var t = Crear();
        var empleadoId = Guid.NewGuid();

        var usr = t.AgregarUsuarioAutorizado(
            empleadoId, new DateOnly(2026, 1, 1), null, montoMaxMensualMxn: 50000m);
        usr.EmpleadoId.Should().Be(empleadoId);
        t.UsuariosAutorizados.Should().HaveCount(1);

        var actDup = () => t.AgregarUsuarioAutorizado(
            empleadoId, new DateOnly(2026, 6, 1), null, null);
        actDup.Should().Throw<BusinessRuleException>().Where(e => e.Code == "TC_USR_DUPLICADO");
    }

    [Fact]
    public void AgregarUsuarioAutorizado_acepta_segunda_autorizacion_si_anterior_cerrada()
    {
        var t = Crear();
        var empleadoId = Guid.NewGuid();

        var usr1 = t.AgregarUsuarioAutorizado(
            empleadoId, new DateOnly(2026, 1, 1), new DateOnly(2026, 6, 30), 50000m);
        // Cerramos antes de 2026-07-01
        // (la segunda autorización empieza el 2026-07-01)
        var usr2 = t.AgregarUsuarioAutorizado(
            empleadoId, new DateOnly(2026, 7, 1), null, 75000m);

        t.UsuariosAutorizados.Should().HaveCount(2);
    }

    [Fact]
    public void AsegurarUsuarioAutorizado_titular_siempre_pasa()
    {
        var t = Crear();
        var act = () => t.AsegurarUsuarioAutorizado(t.TitularId, new DateOnly(2026, 5, 1));
        act.Should().NotThrow();
    }

    [Fact]
    public void AsegurarUsuarioAutorizado_otro_no_listado_rechaza()
    {
        var t = Crear();
        var act = () => t.AsegurarUsuarioAutorizado(Guid.NewGuid(), new DateOnly(2026, 5, 1));
        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "TC_USR_NO_AUTORIZADO");
    }

    [Fact]
    public void AsegurarUsuarioAutorizado_listado_vigente_pasa()
    {
        var t = Crear();
        var empId = Guid.NewGuid();
        t.AgregarUsuarioAutorizado(empId, new DateOnly(2026, 1, 1), null, null);

        var act = () => t.AsegurarUsuarioAutorizado(empId, new DateOnly(2026, 5, 1));
        act.Should().NotThrow();
    }
}
