import { Document, Page, StyleSheet, Text, View } from '@react-pdf/renderer';
import type { ProformaPedidoResponse } from '@/features/facturacion/api/types';
import { formatDateTime } from '@/lib/datetime';

// Tokens DS (mismos que BorradorCfdiDocument): ink, ink-muted, line,
// surface-subtle, brand y warning-note. Helvetica permite generar sin
// descargar fuentes: la proforma se genera sin red.
const styles = StyleSheet.create({
  page: { padding: 32, paddingBottom: 48, backgroundColor: '#FFFFFF', fontFamily: 'Helvetica', fontSize: 9, color: '#161B26' },
  header: { flexDirection: 'row', justifyContent: 'space-between', alignItems: 'flex-start', marginBottom: 14 },
  title: { fontSize: 18, color: '#2748B8' },
  subtitle: { fontSize: 9, color: '#667085', marginTop: 2 },
  folio: { fontSize: 12, fontFamily: 'Courier', textAlign: 'right' },
  meta: { fontSize: 9, color: '#667085', textAlign: 'right', marginTop: 2 },
  watermark: { position: 'absolute', top: 390, left: 35, right: 35, textAlign: 'center', fontSize: 28, color: '#667085', opacity: 0.25, transform: 'rotate(-25deg)' },
  bloques: { flexDirection: 'row', gap: 10, marginBottom: 12 },
  bloque: { flex: 1, borderWidth: 1, borderColor: '#E3E6EC', borderRadius: 4, padding: 8 },
  bloqueTitulo: { fontSize: 8, color: '#667085', marginBottom: 4 },
  dato: { marginBottom: 2 },
  row: { flexDirection: 'row', borderBottomWidth: 1, borderBottomColor: '#E3E6EC', paddingVertical: 5, paddingHorizontal: 4 },
  heading: { backgroundColor: '#FAFBFC', color: '#667085', fontSize: 8 },
  clave: { width: 58, fontFamily: 'Courier', fontSize: 8 },
  unidad: { width: 34, fontFamily: 'Courier', fontSize: 8 },
  descripcion: { flex: 1, paddingRight: 4 },
  num: { width: 62, textAlign: 'right' },
  cant: { width: 42, textAlign: 'right' },
  summary: { marginTop: 14, marginLeft: 280 },
  summaryRow: { flexDirection: 'row', justifyContent: 'space-between', paddingVertical: 3 },
  total: { borderTopWidth: 1, borderTopColor: '#E3E6EC', marginTop: 3, paddingTop: 5, fontSize: 11 },
  avisos: { marginTop: 14, backgroundColor: '#FFF8E8', color: '#5C3D00', padding: 8, borderRadius: 4 },
  footer: { position: 'absolute', bottom: 20, left: 32, right: 32, fontSize: 8, color: '#667085', textAlign: 'center' },
});

const SERIE_PREVISTA: Record<string, string> = {
  Cfdi: 'serie de facturas',
  VentaDirecta: 'serie de venta directa',
};

function fecha(iso: string): string {
  try {
    return formatDateTime(iso);
  } catch {
    return iso;
  }
}

