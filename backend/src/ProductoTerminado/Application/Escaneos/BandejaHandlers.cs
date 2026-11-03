using MediatR;
using Microsoft.EntityFrameworkCore;
using Millet.Identidad.Domain;
using Millet.ProductoTerminado.Domain;
using Millet.ProductoTerminado.Infrastructure.Persistence;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;

namespace Millet.ProductoTerminado.Application.Escaneos;

/// <summary>Carga el escaneo y aplica permiso, alcance de sucursal, versión y estado abierto (común a los tres comandos).</summary>
internal static class BandejaGuardas
{
    public static async Task<EscaneoPt> CargarAbiertoAsync(ProductoTerminadoDbContext db, PtAcceso acceso, Guid id, int version, CancellationToken ct)
    {
        var e = await db.Escaneos.FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new EntityNotFoundException("PT_ESCANEO_NO_ENCONTRADO", "No existe el escaneo.");
        await acceso.ExigirPermisoAsync(PermisosCanonicos.ProductoTerminadoExcepcionesGestionar, ct);
        await acceso.VerificarSucursalAsync(e.SucursalId, PermisosCanonicos.ProductoTerminadoExcepcionesGestionarTodasSucursales, ct);
        if (e.Version != version) throw new ConcurrencyException(nameof(EscaneoPt), e.Id);
        if (!e.EstaAbierto) throw new ConflictException("PT_ESCANEO_YA_RESUELTO", "El escaneo ya fue resuelto.");
        return e;
    }

    public static EscaneoDto ADto(EscaneoPt e) => new(e.Id, e.Origen, e.Etiqueta, e.NumeroPedido, e.Posicion, e.NumeroPiezaTexto, e.Estacion, e.Area, e.RegistradoAt, e.SucursalId, e.Resultado.ToString(), e.Motivo, e.Intentos, e.PiezaId, e.ResueltoAt, e.MotivoResolucion, e.Version);
}

public sealed class ReintentarEscaneoHandler(ProductoTerminadoDbContext db, PtAcceso acceso, EscaneoProcesador procesador)
    : IRequestHandler<ReintentarEscaneoCommand, EscaneoDto>
{
    public async Task<EscaneoDto> Handle(ReintentarEscaneoCommand c, CancellationToken cancellationToken)
    {
        var e = await BandejaGuardas.CargarAbiertoAsync(db, acceso, c.EscaneoId, c.Version, cancellationToken);
        if (e.Resultado == ResultadoEscaneo.Reposicion)
            throw new BusinessRuleException("PT_REINTENTO_NO_APLICA", "Una reposición no se reintenta; márquela como revisada o descártela.");
        var antes = e.Intentos;
        // La única vía de alta de piezas es el procesador (sin tope práctico: es un reintento manual).
        await procesador.ProcesarAsync(e, int.MaxValue, cancellationToken);
        if (e.EstaAbierto && e.Intentos == antes) e.Intentar(int.MaxValue);
        await db.SaveChangesAsync(cancellationToken);
        return BandejaGuardas.ADto(e);
    }
}

public sealed class DescartarEscaneoHandler(ProductoTerminadoDbContext db, PtAcceso acceso, IClock clock)
    : IRequestHandler<DescartarEscaneoCommand, EscaneoDto>
{
    public async Task<EscaneoDto> Handle(DescartarEscaneoCommand c, CancellationToken cancellationToken)
    {
        var e = await BandejaGuardas.CargarAbiertoAsync(db, acceso, c.EscaneoId, c.Version, cancellationToken);
        e.Resolver(acceso.Actor, c.Motivo.Trim(), clock.UtcNow);
        await db.SaveChangesAsync(cancellationToken);
        return BandejaGuardas.ADto(e);
    }
}

public sealed class MarcarReposicionRevisadaHandler(ProductoTerminadoDbContext db, PtAcceso acceso, IClock clock)
    : IRequestHandler<MarcarReposicionRevisadaCommand, EscaneoDto>
{
    public async Task<EscaneoDto> Handle(MarcarReposicionRevisadaCommand c, CancellationToken cancellationToken)
    {
        var e = await BandejaGuardas.CargarAbiertoAsync(db, acceso, c.EscaneoId, c.Version, cancellationToken);
        if (e.Resultado != ResultadoEscaneo.Reposicion)
            throw new BusinessRuleException("PT_REVISION_NO_APLICA", "Solo las reposiciones se marcan como revisadas.");
        e.Resolver(acceso.Actor, null, clock.UtcNow);
        await db.SaveChangesAsync(cancellationToken);
        return BandejaGuardas.ADto(e);
    }
}
