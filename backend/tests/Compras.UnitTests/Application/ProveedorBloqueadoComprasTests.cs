using Millet.Catalogos.Domain;
using Millet.Compras.Application.Proveedores;
using Millet.DatosMaestros.Domain;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compras.UnitTests.Application;

/// <summary>86e3tactf: bloqueo total; el guard que usan los handlers de OC (crear, duplicar, enviar a autorizar, autorizar, cambiar de proveedor).</summary>
public sealed class ProveedorBloqueadoComprasTests
{
    private static Proveedor Activo() =>
        new(Guid.NewGuid(), "P01", "Herrajes DEMO", "HER010101AAA", TipoPersonaProveedor.Moral, EstatusCatalogo.Activo);

    [Fact]
    public void Proveedor_bloqueado_se_rechaza_con_PROVEEDOR_BLOQUEADO_aunque_este_activo()
    {
        var p = Activo();
        p.Bloquear(Guid.NewGuid(), "Dirección revisa su cuenta", DateTimeOffset.UtcNow);

        var act = () => ProveedorNoUtilizable.RechazarSiBloqueado(p);

        act.Should().Throw<BusinessRuleException>().Where(e => e.Code == "PROVEEDOR_BLOQUEADO");
        p.Estatus.Should().Be(EstatusCatalogo.Activo);
    }

    [Fact]
    public void Proveedor_activo_o_ya_desbloqueado_pasa()
    {
        var p = Activo();
        var usuario = Guid.NewGuid();
        ProveedorNoUtilizable.RechazarSiBloqueado(p);

        p.Bloquear(usuario, "Dirección revisa su cuenta", DateTimeOffset.UtcNow);
        p.Desbloquear(usuario, "Ya se aclaró con el proveedor", puedeReemplazar: false);

        ProveedorNoUtilizable.RechazarSiBloqueado(p);
    }
}
