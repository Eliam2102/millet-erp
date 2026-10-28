using MediatR;
using Millet.ActivosFijos.Domain;
namespace Millet.ActivosFijos.Application;
public sealed record CrearActivoCommand(DatosActivo Datos) : IRequest<Activo>;
public sealed record ListarActivosQuery(Guid? ClaseId = null, Guid? SucursalId = null, EstadoActivo? Estado = null, Guid? ResponsableId = null, Guid? EquipoId = null, int Offset = 0, int Limit = 10) : IRequest<ActivosPagina>;
public sealed record ActivosPagina(IReadOnlyList<Activo> Items, int Total);
public sealed record ObtenerActivoQuery(Guid Id) : IRequest<Activo>;
public sealed record AjusteActivoDatos(Guid DocumentoId, Guid FacturaId, DateOnly Fecha, decimal Importe, string Descripcion);
public sealed record MutarActivoCommand(Guid Id, int Version, string Accion, DatosActivo? Datos = null, CustodiaActivo? Destino = null, AjusteActivoDatos? Ajuste = null, string? Motivo = null, DateOnly? FechaEfectiva = null) : IRequest<Activo>;
public sealed record ClaseActivoDatos(string Clave, string Nombre, decimal? TasaFiscalAnual, Guid? CuentaActivoId, Guid? CuentaDepreciacionId, Guid? CuentaGastoId, Guid? CuentaPuenteId, bool Activa, bool Depreciable = true, bool TasaConfirmada = false, decimal? TasaContableAnual = null);
public sealed record GuardarClaseCommand(Guid? Id, int? Version, ClaseActivoDatos Datos) : IRequest<ClaseActivo>;
public sealed record ReferenciaCarga(Guid Id, string Nombre);
public interface ICatalogosActivoReadPort
{
    Task<IReadOnlyList<ReferenciaCarga>> ResolverCargaAsync(string columna, string valor, CancellationToken ct);
    Task<CustodiaDescripcion> DescribirAsync(CustodiaActivo custodia, CancellationToken ct);
    Task<IReadOnlyList<Guid>> ResponsablesAsync(Guid usuario, CancellationToken ct);
    Task ValidarCustodiaAsync(CustodiaActivo custodia, bool completa, CancellationToken ct);
}

public interface ITransaccionPolizaActivo
{
    Task<IAsyncDisposable> VincularAsync(CancellationToken ct);
}
