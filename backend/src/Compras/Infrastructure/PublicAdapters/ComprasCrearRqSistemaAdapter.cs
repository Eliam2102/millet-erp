using Millet.Administracion.Domain;
using Millet.Almacen.Domain.Ports;
using Millet.Compras.Application.Folios;
using Millet.Compras.Domain;
using Millet.SharedKernel.Application;
using Millet.SharedKernel.Application.Exceptions;
using Millet.SharedKernel.Domain;
using ComprasAlmacenReadPort = Millet.Compras.Domain.Ports.Almacen.IAlmacenReadPort;

namespace Millet.Compras.Infrastructure.PublicAdapters;

/// <summary>
/// Adapter productivo de <see cref="IComprasCrearRqSistemaPort"/> (ADR-0047 PR5.C).
/// Crea la Requisición en Borrador de origen sistema para el motor de reorden. Reside
/// en Compras (dueño del agregado; Compras referencia Almacén). Reemplaza el
/// <c>NoOpComprasCrearRqSistemaPort</c>; cableado en <c>Program.cs</c>.
///
/// <para>
/// Arma las 4 fuentes: usuario de servicio + empresa (<see cref="IUsuarioServicioReadPort"/>),
/// depto de sistema (<see cref="DepartamentosSistema.ReabastecimientoAutomatico"/>),
/// precio/UM del maestro (<see cref="IArticuloReadPort"/>), código de sucursal
/// (<see cref="ISucursalReadPort"/>). Reusa <see cref="IFolioSecuenciaService"/>,
/// conserva la validación almacén-pertenece-a-sucursal y <b>omite</b> opera-en-sucursal
/// (RQ de sistema, ADR-0047 PR5.A). La empresa se toma del usuario de servicio
/// (mono-empresa).
/// </para>
/// <para>
/// <b>Bypass de tenancy acotado:</b> la Requisición es <c>IPerteneceAEmpresa</c>; el
/// motor corre sin empresa en contexto → el <c>SaveChanges</c> se envuelve en
/// <c>Bypass()</c> (el <c>EmpresaId</c> ya viene explícito del SP). Las lecturas usan
/// sus propios adapters (que bypassean internamente); el folio es SQL crudo.
/// </para>
/// </summary>
public sealed class ComprasCrearRqSistemaAdapter : IComprasCrearRqSistemaPort
{
    private readonly ComprasDbContext _db;
    private readonly IFolioSecuenciaService _folios;
    private readonly IUsuarioServicioReadPort _usuarioServicio;
    private readonly ISucursalReadPort _sucursales;
    private readonly IArticuloReadPort _articulos;
    private readonly ComprasAlmacenReadPort _almacen;
    private readonly ICurrentEmpresaContext _empresa;

    public ComprasCrearRqSistemaAdapter(
        ComprasDbContext db,
        IFolioSecuenciaService folios,
        IUsuarioServicioReadPort usuarioServicio,
        ISucursalReadPort sucursales,
        IArticuloReadPort articulos,
        ComprasAlmacenReadPort almacen,
        ICurrentEmpresaContext empresa)
    {
        _db = db;
        _folios = folios;
        _usuarioServicio = usuarioServicio;
        _sucursales = sucursales;
        _articulos = articulos;
        _almacen = almacen;
        _empresa = empresa;
    }

