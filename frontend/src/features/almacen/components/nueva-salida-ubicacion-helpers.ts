import type { FilaSalidaValues } from '@/features/almacen/schemas/salida';

/**
 * Almacén-por-línea PR5 — helper de ubicación de cabecera para la salida
 * con RQ (variante A).
 *
 * <p>A diferencia del helper de recepción (PR4, ADR-0047 C7.2b-bis), en
 * salida el bin no se <i>elige</i>: se deriva de DÓNDE HAY EXISTENCIA, y eso
 * varía por artículo. Un helper que aplicara su valor a ciegas asignaría
 * bins sin saldo del artículo y el trigger abortaría la transacción con
 * <c>SALDO_INEXISTENTE</c>. Por eso estas funciones cruzan siempre contra los
 * saldos por artículo: el helper sólo toca las filas que realmente puede
 * surtir, y las que no, se reportan para que el almacenista les elija bin.</p>
 *
 * <p>Puras a propósito — el wiring de queries vive en el sheet; aquí sólo la
 * lógica de cobertura, que es la parte con reglas y la que vale la pena
 * cubrir con tests.</p>
 */

/** Bin con existencia de UN artículo — subconjunto de <c>SaldoUbicacionItem</c>. */
export interface BinConSaldo {
  ubicacionId: string;
  clave: string;
  nombre: string;
  cantidad: number;
}

/** <c>articuloId</c> → bins donde ese artículo tiene existencia. */
export type SaldosPorArticulo = ReadonlyMap<string, readonly BinConSaldo[]>;

/** Opción del selector de cabecera, ya etiquetada con su cobertura. */
export interface OpcionHelper {
  ubicacionId: string;
  clave: string;
  nombre: string;
  /** Cuántos artículos del sheet tienen existencia en este bin. */
  articulosConExistencia: number;
  /** Cuántos artículos distintos hay en el sheet. */
  articulosTotales: number;
  /** Texto listo para render: "RACK-A · 3 de 5 artículos con existencia". */
  etiqueta: string;
}

/**
 * Forma mínima que consumen los helpers. Sirve a las filas de RQ (variante A,
 * con <c>incluida</c> boolean) y a las líneas del vale (variante B, SIN
 * <c>incluida</c> → se tratan como incluidas). Para RQ el comportamiento es
 * idéntico al previo: con <c>incluida</c> boolean, <c>incluida !== false</c>
 * equivale a <c>incluida</c>.
 */
type FilaMinima = {
  articuloId: string;
  ubicacionId?: string | null;
  incluida?: boolean;
};

/**
 * Artículos distintos del sheet, ORDENADOS. El orden estable importa: de
 * esta lista se derivan las claves de <c>useQueries</c>, y un orden que
 * cambiara entre renders remontaría las queries en cada teclazo.
 */
export function articuloIdsUnicos(
  filas: readonly Pick<FilaSalidaValues, 'articuloId'>[],
): string[] {
  return Array.from(
    new Set(filas.map((f) => f.articuloId).filter((id): id is string => !!id)),
  ).sort();
}

/** ¿Este artículo tiene existencia (>0) en este bin? */
function tieneExistencia(
  saldos: SaldosPorArticulo,
  articuloId: string,
  ubicacionId: string,
): boolean {
  return (saldos.get(articuloId) ?? []).some(
    (bin) => bin.ubicacionId === ubicacionId && bin.cantidad > 0,
  );
}

/**
 * Bins candidatos para el helper: la UNIÓN de los bins con existencia de
 * todos los artículos del sheet, cada uno etiquetado con a cuántos cubre.
 * Ordenados por cobertura descendente — el bin que resuelve más líneas va
 * primero, que es lo que el almacenista quiere elegir.
 *
 * <p>Devuelve <c>[]</c> si ningún artículo tiene existencia en ningún bin;
 * el sheet usa eso para deshabilitar el helper con mensaje.</p>
 */
export function opcionesHelper(
  filas: readonly Pick<FilaSalidaValues, 'articuloId'>[],
  saldos: SaldosPorArticulo,
): OpcionHelper[] {
  const articulos = articuloIdsUnicos(filas);
  const porBin = new Map<
    string,
    { clave: string; nombre: string; articulos: Set<string> }
  >();

  for (const articuloId of articulos) {
    for (const bin of saldos.get(articuloId) ?? []) {
      if (bin.cantidad <= 0) continue;
      const existente = porBin.get(bin.ubicacionId);
      if (existente) {
        existente.articulos.add(articuloId);
      } else {
        porBin.set(bin.ubicacionId, {
          clave: bin.clave,
          nombre: bin.nombre,
          articulos: new Set([articuloId]),
        });
      }
    }
  }

  return Array.from(porBin.entries())
    .map(([ubicacionId, datos]) => ({
      ubicacionId,
      clave: datos.clave,
      nombre: datos.nombre,
      articulosConExistencia: datos.articulos.size,
      articulosTotales: articulos.length,
      etiqueta: `${datos.clave} · ${datos.articulos.size} de ${articulos.length} artículo${articulos.length === 1 ? '' : 's'} con existencia`,
    }))
    .sort(
      (a, b) =>
        b.articulosConExistencia - a.articulosConExistencia ||
        a.clave.localeCompare(b.clave),
    );
}

/**
 * Índices de las filas a las que el helper SÍ puede aplicarse: las que aún
 * no tienen bin Y cuyo artículo tiene existencia en el bin del helper.
 *
 * <p>No filtra por <c>incluida</c> a propósito (igual que en recepción): si
 * el almacenista incluye la línea después, ya trae bin y no tiene que
 * volver a elegirlo. Nunca pisa un bin ya capturado.</p>
 */
export function indicesParaAutoAsignarConCobertura(
  filas: readonly FilaMinima[],
  ubicacionHelperId: string | null | undefined,
  saldos: SaldosPorArticulo,
): number[] {
  if (!ubicacionHelperId) return [];
  return filas.reduce<number[]>((acc, fila, index) => {
    if (
      !fila.ubicacionId &&
      tieneExistencia(saldos, fila.articuloId, ubicacionHelperId)
    ) {
      acc.push(index);
    }
    return acc;
  }, []);
}

/**
 * Cuántas líneas INCLUIDAS quedan sin cubrir por el helper — su artículo no
 * tiene existencia en ese bin. Alimenta el aviso "N líneas sin existencia en
 * X"; son las que el almacenista todavía debe resolver a mano.
 */
export function contarLineasSinCobertura(
  filas: readonly FilaMinima[],
  ubicacionHelperId: string | null | undefined,
  saldos: SaldosPorArticulo,
): number {
  if (!ubicacionHelperId) return 0;
  // `incluida !== false`: para RQ (incluida boolean) es idéntico a `incluida`;
  // para el vale (sin incluida → undefined) trata la línea como incluida.
  return filas.filter(
    (fila) =>
      fila.incluida !== false &&
      !tieneExistencia(saldos, fila.articuloId, ubicacionHelperId),
  ).length;
}
