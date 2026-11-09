import { AppShell } from "./layout/AppShell";
import { useHashRoute } from "./routing/useHashRoute";
import { ProjectsScreen } from "./screens/ProjectsScreen";
import { SettingsScreen } from "./screens/SettingsScreen";

export function App() {
  const [route, navigate] = useHashRoute();

  return (
    <AppShell route={route} onNavigate={navigate}>
      {route === "settings" ? <SettingsScreen /> : <ProjectsScreen />}
    </AppShell>
  );
}
