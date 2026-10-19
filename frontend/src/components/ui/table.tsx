import type { ComponentProps } from 'react';
import { cn } from '@/lib/utils';

function Table({ className, ...props }: ComponentProps<'table'>) {
  return <table className={cn('w-full text-sm', className)} {...props} />;
}
interface TableContainerProps extends ComponentProps<'div'> {
  maxHeightClassName?: string;
}
function TableContainer({
  className,
  maxHeightClassName = 'max-h-[calc(100vh-14rem)]',
  ...props
}: TableContainerProps) {
  return <div className={cn('relative overflow-auto', maxHeightClassName, className)} {...props} />;
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
