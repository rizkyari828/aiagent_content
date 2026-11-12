import {
  useCallback,
  useEffect,
  useRef,
  useState,
  type FormEvent,
} from "react";
import { isApiRequestError, type ApiError } from "../../api/client";
import {
  createCostEntry,
  createMetricSnapshot,
  createTimeEntry,
  getTrackingSummary,
  listCostEntries,
  listMetricSnapshots,
  listTimeEntries,
  type CostEntryKind,
  type CostEntryResponse,
  type CurrencyCostSummary,
  type MetricSnapshotResponse,
  type TimeEntryKind,
  type TimeEntryResponse,
  type TrackingSummaryResponse,
} from "../../api/tracking";
import { Badge, type BadgeTone } from "../../components/Badge";
import { Button } from "../../components/Button";
import { Card } from "../../components/Card";
import { Dialog } from "../../components/Dialog";
import { InlineError } from "../../components/InlineError";
import { Skeleton } from "../../components/Skeleton";
import { Table, type TableColumn } from "../../components/Table";
import { useToast } from "../../components/Toast";
import { useFormatters } from "../../i18n/format";
import { useI18n, type Translate } from "../../i18n/i18n";

export interface MetricsSectionProps {
  projectId: string;
  onProjectChanged: () => void;
}

type LoadState = "loading" | "ready" | "error";

interface SnapshotFormState {
  views: string;
  impressions: string;
  clickThroughRate: string;
  averageViewDurationSeconds: string;
  watchTimeMinutes: string;
  subscribersGained: string;
  capturedAt: string;
  source: string;
  notes: string;
}

interface SnapshotFormErrors {
  views?: string;
  impressions?: string;
  clickThroughRate?: string;
  averageViewDurationSeconds?: string;
  watchTimeMinutes?: string;
  subscribersGained?: string;
  capturedAt?: string;
}

interface TimeEntryFormState {
  kind: TimeEntryKind;
  category: string;
  minutes: string;
  recordedAt: string;
  notes: string;
}

interface TimeEntryFormErrors {
  category?: string;
  minutes?: string;
  recordedAt?: string;
}

interface CostEntryFormState {
  amount: string;
  currency: string;
  kind: CostEntryKind;
  category: string;
  incurredAt: string;
  notes: string;
}

interface CostEntryFormErrors {
  amount?: string;
  currency?: string;
  category?: string;
  incurredAt?: string;
}

function toApiError(error: unknown): ApiError {
  if (isApiRequestError(error)) {
    return {
      code: error.code,
      title: error.title,
      detail: error.detail,
      status: error.status,
      fieldErrors: error.fieldErrors,
    };
  }

  return {
    code: "unexpected_error",
    title: "Unexpected error.",
    detail: error instanceof Error ? error.message : undefined,
    status: 0,
  };
}

function describeLoadError(error: ApiError, t: Translate): string {
  if (error.code === "network_error") {
    return t("project.error.network");
  }

  if (error.code === "request_timeout") {
    return t("project.error.timeout");
  }

  if (error.code === "content_project_not_found") {
    return t("project.error.notFound");
  }

  return t("project.error.http");
}

function errorCode(error: ApiError): string | undefined {
  if (error.code) {
    return error.code;
  }

  return error.status > 0 ? `HTTP ${error.status}` : undefined;
}

function toLocalDatetimeString(date: Date = new Date()): string {
  const pad = (n: number) => String(n).padStart(2, "0");
  const year = date.getFullYear();
  const month = pad(date.getMonth() + 1);
  const day = pad(date.getDate());
  const hours = pad(date.getHours());
  const minutes = pad(date.getMinutes());
  return `${year}-${month}-${day}T${hours}:${minutes}`;
}

function toIsoTimestamp(localValue: string): string {
  const date = new Date(localValue);
  if (Number.isNaN(date.getTime())) {
    return new Date().toISOString();
  }
  return date.toISOString();
}

function costKindTone(kind: CostEntryKind): BadgeTone {
  switch (kind) {
    case "actual":
      return "neutral";
    case "estimated":
      return "accent";
    case "refunded":
      return "warning";
    default:
      return "neutral";
  }
}

/**
 * Null-safe number formatter for metrics.
 * NEVER coerces null/undefined to 0. When value is 0, renders "0".
 */
function renderMetricNumber(
  value: number | null | undefined,
  formatNumber: (n: number) => string,
  emptyText: string,
): string {
  if (value === null || value === undefined) {
    return emptyText;
  }
  return formatNumber(value);
}

function renderMetricPercent(
  value: number | null | undefined,
  formatNumber: (n: number) => string,
  t: Translate,
): string {
  if (value === null || value === undefined) {
    return t("project.metrics.notRecorded");
  }
  return t("project.metrics.unit.percent", { value: formatNumber(value) });
}

function renderMetricSeconds(
  value: number | null | undefined,
  formatNumber: (n: number) => string,
  t: Translate,
): string {
  if (value === null || value === undefined) {
    return t("project.metrics.notRecorded");
  }
  return t("project.metrics.unit.seconds", { value: formatNumber(value) });
}

function parseNullableNumberInput(str: string): number | null {
  const trimmed = str.trim();
  if (trimmed === "") {
    return null;
  }
  const parsed = Number(trimmed);
  return Number.isNaN(parsed) ? null : parsed;
}

function isValidOptionalNumber(input: string, allowNegative = false): boolean {
  const trimmed = input.trim();
  if (trimmed === "") {
    return true;
  }
  const num = Number(trimmed);
  if (Number.isNaN(num)) {
    return false;
  }
  if (!allowNegative && num < 0) {
    return false;
  }
  return true;
}

