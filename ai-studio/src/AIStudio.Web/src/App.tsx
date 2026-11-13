import { lazy, Suspense } from "react";
import { Skeleton } from "./components/Skeleton";
import { AppShell } from "./layout/AppShell";
import { useHashRoute } from "./routing/useHashRoute";
import { ProjectsScreen } from "./screens/ProjectsScreen";

const ProjectDetailScreen = lazy(() =>
  import("./screens/ProjectDetailScreen").then((m) => ({
    default: m.ProjectDetailScreen,
  })),
);

const RunsScreen = lazy(() =>
  import("./screens/RunsScreen").then((m) => ({
    default: m.RunsScreen,
  })),
);

const ResearchScreen = lazy(() =>
  import("./screens/ResearchScreen").then((m) => ({
    default: m.ResearchScreen,
  })),
);

const SettingsScreen = lazy(() =>
  import("./screens/SettingsScreen").then((m) => ({
    default: m.SettingsScreen,
  })),
);

function ScreenFallback() {
  return (
    <div className="stack" style={{ padding: "var(--space-4)" }} aria-busy="true">
      <Skeleton width="40%" height={24} />
      <Skeleton width="60%" height={16} />
      <div style={{ marginTop: "var(--space-4)" }}>
        <Skeleton width="100%" height={120} />
      </div>
    </div>
  );
}

export function App() {
  const [route, navigate] = useHashRoute();

  return (
    <AppShell route={route} onNavigate={navigate}>
      <Suspense fallback={<ScreenFallback />}>
        {route.screen === "research" ? (
        <Suspense fallback={<ScreenFallback />}>
          <ResearchScreen
            onOpenProject={(projectId) =>
              navigate({ screen: "project", projectId })
            }
          />
        </Suspense>
      ) : null}

      {route.screen === "settings" ? (
          <SettingsScreen
            onOpenProject={(projectId) =>
              navigate({ screen: "project", projectId })
            }
          />
        ) : null}

        {route.screen === "runs" ? (
          <RunsScreen
            initialJobId={route.jobId}
            onOpenProject={(projectId) =>
              navigate({ screen: "project", projectId })
            }
            onSelectJob={(jobId) =>
              navigate({ screen: "runs", jobId })
            }
          />
        ) : null}

        {route.screen === "projects" ? (
          <ProjectsScreen
            onOpenProject={(projectId) =>
              navigate({ screen: "project", projectId })
            }
          />
        ) : null}

        {route.screen === "project" ? (
          <ProjectDetailScreen
            projectId={route.projectId}
            onBack={() => navigate({ screen: "projects" })}
          />
        ) : null}
      </Suspense>
    </AppShell>
  );
}
