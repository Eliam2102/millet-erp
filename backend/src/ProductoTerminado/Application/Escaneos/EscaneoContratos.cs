using MediatR;

namespace Millet.ProductoTerminado.Application.Escaneos;

/// <summary>
/// Registra un escaneo de A+W. Solo lo invoca el worker de A+W (sin HTTP ni verificación de permisos);
/// la sucursal se resuelve dentro de PT a partir de <c>ClaveSucursalAw</c>. <c>MaxIntentos</c> es el tope configurado
/// (PiezasPtOptions.MaxIntentos) y debe ser el mismo que usan los reintentos.
/// </summary>
public sealed record RegistrarEscaneoPiezaCommand(Guid EmpresaId, string Origen, string Etiqueta, string NumeroPedido, int? Posicion, int? UPos, string? NumeroPieza, int Estacion, string? Area, DateTime RegistradoAt, string? ClaveSucursalAw, int MaxIntentos = 5) : IRequest<EscaneoResultadoDto>;

public sealed record EscaneoResultadoDto(Guid EscaneoId, string Resultado, string? Motivo, Guid? PiezaId, bool Repetido);

public sealed record EscaneoDto(Guid Id, string Origen, string Etiqueta, string NumeroPedido, int? Posicion, string? NumeroPieza, int Estacion, string? Area, DateTime RegistradoAt, Guid? SucursalId, string Resultado, string? Motivo, int Intentos, Guid? PiezaId, DateTimeOffset? ResueltoAt, string? MotivoResolucion, int Version);

/// <summary>Reintenta los escaneos Pospuesto de la empresa; los que pasan <c>PospuestoHoras</c> sin resolverse se promueven a Excepcion. Devuelve cuántos dejaron de estar Pospuesto.</summary>
public sealed record ReintentarPospuestosCommand(Guid EmpresaId, int PospuestoHoras, int MaxIntentos, int Lote) : IRequest<int>;

/// <summary>Marca de agua de la ingesta: máximo <c>RegistradoAt</c> del origen dado (null si no hay filas).</summary>
public sealed record ObtenerMarcaAguaEscaneosQuery(Guid EmpresaId, string Origen) : IRequest<DateTime?>;

/// <summary>Reintenta manualmente un escaneo abierto de la bandeja (Pospuesto o Excepcion); Reposicion no aplica. Pasa siempre por <c>EscaneoProcesador</c>.</summary>
public sealed record ReintentarEscaneoCommand(Guid EscaneoId, int Version) : IRequest<EscaneoDto>;

/// <summary>Descarta un escaneo abierto con motivo obligatorio; nunca crea pieza.</summary>
public sealed record DescartarEscaneoCommand(Guid EscaneoId, int Version, string Motivo) : IRequest<EscaneoDto>;

/// <summary>Marca como revisada una reposición (etiqueta ya presente); nunca crea pieza.</summary>
public sealed record MarcarReposicionRevisadaCommand(Guid EscaneoId, int Version) : IRequest<EscaneoDto>;
