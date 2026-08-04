using Millet.Integraciones.Aw.Domain;

namespace Millet.Integraciones.Aw.Application.Commands.MarcarResueltoManual;

public sealed record MarcarResueltoManualResponse(
    Guid Id,
    string QuoteReference,
    EstadoEntidad Estado,
    string ResolutionNote,
    DateTimeOffset ResueltoEn);
