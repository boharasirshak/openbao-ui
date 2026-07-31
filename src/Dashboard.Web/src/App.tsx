import { useQuery, useQueryClient } from "@tanstack/react-query";
import { Alert, Box, Button, Chip, Container, Stack, Toolbar, Typography } from "@mui/material";
import {
  Link as RouterLink,
  Navigate,
  Outlet,
  Route,
  Routes,
  useNavigate,
  useOutletContext,
} from "react-router-dom";
import type { components } from "./api/generated";
import { getSession, logout } from "./api/client";
import { AdminPage, ProjectsPage } from "./pages/AdminPage";
import { LoginPage } from "./pages/LoginPage";
import { SecretsPage } from "./pages/SecretsPage";

export type SessionResponse = components["schemas"]["SessionResponse"];

export function App() {
  return (
    <Routes>
      <Route path="/login" element={<LoginPage />} />
      <Route element={<RequireSession />}>
        <Route element={<AppShell />}>
          <Route
            index
            element={
              <Navigate replace to="/projects/thorneai/environments/development/secrets/backend" />
            }
          />
          <Route path="/projects" element={<ProjectsPage />} />
          <Route
            path="/projects/:project/environments/:environment/secrets/*"
            element={<SecretsPage />}
          />
          <Route path="/admin/:section" element={<AdminPage />} />
          <Route path="/audit" element={<AdminPage section="audit" />} />
        </Route>
      </Route>
      <Route path="*" element={<Navigate replace to="/" />} />
    </Routes>
  );
}

export function useSession() {
  return useOutletContext<SessionResponse>();
}

export function isAdmin(session: SessionResponse) {
  return (
    session.policies?.some((policy) => policy === "wrapper-admin" || policy === "root") ?? false
  );
}

function RequireSession() {
  const session = useQuery({ queryKey: ["session"], queryFn: getSession, staleTime: 30_000 });

  if (session.isLoading) {
    return <Box sx={{ p: 4 }}>Restoring secure session…</Box>;
  }

  return session.data ? <Outlet context={session.data} /> : <Navigate replace to="/login" />;
}

function AppShell() {
  const session = useSession();
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const admin = isAdmin(session);

  async function signOut() {
    await logout();
    queryClient.clear();
    navigate("/login", { replace: true });
  }

  return (
    <Container maxWidth="xl">
      <Toolbar disableGutters sx={{ justifyContent: "space-between", py: 2 }}>
        <Stack direction="row" spacing={2} alignItems="center">
          <Typography
            component={RouterLink}
            to="/projects"
            variant="h5"
            sx={{ color: "inherit", textDecoration: "none" }}
          >
            OpenBao Secrets
          </Typography>
          <Chip
            size="small"
            label={`Session expires ${new Date(session.expiresAt).toLocaleString()}`}
          />
        </Stack>
        <Button onClick={signOut}>Sign out</Button>
      </Toolbar>
      <Stack direction="row" spacing={1} sx={{ mb: 3, flexWrap: "wrap" }}>
        <Button component={RouterLink} to="/projects">
          Projects
        </Button>
        {admin && (
          <Button component={RouterLink} to="/admin/projects">
            Admin
          </Button>
        )}
        {admin && (
          <Button component={RouterLink} to="/audit">
            Audit
          </Button>
        )}
      </Stack>
      {!admin && (
        <Alert severity="info" sx={{ mb: 3 }}>
          Open a project URL you are authorized to access. OpenBao remains the authorization
          authority.
        </Alert>
      )}
      <Outlet context={session} />
    </Container>
  );
}
