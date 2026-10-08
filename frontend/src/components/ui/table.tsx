import type { ComponentProps } from 'react';
import { cn } from '@/lib/utils';

function Table({ className, ...props }: ComponentProps<'table'>) {
  return <table className={cn('w-full text-sm', className)} {...props} />;
}
function TableHeader({ className, ...props }: ComponentProps<'thead'>) {
  return (
    <thead
      className={cn(
        'h-9 border-b border-line-divider bg-surface-subtle text-2xs uppercase text-ink-muted',
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
export { Table, TableHeader, TableBody, TableRow, TableHead, TableCell };
