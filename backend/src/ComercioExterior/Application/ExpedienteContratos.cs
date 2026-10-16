using MediatR;
using Millet.ComercioExterior.Domain;

namespace Millet.ComercioExterior.Application;
public sealed record CrearExpedienteCommand(TipoExpediente Tipo, Guid? OcId, Guid? SucursalId, string Referencia, Guid? IncotermId, string? Moneda, Guid ResponsableUsuarioId, string[] Clientes, decimal? Tc = null, DateOnly? FechaTc = null, string? FuenteTc = null) : IRequest<ExpedienteDetalle>;
public sealed record EditarExpedienteDatos(string Referencia, Guid IncotermId, string Moneda, Guid ResponsableUsuarioId, decimal? Tc, DateOnly? FechaTc, string? FuenteTc);
public sealed record ParticipanteDatos(string Nombre, string Tipo, string? Contacto, PapelParticipante Papel, Guid? ProveedorId, Guid? ClienteId);
public sealed record DocumentoDatos(Guid TipoDocumentoId, Guid? AdjuntoId, bool NoAplica, string? Motivo);
public sealed record MutarExpedienteCommand(Guid Id, int Version, string Accion, EditarExpedienteDatos? Datos = null, ParticipanteDatos? Participante = null, Guid? ParticipanteId = null, DocumentoDatos? Documento = null, string? Motivo = null) : IRequest<ExpedienteDetalle>;
public sealed record ObtenerExpedienteCeQuery(Guid Id) : IRequest<ExpedienteDetalle>;
public sealed record ListarExpedientesQuery(TipoExpediente? Tipo, EstadoExpediente? Estado, Guid? SucursalId, string? Q, int Offset = 0, int Limit = 10) : IRequest<ExpedientesPagina>;
public sealed record ExpedienteFila(Guid Id, string Folio, TipoExpediente Tipo, string Referencia, Guid SucursalId, string SucursalNombre, EstadoExpediente Estado, bool RevisionPendiente, string Moneda, string ResponsableNombre, int Version);
public sealed record ExpedientesPagina(IReadOnlyList<ExpedienteFila> Items, int Total, int Offset, int Limit);
public sealed record DocumentoVersionDto(Guid Id, Guid TipoDocumentoId, int NumeroVersion, Guid? AdjuntoId, Guid? ReemplazaA, string? Motivo, bool NoAplica, EstadoDocumento Estado, Guid ResponsableUsuarioId, string ResponsableNombre, DateTimeOffset CreatedAt);
public sealed record ParticipanteDto(Guid Id, string Nombre, string Tipo, string? Contacto, PapelParticipante Papel, Guid? ProveedorId, Guid? ClienteId);
public sealed record ImportacionDto(Guid? IncotermId, string? PaisOrigen, string? NumeroContenedor, string? CodigoRuta, string? SemanaEmbarque, string? NumeroPedimento);
public sealed record ExpedienteDetalle(ExpedienteFila Expediente, Guid? OcId, string? OcFolio, ImportacionDto? InformacionImportacion, Guid IncotermId, string IncotermCodigo, Guid ResponsableUsuarioId, decimal? Tc, DateOnly? FechaTc, string? FuenteTc, string? MotivoCancelacion, Guid? RevisadoPorId, IReadOnlyList<ParticipanteDto> Participantes, IReadOnlyList<DocumentoVersionDto> Documentos, IReadOnlyList<RequisitoDocumento> Requisitos, IReadOnlyList<RequisitoDocumento> Faltantes, string EstadoDocumental);
