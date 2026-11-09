import type { ReactNode } from "react";

export interface TableColumn<T> {
  key: string;
  header: ReactNode;
  render: (row: T) => ReactNode;
  align?: "start" | "end";
  className?: string;
}

export interface TableProps<T> {
  columns: readonly TableColumn<T>[];
  rows: readonly T[];
  rowKey: (row: T) => string;
  caption: string;
}

export function Table<T>({ columns, rows, rowKey, caption }: TableProps<T>) {
  return (
    <div className="table-wrap">
      <table className="table">
        <caption className="visually-hidden">{caption}</caption>
        <thead>
          <tr>
            {columns.map((column) => (
              <th
                key={column.key}
                scope="col"
                className={column.align === "end" ? "table__align-end" : undefined}
              >
                {column.header}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {rows.map((row) => (
            <tr key={rowKey(row)}>
              {columns.map((column) => {
                const className = [
                  column.className ?? "",
                  column.align === "end" ? "table__align-end" : "",
                ]
                  .filter(Boolean)
                  .join(" ");

                return (
                  <td key={column.key} className={className || undefined}>
                    {column.render(row)}
                  </td>
                );
              })}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
