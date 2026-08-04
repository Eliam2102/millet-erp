import {
  Document,
  Page,
  Text,
  View,
  StyleSheet,
} from '@react-pdf/renderer';
import type { SalidaDetalle } from '@/features/almacen/api/types';
import {
  articuloLabel,
  ccMaquinaLabel,
  destinatarioLabel,
  requisicionLabel,
  rqRegularizadoraLabel,
  subAlmacenLabel,
} from './salida-display';

/**
 * Documento React-PDF del comprobante de salida (variantes A y B).
 * Lazy-loaded vía <c>imprimir-comprobante-salida.tsx</c>.
 *
 * <para>Contiene cabecera con folio + fecha + estado, datos generales
 * (sub-almacén, RQ, persona, máquina, observaciones), tabla de líneas
 * con cantidad/unidad/costo/importe, total y pie de firma.</para>
 */
export interface ComprobanteSalidaData {
  salida: SalidaDetalle;
  subAlmacenLabel?: string;
  destinatarioLabel?: string;
  empresaNombre?: string;
  generadoEn: string; // ISO
}

const styles = StyleSheet.create({
  page: {
    padding: 24,
    fontSize: 9,
    fontFamily: 'Helvetica',
  },
  header: {
    marginBottom: 12,
    borderBottom: '1pt solid #d4d4d8',
    paddingBottom: 8,
  },
  empresa: {
    fontSize: 10,
    fontFamily: 'Helvetica-Bold',
    color: '#52525b',
    marginBottom: 2,
  },
  titulo: {
    fontSize: 14,
    fontFamily: 'Helvetica-Bold',
    marginBottom: 4,
  },
  folio: {
    fontSize: 12,
    fontFamily: 'Helvetica-Bold',
    color: '#1d4ed8',
  },
  meta: {
    fontSize: 8,
    color: '#71717a',
    marginTop: 2,
  },
  seccion: {
    marginTop: 10,
    marginBottom: 6,
  },
  seccionTitulo: {
    fontSize: 10,
    fontFamily: 'Helvetica-Bold',
    marginBottom: 4,
    color: '#3f3f46',
  },
  campoRow: {
    flexDirection: 'row',
    marginBottom: 2,
  },
  campoLabel: {
    width: 110,
    fontFamily: 'Helvetica-Bold',
    color: '#52525b',
  },
  campoValor: {
    flex: 1,
  },
  table: {
    marginTop: 4,
    borderTop: '1pt solid #e4e4e7',
    borderLeft: '1pt solid #e4e4e7',
  },
  row: {
    flexDirection: 'row',
    borderBottom: '0.5pt solid #e4e4e7',
  },
  rowHeader: {
    flexDirection: 'row',
    backgroundColor: '#f4f4f5',
    borderBottom: '1pt solid #d4d4d8',
  },
  rowTotales: {
    flexDirection: 'row',
    backgroundColor: '#f4f4f5',
    borderTop: '1pt solid #d4d4d8',
    borderBottom: '1pt solid #d4d4d8',
  },
  cell: {
    padding: 4,
    borderRight: '0.5pt solid #e4e4e7',
  },
  cellHeader: {
    fontFamily: 'Helvetica-Bold',
  },
  cellTotales: {
    fontFamily: 'Helvetica-Bold',
  },
  cellRight: {
    textAlign: 'right',
  },
  // Anchos relativos (suman 100%). Fase E PR5: Artículo cede 14% al CC-Máquina
  // (38→24) para que la fila de totales siga en 81% sin recalcular su span.
  colPos: { width: '5%' },
  colArt: { width: '24%' },
  colCc: { width: '14%' },
  colUm: { width: '8%' },
  colCant: { width: '13%' },
  colCosto: { width: '17%' },
  colImporte: { width: '19%' },
  observaciones: {
    marginTop: 8,
    padding: 6,
    backgroundColor: '#fafafa',
    border: '0.5pt solid #e4e4e7',
  },
  firmaSeccion: {
    marginTop: 30,
    flexDirection: 'row',
    justifyContent: 'space-around',
  },
  firmaBloque: {
    width: '30%',
    alignItems: 'center',
  },
  firmaLinea: {
    width: '100%',
    borderTop: '0.5pt solid #71717a',
    marginBottom: 4,
  },
  firmaTexto: {
    fontSize: 8,
    color: '#52525b',
  },
  pie: {
    position: 'absolute',
    bottom: 16,
    left: 24,
    right: 24,
    fontSize: 7,
    color: '#a1a1aa',
    textAlign: 'center',
  },
});