export function ProformaCfdiDocument({ proforma }: { proforma: ProformaPedidoResponse }) {
  const c = proforma.contenido;
  const num = (n: number) =>
    Number.isFinite(n)
      ? n.toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 })
      : '—';
  const money = (n: number) => `${num(n)} ${c.moneda}`;
  const pedido = proforma.numeroPedido ?? 'Sin número';
  const serie = SERIE_PREVISTA[c.serieFiscalPrevista];
  const totales: { etiqueta: string; monto: number; resta?: boolean }[] = [
    { etiqueta: 'Subtotal', monto: c.totales.subtotal },
    { etiqueta: 'Descuento', monto: c.totales.descuento, resta: true },
    { etiqueta: 'IVA trasladado', monto: c.totales.impuestosTrasladados },
    { etiqueta: 'Retenciones', monto: c.totales.retenciones, resta: true },
  ];

  return (
    <Document title={`Proforma ${proforma.folio}`}>
      <Page size="A4" style={styles.page}>
        <Text fixed style={styles.watermark}>PROFORMA – SIN VALOR FISCAL</Text>

        <View style={styles.header}>
          <View>
            <Text style={styles.title}>PROFORMA</Text>
            <Text style={styles.subtitle}>SIN VALOR FISCAL · Pedido {pedido}</Text>
          </View>
          <View>
            <Text style={styles.folio}>{proforma.folio}</Text>
            <Text style={styles.meta}>
              Generada {fecha(proforma.generadaEn)}
              {proforma.generadaPor ? ` por ${proforma.generadaPor}` : ''}
            </Text>
          </View>
        </View>

        <View style={styles.bloques}>
          <View style={styles.bloque}>
            <Text style={styles.bloqueTitulo}>EMISOR</Text>
            <Text style={styles.dato}>{c.emisor.nombre || 'Sin emisor'}</Text>
            <Text style={styles.dato}>RFC: {c.emisor.rfc || '—'}</Text>
            <Text style={styles.dato}>Régimen fiscal: {c.emisor.regimenFiscal || '—'}</Text>
          </View>
          <View style={styles.bloque}>
            <Text style={styles.bloqueTitulo}>RECEPTOR</Text>
            <Text style={styles.dato}>{c.receptor.nombre || 'Sin receptor'}</Text>
            <Text style={styles.dato}>RFC: {c.receptor.rfc || '—'}</Text>
            <Text style={styles.dato}>Régimen fiscal: {c.receptor.regimenFiscal || '—'}</Text>
            <Text style={styles.dato}>Código postal: {c.receptor.codigoPostal || '—'}</Text>
          </View>
          <View style={styles.bloque}>
            <Text style={styles.bloqueTitulo}>COMPROBANTE</Text>
            <Text style={styles.dato}>Uso CFDI: {c.receptor.usoCfdi || '—'}</Text>
            <Text style={styles.dato}>Método de pago: {c.metodoPago || '—'}</Text>
            <Text style={styles.dato}>Forma de pago: {c.formaPago || '—'}</Text>
            <Text style={styles.dato}>
              Moneda: {c.moneda}
              {c.tipoCambio != null ? ` · Tipo de cambio: ${c.tipoCambio}` : ''}
            </Text>
            <Text style={styles.dato}>Lugar de expedición: {c.emisor.lugarExpedicion || '—'}</Text>
            <Text style={styles.dato}>
              Serie y folio fiscal: Se asigna al timbrar{serie ? ` (${serie})` : ''}
            </Text>
          </View>
        </View>

        <View fixed style={[styles.row, styles.heading]}>
          <Text style={styles.clave}>Clave</Text>
          <Text style={styles.unidad}>Unidad</Text>
          <Text style={styles.descripcion}>Partidas</Text>
          <Text style={styles.cant}>Cantidad</Text>
          <Text style={styles.num}>P. unitario</Text>
          <Text style={styles.num}>Descuento</Text>
          <Text style={styles.num}>Importe</Text>
          <Text style={styles.num}>IVA</Text>
        </View>
        {c.conceptos.map((p) => (
          <View key={p.posicion} wrap={false} style={styles.row}>
            <Text style={styles.clave}>{p.claveProdServ || '—'}</Text>
            <Text style={styles.unidad}>{p.claveUnidad || '—'}</Text>
            <Text style={styles.descripcion}>{p.descripcion}</Text>
            <Text style={styles.cant}>{p.cantidad}</Text>
            <Text style={styles.num}>{num(p.valorUnitario)}</Text>
            <Text style={styles.num}>{num(p.descuento)}</Text>
            <Text style={styles.num}>{num(p.importe)}</Text>
            <Text style={styles.num}>{num(p.iva)}</Text>
          </View>
        ))}

        <View wrap={false} style={styles.summary}>
          {totales.map((t) => (
            <View key={t.etiqueta} style={styles.summaryRow}>
              <Text>{t.etiqueta}</Text>
              <Text>
                {t.resta && t.monto !== 0 ? '-' : ''}
                {money(Math.abs(t.monto))}
              </Text>
            </View>
          ))}
          <View style={[styles.summaryRow, styles.total]}>
            <Text>Total</Text>
            <Text>{money(c.totales.total)}</Text>
          </View>
        </View>

        {proforma.avisos.length > 0 && (
          <View wrap={false} style={styles.avisos}>
            <Text style={{ marginBottom: 3 }}>Datos por completar antes de timbrar:</Text>
            {proforma.avisos.map((a, i) => (
              <Text key={i}>· {a}</Text>
            ))}
          </View>
        )}

        <Text fixed style={styles.footer}>
          PROFORMA – SIN VALOR FISCAL · No es un comprobante fiscal digital · Pedido {pedido}
        </Text>
      </Page>
    </Document>
  );
}
