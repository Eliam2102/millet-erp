import { existsSync, readFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { describe, expect, it } from 'vitest';
import {
  Clasificacion,
  EstatusCatalogo,
  HistoricoTipo,
  MotivoRechazoAplicaA,
  Naturaleza,
  NivelAutorizacion,
  Prioridad,
  RolAprobador,
} from '@/features/compras/api/types';
import {
  DescuentoTipo,
  EstadoOrdenCompra,
  SubEstadoFacturacion,
  SubEstadoPago,
  SubEstadoRecepcion,
} from '@/features/compras/ordenes/api/types';
import {
  EstadoRequisicion,
  SituacionSurtido,
} from '@/features/compras/api/types';
import { NivelAutorizacion as NivelAutorizacionOc } from '@/features/compras/ordenes/schemas/autorizar';

/**
 * Guard de contrato de los enums espejo de Compras (Opción A del análisis
 * del bug HistoricoTipo). El frontend replica a mano varios enums del
 * backend como objetos `as const`; este test los compara — por VALOR
 * NUMÉRICO — contra el mismo manifiesto que verifica el guard de backend
 * (`EnumContractManifestTests`), de modo que un drift de cualquiera de los
 * dos lados rompe el build.
 *
 * El manifiesto se genera por reflexión desde el backend (no se edita a
 * mano): `UPDATE_ENUM_CONTRACT=1 dotnet test --filter EnumContractManifestTests`.
 * Ver `docs/api/README.md`.
 *
 * Limitación: solo cubre los enums REGISTRADOS abajo. Un enum espejo nuevo
 * debe registrarse aquí, en el manifiesto y en el guard de backend.
 * ADR-0017 (codegen desde OpenAPI) es el fix de fondo — follow-up.
 */

/**
 * Sube desde el cwd buscando la raíz del repo (el directorio que contiene
 * `docs/api/compras-enums.contract.json`). Robusto sin importar si vitest
 * corre desde `frontend/` o desde la raíz del repo.
 */
function rutaManifiesto(): string {
  let dir = process.cwd();
  for (;;) {
    const candidato = join(dir, 'docs', 'api', 'compras-enums.contract.json');
    if (existsSync(candidato)) return candidato;
    const padre = dirname(dir);
    if (padre === dir) {
      throw new Error(
        `No se encontró docs/api/compras-enums.contract.json subiendo desde ${process.cwd()}.`,
      );
    }
    dir = padre;
  }
}

type Manifiesto = Record<string, Record<string, number>>;

const manifiesto: Manifiesto = JSON.parse(
  readFileSync(rutaManifiesto(), 'utf-8'),
) as Manifiesto;

/**
 * Objetos espejo del frontend, indexados por el mismo nombre que usa el
 * manifiesto (= nombre simple del enum C#). Spread a objeto plano para que
 * `toEqual` compare valores sin el branding readonly del `as const`.
 */
const mirrorsFE: Record<string, Record<string, number>> = {
  HistoricoTipo: { ...HistoricoTipo },
  EstadoRequisicion: { ...EstadoRequisicion },
  SituacionSurtido: { ...SituacionSurtido },
  EstadoOrdenCompra: { ...EstadoOrdenCompra },
  NivelAutorizacion: { ...NivelAutorizacion },
  Clasificacion: { ...Clasificacion },
  Prioridad: { ...Prioridad },
  MotivoRechazoAplicaA: { ...MotivoRechazoAplicaA },
  RolAprobador: { ...RolAprobador },
  SubEstadoRecepcion: { ...SubEstadoRecepcion },
  SubEstadoFacturacion: { ...SubEstadoFacturacion },
  SubEstadoPago: { ...SubEstadoPago },
  DescuentoTipo: { ...DescuentoTipo },
  Naturaleza: { ...Naturaleza },
  EstatusCatalogo: { ...EstatusCatalogo },
};

/**
 * Enums presentes en el manifiesto que TODAVÍA no tienen objeto espejo en
 * el frontend (cubiertos solo del lado backend hasta que se agregue el
 * mirror). Mantener sincronizado al agregar/quitar mirrors.
 */
const SIN_MIRROR_FE = ['ResultadoAutorizacionOc'];

describe('contrato de enums espejo Compras (manifiesto cross-language)', () => {
  it.each(Object.keys(mirrorsFE))(
    'el mirror FE de %s coincide con el manifiesto (valor numérico)',
    (nombre) => {
      expect(manifiesto[nombre]).toBeDefined();
      expect(mirrorsFE[nombre]).toEqual(manifiesto[nombre]);
    },
  );

  it('todo enum del manifiesto tiene mirror FE o está marcado sin-mirror', () => {
    const faltantes = Object.keys(manifiesto).filter(
      (nombre) => !(nombre in mirrorsFE) && !SIN_MIRROR_FE.includes(nombre),
    );
    expect(faltantes).toEqual([]);
  });

  it('ningún mirror FE está ausente del manifiesto', () => {
    const ausentes = Object.keys(mirrorsFE).filter(
      (nombre) => !(nombre in manifiesto),
    );
    expect(ausentes).toEqual([]);
  });

  it('el NivelAutorizacion duplicado (ordenes/schemas/autorizar) también coincide', () => {
    expect({ ...NivelAutorizacionOc }).toEqual(manifiesto.NivelAutorizacion);
  });
});
