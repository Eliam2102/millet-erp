using Millet.SharedKernel.Application.Exceptions;
using Millet.Tesoreria.Application.Pagos;
using Millet.Tesoreria.Domain.Ports.DatosMaestros;

namespace Millet.Tesoreria.UnitTests.Pagos;

public sealed class ReglasDestinoProveedorTests
{
    private static CuentaProveedorDto Cuenta(string moneda, bool predeterminada, string estado = "Activa") =>
        new(Guid.NewGuid(), "BBVA", "012180000000001234", true, null, moneda, "Prov SA", predeterminada, estado);

    private static ProveedorBancoDto Proveedor(params CuentaProveedorDto[] cuentas) =>
        new(Guid.NewGuid(), "P1", "Prov SA", cuentas);

    [Fact]
    public void Proveedor_bloqueado_se_rechaza_aunque_este_activo_y_tenga_cuenta()
    {
        var bloqueado = Proveedor(Cuenta("MXN", true)) with { Bloqueado = true, MotivoBloqueo = "Dirección revisa su cuenta" };
        var ex = Assert.Throws<BusinessRuleException>(() => ReglasDestinoProveedor.Resolver(bloqueado, "MXN", null));
        Assert.Equal("PROVEEDOR_BLOQUEADO", ex.Code);
        Assert.Contains("Proveedor bloqueado", ex.Message);
    }

    [Fact]
    public void Proveedor_desbloqueado_se_paga_normalmente()
    {
        var predeterminada = Cuenta("MXN", true);
        var desbloqueado = Proveedor(predeterminada) with { Bloqueado = false };
        Assert.Equal(predeterminada, ReglasDestinoProveedor.Resolver(desbloqueado, "MXN", null));
    }

    [Fact]
    public void Varias_cuentas_activas_sin_predeterminada_pide_marcar_una()
    {
        var ex = Assert.Throws<BusinessRuleException>(() =>
            ReglasDestinoProveedor.Resolver(Proveedor(Cuenta("MXN", false), Cuenta("MXN", false)), "MXN", null));
        Assert.Equal("PROVEEDOR_CUENTA_PREDETERMINADA_REQUERIDA", ex.Code);
    }

    [Fact]
    public void Varias_cuentas_activas_con_predeterminada_usa_la_predeterminada()
    {
        var predeterminada = Cuenta("MXN", true);
        Assert.Equal(predeterminada, ReglasDestinoProveedor.Resolver(Proveedor(Cuenta("MXN", false), predeterminada), "MXN", null));
    }

    [Fact]
    public void Sin_cuenta_activa_en_la_moneda_sigue_siendo_sin_cuenta_activa()
    {
        var ex = Assert.Throws<BusinessRuleException>(() =>
            ReglasDestinoProveedor.Resolver(Proveedor(Cuenta("USD", true), Cuenta("MXN", false, "PorValidar")), "MXN", null));
        Assert.Equal("PROVEEDOR_SIN_CUENTA_ACTIVA", ex.Code);
    }
}
