import { useEffect, useMemo, useState } from "react";
import { createRoot } from "react-dom/client";
import { QueryClient, QueryClientProvider, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  Alert,
  Box,
  Breadcrumbs,
  Button,
  Chip,
  Container,
  Divider,
  IconButton,
  Paper,
  Stack,
  TextField,
  Toolbar,
  Tooltip,
  Typography,
} from "@mui/material";
import ContentCopyIcon from "@mui/icons-material/ContentCopy";
import DeleteOutlineIcon from "@mui/icons-material/DeleteOutline";
import VisibilityIcon from "@mui/icons-material/Visibility";
import VisibilityOffIcon from "@mui/icons-material/VisibilityOff";
import {
  createMember,
  createProject,
  deleteSecret,
  deleteProject,
  disableMember,
  getSession,
  importSecrets,
  listAuditEvents,
  listAdminProjects,
  listMachineIdentities,
  listMembers,
  listSecretEntries,
  listRoles,
  listVersions,
  login,
  logout,
  readSecret,
  restoreSecret,
  undeleteSecret,
  writeSecret,
} from "./api/client";
import type { components } from "./api/generated";

type ProjectResponse = components["schemas"]["ProjectResponse"];
type SecretVersionResponse = components["schemas"]["SecretVersionResponse"];
type SessionResponse = components["schemas"]["SessionResponse"];
type SecretEntry = components["schemas"]["SecretEntry"];

type Values = Record<string, string>;

function Login({ onLogin }: { onLogin: (session: SessionResponse) => void }) {
  const [username, setUsername] = useState("");
  const [password, setPassword] = useState("");
  const [error, setError] = useState("");

  async function submit(event: React.FormEvent) {
    event.preventDefault();
    setError("");
    try {
      onLogin(await login(username, password));
    } catch {
      setError("Sign in failed.");
    }
  }

  return (
    <Container maxWidth="sm">
      <Box component="form" onSubmit={submit} sx={{ mt: 12, display: "grid", gap: 2 }}>
        <Typography variant="h4">OpenBao Secrets</Typography>
        <Typography color="text.secondary">Secure internal developer access</Typography>
        {error && <Alert severity="error">{error}</Alert>}
        <TextField
          label="Username"
          autoComplete="username"
          value={username}
          onChange={(event) => setUsername(event.target.value)}
          required
        />
        <TextField
          label="Password"
          type="password"
          autoComplete="current-password"
          value={password}
          onChange={(event) => setPassword(event.target.value)}
          required
        />
        <Button type="submit" variant="contained">
          Sign in
        </Button>
      </Box>
    </Container>
  );
}

