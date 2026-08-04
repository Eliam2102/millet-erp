namespace Millet.Integraciones.Fiscal.Domain;

/// <summary>
/// Vendor del PAC. Hoy solo FiscalAPI (ADR-0038). El enum existe para
/// soportar un futuro <c>FailoverPacClient</c> composite con un secundario
/// — la abstracción está lista, no se contrata un secundario en fase 1.
/// </summary>
public enum ProveedorPac : short
{
    FiscalApi = 1,
}
