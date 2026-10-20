import { Document, Page, StyleSheet, Text, View } from '@react-pdf/renderer';
import type { BorradorCfdi } from '@/features/facturacion/components/vista-previa/modelo';

// Tokens DS: ink, ink-muted, line, surface-subtle y brand. Helvetica permite
// generar sin descargar fuentes: la vista previa funciona sin red.
const styles = StyleSheet.create({
  page: { padding: 32, backgroundColor: '#FFFFFF', fontFamily: 'Helvetica', fontSize: 10, color: '#161B26' },
  title: { fontSize: 18, marginBottom: 16, color: '#2748B8' },
  meta: { marginBottom: 8 },
  watermark: { position: 'absolute', top: 390, left: 35, right: 35, textAlign: 'center', fontSize: 24, color: '#667085', opacity: 0.3, transform: 'rotate(-25deg)' },
  row: { flexDirection: 'row', borderBottomWidth: 1, borderBottomColor: '#E3E6EC', padding: 8 },
  heading: { backgroundColor: '#FAFBFC' },
  description: { flex: 1 },
  amount: { width: 110, textAlign: 'right' },
  summary: { marginTop: 20, marginLeft: 220 },
  footer: { position: 'absolute', bottom: 20, left: 32, fontSize: 9, color: '#667085' },
});

export function BorradorCfdiDocument({ datos }: { datos: BorradorCfdi }) {
  const money = (n: number) => `${Number.isFinite(n) ? n.toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 }) : '—'} ${datos.moneda}`;
  return <Document title={`Borrador de ${datos.tipo}`}>
    <Page size="A4" style={styles.page}>
      <Text fixed style={styles.watermark}>BORRADOR · sin validez fiscal</Text>
      <Text style={styles.title}>{datos.tipo} · Vista previa</Text>
      <Text style={styles.meta}>Emisor: {datos.emisor || '[EMISOR]'}</Text>
      <Text style={styles.meta}>Receptor: {datos.receptor}</Text>
      <Text style={styles.meta}>Uso CFDI: {datos.usoCfdi} · Método: {datos.metodoPago} · Forma de pago: {datos.formaPago}</Text>
      <View style={[styles.row, styles.heading]}><Text style={styles.description}>Partidas</Text><Text style={styles.amount}>Cantidad</Text><Text style={styles.amount}>Importe</Text></View>
      {datos.partidas.map((p, i) => <View key={i} wrap={false} style={styles.row}><Text style={styles.description}>{p.descripcion}</Text><Text style={styles.amount}>{p.cantidad}</Text><Text style={styles.amount}>{money(p.importe)}</Text></View>)}
      <View wrap={false} style={styles.summary}>
        {datos.importes.map((r, i) => <View key={i} style={styles.row}><Text style={styles.description}>{r.etiqueta}</Text><Text style={styles.amount}>{money(r.monto)}</Text></View>)}
      </View>
      <Text fixed style={styles.footer}>BORRADOR · sin validez fiscal · Pendiente de emisión</Text>
    </Page>
  </Document>;
}
