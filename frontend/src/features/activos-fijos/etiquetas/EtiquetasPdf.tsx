import { Document, Page, View, Text, Svg, Rect, StyleSheet } from '@react-pdf/renderer';
import type { Etiqueta } from '../api/conservacion';
import { code128, qrActivo } from './codigos';
import { paginasEtiquetas } from './paginas';
const estilos = StyleSheet.create({
  hoja: { padding: 30, fontFamily: 'Helvetica' },
  fila: { flexDirection: 'row', flexWrap: 'wrap' },
  etiqueta: { height: 140, padding: 8, borderWidth: 0.5, borderColor: '#999999' },
  nombre: { fontSize: 9, marginBottom: 2, maxLines: 2 },
  numero: { fontSize: 11, marginBottom: 3 },
  detalle: { fontSize: 8, marginBottom: 2, maxLines: 2 },
  superior: { flexDirection: 'row', justifyContent: 'space-between', height: 78 },
});
export function EtiquetasPdf({ etiquetas }: { etiquetas: Etiqueta[] }) {
  const { columnas, paginas } = paginasEtiquetas(etiquetas);
  return (
    <Document title="Etiquetas de activos fijos">
      {paginas.map((pagina, i) => (
        <Page key={i} size="LETTER" style={estilos.hoja}>
          <View style={estilos.fila}>
            {pagina.map((e) => {
              const barras = code128(e.numero);
              const qr = qrActivo(e.numero);
              return (
                <View
                  key={e.id}
                  wrap={false}
                  style={[estilos.etiqueta, { width: columnas === 2 ? '50%' : '100%' }]}
                >
                  <View style={estilos.superior}>
                    <View style={{ width: columnas === 2 ? 180 : 440 }}>
                      <Text style={estilos.nombre}>{e.empresa.slice(0, 45)}</Text>
                      <Text style={estilos.detalle}>{e.sucursal.slice(0, 35)}</Text>
                      <Text style={estilos.numero}>{e.numero}</Text>
                      <Text style={estilos.detalle}>{e.descripcion.slice(0, 60)}</Text>
                    </View>
                    <Svg width={58} height={58} viewBox="0 0 41 41">
                      {qr.flatMap((fila, y) =>
                        fila.flatMap((negro, x) =>
                          negro
                            ? [
                                <Rect
                                  key={`${x}-${y}`}
                                  x={x + 4}
                                  y={y + 4}
                                  width={1}
                                  height={1}
                                  fill="black"
                                />,
                              ]
                            : [],
                        ),
                      )}
                    </Svg>
                  </View>
                  <Svg
                    width={columnas === 2 ? 258 : 530}
                    height={34}
                    viewBox={`0 0 ${barras.ancho} 34`}
                  >
                    {barras.barras.map((b) => (
                      <Rect key={b.x} x={b.x} y={0} width={b.ancho} height={34} fill="black" />
                    ))}
                  </Svg>
                </View>
              );
            })}
          </View>
        </Page>
      ))}
    </Document>
  );
}
