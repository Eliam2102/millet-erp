using Millet.Compras.Domain;

namespace Millet.Compras.Application.Motivos;

public sealed record MotivoRechazoResponse(
    Guid Id,
    string Clave,
    string Descripcion,
    bool PermiteTextoLibre,
    MotivoRechazoAplicaA AplicaA);
