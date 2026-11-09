import { AppShell } from "./layout/AppShell";
import { useHashRoute } from "./routing/useHashRoute";
import { ProjectDetailScreen } from "./screens/ProjectDetailScreen";
import { ProjectsScreen } from "./screens/ProjectsScreen";
import { SettingsScreen } from "./screens/SettingsScreen";

export function App() {
  const [route, navigate] = useHashRoute();

  return (
    <AppShell route={route} onNavigate={navigate}>
      {route.screen === "settings" ? <SettingsScreen /> : null}

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
    </AppShell>
  );
}