import { useCallback, useEffect, useState, type FormEvent } from "react";
import { isApiRequestError, type ApiError } from "../api/client";
import {
  collectTrends,
  getResearchPackage,
  listResearchPackages,
  promoteResearchCandidate,
  synthesizeResearch,
  type ResearchPackageDto,
  type ResearchPackageSummaryDto,
  type TrendSignalDto,
} from "../api/research";
import { Badge } from "../components/Badge";
import { Button } from "../components/Button";
import { Card } from "../components/Card";
import { InlineError } from "../components/InlineError";
import { useToast } from "../components/Toast";
import { useFormatters } from "../i18n/format";
import { useI18n } from "../i18n/i18n";

export interface ResearchScreenProps {
  onOpenProject: (projectId: string) => void;
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

export function ResearchScreen({ onOpenProject }: ResearchScreenProps) {
  const { t } = useI18n();
  const { dateTime, number } = useFormatters();
  const { showToast } = useToast();

  const [topic, setTopic] = useState("");
  const [regionCode, setRegionCode] = useState("ID");
  const [targetAudience, setTargetAudience] = useState("");
  const [offerContext, setOfferContext] = useState("");
  const [includeWatchlist, setIncludeWatchlist] = useState(false);
  const [seedUrlsText, setSeedUrlsText] = useState("");

  const [collecting, setCollecting] = useState(false);
  const [synthesizing, setSynthesizing] = useState(false);
  const [promotingIndex, setPromotingIndex] = useState<number | null>(null);
  const [error, setError] = useState<ApiError | null>(null);

  const [signals, setSignals] = useState<TrendSignalDto[]>([]);
  const [activePackage, setActivePackage] = useState<ResearchPackageDto | null>(null);
  const [history, setHistory] = useState<ResearchPackageSummaryDto[]>([]);

  const loadHistory = useCallback(async (signal?: AbortSignal) => {
    try {
      const items = await listResearchPackages(10, signal);
      if (!signal?.aborted) {
        setHistory(items);
      }
    } catch {
      // Non-critical history list
    }
  }, []);

  useEffect(() => {
    const controller = new AbortController();
    loadHistory(controller.signal);
    return () => controller.abort();
  }, [loadHistory]);

  const parsedSeeds = seedUrlsText
    .split(/\r?\n/)
    .map((line) => line.trim())
    .filter((line) => line.length > 0);

  const handleCollect = async (e: FormEvent) => {
    e.preventDefault();
    if (!topic.trim()) {
      return;
    }

    setCollecting(true);
    setError(null);
    try {
      const collected = await collectTrends({
        topic: topic.trim(),
        regionCode: regionCode.trim() || "ID",
        includeWatchlist,
        operatorSeedUrls: parsedSeeds,
      });
      setSignals(collected);
      showToast(t("research.toast.collected", { count: collected.length }), "success");
    } catch (err: unknown) {
      setError(toApiError(err));
    } finally {
      setCollecting(false);
    }
  };

  const handleSynthesize = async () => {
    if (!topic.trim()) {
      return;
    }

    setSynthesizing(true);
    setError(null);
    try {
      const pkg = await synthesizeResearch({
        topic: topic.trim(),
        regionCode: regionCode.trim() || "ID",
        includeWatchlist,
        operatorSeedUrls: parsedSeeds,
        signals: signals.length > 0 ? signals : undefined,
        targetAudience: targetAudience.trim() || undefined,
        offerContext: offerContext.trim() || undefined,
      });
      setActivePackage(pkg);
      showToast(t("research.toast.synthesized"), "success");
      await loadHistory();
    } catch (err: unknown) {
      setError(toApiError(err));
    } finally {
      setSynthesizing(false);
    }
  };

  const handleOpenPackage = async (id: string) => {
    setError(null);
    try {
      const pkg = await getResearchPackage(id);
      setActivePackage(pkg);
    } catch (err: unknown) {
      setError(toApiError(err));
    }
  };

  const handlePromote = async (candidateIndex: number) => {
    if (!activePackage) {
      return;
    }

    setPromotingIndex(candidateIndex);
    setError(null);
    try {
      const result = await promoteResearchCandidate(activePackage.id, candidateIndex);
      showToast(t("research.toast.promoted", { title: result.title }), "success");
      onOpenProject(result.contentProjectId);
    } catch (err: unknown) {
      setError(toApiError(err));
    } finally {
      setPromotingIndex(null);
    }
  };

  return (
    <div className="stack">
      <div className="page-header">
        <div>
          <h1 className="page-title">{t("research.title")}</h1>
          <p className="page-subtitle">{t("research.subtitle")}</p>
        </div>
      </div>

      {error ? (
        <InlineError
          title={error.title}
          detail={error.detail}
          code={error.code}
        />
      ) : null}

      <Card title={t("research.form.title")} description={t("research.form.subtitle")}>
        <form className="stack" onSubmit={handleCollect}>
          <div className="grid-2">
            <div className="field">
              <label className="field__label" htmlFor="research-topic">
                {t("research.form.topic")}
              </label>
              <input
                id="research-topic"
                className="field__input"
                type="text"
                value={topic}
                onChange={(e) => setTopic(e.target.value)}
                placeholder={t("research.form.topicPlaceholder")}
                required
              />
            </div>
            <div className="field">
              <label className="field__label" htmlFor="research-region">
                {t("research.form.region")}
              </label>
              <input
                id="research-region"
                className="field__input"
                type="text"
                value={regionCode}
                onChange={(e) => setRegionCode(e.target.value)}
              />
            </div>
          </div>

          <div className="grid-2">
            <div className="field">
              <label className="field__label" htmlFor="research-audience">
                {t("research.form.audience")}
              </label>
              <input
                id="research-audience"
                className="field__input"
                type="text"
                value={targetAudience}
                onChange={(e) => setTargetAudience(e.target.value)}
              />
            </div>
            <div className="field">
              <label className="field__label" htmlFor="research-offer">
                {t("research.form.offer")}
              </label>
              <input
                id="research-offer"
                className="field__input"
                type="text"
                value={offerContext}
                onChange={(e) => setOfferContext(e.target.value)}
              />
            </div>
          </div>

          <div className="field">
            <label className="field__label" htmlFor="research-seeds">
              {t("research.form.seeds")}
            </label>
            <textarea
              id="research-seeds"
              className="field__textarea"
              rows={3}
              value={seedUrlsText}
              onChange={(e) => setSeedUrlsText(e.target.value)}
              placeholder={t("research.form.seedsPlaceholder")}
            />
          </div>

          <label className="checkbox-row">
            <input
              type="checkbox"
              checked={includeWatchlist}
              onChange={(e) => setIncludeWatchlist(e.target.checked)}
            />
            <span>{t("research.form.includeWatchlist")}</span>
          </label>

          <div className="row-actions">
            <Button type="submit" disabled={collecting || !topic.trim()}>
              {collecting ? t("research.form.collecting") : t("research.form.collectButton")}
            </Button>
            <Button
              type="button"
              variant="secondary"
              disabled={synthesizing || !topic.trim()}
              onClick={handleSynthesize}
            >
              {synthesizing
                ? t("research.form.synthesizing")
                : t("research.form.synthesizeButton")}
            </Button>
          </div>
        </form>
      </Card>

      {signals.length > 0 ? (
        <Card
          title={t("research.signals.title", { count: signals.length })}
          description={t("research.signals.subtitle")}
        >
          <div className="stack">
            {signals.map((sig) => (
              <div key={sig.url + sig.externalId} className="kv-card">
                <div className="row-between">
                  <strong>{sig.title}</strong>
                  <div className="row-actions">
                    <Badge tone="neutral">{sig.sourceType}</Badge>
                    <Badge tone={sig.freshnessTier === "breaking" ? "accent" : "neutral"}>
                      {sig.freshnessTier}
                    </Badge>
                  </div>
                </div>
                <p className="muted-text">{sig.rawSummary}</p>
                {sig.metrics.viewCount != null ? (
                  <p className="muted-text">
                    {t("research.signals.metrics", {
                      views: number(sig.metrics.viewCount),
                      outlier: sig.metrics.outlierRatio ?? 0,
                      vph: sig.metrics.viewsPerHour ?? 0,
                    })}
                  </p>
                ) : null}
                <a href={sig.url} target="_blank" rel="noreferrer">
                  {sig.url}
                </a>
              </div>
            ))}
          </div>
        </Card>
      ) : null}

      {activePackage ? (
        <Card
          title={t("research.package.title", { topic: activePackage.queryTopic })}
          description={t("research.package.subtitle", {
            provider: activePackage.providerUsed,
            date: dateTime(activePackage.createdAt),
          })}
        >
          <div className="stack">
            {activePackage.degradedFallback ? (
              <Badge tone="warning">{t("research.package.degraded")}</Badge>
            ) : null}

            {activePackage.claims.length > 0 ? (
              <div className="stack">
                <h3>{t("research.package.claimsTitle")}</h3>
                {activePackage.claims.map((claim) => (
                  <div key={claim.id} className="kv-card">
                    <div className="row-between">
                      <span>{claim.claimText}</span>
                      <Badge
                        tone={
                          claim.claimKind === "verified_stat"
                            ? "success"
                            : claim.claimKind === "observed_pattern"
                              ? "accent"
                              : "warning"
                        }
                      >
                        {claim.claimKind}
                      </Badge>
                    </div>
                    {claim.confidenceNote ? (
                      <p className="muted-text">{claim.confidenceNote}</p>
                    ) : null}
                  </div>
                ))}
              </div>
            ) : null}

            <div className="stack">
              <h3>{t("research.package.candidatesTitle")}</h3>
              {activePackage.candidates.map((cand, idx) => (
                <div key={`${cand.title}-${idx}`} className="kv-card stack">
                  <div className="row-between">
                    <strong>{cand.title}</strong>
                    <div className="row-actions">
                      <Badge tone="accent">{cand.angleCategory}</Badge>
                      <Badge
                        tone={
                          cand.strategicIntent === "offer_bridge" ? "success" : "neutral"
                        }
                      >
                        {cand.strategicIntent}
                      </Badge>
                    </div>
                  </div>
                  <p>{cand.hook}</p>
                  <p className="muted-text">{cand.angle}</p>
                  <p className="muted-text">{cand.whyTrendingNow}</p>
                  <div>
                    <Button
                      type="button"
                      disabled={promotingIndex === idx}
                      onClick={() => handlePromote(idx)}
                    >
                      {promotingIndex === idx
                        ? t("research.package.promoting")
                        : t("research.package.promoteButton")}
                    </Button>
                  </div>
                </div>
              ))}
            </div>
          </div>
        </Card>
      ) : null}

      {history.length > 0 ? (
        <Card title={t("research.history.title")}>
          <div className="table-wrap">
            <table className="table">
              <thead>
                <tr>
                  <th>{t("research.history.topic")}</th>
                  <th>{t("research.history.region")}</th>
                  <th>{t("research.history.candidates")}</th>
                  <th>{t("research.history.createdAt")}</th>
                  <th>{t("research.history.action")}</th>
                </tr>
              </thead>
              <tbody>
                {history.map((item) => (
                  <tr key={item.id}>
                    <td>{item.queryTopic}</td>
                    <td>{item.regionCode}</td>
                    <td>{item.candidateCount}</td>
                    <td>{dateTime(item.createdAt)}</td>
                    <td>
                      <Button
                        type="button"
                        variant="secondary"
                        onClick={() => handleOpenPackage(item.id)}
                      >
                        {t("research.history.open")}
                      </Button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </Card>
      ) : null}
    </div>
  );
}
