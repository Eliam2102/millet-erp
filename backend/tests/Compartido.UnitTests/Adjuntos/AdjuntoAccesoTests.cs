using Millet.Catalogos.Domain;
using Millet.DatosMaestros.Domain;
using Millet.SharedKernel.Application.Adjuntos;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.Compartido.UnitTests.Adjuntos;

public sealed class AdjuntoAccesoTests
{
    private const string Ver = "datos_maestros.proveedores.adjuntos-ver";
    private const string Subir = "datos_maestros.proveedores.adjuntos-subir";

    private static async Task<Guid> SembrarProveedorAsync(
        AdjuntosEntorno e, EstatusCatalogo estatus = EstatusCatalogo.Activo,
        TipoPersonaProveedor tipo = TipoPersonaProveedor.Moral)
    {
        var p = new Proveedor(Guid.NewGuid(), "P001", "Vidrios SA", "VSA010101AAA", tipo, estatus);
        e.Db.Proveedores.Add(p);
        await e.Db.SaveChangesAsync();
        return p.Id;
    }

    [Fact]
    public async Task Tipo_de_entidad_desconocido_es_404()
    {
        using var e = new AdjuntosEntorno();
        var act = () => e.Acceso.AutorizarAsync("poliza", Guid.NewGuid(), AdjuntoOperacion.Ver, default);
        (await act.Should().ThrowAsync<EntityNotFoundException>())
            .Which.Code.Should().Be("ADJUNTO_TIPO_ENTIDAD_DESCONOCIDO");
    }

    [Fact]
    public async Task Sin_permiso_es_403_y_se_audita_la_denegacion()
    {
        using var e = new AdjuntosEntorno();
        var id = await SembrarProveedorAsync(e);

        var act = () => e.Acceso.AutorizarAsync("proveedor", id, AdjuntoOperacion.Ver, default);

        (await act.Should().ThrowAsync<ForbiddenException>()).Which.Code.Should().Be("ADJUNTO_PERMISO_DENEGADO");
        e.Audit.Registros.Should().ContainSingle(r => r.Operacion == "denegacion");
    }

    [Fact]
    public async Task Permiso_se_evalua_antes_que_la_existencia_del_padre()
    {
        using var e = new AdjuntosEntorno();
        var act = () => e.Acceso.AutorizarAsync("proveedor", Guid.NewGuid(), AdjuntoOperacion.Ver, default);
        await act.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task Proveedor_inexistente_es_404()
    {
        using var e = new AdjuntosEntorno();
        e.Conceder(Ver);
        var act = () => e.Acceso.AutorizarAsync("proveedor", Guid.NewGuid(), AdjuntoOperacion.Ver, default);
        (await act.Should().ThrowAsync<EntityNotFoundException>()).Which.Code.Should().Be("PROVEEDOR_NO_ENCONTRADO");
    }

    [Fact]
    public async Task Proveedor_inactivo_no_admite_subida_pero_si_lectura()
    {
        using var e = new AdjuntosEntorno();
        e.Conceder(Ver, Subir);
        var id = await SembrarProveedorAsync(e, EstatusCatalogo.Inactivo);

        var subir = () => e.Acceso.AutorizarAsync("proveedor", id, AdjuntoOperacion.Subir, default);
        (await subir.Should().ThrowAsync<BusinessRuleException>())
            .Which.Code.Should().Be("ADJUNTO_ENTIDAD_NO_ADMITE_SUBIDA");

        var (_, padre) = await e.Acceso.AutorizarAsync("proveedor", id, AdjuntoOperacion.Ver, default);
        padre.PuedeSubir.Should().BeFalse();
    }

    [Theory]
    [InlineData(TipoPersonaProveedor.Moral, true)]
    [InlineData(TipoPersonaProveedor.Fisica, false)]
    public async Task Resuelve_persona_moral_y_etiqueta(TipoPersonaProveedor tipo, bool moral)
    {
        using var e = new AdjuntosEntorno();
        e.Conceder(Ver);
        var id = await SembrarProveedorAsync(e, tipo: tipo);

        var (_, padre) = await e.Acceso.AutorizarAsync("proveedor", id, AdjuntoOperacion.Ver, default);

        padre.EsPersonaMoral.Should().Be(moral);
        padre.EmpresaId.Should().BeNull();
        padre.Etiqueta.Should().Be("P001 · Vidrios SA");
    }
}
