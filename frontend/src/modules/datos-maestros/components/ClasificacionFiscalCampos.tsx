import { Label } from '@/components/ui/label';
import { RegimenFiscalSelector } from '@/components/erp/selectors/RegimenFiscalSelector';
import { useHasPermission } from '@/lib/auth/useHasPermission';
import { PermisosCanonicos } from '@/lib/auth/permission-codes';
import { useRetenciones } from '@/features/cxp/api/useRetenciones';
import { CONCEPTO_SIN_RETENCION, etiquetaConcepto } from '@/features/cxp/lib/retenciones-p8';
import { OrigenProveedor, TipoPersonaProveedor } from '@/modules/datos-maestros/api/types';

export interface ClasificacionFiscalValores {
  origen: OrigenProveedor | null;
  regimenFiscalCodigo: string | null;
  conceptoRetencion: string | null;
}

export interface ErroresClasificacionFiscal {
  origen?: string;
  regimen?: string;
  concepto?: string;
}

interface Props {
  idBase: string;
  valores: ClasificacionFiscalValores;
  onChange: (valores: ClasificacionFiscalValores) => void;
  errores?: ErroresClasificacionFiscal;
  disabled?: boolean;
  /** Si se informa, el selector solo ofrece regímenes que aplican a ese tipo de persona. */
  tipoPersona?: TipoPersonaProveedor;
}

const claseSelect =
  'flex h-ctl-xl max-sm:min-h-touch w-full rounded-md border border-line-control bg-surface-card px-3 text-sm text-ink focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-brand aria-invalid:border-danger disabled:cursor-not-allowed disabled:opacity-50';

/** Campos compartidos de clasificación fiscal del proveedor (ficha y alta). */
export function ClasificacionFiscalCampos({ idBase, valores, onChange, errores = {}, disabled, tipoPersona }: Props) {
  const puedeVerCatalogo = useHasPermission(PermisosCanonicos.CuentasPorPagarRetencionesLeer);
  const catalogo = useRetenciones(puedeVerCatalogo);
  const extranjero = valores.origen === OrigenProveedor.Extranjero;
  const conceptos = [
    ...new Set([
      CONCEPTO_SIN_RETENCION,
      ...(valores.conceptoRetencion ? [valores.conceptoRetencion] : []),
      ...(puedeVerCatalogo ? (catalogo.data?.filter((r) => r.activa).map((r) => r.concepto) ?? []) : []),
    ]),
  ];
  const origenId = `${idBase}-origen`;
  const regimenId = `${idBase}-regimen`;
  const conceptoId = `${idBase}-concepto`;

  return (
    <div className="space-y-4">
      <div className="space-y-1.5">
        <Label htmlFor={origenId}>Origen</Label>
        <select id={origenId} className={claseSelect} disabled={disabled} aria-invalid={!!errores.origen}
          aria-describedby={errores.origen ? `${origenId}-error` : undefined}
          value={valores.origen == null ? '' : String(valores.origen)}
          onChange={(e) => {
            const origen = e.target.value === '' ? null : (Number(e.target.value) as OrigenProveedor);
            onChange({ ...valores, origen, regimenFiscalCodigo: origen === OrigenProveedor.Extranjero ? null : valores.regimenFiscalCodigo });
          }}>
          <option value="">Por revisar</option>
          <option value={OrigenProveedor.Nacional}>Nacional</option>
          <option value={OrigenProveedor.Extranjero}>Extranjero</option>
        </select>
        {errores.origen && <p id={`${origenId}-error`} role="alert" className="text-xs text-danger-fg">{errores.origen}</p>}
      </div>

      <div className="space-y-1.5">
        <p className="text-sm font-medium text-ink">Régimen fiscal</p>
        <div id={regimenId}>
          <RegimenFiscalSelector value={valores.regimenFiscalCodigo} disabled={disabled || extranjero}
            aplicaPersonaFisica={tipoPersona == null ? undefined : tipoPersona === TipoPersonaProveedor.Fisica}
            onChange={(codigo) => onChange({ ...valores, regimenFiscalCodigo: codigo })} />
        </div>
        {extranjero
          ? <p className="text-xs text-ink-muted">Un proveedor extranjero no lleva régimen fiscal del SAT.</p>
          : <p className="text-xs text-ink-muted">Obligatorio para proveedores nacionales. CxP lo compara contra el XML de cada factura.</p>}
        {errores.regimen && <p role="alert" className="text-xs text-danger-fg">{errores.regimen}</p>}
      </div>

      <div className="space-y-1.5">
        <Label htmlFor={conceptoId}>Retención que aplica</Label>
        <select id={conceptoId} className={claseSelect} disabled={disabled} aria-invalid={!!errores.concepto}
          aria-describedby={`${conceptoId}-ayuda`}
          value={valores.conceptoRetencion ?? ''}
          onChange={(e) => onChange({ ...valores, conceptoRetencion: e.target.value === '' ? null : e.target.value })}>
          <option value="">Por revisar</option>
          {conceptos.map((c) => <option key={c} value={c}>{etiquetaConcepto(c)}</option>)}
        </select>
        <p id={`${conceptoId}-ayuda`} className="text-xs text-ink-muted">
          CxP la compara contra el XML de cada factura.
          {!puedeVerCatalogo && ' Tu perfil solo ve «Sin retención» y el valor actual.'}
        </p>
        {errores.concepto && <p role="alert" className="text-xs text-danger-fg">{errores.concepto}</p>}
      </div>
    </div>
  );
}
