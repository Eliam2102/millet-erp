namespace Millet.SharedKernel.Domain.Adjuntos;

/// <summary>Una excepción se pide (Pendiente) y otra persona la resuelve (Otorgada o Negada); 86e3yzemu · ADM-proveedor-10.</summary>
public enum EstadoExcepcionAdjunto : short
{
    Pendiente = 0,
    Otorgada = 1,
    Negada = 2,
}
