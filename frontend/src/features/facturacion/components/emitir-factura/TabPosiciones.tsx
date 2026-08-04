import { useEffect, useState } from 'react';
import { useFieldArray, useWatch, type UseFormReturn } from 'react-hook-form';
import { Link } from '@tanstack/react-router';
import { AlertTriangle, Check, Pencil, Plus, SquarePen, Trash2 } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { OBJETOS_IMP } from '@/features/facturacion/api/types';
import type { EmitirFacturaValues } from '@/features/facturacion/schemas/emitir-factura';
import { ProductoAwSelector } from '@/features/facturacion/components/selectors/ProductoAwSelector';
import { ClaveSatSelector } from '@/components/erp/selectors/ClaveSatSelector';
import { cn } from '@/lib/utils';
import { defaultLinea, numeroONull } from './valores';

/** Redondeo a N decimales para las derivaciones aduaneras (Fase 1c). */
function redondear(n: number, decimales: number): number {
  const f = 10 ** decimales;
  return Math.round(n * f) / f;
}

/**
 * Pestaña "Posiciones" del form de emisión (FAC-UX-PR2): conceptos del
 * CFDI con inline forms (§6.3 patrones-compras — sin modales). Incluye
 * los datos de aduana por línea cuando el comportamiento es exportación
 * con CCE.
 *
 * <para>Detallado pt. 2/3: las líneas del pedido (`desdePedido`) llegan
 * resueltas (pedido + master ProductoAw) y se muestran como filas
 * compactas de solo lectura; una línea con datos fiscales faltantes o
 * errores NO se expande sola — la fila se marca en rojo e informa. Los
 * datos fiscales del artículo (claves SAT, objeto imp., tasas) son de
 * solo lectura cuando la línea tiene producto del catálogo: se corrigen
 * en Datos Maestros → Productos A+W vía el CTA (gated por el permiso
 * `facturacion.facturas.editar-articulo`). "Editar" solo expone los
 * datos comerciales (descripción, cantidad, precio, descuento).</para>
 */
export interface TabPosicionesProps {
  form: UseFormReturn<EmitirFacturaValues>;
  esCce: boolean;
  /** IVA default de la empresa (emisor-defaults, FAC-DET-PR3). */
  tasaIvaDefault?: number | null;
  /** True cuando las líneas vienen prellenadas del pedido facturable. */
  desdePedido: boolean;
  /** Permiso editar-articulo: muestra el CTA al catálogo de productos. */
  puedeCorregirArticulo: boolean;
}

export function TabPosiciones({
  form,
  esCce,
  tasaIvaDefault,
  desdePedido,
  puedeCorregirArticulo,
}: TabPosicionesProps) {
  const { fields, append, remove } = useFieldArray({
    control: form.control,
    name: 'lineas',
  });

  // Ids (RHF field.id) colapsados. Las líneas del pedido arrancan
  // colapsadas; las agregadas a mano nunca entran al set hasta que el
  // usuario las colapsa con "Listo".
  const [colapsadas, setColapsadas] = useState<ReadonlySet<string>>(
    () => new Set(desdePedido ? fields.map((f) => f.id) : []),
  );
  function setColapsada(id: string, colapsar: boolean) {
    setColapsadas((prev) => {
      const s = new Set(prev);
      if (colapsar) s.add(id);
      else s.delete(id);
      return s;
    });
  }

  return (
    <section className="space-y-2">
      <header className="flex items-center justify-between">
        <h3 className="text-sm font-medium">Conceptos ({fields.length})</h3>
        <Button
          type="button"
          variant="ghost"
          size="sm"
          onClick={() => append(defaultLinea(tasaIvaDefault))}
        >
          <Plus className="mr-1 h-3 w-3" />
          Agregar concepto
        </Button>
      </header>

      {desdePedido && (
        <p className="text-xs text-muted-foreground">
          Líneas del pedido — cantidades y precios vienen del pedido y los
          datos fiscales del master del artículo. Editar solo expone lo
          comercial; lo fiscal se corrige en Datos Maestros → Productos A+W.
        </p>
      )}

      <div className="space-y-2">
        {fields.map((field, index) => (
          <ConceptoInline
            key={field.id}
            index={index}
            form={form}
            onRemove={() => remove(index)}
            puedeQuitar={fields.length > 1}
            esCce={esCce}
            tasaIvaDefault={tasaIvaDefault}
            desdePedido={desdePedido}
            puedeCorregirArticulo={puedeCorregirArticulo}
            colapsada={colapsadas.has(field.id)}
            onColapsar={(colapsar) => setColapsada(field.id, colapsar)}
          />
        ))}
      </div>

      {form.formState.errors.lineas?.message && (
        <p className="text-xs text-destructive">
          {form.formState.errors.lineas.message}
        </p>
      )}
    </section>
  );
}

