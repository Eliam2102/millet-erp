using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Millet.Catalogos.Domain;
using Millet.Compartido.Infrastructure.Persistence;
using Millet.DatosMaestros.Domain;
using Millet.SharedKernel.Application;

namespace Millet.Api.IntegrationTests.Contabilidad;

/// <summary>C1.4: el validador de dimensiones exige auxiliares que existan; siembra un proveedor y un cliente ficticios.</summary>
internal static class MotorTerceros
{
    public static async Task<(Guid Proveedor, Guid Cliente)> SembrarAsync(WebApplicationFactory<Program> factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CompartidoDbContext>();
        using var bypass = scope.ServiceProvider.GetRequiredService<ICurrentEmpresaContext>().Bypass();
        var n = Guid.NewGuid().ToString("N");
        var p = new Proveedor(Guid.CreateVersion7(), $"C14-{n[..10]}", $"FIX proveedor C14 {n[..6]}", $"CC{n[..10]}".ToUpperInvariant(),
            TipoPersonaProveedor.Moral, EstatusCatalogo.Activo);
        p.ActualizarClasificacionFiscal(OrigenProveedor.Nacional, "601", null);
        var c = new Cliente(Guid.CreateVersion7(), $"C14-{n[..10]}", $"FIX cliente C14 {n[..6]}", rfc: $"CL{n[..10]}".ToUpperInvariant());
        db.Proveedores.Add(p);
        db.Clientes.Add(c);
        await db.SaveChangesAsync();
        return (p.Id, c.Id);
    }
}
