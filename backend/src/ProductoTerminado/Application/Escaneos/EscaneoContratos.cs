using MediatR;

namespace Millet.ProductoTerminado.Application.Escaneos;

/// <summary>
/// Registra un escaneo de A+W. Solo lo invoca el worker de A+W (sin HTTP ni verificación de permisos);
/// la sucursal se resuelve dentro de PT a partir de <c>ClaveSucursalAw</c>.
/// </summary>
public sealed record RegistrarEscaneoPiezaCommand(Guid EmpresaId, string Origen, string Etiqueta, string NumeroPedido, int? Posicion, int? UPos, string? NumeroPieza, int Estacion, string? Area, DateTime RegistradoAt, string? ClaveSucursalAw) : IRequest<EscaneoResultadoDto>;

public sealed record EscaneoResultadoDto(Guid EscaneoId, string Resultado, string? Motivo, Guid? PiezaId, bool Repetido);

public sealed record EscaneoDto(Guid Id, string Origen, string Etiqueta, string NumeroPedido, int? Posicion, string? NumeroPieza, int Estacion, string? Area, DateTime RegistradoAt, Guid? SucursalId, string Resultado, string? Motivo, int Intentos, Guid? PiezaId, DateTimeOffset? ResueltoAt, string? MotivoResolucion, int Version);
