import { DataTable, Td, Th, Tr } from '@/components/ui/primitives'
import type { AssistantTable as Table } from '@/lib/assistant'

// What the assistant read, laid out as it asked: the figures are the system's, the choice of columns is the assistant's.
export function AssistantTable({ table }: { table: Table }) {
  return (
    <section data-testid="assistant-table" aria-label={table.title} className="space-y-2">
      <h3 className="text-2xs font-medium tracking-[0.08em] text-ink-tertiary uppercase">{table.title}</h3>
      {table.rows.length === 0 ? (
        <p className="text-ink-secondary">Nothing to show.</p>
      ) : (
        <div className="overflow-x-auto rounded-lg border border-line bg-surface">
          <DataTable>
            <thead>
              <tr>
                {table.columns.map((column) => (
                  <Th key={column}>{column}</Th>
                ))}
              </tr>
            </thead>
            <tbody>
              {table.rows.map((row, index) => (
                <Tr key={`${index}-${row[0] ?? ''}`}>
                  {row.map((cell, cellIndex) => (
                    <Td key={`${cellIndex}-${cell}`}>{cell}</Td>
                  ))}
                </Tr>
              ))}
            </tbody>
          </DataTable>
        </div>
      )}
      {table.note ? <p className="text-sm text-ink-secondary">{table.note}</p> : null}
    </section>
  )
}