export function MetricsSection({
  projectId,
  onProjectChanged,
}: MetricsSectionProps) {
  const { t } = useI18n();
  const { dateTime, number } = useFormatters();
  const { showToast } = useToast();

  const [summary, setSummary] = useState<TrackingSummaryResponse | null>(null);
  const [snapshots, setSnapshots] = useState<MetricSnapshotResponse[]>([]);
  const [timeEntries, setTimeEntries] = useState<TimeEntryResponse[]>([]);
  const [costEntries, setCostEntries] = useState<CostEntryResponse[]>([]);
  const [loadState, setLoadState] = useState<LoadState>("loading");
  const [loadError, setLoadError] = useState<ApiError | null>(null);
  const [refreshToken, setRefreshToken] = useState(0);

  // Dialog: Snapshot
  const [snapshotDialogOpen, setSnapshotDialogOpen] = useState(false);
  const [submittingSnapshot, setSubmittingSnapshot] = useState(false);
  const [snapshotFormError, setSnapshotFormError] = useState<ApiError | null>(
    null,
  );
  const [snapshotForm, setSnapshotForm] = useState<SnapshotFormState>({
    views: "",
    impressions: "",
    clickThroughRate: "",
    averageViewDurationSeconds: "",
    watchTimeMinutes: "",
    subscribersGained: "",
    capturedAt: toLocalDatetimeString(),
    source: "YouTube Studio",
    notes: "",
  });
  const [snapshotFieldErrors, setSnapshotFieldErrors] =
    useState<SnapshotFormErrors>({});
  const snapshotViewsRef = useRef<HTMLInputElement>(null);

  // Dialog: Time Entry
  const [timeDialogOpen, setTimeDialogOpen] = useState(false);
  const [submittingTime, setSubmittingTime] = useState(false);
  const [timeFormError, setTimeFormError] = useState<ApiError | null>(null);
  const [timeForm, setTimeForm] = useState<TimeEntryFormState>({
    kind: "human",
    category: "",
    minutes: "",
    recordedAt: toLocalDatetimeString(),
    notes: "",
  });
  const [timeFieldErrors, setTimeFieldErrors] = useState<TimeEntryFormErrors>(
    {},
  );
  const timeCategoryRef = useRef<HTMLInputElement>(null);

  // Dialog: Cost Entry
  const [costDialogOpen, setCostDialogOpen] = useState(false);
  const [submittingCost, setSubmittingCost] = useState(false);
  const [costFormError, setCostFormError] = useState<ApiError | null>(null);
  const [costForm, setCostForm] = useState<CostEntryFormState>({
    amount: "",
    currency: "USD",
    kind: "actual",
    category: "",
    incurredAt: toLocalDatetimeString(),
    notes: "",
  });
  const [costFieldErrors, setCostFieldErrors] = useState<CostEntryFormErrors>(
    {},
  );
  const costAmountRef = useRef<HTMLInputElement>(null);

  // Initial and refreshed data loading
  useEffect(() => {
    const controller = new AbortController();
    setLoadState("loading");
    setLoadError(null);

    Promise.all([
      getTrackingSummary(projectId, controller.signal),
      listMetricSnapshots(projectId, 10, controller.signal),
      listTimeEntries(projectId, 10, controller.signal),
      listCostEntries(projectId, 10, controller.signal),
    ])
      .then(([nextSummary, nextSnapshots, nextTime, nextCosts]) => {
        if (controller.signal.aborted) {
          return;
        }
        setSummary(nextSummary);
        setSnapshots(nextSnapshots);
        setTimeEntries(nextTime);
        setCostEntries(nextCosts);
        setLoadState("ready");
      })
      .catch((err: unknown) => {
        if (controller.signal.aborted) {
          return;
        }
        setLoadError(toApiError(err));
        setLoadState("error");
      });

    return () => controller.abort();
  }, [projectId, refreshToken]);

  const refresh = useCallback(() => {
    setRefreshToken((token) => token + 1);
  }, []);

  // --- Handlers: Metric Snapshot ---

  const openSnapshotDialog = useCallback(() => {
    setSnapshotForm({
      views: "",
      impressions: "",
      clickThroughRate: "",
      averageViewDurationSeconds: "",
      watchTimeMinutes: "",
      subscribersGained: "",
      capturedAt: toLocalDatetimeString(),
      source: "YouTube Studio",
      notes: "",
    });
    setSnapshotFieldErrors({});
    setSnapshotFormError(null);
    setSnapshotDialogOpen(true);
  }, []);

  const closeSnapshotDialog = useCallback(() => {
    if (submittingSnapshot) {
      return;
    }
    setSnapshotDialogOpen(false);
    setSnapshotFieldErrors({});
    setSnapshotFormError(null);
  }, [submittingSnapshot]);

  const handleSnapshotSubmit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();

    if (submittingSnapshot) {
      return;
    }

    const errors: SnapshotFormErrors = {};

    if (!isValidOptionalNumber(snapshotForm.views)) {
      errors.views = t("project.metrics.snapshots.form.numberInvalid");
    }
    if (!isValidOptionalNumber(snapshotForm.impressions)) {
      errors.impressions = t("project.metrics.snapshots.form.numberInvalid");
    }
    if (!isValidOptionalNumber(snapshotForm.clickThroughRate)) {
      errors.clickThroughRate = t(
        "project.metrics.snapshots.form.numberInvalid",
      );
    }
    if (!isValidOptionalNumber(snapshotForm.averageViewDurationSeconds)) {
      errors.averageViewDurationSeconds = t(
        "project.metrics.snapshots.form.numberInvalid",
      );
    }
    if (!isValidOptionalNumber(snapshotForm.watchTimeMinutes)) {
      errors.watchTimeMinutes = t(
        "project.metrics.snapshots.form.numberInvalid",
      );
    }
    if (!isValidOptionalNumber(snapshotForm.subscribersGained, true)) {
      errors.subscribersGained = t(
        "project.metrics.snapshots.form.numberInvalid",
      );
    }

    if (!snapshotForm.capturedAt.trim()) {
      errors.capturedAt = t(
        "project.metrics.snapshots.form.capturedAtRequired",
      );
    } else if (Number.isNaN(new Date(snapshotForm.capturedAt).getTime())) {
      errors.capturedAt = t(
        "project.metrics.snapshots.form.capturedAtInvalid",
      );
    }

    setSnapshotFieldErrors(errors);
    setSnapshotFormError(null);

    if (Object.keys(errors).length > 0) {
      snapshotViewsRef.current?.focus();
      return;
    }

    setSubmittingSnapshot(true);

    try {
      await createMetricSnapshot(projectId, {
        views: parseNullableNumberInput(snapshotForm.views),
        impressions: parseNullableNumberInput(snapshotForm.impressions),
        clickThroughRate: parseNullableNumberInput(
          snapshotForm.clickThroughRate,
        ),
        averageViewDurationSeconds: parseNullableNumberInput(
          snapshotForm.averageViewDurationSeconds,
        ),
        watchTimeMinutes: parseNullableNumberInput(
          snapshotForm.watchTimeMinutes,
        ),
        subscribersGained: parseNullableNumberInput(
          snapshotForm.subscribersGained,
        ),
        capturedAt: toIsoTimestamp(snapshotForm.capturedAt),
        source: snapshotForm.source.trim() || undefined,
        notes: snapshotForm.notes.trim() || undefined,
      });

      setSubmittingSnapshot(false);
      setSnapshotDialogOpen(false);
      showToast(t("project.metrics.snapshots.recordSuccess"), "success");
      onProjectChanged();
      refresh();
    } catch (err: unknown) {
      setSubmittingSnapshot(false);
      setSnapshotFormError(toApiError(err));
    }
  };

  // --- Handlers: Time Entry ---

  const openTimeDialog = useCallback(() => {
    setTimeForm({
      kind: "human",
      category: "",
      minutes: "",
      recordedAt: toLocalDatetimeString(),
      notes: "",
    });
    setTimeFieldErrors({});
    setTimeFormError(null);
    setTimeDialogOpen(true);
  }, []);

  const closeTimeDialog = useCallback(() => {
    if (submittingTime) {
      return;
    }
    setTimeDialogOpen(false);
    setTimeFieldErrors({});
    setTimeFormError(null);
  }, [submittingTime]);

  const handleTimeSubmit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();

    if (submittingTime) {
      return;
    }

    const errors: TimeEntryFormErrors = {};
    const normalizedCategory = timeForm.category.trim();
    const minutesNum = Number(timeForm.minutes.trim());

    if (!normalizedCategory) {
      errors.category = t("project.metrics.time.form.categoryRequired");
    }

    if (
      !timeForm.minutes.trim() ||
      Number.isNaN(minutesNum) ||
      minutesNum <= 0
    ) {
      errors.minutes = t("project.metrics.time.form.minutesPositive");
    }

    if (!timeForm.recordedAt.trim()) {
      errors.recordedAt = t("project.metrics.time.form.recordedAtRequired");
    }

    setTimeFieldErrors(errors);
    setTimeFormError(null);

    if (Object.keys(errors).length > 0) {
      if (errors.category) {
        timeCategoryRef.current?.focus();
      }
      return;
    }

    setSubmittingTime(true);

    try {
      await createTimeEntry(projectId, {
        kind: timeForm.kind,
        category: normalizedCategory,
        minutes: minutesNum,
        recordedAt: toIsoTimestamp(timeForm.recordedAt),
        notes: timeForm.notes.trim() || undefined,
      });

      setSubmittingTime(false);
      setTimeDialogOpen(false);
      showToast(t("project.metrics.time.saved"), "success");
      onProjectChanged();
      refresh();
    } catch (err: unknown) {
      setSubmittingTime(false);
      setTimeFormError(toApiError(err));
    }
  };

  // --- Handlers: Cost Entry ---

  const openCostDialog = useCallback(() => {
    setCostForm({
      amount: "",
      currency: "USD",
      kind: "actual",
      category: "",
      incurredAt: toLocalDatetimeString(),
      notes: "",
    });
    setCostFieldErrors({});
    setCostFormError(null);
    setCostDialogOpen(true);
  }, []);

  const closeCostDialog = useCallback(() => {
    if (submittingCost) {
      return;
    }
    setCostDialogOpen(false);
    setCostFieldErrors({});
    setCostFormError(null);
  }, [submittingCost]);

  const handleCostSubmit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();

    if (submittingCost) {
      return;
    }

    const errors: CostEntryFormErrors = {};
    const amountNum = Number(costForm.amount.trim());
    const normalizedCurrency = costForm.currency.trim().toUpperCase();
    const normalizedCategory = costForm.category.trim();

    if (!costForm.amount.trim() || Number.isNaN(amountNum) || amountNum <= 0) {
      errors.amount = t("project.metrics.costs.form.amountPositive");
    }

    if (!normalizedCurrency || normalizedCurrency.length !== 3) {
      errors.currency = t("project.metrics.costs.form.currencyInvalid");
    }

    if (!normalizedCategory) {
      errors.category = t("project.metrics.costs.form.categoryRequired");
    }

    if (!costForm.incurredAt.trim()) {
      errors.incurredAt = t("project.metrics.costs.form.incurredAtRequired");
    }

    setCostFieldErrors(errors);
    setCostFormError(null);

    if (Object.keys(errors).length > 0) {
      if (errors.amount) {
        costAmountRef.current?.focus();
      }
      return;
    }

    setSubmittingCost(true);

    try {
      await createCostEntry(projectId, {
        amount: amountNum,
        currency: normalizedCurrency,
        kind: costForm.kind,
        category: normalizedCategory,
        incurredAt: toIsoTimestamp(costForm.incurredAt),
        notes: costForm.notes.trim() || undefined,
      });

      setSubmittingCost(false);
      setCostDialogOpen(false);
      showToast(t("project.metrics.costs.saved"), "success");
      onProjectChanged();
      refresh();
    } catch (err: unknown) {
      setSubmittingCost(false);
      setCostFormError(toApiError(err));
    }
  };

  // --- Table Columns ---

  const costSummaryColumns: readonly TableColumn<CurrencyCostSummary>[] = [
    {
      key: "currency",
      header: t("project.metrics.costs.columnCurrency"),
      render: (row) => <span className="mono">{row.currency}</span>,
    },
    {
      key: "actualTotal",
      header: t("project.metrics.costs.columnActualTotal"),
      align: "end",
      render: (row) => number(row.actualTotal),
    },
    {
      key: "estimatedTotal",
      header: t("project.metrics.costs.columnEstimatedTotal"),
      align: "end",
      render: (row) => number(row.estimatedTotal),
    },
    {
      key: "refundedTotal",
      header: t("project.metrics.costs.columnRefundedTotal"),
      align: "end",
      render: (row) => number(row.refundedTotal),
    },
    {
      key: "netActualTotal",
      header: t("project.metrics.costs.columnNetActualTotal"),
      align: "end",
      render: (row) => number(row.netActualTotal),
    },
    {
      key: "status",
      header: t("project.metrics.costs.columnStatus"),
      render: (row) =>
        row.refundExceedsActual ? (
          <Badge tone="warning">
            {t("project.metrics.costs.refundExceedsActual")}
          </Badge>
        ) : (
          <span className="inline-note">{t("project.metrics.notRecorded")}</span>
        ),
    },
  ];

  const snapshotColumns: readonly TableColumn<MetricSnapshotResponse>[] = [
    {
      key: "capturedAt",
      header: t("project.metrics.snapshots.columnCapturedAt"),
      render: (row) => (
        <time dateTime={row.capturedAt}>{dateTime(row.capturedAt)}</time>
      ),
    },
    {
      key: "source",
      header: t("project.metrics.snapshots.columnSource"),
      render: (row) => row.source,
    },
    {
      key: "views",
      header: t("project.metrics.snapshots.columnViews"),
      align: "end",
      render: (row) =>
        renderMetricNumber(row.views, number, t("project.metrics.notRecorded")),
    },
    {
      key: "impressions",
      header: t("project.metrics.snapshots.columnImpressions"),
      align: "end",
      render: (row) =>
        renderMetricNumber(
          row.impressions,
          number,
          t("project.metrics.notRecorded"),
        ),
    },
    {
      key: "clickThroughRate",
      header: t("project.metrics.snapshots.columnCtr"),
      align: "end",
      render: (row) => renderMetricPercent(row.clickThroughRate, number, t),
    },
    {
      key: "averageViewDurationSeconds",
      header: t("project.metrics.snapshots.columnDuration"),
      align: "end",
      render: (row) =>
        renderMetricSeconds(row.averageViewDurationSeconds, number, t),
    },
    {
      key: "watchTimeMinutes",
      header: t("project.metrics.snapshots.columnWatchTime"),
      align: "end",
      render: (row) =>
        renderMetricNumber(
          row.watchTimeMinutes,
          number,
          t("project.metrics.notRecorded"),
        ),
    },
    {
      key: "subscribersGained",
      header: t("project.metrics.snapshots.columnSubs"),
      align: "end",
      render: (row) =>
        renderMetricNumber(
          row.subscribersGained,
          number,
          t("project.metrics.notRecorded"),
        ),
    },
  ];

  const timeColumns: readonly TableColumn<TimeEntryResponse>[] = [
    {
      key: "kind",
      header: t("project.metrics.time.columnKind"),
      render: (row) => (
        <Badge tone={row.kind === "human" ? "accent" : "neutral"}>
          {t(`project.metrics.time.kind.${row.kind}`)}
        </Badge>
      ),
    },
    {
      key: "category",
      header: t("project.metrics.time.columnCategory"),
      render: (row) => row.category,
    },
    {
      key: "minutes",
      header: t("project.metrics.time.columnMinutes"),
      align: "end",
      render: (row) =>
        t("project.metrics.time.minutesUnit", { count: number(row.minutes) }),
    },
    {
      key: "recordedAt",
      header: t("project.metrics.time.columnRecordedAt"),
      render: (row) => (
        <time dateTime={row.recordedAt}>{dateTime(row.recordedAt)}</time>
      ),
    },
    {
      key: "notes",
      header: t("project.metrics.time.columnNotes"),
      render: (row) =>
        row.notes ? (
          row.notes
        ) : (
          <span className="inline-note">
            {t("project.metrics.notRecorded")}
          </span>
        ),
    },
  ];

  const costColumns: readonly TableColumn<CostEntryResponse>[] = [
    {
      key: "amount",
      header: t("project.metrics.costs.columnAmount"),
      align: "end",
      render: (row) => (
        <span className="mono">
          {number(row.amount)} {row.currency}
        </span>
      ),
    },
    {
      key: "kind",
      header: t("project.metrics.costs.columnKind"),
      render: (row) => (
        <Badge tone={costKindTone(row.kind)}>
          {t(`project.metrics.costs.kind.${row.kind}`)}
        </Badge>
      ),
    },
    {
      key: "category",
      header: t("project.metrics.costs.columnCategory"),
      render: (row) => row.category,
    },
    {
      key: "incurredAt",
      header: t("project.metrics.costs.columnIncurredAt"),
      render: (row) => (
        <time dateTime={row.incurredAt}>{dateTime(row.incurredAt)}</time>
      ),
    },
    {
      key: "notes",
      header: t("project.metrics.costs.columnNotes"),
      render: (row) =>
        row.notes ? (
          row.notes
        ) : (
          <span className="inline-note">
            {t("project.metrics.notRecorded")}
          </span>
        ),
    },
  ];

  return (
    <section className="stack" aria-label={t("project.metrics.title")}>
      {loadState === "loading" ? (
        <div role="status" aria-busy="true">
          <span className="visually-hidden">{t("project.metrics.loading")}</span>
          <div className="detail-skeleton" aria-hidden="true">
            <Skeleton width="40%" height={24} />
            <Skeleton width="100%" height={120} />
            <Skeleton width="100%" height={120} />
          </div>
        </div>
      ) : null}

      {loadState === "error" && loadError ? (
        <InlineError
          title={t("project.metrics.error.title")}
          detail={describeLoadError(loadError, t)}
          code={errorCode(loadError)}
          actionLabel={t("action.retry")}
          onAction={refresh}
        />
      ) : null}

      {loadState === "ready" ? (
        <div className="stack">
          {/* Section 1: Tracking Overview & Latest Snapshot */}
          <div className="detail-grid">
            <Card
              title={t("project.metrics.summary.title")}
              description={t("project.metrics.summary.description")}
            >
              {summary ? (
                <dl className="definition-list">
                  <div>
                    <dt>{t("project.metrics.summary.humanMinutes")}</dt>
                    <dd>
                      {t("project.metrics.time.minutesUnit", {
                        count: number(summary.humanMinutesTotal),
                      })}
                    </dd>
                  </div>
                  <div>
                    <dt>{t("project.metrics.summary.machineMinutes")}</dt>
                    <dd>
                      {t("project.metrics.time.minutesUnit", {
                        count: number(summary.machineMinutesTotal),
                      })}
                    </dd>
                  </div>
                  <div>
                    <dt>{t("project.metrics.summary.latestSnapshot")}</dt>
                    <dd>
                      {summary.latestMetricSnapshot ? (
                        <div className="stack-sm">
                          <dl className="definition-list">
                            <div>
                              <dt>{t("project.metrics.summary.latestViews")}</dt>
                              <dd>
                                {renderMetricNumber(
                                  summary.latestMetricSnapshot.views,
                                  number,
                                  t("project.metrics.notRecorded"),
                                )}
                              </dd>
                            </div>
                            <div>
                              <dt>
                                {t("project.metrics.summary.latestImpressions")}
                              </dt>
                              <dd>
                                {renderMetricNumber(
                                  summary.latestMetricSnapshot.impressions,
                                  number,
                                  t("project.metrics.notRecorded"),
                                )}
                              </dd>
                            </div>
                            <div>
                              <dt>{t("project.metrics.summary.latestCtr")}</dt>
                              <dd>
                                {renderMetricPercent(
                                  summary.latestMetricSnapshot.clickThroughRate,
                                  number,
                                  t,
                                )}
                              </dd>
                            </div>
                            <div>
                              <dt>
                                {t("project.metrics.summary.latestDuration")}
                              </dt>
                              <dd>
                                {renderMetricSeconds(
                                  summary.latestMetricSnapshot
                                    .averageViewDurationSeconds,
                                  number,
                                  t,
                                )}
                              </dd>
                            </div>
                            <div>
                              <dt>
                                {t("project.metrics.summary.latestWatchTime")}
                              </dt>
                              <dd>
                                {renderMetricNumber(
                                  summary.latestMetricSnapshot.watchTimeMinutes,
                                  number,
                                  t("project.metrics.notRecorded"),
                                )}
                              </dd>
                            </div>
                            <div>
                              <dt>{t("project.metrics.summary.latestSubs")}</dt>
                              <dd>
                                {renderMetricNumber(
                                  summary.latestMetricSnapshot
                                    .subscribersGained,
                                  number,
                                  t("project.metrics.notRecorded"),
                                )}
                              </dd>
                            </div>
                            <div>
                              <dt>
                                {t("project.metrics.summary.latestCaptured")}
                              </dt>
                              <dd>
                                <time
                                  dateTime={
                                    summary.latestMetricSnapshot.capturedAt
                                  }
                                >
                                  {dateTime(
                                    summary.latestMetricSnapshot.capturedAt,
                                  )}
                                </time>
                              </dd>
                            </div>
                            <div>
                              <dt>
                                {t("project.metrics.summary.latestSource")}
                              </dt>
                              <dd>{summary.latestMetricSnapshot.source}</dd>
                            </div>
                          </dl>
                        </div>
                      ) : (
                        <span className="inline-note">
                          {t("project.metrics.snapshots.empty")}
                        </span>
                      )}
                    </dd>
                  </div>
                </dl>
              ) : null}
            </Card>

            {/* Currency Cost Summaries */}
            <Card
              title={t("project.metrics.costs.summaryTitle")}
              description={t("project.metrics.costs.summaryDescription")}
            >
              {summary && summary.costs.length > 0 ? (
                <Table
                  columns={costSummaryColumns}
                  rows={summary.costs}
                  rowKey={(row) => row.currency}
                  caption={t("project.metrics.costs.summaryTableCaption")}
                />
              ) : (
                <p className="inline-note">
                  {t("project.metrics.costs.summaryEmpty")}
                </p>
              )}
            </Card>
          </div>

          {/* Section 2: Metric Snapshots Table */}
          <Card
            title={t("project.metrics.snapshots.title")}
            description={t("project.metrics.snapshots.description")}
            actions={
              <Button
                variant="secondary"
                size="sm"
                onClick={openSnapshotDialog}
              >
                {t("project.metrics.snapshots.recordAction")}
              </Button>
            }
          >
            {snapshots.length > 0 ? (
              <Table
                columns={snapshotColumns}
                rows={snapshots}
                rowKey={(row) => row.id}
                caption={t("project.metrics.snapshots.tableCaption")}
              />
            ) : (
              <p className="inline-note">
                {t("project.metrics.snapshots.empty")}
              </p>
            )}
          </Card>

          {/* Section 3: Time Entries Table */}
          <Card
            title={t("project.metrics.time.title")}
            description={t("project.metrics.time.description")}
            actions={
              <Button variant="secondary" size="sm" onClick={openTimeDialog}>
                {t("project.metrics.time.recordAction")}
              </Button>
            }
          >
            {timeEntries.length > 0 ? (
              <Table
                columns={timeColumns}
                rows={timeEntries}
                rowKey={(row) => row.id}
                caption={t("project.metrics.time.tableCaption")}
              />
            ) : (
              <p className="inline-note">
                {t("project.metrics.time.empty")}
              </p>
            )}
          </Card>

          {/* Section 4: Cost Entries Table */}
          <Card
            title={t("project.metrics.costs.title")}
            description={t("project.metrics.costs.description")}
            actions={
              <Button variant="secondary" size="sm" onClick={openCostDialog}>
                {t("project.metrics.costs.recordAction")}
              </Button>
            }
          >
            {costEntries.length > 0 ? (
              <Table
                columns={costColumns}
                rows={costEntries}
                rowKey={(row) => row.id}
                caption={t("project.metrics.costs.tableCaption")}
              />
            ) : (
              <p className="inline-note">
                {t("project.metrics.costs.empty")}
              </p>
            )}
          </Card>
        </div>
      ) : null}

      {/* Dialog: Record Metric Snapshot */}
      <Dialog
        open={snapshotDialogOpen}
        title={t("project.metrics.snapshots.dialogTitle")}
        description={t("project.metrics.snapshots.dialogDescription")}
        onClose={closeSnapshotDialog}
        dismissible={!submittingSnapshot}
      >
        <form
          className="form-stack"
          onSubmit={handleSnapshotSubmit}
          noValidate
        >
          {snapshotFormError ? (
            <InlineError
              title={t("project.metrics.snapshots.dialogTitle")}
              detail={snapshotFormError.detail ?? snapshotFormError.title}
              code={errorCode(snapshotFormError)}
            />
          ) : null}

          <div className="field">
            <label className="field__label" htmlFor="snapshot-views">
              {t("project.metrics.snapshots.form.views")}
              <span className="field__optional">
                {t("project.publish.approval.noteOptional")}
              </span>
            </label>
            <input
              ref={snapshotViewsRef}
              id="snapshot-views"
              data-autofocus
              className="field__input"
              type="number"
              min={0}
              step={1}
              value={snapshotForm.views}
              placeholder={t("project.metrics.notRecorded")}
              aria-invalid={snapshotFieldErrors.views ? true : undefined}
              aria-describedby={
                snapshotFieldErrors.views
                  ? "snapshot-views-error"
                  : "snapshot-views-hint"
              }
              onChange={(e) =>
                setSnapshotForm((prev) => ({
                  ...prev,
                  views: e.target.value,
                }))
              }
            />
            {snapshotFieldErrors.views ? (
              <p className="field__error" id="snapshot-views-error">
                {snapshotFieldErrors.views}
              </p>
            ) : (
              <p className="field__hint" id="snapshot-views-hint">
                {t("project.metrics.snapshots.form.viewsHint")}
              </p>
            )}
          </div>

          <div className="field">
            <label className="field__label" htmlFor="snapshot-impressions">
              {t("project.metrics.snapshots.form.impressions")}
              <span className="field__optional">
                {t("project.publish.approval.noteOptional")}
              </span>
            </label>
            <input
              id="snapshot-impressions"
              className="field__input"
              type="number"
              min={0}
              step={1}
              value={snapshotForm.impressions}
              placeholder={t("project.metrics.notRecorded")}
              aria-invalid={snapshotFieldErrors.impressions ? true : undefined}
              aria-describedby={
                snapshotFieldErrors.impressions
                  ? "snapshot-impressions-error"
                  : "snapshot-impressions-hint"
              }
              onChange={(e) =>
                setSnapshotForm((prev) => ({
                  ...prev,
                  impressions: e.target.value,
                }))
              }
            />
            {snapshotFieldErrors.impressions ? (
              <p className="field__error" id="snapshot-impressions-error">
                {snapshotFieldErrors.impressions}
              </p>
            ) : (
              <p className="field__hint" id="snapshot-impressions-hint">
                {t("project.metrics.snapshots.form.impressionsHint")}
              </p>
            )}
          </div>

          <div className="field">
            <label className="field__label" htmlFor="snapshot-ctr">
              {t("project.metrics.snapshots.form.ctr")}
              <span className="field__optional">
                {t("project.publish.approval.noteOptional")}
              </span>
            </label>
            <input
              id="snapshot-ctr"
              className="field__input"
              type="number"
              min={0}
              step="any"
              value={snapshotForm.clickThroughRate}
              placeholder={t("project.metrics.notRecorded")}
              aria-invalid={
                snapshotFieldErrors.clickThroughRate ? true : undefined
              }
              aria-describedby={
                snapshotFieldErrors.clickThroughRate
                  ? "snapshot-ctr-error"
                  : "snapshot-ctr-hint"
              }
              onChange={(e) =>
                setSnapshotForm((prev) => ({
                  ...prev,
                  clickThroughRate: e.target.value,
                }))
              }
            />
            {snapshotFieldErrors.clickThroughRate ? (
              <p className="field__error" id="snapshot-ctr-error">
                {snapshotFieldErrors.clickThroughRate}
              </p>
            ) : (
              <p className="field__hint" id="snapshot-ctr-hint">
                {t("project.metrics.snapshots.form.ctrHint")}
              </p>
            )}
          </div>

          <div className="field">
            <label className="field__label" htmlFor="snapshot-duration">
              {t("project.metrics.snapshots.form.duration")}
              <span className="field__optional">
                {t("project.publish.approval.noteOptional")}
              </span>
            </label>
            <input
              id="snapshot-duration"
              className="field__input"
              type="number"
              min={0}
              step="any"
              value={snapshotForm.averageViewDurationSeconds}
              placeholder={t("project.metrics.notRecorded")}
              aria-invalid={
                snapshotFieldErrors.averageViewDurationSeconds
                  ? true
                  : undefined
              }
              aria-describedby={
                snapshotFieldErrors.averageViewDurationSeconds
                  ? "snapshot-duration-error"
                  : "snapshot-duration-hint"
              }
              onChange={(e) =>
                setSnapshotForm((prev) => ({
                  ...prev,
                  averageViewDurationSeconds: e.target.value,
                }))
              }
            />
            {snapshotFieldErrors.averageViewDurationSeconds ? (
              <p className="field__error" id="snapshot-duration-error">
                {snapshotFieldErrors.averageViewDurationSeconds}
              </p>
            ) : (
              <p className="field__hint" id="snapshot-duration-hint">
                {t("project.metrics.snapshots.form.durationHint")}
              </p>
            )}
          </div>

          <div className="field">
            <label className="field__label" htmlFor="snapshot-watch-time">
              {t("project.metrics.snapshots.form.watchTime")}
              <span className="field__optional">
                {t("project.publish.approval.noteOptional")}
              </span>
            </label>
            <input
              id="snapshot-watch-time"
              className="field__input"
              type="number"
              min={0}
              step="any"
              value={snapshotForm.watchTimeMinutes}
              placeholder={t("project.metrics.notRecorded")}
              aria-invalid={
                snapshotFieldErrors.watchTimeMinutes ? true : undefined
              }
              aria-describedby={
                snapshotFieldErrors.watchTimeMinutes
                  ? "snapshot-watch-time-error"
                  : "snapshot-watch-time-hint"
              }
              onChange={(e) =>
                setSnapshotForm((prev) => ({
                  ...prev,
                  watchTimeMinutes: e.target.value,
                }))
              }
            />
            {snapshotFieldErrors.watchTimeMinutes ? (
              <p className="field__error" id="snapshot-watch-time-error">
                {snapshotFieldErrors.watchTimeMinutes}
              </p>
            ) : (
              <p className="field__hint" id="snapshot-watch-time-hint">
                {t("project.metrics.snapshots.form.watchTimeHint")}
              </p>
            )}
          </div>

          <div className="field">
            <label className="field__label" htmlFor="snapshot-subs">
              {t("project.metrics.snapshots.form.subs")}
              <span className="field__optional">
                {t("project.publish.approval.noteOptional")}
              </span>
            </label>
            <input
              id="snapshot-subs"
              className="field__input"
              type="number"
              step={1}
              value={snapshotForm.subscribersGained}
              placeholder={t("project.metrics.notRecorded")}
              aria-invalid={
                snapshotFieldErrors.subscribersGained ? true : undefined
              }
              aria-describedby={
                snapshotFieldErrors.subscribersGained
                  ? "snapshot-subs-error"
                  : "snapshot-subs-hint"
              }
              onChange={(e) =>
                setSnapshotForm((prev) => ({
                  ...prev,
                  subscribersGained: e.target.value,
                }))
              }
            />
            {snapshotFieldErrors.subscribersGained ? (
              <p className="field__error" id="snapshot-subs-error">
                {snapshotFieldErrors.subscribersGained}
              </p>
            ) : (
              <p className="field__hint" id="snapshot-subs-hint">
                {t("project.metrics.snapshots.form.subsHint")}
              </p>
            )}
          </div>

          <div className="field">
            <label className="field__label" htmlFor="snapshot-captured-at">
              {t("project.metrics.snapshots.form.capturedAt")}
            </label>
            <input
              id="snapshot-captured-at"
              className="field__input"
              type="datetime-local"
              value={snapshotForm.capturedAt}
              aria-invalid={snapshotFieldErrors.capturedAt ? true : undefined}
              aria-describedby={
                snapshotFieldErrors.capturedAt
                  ? "snapshot-captured-at-error"
                  : "snapshot-captured-at-hint"
              }
              onChange={(e) =>
                setSnapshotForm((prev) => ({
                  ...prev,
                  capturedAt: e.target.value,
                }))
              }
            />
            {snapshotFieldErrors.capturedAt ? (
              <p className="field__error" id="snapshot-captured-at-error">
                {snapshotFieldErrors.capturedAt}
              </p>
            ) : (
              <p className="field__hint" id="snapshot-captured-at-hint">
                {t("project.metrics.snapshots.form.capturedAtHint")}
              </p>
            )}
          </div>

          <div className="field">
            <label className="field__label" htmlFor="snapshot-source">
              {t("project.metrics.snapshots.form.source")}
              <span className="field__optional">
                {t("project.publish.approval.noteOptional")}
              </span>
            </label>
            <input
              id="snapshot-source"
              className="field__input"
              type="text"
              value={snapshotForm.source}
              placeholder={t(
                "project.metrics.snapshots.form.sourcePlaceholder",
              )}
              aria-describedby="snapshot-source-hint"
              onChange={(e) =>
                setSnapshotForm((prev) => ({
                  ...prev,
                  source: e.target.value,
                }))
              }
            />
            <p className="field__hint" id="snapshot-source-hint">
              {t("project.metrics.snapshots.form.sourceHint")}
            </p>
          </div>

          <div className="field">
            <label className="field__label" htmlFor="snapshot-notes">
              {t("project.metrics.snapshots.form.notes")}
              <span className="field__optional">
                {t("project.publish.approval.noteOptional")}
              </span>
            </label>
            <textarea
              id="snapshot-notes"
              className="field__textarea"
              rows={2}
              value={snapshotForm.notes}
              aria-describedby="snapshot-notes-hint"
              onChange={(e) =>
                setSnapshotForm((prev) => ({
                  ...prev,
                  notes: e.target.value,
                }))
              }
            />
            <p className="field__hint" id="snapshot-notes-hint">
              {t("project.metrics.snapshots.form.notesHint")}
            </p>
          </div>

          <div className="dialog-form__actions">
            <Button
              variant="secondary"
              onClick={closeSnapshotDialog}
              disabled={submittingSnapshot}
            >
              {t("action.cancel")}
            </Button>
            <Button
              type="submit"
              variant="primary"
              loading={submittingSnapshot}
              loadingLabel={t("project.metrics.snapshots.form.submitting")}
            >
              {t("project.metrics.snapshots.form.submit")}
            </Button>
          </div>
        </form>
      </Dialog>

      {/* Dialog: Record Time Entry */}
      <Dialog
        open={timeDialogOpen}
        title={t("project.metrics.time.dialogTitle")}
        description={t("project.metrics.time.dialogDescription")}
        onClose={closeTimeDialog}
        dismissible={!submittingTime}
      >
        <form className="form-stack" onSubmit={handleTimeSubmit} noValidate>
          {timeFormError ? (
            <InlineError
              title={t("project.metrics.time.dialogTitle")}
              detail={timeFormError.detail ?? timeFormError.title}
              code={errorCode(timeFormError)}
            />
          ) : null}

          <div className="field">
            <label className="field__label" htmlFor="time-kind">
              {t("project.metrics.time.form.kind")}
            </label>
            <select
              id="time-kind"
              className="select"
              value={timeForm.kind}
              onChange={(e) =>
                setTimeForm((prev) => ({
                  ...prev,
                  kind: e.target.value as TimeEntryKind,
                }))
              }
            >
              <option value="human">
                {t("project.metrics.time.kind.human")}
              </option>
              <option value="machine">
                {t("project.metrics.time.kind.machine")}
              </option>
            </select>
          </div>

          <div className="field">
            <label className="field__label" htmlFor="time-category">
              {t("project.metrics.time.form.category")}
            </label>
            <input
              ref={timeCategoryRef}
              id="time-category"
              data-autofocus
              className="field__input"
              type="text"
              value={timeForm.category}
              placeholder={t(
                "project.metrics.time.form.categoryPlaceholder",
              )}
              aria-invalid={timeFieldErrors.category ? true : undefined}
              aria-describedby={
                timeFieldErrors.category
                  ? "time-category-error"
                  : "time-category-hint"
              }
              onChange={(e) =>
                setTimeForm((prev) => ({
                  ...prev,
                  category: e.target.value,
                }))
              }
            />
            {timeFieldErrors.category ? (
              <p className="field__error" id="time-category-error">
                {timeFieldErrors.category}
              </p>
            ) : (
              <p className="field__hint" id="time-category-hint">
                {t("project.metrics.time.form.categoryHint")}
              </p>
            )}
          </div>

          <div className="field">
            <label className="field__label" htmlFor="time-minutes">
              {t("project.metrics.time.form.minutes")}
            </label>
            <input
              id="time-minutes"
              className="field__input"
              type="number"
              min={1}
              step={1}
              value={timeForm.minutes}
              aria-invalid={timeFieldErrors.minutes ? true : undefined}
              aria-describedby={
                timeFieldErrors.minutes
                  ? "time-minutes-error"
                  : "time-minutes-hint"
              }
              onChange={(e) =>
                setTimeForm((prev) => ({
                  ...prev,
                  minutes: e.target.value,
                }))
              }
            />
            {timeFieldErrors.minutes ? (
              <p className="field__error" id="time-minutes-error">
                {timeFieldErrors.minutes}
              </p>
            ) : (
              <p className="field__hint" id="time-minutes-hint">
                {t("project.metrics.time.form.minutesHint")}
              </p>
            )}
          </div>

          <div className="field">
            <label className="field__label" htmlFor="time-recorded-at">
              {t("project.metrics.time.form.recordedAt")}
            </label>
            <input
              id="time-recorded-at"
              className="field__input"
              type="datetime-local"
              value={timeForm.recordedAt}
              aria-invalid={timeFieldErrors.recordedAt ? true : undefined}
              aria-describedby={
                timeFieldErrors.recordedAt
                  ? "time-recorded-at-error"
                  : "time-recorded-at-hint"
              }
              onChange={(e) =>
                setTimeForm((prev) => ({
                  ...prev,
                  recordedAt: e.target.value,
                }))
              }
            />
            {timeFieldErrors.recordedAt ? (
              <p className="field__error" id="time-recorded-at-error">
                {timeFieldErrors.recordedAt}
              </p>
            ) : (
              <p className="field__hint" id="time-recorded-at-hint">
                {t("project.metrics.time.form.recordedAtHint")}
              </p>
            )}
          </div>

          <div className="field">
            <label className="field__label" htmlFor="time-notes">
              {t("project.metrics.time.form.notes")}
              <span className="field__optional">
                {t("project.publish.approval.noteOptional")}
              </span>
            </label>
            <textarea
              id="time-notes"
              className="field__textarea"
              rows={2}
              value={timeForm.notes}
              aria-describedby="time-notes-hint"
              onChange={(e) =>
                setTimeForm((prev) => ({ ...prev, notes: e.target.value }))
              }
            />
            <p className="field__hint" id="time-notes-hint">
              {t("project.metrics.time.form.notesHint")}
            </p>
          </div>

          <div className="dialog-form__actions">
            <Button
              variant="secondary"
              onClick={closeTimeDialog}
              disabled={submittingTime}
            >
              {t("action.cancel")}
            </Button>
            <Button
              type="submit"
              variant="primary"
              loading={submittingTime}
              loadingLabel={t("project.metrics.time.form.submitting")}
            >
              {t("project.metrics.time.form.submit")}
            </Button>
          </div>
        </form>
      </Dialog>

      {/* Dialog: Record Cost Entry */}
      <Dialog
        open={costDialogOpen}
        title={t("project.metrics.costs.dialogTitle")}
        description={t("project.metrics.costs.dialogDescription")}
        onClose={closeCostDialog}
        dismissible={!submittingCost}
      >
        <form className="form-stack" onSubmit={handleCostSubmit} noValidate>
          {costFormError ? (
            <InlineError
              title={t("project.metrics.costs.dialogTitle")}
              detail={costFormError.detail ?? costFormError.title}
              code={errorCode(costFormError)}
            />
          ) : null}

          <div className="field">
            <label className="field__label" htmlFor="cost-amount">
              {t("project.metrics.costs.form.amount")}
            </label>
            <input
              ref={costAmountRef}
              id="cost-amount"
              data-autofocus
              className="field__input"
              type="number"
              min={0.01}
              step="any"
              value={costForm.amount}
              aria-invalid={costFieldErrors.amount ? true : undefined}
              aria-describedby={
                costFieldErrors.amount
                  ? "cost-amount-error"
                  : "cost-amount-hint"
              }
              onChange={(e) =>
                setCostForm((prev) => ({
                  ...prev,
                  amount: e.target.value,
                }))
              }
            />
            {costFieldErrors.amount ? (
              <p className="field__error" id="cost-amount-error">
                {costFieldErrors.amount}
              </p>
            ) : (
              <p className="field__hint" id="cost-amount-hint">
                {t("project.metrics.costs.form.amountHint")}
              </p>
            )}
          </div>

          <div className="field">
            <label className="field__label" htmlFor="cost-currency">
              {t("project.metrics.costs.form.currency")}
            </label>
            <input
              id="cost-currency"
              className="field__input mono"
              type="text"
              maxLength={3}
              value={costForm.currency}
              placeholder={t(
                "project.metrics.costs.form.currencyPlaceholder",
              )}
              aria-invalid={costFieldErrors.currency ? true : undefined}
              aria-describedby={
                costFieldErrors.currency
                  ? "cost-currency-error"
                  : "cost-currency-hint"
              }
              onChange={(e) =>
                setCostForm((prev) => ({
                  ...prev,
                  currency: e.target.value.toUpperCase(),
                }))
              }
            />
            {costFieldErrors.currency ? (
              <p className="field__error" id="cost-currency-error">
                {costFieldErrors.currency}
              </p>
            ) : (
              <p className="field__hint" id="cost-currency-hint">
                {t("project.metrics.costs.form.currencyHint")}
              </p>
            )}
          </div>

          <div className="field">
            <label className="field__label" htmlFor="cost-kind">
              {t("project.metrics.costs.form.kind")}
            </label>
            <select
              id="cost-kind"
              className="select"
              value={costForm.kind}
              onChange={(e) =>
                setCostForm((prev) => ({
                  ...prev,
                  kind: e.target.value as CostEntryKind,
                }))
              }
            >
              <option value="actual">
                {t("project.metrics.costs.kind.actual")}
              </option>
              <option value="estimated">
                {t("project.metrics.costs.kind.estimated")}
              </option>
              <option value="refunded">
                {t("project.metrics.costs.kind.refunded")}
              </option>
            </select>
          </div>

          <div className="field">
            <label className="field__label" htmlFor="cost-category">
              {t("project.metrics.costs.form.category")}
            </label>
            <input
              id="cost-category"
              className="field__input"
              type="text"
              value={costForm.category}
              placeholder={t(
                "project.metrics.costs.form.categoryPlaceholder",
              )}
              aria-invalid={costFieldErrors.category ? true : undefined}
              aria-describedby={
                costFieldErrors.category
                  ? "cost-category-error"
                  : "cost-category-hint"
              }
              onChange={(e) =>
                setCostForm((prev) => ({
                  ...prev,
                  category: e.target.value,
                }))
              }
            />
            {costFieldErrors.category ? (
              <p className="field__error" id="cost-category-error">
                {costFieldErrors.category}
              </p>
            ) : (
              <p className="field__hint" id="cost-category-hint">
                {t("project.metrics.costs.form.categoryHint")}
              </p>
            )}
          </div>

          <div className="field">
            <label className="field__label" htmlFor="cost-incurred-at">
              {t("project.metrics.costs.form.incurredAt")}
            </label>
            <input
              id="cost-incurred-at"
              className="field__input"
              type="datetime-local"
              value={costForm.incurredAt}
              aria-invalid={costFieldErrors.incurredAt ? true : undefined}
              aria-describedby={
                costFieldErrors.incurredAt
                  ? "cost-incurred-at-error"
                  : "cost-incurred-at-hint"
              }
              onChange={(e) =>
                setCostForm((prev) => ({
                  ...prev,
                  incurredAt: e.target.value,
                }))
              }
            />
            {costFieldErrors.incurredAt ? (
              <p className="field__error" id="cost-incurred-at-error">
                {costFieldErrors.incurredAt}
              </p>
            ) : (
              <p className="field__hint" id="cost-incurred-at-hint">
                {t("project.metrics.costs.form.incurredAtHint")}
              </p>
            )}
          </div>

          <div className="field">
            <label className="field__label" htmlFor="cost-notes">
              {t("project.metrics.costs.form.notes")}
              <span className="field__optional">
                {t("project.publish.approval.noteOptional")}
              </span>
            </label>
            <textarea
              id="cost-notes"
              className="field__textarea"
              rows={2}
              value={costForm.notes}
              aria-describedby="cost-notes-hint"
              onChange={(e) =>
                setCostForm((prev) => ({ ...prev, notes: e.target.value }))
              }
            />
            <p className="field__hint" id="cost-notes-hint">
              {t("project.metrics.costs.form.notesHint")}
            </p>
          </div>

          <div className="dialog-form__actions">
            <Button
              variant="secondary"
              onClick={closeCostDialog}
              disabled={submittingCost}
            >
              {t("action.cancel")}
            </Button>
            <Button
              type="submit"
              variant="primary"
              loading={submittingCost}
              loadingLabel={t("project.metrics.costs.form.submitting")}
            >
              {t("project.metrics.costs.form.submit")}
            </Button>
          </div>
        </form>
      </Dialog>
    </section>
  );
}
