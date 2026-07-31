import { useEffect, useState } from "react";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import {
  Alert,
  Breadcrumbs,
  Button,
  Chip,
  Divider,
  IconButton,
  Paper,
  Stack,
  TextField,
  Tooltip,
  Typography,
} from "@mui/material";
import ContentCopyIcon from "@mui/icons-material/ContentCopy";
import DeleteOutlineIcon from "@mui/icons-material/DeleteOutline";
import VisibilityIcon from "@mui/icons-material/Visibility";
import VisibilityOffIcon from "@mui/icons-material/VisibilityOff";
import { useNavigate, useParams } from "react-router-dom";
import type { components } from "../api/generated";
import {
  deleteSecret,
  importSecrets,
  listSecretEntries,
  listVersions,
  readSecret,
  restoreSecret,
  undeleteSecret,
  writeSecret,
} from "../api/client";

type SecretEntry = components["schemas"]["SecretEntry"];
type SecretVersionResponse = components["schemas"]["SecretVersionResponse"];
type Values = Record<string, string>;

export function SecretsPage() {
  const {
    project = "thorneai",
    environment = "development",
    "*": routePath = "backend",
  } = useParams();
  const path = routePath.split("/").map(decodeURIComponent).filter(Boolean).join("/") || "backend";
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const [openPath, setOpenPath] = useState(path);
  const [values, setValues] = useState<Values>({});
  const [bulkValues, setBulkValues] = useState("{}");
  const [description, setDescription] = useState("");
  const [version, setVersion] = useState(0);
  const [revealed, setRevealed] = useState<Record<string, boolean>>({});
  const [bulkRevealed, setBulkRevealed] = useState(false);
  const [message, setMessage] = useState("");
  const [error, setError] = useState("");

  const secret = useQuery({
    queryKey: ["secret", project, environment, path],
    queryFn: async () => ({
      document: await readSecret(project, environment, path),
      versions: await listVersions(project, environment, path),
    }),
  });
  const entries = useQuery<SecretEntry[]>({
    queryKey: ["secret-entries", project, environment, path],
    queryFn: () => listSecretEntries(project, environment, path),
  });

  useEffect(() => {
    setOpenPath(path);
  }, [path]);

  useEffect(() => {
    if (!secret.data) return;
    setValues(secret.data.document.values);
    setBulkValues(JSON.stringify(secret.data.document.values, null, 2));
    setVersion(Number(secret.data.document.version));
    setDescription(secret.data.document.description ?? "");
    setRevealed({});
    setBulkRevealed(false);
  }, [secret.data]);

  useEffect(() => {
    if (secret.error) {
      setError(secret.error instanceof Error ? secret.error.message : "Secret load failed.");
    }
  }, [secret.error]);

  function goToPath(nextPath: string) {
    const encodedPath = nextPath.split("/").filter(Boolean).map(encodeURIComponent).join("/");
    navigate(
      `/projects/${encodeURIComponent(project)}/environments/${encodeURIComponent(environment)}/secrets/${encodedPath}`,
    );
  }

  async function refresh() {
    setError("");
    await queryClient.invalidateQueries({ queryKey: ["secret", project, environment, path] });
    await queryClient.invalidateQueries({
      queryKey: ["secret-entries", project, environment, path],
    });
  }

  async function save() {
    try {
      const nextValues = JSON.parse(bulkValues) as Values;
      await writeSecret(project, environment, path, nextValues, version, description);
      setValues(nextValues);
      setMessage("Saved.");
      await refresh();
    } catch (saveError) {
      setError(saveError instanceof Error ? saveError.message : "Save failed.");
    }
  }

  async function remove() {
    if (!window.confirm("Soft-delete this secret collection?")) return;
    try {
      await deleteSecret(project, environment, path);
      setValues({});
      setMessage("Deleted.");
      await refresh();
    } catch (deleteError) {
      setError(deleteError instanceof Error ? deleteError.message : "Delete failed.");
    }
  }

  async function importFile(file: File) {
    try {
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
      setMessage("Imported.");
      await refresh();
    } catch (importError) {
      setError(importError instanceof Error ? importError.message : "Import failed.");
    }
  }

  function download(format: "env" | "json") {
    const contents =
      format === "json"
        ? JSON.stringify(values, null, 2)
        : Object.entries(values)
            .map(([key, value]) => `${key}=${value}`)
            .join("\n");
    const blob = new Blob([contents], {
      type: format === "json" ? "application/json" : "text/plain",
    });
    const url = URL.createObjectURL(blob);
    const link = document.createElement("a");
    link.href = url;
    link.download = `${project}-${environment}-${path.replaceAll("/", "-")}.${format}`;
    link.click();
    URL.revokeObjectURL(url);
  }

  const versions = secret.data?.versions ?? [];
  return (
    <Paper sx={{ p: 3 }}>
      <Stack direction={{ xs: "column", md: "row" }} spacing={2} alignItems={{ md: "center" }}>
        <TextField
          label="Collection path"
          value={openPath}
          onChange={(event) => setOpenPath(event.target.value)}
        />
        <Button variant="contained" onClick={() => goToPath(openPath)}>
          Open
        </Button>
        <Button onClick={refresh}>Refresh</Button>
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
      {entries.data && entries.data.length > 0 && (
        <Stack direction="row" spacing={1} sx={{ mb: 2, flexWrap: "wrap" }}>
          {entries.data.map((entry) => (
            <Button
              key={`${entry.name}-${entry.isFolder}`}
              size="small"
              variant="outlined"
              onClick={() => goToPath(`${path}/${entry.name}`)}
            >
              {entry.name}
              {entry.isFolder ? "/" : ""}
            </Button>
          ))}
        </Stack>
      )}
      <TextField
        label="Description"
        value={description}
        onChange={(event) => setDescription(event.target.value)}
        fullWidth
        sx={{ mb: 2 }}
      />
      <Stack direction="row" spacing={1} alignItems="center" sx={{ mb: 1 }}>
        <Typography variant="subtitle1">Bulk JSON editor</Typography>
        <Button size="small" onClick={() => setBulkRevealed((current) => !current)}>
          {bulkRevealed ? "Mask values" : "Reveal values"}
        </Button>
      </Stack>
      <TextField
        aria-label="Bulk JSON editor"
        value={
          bulkRevealed
            ? bulkValues
            : JSON.stringify(
                Object.fromEntries(Object.keys(values).map((key) => [key, "••••••••"])),
                null,
                2,
              )
        }
        onChange={(event) => setBulkValues(event.target.value)}
        multiline
        minRows={5}
        fullWidth
        disabled={!bulkRevealed}
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
                  const nextValues = { ...values };
                  delete nextValues[key];
                  setValues(nextValues);
                  setBulkValues(JSON.stringify(nextValues, null, 2));
                }}
              >
                <DeleteOutlineIcon />
              </IconButton>
            </Tooltip>
          </Stack>
        ))}
      </Stack>
      <Stack direction="row" spacing={1} sx={{ mt: 2, flexWrap: "wrap" }}>
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
        <Button onClick={() => download("env")}>Export .env</Button>
        <Button onClick={() => download("json")}>Export JSON</Button>
      </Stack>
      <VersionHistory
        project={project}
        environment={environment}
        path={path}
        versions={versions}
        refresh={refresh}
        setError={setError}
        setMessage={setMessage}
      />
    </Paper>
  );
}