function Dashboard({ session, onLogout }: { session: SessionResponse; onLogout: () => void }) {
  const [project, setProject] = useState("thorneai");
  const [environment, setEnvironment] = useState("development");
  const [path, setPath] = useState("backend");
  const [values, setValues] = useState<Values>({});
  const [bulkValues, setBulkValues] = useState("{}");
  const [description, setDescription] = useState("");
  const [version, setVersion] = useState(0);
  const [versions, setVersions] = useState<SecretVersionResponse[]>([]);
  const [revealed, setRevealed] = useState<Record<string, boolean>>({});
  const [message, setMessage] = useState("");
  const [error, setError] = useState("");
  const queryClient = useQueryClient();

  const projectsQuery = useQuery({
    queryKey: ["admin", "projects"],
    queryFn: listAdminProjects,
    enabled:
      session.policies?.some((policy) => policy === "wrapper-admin" || policy === "root") ?? false,
  });
  const secretQuery = useQuery({
    queryKey: ["secret", project, environment, path],
    queryFn: async () => ({
      document: await readSecret(project, environment, path),
      versions: await listVersions(project, environment, path),
    }),
  });
  const entriesQuery = useQuery<SecretEntry[]>({
    queryKey: ["secret-entries", project, environment, path],
    queryFn: () => listSecretEntries(project, environment, path),
  });

  useEffect(() => {
    if (secretQuery.data) {
      setValues(secretQuery.data.document.values);
      setBulkValues(JSON.stringify(secretQuery.data.document.values, null, 2));
      setVersion(Number(secretQuery.data.document.version));
      setDescription(secretQuery.data.document.description ?? "");
      setVersions(secretQuery.data.versions);
      setRevealed({});
    }
    if (secretQuery.error) {
      setError(
        secretQuery.error instanceof Error ? secretQuery.error.message : "Secret load failed.",
      );
    }
  }, [secretQuery.data, secretQuery.error]);

  const projectOptions = useMemo(
    () => (projectsQuery.data ?? []).map((item: ProjectResponse) => item.id),
    [projectsQuery.data],
  );

  async function load() {
    setError("");
    await queryClient.invalidateQueries({ queryKey: ["secret", project, environment, path] });
  }

  async function save() {
    try {
      const nextValues = JSON.parse(bulkValues) as Values;
      await writeSecret(project, environment, path, nextValues, version, description);
      setValues(nextValues);
      setMessage("Saved.");
      await load();
    } catch (saveError) {
      setError(saveError instanceof Error ? saveError.message : "Save failed.");
    }
  }

  async function remove() {
    if (!window.confirm("Soft-delete this secret collection?")) return;
    await deleteSecret(project, environment, path);
    setValues({});
    setMessage("Deleted.");
  }

  async function restore(versionToRestore: number) {
    await restoreSecret(project, environment, path, versionToRestore);
    await load();
    setMessage(`Restored version ${versionToRestore}.`);
  }

  async function undelete(versionToRestore: number) {
    await undeleteSecret(project, environment, path, versionToRestore);
    await load();
    setMessage(`Undeleted version ${versionToRestore}.`);
  }

  async function importFile(file: File) {
    const text = await file.text();
    const imported = file.name.endsWith(".json")
      ? (JSON.parse(text) as Values)
      : Object.fromEntries(
          text
            .split(/\r?\n/)
            .filter((line) => line && !line.startsWith("#"))
            .map((line) => {
              const index = line.indexOf("=");
              return [line.slice(0, index), line.slice(index + 1).replace(/^"|"$/g, "")];
            }),
        );
    await importSecrets(project, environment, path, imported, version || undefined, description);
    await load();
  }

  async function exportFile() {
    const blob = new Blob(
      [
        Object.entries(values)
          .map(([key, value]) => `${key}=${value}`)
          .join("\n"),
      ],
      { type: "text/plain" },
    );
    const url = URL.createObjectURL(blob);
    const link = document.createElement("a");
    link.href = url;
    link.download = `${project}-${environment}-${path.replaceAll("/", "-")}.env`;
    link.click();
    URL.revokeObjectURL(url);
  }

  function exportJson() {
    const blob = new Blob([JSON.stringify(values, null, 2)], { type: "application/json" });
    const url = URL.createObjectURL(blob);
    const link = document.createElement("a");
    link.href = url;
    link.download = `${project}-${environment}-${path.replaceAll("/", "-")}.json`;
    link.click();
    URL.revokeObjectURL(url);
  }

  return (
    <Container maxWidth="lg">
      <Toolbar disableGutters sx={{ justifyContent: "space-between", py: 2 }}>
        <Stack direction="row" spacing={2} alignItems="center">
          <Typography variant="h5">OpenBao Secrets</Typography>
          <Chip
            size="small"
            label={`Session expires ${new Date(session.expiresAt).toLocaleString()}`}
          />
        </Stack>
        <Button
          onClick={async () => {
            await logout();
            onLogout();
          }}
        >
          Sign out
        </Button>
      </Toolbar>
      <Paper sx={{ p: 3 }}>
        <Stack direction={{ xs: "column", md: "row" }} spacing={2}>
          <TextField
            select
            label="Project"
            value={project}
            onChange={(event) => setProject(event.target.value)}
            SelectProps={{ native: true }}
          >
            {projectOptions.length === 0 && <option value="thorneai">thorneai</option>}
            {projectOptions.map((item) => (
              <option key={item} value={item}>
                {item}
              </option>
            ))}
          </TextField>
          <TextField
            label="Environment"
            value={environment}
            onChange={(event) => setEnvironment(event.target.value)}
          />
          <TextField
            label="Folder / collection"
            value={path}
            onChange={(event) => setPath(event.target.value)}
          />
          <Button variant="contained" onClick={load}>
            Load
          </Button>
          {environment === "production" && <Chip color="error" label="PRODUCTION" />}
        </Stack>
        {error && (
          <Alert severity="error" sx={{ mt: 2 }}>
            {error}
          </Alert>
        )}
        {message && (
          <Alert severity="success" sx={{ mt: 2 }} onClose={() => setMessage("")}>
            {message}
          </Alert>
        )}
        <Divider sx={{ my: 3 }} />
        <Breadcrumbs sx={{ mb: 2 }}>
          <Typography color="text.secondary">{project}</Typography>
          <Typography color={environment === "production" ? "error" : "text.secondary"}>
            {environment}
          </Typography>
          {path.split("/").map((segment) => (
            <Typography key={segment}>{segment}</Typography>
          ))}
        </Breadcrumbs>
        <TextField
          label="Description"
          value={description}
          onChange={(event) => setDescription(event.target.value)}
          fullWidth
          sx={{ mb: 2 }}
        />
        {entriesQuery.data && entriesQuery.data.length > 0 && (
          <Stack direction="row" spacing={1} sx={{ mb: 2, flexWrap: "wrap" }}>
            {entriesQuery.data.map((entry) => (
              <Button
                key={`${entry.name}-${entry.isFolder}`}
                size="small"
                variant="outlined"
                onClick={() => setPath(path ? `${path}/${entry.name}` : entry.name)}
              >
                {entry.name}
                {entry.isFolder ? "/" : ""}
              </Button>
            ))}
          </Stack>
        )}
        <TextField
          label="Bulk JSON editor"
          value={bulkValues}
          onChange={(event) => setBulkValues(event.target.value)}
          multiline
          minRows={5}
          fullWidth
          sx={{ mb: 2 }}
        />
        <Stack spacing={1}>
          {Object.entries(values).map(([key, value]) => (
            <Stack key={key} direction="row" spacing={1} alignItems="center">
              <TextField
                label={key}
                value={revealed[key] ? value : "••••••••"}
                onChange={(event) => {
                  const nextValues = { ...values, [key]: event.target.value };
                  setValues(nextValues);
                  setBulkValues(JSON.stringify(nextValues, null, 2));
                }}
                fullWidth
              />
              <Tooltip title={revealed[key] ? "Mask value" : "Reveal value"}>
                <IconButton onClick={() => setRevealed({ ...revealed, [key]: !revealed[key] })}>
                  {revealed[key] ? <VisibilityOffIcon /> : <VisibilityIcon />}
                </IconButton>
              </Tooltip>
              <Tooltip title="Copy value">
                <IconButton onClick={() => navigator.clipboard.writeText(value)}>
                  <ContentCopyIcon />
                </IconButton>
              </Tooltip>
              <Tooltip title="Delete key">
                <IconButton
                  onClick={() => {
                    const next = { ...values };
                    delete next[key];
                    setValues(next);
                    setBulkValues(JSON.stringify(next, null, 2));
                  }}
                >
                  <DeleteOutlineIcon />
                </IconButton>
              </Tooltip>
            </Stack>
          ))}
        </Stack>
        <Stack direction="row" spacing={1} sx={{ mt: 2 }}>
          <Button
            onClick={() => {
              const nextValues = { ...values, NEW_SECRET: "" };
              setValues(nextValues);
              setBulkValues(JSON.stringify(nextValues, null, 2));
            }}
          >
            Add secret
          </Button>
          <Button variant="contained" onClick={save}>
            Save
          </Button>
          <Button color="error" onClick={remove}>
            Delete collection
          </Button>
          <Button component="label">
            Import
            <input
              hidden
              type="file"
              accept=".env,.json,text/plain,application/json"
              onChange={(event) => event.target.files?.[0] && importFile(event.target.files[0])}
            />
          </Button>
          <Button onClick={exportFile}>Export .env</Button>
          <Button onClick={exportJson}>Export JSON</Button>
        </Stack>
        {versions.length > 0 && (
          <>
            <Typography variant="h6" sx={{ mt: 4 }}>
              Version history
            </Typography>
            {versions.map((item) => (
              <Stack key={item.version} direction="row" spacing={2} alignItems="center">
                <Typography>Version {item.version}</Typography>
                <Typography color="text.secondary">{item.deletedAt ?? "active"}</Typography>
                {item.deletedAt && (
                  <Button onClick={() => undelete(Number(item.version))}>Undelete</Button>
                )}
                {!item.deletedAt && (
                  <Button onClick={() => restore(Number(item.version))}>Restore</Button>
                )}
              </Stack>
            ))}
          </>
        )}
      </Paper>
      {session.policies?.some((policy) => policy === "wrapper-admin" || policy === "root") && (
        <AdminPanel />
      )}
    </Container>
  );
}

