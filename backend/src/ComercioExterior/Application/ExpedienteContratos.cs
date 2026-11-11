using MediatR;
using Millet.ComercioExterior.Domain;

namespace Millet.ComercioExterior.Application;
public sealed record CrearExpedienteCommand(TipoExpediente Tipo, Guid? OcId, Guid? SucursalId, string Referencia, Guid? IncotermId, string? Moneda, Guid ResponsableUsuarioId, string[] Clientes, decimal? Tc = null, DateOnly? FechaTc = null, string? FuenteTc = null) : IRequest<ExpedienteDetalle>;
public sealed record EditarExpedienteDatos(string Referencia, Guid IncotermId, string Moneda, Guid ResponsableUsuarioId, decimal? Tc, DateOnly? FechaTc, string? FuenteTc);
public sealed record ParticipanteDatos(string Nombre, TipoParticipante Tipo, string? Contacto, PapelParticipante Papel, Guid? ProveedorId, Guid? ClienteId, string? Descripcion = null);
public sealed record DocumentoDatos(Guid TipoDocumentoId, Guid? AdjuntoId, bool NoAplica, string? Motivo, string? NumeroPedimento = null);
/// <param name="Omitir">M-373: en la corrección, true cambia el hito a omitido (con <paramref name="Motivo"/>).</param>
public sealed record HitoDatos(string Clave, DateOnly? FechaCompromiso, DateOnly? FechaReal, Guid ResponsableUsuarioId, Guid? EvidenciaAdjuntoId = null, string? Motivo = null, string? AduanaEntrada = null, bool Omitir = false);
public sealed record HitoDto(Guid Id, string Clave, string Nombre, int Orden, DateOnly? FechaCompromiso, DateOnly? FechaReal, Guid ResponsableUsuarioId, string ResponsableNombre, Guid? EvidenciaAdjuntoId, bool Omitido, string? Motivo, string? MotivoCambioCompromiso, bool Atrasado, int DiasAtraso, bool ConIncidencia = false, IReadOnlyList<CorreccionHitoDto>? Correcciones = null);
/// <summary>M-373: historial de una corrección: valor anterior → nuevo, usuario, fecha y motivo.</summary>
public sealed record CorreccionHitoDto(Guid Id, DateOnly? FechaRealAnterior, bool OmitidoAnterior, DateOnly? FechaRealNueva, bool OmitidoNuevo, string Motivo, Guid UsuarioId, string UsuarioNombre, DateTimeOffset Fecha);
public sealed record AvisoHitoDto(Guid Id, Guid HitoId, string HitoNombre, DateOnly FechaCompromiso, DateOnly FechaAviso, DateOnly? CerradoEn = null, string? MotivoCierre = null);
public sealed record RequisitoHitoDatos(Guid TipoDocumentoId, string HitoClave);
/// <summary>M-373: marca o desmarca un tipo de documento del expediente como condicional (se pide sólo cuando aplica).</summary>
public sealed record RequisitoCondicionalDatos(Guid TipoDocumentoId, bool Condicional);
public sealed record MutarExpedienteCommand(Guid Id, int Version, string Accion, EditarExpedienteDatos? Datos = null, ParticipanteDatos? Participante = null, Guid? ParticipanteId = null, DocumentoDatos? Documento = null, string? Motivo = null, HitoDatos? Hito = null, RequisitoHitoDatos? RequisitoHito = null, RequisitoCondicionalDatos? RequisitoCondicional = null) : IRequest<ExpedienteDetalle>;
public sealed record ObtenerExpedienteCeQuery(Guid Id) : IRequest<ExpedienteDetalle>;
public sealed record ListarExpedientesQuery(TipoExpediente? Tipo, EstadoExpediente? Estado, Guid? SucursalId, string? Q, int Offset = 0, int Limit = 10, bool? Atraso = null) : IRequest<ExpedientesPagina>;
public sealed record ExpedienteFila(Guid Id, string Folio, TipoExpediente Tipo, string Referencia, Guid SucursalId, string SucursalNombre, EstadoExpediente Estado, bool RevisionPendiente, string Moneda, string ResponsableNombre, int Version, int HitosAtrasados, int Avisos);
public sealed record ExpedientesPagina(IReadOnlyList<ExpedienteFila> Items, int Total, int Offset, int Limit);
public sealed record DocumentoVersionDto(Guid Id, Guid TipoDocumentoId, int NumeroVersion, Guid? AdjuntoId, Guid? ReemplazaA, string? Motivo, bool NoAplica, EstadoDocumento Estado, Guid ResponsableUsuarioId, string ResponsableNombre, DateTimeOffset CreatedAt, string? NumeroPedimento = null);
public sealed record ParticipanteDto(Guid Id, string Nombre, TipoParticipante Tipo, string? Contacto, PapelParticipante Papel, Guid? ProveedorId, Guid? ClienteId, string? Descripcion = null);
public sealed record ImportacionDto(Guid? IncotermId, string? PaisOrigen, string? NumeroContenedor, string? CodigoRuta, string? SemanaEmbarque, string? NumeroPedimento);
public sealed record ExpedienteDetalle(ExpedienteFila Expediente, Guid? OcId, string? OcFolio, ImportacionDto? InformacionImportacion, Guid IncotermId, string IncotermCodigo, Guid ResponsableUsuarioId, decimal? Tc, DateOnly? FechaTc, string? FuenteTc, string? MotivoCancelacion, Guid? RevisadoPorId, IReadOnlyList<ParticipanteDto> Participantes, IReadOnlyList<DocumentoVersionDto> Documentos, IReadOnlyList<RequisitoDocumento> Requisitos, IReadOnlyList<RequisitoDocumento> Faltantes, string EstadoDocumental, string EstadoFisico, string EstadoFinanciero, IReadOnlyList<HitoDto> Hitos, IReadOnlyList<AvisoHitoDto> Avisos, IReadOnlyList<IncidenciaDto> Incidencias, IReadOnlyList<AvisoIncidenciaDto> AvisosIncidencias, IReadOnlyList<FacturaExpedienteDto>? Facturas = null, IReadOnlyList<CierreDimension>? Cierres = null, Guid? CierreAprobadoPorId = null, DateTimeOffset? CierreAprobadoEn = null)
{
    public string? AduanaEntrada { get; init; }
    public IReadOnlyDictionary<string, string> Aduanas { get; init; } = Millet.SharedKernel.Domain.CatalogoAduanas.Aduanas;
}
public sealed record FacturaExpedienteDto(Guid Id, Guid PedidoFacturableId, int VersionEmbarque, string Uuid, string Folio, DateTimeOffset? FechaTimbrado, bool Cancelada, string? UuidOriginal);