    public async Task<Guid> CrearBorradorSistemaAsync(
        CrearRqSistemaSolicitud solicitud, CancellationToken cancellationToken)
    {
        if (solicitud.Lineas.Count == 0)
            throw new BusinessRuleException("REORDEN_RQ_SIN_LINEAS",
                "La RQ de sistema requiere al menos una línea.");

        // 1. Usuario de servicio del reorden → creador/requisitante + empresa.
        var sp = await _usuarioServicio.ObtenerReordenAsync(cancellationToken);
        if (sp is null || !sp.Activo)
            throw new BusinessRuleException("REORDEN_SP_NO_DISPONIBLE",
                "El usuario de servicio del reorden no está disponible (no sembrado o inactivo).");

        // 2. Sucursal → código (para el folio). Empresa NO sale de la sucursal
        //    (mono-empresa: Sucursal no modela EmpresaId); sale del SP.
        var sucursal = await _sucursales.ObtenerAsync(solicitud.SucursalId, cancellationToken);
        if (sucursal is null)
            throw new EntityNotFoundException("REORDEN_SUCURSAL_NO_ENCONTRADA",
                $"No existe sucursal con id '{solicitud.SucursalId}'.");

        // 3. Validación conservada: el almacén pertenece a la sucursal. (opera-en-sucursal se OMITE)
        var almacen = await _almacen.ObtenerAsync(solicitud.AlmacenDestinoId, cancellationToken);
        if (almacen is null)
            throw new EntityNotFoundException("REORDEN_ALMACEN_NO_ENCONTRADO",
                $"No existe almacén con id '{solicitud.AlmacenDestinoId}'.");
        if (almacen.SucursalId != solicitud.SucursalId)
            throw new BusinessRuleException("REORDEN_ALMACEN_NO_PERTENECE_A_SUCURSAL",
                "El almacén destino no pertenece a la sucursal.");

        // 4. Maestro de artículos (batch): precio de referencia + unidad de medida.
        var articuloIds = solicitud.Lineas.Select(l => l.ArticuloId).Distinct().ToList();
        var articulos = await _articulos.ObtenerPorIdsAsync(articuloIds, cancellationToken);

        var empresaId = sp.EmpresaId;
        var folioAnio = (short)DateTimeOffset.UtcNow.Year;
        var folioStr = await _folios.SiguienteFolioRequisicionAsync(
            empresaId, solicitud.SucursalId, sucursal.Clave, folioAnio, cancellationToken);

        // Clasificación/Prioridad por defecto del sistema: OrdenCompra (todo lo que el
        // motor pide genera compra → es el valor neutro más coherente). La RQ nace en
        // Borrador; el humano la ajusta vía EditarCabecera si un caso necesita otra
        // categoría. Prioridad Normal.
        var requisicion = new Requisicion(
            id: Guid.CreateVersion7(),
            empresaId: empresaId,
            folio: Folio.Parse(folioStr),
            folioAnio: folioAnio,
            clasificacion: Clasificacion.OrdenCompra,
            sucursalId: solicitud.SucursalId,
            departamentoId: DepartamentosSistema.ReabastecimientoAutomatico,
            almacenDestinoId: solicitud.AlmacenDestinoId,
            requisitanteId: sp.Id,
            creadorId: sp.Id,
            prioridad: Prioridad.Normal,
            fechaSolicitud: DateTimeOffset.UtcNow,
            origen: OrigenRequisicion.Sistema);

        foreach (var linea in solicitud.Lineas)
        {
            if (!articulos.TryGetValue(linea.ArticuloId, out var art))
                throw new EntityNotFoundException("REORDEN_ARTICULO_NO_ENCONTRADO",
                    $"No existe artículo con id '{linea.ArticuloId}'.");

            var precio = art.PrecioReferenciaMonto ?? 0m;   // 0 si el maestro no tiene precio
            var moneda = art.PrecioReferenciaMoneda ?? "MXN";
            requisicion.AgregarLinea(
                lineaId: Guid.CreateVersion7(),
                articuloId: linea.ArticuloId,
                cantidad: linea.Cantidad,
                unidadMedida: art.UnidadMedida,
                precioEstimado: Money.Of(precio, moneda));
        }

        // Bypass acotado: la RQ es IPerteneceAEmpresa y el motor no tiene empresa en
        // contexto; el EmpresaId ya viene explícito del SP.
        using (_empresa.Bypass())
        {
            _db.Requisiciones.Add(requisicion);
            await _db.SaveChangesAsync(cancellationToken);
        }

        return requisicion.Id;
    }
}
