using Millet.Compartido.Infrastructure.Notificaciones;
using Millet.Almacen.Infrastructure.Notificaciones;
using Millet.Almacen.Infrastructure.Persistence;
using Millet.Almacen.Domain.Ports;
using Millet.Compras.Infrastructure.Notificaciones;
using Millet.Api.Web;
using Millet.SharedKernel.Application;
using Millet.Identidad.Infrastructure.Notificaciones;
using Millet.SharedKernel.Application.Notificaciones;
using Millet.SharedKernel.Infrastructure.Notificaciones;

namespace Millet.Api.Notificaciones;

public static class NotificacionesRegistration
{
    public static IServiceCollection AddCentroAvisos(this IServiceCollection services)
    {
        services.AddScoped<IRecibosAvisos, RecibosAvisos>();
        services.AddScoped<CentroAvisos>();
        services.AddScoped<PreferenciaSonidoAdapter>();
        services.AddScoped<IFuenteAvisosUsuario, ComprasFuenteAvisos>();
        services.AddScoped<IFuenteAvisosUsuario>(sp => new AlmacenFuenteAvisos(
            sp.GetRequiredService<AlmacenDbContext>(), sp.GetRequiredService<ICurrentUserContext>(),
            sp.GetRequiredService<ICurrentEmpresaContext>(), sp.GetRequiredService<ICurrentUserPermissions>(),
            sp.GetRequiredService<IDestinatariosAvisoReadPort>(), sp.GetRequiredService<IAlmacenSucursalReadPort>(),
            sp.GetRequiredService<DocumentoSucursalScope>().VerificarSucursalAsync, sp.GetRequiredService<IClock>()));
        services.AddScoped<IFuenteAvisosUsuario, IncidenciasFuenteAvisos>();
        return services;
    }
}