function VersionHistory({
  project,
  environment,
  path,
  versions,
  refresh,
  setError,
  setMessage,
}: {
  project: string;
  environment: string;
  path: string;
  versions: SecretVersionResponse[];
  refresh: () => Promise<void>;
  setError: (value: string) => void;
  setMessage: (value: string) => void;
}) {
  if (versions.length === 0) return null;
  return (
    <>
      <Typography variant="h6" sx={{ mt: 4 }}>
        Version history
      </Typography>
      {versions.map((item) => (
        <Stack key={item.version} direction="row" spacing={2} alignItems="center">
          <Typography>Version {item.version}</Typography>
          <Typography color="text.secondary">{item.deletedAt ?? "active"}</Typography>
          {item.deletedAt ? (
            <Button
              onClick={async () => {
                try {
                  await undeleteSecret(project, environment, path, Number(item.version));
                  await refresh();
                  setMessage(`Undeleted version ${item.version}.`);
                } catch (error) {
                  setError(error instanceof Error ? error.message : "Undelete failed.");
                }
              }}
            >
              Undelete
            </Button>
          ) : (
            <Button
              onClick={async () => {
                try {
                  await restoreSecret(project, environment, path, Number(item.version));
                  await refresh();
                  setMessage(`Restored version ${item.version}.`);
                } catch (error) {
                  setError(error instanceof Error ? error.message : "Restore failed.");
                }
              }}
            >
              Restore
            </Button>
          )}
        </Stack>
      ))}
    </>
  );
}