function AdminPanel() {
  const queryClient = useQueryClient();
  const [projectId, setProjectId] = useState("");
  const [projectDescription, setProjectDescription] = useState("");
  const [memberUsername, setMemberUsername] = useState("");
  const [memberPassword, setMemberPassword] = useState("");
  const [message, setMessage] = useState("");
  const [error, setError] = useState("");
  const projects = useQuery({ queryKey: ["admin", "projects"], queryFn: listAdminProjects });
  const members = useQuery({ queryKey: ["admin", "members"], queryFn: listMembers });
  const roles = useQuery({ queryKey: ["admin", "roles"], queryFn: listRoles });
  const machines = useQuery({ queryKey: ["admin", "machines"], queryFn: listMachineIdentities });
  const audit = useQuery({ queryKey: ["admin", "audit"], queryFn: listAuditEvents });

  async function run(action: () => Promise<void>, success: string) {
    setError("");
    try {
      await action();
      setMessage(success);
      await queryClient.invalidateQueries({ queryKey: ["admin"] });
    } catch (actionError) {
      setError(actionError instanceof Error ? actionError.message : "Admin operation failed.");
    }
  }

  return (
    <Paper sx={{ p: 3, mt: 3 }}>
      <Typography variant="h6">Administration</Typography>
      {message && <Alert severity="success">{message}</Alert>}
      {error && <Alert severity="error">{error}</Alert>}
      <Stack direction={{ xs: "column", md: "row" }} spacing={1} sx={{ mt: 2 }}>
        <TextField
          label="New project"
          value={projectId}
          onChange={(event) => setProjectId(event.target.value)}
        />
        <TextField
          label="Description"
          value={projectDescription}
          onChange={(event) => setProjectDescription(event.target.value)}
        />
        <Button
          variant="contained"
          onClick={() =>
            run(async () => {
              await createProject(projectId, projectDescription);
              setProjectId("");
              setProjectDescription("");
            }, "Project created.")
          }
        >
          Create project
        </Button>
      </Stack>
      <Typography variant="subtitle1" sx={{ mt: 2 }}>
        Projects
      </Typography>
      {(projects.data ?? []).map((project) => (
        <Stack key={project.id} direction="row" spacing={1} alignItems="center">
          <Typography sx={{ flex: 1 }}>
            {project.id} — {project.description || "no description"}
          </Typography>
          <Button
            color="error"
            onClick={() =>
              window.confirm(`Delete ${project.id}?`) &&
              run(() => deleteProject(project.id), "Project deleted.")
            }
          >
            Delete
          </Button>
        </Stack>
      ))}
      <Stack direction={{ xs: "column", md: "row" }} spacing={1} sx={{ mt: 2 }}>
        <TextField
          label="New member"
          value={memberUsername}
          onChange={(event) => setMemberUsername(event.target.value)}
        />
        <TextField
          label="Temporary password"
          type="password"
          value={memberPassword}
          onChange={(event) => setMemberPassword(event.target.value)}
        />
        <Button
          onClick={() =>
            run(async () => {
              await createMember(memberUsername, memberPassword, ["default"]);
              setMemberUsername("");
              setMemberPassword("");
            }, "Member created.")
          }
        >
          Create member
        </Button>
      </Stack>
      <Typography variant="subtitle1" sx={{ mt: 2 }}>
        Members
      </Typography>
      {(members.data ?? []).map((member) => (
        <Stack key={member.username} direction="row" spacing={1} alignItems="center">
          <Typography sx={{ flex: 1 }}>
            {member.username} — {member.disabled ? "disabled" : member.policies.join(", ")}
          </Typography>
          {!member.disabled && (
            <Button onClick={() => run(() => disableMember(member.username), "Member disabled.")}>
              Disable
            </Button>
          )}
        </Stack>
      ))}
      <Typography variant="subtitle1" sx={{ mt: 2 }}>
        Roles
      </Typography>
      <Typography color="text.secondary">
        {(roles.data ?? []).map((role) => role.name).join(", ") || "No roles available"}
      </Typography>
      <Typography variant="subtitle1" sx={{ mt: 2 }}>
        Machine identities
      </Typography>
      <Typography color="text.secondary">
        {(machines.data ?? []).map((machine) => machine.name).join(", ") || "No machine identities"}
      </Typography>
      <Typography variant="subtitle1" sx={{ mt: 2 }}>
        Recent audit events
      </Typography>
      <Typography color="text.secondary">
        {audit.data?.length ?? 0} projected events loaded; secret values are never displayed.
      </Typography>
    </Paper>
  );
}

function App() {
  const [session, setSession] = useState<SessionResponse | null>(null);
  const queryClient = useQueryClient();
  const sessionQuery = useQuery({ queryKey: ["session"], queryFn: getSession, staleTime: 30_000 });
  useEffect(() => {
    if (sessionQuery.data) setSession(sessionQuery.data);
  }, [sessionQuery.data]);
  if (sessionQuery.isLoading) return null;
  return session ? (
    <Dashboard
      session={session}
      onLogout={() => {
        queryClient.setQueryData(["session"], null);
        setSession(null);
      }}
    />
  ) : (
    <Login onLogin={setSession} />
  );
}

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      staleTime: 30_000,
      retry: 1,
    },
  },
});

createRoot(document.getElementById("root")!).render(
  <QueryClientProvider client={queryClient}>
    <App />
  </QueryClientProvider>,
);
