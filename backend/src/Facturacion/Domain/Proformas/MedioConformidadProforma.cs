namespace Millet.Facturacion.Domain.Proformas;

/// <summary>M-234: medio por el que el cliente dio su conformidad con la proforma.</summary>
public enum MedioConformidadProforma
{
    Correo = 1,
    Telefono = 2,
    Presencial = 3,
    Mensajeria = 4,
    Otro = 5,
}
