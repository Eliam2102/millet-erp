using System.Globalization;

namespace Millet.Facturacion.Domain.Ports;

/// <summary>
/// Pedidos de A+W ya facturados en SAP antes del arranque (diagnóstico OTR 10446260, plano 13 §7.1 paso 5 y plano 06 Regla 3).
/// Con <see cref="Habilitado"/>, un pedido cuyo estatus A+W es igual o mayor a <see cref="EstatusDesde"/> es histórico: la ingesta
/// no lo da de alta como facturable y OTR no lo propone para vincular. Apagado por omisión mientras se confirma el tratamiento.
/// </summary>
public sealed class HistoricoSapOptions
{
    public const string SectionName = "IntegracionesAw:HistoricoSap";
    public bool Habilitado { get; set; }
    /// <summary>Estatus A+W desde el que el pedido se considera facturado fuera del ERP (115 = facturado y entregado, TI 5-nov).</summary>
    public int EstatusDesde { get; set; } = 115;

    public bool EsHistorico(string? estadoOrigen) =>
        int.TryParse(estadoOrigen, NumberStyles.Integer, CultureInfo.InvariantCulture, out var estatus) && EsHistorico(estatus);

    public bool EsHistorico(int estatus) => Habilitado && estatus >= EstatusDesde;
}
