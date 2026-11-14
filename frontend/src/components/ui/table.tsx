import { Children, cloneElement, isValidElement, type ComponentProps, type ReactNode } from 'react';
import { cn } from '@/lib/utils';
import { PaginacionTabla } from '@/components/erp/data/PaginacionTabla';
import { usePaginacionLocal } from '@/components/erp/data/usePaginacionLocal';
import { useAltoListado } from '@/components/erp/data/useAltoListado';

interface TableProps extends ComponentProps<'table'> {
  /** Sólo para colecciones de consulta; las partidas editables y reportes agrupados conservan todas sus filas. */
  paginada?: boolean;
}

function Table({ paginada, ...props }: TableProps) {
  return paginada ? <TablePaginada {...props} /> : <TableBase {...props} />;
}

function TableBase({ className, ...props }: ComponentProps<'table'>) {
  return (
    <table
      className={cn(
        'w-full text-sm [&_th]:whitespace-nowrap [&_td]:align-top [&_thead]:sticky [&_thead]:top-0 [&_thead]:bg-surface-subtle [&_tbody>tr]:h-11',
        className,
      )}
      {...props}
    />
  );
}

function TablePaginada({ children, ...props }: ComponentProps<'table'>) {
  const region = useAltoListado();
  const partes = Children.toArray(children);
  const cuerpo = partes.find(
    (n) =>
      isValidElement<{ children: ReactNode }>(n) && (n.type === TableBody || n.type === 'tbody'),
  );
  const filas = isValidElement<{ children: ReactNode }>(cuerpo)
    ? Children.toArray(cuerpo.props.children)
    : [];
  const pagina = usePaginacionLocal(
    filas,
    filas.map((f) => (isValidElement(f) ? f.key : '')).join('|'),
  );
  return (
    <div
      ref={region}
      className="flex h-[var(--listado-alto,auto)] min-h-0 min-w-0 max-w-full flex-col"
    >
      <div
        id={pagina.regionId}
        data-table-viewport
        className="min-h-0 min-w-0 flex-1 overflow-auto"
        tabIndex={0}
        aria-label="Registros de la consulta"
      >
        <TableBase {...props}>
          {partes.map((n) =>
            n === cuerpo && isValidElement<{ children: ReactNode }>(n)
              ? cloneElement(n, { children: pagina.visibles })
              : n,
          )}
        </TableBase>
      </div>
      <PaginacionTabla regionId={pagina.regionId}
        total={filas.length}
        offset={pagina.offset}
        limit={pagina.limit}
        onChange={pagina.onChange}
      />
    </div>
  );
}
interface TableContainerProps extends ComponentProps<'div'> {
  maxHeightClassName?: string;
}
function TableContainer({
  className,
  maxHeightClassName = '', ...props }: TableContainerProps) {
  return (
    <div
      data-table-container
      className={cn('relative min-w-0 max-w-full overflow-auto', maxHeightClassName, className)} {...props} />
  );
}
function TableHeader({ className, ...props }: ComponentProps<'thead'>) {
  return (
    <thead
      className={cn(
        'sticky top-0 z-10 h-9 tracking-[0.04em] border-b border-line-divider bg-surface-subtle text-2xs uppercase text-ink-muted',
        className,
      )}
      {...props}
    />
  );
}
function TableBody(props: ComponentProps<'tbody'>) {
  return <tbody {...props} />;
}
function TableRow({ className, ...props }: ComponentProps<'tr'>) {
  return <tr className={cn('border-b border-line-row', className)} {...props} />;
}
function TableHead({ className, ...props }: ComponentProps<'th'>) {
  return <th className={cn('px-3 py-2 text-left font-semibold', className)} {...props} />;
}
function TableCell({ className, ...props }: ComponentProps<'td'>) {
  return <td className={cn('px-3 py-2', className)} {...props} />;
}
export { Table, TableContainer, TableHeader, TableBody, TableRow, TableHead, TableCell };