export function ComprobanteSalidaDocument({
  data,
}: {
  data: ComprobanteSalidaData;
}) {
  const { salida } = data;
  // Nombres resueltos en backend (ADR-0042); el label opcional sigue como
  // override y el id crudo como último fallback (vía helpers compartidos).
  const subAlmacen = data.subAlmacenLabel ?? subAlmacenLabel(salida);
  const destinatario = data.destinatarioLabel ?? destinatarioLabel(salida);
  const totalMonto = salida.lineas.reduce((a, l) => a + l.montoTotalMxn, 0);

  return (
    <Document
      title={`Comprobante de salida ${salida.folio}`}
      creator="Millet ERP"
      producer="Millet ERP"
    >
      <Page size="LETTER" style={styles.page}>
        <View style={styles.header}>
          {data.empresaNombre && (
            <Text style={styles.empresa}>{data.empresaNombre}</Text>
          )}
          <Text style={styles.titulo}>
            {salida.esPorVale
              ? 'Comprobante de salida por vale urgente'
              : 'Comprobante de salida con requisición'}
          </Text>
          <Text style={styles.folio}>{salida.folio}</Text>
          <Text style={styles.meta}>
            Fecha: {salida.fechaMovimiento} ·
            Estado: {estadoLabel(salida.estado)}
            {salida.registradoAt && (
              <>
                {' · '}
                Registrado:{' '}
                {new Date(salida.registradoAt).toLocaleString('es-MX')}
              </>
            )}
          </Text>
        </View>

        <View style={styles.seccion}>
          <Text style={styles.seccionTitulo}>Datos generales</Text>
          <Campo label="Sub-almacén:" valor={subAlmacen} />
          {salida.rqId && (
            <Campo label="Requisición:" valor={requisicionLabel(salida) ?? salida.rqId} />
          )}
          {salida.rqRegularizadoraId && (
            <Campo
              label="RQ regularizadora:"
              valor={rqRegularizadoraLabel(salida) ?? salida.rqRegularizadoraId}
            />
          )}
          <Campo label="Destinatario:" valor={destinatario} />
        </View>

        <View>
          <Text style={styles.seccionTitulo}>Líneas</Text>
          <View style={styles.table}>
            <View style={styles.rowHeader}>
              <Text style={[styles.cell, styles.cellHeader, styles.colPos]}>#</Text>
              <Text style={[styles.cell, styles.cellHeader, styles.colArt]}>
                Artículo
              </Text>
              <Text style={[styles.cell, styles.cellHeader, styles.colCc]}>
                CC-Máquina
              </Text>
              <Text style={[styles.cell, styles.cellHeader, styles.colUm]}>UM</Text>
              <Text
                style={[
                  styles.cell,
                  styles.cellHeader,
                  styles.cellRight,
                  styles.colCant,
                ]}
              >
                Cantidad
              </Text>
              <Text
                style={[
                  styles.cell,
                  styles.cellHeader,
                  styles.cellRight,
                  styles.colCosto,
                ]}
              >
                Costo unitario
              </Text>
              <Text
                style={[
                  styles.cell,
                  styles.cellHeader,
                  styles.cellRight,
                  styles.colImporte,
                ]}
              >
                Importe
              </Text>
            </View>

            {salida.lineas.length === 0 ? (
              <View style={styles.row}>
                <Text
                  style={[
                    styles.cell,
                    { width: '100%', textAlign: 'center' },
                  ]}
                >
                  Sin líneas.
                </Text>
              </View>
            ) : (
              salida.lineas.map((l) => (
                <View key={l.id} style={styles.row} wrap={false}>
                  <Text style={[styles.cell, styles.colPos]}>
                    {l.posicion}
                  </Text>
                  <Text style={[styles.cell, styles.colArt]}>
                    {articuloLabel(l)}
                  </Text>
                  <Text style={[styles.cell, styles.colCc]}>
                    {ccMaquinaLabel(l)}
                  </Text>
                  <Text style={[styles.cell, styles.colUm]}>
                    {l.unidadMedida}
                  </Text>
                  <Text style={[styles.cell, styles.cellRight, styles.colCant]}>
                    {formatNum(l.cantidad)}
                  </Text>
                  <Text style={[styles.cell, styles.cellRight, styles.colCosto]}>
                    {formatMoney(l.costoUnitarioMxn)}
                  </Text>
                  <Text style={[styles.cell, styles.cellRight, styles.colImporte]}>
                    {formatMoney(l.montoTotalMxn)}
                  </Text>
                </View>
              ))
            )}

            <View style={styles.rowTotales} wrap={false}>
              <Text
                style={[
                  styles.cell,
                  styles.cellTotales,
                  { width: '81%', textAlign: 'right' },
                ]}
              >
                Total MXN
              </Text>
              <Text
                style={[
                  styles.cell,
                  styles.cellTotales,
                  styles.cellRight,
                  styles.colImporte,
                ]}
              >
                {formatMoney(totalMonto)}
              </Text>
            </View>
          </View>
        </View>

        {salida.observaciones && (
          <View style={styles.observaciones}>
            <Text style={[styles.cellHeader, { marginBottom: 2 }]}>
              Observaciones
            </Text>
            <Text>{salida.observaciones}</Text>
          </View>
        )}

        <View style={styles.firmaSeccion}>
          <View style={styles.firmaBloque}>
            <View style={styles.firmaLinea} />
            <Text style={styles.firmaTexto}>Entregó (Almacén)</Text>
          </View>
          <View style={styles.firmaBloque}>
            <View style={styles.firmaLinea} />
            <Text style={styles.firmaTexto}>Recibió</Text>
          </View>
        </View>

        <Text
          style={styles.pie}
          render={({ pageNumber, totalPages }) =>
            `Millet ERP · Generado ${new Date(data.generadoEn).toLocaleString('es-MX')} · Página ${pageNumber} de ${totalPages}`
          }
          fixed
        />
      </Page>
    </Document>
  );
}

function Campo({ label, valor }: { label: string; valor: string }) {
  return (
    <View style={styles.campoRow}>
      <Text style={styles.campoLabel}>{label}</Text>
      <Text style={styles.campoValor}>{valor}</Text>
    </View>
  );
}

function estadoLabel(estado: number): string {
  // Mirror del enum del FE (almacen/api/types.ts).
  switch (estado) {
    case 0: return 'Borrador';
    case 1: return 'Validado';
    case 2: return 'Registrado';
    case 3: return 'Cancelado';
    default: return String(estado);
  }
}

function formatNum(n: number): string {
  return n.toLocaleString('es-MX', {
    minimumFractionDigits: 0,
    maximumFractionDigits: 4,
  });
}

function formatMoney(n: number): string {
  return new Intl.NumberFormat('es-MX', {
    style: 'currency',
    currency: 'MXN',
    minimumFractionDigits: 2,
  }).format(n);
}
