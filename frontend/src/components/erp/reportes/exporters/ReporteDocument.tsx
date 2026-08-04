import {
  Document,
  Page,
  Text,
  View,
  StyleSheet,
} from '@react-pdf/renderer';

/**
 * Documento React-PDF cross-módulo para reportes operativos (ADR-0036).
 * Lo invoca <c>descargarReportePdf</c> dentro de
 * <c>reporte-pdf-exporter.ts</c>; ese módulo se importa dinámicamente
 * para mantener el bundle inicial chico.
 */
export interface ReporteParaExportar<TFila> {
  titulo: string;
  generadoEn: string;
  filtrosAplicados: Record<string, string | null>;
  columnas: readonly {
    clave: string;
    etiqueta: string;
    tipo: 'texto' | 'numero' | 'moneda' | 'fecha';
    alineacion?: string | null;
    anchoPx?: number | null;
  }[];
  filas: readonly TFila[];
  totales?: Record<string, number> | null;
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
    paddingBottom: 6,
  },
  titulo: {
    fontSize: 14,
    fontFamily: 'Helvetica-Bold',
    marginBottom: 2,
  },
  meta: {
    fontSize: 8,
    color: '#71717a',
  },
  filtros: {
    fontSize: 8,
    color: '#71717a',
    marginTop: 2,
  },
  table: {
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
    flex: 1,
  },
  cellHeader: {
    fontFamily: 'Helvetica-Bold',
  },
  cellTotales: {
    fontFamily: 'Helvetica-Bold',
  },
  cellAlignRight: {
    textAlign: 'right',
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

export function ReporteDocument<TFila>({
  reporte,
  obtenerCelda,
}: {
  reporte: ReporteParaExportar<TFila>;
  obtenerCelda: (row: TFila, clave: string) => unknown;
}) {
  const filtrosTexto = Object.entries(reporte.filtrosAplicados)
    .filter(([, v]) => v != null && v !== '')
    .map(([k, v]) => `${k}: ${v}`)
    .join(' · ');

  return (
    <Document
      title={reporte.titulo}
      creator="Millet ERP"
      producer="Millet ERP"
    >
      <Page size="LETTER" orientation="landscape" style={styles.page}>
        <View style={styles.header}>
          <Text style={styles.titulo}>{reporte.titulo}</Text>
          <Text style={styles.meta}>
            Generado: {new Date(reporte.generadoEn).toLocaleString('es-MX')}
          </Text>
          {filtrosTexto && (
            <Text style={styles.filtros}>Filtros: {filtrosTexto}</Text>
          )}
        </View>

        <View style={styles.table}>
          <View style={styles.rowHeader}>
            {reporte.columnas.map((c) => (
              <Text
                key={c.clave}
                style={[
                  styles.cell,
                  styles.cellHeader,
                  esNumerica(c.tipo) ? styles.cellAlignRight : {},
                ]}
              >
                {c.etiqueta}
              </Text>
            ))}
          </View>

          {reporte.filas.length === 0 ? (
            <View style={styles.row}>
              <Text
                style={[
                  styles.cell,
                  { flex: reporte.columnas.length, textAlign: 'center' },
                ]}
              >
                Sin datos para los filtros aplicados.
              </Text>
            </View>
          ) : (
            reporte.filas.map((fila, i) => (
              <View key={i} style={styles.row} wrap={false}>
                {reporte.columnas.map((c) => (
                  <Text
                    key={c.clave}
                    style={[
                      styles.cell,
                      esNumerica(c.tipo) ? styles.cellAlignRight : {},
                    ]}
                  >
                    {formatearCelda(obtenerCelda(fila, c.clave), c.tipo)}
                  </Text>
                ))}
              </View>
            ))
          )}

          {reporte.totales && Object.keys(reporte.totales).length > 0 && (
            <View style={styles.rowTotales} wrap={false}>
              {reporte.columnas.map((c, idx) => {
                const total = reporte.totales?.[c.clave];
                return (
                  <Text
                    key={c.clave}
                    style={[
                      styles.cell,
                      styles.cellTotales,
                      esNumerica(c.tipo) ? styles.cellAlignRight : {},
                    ]}
                  >
                    {idx === 0 && total == null ? 'Totales' : ''}
                    {total != null ? formatearCelda(total, c.tipo) : ''}
                  </Text>
                );
              })}
            </View>
          )}
        </View>

        <Text
          style={styles.pie}
          render={({ pageNumber, totalPages }) =>
            `Millet ERP · Página ${pageNumber} de ${totalPages}`
          }
          fixed
        />
      </Page>
    </Document>
  );
}

function esNumerica(tipo: string): boolean {
  return tipo === 'numero' || tipo === 'moneda';
}

function formatearCelda(valor: unknown, tipo: string): string {
  if (valor == null) return '—';
  switch (tipo) {
    case 'moneda':
      return typeof valor === 'number'
        ? new Intl.NumberFormat('es-MX', {
            style: 'currency',
            currency: 'MXN',
            minimumFractionDigits: 2,
          }).format(valor)
        : String(valor);
    case 'numero':
      return typeof valor === 'number'
        ? valor.toLocaleString('es-MX', {
            minimumFractionDigits: 0,
            maximumFractionDigits: 4,
          })
        : String(valor);
    case 'fecha':
      return typeof valor === 'string'
        ? new Date(valor).toLocaleDateString('es-MX')
        : String(valor);
    default:
      return String(valor);
  }
}
