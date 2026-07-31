import { useEffect, useMemo, useState } from "react";
import { createRoot } from "react-dom/client";
import {
  Alert,
  Box,
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
  deleteSecret,
  importSecrets,
  listAdminProjects,
  listVersions,
  login,
  logout,
  readSecret,
  restoreSecret,
  writeSecret,
} from "./api/client";
import type { ProjectResponse, SecretVersionResponse } from "./api/generated";

type Values = Record<string, string>;

function Login({ onLogin }: { onLogin: () => void }) {
  const [username, setUsername] = useState("");
  const [password, setPassword] = useState("");
  const [error, setError] = useState("");

  async function submit(event: React.FormEvent) {
    event.preventDefault();
    setError("");
    try {
      await login(username, password);
      onLogin();
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

function Dashboard({ onLogout }: { onLogout: () => void }) {
  const [project, setProject] = useState("thorneai");
  const [environment, setEnvironment] = useState("development");
  const [path, setPath] = useState("backend");
  const [values, setValues] = useState<Values>({});
  const [version, setVersion] = useState(0);
  const [versions, setVersions] = useState<SecretVersionResponse[]>([]);
  const [projects, setProjects] = useState<ProjectResponse[]>([]);
  const [revealed, setRevealed] = useState<Record<string, boolean>>({});
  const [message, setMessage] = useState("");
  const [error, setError] = useState("");

  useEffect(() => {
    listAdminProjects()
      .then(setProjects)
      .catch(() => setProjects([]));
  }, []);

  const projectOptions = useMemo(() => projects.map((item) => item.id), [projects]);

  async function load() {
    setError("");
    try {
      const document = await readSecret(project, environment, path);
      setValues(document.values);
      setVersion(document.version);
      setVersions(await listVersions(project, environment, path));
      setRevealed({});
    } catch (loadError) {
      setError(loadError instanceof Error ? loadError.message : "Secret load failed.");
    }
  }

  async function save() {
    try {
      await writeSecret(project, environment, path, values, version);
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

  async function importFile(file: File) {
    const text = await file.text();
    const imported = Object.fromEntries(
      text
        .split(/\r?\n/)
        .filter((line) => line && !line.startsWith("#"))
        .map((line) => {
          const index = line.indexOf("=");
          return [line.slice(0, index), line.slice(index + 1).replace(/^"|"$/g, "")];
        }),
    );
    await importSecrets(project, environment, path, imported, version || undefined);
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

  return (
    <Container maxWidth="lg">
      <Toolbar disableGutters sx={{ justifyContent: "space-between", py: 2 }}>
        <Typography variant="h5">OpenBao Secrets</Typography>
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
        <Stack spacing={1}>
          {Object.entries(values).map(([key, value]) => (
            <Stack key={key} direction="row" spacing={1} alignItems="center">
              <TextField
                label={key}
                value={revealed[key] ? value : "••••••••"}
                onChange={(event) => setValues({ ...values, [key]: event.target.value })}
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
                  }}
                >
                  <DeleteOutlineIcon />
                </IconButton>
              </Tooltip>
            </Stack>
          ))}
        </Stack>
        <Stack direction="row" spacing={1} sx={{ mt: 2 }}>
          <Button onClick={() => setValues({ ...values, NEW_SECRET: "" })}>Add secret</Button>
          <Button variant="contained" onClick={save}>
            Save
          </Button>
          <Button color="error" onClick={remove}>
            Delete collection
          </Button>
          <Button component="label">
            Import .env
            <input
              hidden
              type="file"
              accept=".env,text/plain"
              onChange={(event) => event.target.files?.[0] && importFile(event.target.files[0])}
            />
          </Button>
          <Button onClick={exportFile}>Export .env</Button>
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
                <Button onClick={() => restore(item.version)}>Restore</Button>
              </Stack>
            ))}
          </>
        )}
      </Paper>
    </Container>
  );
}

function App() {
  const [authenticated, setAuthenticated] = useState(false);
  return authenticated ? (
    <Dashboard onLogout={() => setAuthenticated(false)} />
  ) : (
    <Login onLogin={() => setAuthenticated(true)} />
  );
}

createRoot(document.getElementById("root")!).render(<App />);
