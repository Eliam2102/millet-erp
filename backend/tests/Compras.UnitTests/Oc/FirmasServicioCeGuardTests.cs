using Millet.Compras.Application.Oc;
using Millet.Compras.Domain;
using Millet.Compras.Domain.Oc;
using Millet.Identidad.Domain;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
namespace Millet.Compras.UnitTests.Oc;

public sealed class FirmasServicioCeGuardTests
{
    [Theory]
    [InlineData(NivelAutorizacion.Nivel1)]
    [InlineData(NivelAutorizacion.Nivel2)]
    public async Task Firma_CE_exige_el_permiso_del_nivel_solicitado(NivelAutorizacion nivel)
    {
        var oc = Nueva(true);
        await Assert.ThrowsAsync<ForbiddenException>(() => FirmasServicioCeGuard.ExigirAsync(oc, nivel, null, default));
        var correcto = nivel == NivelAutorizacion.Nivel1 ? PermisosCanonicos.ComercioExteriorServiciosAutorizarNivel1 : PermisosCanonicos.ComercioExteriorServiciosAutorizarNivel2;
        var incorrecto = nivel == NivelAutorizacion.Nivel1 ? PermisosCanonicos.ComercioExteriorServiciosAutorizarNivel2 : PermisosCanonicos.ComercioExteriorServiciosAutorizarNivel1;
        await Assert.ThrowsAsync<ForbiddenException>(() => FirmasServicioCeGuard.ExigirAsync(oc, nivel, new Permiso(incorrecto), default));
        await FirmasServicioCeGuard.ExigirAsync(oc, nivel, new Permiso(correcto), default);
    }

    [Fact]
    public async Task Compra_ordinaria_conserva_su_autorizacion_existente() =>
        await FirmasServicioCeGuard.ExigirAsync(Nueva(false), NivelAutorizacion.Nivel1, null, default);

    private static OrdenCompra Nueva(bool ce) => new(Guid.NewGuid(), Guid.NewGuid(), Millet.Compras.Domain.Oc.Folio.Parse("OC-MID2026-000099"), 2026,
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new(2026, 11, 11), esServicioComercioExterior: ce);
    private sealed class Permiso(string codigo) : ICurrentUserPermissions
    {
        public ValueTask<bool> TieneAsync(string permiso, CancellationToken ct = default) => ValueTask.FromResult(permiso == codigo);
    }
}