interface ConceptoInlineProps {
  index: number;
  form: UseFormReturn<EmitirFacturaValues>;
  onRemove: () => void;
  puedeQuitar: boolean;
  esCce: boolean;
  tasaIvaDefault?: number | null;
  desdePedido: boolean;
  puedeCorregirArticulo: boolean;
  colapsada: boolean;
  onColapsar: (colapsar: boolean) => void;
}

/** Clave o unidad SAT vacías = artículo incompleto en el catálogo. */
function fiscalIncompleta(linea: EmitirFacturaValues['lineas'][number]): boolean {
  return (
    (linea.claveProdServSat ?? '').trim() === '' ||
    (linea.claveUnidadSat ?? '').trim() === ''
  );
}

function ConceptoInline({
  index,
  form,
  onRemove,
  puedeQuitar,
  esCce,
  tasaIvaDefault,
  desdePedido,
  puedeCorregirArticulo,
  colapsada,
  onColapsar,
}: ConceptoInlineProps) {
  const errs = form.formState.errors.lineas?.[index];
  const linea = useWatch({
    control: form.control,
    name: `lineas.${index}` as const,
  });
  const productoId = linea.productoId;
  // Con producto del catálogo, lo fiscal es de solo lectura (se corrige
  // en el master, no en la factura) — mismo principio que el receptor.
  const fiscalFija = productoId != null;

  // Fase 1c: en CCE, cantidad y valores aduaneros se DERIVAN de la línea y
  // del peso del artículo (heredado en Fase 1b) — cero captura por posición.
  //  - cantidadAduana = peso × cantidad (o = cantidad si el artículo no trae peso)
  //  - valorUnitarioAduana = valor unitario (export en USD)
  //  - valorDolares = valor unitario × cantidad (importe USD)
  // Siguen editables; se recalculan al cambiar cantidad/valor/peso.
  const cantidad = linea.cantidad ?? 0;
  const valorUnitario = linea.valorUnitario ?? 0;
  const pesoUnitario = linea.pesoUnitarioKg;
  useEffect(() => {
    if (!esCce) return;
    const cantAduana =
      pesoUnitario != null && pesoUnitario > 0
        ? pesoUnitario * cantidad
        : cantidad;
    const opts = { shouldDirty: true, shouldValidate: false } as const;
    form.setValue(`lineas.${index}.cantidadAduana` as const, redondear(cantAduana, 6), opts);
    form.setValue(`lineas.${index}.valorUnitarioAduana` as const, redondear(valorUnitario, 6), opts);
    form.setValue(`lineas.${index}.valorDolares` as const, redondear(valorUnitario * cantidad, 2), opts);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [esCce, cantidad, valorUnitario, pesoUnitario, index]);

  if (colapsada) {
    return (
      <ConceptoFilaCompacta
        linea={linea}
        errs={errs}
        puedeCorregirArticulo={puedeCorregirArticulo}
        onEditar={() => onColapsar(false)}
        onRemove={onRemove}
        puedeQuitar={puedeQuitar}
      />
    );
  }
  return (
    <div
      className={cn(
        'space-y-2 rounded-md border p-3',
        desdePedido
          ? 'border-amber-400/60 bg-amber-50/40 dark:bg-amber-950/10'
          : 'border-dashed border-primary/40 bg-primary/5',
        errs && 'border-destructive/50',
      )}
    >
      <div className="grid grid-cols-1 gap-2 sm:grid-cols-12">
        {!desdePedido && (
          <div className="sm:col-span-12">
            <Label className="text-xs">Producto del catálogo (opcional)</Label>
            <ProductoAwSelector
              value={productoId}
              onChange={(item) => {
                const opts = { shouldDirty: true } as const;
                form.setValue(`lineas.${index}.productoId` as const, item?.id ?? null, opts);
                if (item == null) return;
                form.setValue(`lineas.${index}.descripcion` as const, item.descripcion, opts);
                form.setValue(`lineas.${index}.claveProdServSat` as const, item.claveProdServSat ?? '', opts);
                form.setValue(`lineas.${index}.claveUnidadSat` as const, item.claveUnidadSat ?? '', opts);
                form.setValue(`lineas.${index}.objetoImp` as const, item.objetoImp ?? '02', opts);
                // Precedencia FAC-DET-PR3: artículo > IVA default empresa > 0.16.
                form.setValue(`lineas.${index}.tasaIvaTraslado` as const, item.tasaIvaTraslado ?? tasaIvaDefault ?? 0.16, opts);
                form.setValue(`lineas.${index}.tasaRetencionIva` as const, item.tasaRetencionIva, opts);
                form.setValue(`lineas.${index}.tasaRetencionIsr` as const, item.tasaRetencionIsr, opts);
                // Datos de aduana (CCE, Fase 1b): la línea hereda la fracción y
                // la unidad aduanera del artículo. Siguen editables (fallback al
                // picker de #689 si el artículo no las tiene). Cantidad/valores
                // aduaneros se derivan aparte (Fase 1c).
                if (esCce) {
                  const aduanaOpts = { shouldDirty: true, shouldValidate: true } as const;
                  form.setValue(`lineas.${index}.fraccionArancelaria` as const, item.fraccionArancelaria, aduanaOpts);
                  form.setValue(`lineas.${index}.unidadAduana` as const, item.unidadAduana, aduanaOpts);
                  // Peso del artículo → carrier para derivar la cantidad aduanera
                  // (la derivación reactiva la recalcula, Fase 1c).
                  form.setValue(`lineas.${index}.pesoUnitarioKg` as const, item.pesoUnitarioKg, aduanaOpts);
                }
              }}
            />
          </div>
        )}
        {fiscalFija && (
          <div className="sm:col-span-12">
            <ArticuloFiscalResumen
              linea={linea}
              puedeCorregirArticulo={puedeCorregirArticulo}
            />
          </div>
        )}
        <div className={cn(fiscalFija ? 'sm:col-span-7' : 'sm:col-span-5')}>
          <Label className="text-xs">Descripción *</Label>
          <Input {...form.register(`lineas.${index}.descripcion` as const)} />
          {errs?.descripcion && (
            <p className="text-xs text-destructive">{errs.descripcion.message}</p>
          )}
        </div>
        {!fiscalFija && (
          <>
            <div className="sm:col-span-2">
              <Label className="text-xs">Clave SAT *</Label>
              <Input
                maxLength={10}
                {...form.register(`lineas.${index}.claveProdServSat` as const)}
              />
              {errs?.claveProdServSat && (
                <p className="text-xs text-destructive">
                  {errs.claveProdServSat.message}
                </p>
              )}
            </div>
            <div className="sm:col-span-2">
              <Label className="text-xs">Unidad SAT *</Label>
              <Input
                maxLength={10}
                {...form.register(`lineas.${index}.claveUnidadSat` as const)}
              />
              {errs?.claveUnidadSat && (
                <p className="text-xs text-destructive">
                  {errs.claveUnidadSat.message}
                </p>
              )}
            </div>
          </>
        )}
        <div className="sm:col-span-1">
          <Label className="text-xs">Cant. *</Label>
          <Input
            type="number"
            step="0.0001"
            min="0"
            {...form.register(`lineas.${index}.cantidad` as const, {
              valueAsNumber: true,
            })}
          />
        </div>
        <div className="sm:col-span-2">
          <Label className="text-xs">Valor unit. *</Label>
          <Input
            type="number"
            step="0.01"
            min="0"
            {...form.register(`lineas.${index}.valorUnitario` as const, {
              valueAsNumber: true,
            })}
          />
        </div>
        <div className="sm:col-span-2">
          <Label className="text-xs">Descuento</Label>
          <Input
            type="number"
            step="0.01"
            min="0"
            {...form.register(`lineas.${index}.descuento` as const, {
              valueAsNumber: true,
            })}
          />
        </div>
        {!fiscalFija && (
          <>
            <div className="sm:col-span-2">
              <Label className="text-xs">Objeto imp.</Label>
              <select
                className="h-9 w-full rounded-md border border-input bg-transparent px-2 text-sm shadow-sm"
                {...form.register(`lineas.${index}.objetoImp` as const)}
              >
                {OBJETOS_IMP.map((o) => (
                  <option key={o.value} value={o.value}>
                    {o.value}
                  </option>
                ))}
              </select>
            </div>
            <div className="sm:col-span-2">
              <Label className="text-xs">Tasa IVA</Label>
              <Input
                type="number"
                step="0.01"
                min="0"
                max="1"
                placeholder="0.16"
                {...form.register(`lineas.${index}.tasaIvaTraslado` as const, {
                  setValueAs: numeroONull,
                })}
              />
            </div>
            <div className="sm:col-span-2">
              <Label className="text-xs">Ret. IVA</Label>
              <Input
                type="number"
                step="0.01"
                min="0"
                max="1"
                {...form.register(`lineas.${index}.tasaRetencionIva` as const, {
                  setValueAs: numeroONull,
                })}
              />
            </div>
            <div className="sm:col-span-2">
              <Label className="text-xs">Ret. ISR</Label>
              <Input
                type="number"
                step="0.01"
                min="0"
                max="1"
                {...form.register(`lineas.${index}.tasaRetencionIsr` as const, {
                  setValueAs: numeroONull,
                })}
              />
            </div>
          </>
        )}
        <div className="flex items-end gap-3 sm:col-span-4">
          <label className="flex items-center gap-1.5 text-xs">
            <input
              type="checkbox"
              className="size-4 rounded border-input"
              {...form.register(`lineas.${index}.requierePedimento` as const)}
            />
            Requiere pedimento
          </label>
        </div>
        <div className="flex items-end justify-end gap-1 sm:col-span-2">
          <Button
            type="button"
            variant="ghost"
            size="sm"
            onClick={() => onColapsar(true)}
          >
            <Check className="mr-1 h-3 w-3" />
            Listo
          </Button>
          <Button
            type="button"
            variant="ghost"
            size="sm"
            onClick={onRemove}
            disabled={!puedeQuitar}
            className="text-destructive hover:bg-destructive/10"
          >
            <Trash2 className="mr-1 h-3 w-3" />
            Quitar
          </Button>
        </div>
      </div>

      {esCce && (
        <div className="grid grid-cols-1 gap-2 border-t border-primary/20 pt-2 sm:grid-cols-12">
          <div className="sm:col-span-12 text-[11px] font-medium text-muted-foreground">
            Datos de aduana (CCE)
          </div>
          <div className="sm:col-span-3">
            <Label className="text-xs">Fracción arancelaria</Label>
            <ClaveSatSelector
              catalogo="fraccion-arancelaria"
              value={linea.fraccionArancelaria}
              initialLabel={linea.fraccionArancelaria}
              placeholder="Buscar fracción…"
              onChange={(item) =>
                form.setValue(
                  `lineas.${index}.fraccionArancelaria` as const,
                  item?.codigo ?? null,
                  { shouldDirty: true, shouldValidate: true },
                )
              }
            />
            {errs?.fraccionArancelaria && (
              <p className="text-xs text-destructive">{errs.fraccionArancelaria.message}</p>
            )}
          </div>
          <div className="sm:col-span-2">
            <Label className="text-xs">Unidad aduana</Label>
            <ClaveSatSelector
              catalogo="unidad-aduana"
              value={linea.unidadAduana}
              initialLabel={linea.unidadAduana}
              placeholder="Buscar unidad…"
              onChange={(item) =>
                form.setValue(
                  `lineas.${index}.unidadAduana` as const,
                  item?.codigo ?? null,
                  { shouldDirty: true, shouldValidate: true },
                )
              }
            />
            {errs?.unidadAduana && (
              <p className="text-xs text-destructive">{errs.unidadAduana.message}</p>
            )}
          </div>
          <div className="sm:col-span-2">
            <Label className="text-xs">Cant. aduana</Label>
            <Input
              type="number"
              step="0.0001"
              min="0"
              {...form.register(`lineas.${index}.cantidadAduana` as const, { setValueAs: numeroONull })}
            />
            {errs?.cantidadAduana && (
              <p className="text-xs text-destructive">{errs.cantidadAduana.message}</p>
            )}
          </div>
          <div className="sm:col-span-2">
            <Label className="text-xs">Valor unit. aduana</Label>
            <Input
              type="number"
              step="0.01"
              min="0"
              {...form.register(`lineas.${index}.valorUnitarioAduana` as const, { setValueAs: numeroONull })}
            />
            {errs?.valorUnitarioAduana && (
              <p className="text-xs text-destructive">{errs.valorUnitarioAduana.message}</p>
            )}
          </div>
          <div className="sm:col-span-2">
            <Label className="text-xs">Valor USD</Label>
            <Input
              type="number"
              step="0.01"
              min="0"
              {...form.register(`lineas.${index}.valorDolares` as const, { setValueAs: numeroONull })}
            />
            {errs?.valorDolares && (
              <p className="text-xs text-destructive">{errs.valorDolares.message}</p>
            )}
          </div>
          <div className="flex items-end sm:col-span-1">
            <label className="flex items-center gap-1 text-xs">
              <input
                type="checkbox"
                className="size-4 rounded border-input"
                {...form.register(`lineas.${index}.aplicaIva0` as const)}
              />
              IVA 0%
            </label>
          </div>
        </div>
      )}
    </div>
  );
}

/**
 * Datos fiscales del artículo en solo lectura (línea con producto del
 * catálogo): claves SAT, objeto de impuesto y tasas vienen fijos del
 * master ProductoAw; el CTA lleva al catálogo para corregirlos.
 */
function ArticuloFiscalResumen({
  linea,
  puedeCorregirArticulo,
}: {
  linea: EmitirFacturaValues['lineas'][number];
  puedeCorregirArticulo: boolean;
}) {
  const pct = (v: number | null) =>
    v == null ? '—' : `${Math.round(v * 1000) / 10}%`;
  return (
    <div className="flex items-start justify-between gap-3 rounded-md border bg-muted/30 px-3 py-2">
      <dl className="grid flex-1 grid-cols-3 gap-x-4 gap-y-1.5 sm:grid-cols-6">
        <DatoArticulo etiqueta="Clave SAT" valor={linea.claveProdServSat} mono requerido />
        <DatoArticulo etiqueta="Unidad SAT" valor={linea.claveUnidadSat} mono requerido />
        <DatoArticulo etiqueta="Objeto imp." valor={linea.objetoImp} />
        <DatoArticulo etiqueta="IVA" valor={pct(linea.tasaIvaTraslado)} />
        <DatoArticulo etiqueta="Ret. IVA" valor={pct(linea.tasaRetencionIva)} />
        <DatoArticulo etiqueta="Ret. ISR" valor={pct(linea.tasaRetencionIsr)} />
      </dl>
      {puedeCorregirArticulo && linea.productoId != null && (
        <Link
          to="/admin/datos-maestros/productos-aw/$id"
          params={{ id: linea.productoId }}
          className="inline-flex shrink-0 items-center gap-1 text-xs font-medium text-primary hover:underline"
          title="Corregir los datos fiscales en el catálogo de productos A+W"
        >
          <SquarePen className="size-3.5" aria-hidden />
          Corregir en catálogo
        </Link>
      )}
    </div>
  );
}

function DatoArticulo({
  etiqueta,
  valor,
  mono,
  requerido,
}: {
  etiqueta: string;
  valor: string;
  mono?: boolean;
  requerido?: boolean;
}) {
  const vacio = (valor ?? '').trim() === '';
  return (
    <div className="min-w-0">
      <dt className="text-[10px] uppercase tracking-wide text-muted-foreground">
        {etiqueta}
      </dt>
      <dd
        className={cn(
          'truncate text-xs',
          mono && 'font-mono',
          vacio && requerido
            ? 'font-medium text-destructive'
            : 'text-foreground/80',
        )}
      >
        {vacio ? (requerido ? 'Falta en el catálogo' : '—') : valor}
      </dd>
    </div>
  );
}

interface ConceptoFilaCompactaProps {
  linea: EmitirFacturaValues['lineas'][number];
  /** Errores de zod de la línea (solo se usa su presencia). */
  errs: object | undefined;
  puedeCorregirArticulo: boolean;
  onEditar: () => void;
  onRemove: () => void;
  puedeQuitar: boolean;
}

/**
 * Fila compacta de solo lectura de un concepto (líneas del pedido A+W):
 * descripción · clave SAT · cantidad × precio · descuento · IVA ·
 * importe. NUNCA se expande sola: si faltan datos fiscales del artículo
 * o hay errores de validación, la fila se marca en rojo e informa — lo
 * fiscal se corrige en el catálogo (CTA por permiso), lo comercial con
 * "Editar".
 */
function ConceptoFilaCompacta({
  linea,
  errs,
  puedeCorregirArticulo,
  onEditar,
  onRemove,
  puedeQuitar,
}: ConceptoFilaCompactaProps) {
  const cantidad = linea.cantidad ?? 0;
  const valorUnitario = linea.valorUnitario ?? 0;
  const descuento = linea.descuento ?? 0;
  const importe = Math.max(0, cantidad * valorUnitario - descuento);
  const faltaFiscal = fiscalIncompleta(linea);
  const conError = faltaFiscal || errs != null;
  return (
    <div
      className={cn(
        'space-y-1 rounded-md border px-3 py-2',
        conError && 'border-destructive/60 bg-destructive/5',
      )}
    >
      <div className="flex flex-wrap items-center gap-x-4 gap-y-1 text-sm">
        <span
          className="min-w-0 flex-1 basis-48 truncate"
          title={linea.descripcion}
        >
          {linea.descripcion || <span className="text-muted-foreground">(sin descripción)</span>}
        </span>
        <span
          className={cn(
            'font-mono text-xs',
            faltaFiscal ? 'font-medium text-destructive' : 'text-muted-foreground',
          )}
        >
          {linea.claveProdServSat || 'sin clave SAT'}
        </span>
        <span className="text-xs tabular-nums text-muted-foreground">
          {cantidad} × {valorUnitario.toFixed(2)}
        </span>
        {descuento > 0 && (
          <span className="text-xs tabular-nums text-muted-foreground">
            − {descuento.toFixed(2)}
          </span>
        )}
        <span className="text-xs text-muted-foreground">
          IVA {Math.round((linea.tasaIvaTraslado ?? 0) * 100)}%
        </span>
        <span className="font-medium tabular-nums">{importe.toFixed(2)}</span>
        <span className="flex items-center">
          <Button type="button" variant="ghost" size="sm" onClick={onEditar}>
            <Pencil className="mr-1 h-3 w-3" />
            Editar
          </Button>
          <Button
            type="button"
            variant="ghost"
            size="sm"
            onClick={onRemove}
            disabled={!puedeQuitar}
            className="text-destructive hover:bg-destructive/10"
          >
            <Trash2 className="h-3 w-3" />
            <span className="sr-only">Quitar</span>
          </Button>
        </span>
      </div>
      {conError && (
        <p className="flex flex-wrap items-center gap-x-2 gap-y-1 text-xs text-destructive">
          <AlertTriangle className="size-3.5 shrink-0" aria-hidden />
          {faltaFiscal ? (
            <>
              Faltan datos fiscales del artículo (clave/unidad SAT) — no se
              puede emitir.
              {puedeCorregirArticulo && linea.productoId != null && (
                <Link
                  to="/admin/datos-maestros/productos-aw/$id"
                  params={{ id: linea.productoId }}
                  className="inline-flex items-center gap-1 font-medium underline underline-offset-2"
                >
                  <SquarePen className="size-3.5" aria-hidden />
                  Corregir en catálogo
                </Link>
              )}
            </>
          ) : (
            <>La línea tiene errores — revísala con Editar.</>
          )}
        </p>
      )}
    </div>
  );
}
