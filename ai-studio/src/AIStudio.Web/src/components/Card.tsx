import type { ReactNode } from "react";

export interface CardProps {
  title?: ReactNode;
  description?: ReactNode;
  actions?: ReactNode;
  flush?: boolean;
  className?: string;
  children: ReactNode;
}

export function Card({
  title,
  description,
  actions,
  flush = false,
  className,
  children,
}: CardProps) {
  const classes = ["card", flush ? "card--flush" : "", className ?? ""]
    .filter(Boolean)
    .join(" ");

  return (
    <section className={classes}>
      {title || description || actions ? (
        <header className="card__header">
          <div className="card__heading">
            {title ? <h2 className="card__title">{title}</h2> : null}
            {description ? <p className="card__description">{description}</p> : null}
          </div>
          {actions ? <div className="card__actions">{actions}</div> : null}
        </header>
      ) : null}
      <div className="card__body">{children}</div>
    </section>
  );
}
